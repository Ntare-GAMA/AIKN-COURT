using System;
using System.IO;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

// Prepares the story's audio from the imported music / SFX files (trims, normalises, synthesises the missing
// whistle, buzzer and light-switch sounds) and wires the director's cue sheet. Called from GamaStoryBuilder.
public static class GamaAudioPrep
{
    const string A = "Assets/GAMA/Audio";
    const string Gen = A + "/Generated";
    const int Rate = 44100;

    public static void Wire(GamaStoryDirector dir, GameObject go)
    {
        Directory.CreateDirectory(Gen);

        // ---- Prepared clips ----
        var swish = Extract("SFX/shot.mp3", "swish", 0.0f, 1.6f, 0.01f, 0.4f, 0.95f);
        var cheer = Extract("SFX/crowd.mp3", "cheer", 0.0f, 4.0f, 0.05f, 1.6f, 0.85f);
        var crowdBed = Extract("SFX/crowd.mp3", "crowd_bed", 20f, 30f, 0.4f, 0.4f, 0.6f);
        var dribble = Extract("SFX/dribble_loop.mp3", "dribble", 3.0f, 41f, 0.01f, 0.05f, 0.95f);
        var djembe = Extract("Percussion/djembe.mp3", "djembe_norm", 0f, 16f, 0.05f, 0.3f, 0.8f);
        var whistle = Synth("whistle", 0.75f, Whistle);
        var buzzer = Synth("buzzer", 1.3f, Buzzer);
        var lightsOn = Synth("lights_on", 1.6f, LightsOn);
        var signOn = Synth("sign_on", 1.4f, SignOn);

        // ---- Sources ----
        dir.sfx = Src(go, null);
        dir.dribbleLoop = Src(go, dribble);
        dir.crowdBed = Src(go, crowdBed);
        dir.freeplayMusic = Src(go, Load("Music/music_freeplay.mp3"));
        dir.sfxSwish = swish; dir.sfxCheer = cheer; dir.sfxWhistle = whistle; dir.sfxBuzzer = buzzer;
        dir.sfxLightsOn = lightsOn; dir.sfxSignOn = signOn;

        var day = Load("Music/music_day.mp3");
        var night = Load("Music/music_night.mp3");
        dir.layers = new[]
        {
            // Day: African theme under the arrival and the day game, fading as the floodlights come on
            Layer(go, "Day music", day, 0f, 53.6f, 0f, 0.42f, 2f, 1.4f),
            // Percussion builds the pace: conga for the warm-up, hybrid drums for the day game
            Layer(go, "Conga (warm-up)", Load("Percussion/conga.mp3"), 22f, 38f, 0f, 0.22f, 1.5f, 1.5f),
            Layer(go, "Hybrid drums (day game)", Load("Percussion/hybrid_drums.mp3"), 38f, 52.5f, 0f, 0.32f, 1f, 1.5f),
            // Night: basketball track hits as the sign lights up and ends on the final title (35 s long)
            Layer(go, "Night music", night, 54.2f, 90f, 0f, 0.4f, 0.15f, 2f),
            Layer(go, "Djembe (night game)", djembe, 64f, 80f, 0f, 0.3f, 1f, 1.5f),
            // Outro: the African theme returns under the final drone shot and title
            Layer(go, "Outro music", day, 84.6f, 90f, 30f, 0.5f, 1.2f, 3f),
        };
    }

    static GamaStoryDirector.AudioLayer Layer(GameObject go, string name, AudioClip clip, float t0, float t1, float offset, float vol, float fadeIn, float fadeOut)
        => new GamaStoryDirector.AudioLayer { name = name, src = Src(go, clip), t0 = t0, t1 = t1, offset = offset, volume = vol, fadeIn = fadeIn, fadeOut = fadeOut, duck = true };

    static AudioSource Src(GameObject go, AudioClip clip)
    {
        var s = go.AddComponent<AudioSource>();
        s.playOnAwake = false; s.spatialBlend = 0; s.clip = clip;
        return s;
    }

    static AudioClip Load(string rel)
    {
        string path = A + "/" + rel;
        var c = AssetDatabase.LoadAssetAtPath<AudioClip>(path);
        if (c == null) Debug.LogWarning("[GAMA] Missing audio " + path);
        return c;
    }

    // ---------- Extract / normalise ----------

    static AudioClip Extract(string rel, string outName, float start, float dur, float fadeIn, float fadeOut, float peak)
    {
        string src = A + "/" + rel;
        var imp = AssetImporter.GetAtPath(src) as AudioImporter;
        if (imp == null) { Debug.LogWarning("[GAMA] Missing audio " + src); return null; }
        var ss = imp.defaultSampleSettings;
        if (ss.loadType != AudioClipLoadType.DecompressOnLoad) { ss.loadType = AudioClipLoadType.DecompressOnLoad; imp.defaultSampleSettings = ss; imp.SaveAndReimport(); }
        var clip = AssetDatabase.LoadAssetAtPath<AudioClip>(src);
        int ch = clip.channels, f = clip.frequency;
        var all = new float[clip.samples * ch];
        clip.GetData(all, 0);
        int a = Mathf.Clamp((int)(start * f), 0, clip.samples), b = Mathf.Clamp((int)((start + dur) * f), a + 1, clip.samples);
        int n = b - a;
        var mono = new float[n];
        for (int i = 0; i < n; i++) { float s = 0; for (int c = 0; c < ch; c++) s += all[(a + i) * ch + c]; mono[i] = s / ch; }
        float max = 1e-4f; foreach (var v in mono) max = Mathf.Max(max, Mathf.Abs(v));
        float g = peak / max;
        int fi = Mathf.Max(1, (int)(fadeIn * f)), fo = Mathf.Max(1, (int)(fadeOut * f));
        for (int i = 0; i < n; i++)
        {
            float env = Mathf.Min(1f, i / (float)fi) * Mathf.Min(1f, (n - 1 - i) / (float)fo);
            mono[i] *= g * env;
        }
        return Save(outName, mono, f);
    }

