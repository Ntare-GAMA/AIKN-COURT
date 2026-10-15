using TMPro;
using UnityEngine;
using UnityEngine.InputSystem;

// Free play after the story: control Kalisa, shoot at either hoop, toggle day/night, switch cameras.
public class GamaInteractive : MonoBehaviour
{
    public TextMeshProUGUI hud;
    public TextMeshProUGUI toast;

    GamaStoryDirector d;
    GamaActor player, rival;
    Camera cam;
    int camMode;
    int red, black;
    float clock = 300f;

    enum BallState { Held, Flight, After, Return }
    BallState ballState;
    float ballT;
    bool made;
    Vector3 shotFrom, shotTarget, shooterPos, rim, returnFrom;
    float toastUntil;
    float coachCheerUntil;

    public void Begin(GamaStoryDirector director)
    {
        d = director;
        player = d.kalisa;
        rival = d.mugisha;
        cam = d.storyCam;
        cam.gameObject.SetActive(true);
        red = d.scoreboard != null ? d.scoreboard.home : 0;
        black = d.scoreboard != null ? d.scoreboard.away : 0;
        clock = 300f;
        player.transform.SetPositionAndRotation(new Vector3(0, 0, 4f), Quaternion.identity);
        rival.transform.position = new Vector3(0.3f, 0, 6f);
        ballState = BallState.Held;
        camMode = 0;
        enabled = true;
        if (hud != null)
        {
            hud.gameObject.SetActive(true);
            hud.text = "<b>FREE PLAY</b>\nWASD move  ·  Shift sprint  ·  Space shoot\nN day / night  ·  C camera  ·  R replay story  ·  F12 screenshot";
        }
        Toast("Your court now. Show us who you are.", 3f);
        SitAudience(false);
    }

    void OnDisable()
    {
        if (hud != null) hud.gameObject.SetActive(false);
        if (toast != null) toast.gameObject.SetActive(false);
    }

    void Update()
    {
        if (d == null) return;
        var k = Keyboard.current;
        if (k == null) return;
        float dt = Time.deltaTime;

        if (k.nKey.wasPressedThisFrame && d.dayNight != null) d.dayNight.Toggle();
        if (k.cKey.wasPressedThisFrame) camMode = (camMode + 1) % 4;
        if (k.rKey.wasPressedThisFrame) { enabled = false; d.BeginStory(); return; }

        // --- Movement, relative to the camera ---
        var fwd = cam.transform.forward; fwd.y = 0; fwd.Normalize();
        var right = Vector3.Cross(Vector3.up, fwd);
        Vector2 inp = Vector2.zero;
        if (k.wKey.isPressed || k.upArrowKey.isPressed) inp.y += 1;
        if (k.sKey.isPressed || k.downArrowKey.isPressed) inp.y -= 1;
        if (k.dKey.isPressed || k.rightArrowKey.isPressed) inp.x += 1;
        if (k.aKey.isPressed || k.leftArrowKey.isPressed) inp.x -= 1;
        var move = (fwd * inp.y + right * inp.x);
        if (move.sqrMagnitude > 1) move.Normalize();
        float speed = k.leftShiftKey.isPressed ? 5f : 3f;
        var p = player.transform.position + move * speed * dt;
        p.x = Mathf.Clamp(p.x, -7.3f, 7.3f);
        p.z = Mathf.Clamp(p.z, -13.6f, 13.6f);
        player.transform.position = p;
        var target = TargetRim(p);
        var faceDir = move.sqrMagnitude > 0.01f ? move : Flat(target - p);
        if (faceDir.sqrMagnitude > 0.001f)
            player.transform.rotation = Quaternion.Slerp(player.transform.rotation, Quaternion.LookRotation(faceDir), 10f * dt);

        // --- Ball ---
        ballT += dt;
        switch (ballState)
        {
            case BallState.Held:
                player.SetState("Dribble");
                d.ball.position = GamaBall.Dribble(player, Time.time);
                if (k.spaceKey.wasPressedThisFrame) Shoot(target);
                break;
            case BallState.Flight:
                d.ball.position = GamaBall.Arc(shotFrom, shotTarget, Mathf.Clamp01(ballT / GamaBall.FlightTime));
                if (ballT >= GamaBall.FlightTime) Resolve();
                break;
            case BallState.After:
                player.SetState(made ? "Silly Dancing" : "Laughing");
                d.ball.position = GamaBall.AfterRim(shotTarget, made, shooterPos, ballT);
                if (ballT > 1.8f) { ballState = BallState.Return; ballT = 0; returnFrom = d.ball.position; }
                break;
            case BallState.Return:
                player.SetState("Dribble");
                d.ball.position = GamaBall.Arc(returnFrom, GamaBall.Dribble(player, Time.time) + Vector3.up * 0.3f, Mathf.Clamp01(ballT / 0.7f), 0.8f);
                if (ballT >= 0.7f) ballState = BallState.Held;
                break;
        }

        // --- Rival plays defence between you and the rim ---
        var toRim = Flat(target - p).normalized;
        var want = p + toRim * 1.3f;
        var rp = Vector3.MoveTowards(rival.transform.position, want, 3.4f * dt);
        rival.transform.position = rp;
        var look = Flat(p - rp);
        if (look.sqrMagnitude > 0.001f) rival.transform.rotation = Quaternion.Slerp(rival.transform.rotation, Quaternion.LookRotation(look), 8f * dt);
        rival.SetState("Dribble");

        // --- Coaches stand and yell after a basket, otherwise sit ---
        SitAudience(Time.time < coachCheerUntil);

        // --- Scoreboard clock ---
        clock = Mathf.Max(0, clock - dt);
        if (d.scoreboard != null) d.scoreboard.Set(red, black, "OT", clock, clock <= 0);

        // --- Camera ---
        UpdateCamera(p, toRim, dt);

        if (toast != null) toast.gameObject.SetActive(Time.time < toastUntil);
    }

