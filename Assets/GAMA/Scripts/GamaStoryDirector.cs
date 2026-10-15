using System;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.UI;

// The 90-second cinematic "AIKN Court". Everything is a pure function of time t, so the story can be
// played, skipped, or scrubbed from the editor (GAMA Court > Render Story Frames).
public class GamaStoryDirector : MonoBehaviour
{
    public const float Duration = 90f;

    [Header("Cast")]
    public GamaActor kalisa;      // red #7 (suit model in red sportswear)
    public GamaActor mugisha;     // black #23 (suit model styled after the Sketchfab players)
    public GamaActor coachGatete; // red team coach
    public GamaActor coachNshuti; // black team coach
    public GamaActor investorA;
    public GamaActor investorB;
    public GamaActor woman;       // Ms. Uwase, ALU
    public Transform ball;

    [Header("Places")]
    public Transform seatInvestorA, seatWoman, seatInvestorB;
    public Transform chairGatete, chairNshuti;

    [Header("Systems")]
    public Camera storyCam;
    public Camera[] otherCams;
    public GamaDayNight dayNight;
    public GamaScoreboard scoreboard;
    public GamaInteractive interactive;
    public AudioSource narration;
    [Tooltip("Optional: one clip per spoken line (L01..L24 in Assets/GAMA/Audio/Lines). Each plays at its line's time.")]
    public AudioClip[] lineClips;

    [Header("Music and sound effects (all optional; see STORY_ElevenLabs.md)")]
    [Tooltip("Timed music / percussion layers: each plays from 'offset' between t0 and t1 with fades, ducked under dialogue.")]
    public AudioLayer[] layers;
    public AudioSource dribbleLoop, crowdBed, sfx, freeplayMusic;
    public AudioClip sfxSwish, sfxCheer, sfxWhistle, sfxBuzzer, sfxLightsOn, sfxSignOn;
    [Range(0, 1)] public float duckAmount = 0.4f;

    [Serializable]
    public class AudioLayer
    {
        public string name;
        public AudioSource src;
        public float t0, t1, offset, volume = 0.4f, fadeIn = 1f, fadeOut = 1f;
        public bool duck = true;
        [NonSerialized] public bool started;
    }

    // Story moments the sound and lights are cued to (seconds)
    public const float LampsOn1 = 52.4f, LampsOn2 = 52.8f, LampsOn3 = 53.2f, SignOn = 54.2f;
    public Volume volume;

    [Header("UI")]
    public RectTransform barTop, barBottom;
    public Image fade;
    public TextMeshProUGUI subtitle, title, hint;

    [Header("Animation names")]
    public string shootState = "Standing Yell";
    public string defendState = "Dribble";
    public string walkState = "Walk In Circle";

    public bool playOnStart = true;

    public float Time01 => t / Duration;
    public bool Playing => playing;

    float t;
    bool playing;
    DepthOfField dof;

    // ---------- Timeline data ----------

    class Seg
    {
        public float t0;
        public string state;
        public Vector3[] path;
        public float t1;
        public Transform seat;
        public Vector3 seatOffset;
        public GamaActor faceActor;
        public Vector3? face;
        public GamaActor guard;
    }

    enum BallKind { Dribble, Shot, Rest, Pass }

    class BallSeg
    {
        public float t0;
        public BallKind kind;
        public GamaActor actor;
        public bool made;
        public Vector3 shooterPos, rest;
    }

    class Line { public float t0, t1; public string who, text; }

    class Shot { public float t0, t1; public Func<float, Pose3> eval; }

    struct Pose3 { public Vector3 pos, look; public float fov; }

    Dictionary<GamaActor, List<Seg>> segs;
    List<BallSeg> ballSegs;
    List<(float t, int team, int pts)> scores;
    List<Line> lines;
    List<Shot> shots;

    static readonly Vector3 Rim = GamaBall.FarRim;
    static readonly Vector3 SitOffset = new Vector3(0, 0.1f, 0.04f);       // courtside chairs
    public static readonly Vector3 StandSeatOffset = new Vector3(0, 0.1f, -0.06f); // stadium seats: sit back so feet stay on the tier
    static readonly Vector3 StandOffset = new Vector3(0, 0, 0.75f);

    // ---------- Playback ----------

    void Start()
    {
        BuildTimeline();
        if (playOnStart) BeginStory();
        else if (interactive != null) EndStory();
    }

    public void BeginStory()
    {
        BuildTimeline();
        if (interactive != null) interactive.enabled = false;
        foreach (var c in otherCams) if (c != null) c.gameObject.SetActive(false);
        storyCam.gameObject.SetActive(true);
        if (volume != null && volume.profile.TryGet(out dof)) dof.active = true;
        t = 0f;
        playing = true;
        SetStoryUI(true);
        if (narration != null && narration.clip != null) { narration.time = 0; narration.Play(); }
        StartAudio();
        Evaluate(0f, false);
    }

