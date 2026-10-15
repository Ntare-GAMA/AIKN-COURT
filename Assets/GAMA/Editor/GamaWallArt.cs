using System.Collections.Generic;
using System.IO;
using TMPro;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.TextCore.LowLevel;

// End-wall art. Front (court side): a carved relief portrait plus a blackletter quote cut into the stone.
// Back (outside): an embedded LED sign "ALU / AIKN COURT" in ALU's blue, red and white.
// Far wall = Fred Swaniker, near wall = H.E. Paul Kagame. Called from GamaStoryBuilder.BuildStory.
public static class GamaWallArt
{
    const string ArtDir = "Assets/GAMA/Art";
    const string FontDir = "Assets/GAMA/Fonts";
    static readonly string PortraitDir = Path.GetFullPath(Path.Combine(Application.dataPath, "..", "Art", "portraits"));

    // ALU brand palette (sampled from ALU's own publications)
    static readonly Color AluBlue = new Color(0.10f, 0.42f, 0.86f);
    static readonly Color AluNavy = new Color(0.02f, 0.07f, 0.19f);
    static readonly Color AluRed = new Color(0.86f, 0.10f, 0.14f);

    // Wall stone as it reads under the scene's lighting (TMP is unlit, so carved text is tinted to match).
    static readonly Color StoneLit = new Color(0.80f, 0.72f, 0.60f);

    class Portrait
    {
        public string file, name, title, quote;
        public RectInt crop; // pixels, origin top-left
    }