    // ---------- Synthesised effects ----------

    static AudioClip Synth(string name, float dur, Func<float, System.Random, float> fn)
    {
        int n = (int)(dur * Rate);
        var d = new float[n];
        var rnd = new System.Random(name.GetHashCode());
        for (int i = 0; i < n; i++) d[i] = fn(i / (float)Rate, rnd);
        float max = 1e-4f; foreach (var v in d) max = Mathf.Max(max, Mathf.Abs(v));
        for (int i = 0; i < n; i++) d[i] *= 0.9f / max;
        return Save(name, d, Rate);
    }

    static float Noise(System.Random r) => (float)(r.NextDouble() * 2 - 1);

    // Referee pea whistle: high tone with a fast trill and a breathy attack
    static float Whistle(float t, System.Random r)
    {
        float env = Mathf.Clamp01(t / 0.02f) * Mathf.Clamp01((0.75f - t) / 0.08f);
        float trill = Mathf.Sin(2 * Mathf.PI * 32f * t);
        float freq = 2900f + 180f * trill;
        float tone = Mathf.Sin(2 * Mathf.PI * freq * t) + 0.25f * Mathf.Sin(2 * Mathf.PI * freq * 2f * t);
        return env * (tone * (0.85f + 0.15f * trill) + Noise(r) * 0.12f * Mathf.Exp(-t * 8f));
    }

    // Arena buzzer: two detuned square/saw tones
    static float Buzzer(float t, System.Random r)
    {
        float env = Mathf.Clamp01(t / 0.02f) * Mathf.Clamp01((1.3f - t) / 0.05f);
        float sq = Mathf.Sign(Mathf.Sin(2 * Mathf.PI * 196f * t));
        float saw = 2f * (t * 233f - Mathf.Floor(t * 233f + 0.5f));
        return env * (0.55f * sq + 0.45f * saw) * 0.8f;
    }

    // Floodlight switching on: heavy relay clunk, then a rising electrical hum
    static float LightsOn(float t, System.Random r)
    {
        float thump = Mathf.Sin(2 * Mathf.PI * 52f * t) * Mathf.Exp(-t / 0.09f);
        float click = Noise(r) * Mathf.Exp(-t / 0.012f) * 0.8f;
        float humEnv = Mathf.Clamp01((t - 0.05f) / 0.4f) * Mathf.Clamp01((1.6f - t) / 0.6f);
        float hum = (Mathf.Sin(2 * Mathf.PI * 100f * t) + 0.35f * Mathf.Sin(2 * Mathf.PI * 200f * t) + 0.15f * Mathf.Sin(2 * Mathf.PI * 300f * t)) * 0.28f * humEnv;
        return thump + click + hum;
    }

    // LED sign powering up: crackling flicker, then a bright electric buzz settling to a hum
    static float SignOn(float t, System.Random r)
    {
        float crackle = t < 0.45f && Mathf.Sin(t * 90f) > 0.2f ? Noise(r) * 0.6f * Mathf.Exp(-t * 3f) : 0f;
        float rise = Mathf.Clamp01((t - 0.35f) / 0.15f) * Mathf.Clamp01((1.4f - t) / 0.7f);
        float freq = 120f + 60f * Mathf.Clamp01((t - 0.35f) / 0.3f);
        float buzz = (Mathf.Sin(2 * Mathf.PI * freq * t) + 0.5f * Mathf.Sign(Mathf.Sin(2 * Mathf.PI * freq * 2f * t)) * 0.3f) * 0.5f * rise;
        float zap = Mathf.Sin(2 * Mathf.PI * (1800f - 1400f * Mathf.Clamp01((t - 0.35f) / 0.12f)) * t) * Mathf.Exp(-Mathf.Max(0, t - 0.35f) / 0.05f) * (t > 0.35f ? 0.4f : 0f);
        return crackle + buzz + zap;
    }

    // ---------- WAV writing ----------

    static AudioClip Save(string name, float[] mono, int freq)
    {
        string path = Gen + "/" + name + ".wav";
        using (var fs = File.Create(path))
        using (var w = new BinaryWriter(fs))
        {
            w.Write(System.Text.Encoding.ASCII.GetBytes("RIFF")); w.Write(36 + mono.Length * 2);
            w.Write(System.Text.Encoding.ASCII.GetBytes("WAVEfmt ")); w.Write(16); w.Write((short)1); w.Write((short)1);
            w.Write(freq); w.Write(freq * 2); w.Write((short)2); w.Write((short)16);
            w.Write(System.Text.Encoding.ASCII.GetBytes("data")); w.Write(mono.Length * 2);
            foreach (var v in mono) w.Write((short)Mathf.Clamp(Mathf.RoundToInt(v * 32767f), -32767, 32767));
        }
        AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceUpdate);
        return AssetDatabase.LoadAssetAtPath<AudioClip>(path);
    }
}