    public void EndStory()
    {
        playing = false;
        if (narration != null) narration.Stop();
        StopAudio();
        SetStoryUI(false);
        if (dof != null) dof.active = false;
        dayNight?.Apply(1f, Time.time); // free play starts under the lights; press N for day
        if (interactive != null) interactive.Begin(this);
    }

    void Update()
    {
        if (!playing) return;
        var k = Keyboard.current;
        if (k != null && (k.enterKey.wasPressedThisFrame || k.escapeKey.wasPressedThisFrame)) { EndStory(); return; }
        float prev = t;
        t += Time.deltaTime;
        if (t >= Duration) { EndStory(); return; }
        PlayLineClips(prev, t);
        Evaluate(t, false);
        AudioTick(prev, t);
    }

    void PlayLineClips(float from, float to)
    {
        if (narration == null || lineClips == null) return;
        for (int i = 0; i < lines.Count && i < lineClips.Length; i++)
            if (lineClips[i] != null && lines[i].t0 > from && lines[i].t0 <= to)
                narration.PlayOneShot(lineClips[i]);
    }

    // ---------- Audio ----------

    void StartAudio()
    {
        if (freeplayMusic != null) freeplayMusic.Stop();
        if (layers != null) foreach (var l in layers) { l.started = false; if (l.src != null) { l.src.Stop(); l.src.volume = 0; } }
        foreach (var s in new[] { dribbleLoop, crowdBed })
            if (s != null && s.clip != null) { s.loop = true; s.volume = 0; s.time = 0; s.Play(); }
    }

    void OneShot(AudioClip c, float v) { if (sfx != null && c != null) sfx.PlayOneShot(c, v); }

    void StopAudio()
    {
        if (layers != null) foreach (var l in layers) if (l.src != null) l.src.Stop();
        foreach (var s in new[] { dribbleLoop, crowdBed }) if (s != null) s.Stop();
        if (freeplayMusic != null && freeplayMusic.clip != null) { freeplayMusic.loop = true; freeplayMusic.volume = 0.6f; freeplayMusic.Play(); }
    }

    void AudioTick(float from, float to)
    {
        bool speaking = false;
        foreach (var l in lines) if (to >= l.t0 - 0.15f && to < l.t1 + 0.15f) speaking = true;
        float k = 1f - Mathf.Exp(-6f * Time.deltaTime);
        float duck = speaking ? duckAmount : 1f;

        // Music and percussion layers
        if (layers != null)
            foreach (var l in layers)
            {
                if (l.src == null || l.src.clip == null) continue;
                bool inside = to >= l.t0 && to < l.t1;
                if (inside && !l.started) { l.started = true; l.src.time = Mathf.Min(l.offset, l.src.clip.length - 0.05f); l.src.volume = 0; l.src.Play(); }
                if (!inside) { if (l.started && l.src.isPlaying && to >= l.t1) l.src.Stop(); continue; }
                float env = Mathf.Clamp01((to - l.t0) / Mathf.Max(0.01f, l.fadeIn)) * Mathf.Clamp01((l.t1 - to) / Mathf.Max(0.01f, l.fadeOut));
                l.src.volume = Mathf.Lerp(l.src.volume, l.volume * env * (l.duck ? duck : 1f), k);
            }

        // Dribbling while a player has the ball in play, crowd bed louder during the games
        if (dribbleLoop != null)
        {
            int idx = 0;
            for (int i = 0; i < ballSegs.Count; i++) if (to >= ballSegs[i].t0) idx = i;
            bool dribbling = ballSegs[idx].kind == BallKind.Dribble && to > 30f && to < 80f;
            dribbleLoop.volume = Mathf.Lerp(dribbleLoop.volume, dribbling ? 0.55f : 0f, k);
        }
        if (crowdBed != null)
        {
            bool game = (to > 30f && to < 49f) || (to > 56f && to < 82f);
            float end = Mathf.Clamp01((Duration - to) / 3f);
            crowdBed.volume = Mathf.Lerp(crowdBed.volume, (game ? 0.16f : 0.05f) * end, k * 0.5f);
        }

        // One-shot cues
        bool Cross(float at) => at > from && at <= to;
        foreach (var s in scores)
        {
            if (Cross(s.t - 0.05f)) OneShot(sfxSwish, 1f);
            if (Cross(s.t + 0.2f)) OneShot(sfxCheer, 0.8f);
        }
        // No referee whistles: they sounded like a stray transition effect.
        if (Cross(LampsOn1)) OneShot(sfxLightsOn, 0.7f);
        if (Cross(LampsOn2)) OneShot(sfxLightsOn, 0.8f);
        if (Cross(LampsOn3)) OneShot(sfxLightsOn, 0.95f);
        if (Cross(SignOn)) OneShot(sfxSignOn, 0.9f);
        if (Cross(79.5f)) OneShot(sfxBuzzer, 0.6f);
    }

    void SetStoryUI(bool on)
    {
        if (barTop) barTop.gameObject.SetActive(on);
        if (barBottom) barBottom.gameObject.SetActive(on);
        if (subtitle) subtitle.gameObject.SetActive(on);
        if (title) title.gameObject.SetActive(on);
        if (fade) fade.gameObject.SetActive(on);
        if (hint) hint.gameObject.SetActive(on);
    }