    public static void Build(GamaDayNight dn)
    {
        Directory.CreateDirectory(ArtDir);
        var blackletter = FontAsset("UnifrakturMaguntia-Book");
        var roman = FontAsset("Cinzel");
        var sans = FontAsset("Poppins-Bold");

        var leds = new List<Material>(); var ledColors = new List<Color>();
        var carved = new List<Material>(); var carvedColors = new List<Color>();
        var accents = new List<Light>();
        var tigerZebra = TigerZebra();

        var walls = new[]
        {
            (wall: GameObject.Find("Wall_Far"), side: +1, p: new Portrait {
                file = "swaniker.jpg", name = "FRED SWANIKER", title = "FOUNDER · AFRICAN LEADERSHIP UNIVERSITY",
                quote = "it's about who you're...", crop = new RectInt(55, 0, 455, 485) }),
            (wall: GameObject.Find("Wall_Near"), side: -1, p: new Portrait {
                file = "kagame.png", name = "H.E. PAUL KAGAME", title = "PRESIDENT OF THE REPUBLIC OF RWANDA",
                quote = "dare to be more...", crop = new RectInt(140, 30, 460, 470) }),
        };

        foreach (var w in walls)
        {
            if (w.wall == null) continue;
            var t = w.wall.transform;
            float s = w.side;
            float halfT = t.localScale.z * 0.5f;
            var root = new GameObject("WallArt_" + (s > 0 ? "Far" : "Near")).transform;
            root.SetParent(t.parent);

            // ---------- FRONT: carved portrait + quote ----------
            var nFront = new Vector3(0, 0, -s);                   // outward normal of the court-facing side
            var frontC = new Vector3(t.position.x, 0, t.position.z) + nFront * (halfT + 0.006f);
            var right = Vector3.Cross(Vector3.up, -nFront);       // viewer's right when facing the wall
            var faceRot = Quaternion.LookRotation(-nFront);
            // The near wall's front is mostly in shade: darker unlit tints there.
            float shade = s > 0 ? 1f : 0.72f;

            var relief = MakeRelief(w.p);
            float ph = 2.85f, pw = ph * relief.aspect;
            var quad = GameObject.CreatePrimitive(PrimitiveType.Quad);
            Object.DestroyImmediate(quad.GetComponent<Collider>());
            quad.name = "Relief_" + w.p.name;
            quad.transform.SetParent(root);
            quad.transform.SetPositionAndRotation(frontC + right * -4.1f + Vector3.up * (0.42f + ph * 0.5f), faceRot);
            quad.transform.localScale = new Vector3(pw, ph, 1);
            quad.GetComponent<Renderer>().sharedMaterial = relief.mat;
            quad.GetComponent<Renderer>().shadowCastingMode = ShadowCastingMode.Off;

            // Name plate under the portrait, Roman inscription capitals
            Carve(root, roman, "<size=100%>" + w.p.name + "</size>\n<size=62%>" + w.p.title + "</size>",
                frontC + right * -4.1f + Vector3.up * 0.24f, faceRot, right, new Vector2(3.4f, 0.42f), 3f, shade, carved, carvedColors, "Plate");

            // Quote in blackletter, beside the portrait
            Carve(root, blackletter, w.p.quote, frontC + right * 3.1f + Vector3.up * 1.95f, faceRot, right,
                new Vector2(6.6f, 1.25f), 6f, shade, carved, carvedColors, "Quote");

            // Warm uplights for night
            foreach (float dx in new[] { -4.1f, 3.1f })
            {
                var lg = new GameObject("ArtUplight").transform;
                lg.SetParent(root);
                lg.position = frontC + right * dx + nFront * (dx > 0 ? 1.6f : 1.1f) + Vector3.up * 0.12f;
                lg.rotation = Quaternion.LookRotation(frontC + right * dx + Vector3.up * (dx > 0 ? 3.4f : 2.4f) - lg.position);
                var l = lg.gameObject.AddComponent<Light>();
                l.type = LightType.Spot; l.spotAngle = dx > 0 ? 110f : 75f; l.innerSpotAngle = 40f; l.range = 6.5f;
                l.color = new Color(1f, 0.82f, 0.58f); l.shadows = LightShadows.None; l.enabled = false;
                accents.Add(l);
                var fix = GameObject.CreatePrimitive(PrimitiveType.Cube);
                Object.DestroyImmediate(fix.GetComponent<Collider>());
                fix.name = "UplightFixture"; fix.transform.SetParent(root);
                fix.transform.position = lg.position - Vector3.up * 0.06f;
                fix.transform.localScale = new Vector3(0.22f, 0.12f, 0.16f);
                fix.GetComponent<Renderer>().sharedMaterial = GamaCourtBuilder.Mat("M_DarkMetal", new Color(0.06f, 0.06f, 0.07f), 0.5f, null, null, 0.6f);
            }

            // ---------- BACK: embedded LED sign ----------
            var nBack = new Vector3(0, 0, s);
            var backC = new Vector3(t.position.x, 0, t.position.z) + nBack * halfT;
            var bRight = Vector3.Cross(Vector3.up, -nBack);
            var bRot = Quaternion.LookRotation(-nBack);

            var panelMat = GamaCourtBuilder.Mat("M_LED_Panel", AluNavy, 0.35f, null, null, 0.1f);
            var frameMat = GamaCourtBuilder.Mat("M_LED_Frame", new Color(0.12f, 0.12f, 0.13f), 0.6f, null, null, 0.8f);
            var panel = GameObject.CreatePrimitive(PrimitiveType.Cube);
            Object.DestroyImmediate(panel.GetComponent<Collider>());
            panel.name = "LED_Panel"; panel.transform.SetParent(root);
            panel.transform.SetPositionAndRotation(backC + nBack * 0.012f + Vector3.up * 1.85f, bRot);
            panel.transform.localScale = new Vector3(10.6f, 2.55f, 0.05f);
            panel.GetComponent<Renderer>().sharedMaterial = panelMat;
            foreach (var (off, size) in new[] { (new Vector3(0, 1.3f, 0), new Vector3(10.8f, 0.08f, 0.07f)), (new Vector3(0, -1.3f, 0), new Vector3(10.8f, 0.08f, 0.07f)),
                                                (new Vector3(-5.36f, 0, 0), new Vector3(0.08f, 2.68f, 0.07f)), (new Vector3(5.36f, 0, 0), new Vector3(0.08f, 2.68f, 0.07f)) })
            {
                var f = GameObject.CreatePrimitive(PrimitiveType.Cube);
                Object.DestroyImmediate(f.GetComponent<Collider>());
                f.name = "LED_Frame"; f.transform.SetParent(root);
                f.transform.SetPositionAndRotation(panel.transform.position + bRight * off.x + Vector3.up * off.y + nBack * 0.01f, bRot);
                f.transform.localScale = size;
                f.GetComponent<Renderer>().sharedMaterial = frameMat;
            }
            var lc = backC + nBack * 0.045f;
            Led(root, sans, "ALU", lc + Vector3.up * 2.35f, bRot, new Vector2(6f, 1.25f), 14f, Color.white, "LED_ALU", leds, ledColors, 0.06f);
            var bar = GameObject.CreatePrimitive(PrimitiveType.Cube);
            Object.DestroyImmediate(bar.GetComponent<Collider>());
            bar.name = "LED_RedBar"; bar.transform.SetParent(root);
            bar.transform.SetPositionAndRotation(lc + Vector3.up * 1.52f, bRot);
            bar.transform.localScale = new Vector3(5.2f, 0.09f, 0.02f);
            var barMat = GamaCourtBuilder.Mat("M_LED_RedBar", AluRed, 0.5f);
            bar.GetComponent<Renderer>().sharedMaterial = barMat;
            leds.Add(barMat); ledColors.Add(AluRed);
            Led(root, sans, "AIKN COURT", lc + Vector3.up * 1.0f, bRot, new Vector2(9.6f, 0.9f), 9f, Color.white, "LED_AIKN", leds, ledColors, 0.16f, tigerZebra);

            // Night glow onto the wall and ground
            var gl = new GameObject("LED_Glow").transform;
            gl.SetParent(root);
            gl.position = backC + nBack * 1.4f + Vector3.up * 1.9f;
            var glow = gl.gameObject.AddComponent<Light>();
            glow.type = LightType.Point; glow.range = 7f; glow.color = new Color(0.45f, 0.62f, 1f);
            glow.shadows = LightShadows.None; glow.enabled = false;
            accents.Add(glow);
        }

        dn.ledMaterials = leds.ToArray(); dn.ledColors = ledColors.ToArray();
        dn.carvedTextMaterials = carved.ToArray(); dn.carvedTextColors = carvedColors.ToArray();
        dn.accentLights = accents.ToArray();
        AssetDatabase.SaveAssets();
    }

