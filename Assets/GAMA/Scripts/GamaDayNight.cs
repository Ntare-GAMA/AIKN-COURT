using UnityEngine;

// Blends the whole scene between day (0) and night (1): sun/moon, AllSky skyboxes, ambient, fog, lamp lights.
public class GamaDayNight : MonoBehaviour
{
    public Light sun;
    public Material daySky;
    public Material nightSky;
    public Light[] lampLights;
    public Material lampHeadMaterial;
    public float lampIntensity = 140f;

    [Header("Wall art")]
    [Tooltip("LED sign materials (TMP face or URP Lit emissive). Bright by day, shining and pulsing at night.")]
    public Material[] ledMaterials;
    public Color[] ledColors;
    [Tooltip("Carved-text materials: unlit TMP, so they are dimmed at night to match the lighting.")]
    public Material[] carvedTextMaterials;
    public Color[] carvedTextColors;
    public Light[] accentLights;
    public float accentIntensity = 12f;

    [Range(0, 1)] public float night;

    Material dayInst, nightInst;
    float target = -1f;

    static readonly Color DayAmbSky = new Color(0.72f, 0.78f, 0.88f);
    static readonly Color DayAmbEq = new Color(0.62f, 0.62f, 0.58f);
    static readonly Color DayAmbGround = new Color(0.30f, 0.28f, 0.24f);
    static readonly Color DuskAmbSky = new Color(0.55f, 0.42f, 0.40f);
    static readonly Color NightAmbSky = new Color(0.17f, 0.21f, 0.36f);
    static readonly Color NightAmbEq = new Color(0.12f, 0.13f, 0.18f);
    static readonly Color NightAmbGround = new Color(0.06f, 0.06f, 0.07f);

    // Night-blend thresholds (story: 52.4 s, 52.8 s, 53.2 s for the floodlight pairs, 54.2 s for the sign)
    static readonly float[] PairOn = { 0.585f, 0.668f, 0.747f };
    const float SignOnStart = 0.905f, SignOnFull = 0.975f;

    public bool IsNight => night > 0.5f;

    void Start() { Apply(night, 0f); }

    void Update()
    {
        if (target < 0)
        {
            if (night > 0.5f) ApplyArt(night, Time.time); // keep the LED pulse alive in free play
            return;
        }
        night = Mathf.MoveTowards(night, target, Time.deltaTime / 3f);
        Apply(night, Time.time);
        if (Mathf.Approximately(night, target)) target = -1;
    }

    public void Toggle() { target = night < 0.5f ? 1f : 0f; }