    // Editor helper: pose the whole scene at time t (used to render story frames without Play mode).
    public void EvaluateAt(float time)
    {
        BuildTimeline();
        foreach (var a in segs.Keys) a.Init();
        Evaluate(time, true);
    }

    void Evaluate(float time, bool seek)
    {
        // Actors (guards last, because they follow the player they mark)
        foreach (var kv in segs) if (Find(kv.Value, time).guard == null) EvalActor(kv.Key, kv.Value, time, seek);
        foreach (var kv in segs) if (Find(kv.Value, time).guard != null) EvalActor(kv.Key, kv.Value, time, seek);

        if (ball != null) ball.position = EvalBall(time);

        dayNight?.Apply(Mathf.SmoothStep(0, 1, Mathf.InverseLerp(48.5f, 55.5f, time)), time);

        // Scoreboard
        int red = 0, black = 0;
        foreach (var s in scores) if (time >= s.t) { if (s.team == 1) red += s.pts; else black += s.pts; }
        if (scoreboard != null)
        {
            if (time < 52f) scoreboard.Set(red, black, "Q2", 300f - time * 3f);
            else if (time < 80f) scoreboard.Set(red, black, "Q4", 120f - Mathf.Max(0, time - 56f) * 5f);
            else scoreboard.Set(red, black, "Q4", 0, true);
        }

        EvalCamera(time);
        EvalUI(time);
    }

    // ---------- Actors ----------

    static Seg Find(List<Seg> list, float time)
    {
        var s = list[0];
        foreach (var x in list) if (time >= x.t0) s = x;
        return s;
    }

    void EvalActor(GamaActor a, List<Seg> list, float time, bool seek)
    {
        var s = Find(list, time);
        float u = Mathf.Clamp01((time - s.t0) / Mathf.Max(0.01f, s.t1 - s.t0));
        Vector3 pos;
        Vector3 fwd;
        if (s.seat != null)
        {
            pos = s.seat.position + s.seat.rotation * s.seatOffset;
            fwd = s.seat.forward;
        }
        else if (s.guard != null)
        {
            var tp = s.guard.transform.position;
            var toRim = Flat(Rim - tp).normalized;
            pos = tp + toRim * 1.15f + Vector3.Cross(Vector3.up, toRim) * 0.15f;
            fwd = tp - pos;
        }
        else
        {
            float e = u * u * (3f - 2f * u);
            pos = PathAt(s.path, s.path.Length > 1 ? Mathf.Lerp(u, e, 0.5f) : 0f, out fwd);
        }
        if (s.faceActor != null) fwd = s.faceActor.transform.position - pos;
        else if (s.face.HasValue) fwd = s.face.Value - pos;
        fwd = Flat(fwd);
        a.transform.SetPositionAndRotation(pos, fwd.sqrMagnitude > 1e-4f ? Quaternion.LookRotation(fwd) : a.transform.rotation);
        if (seek) a.SeekState(s.state, time - s.t0);
        else a.SetState(s.state);
    }

    static Vector3 PathAt(Vector3[] p, float u, out Vector3 dir)
    {
        dir = Vector3.zero;
        if (p.Length == 1) return p[0];
        float total = 0;
        for (int i = 1; i < p.Length; i++) total += Vector3.Distance(p[i - 1], p[i]);
        float d = u * total;
        for (int i = 1; i < p.Length; i++)
        {
            float len = Vector3.Distance(p[i - 1], p[i]);
            dir = p[i] - p[i - 1];
            if (d <= len || i == p.Length - 1) return Vector3.Lerp(p[i - 1], p[i], len > 0 ? Mathf.Clamp01(d / len) : 1);
            d -= len;
        }
        return p[p.Length - 1];
    }

    static Vector3 Flat(Vector3 v) { v.y = 0; return v; }

    // ---------- Ball ----------

    Vector3 EvalBall(float time)
    {
        int idx = 0;
        for (int i = 0; i < ballSegs.Count; i++) if (time >= ballSegs[i].t0) idx = i;
        return EvalBallSeg(idx, time);
    }

    Vector3 EvalBallSeg(int idx, float time)
    {
        var b = ballSegs[idx];
        float s = time - b.t0;
        switch (b.kind)
        {
            case BallKind.Dribble:
                return GamaBall.Dribble(b.actor, time);
            case BallKind.Shot:
            {
                var from = ReleaseFrom(b.shooterPos);
                var target = GamaBall.ShotTarget(Rim, b.made, b.shooterPos);
                if (s < GamaBall.FlightTime) return GamaBall.Arc(from, target, s / GamaBall.FlightTime);
                return GamaBall.AfterRim(target, b.made, b.shooterPos, s - GamaBall.FlightTime);
            }
            case BallKind.Pass:
            {
                float dur = idx + 1 < ballSegs.Count ? ballSegs[idx + 1].t0 - b.t0 : 0.5f;
                var from = idx > 0 ? EvalBallSeg(idx - 1, b.t0) : b.rest;
                return GamaBall.Arc(from, GamaBall.Dribble(b.actor, time) + Vector3.up * 0.3f, Mathf.Clamp01(s / dur), 0.5f);
            }
            default:
                return b.rest;
        }
    }