    // ---------- Text ----------

    // Carved lettering: a light lower-right edge under a dark recessed face, like an inscription cut in stone.
    static void Carve(Transform root, TMP_FontAsset font, string text, Vector3 pos, Quaternion rot, Vector3 right, Vector2 size,
        float maxSize, float shade, List<Material> mats, List<Color> cols, string key)
    {
        var lit = new Color(Mathf.Min(1, StoneLit.r * 1.18f * shade), Mathf.Min(1, StoneLit.g * 1.18f * shade), Mathf.Min(1, StoneLit.b * 1.16f * shade), 1);
        var dark = new Color(StoneLit.r * 0.42f * shade, StoneLit.g * 0.38f * shade, StoneLit.b * 0.34f * shade, 1);
        var hl = Text(root, font, text, pos + right * 0.012f - Vector3.up * 0.012f + rot * Vector3.forward * 0.001f, rot, size, maxSize,
            TextMat(font, "M_Carve_" + key + "_Edge" + (shade < 1 ? "_Shade" : ""), lit), key + "_Edge");
        var face = Text(root, font, text, pos, rot, size, maxSize, TextMat(font, "M_Carve_" + key + (shade < 1 ? "_Shade" : ""), dark), key);
        mats.Add(hl.fontSharedMaterial); cols.Add(lit);
        mats.Add(face.fontSharedMaterial); cols.Add(dark);
    }

    static void Led(Transform root, TMP_FontAsset font, string text, Vector3 pos, Quaternion rot, Vector2 size, float maxSize, Color c,
        string name, List<Material> mats, List<Color> cols, float spacing, Texture2D faceTex = null)
    {
        var mat = TextMat(font, "M_" + name, c);
        var t = Text(root, font, text, pos, rot, size, maxSize, mat, name);
        t.characterSpacing = spacing * 100f;
        if (faceTex != null)
        {
            // Texture spans the whole line, so the stripes flow across all the letters.
            mat.shader = Shader.Find("TextMeshPro/Distance Field"); // the Mobile shader has no face texture
            mat.SetTexture("_FaceTex", faceTex);
            t.horizontalMapping = TextureMappingOptions.Line;
            t.verticalMapping = TextureMappingOptions.Line;
            mat.SetFloat("_OutlineWidth", 0.12f);
            mat.SetColor("_OutlineColor", new Color(0.95f, 0.55f, 0.12f));
            mat.EnableKeyword("OUTLINE_ON");
        }
        mats.Add(mat); cols.Add(c);
    }