    Vector3 TargetRim(Vector3 p) => p.z >= 0 ? GamaBall.FarRim : GamaBall.NearRim;

    void Shoot(Vector3 rimPos)
    {
        rim = rimPos;
        shooterPos = player.transform.position;
        float dist = Flat(rim - shooterPos).magnitude;
        float chance = Mathf.Clamp(1.05f - dist * 0.075f, 0.15f, 0.95f);
        made = Random.value < chance;
        shotFrom = player.ReleasePoint;
        shotTarget = GamaBall.ShotTarget(rim, made, shooterPos);
        player.transform.rotation = Quaternion.LookRotation(Flat(rim - shooterPos));
        player.SetState(d.shootState, 0.15f);
        ballState = BallState.Flight;
        ballT = 0;
    }

    void Resolve()
    {
        ballState = BallState.After;
        ballT = 0;
        if (made)
        {
            int pts = GamaBall.PointsFrom(shooterPos, rim);
            red += pts;
            Toast(pts == 3 ? "THREE! That's who you are!" : "BUCKET! +2", 2f);
            coachCheerUntil = Time.time + 3f;
        }
        else Toast("Off the rim. Go again.", 1.5f);
    }

    void SitAudience(bool coachesStand)
    {
        Place(d.coachGatete, d.chairGatete, coachesStand ? "Yelling Out" : "Sitting Clap", !coachesStand);
        Place(d.coachNshuti, d.chairNshuti, coachesStand ? "Yelling" : "Sitting Angry", !coachesStand);
        Place(d.investorA, d.seatInvestorA, coachesStand ? "Sitting Clap" : "Sitting Talking", true);
        Place(d.woman, d.seatWoman, coachesStand ? "Sitting Clap" : "Sitting Laughing", true);
        Place(d.investorB, d.seatInvestorB, coachesStand ? "Sitting Clap" : "Sitting Talking", true);
    }

    static void Place(GamaActor a, Transform seat, string state, bool sit)
    {
        if (a == null || seat == null) return;
        var off = !sit ? new Vector3(0, 0, 0.75f) : seat.name.StartsWith("Seat_") ? GamaStoryDirector.StandSeatOffset : new Vector3(0, 0.1f, 0.04f);
        a.transform.SetPositionAndRotation(seat.position + seat.rotation * off, Quaternion.LookRotation(Flat(seat.forward)));
        a.SetState(state);
    }

    void UpdateCamera(Vector3 p, Vector3 toRim, float dt)
    {
        Vector3 pos, lookAt; float fov = 50f;
        switch (camMode)
        {
            case 0: pos = p - toRim * 5.5f + Vector3.up * 2.8f; lookAt = p + toRim * 3f + Vector3.up * 1.0f; break;   // follow
            case 1: pos = new Vector3(-17f, 6.5f, p.z * 0.6f); lookAt = new Vector3(0, 1f, p.z * 0.75f); fov = 45f; break; // broadcast
            case 2: pos = new Vector3(0, 9f, -25f); lookAt = new Vector3(0, 0, 4f); fov = 50f; break;                      // render match
            default: pos = new Vector3(6.3f, 1.9f, -4.6f); lookAt = new Vector3(9.75f, 1.35f, -2.9f); fov = 40f; break;   // VIP box
        }
        float k = 1f - Mathf.Exp(-6f * dt);
        cam.transform.position = Vector3.Lerp(cam.transform.position, pos, k);
        cam.transform.rotation = Quaternion.Slerp(cam.transform.rotation, Quaternion.LookRotation(lookAt - cam.transform.position), k);
        cam.fieldOfView = Mathf.Lerp(cam.fieldOfView, fov, k);
    }

    void Toast(string msg, float secs)
    {
        if (toast == null) return;
        toast.text = msg;
        toast.gameObject.SetActive(true);
        toastUntil = Time.time + secs;
    }

    static Vector3 Flat(Vector3 v) { v.y = 0; return v; }
}