    public void Apply(float n, float time)
    {
        night = n;
        if (dayInst == null && daySky != null) dayInst = new Material(daySky) { name = daySky.name + " (runtime)" };
        if (nightInst == null && nightSky != null) nightInst = new Material(nightSky) { name = nightSky.name + " (runtime)" };

        float dusk = Mathf.Clamp01(n * 2f);          // 0..1 over the first half
        float moon = Mathf.Clamp01((n - 0.5f) * 2f); // 0..1 over the second half

        // Sky: day fades down, swap at the midpoint, night fades up.
        if (n < 0.5f && dayInst != null)
        {
            dayInst.SetFloat("_Exposure", Mathf.Lerp(1f, 0.25f, dusk));
            dayInst.SetColor("_Tint", Color.Lerp(new Color(0.5f, 0.5f, 0.5f), new Color(0.62f, 0.42f, 0.36f), dusk));
            RenderSettings.skybox = dayInst;
        }
        else if (nightInst != null)
        {
            nightInst.SetFloat("_Exposure", Mathf.Lerp(0.25f, 1f, moon));
            RenderSettings.skybox = nightInst;
        }

        // Sun goes down and warms; then becomes a cool, dim moon.
        if (sun != null)
        {
            if (n < 0.5f)
            {
                sun.transform.rotation = Quaternion.Euler(Mathf.Lerp(24f, 3f, dusk), Mathf.Lerp(32f, 55f, dusk), 0);
                sun.color = Color.Lerp(new Color(1f, 0.83f, 0.62f), new Color(1f, 0.45f, 0.25f), dusk);
                sun.intensity = Mathf.Lerp(2.1f, 0.2f, dusk);
            }
            else
            {
                sun.transform.rotation = Quaternion.Euler(48f, -140f, 0);
                sun.color = new Color(0.55f, 0.66f, 1f);
                sun.intensity = Mathf.Lerp(0.1f, 0.6f, moon);
            }
        }

        RenderSettings.ambientMode = UnityEngine.Rendering.AmbientMode.Trilight;
        RenderSettings.ambientSkyColor = n < 0.5f ? Color.Lerp(DayAmbSky, DuskAmbSky, dusk) : Color.Lerp(DuskAmbSky * 0.3f, NightAmbSky, moon);
        RenderSettings.ambientEquatorColor = Color.Lerp(DayAmbEq, NightAmbEq, n);
        RenderSettings.ambientGroundColor = Color.Lerp(DayAmbGround, NightAmbGround, n);
        RenderSettings.reflectionIntensity = Mathf.Lerp(1f, 0.25f, n);
        RenderSettings.fogColor = Color.Lerp(new Color(0.78f, 0.80f, 0.82f), new Color(0.04f, 0.05f, 0.09f), n);

        // Lamps switch on in the dark half with a short flicker.
        bool anyOn = false;
        if (lampLights != null)
            for (int i = 0; i < lampLights.Length; i++)
            {
                var l = lampLights[i];
                if (l == null) continue;
                // Floodlights come on in pairs, sweeping from the near end to the far end, each with a short flicker.
                float th = PairOn[Mathf.Min(i % 3, PairOn.Length - 1)];
                bool on = n > th;
                if (on && n < th + 0.035f)
                    on = Hash(i, Mathf.FloorToInt(time * 14f)) > 0.4f;
                l.enabled = on;
                l.intensity = lampIntensity * Mathf.Lerp(0.6f, 1f, moon);
                anyOn |= on;
            }
        if (lampHeadMaterial != null)
        {
            lampHeadMaterial.EnableKeyword("_EMISSION");
            lampHeadMaterial.SetColor("_EmissionColor", new Color(1f, 0.86f, 0.62f) * (anyOn ? 9f : 0.3f));
        }
        ApplyArt(n, time);
    }

    // LED sign: clearly lit in daylight, shining with a slow pulse at night. Carved text follows the ambient light.
    public void ApplyArt(float n, float time)
    {
        // The LED sign powers up only after the whole court is lit; the carved-wall uplights come on with the last floodlights.
        float on = Mathf.SmoothStep(0, 1, Mathf.InverseLerp(SignOnStart, SignOnFull, n));
        if (on > 0f && on < 0.6f) on *= Hash(7, Mathf.FloorToInt(time * 18f)) > 0.35f ? 1f : 0.15f;
        float upl = n > PairOn[PairOn.Length - 1] ? 1f : 0f;
        if (ledMaterials != null)
            for (int i = 0; i < ledMaterials.Length; i++)
            {
                var m = ledMaterials[i];
                if (m == null) continue;
                var c = ledColors != null && i < ledColors.Length ? ledColors[i] : Color.white;
                float pulse = 0.82f + 0.18f * Mathf.Sin(time * 1.6f + i * 1.3f);
                float k = Mathf.Lerp(1.15f, 2.6f * pulse, on);
                var hdr = new Color(c.r * k, c.g * k, c.b * k, 1f);
                if (m.HasProperty("_FaceColor")) m.SetColor("_FaceColor", hdr);
                if (m.HasProperty("_EmissionColor")) { m.EnableKeyword("_EMISSION"); m.SetColor("_EmissionColor", hdr * 0.8f); }
            }
        if (carvedTextMaterials != null)
            for (int i = 0; i < carvedTextMaterials.Length; i++)
            {
                var m = carvedTextMaterials[i];
                if (m == null) continue;
                var c = carvedTextColors != null && i < carvedTextColors.Length ? carvedTextColors[i] : Color.white;
                float k = Mathf.Lerp(1f, 0.42f, n);
                m.SetColor("_FaceColor", new Color(c.r * k, c.g * k, c.b * k, c.a));
            }
        if (accentLights != null)
            foreach (var l in accentLights)
            {
                if (l == null) continue;
                float a = l.type == LightType.Point ? on : upl;
                l.enabled = a > 0.01f;
                l.intensity = (l.type == LightType.Point ? accentIntensity * 0.5f : accentIntensity) * a;
            }
    }

    static float Hash(int a, int b)
    {
        uint h = (uint)(a * 73856093 ^ b * 19349663);
        h = (h ^ (h >> 13)) * 1274126177;
        return ((h ^ (h >> 16)) & 0xffff) / 65535f;
    }
}