    // Tiger -> zebra: orange coat on the left blending into white on the right, with shared wavy black stripes.
    static Texture2D TigerZebra()
    {
        string path = ArtDir + "/TEX_TigerZebra.png";
        const int W = 2048, H = 256;
        var tex = new Texture2D(W, H, TextureFormat.RGBA32, false);
        var px = new Color[W * H];
        var tiger = new Color(0.98f, 0.52f, 0.08f);
        var tigerBelly = new Color(1.0f, 0.78f, 0.36f);
        var zebra = new Color(0.97f, 0.96f, 0.93f);
        for (int y = 0; y < H; y++)
            for (int x = 0; x < W; x++)
            {
                float u = x / (float)W, v = y / (float)H;
                // Blend zone in the middle of the line
                float b = Smooth(0.38f, 0.62f, u);
                var coat = Color.Lerp(Color.Lerp(tiger, tigerBelly, v * 0.35f), zebra, b);
                // Stripes: slanted, wavy and tapering (tiger) -> straighter and bolder (zebra)
                float warp = Mathf.PerlinNoise(u * 6f, v * 2.5f) * 0.9f + Mathf.Sin(v * 9f + u * 20f) * 0.12f;
                float phase = u * 34f + (v - 0.5f) * Mathf.Lerp(2.6f, 1.2f, b) + warp;
                float s = Mathf.Abs(Mathf.Sin(phase * Mathf.PI));
                float width = Mathf.Lerp(0.30f - 0.12f * Mathf.Abs(v - 0.5f) * 2f, 0.42f, b); // tiger stripes thinner, tapering at the edges
                float stripe = Smooth(1f - width - 0.05f, 1f - width + 0.05f, s);
                stripe *= Mathf.Lerp(Mathf.PerlinNoise(u * 22f, v * 4f) > 0.3f ? 1f : 0f, 1f, b); // broken tiger stripes
                var c = Color.Lerp(coat, new Color(0.05f, 0.04f, 0.04f), stripe);
                px[y * W + x] = c;
            }
        tex.SetPixels(px);
        File.WriteAllBytes(path, tex.EncodeToPNG());
        Object.DestroyImmediate(tex);
        AssetDatabase.ImportAsset(path);
        var imp = (TextureImporter)AssetImporter.GetAtPath(path);
        imp.wrapMode = TextureWrapMode.Clamp; imp.maxTextureSize = 2048; imp.SaveAndReimport();
        return AssetDatabase.LoadAssetAtPath<Texture2D>(path);
    }

    static TextMeshPro Text(Transform root, TMP_FontAsset font, string text, Vector3 pos, Quaternion rot, Vector2 size, float maxSize, Material mat, string name)
    {
        var go = new GameObject(name);
        go.transform.SetParent(root);
        go.transform.SetPositionAndRotation(pos, rot);
        var tmp = go.AddComponent<TextMeshPro>();
        tmp.font = font;
        tmp.fontSharedMaterial = mat;
        tmp.text = text;
        tmp.color = Color.white;
        tmp.alignment = TextAlignmentOptions.Center;
        tmp.enableAutoSizing = true;
        tmp.fontSizeMin = 0.3f;
        tmp.fontSizeMax = maxSize;
        tmp.enableWordWrapping = false;
        tmp.rectTransform.sizeDelta = size;
        go.GetComponent<MeshRenderer>().shadowCastingMode = ShadowCastingMode.Off;
        return tmp;
    }

    static Material TextMat(TMP_FontAsset font, string name, Color face)
    {
        string path = ArtDir + "/" + name + ".mat";
        AssetDatabase.DeleteAsset(path);
        var m = new Material(font.material) { name = name };
        m.SetColor("_FaceColor", face);
        AssetDatabase.CreateAsset(m, path);
        return m;
    }