    static Vector3 ReleaseFrom(Vector3 shooterPos)
    {
        var dir = Flat(Rim - shooterPos).normalized;
        return shooterPos + Vector3.up * 2.35f + dir * 0.25f;
    }

    // ---------- Camera ----------

    void EvalCamera(float time)
    {
        if (storyCam == null) return;
        Shot shot = shots[0];
        foreach (var s in shots) if (time >= s.t0) shot = s;
        var p = shot.eval(Mathf.Clamp01((time - shot.t0) / (shot.t1 - shot.t0)));
        // A touch of handheld drift
        var n = new Vector3(Mathf.PerlinNoise(time * 0.35f, 1.3f) - 0.5f, Mathf.PerlinNoise(2.7f, time * 0.35f) - 0.5f, 0) * 0.12f;
        storyCam.transform.position = p.pos;
        storyCam.transform.rotation = Quaternion.LookRotation((p.look - p.pos).normalized + storyCam.transform.TransformDirection(n) * 0.05f);
        storyCam.fieldOfView = p.fov;
        if (dof != null) dof.focusDistance.value = Vector3.Distance(p.pos, p.look);
    }

    static float Ease(float u) => u * u * (3f - 2f * u);

    Shot Dolly(float t0, float t1, Vector3 p0, Vector3 p1, Vector3 l0, Vector3 l1, float fov0 = 45f, float fov1 = -1f)
    {
        if (fov1 < 0) fov1 = fov0;
        return new Shot { t0 = t0, t1 = t1, eval = u => { float e = Ease(u); return new Pose3 { pos = Vector3.Lerp(p0, p1, e), look = Vector3.Lerp(l0, l1, e), fov = Mathf.Lerp(fov0, fov1, e) }; } };
    }

    Shot DollyLookBall(float t0, float t1, Vector3 p0, Vector3 p1, float fov = 50f)
    {
        return new Shot { t0 = t0, t1 = t1, eval = u => new Pose3 { pos = Vector3.Lerp(p0, p1, Ease(u)), look = Vector3.Lerp(ball.position, Rim, 0.35f), fov = fov } };
    }

    Shot Track(float t0, float t1, GamaActor a, Vector3 off0, Vector3 off1, float lookUp = 1.3f, float fov = 45f)
    {
        return new Shot { t0 = t0, t1 = t1, eval = u => { var c = a.transform.position; return new Pose3 { pos = c + Vector3.Lerp(off0, off1, Ease(u)), look = c + Vector3.up * lookUp, fov = fov }; } };
    }

    // ---------- UI ----------

    void EvalUI(float time)
    {
        if (subtitle != null)
        {
            string text = "";
            foreach (var l in lines)
                if (time >= l.t0 && time < l.t1)
                    text = string.IsNullOrEmpty(l.who) ? "<i>" + l.text + "</i>" : "<b><color=#FFB000>" + l.who + ":</color></b> " + l.text;
            subtitle.text = text;
        }
        if (title != null)
        {
            if (time < 10f)
            {
                title.text = "<cspace=0.25em>AIKN COURT</cspace>\n<size=32%><color=#FFB000>RENOVATED AT THE AFRICAN LEADERSHIP UNIVERSITY</color></size>";
                title.alpha = Mathf.Clamp01((time - 5.2f) / 1.0f) * Mathf.Clamp01((9.8f - time) / 0.8f); // after the sign close-up
            }
            else if (time > 83.5f)
            {
                title.text = "<cspace=0.25em>AIKN COURT</cspace>\n<size=40%><color=#FFB000><cspace=0.3em>WHERE LEADERS PLAY</cspace></color></size>";
                title.alpha = Mathf.Clamp01((time - 83.5f) / 1.5f);
            }
            else title.alpha = 0;
        }
        if (fade != null)
        {
            float a = Mathf.Max(Mathf.Clamp01(1f - time / 1.5f), Mathf.Clamp01((time - 88.3f) / 1.5f));
            fade.color = new Color(0, 0, 0, a);
        }
        if (hint != null) hint.alpha = time < 6f ? 0.7f : Mathf.Clamp01(1f - (time - 6f));
    }

    // ---------- The story ----------