    static TMP_FontAsset FontAsset(string ttfName)
    {
        string path = FontDir + "/" + ttfName + " SDF.asset";
        var existing = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(path);
        if (existing != null) return existing;
        var font = AssetDatabase.LoadAssetAtPath<Font>(FontDir + "/" + ttfName + ".ttf");
        var fa = TMP_FontAsset.CreateFontAsset(font, 90, 9, GlyphRenderMode.SDFAA, 1024, 1024, AtlasPopulationMode.Dynamic, true);
        fa.name = ttfName + " SDF";
        AssetDatabase.CreateAsset(fa, path);
        fa.atlasTextures[0].name = ttfName + " Atlas";
        AssetDatabase.AddObjectToAsset(fa.atlasTextures[0], fa);
        fa.material.name = ttfName + " Material";
        AssetDatabase.AddObjectToAsset(fa.material, fa);
        // Pre-populate the glyphs used here so the atlas is saved with them.
        fa.TryAddCharacters("ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz0123456789'.,·- ", out _);
        EditorUtility.SetDirty(fa);
        AssetDatabase.SaveAssets();
        return fa;
    }

    // ---------- Relief portrait ----------

    struct Relief { public Material mat; public float aspect; }

    static Relief MakeRelief(Portrait p)
    {
        string key = Path.GetFileNameWithoutExtension(p.file);
        var src = new Texture2D(2, 2, TextureFormat.RGBA32, false);
        src.LoadImage(File.ReadAllBytes(Path.Combine(PortraitDir, p.file)));
        var r = p.crop;
        int W = 1024, H = Mathf.RoundToInt(1024f * r.height / r.width);
        var L = new float[W * H];
        for (int y = 0; y < H; y++)
            for (int x = 0; x < W; x++)
            {
                float u = (r.x + (x + 0.5f) / W * r.width) / src.width;
                float v = 1f - (r.y + (1f - (y + 0.5f) / H) * r.height) / src.height;
                var c = src.GetPixelBilinear(u, v);
                L[y * W + x] = c.r * 0.3f + c.g * 0.59f + c.b * 0.11f;
            }
        Object.DestroyImmediate(src);

        // Auto-levels, local contrast, posterised carving tones, carved outlines
        var sorted = (float[])L.Clone(); System.Array.Sort(sorted);
        float lo = sorted[(int)(sorted.Length * 0.03f)], hi = sorted[(int)(sorted.Length * 0.97f)];
        for (int i = 0; i < L.Length; i++) L[i] = Mathf.Clamp01((L[i] - lo) / Mathf.Max(0.05f, hi - lo));
        var big = Blur(L, W, H, 28);
        var small = Blur(L, W, H, 2);
        var tone = new float[W * H];
        var alpha = new float[W * H];
        for (int y = 1; y < H - 1; y++)
            for (int x = 1; x < W - 1; x++)
            {
                int i = y * W + x;
                float l = Mathf.Clamp01(0.5f + (small[i] - big[i]) * 1.7f + (small[i] - 0.5f) * 0.75f);
                float post = Mathf.Round(l * 5f) / 5f;
                float t = Mathf.Lerp(l, post, 0.65f);
                float gx = small[i + 1] - small[i - 1], gy = small[i + W] - small[i - W];
                float edge = Mathf.Clamp01(Mathf.Sqrt(gx * gx + gy * gy) * 4.5f);
                t = Mathf.Clamp01(t - edge * 0.55f);

                // Arched medallion mask with a carved border ring
                float u = (x + 0.5f) / W, v = (y + 0.5f) / H;
                float dx = (u - 0.5f) / 0.47f, dy = (v - 0.47f) / 0.55f;
                if (dy < 0) dy *= 0.55f; // flatter at the bottom, so the shoulders sit on the base line
                float d = Mathf.Sqrt(dx * dx + dy * dy);
                float m = 1f - Smooth(0.9f, 0.97f, d);
                float ring = 1f - Mathf.Clamp01(Mathf.Abs(d - 0.995f) / 0.018f);
                float bottom = Smooth(0.0f, 0.05f, v);
                tone[i] = Mathf.Lerp(1f, t, m) - ring * 0.6f;
                alpha[i] = Mathf.Max(m, ring) * bottom;
            }

        var albedo = new Texture2D(W, H, TextureFormat.RGBA32, false);
        var normal = new Texture2D(W, H, TextureFormat.RGBA32, false);
        var ap = new Color[W * H]; var np = new Color[W * H];
        var stone = new Color(0.62f, 0.60f, 0.57f);
        for (int y = 0; y < H; y++)
            for (int x = 0; x < W; x++)
            {
                int i = y * W + x;
                float t = tone[i];
                float k = Mathf.Lerp(0.22f, 1.08f, t);
                ap[i] = new Color(stone.r * k, stone.g * k, stone.b * k, alpha[i]);
                int xl = Mathf.Max(0, x - 1), xr = Mathf.Min(W - 1, x + 1), yd = Mathf.Max(0, y - 1), yu = Mathf.Min(H - 1, y + 1);
                float hx = tone[y * W + xr] - tone[y * W + xl], hy = tone[yu * W + x] - tone[yd * W + x];
                var n = new Vector3(-hx * 3.2f, -hy * 3.2f, 1f).normalized;
                np[i] = new Color(n.x * 0.5f + 0.5f, n.y * 0.5f + 0.5f, n.z * 0.5f + 0.5f, 1);
            }
        albedo.SetPixels(ap); normal.SetPixels(np);
        string aPath = ArtDir + "/TEX_Relief_" + key + ".png", nPath = ArtDir + "/TEX_Relief_" + key + "_N.png";
        File.WriteAllBytes(aPath, albedo.EncodeToPNG());
        File.WriteAllBytes(nPath, normal.EncodeToPNG());
        Object.DestroyImmediate(albedo); Object.DestroyImmediate(normal);
        AssetDatabase.ImportAsset(aPath); AssetDatabase.ImportAsset(nPath);
        var ai = (TextureImporter)AssetImporter.GetAtPath(aPath);
        ai.alphaIsTransparency = true; ai.wrapMode = TextureWrapMode.Clamp; ai.maxTextureSize = 2048; ai.SaveAndReimport();
        var ni = (TextureImporter)AssetImporter.GetAtPath(nPath);
        ni.textureType = TextureImporterType.NormalMap; ni.wrapMode = TextureWrapMode.Clamp; ni.SaveAndReimport();

        var mat = GamaCourtBuilder.Mat("M_Relief_" + key, Color.white, 0.12f, AssetDatabase.LoadAssetAtPath<Texture2D>(aPath));
        mat.SetTexture("_BumpMap", AssetDatabase.LoadAssetAtPath<Texture2D>(nPath));
        mat.SetFloat("_BumpScale", 1.6f);
        mat.EnableKeyword("_NORMALMAP");
        mat.SetFloat("_Surface", 1); mat.SetFloat("_Blend", 0); mat.SetFloat("_ZWrite", 0);
        mat.SetFloat("_SrcBlend", (float)BlendMode.SrcAlpha); mat.SetFloat("_DstBlend", (float)BlendMode.OneMinusSrcAlpha);
        mat.SetFloat("_SrcBlendAlpha", (float)BlendMode.One); mat.SetFloat("_DstBlendAlpha", (float)BlendMode.OneMinusSrcAlpha);
        mat.SetOverrideTag("RenderType", "Transparent");
        mat.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
        mat.renderQueue = (int)RenderQueue.Transparent;
        mat.SetShaderPassEnabled("DepthOnly", false);
        EditorUtility.SetDirty(mat);
        return new Relief { mat = mat, aspect = r.width / (float)r.height };
    }

    static float Smooth(float a, float b, float x) { float t = Mathf.Clamp01((x - a) / (b - a)); return t * t * (3f - 2f * t); }

    static float[] Blur(float[] src, int w, int h, int rad)
    {
        var tmp = new float[w * h]; var dst = new float[w * h];
        for (int y = 0; y < h; y++)
        {
            float acc = 0; int n = 0;
            for (int x = -rad; x < w; x++)
            {
                if (x + rad < w) { acc += src[y * w + x + rad]; n++; }
                if (x - rad - 1 >= 0) { acc -= src[y * w + x - rad - 1]; n--; }
                if (x >= 0) tmp[y * w + x] = acc / n;
            }
        }
        for (int x = 0; x < w; x++)
        {
            float acc = 0; int n = 0;
            for (int y = -rad; y < h; y++)
            {
                if (y + rad < h) { acc += tmp[(y + rad) * w + x]; n++; }
                if (y - rad - 1 >= 0) { acc -= tmp[(y - rad - 1) * w + x]; n--; }
                if (y >= 0) dst[y * w + x] = acc / n;
            }
        }
        return dst;
    }
}