    void BuildTimeline()
    {
        segs = new Dictionary<GamaActor, List<Seg>>();
        ballSegs = new List<BallSeg>();
        scores = new List<(float, int, int)>();
        lines = new List<Line>();
        shots = new List<Shot>();

        var K = kalisa; var M = mugisha; var G = coachGatete; var S = coachNshuti;
        var IA = investorA; var IB = investorB; var W = woman;
        foreach (var a in new[] { K, M, G, S, IA, IB, W }) segs[a] = new List<Seg>();

        // --- Players: 1v1 half court at the far hoop ---
        var kWarm = V(-2f, 4.5f); var mWarm = V(1.0f, 4.8f);
        At(K, 0, 30, "Dribble", kWarm, face: Rim);
        At(M, 0, 22, "Talking (1)", mWarm, faceActor: K);
        At(M, 22, 30, "Laughing", mWarm, faceActor: K);

        // Day: Kalisa scores
        Walk(K, 30, 36.3f, "Dribble", kWarm, V(-1.5f, 7.6f));
        Guard(M, 30, 36.3f, K);
        float shot1 = 36.3f;
        At(K, shot1, 38.5f, shootState, V(-1.5f, 7.6f), face: Rim);
        At(M, shot1, 38.5f, defendState, GuardSpot(V(-1.5f, 7.6f)), faceActor: K);
        At(K, 38.5f, 41f, "Silly Dancing", V(-1.5f, 7.6f), face: V(-8.4f, 2.6f));
        Walk(M, 38.5f, 40.5f, walkState, GuardSpot(V(-1.5f, 7.6f)), V(0f, 11.4f));
        At(M, 40.5f, 41f, "Dribble", V(0f, 11.4f), face: V(0, 5));

        // Day: Mugisha answers with a three
        Walk(M, 41f, 44f, "Dribble", V(0f, 11.4f), V(3.5f, 5.6f));
        Guard(K, 41f, 46.3f, M);
        At(M, 44f, 46.3f, "Dribble", V(3.5f, 5.6f), face: Rim);
        float shot2 = 46.3f;
        At(M, shot2, 48.5f, shootState, V(3.5f, 5.6f), face: Rim);
        At(K, shot2, 48.5f, defendState, GuardSpot(V(3.5f, 5.6f)), faceActor: M);
        At(M, 48.5f, 56f, "Laughing", V(3.5f, 5.6f), face: V(-1f, 7f));
        At(K, 48.5f, 56f, "Talking (1)", V(2.3f, 7.0f), faceActor: M);

        // Night
        Walk(K, 56f, 62.3f, "Dribble", V(0f, 5f), V(-4.5f, 9f));
        Guard(M, 56f, 62.3f, K);
        float shot3 = 62.3f;
        At(K, shot3, 64.5f, shootState, V(-4.5f, 9f), face: Rim);
        At(M, shot3, 64.5f, defendState, GuardSpot(V(-4.5f, 9f)), faceActor: K);
        At(K, 64.5f, 66f, "Laughing", V(-4.5f, 9f), faceActor: M);
        Walk(M, 64.5f, 66f, walkState, GuardSpot(V(-4.5f, 9f)), V(0f, 11.4f));
        Walk(M, 66f, 69f, "Dribble", V(0f, 11.4f), V(0.5f, 6.6f));
        Walk(M, 69f, 72.2f, "Dribble", V(0.5f, 6.6f), V(1.5f, 9.2f));
        Guard(K, 66f, 72.2f, M);
        float shot4 = 72.2f;
        At(M, shot4, 74f, shootState, V(1.5f, 9.2f), face: Rim);
        At(K, shot4, 73.6f, defendState, GuardSpot(V(1.5f, 9.2f)), faceActor: M);
        Walk(K, 73.6f, 75f, walkState, GuardSpot(V(1.5f, 9.2f)), V(0.3f, 11.3f));
        At(M, 74f, 75f, "Talking (1)", V(1.5f, 9.2f), faceActor: K);
        Walk(K, 75f, 78.2f, "Dribble", V(0.3f, 11.3f), V(-5.6f, 6.2f));
        Guard(M, 75f, 78.2f, K);
        float shot5 = 78.2f;
        At(K, shot5, 79.8f, shootState, V(-5.6f, 6.2f), face: Rim);
        At(M, shot5, 79.8f, defendState, GuardSpot(V(-5.6f, 6.2f)), faceActor: K);
        At(K, 79.8f, 90f, "Silly Dancing", V(-5.6f, 6.2f), face: V(-2f, 0f));
        Walk(M, 79.8f, 81f, walkState, GuardSpot(V(-5.6f, 6.2f)), V(-4.3f, 6.9f));
        At(M, 81f, 90f, "Silly Dancing", V(-4.3f, 6.9f), face: V(-2f, 0f));

        // --- Ball ---
        Ball(0, BallKind.Dribble, K);
        BallShot(shot1, K, V(-1.5f, 7.6f), true, 1);
        BallPass(40.3f, M, V(-1.5f, 7.6f));
        Ball(40.8f, BallKind.Dribble, M);
        BallShot(shot2, M, V(3.5f, 5.6f), true, 2);
        Ball(56f, BallKind.Dribble, K);
        BallShot(shot3, K, V(-4.5f, 9f), true, 1);
        BallPass(65.6f, M, V(-4.5f, 9f));
        Ball(66f, BallKind.Dribble, M);
        BallShot(shot4, M, V(1.5f, 9.2f), true, 2);
        BallPass(74.6f, K, V(1.5f, 9.2f));
        Ball(75f, BallKind.Dribble, K);
        BallShot(shot5, K, V(-5.6f, 6.2f), true, 1);

        // --- Coaches: sit, stand to yell, sit again ---
        Coach(G, chairGatete, new[] {
            (0f, "Sitting Talking", true), (24.4f, "Yelling", false), (30.5f, "Sitting Angry", true), (37.4f, "Yelling Out", false),
            (41f, "Sitting Clap", true), (56f, "Standing Yell", false), (64f, "Sitting Angry", true), (79.3f, "Yelling Out", false) });
        Coach(S, chairNshuti, new[] {
            (0f, "Sitting Talking", true), (27.6f, "Yelling While Standing", false), (31.2f, "Sitting Angry", true), (45.5f, "Yelling", false),
            (50f, "Sitting Clap", true), (64f, "Yelling While Standing", false), (67.5f, "Sitting Angry", true), (73.3f, "Yelling Out", false),
            (79.3f, "Laughing", false) });

        // --- Investors and Ms. Uwase: arrive, walk the court, then sit in Intare ---
        var wPath = new[] { V(1.7f, -14.3f), V(7.9f, -14.3f), V(7.9f, -8.0f) };   // Ms. Uwase leads, nearest the camera
        var aPath = new[] { V(0.9f, -14.9f), V(7.3f, -14.9f), V(7.3f, -8.9f) };
        var bPath = new[] { V(0.4f, -15.5f), V(8.6f, -15.5f), V(8.6f, -9.4f) };
        At(W, 0, 10, "Talking (1)", wPath[0], face: V(4, -15));
        At(IA, 0, 10, "Laughing", aPath[0], faceActor: W);
        At(IB, 0, 10, "Talking (1)", bPath[0], faceActor: W);
        WalkPath(W, 10, 22, walkState, wPath);
        WalkPath(IA, 10, 22, walkState, aPath);
        WalkPath(IB, 10, 22, walkState, bPath);
        At(W, 22, 30, "Telling A Secret", wPath[2], faceActor: IA);
        At(IA, 22, 30, "Talking (1)", aPath[2], faceActor: W);
        At(IB, 22, 30, "Talking (1)", bPath[2], faceActor: W); // Mr. Mensah speaks 21.4-24.5 s
        Seated(IA, seatInvestorA, new[] { (30f, "Sitting Talking"), (37.4f, "Sitting Clap"), (42f, "Sitting Talking"), (56f, "Sitting Laughing"), (63.4f, "Sitting Clap"), (67f, "Sitting Talking"), (79.3f, "Sitting Clap") });
        Seated(W, seatWoman, new[] { (30f, "Sitting Laughing"), (37.4f, "Sitting Clap"), (42f, "Sitting Talking"), (47.4f, "Sitting Clap"), (52f, "Talking"), (66f, "Sitting Talking"), (79.3f, "Sitting Clap") });
        Seated(IB, seatInvestorB, new[] { (30f, "Sitting Talking"), (37.4f, "Sitting Clap"), (42f, "Sitting Laughing"), (47.4f, "Sitting Clap"), (56f, "Sitting Laughing"), (79.3f, "Sitting Clap") });

        // --- Words (narration + character lines). Same text and timings as STORY_ElevenLabs.md ---
        Say(0.8f, 9.55f, "", "At the African Leadership University, a tired old court was given a second life. This is AIKN Court.");
        Say(9.6f, 13.05f, "", "Today, the people who made it possible came to see it for themselves.");
        Say(13.1f, 15.15f, "Mr. Habimana", "So this is where our money went?");
        Say(15.2f, 21.35f, "Ms. Uwase", "Every franc. Imanzi, Jamaica, Imena, Intare. The students named them all.");
        Say(21.4f, 24.6f, "Mr. Mensah", "Swaniker on one wall. Kagame on the other.");
        Say(24.65f, 27.65f, "Coach Gatete", "Kalisa! Warm up! I want energy today!");
        Say(27.7f, 31.15f, "Coach Nshuti", "Mugisha, stay low. Play your game, not his!");
        Say(31.2f, 35.8f, "", "Kalisa and Mugisha. Classmates by day. Rivals on this floor.");
        Say(35.85f, 38.05f, "Kalisa", "You're not stopping me today, Mugisha.");
        Say(38.1f, 40.54f, "Coach Gatete", "Yes! That's you, Kalisa! That's who you are!");
        Say(41.4f, 43.4f, "Mr. Habimana", "They play like they own the place.");
        Say(43.45f, 45.55f, "Ms. Uwase", "They do. That's the point.");
        Say(45.6f, 47.66f, "Coach Nshuti", "Shoot it! Trust yourself!");
        Say(47.9f, 50.45f, "Mugisha", "From downtown! Your move, Kalisa.");
        Say(50.5f, 55.32f, "", "And when the sun goes down over Kigali... the court doesn't sleep.");
        Say(56.5f, 59.75f, "", "Under the new lights, the game only gets louder.");
        Say(59.8f, 62.61f, "Mugisha", "Lights on. No excuses now.");
        Say(64.6f, 67.55f, "Coach Nshuti", "Be bold, Mugisha! Dare to be more!");
        Say(67.6f, 72.95f, "", "The coaches ask for one thing. Not perfection. Just play like yourselves.");
        Say(73.6f, 77.0f, "Ms. Uwase", "This is why we invest. Not in concrete...");
        Say(77.05f, 77.85f, "Mr. Mensah", "...in them.");
        Say(77.9f, 79.87f, "Coach Gatete", "Take the shot!");
        Say(80.0f, 82.75f, "Coaches", "That's it! That's our court!");
        Say(84.0f, 89.54f, "", "AIKN Court. Renovated at ALU. Where leaders play.");

        // --- Camera ---
        // Day: drone close-up gliding along the ALU / AIKN COURT sign on the back of the near wall, then rising over it to reveal the court
        shots.Add(Dolly(0, 4.6f, new Vector3(-6.2f, 1.7f, -21.2f), new Vector3(2.6f, 2.3f, -20.6f), new Vector3(-1.6f, 2.0f, -16.4f), new Vector3(0.9f, 2.0f, -16.4f), 42, 40));
        shots.Add(Dolly(4.6f, 10, new Vector3(0.6f, 2.8f, -22.8f), new Vector3(0, 9.5f, -24.5f), new Vector3(0.2f, 2.0f, -16.4f), new Vector3(0, 1.5f, 6), 44, 48));
        shots.Add(Track(10, 16.5f, W, new Vector3(3.0f, 1.7f, 2.6f), new Vector3(2.4f, 1.6f, 2.8f), 1.4f, 45));
        shots.Add(Track(16.5f, 24.4f, W, new Vector3(-1.8f, 1.8f, 4.0f), new Vector3(-1.5f, 1.6f, 3.0f), 1.4f, 45));
        shots.Add(Dolly(24.4f, 27.6f, new Vector3(-4.6f, 1.4f, 0.4f), new Vector3(-5.0f, 1.5f, 1.3f), new Vector3(-8.0f, 1.15f, 2.7f), new Vector3(-8.0f, 1.2f, 2.7f), 45));
        shots.Add(Dolly(27.6f, 31.0f, new Vector3(4.6f, 1.4f, 4.8f), new Vector3(5.0f, 1.5f, 3.9f), new Vector3(8.0f, 1.15f, 2.5f), new Vector3(8.0f, 1.2f, 2.5f), 45));
        shots.Add(Track(31.0f, shot1, K, new Vector3(-3.2f, 1.3f, -2.6f), new Vector3(-2.8f, 1.6f, -1.6f), 1.1f, 45));
        shots.Add(DollyLookBall(shot1, 39.2f, new Vector3(-3.6f, 2.2f, 2.8f), new Vector3(-3.0f, 2.8f, 4.0f), 55));
        shots.Add(Track(39.2f, 41.5f, K, new Vector3(-2.7f, 0.8f, -0.6f), new Vector3(-2.3f, 0.9f, -1.0f), 1.3f, 45));
        shots.Add(Dolly(41.5f, 44.5f, new Vector3(6.4f, 2.0f, -4.7f), new Vector3(6.6f, 2.0f, -3.9f), new Vector3(9.75f, 1.75f, -2.9f), new Vector3(9.75f, 1.75f, -3.0f), 32));
        shots.Add(DollyLookBall(44.5f, 48.5f, new Vector3(-6.4f, 3.0f, 2.8f), new Vector3(-6.2f, 3.4f, 4.4f), 50));
        // Sunset: high drone over the whole court as the sky darkens and the floodlights switch on pair by pair (52.4-53.2 s)
        shots.Add(Dolly(48.5f, 53.9f, new Vector3(-19f, 15f, -18f), new Vector3(-21f, 13f, -4f), new Vector3(0, 0, -3f), new Vector3(0, 0.5f, 3f), 56, 54));
        // Then the back of the wall: the ALU / AIKN COURT sign powers up (54.2 s)
        shots.Add(Dolly(53.9f, 56, new Vector3(-4.6f, 2.5f, -23.6f), new Vector3(-2.9f, 2.2f, -21.6f), new Vector3(0, 1.9f, -16.4f), new Vector3(0, 1.9f, -16.4f), 46, 42));
        shots.Add(Track(56, shot3, K, new Vector3(3.0f, 1.4f, -2.8f), new Vector3(2.2f, 1.7f, -2.0f), 1.1f, 45));
        shots.Add(DollyLookBall(shot3, 64.5f, new Vector3(1.6f, 0.9f, 15.2f), new Vector3(1.3f, 1.2f, 14.9f), 58));
        shots.Add(Dolly(64.5f, 67.5f, new Vector3(4.8f, 1.4f, 4.6f), new Vector3(5.1f, 1.5f, 3.8f), new Vector3(8.0f, 1.2f, 2.5f), new Vector3(8.0f, 1.2f, 2.5f), 45));
        shots.Add(Track(67.5f, shot4, M, new Vector3(-3.2f, 1.5f, -2.5f), new Vector3(-2.6f, 1.6f, -2.0f), 1.1f, 45));
        shots.Add(DollyLookBall(shot4, 73.6f, new Vector3(-2.0f, 2.2f, 6.0f), new Vector3(-1.6f, 2.6f, 7.0f), 52));
        shots.Add(Dolly(73.6f, 78, new Vector3(7.2f, 2.0f, -1.5f), new Vector3(7.4f, 2.0f, -2.2f), new Vector3(9.75f, 1.75f, -3.4f), new Vector3(9.75f, 1.75f, -3.1f), 30));
        shots.Add(DollyLookBall(78, 80.5f, new Vector3(-8.2f, 2.8f, 2.4f), new Vector3(-7.8f, 3.3f, 3.2f), 52));
        shots.Add(Dolly(80.5f, 84.5f, new Vector3(-2.6f, 1.5f, 3.0f), new Vector3(-3.2f, 2.4f, 1.6f), new Vector3(-5f, 1.2f, 6.6f), new Vector3(-4.9f, 1.2f, 6.6f), 45, 45));
        // Night finale: drone pulls back over the glowing sign, court and floodlights behind it
        shots.Add(Dolly(84.5f, 90, new Vector3(0, 7.5f, -19.5f), new Vector3(0, 13f, -33f), new Vector3(0, 1.8f, -4), new Vector3(0, 3.2f, -6), 50, 50));
    }

    static Vector3 V(float x, float z) => new Vector3(x, 0, z);

    static Vector3 GuardSpot(Vector3 attacker)
    {
        var toRim = Flat(Rim - attacker).normalized;
        return attacker + toRim * 1.15f + Vector3.Cross(Vector3.up, toRim) * 0.15f;
    }

    void Add(GamaActor a, Seg s) { if (a != null) segs[a].Add(s); }

    void At(GamaActor a, float t0, float t1, string state, Vector3 pos, Vector3? face = null, GamaActor faceActor = null)
        => Add(a, new Seg { t0 = t0, t1 = t1, state = state, path = new[] { pos }, face = face, faceActor = faceActor });

    void Walk(GamaActor a, float t0, float t1, string state, Vector3 from, Vector3 to)
        => Add(a, new Seg { t0 = t0, t1 = t1, state = state, path = new[] { from, to } });

    void WalkPath(GamaActor a, float t0, float t1, string state, Vector3[] path)
        => Add(a, new Seg { t0 = t0, t1 = t1, state = state, path = path });

    void Guard(GamaActor a, float t0, float t1, GamaActor target)
        => Add(a, new Seg { t0 = t0, t1 = t1, state = defendState, guard = target, path = new[] { Vector3.zero } });

    void Coach(GamaActor a, Transform chair, (float t0, string state, bool sit)[] beats)
    {
        for (int i = 0; i < beats.Length; i++)
        {
            float t1 = i + 1 < beats.Length ? beats[i + 1].t0 : Duration;
            Add(a, new Seg { t0 = beats[i].t0, t1 = t1, state = beats[i].state, seat = chair, seatOffset = beats[i].sit ? SitOffset : StandOffset, path = new[] { Vector3.zero } });
        }
    }

    void Seated(GamaActor a, Transform seat, (float t0, string state)[] beats)
    {
        for (int i = 0; i < beats.Length; i++)
        {
            float t1 = i + 1 < beats.Length ? beats[i + 1].t0 : Duration;
            Add(a, new Seg { t0 = beats[i].t0, t1 = t1, state = beats[i].state, seat = seat, seatOffset = StandSeatOffset, path = new[] { Vector3.zero } });
        }
    }

    void Ball(float t0, BallKind kind, GamaActor a) => ballSegs.Add(new BallSeg { t0 = t0, kind = kind, actor = a });

    void BallShot(float t0, GamaActor shooter, Vector3 shooterPos, bool made, int team)
    {
        ballSegs.Add(new BallSeg { t0 = t0, kind = BallKind.Shot, actor = shooter, made = made, shooterPos = shooterPos });
        if (made) scores.Add((t0 + GamaBall.FlightTime, team, GamaBall.PointsFrom(shooterPos, Rim)));
        var target = GamaBall.ShotTarget(Rim, made, shooterPos);
        ballSegs.Add(new BallSeg { t0 = t0 + GamaBall.FlightTime + 2.6f, kind = BallKind.Rest, rest = GamaBall.RestPoint(target, made, shooterPos) });
    }

    void BallPass(float t0, GamaActor to, Vector3 lastShooterPos)
    {
        ballSegs.RemoveAll(x => x.kind == BallKind.Rest && x.t0 >= t0);
        var target = GamaBall.ShotTarget(Rim, true, lastShooterPos);
        ballSegs.Add(new BallSeg { t0 = t0, kind = BallKind.Pass, actor = to, rest = GamaBall.RestPoint(target, true, lastShooterPos) });
    }

    void Say(float t0, float t1, string who, string text) => lines.Add(new Line { t0 = t0, t1 = t1, who = who, text = text });
}
