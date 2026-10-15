using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using TMPro;
using Object = UnityEngine.Object;
using Random = UnityEngine.Random;

// Builds the whole GAMA Court scene from code. Menu: GAMA Court > Build Everything.
// Re-running it regenerates the scene, so do manual tweaks after the final build or in the constants below.
public static class GamaCourtBuilder
{
    const string Root = "Assets/GAMA";
    const string MatDir = Root + "/Materials";
    const string TexDir = Root + "/Textures";
    const string GenDir = Root + "/Generated";
    const string PrefabDir = Root + "/Prefabs";
    const string ScenePath = "Assets/Scenes/GamaCourt.unity";
    const string HdriPath = Root + "/Sky/kloofendal_48d_partly_cloudy_puresky.hdr";
    public const string DaySkyPath = "Assets/AllSkyFree/Cartoon Base BlueSky/Day_BlueSky_Nothing Equirect.mat";
    public const string NightSkyPath = "Assets/AllSkyFree/Night MoonBurst/AllSky_Night_MoonBurst Equirect.mat";

    // Handles for the story builder
    public static readonly List<Light> LampLights = new List<Light>();
    public static Light Sun;
    public static Material LampMaterial => matLamp;
    public static Volume PostVolume;

    // Camera matched to the render: behind the near wall, looking down the court.
    static readonly Vector3 CamPos = new Vector3(0f, 9f, -25f);
    static readonly Vector3 CamRot = new Vector3(19f, 0f, 0f);
    const float CamFov = 50f;

    static readonly Color Red = new Color(0.80f, 0.07f, 0.09f);
    static readonly Color Green = new Color(0.04f, 0.48f, 0.22f);
    static readonly Color Yellow = new Color(1.00f, 0.78f, 0.04f);
    static readonly Color Blue = new Color(0.05f, 0.28f, 0.85f);

    static Material matConcrete, matWall, matAsphalt, matFloor, matSeat, matDark, matWhite, matGlass,
        matRim, matNet, matFence, matTrunk, matLamp, matScore, matFlower;
    static Material[] matLeaves;
    static Mesh[] blobMeshes;

    // ---------- Entry points ----------

    [MenuItem("GAMA Court/Build Everything", priority = 0)]
    public static void BuildEverything()
    {
        if (!EnsureTmpEssentials()) return;
        Random.InitState(1015);
        Directory.CreateDirectory(MatDir);
        Directory.CreateDirectory(GenDir);
        Directory.CreateDirectory(PrefabDir);

        CreateTexturesAndMaterials();
        var seatPrefab = CreateSeatPrefab();

        var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

        var court = new GameObject("Court").transform;
        BuildCourt(court);
        BuildHoop(court, +1);
        BuildHoop(court, -1);

        var stands = new GameObject("Stands").transform;
        BuildSection(stands, "Imanzi", new Vector3(-9.3f, 0, 8.4f), -90, Red, seatPrefab);
        BuildSection(stands, "Jamaica", new Vector3(-9.3f, 0, -3.2f), -90, Green, seatPrefab);
        BuildSection(stands, "Imena", new Vector3(9.3f, 0, 8.4f), 90, Yellow, seatPrefab);
        BuildSection(stands, "Intare", new Vector3(9.3f, 0, -3.2f), 90, Blue, seatPrefab);
        BuildStairs(stands);

        var env = new GameObject("Environment").transform;
        BuildTerrain(env);
        BuildFence(env);
        BuildLamps(env);
        BuildScoreboard(env);
        BuildTrees(env);
        BuildShrubs(env);

        BuildLightingAndCamera();

        EditorSceneManager.SaveScene(scene, ScenePath);
        EditorBuildSettings.scenes = new[] { new EditorBuildSettingsScene(ScenePath, true) };
        AssetDatabase.SaveAssets();
        Debug.Log("[GAMA] Scene built: " + ScenePath);
    }

    [MenuItem("GAMA Court/Take Screenshots", priority = 20)]
    public static void TakeScreenshots()
    {
        string dir = Path.GetFullPath(Path.Combine(Application.dataPath, "..", "Screenshots"));
        Directory.CreateDirectory(dir);
        string stamp = DateTime.Now.ToString("HHmmss");
        foreach (var cam in Object.FindObjectsByType<Camera>(FindObjectsInactive.Include))
        {
            bool wasActive = cam.gameObject.activeSelf;
            cam.gameObject.SetActive(true);
            Shoot(cam, Path.Combine(dir, cam.name + "_" + stamp + ".png"), 1920, 1080);
            cam.gameObject.SetActive(wasActive);
        }
        Debug.Log("[GAMA] Screenshots saved to " + dir);
    }

    // Command line: -executeMethod GamaCourtBuilder.Batch
    public static void Batch()
    {
        if (!EnsureTmpEssentials())
        {
            Debug.Log("[GAMA] TMP Essentials imported; run again to build.");
            return;
        }
        BuildEverything();
        TakeScreenshots();
    }

    static bool EnsureTmpEssentials()
    {
        if (AssetDatabase.FindAssets("t:TMP_Settings").Length > 0) return true;
        string pkg = Path.GetFullPath("Packages/com.unity.ugui/Package Resources/TMP Essential Resources.unitypackage");
        AssetDatabase.ImportPackage(pkg, false);
        AssetDatabase.Refresh();
        if (AssetDatabase.FindAssets("t:TMP_Settings").Length > 0) return true;
        Debug.LogWarning("[GAMA] TMP Essential Resources were just imported. Run Build Everything again.");
        return false;
    }

    // ---------- Textures & materials ----------

    static void CreateTexturesAndMaterials()
    {
        var concreteTex = MakeTex("TEX_Concrete", 512, 512, false, (x, y) =>
        {
            float n = Fbm(x / 70f, y / 70f, 4) * 0.10f + Hash(x, y) * 0.05f;
            float v = 0.70f + n * 0.6f - 0.04f;
            return new Color(v, v, v * 0.99f, 1);
        });
        var asphaltTex = MakeTex("TEX_Asphalt", 512, 512, false, (x, y) =>
        {
            float v = 0.20f + Fbm(x / 40f, y / 40f, 3) * 0.05f + Hash(x, y) * 0.07f;
            return new Color(v, v * 1.02f, v * 1.08f, 1);
        });
        var grassTex = MakeTex("TEX_Grass", 512, 512, false, (x, y) =>
        {
            float n = Fbm(x / 50f, y / 50f, 4);
            float h = Hash(x, y);
            return new Color(0.22f + n * 0.10f + h * 0.05f, 0.32f + n * 0.12f + h * 0.07f, 0.12f + h * 0.04f, 1);
        });
        var fenceTex = MakeTex("TEX_FenceMesh", 128, 128, true, (x, y) =>
        {
            bool wire = x < 6 || y < 6;
            return wire ? new Color(1, 1, 1, 1) : new Color(1, 1, 1, 0);
        });
        var netTex = MakeTex("TEX_Net", 128, 128, true, (x, y) =>
        {
            int a = (x + y) % 128, b = (x - y + 128) % 128;
            bool wire = a < 8 || b < 8;
            return wire ? new Color(1, 1, 1, 1) : new Color(1, 1, 1, 0);
        });

        var floorTex = AssetDatabase.LoadAssetAtPath<Texture2D>(TexDir + "/TEX_CourtFloor.png");
        var ti = (TextureImporter)AssetImporter.GetAtPath(TexDir + "/TEX_CourtFloor.png");
        if (ti != null && (ti.maxTextureSize != 4096 || ti.anisoLevel != 8))
        {
            ti.maxTextureSize = 4096;
            ti.anisoLevel = 8;
            ti.wrapMode = TextureWrapMode.Clamp;
            ti.SaveAndReimport();
            floorTex = AssetDatabase.LoadAssetAtPath<Texture2D>(TexDir + "/TEX_CourtFloor.png");
        }

        matFloor = Mat("M_CourtFloor", Color.white, 0.3f, floorTex);
        matConcrete = Mat("M_Concrete_Stands", new Color(0.74f, 0.75f, 0.77f), 0.15f, concreteTex, new Vector2(2, 0.7f));
        matWall = Mat("M_Concrete_Wall", new Color(0.80f, 0.80f, 0.81f), 0.12f, concreteTex, new Vector2(3, 0.8f));
        matAsphalt = Mat("M_Apron", new Color(0.85f, 0.85f, 0.9f), 0.2f, asphaltTex, new Vector2(10, 14));
        matSeat = Mat("M_SeatPlastic", Color.white, 0.45f);
        matSeat.SetFloat("_Cull", 0);
        matDark = Mat("M_DarkMetal", new Color(0.06f, 0.06f, 0.07f), 0.5f, null, null, 0.6f);
        matWhite = Mat("M_WhitePaint", new Color(0.95f, 0.95f, 0.95f), 0.5f);
        matRim = Mat("M_Rim", new Color(0.95f, 0.32f, 0.05f), 0.6f, null, null, 0.7f);
        matTrunk = Mat("M_Trunk", new Color(0.30f, 0.22f, 0.15f), 0.1f);
        matLamp = Mat("M_LampHead", new Color(0.1f, 0.1f, 0.1f), 0.5f);
        matLamp.EnableKeyword("_EMISSION");
        matLamp.SetColor("_EmissionColor", new Color(1f, 0.8f, 0.5f) * 0.6f);
        matScore = Mat("M_ScoreboardFace", new Color(0.02f, 0.02f, 0.03f), 0.7f);
        matFlower = Mat("M_Flowers", new Color(0.75f, 0.12f, 0.15f), 0.2f);

        matGlass = Mat("M_BackboardGlass", new Color(0.85f, 0.92f, 0.95f, 0.25f), 0.95f);
        SetTransparent(matGlass);

        matNet = Mat("M_Net", Color.white, 0.2f, netTex, new Vector2(6, 3));
        SetCutout(matNet, true);
        matFence = Mat("M_Fence", new Color(0.05f, 0.18f, 0.10f), 0.3f, fenceTex, new Vector2(1, 1));
        SetCutout(matFence, true);

        matLeaves = new[]
        {
            Mat("M_Leaves_A", new Color(0.20f, 0.33f, 0.10f), 0.1f),
            Mat("M_Leaves_B", new Color(0.30f, 0.42f, 0.13f), 0.1f),
            Mat("M_Leaves_C", new Color(0.14f, 0.26f, 0.08f), 0.1f),
        };
        AssetDatabase.SaveAssets();
    }

    internal static Material Mat(string name, Color color, float smooth, Texture tex = null, Vector2? tiling = null, float metallic = 0f)
    {
        string path = MatDir + "/" + name + ".mat";
        var m = AssetDatabase.LoadAssetAtPath<Material>(path);
        if (m == null)
        {
            m = new Material(Shader.Find("Universal Render Pipeline/Lit"));
            AssetDatabase.CreateAsset(m, path);
        }
        m.SetColor("_BaseColor", color);
        m.SetFloat("_Smoothness", smooth);
        m.SetFloat("_Metallic", metallic);
        m.SetTexture("_BaseMap", tex);
        m.SetTextureScale("_BaseMap", tiling ?? Vector2.one);
        m.enableInstancing = true;
        EditorUtility.SetDirty(m);
        return m;
    }

    static void SetTransparent(Material m)
    {
        m.SetFloat("_Surface", 1);
        m.SetFloat("_Blend", 0);
        m.SetFloat("_ZWrite", 0);
        m.SetFloat("_SrcBlend", (float)BlendMode.SrcAlpha);
        m.SetFloat("_DstBlend", (float)BlendMode.OneMinusSrcAlpha);
        m.SetFloat("_SrcBlendAlpha", (float)BlendMode.One);
        m.SetFloat("_DstBlendAlpha", (float)BlendMode.OneMinusSrcAlpha);
        m.SetOverrideTag("RenderType", "Transparent");
        m.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
        m.renderQueue = (int)RenderQueue.Transparent;
        m.SetShaderPassEnabled("DepthOnly", false);
        m.SetShaderPassEnabled("ShadowCaster", false);
    }

    internal static void SetCutout(Material m, bool twoSided)
    {
        m.SetFloat("_AlphaClip", 1);
        m.SetFloat("_Cutoff", 0.5f);
        m.EnableKeyword("_ALPHATEST_ON");
        m.renderQueue = (int)RenderQueue.AlphaTest;
        m.SetOverrideTag("RenderType", "TransparentCutout");
        if (twoSided)
        {
            m.SetFloat("_Cull", 0);
            m.doubleSidedGI = true;
        }
    }

    static Texture2D MakeTex(string name, int w, int h, bool alpha, Func<int, int, Color> f)
    {
        string path = TexDir + "/" + name + ".png";
        if (!File.Exists(path))
        {
            var t = new Texture2D(w, h, TextureFormat.RGBA32, false);
            var px = new Color[w * h];
            for (int y = 0; y < h; y++)
                for (int x = 0; x < w; x++)
                    px[y * w + x] = f(x, y);
            t.SetPixels(px);
            File.WriteAllBytes(path, t.EncodeToPNG());
            Object.DestroyImmediate(t);
            AssetDatabase.ImportAsset(path);
            var imp = (TextureImporter)AssetImporter.GetAtPath(path);
            imp.wrapMode = TextureWrapMode.Repeat;
            imp.anisoLevel = 4;
            if (alpha)
            {
                imp.alphaIsTransparency = true;
                imp.mipMapsPreserveCoverage = true;
                imp.alphaTestReferenceValue = 0.5f;
            }
            imp.SaveAndReimport();
        }
        return AssetDatabase.LoadAssetAtPath<Texture2D>(path);
    }

    // Tileable value noise helpers
    static float Fbm(float x, float y, int oct)
    {
        float s = 0, a = 0.5f, f = 1;
        for (int i = 0; i < oct; i++) { s += a * (Mathf.PerlinNoise(x * f + 31.7f * i, y * f + 17.3f * i) - 0.5f); a *= 0.5f; f *= 2; }
        return s * 2f;
    }

    static float Hash(int x, int y)
    {
        uint h = (uint)(x * 374761393 + y * 668265263);
        h = (h ^ (h >> 13)) * 1274126177;
        return ((h ^ (h >> 16)) & 0xffff) / 65535f - 0.5f;
    }

    // ---------- Court, walls, hoops ----------

    static void BuildCourt(Transform parent)
    {
        var floor = GameObject.CreatePrimitive(PrimitiveType.Plane);
        floor.name = "CourtFloor";
        floor.transform.SetParent(parent);
        floor.transform.localPosition = new Vector3(0, 0.01f, 0);
        floor.transform.localScale = new Vector3(1.5f, 1, 2.8f);
        floor.GetComponent<Renderer>().sharedMaterial = matFloor;

        var apron = Box("Apron", parent, new Vector3(0, -0.05f, 0), new Vector3(28, 0.1f, 36), matAsphalt);
        apron.GetComponent<Renderer>().shadowCastingMode = ShadowCastingMode.Off;

        Box("Wall_Far", parent, new Vector3(0, 1.7f, 16.2f), new Vector3(15, 3.4f, 0.4f), matWall);
        Box("Wall_Near", parent, new Vector3(0, 1.7f, -16.2f), new Vector3(15, 3.4f, 0.4f), matWall);
    }

    static void BuildHoop(Transform parent, int sign)
    {
        var hoop = new GameObject(sign > 0 ? "Hoop_Far" : "Hoop_Near").transform;
        hoop.SetParent(parent);
        float s = sign;
        float poleZ = 15.0f * s, boardZ = 12.8f * s, rimZ = (14f - 1.575f) * s;

        Box("BasePad", hoop, new Vector3(0, 0.3f, poleZ), new Vector3(0.6f, 0.6f, 0.6f), matDark);
        Box("Pole", hoop, new Vector3(0, 1.85f, poleZ), new Vector3(0.22f, 3.7f, 0.22f), matDark);
        Box("Arm", hoop, new Vector3(0, 3.55f, (poleZ + boardZ) * 0.5f), new Vector3(0.14f, 0.14f, Mathf.Abs(poleZ - boardZ)), matDark);
        Box("Brace", hoop, new Vector3(0, 3.0f, poleZ - 0.5f * s), new Vector3(0.08f, 1.1f, 0.08f), matDark)
            .transform.localRotation = Quaternion.Euler(35f * s, 0, 0);

        // Backboard 1.8 x 1.05, bottom edge 2.9 m
        float bx = 1.8f, by = 1.05f, cy = 2.9f + by * 0.5f, t = 0.05f;
        Box("Glass", hoop, new Vector3(0, cy, boardZ), new Vector3(bx, by, 0.025f), matGlass);
        Box("Frame_T", hoop, new Vector3(0, cy + by * 0.5f, boardZ), new Vector3(bx + t, t, 0.06f), matDark);
        Box("Frame_B", hoop, new Vector3(0, cy - by * 0.5f, boardZ), new Vector3(bx + t, t, 0.06f), matDark);
        Box("Frame_L", hoop, new Vector3(-bx * 0.5f, cy, boardZ), new Vector3(t, by, 0.06f), matDark);
        Box("Frame_R", hoop, new Vector3(bx * 0.5f, cy, boardZ), new Vector3(t, by, 0.06f), matDark);

        // Shooter square 0.59 x 0.45, bottom at rim height
        float fz = boardZ - 0.02f * s, sq = 0.59f, sh = 0.45f, lw = 0.035f, sy = 3.05f + sh * 0.5f;
        Box("Target_T", hoop, new Vector3(0, sy + sh * 0.5f, fz), new Vector3(sq, lw, 0.005f), matWhite);
        Box("Target_B", hoop, new Vector3(0, sy - sh * 0.5f, fz), new Vector3(sq, lw, 0.005f), matWhite);
        Box("Target_L", hoop, new Vector3(-sq * 0.5f, sy, fz), new Vector3(lw, sh, 0.005f), matWhite);
        Box("Target_R", hoop, new Vector3(sq * 0.5f, sy, fz), new Vector3(lw, sh, 0.005f), matWhite);

        // Rim (r = 0.225 m at 3.05 m) and net
        var rim = MeshObj("Rim", hoop, GetMesh("Mesh_Rim", () => TorusMesh(0.225f, 0.011f, 40, 8)), matRim);
        rim.transform.localPosition = new Vector3(0, 3.05f, rimZ);
        Box("RimMount", hoop, new Vector3(0, 3.04f, (boardZ + rimZ + 0.225f * s) * 0.5f), new Vector3(0.12f, 0.03f, 0.18f), matRim);
        var net = MeshObj("Net", hoop, GetMesh("Mesh_Net", () => ConeMesh(0.225f, 0.14f, 0.42f, 20)), matNet);
        net.transform.localPosition = new Vector3(0, 3.05f, rimZ);
        net.GetComponent<Renderer>().shadowCastingMode = ShadowCastingMode.On;
    }

    // ---------- Stands ----------

    static GameObject CreateSeatPrefab()
    {
        var mesh = GetMesh("Mesh_SeatShell", SeatShellMesh, true);
        var root = new GameObject("Seat");
        var shell = MeshObj("Shell", root.transform, mesh, matSeat);
        var mount = GameObject.CreatePrimitive(PrimitiveType.Cube);
        mount.name = "Mount";
        Object.DestroyImmediate(mount.GetComponent<Collider>());
        mount.transform.SetParent(root.transform);
        mount.transform.localPosition = new Vector3(0, 0.2f, -0.05f);
        mount.transform.localScale = new Vector3(0.1f, 0.4f, 0.22f);
        mount.GetComponent<Renderer>().sharedMaterial = matSeat;
        var prefab = PrefabUtility.SaveAsPrefabAsset(root, PrefabDir + "/Seat.prefab");
        Object.DestroyImmediate(root);
        return prefab;
    }

    static void BuildSection(Transform parent, string name, Vector3 pos, float rotY, Color color, GameObject seat)
    {
        var go = new GameObject("Section_" + name);
        go.transform.SetParent(parent);
        go.transform.localPosition = pos;
        go.transform.localRotation = Quaternion.Euler(0, rotY, 0);
        var sec = go.AddComponent<SeatSection>();
        sec.seatPrefab = seat;
        sec.concreteMaterial = matConcrete;
        sec.seatColor = color;
        var seatMat = Mat("M_Seat_" + name, color, 0.25f);
        seatMat.SetFloat("_Cull", 0);
        sec.seatMaterial = seatMat;
        sec.railMaterial = matDark;
        sec.sectionName = name;
        sec.Build();
    }

    // Aisle stairs in the 3.9 m gap between the two sections on each side, plus at the outer ends.
    static void BuildStairs(Transform parent)
    {
        float gapZ = 2.6f; // between z = 0.65 (near section back end) and z = 4.55 (far section front end)
        foreach (int side in new[] { -1, 1 })
        {
            foreach (float z in new[] { gapZ })
            {
                var st = new GameObject("Stairs_" + (side < 0 ? "L" : "R")).transform;
                st.SetParent(parent);
                int steps = 10;
                float run = 3.4f / steps, rise = 0.2f, width = 1.4f;
                for (int i = 0; i < steps; i++)
                {
                    float h = rise * (i + 1);
                    float x = side * (9.3f + run * (i + 0.5f));
                    Box("Step" + i, st, new Vector3(x, h * 0.5f, z), new Vector3(run, h, width), matConcrete);
                }
                // Handrails on both sides of the stair
                foreach (float dz in new[] { -width * 0.5f, width * 0.5f })
                {
                    var a = new Vector3(side * 9.3f, 1.0f, z + dz);
                    var b = new Vector3(side * 12.7f, 3.0f, z + dz);
                    Rod("Rail", st, a, b, 0.04f, matDark);
                    Rod("Post", st, new Vector3(a.x, 0.1f, a.z), a, 0.04f, matDark);
                    Rod("Post", st, new Vector3(b.x, 2.0f, b.z), b, 0.04f, matDark);
                }
            }
        }
    }

    // ---------- Environment ----------

    static void BuildTerrain(Transform parent)
    {
        string path = GenDir + "/GroundTerrain.asset";
        AssetDatabase.DeleteAsset(path);
        var data = new TerrainData { heightmapResolution = 257 };
        const float size = 500f, height = 28f;
        data.size = new Vector3(size, height, size);
        int res = data.heightmapResolution;
        var hts = new float[res, res];
        for (int y = 0; y < res; y++)
            for (int x = 0; x < res; x++)
            {
                float wx = x / (float)(res - 1) * size - size * 0.5f;
                float wz = y / (float)(res - 1) * size - size * 0.5f;
                float d = Mathf.Sqrt(wx * wx * 0.8f + wz * wz);
                float rise = Mathf.SmoothStep(0, 1, Mathf.InverseLerp(70f, 190f, d));
                float n = Mathf.PerlinNoise(wx * 0.012f + 7, wz * 0.012f + 3) * 0.7f + Mathf.PerlinNoise(wx * 0.04f, wz * 0.04f) * 0.3f;
                float behind = wz > 0 ? 1f : 0.5f; // hills mostly beyond the far end (like the render)
                hts[y, x] = rise * n * behind;
            }
        data.SetHeights(0, 0, hts);

        var layer = new TerrainLayer
        {
            diffuseTexture = AssetDatabase.LoadAssetAtPath<Texture2D>(TexDir + "/TEX_Grass.png"),
            tileSize = new Vector2(6, 6),
            smoothness = 0.05f
        };
        AssetDatabase.CreateAsset(data, path);
        AssetDatabase.AddObjectToAsset(layer, data);
        data.terrainLayers = new[] { layer };

        var t = Terrain.CreateTerrainGameObject(data);
        t.name = "Ground";
        t.transform.SetParent(parent);
        t.transform.position = new Vector3(-size * 0.5f, -0.12f, -size * 0.5f);
        var terrain = t.GetComponent<Terrain>();
        terrain.heightmapPixelError = 3;
        terrain.basemapDistance = 400;
    }

    static void BuildFence(Transform parent)
    {
        var fence = new GameObject("Fence").transform;
        fence.SetParent(parent);
        const float h = 2.2f, x = 13.6f, zNear = -17f, zFar = 17.6f;
        FenceRun(fence, new Vector3(-x, 0, zNear), new Vector3(-x, 0, zFar), h);
        FenceRun(fence, new Vector3(x, 0, zNear), new Vector3(x, 0, zFar), h);
        FenceRun(fence, new Vector3(-x, 0, zFar), new Vector3(x, 0, zFar), h);
    }

    static void FenceRun(Transform parent, Vector3 a, Vector3 b, float h)
    {
        float len = Vector3.Distance(a, b);
        int panels = Mathf.CeilToInt(len / 2.5f);
        var dir = (b - a).normalized;
        float pl = len / panels;
        for (int i = 0; i <= panels; i++)
        {
            var p = a + dir * (pl * i);
            Box("Post", parent, p + Vector3.up * h * 0.5f, new Vector3(0.06f, h, 0.06f), matDark);
            if (i == panels) continue;
            var q = GameObject.CreatePrimitive(PrimitiveType.Quad);
            q.name = "Panel";
            Object.DestroyImmediate(q.GetComponent<Collider>());
            q.transform.SetParent(parent);
            q.transform.position = p + dir * pl * 0.5f + Vector3.up * h * 0.5f;
            q.transform.rotation = Quaternion.LookRotation(Vector3.Cross(dir, Vector3.up));
            q.transform.localScale = new Vector3(pl, h, 1);
            q.GetComponent<Renderer>().sharedMaterial = FenceMatFor(pl, h);
        }
        Rod("TopRail", parent, a + Vector3.up * h, b + Vector3.up * h, 0.035f, matDark);
    }

    static readonly Dictionary<string, Material> fenceMats = new Dictionary<string, Material>();
    static Material FenceMatFor(float w, float h)
    {
        string key = w.ToString("0.00");
        if (fenceMats.TryGetValue(key, out var m) && m != null) return m;
        m = Mat("M_Fence_" + key.Replace('.', '_'), matFence.GetColor("_BaseColor"), 0.3f, matFence.GetTexture("_BaseMap"), new Vector2(w / 0.2f, h / 0.2f));
        SetCutout(m, true);
        fenceMats[key] = m;
        return m;
    }

    static void BuildLamps(Transform parent)
    {
        var lamps = new GameObject("LampPosts").transform;
        lamps.SetParent(parent);
        LampLights.Clear();
        foreach (int side in new[] { -1, 1 })
            foreach (float z in new[] { -15.5f, 2.6f, 16.5f })
            {
                var l = new GameObject("Lamp").transform;
                l.SetParent(lamps);
                l.position = new Vector3(side * 13.2f, 0, z);
                Rod("Pole", l, l.position, l.position + Vector3.up * 7.5f, 0.07f, matDark);
                Rod("Arm", l, l.position + Vector3.up * 7.4f, l.position + new Vector3(-side * 0.9f, 7.5f, 0), 0.05f, matDark);
                Box("Head", l, l.position + new Vector3(-side * 1.0f, 7.45f, 0), new Vector3(0.6f, 0.12f, 0.3f), matLamp);
                var lg = new GameObject("LampLight");
                lg.transform.SetParent(l);
                lg.transform.position = l.position + new Vector3(-side * 1.0f, 7.3f, 0);
                lg.transform.rotation = Quaternion.LookRotation(new Vector3(side * 3.5f, 0, z * 0.8f) - lg.transform.position);
                var light = lg.AddComponent<Light>();
                light.type = LightType.Spot;
                light.spotAngle = 110f;
                light.innerSpotAngle = 60f;
                light.range = 30f;
                light.intensity = 90f;
                light.color = new Color(1f, 0.9f, 0.75f);
                light.shadows = LightShadows.None;
                light.enabled = false;
                LampLights.Add(light);
            }
    }

    static void BuildScoreboard(Transform parent)
    {
        var sb = new GameObject("Scoreboard").transform;
        sb.SetParent(parent);
        var c = new Vector3(4.5f, 0, 18.6f);
        Rod("Leg", sb, c + new Vector3(-0.9f, 0, 0), c + new Vector3(-0.9f, 4.2f, 0), 0.07f, matDark);
        Rod("Leg", sb, c + new Vector3(0.9f, 0, 0), c + new Vector3(0.9f, 4.2f, 0), 0.07f, matDark);
        Box("Board", sb, c + new Vector3(0, 4.9f, 0), new Vector3(2.8f, 1.5f, 0.2f), matScore);

        var go = new GameObject("ScoreText");
        go.transform.SetParent(sb);
        go.transform.position = c + new Vector3(0, 4.9f, -0.11f);
        var tmp = go.AddComponent<TextMeshPro>();
        tmp.text = "<color=#FFB000>HOME</color>   <color=#FFB000>GUEST</color>\n<size=150%><color=#FF3B1F>00 : 00</color></size>";
        tmp.alignment = TextAlignmentOptions.Center;
        tmp.enableAutoSizing = true;
        tmp.fontSizeMin = 0.5f;
        tmp.fontSizeMax = 20f;
        tmp.rectTransform.sizeDelta = new Vector2(2.4f, 1.2f);
    }

    static void BuildTrees(Transform parent)
    {
        var trees = new GameObject("Trees").transform;
        trees.SetParent(parent);
        blobMeshes = new[]
        {
            GetMesh("Mesh_LeafBlob0", () => BlobMesh(11), true),
            GetMesh("Mesh_LeafBlob1", () => BlobMesh(23), true),
            GetMesh("Mesh_LeafBlob2", () => BlobMesh(37), true),
        };

        // Big framing trees near the camera (left and right of the render)
        Tree(trees, new Vector3(-15.5f, 0, -20f), 13f, 6.5f);
        Tree(trees, new Vector3(16f, 0, -19f), 14f, 7f);

        // Tree lines along the sides and beyond the far wall
        for (float z = -14; z <= 30; z += Random.Range(5f, 7.5f))
        {
            Tree(trees, new Vector3(-17f - Random.Range(0f, 5f), 0, z), Random.Range(7f, 10f), Random.Range(3f, 4.5f));
            Tree(trees, new Vector3(17f + Random.Range(0f, 5f), 0, z), Random.Range(7f, 10f), Random.Range(3f, 4.5f));
        }
        for (float x = -14; x <= 14; x += Random.Range(5f, 7f))
            Tree(trees, new Vector3(x, 0, 23f + Random.Range(0f, 6f)), Random.Range(6f, 9f), Random.Range(3f, 4.5f));
        // Scattered background trees on the hills
        for (int i = 0; i < 35; i++)
        {
            float ang = Random.Range(-1.3f, 1.3f) + Mathf.PI * 0.5f;
            float r = Random.Range(45f, 140f);
            var p = new Vector3(Mathf.Cos(ang) * r, 0, Mathf.Sin(ang) * r);
            p.y = SampleGround(p);
            Tree(trees, p, Random.Range(5f, 8f), Random.Range(2.5f, 4f));
        }
    }

    static float SampleGround(Vector3 p)
    {
        var t = Terrain.activeTerrain;
        return t != null ? t.SampleHeight(p) + t.transform.position.y : 0f;
    }

    static void Tree(Transform parent, Vector3 pos, float height, float crown)
    {
        var t = new GameObject("Tree").transform;
        t.SetParent(parent);
        t.position = pos;
        t.rotation = Quaternion.Euler(0, Random.Range(0, 360f), 0);
        float trunkH = height * 0.55f;
        var lean = new Vector3(Random.Range(-0.4f, 0.4f), 0, Random.Range(-0.4f, 0.4f));
        Rod("Trunk", t, pos, pos + Vector3.up * trunkH + lean, height * 0.022f + 0.08f, matTrunk);
        var top = pos + Vector3.up * (height - crown * 0.7f) + lean;
        int blobs = Random.Range(5, 9);
        for (int i = 0; i < blobs; i++)
        {
            var off = Random.insideUnitSphere * crown * 0.65f;
            off.y *= 0.5f;
            var b = MeshObj("Leaves", t, blobMeshes[Random.Range(0, blobMeshes.Length)], matLeaves[Random.Range(0, matLeaves.Length)]);
            b.transform.position = top + off;
            b.transform.rotation = Random.rotation;
            float s = crown * Random.Range(0.55f, 0.85f);
            b.transform.localScale = new Vector3(s, s * 0.8f, s);
            if (i < 3) Rod("Branch", t, pos + Vector3.up * trunkH * 0.7f, top + off * 0.7f, height * 0.01f + 0.04f, matTrunk);
        }
    }

    static void BuildShrubs(Transform parent)
    {
        var sh = new GameObject("Shrubs").transform;
        sh.SetParent(parent);
        // Beds in front of the near corners and along the far wall
        var spots = new List<Vector3>();
        for (float x = -13.5f; x <= -9f; x += 0.9f) spots.Add(new Vector3(x, 0, -18.6f));
        for (float x = 9f; x <= 13.5f; x += 0.9f) spots.Add(new Vector3(x, 0, -18.6f));
        for (float x = -7f; x <= 7f; x += 1.0f) spots.Add(new Vector3(x, 0, 17.0f));
        for (float z = -16f; z <= 16f; z += 1.6f) { spots.Add(new Vector3(-14.4f, 0, z)); spots.Add(new Vector3(14.4f, 0, z)); }
        foreach (var p in spots)
        {
            var q = p + new Vector3(Random.Range(-0.3f, 0.3f), 0, Random.Range(-0.3f, 0.3f));
            var b = MeshObj("Shrub", sh, blobMeshes[Random.Range(0, 3)], matLeaves[Random.Range(0, 3)]);
            float s = Random.Range(0.7f, 1.2f);
            b.transform.position = q + Vector3.up * s * 0.3f;
            b.transform.localScale = new Vector3(s, s * 0.7f, s);
            b.transform.rotation = Quaternion.Euler(0, Random.Range(0, 360f), 0);
            if (Random.value < 0.4f)
            {
                var f = MeshObj("Flowers", sh, blobMeshes[0], matFlower);
                f.transform.position = q + new Vector3(0.2f, s * 0.55f, 0.1f);
                f.transform.localScale = Vector3.one * s * 0.3f;
            }
        }
    }

    // ---------- Lighting, sky, post, camera ----------

    static void BuildLightingAndCamera()
    {
        var lighting = new GameObject("Lighting").transform;

        // Warm, low golden-hour sun from behind-left of the camera
        var sunGo = new GameObject("Sun");
        sunGo.transform.SetParent(lighting);
        sunGo.transform.rotation = Quaternion.Euler(24f, 32f, 0f);
        var sun = sunGo.AddComponent<Light>();
        sun.type = LightType.Directional;
        sun.color = new Color(1.0f, 0.80f, 0.58f);
        sun.intensity = 2.2f;
        sun.shadows = LightShadows.Soft;
        sun.shadowStrength = 0.85f;
        RenderSettings.sun = sun;
        Sun = sun;

        // HDRI sky
        var imp = (TextureImporter)AssetImporter.GetAtPath(HdriPath);
        if (imp != null && imp.maxTextureSize != 4096)
        {
            imp.maxTextureSize = 4096;
            imp.mipmapEnabled = false;
            imp.wrapModeU = TextureWrapMode.Repeat;
            imp.wrapModeV = TextureWrapMode.Clamp;
            imp.SaveAndReimport();
        }
        var hdri = AssetDatabase.LoadAssetAtPath<Texture2D>(HdriPath);
        Material sky;
        if (hdri != null)
        {
            sky = new Material(Shader.Find("Skybox/Panoramic"));
            sky.SetTexture("_MainTex", hdri);
            sky.SetFloat("_Mapping", 1);
            sky.SetFloat("_ImageType", 0);
            sky.SetFloat("_Exposure", 1.05f);
            sky.SetFloat("_Rotation", 200f);
            sky.SetColor("_Tint", new Color(0.52f, 0.50f, 0.48f)); // tint is x2 in shader: slight warm
        }
        else
        {
            sky = new Material(Shader.Find("Skybox/Procedural"));
            sky.SetFloat("_SunSize", 0.03f);
            sky.SetFloat("_AtmosphereThickness", 1.3f);
        }
        AssetDatabase.CreateAsset(sky, GenDir + "/M_Sky_" + DateTime.Now.Ticks + ".mat");
        foreach (var old in AssetDatabase.FindAssets("M_Sky_", new[] { GenDir }))
        {
            string p = AssetDatabase.GUIDToAssetPath(old);
            if (p != AssetDatabase.GetAssetPath(sky)) AssetDatabase.DeleteAsset(p);
        }
        RenderSettings.skybox = sky;
        var allSkyDay = AssetDatabase.LoadAssetAtPath<Material>(DaySkyPath);
        if (allSkyDay != null) RenderSettings.skybox = allSkyDay; // day sky from the imported AllSky pack
        RenderSettings.ambientMode = AmbientMode.Skybox;
        RenderSettings.ambientIntensity = 1.1f;
        RenderSettings.defaultReflectionMode = DefaultReflectionMode.Skybox;
        RenderSettings.fog = true;
        RenderSettings.fogMode = FogMode.Linear;
        RenderSettings.fogColor = new Color(0.78f, 0.80f, 0.82f);
        RenderSettings.fogStartDistance = 60f;
        RenderSettings.fogEndDistance = 400f;
        DynamicGI.UpdateEnvironment();

        // Post-processing volume
        string volPath = GenDir + "/CourtVolumeProfile.asset";
        AssetDatabase.DeleteAsset(volPath);
        var profile = ScriptableObject.CreateInstance<VolumeProfile>();
        AssetDatabase.CreateAsset(profile, volPath);
        var bloom = profile.Add<Bloom>(true);
        bloom.intensity.Override(0.35f);
        bloom.threshold.Override(1.0f);
        bloom.scatter.Override(0.6f);
        var tone = profile.Add<Tonemapping>(true);
        tone.mode.Override(TonemappingMode.ACES);
        var ca = profile.Add<ColorAdjustments>(true);
        ca.postExposure.Override(0.2f);
        ca.contrast.Override(12f);
        ca.saturation.Override(8f);
        var wb = profile.Add<WhiteBalance>(true);
        wb.temperature.Override(6f);
        var vig = profile.Add<Vignette>(true);
        vig.intensity.Override(0.22f);
        var dof = profile.Add<DepthOfField>(true);
        dof.mode.Override(DepthOfFieldMode.Bokeh);
        dof.focusDistance.Override(8f);
        dof.focalLength.Override(55f);
        dof.aperture.Override(4f);
        dof.active = false; // the story director turns it on for close-ups
        foreach (var comp in profile.components) AssetDatabase.AddObjectToAsset(comp, profile);
        EditorUtility.SetDirty(profile);

        var volGo = new GameObject("PostProcessVolume");
        volGo.transform.SetParent(lighting);
        var vol = volGo.AddComponent<Volume>();
        vol.isGlobal = true;
        vol.sharedProfile = profile;
        PostVolume = vol;

        // Longer shadow distance for the whole court
        if (GraphicsSettings.currentRenderPipeline is UniversalRenderPipelineAsset urp)
        {
            urp.shadowDistance = 90f;
            urp.shadowCascadeCount = 4;
            urp.maxAdditionalLightsCount = 8;
            EditorUtility.SetDirty(urp);
        }

        // Main camera (render match) + overview camera (disabled)
        var cam = MakeCamera("Cam_Main", CamPos, CamRot, CamFov, true);
        cam.tag = "MainCamera";
        cam.AddComponent<AudioListener>();
        cam.AddComponent<ScreenshotKey>();
        MakeCamera("Cam_Overview", new Vector3(-26f, 22f, -30f), new Vector3(30f, 40f, 0f), 45f, false);
        MakeCamera("Cam_Courtside", new Vector3(0f, 1.7f, -12.5f), new Vector3(4f, 0f, 0f), 60f, false);
    }

    internal static GameObject MakeCamera(string name, Vector3 pos, Vector3 rot, float fov, bool active)
    {
        var go = new GameObject(name);
        go.transform.SetPositionAndRotation(pos, Quaternion.Euler(rot));
        var cam = go.AddComponent<Camera>();
        cam.fieldOfView = fov;
        cam.nearClipPlane = 0.1f;
        cam.farClipPlane = 1500f;
        cam.allowHDR = true;
        var data = go.AddComponent<UniversalAdditionalCameraData>();
        data.renderPostProcessing = true;
        data.antialiasing = AntialiasingMode.SubpixelMorphologicalAntiAliasing;
        data.antialiasingQuality = AntialiasingQuality.High;
        go.SetActive(active);
        return go;
    }

    internal static void Shoot(Camera cam, string file, int w, int h)
    {
        var rt = new RenderTexture(w, h, 24, RenderTextureFormat.ARGB32) { antiAliasing = 4 };
        var prev = cam.targetTexture;
        cam.targetTexture = rt;
        // First frames after a scene load can have uninitialized lighting/post history; render a few to warm up.
        for (int i = 0; i < 3; i++) cam.Render();
        var active = RenderTexture.active;
        RenderTexture.active = rt;
        var tex = new Texture2D(w, h, TextureFormat.RGB24, false);
        tex.ReadPixels(new Rect(0, 0, w, h), 0, 0);
        tex.Apply();
        File.WriteAllBytes(file, tex.EncodeToPNG());
        RenderTexture.active = active;
        cam.targetTexture = prev;
        Object.DestroyImmediate(tex);
        rt.Release();
        Object.DestroyImmediate(rt);
    }

    // ---------- Helpers ----------

    internal static GameObject Box(string name, Transform parent, Vector3 pos, Vector3 scale, Material m)
    {
        var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
        go.name = name;
        go.transform.SetParent(parent);
        go.transform.position = pos;
        go.transform.localScale = scale;
        go.GetComponent<Renderer>().sharedMaterial = m;
        return go;
    }

    internal static GameObject Rod(string name, Transform parent, Vector3 a, Vector3 b, float radius, Material m)
    {
        var go = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
        go.name = name;
        Object.DestroyImmediate(go.GetComponent<Collider>());
        go.transform.SetParent(parent);
        go.transform.position = (a + b) * 0.5f;
        go.transform.up = (b - a).normalized;
        go.transform.localScale = new Vector3(radius * 2, Vector3.Distance(a, b) * 0.5f, radius * 2);
        go.GetComponent<Renderer>().sharedMaterial = m;
        return go;
    }

    static GameObject MeshObj(string name, Transform parent, Mesh mesh, Material m)
    {
        var go = new GameObject(name);
        go.transform.SetParent(parent, false);
        go.AddComponent<MeshFilter>().sharedMesh = mesh;
        go.AddComponent<MeshRenderer>().sharedMaterial = m;
        return go;
    }

    static Mesh GetMesh(string name, Func<Mesh> make, bool rebuild = false)
    {
        string path = GenDir + "/" + name + ".asset";
        var existing = AssetDatabase.LoadAssetAtPath<Mesh>(path);
        if (existing != null && !rebuild) return existing;
        var mesh = make();
        mesh.name = name;
        if (existing != null)
        {
            EditorUtility.CopySerialized(mesh, existing);
            EditorUtility.SetDirty(existing);
            return existing;
        }
        AssetDatabase.CreateAsset(mesh, path);
        return mesh;
    }

    static Mesh TorusMesh(float R, float r, int seg, int side)
    {
        var v = new List<Vector3>();
        var tri = new List<int>();
        for (int i = 0; i <= seg; i++)
        {
            float a = i * Mathf.PI * 2 / seg;
            var c = new Vector3(Mathf.Cos(a), 0, Mathf.Sin(a));
            for (int j = 0; j <= side; j++)
            {
                float b = j * Mathf.PI * 2 / side;
                v.Add(c * (R + r * Mathf.Cos(b)) + Vector3.up * r * Mathf.Sin(b));
            }
        }
        for (int i = 0; i < seg; i++)
            for (int j = 0; j < side; j++)
            {
                int a = i * (side + 1) + j, b = a + side + 1;
                tri.AddRange(new[] { a, a + 1, b, b, a + 1, b + 1 });
            }
        var m = new Mesh();
        m.SetVertices(v);
        m.SetTriangles(tri, 0);
        m.RecalculateNormals();
        return m;
    }

    static Mesh ConeMesh(float rTop, float rBot, float h, int seg)
    {
        var v = new List<Vector3>();
        var uv = new List<Vector2>();
        var tri = new List<int>();
        for (int i = 0; i <= seg; i++)
        {
            float a = i * Mathf.PI * 2 / seg;
            var d = new Vector3(Mathf.Cos(a), 0, Mathf.Sin(a));
            v.Add(d * rTop); uv.Add(new Vector2(i / (float)seg, 1));
            v.Add(d * rBot + Vector3.down * h); uv.Add(new Vector2(i / (float)seg, 0));
        }
        for (int i = 0; i < seg; i++)
        {
            int a = i * 2;
            tri.AddRange(new[] { a, a + 2, a + 1, a + 1, a + 2, a + 3 });
        }
        var m = new Mesh();
        m.SetVertices(v);
        m.SetUVs(0, uv);
        m.SetTriangles(tri, 0);
        m.RecalculateNormals();
        return m;
    }

    // Molded plastic shell: curved seat pan + backrest, front facing +Z, pivot at the base.
    static Mesh SeatShellMesh()
    {
        // Profile (y, z) from the front lip back and up the backrest
        var prof = new[]
        {
            new Vector2(0.38f, 0.22f), new Vector2(0.42f, 0.19f), new Vector2(0.43f, 0.08f),
            new Vector2(0.415f, -0.06f), new Vector2(0.41f, -0.14f), new Vector2(0.45f, -0.21f),
            new Vector2(0.55f, -0.25f), new Vector2(0.68f, -0.27f), new Vector2(0.80f, -0.28f), new Vector2(0.84f, -0.27f)
        };
        const int nx = 9;
        const float hw = 0.24f, thick = 0.025f, cup = 0.045f;
        var v = new List<Vector3>();
        var tri = new List<int>();
        int np = prof.Length;

        Vector2 Normal(int i)
        {
            var t = prof[Mathf.Min(i + 1, np - 1)] - prof[Mathf.Max(i - 1, 0)];
            return new Vector2(-t.y, t.x).normalized; // (y, z): points up on the pan, forward on the back
        }

        for (int layer = 0; layer < 2; layer++)
            for (int i = 0; i < np; i++)
            {
                var n = Normal(i);
                for (int j = 0; j < nx; j++)
                {
                    float u = j / (float)(nx - 1) * 2 - 1;
                    float w = hw * (i == 0 || i == np - 1 ? 0.92f : 1f);
                    float off = cup * u * u - (layer == 1 ? thick : 0);
                    var p = prof[i] + n * off;
                    v.Add(new Vector3(u * w, p.x, p.y));
                }
            }
        int L = np * nx;
        for (int i = 0; i < np - 1; i++)
            for (int j = 0; j < nx - 1; j++)
            {
                int a = i * nx + j, b = a + nx;
                tri.AddRange(new[] { a, b, a + 1, a + 1, b, b + 1 });                 // top surface
                tri.AddRange(new[] { L + a, L + a + 1, L + b, L + a + 1, L + b + 1, L + b }); // underside
            }
        // Close the edges
        void Edge(int a0, int a1)
        {
            tri.AddRange(new[] { a0, L + a0, a1, a1, L + a0, L + a1 });
            tri.AddRange(new[] { a0, a1, L + a0, a1, L + a1, L + a0 });
        }
        for (int i = 0; i < np - 1; i++) { Edge(i * nx, (i + 1) * nx); Edge(i * nx + nx - 1, (i + 1) * nx + nx - 1); }
        for (int j = 0; j < nx - 1; j++) { Edge(j, j + 1); Edge((np - 1) * nx + j, (np - 1) * nx + j + 1); }

        var m = new Mesh();
        m.SetVertices(v);
        m.SetTriangles(tri, 0);
        m.RecalculateNormals();
        m.RecalculateBounds();
        return m;
    }

    // Lumpy icosphere used for tree canopies and shrubs.
    static Mesh BlobMesh(int seed)
    {
        var verts = new List<Vector3>();
        var cache = new Dictionary<long, int>();
        float t = (1f + Mathf.Sqrt(5f)) / 2f;
        foreach (var p in new[] {
            new Vector3(-1, t, 0), new Vector3(1, t, 0), new Vector3(-1, -t, 0), new Vector3(1, -t, 0),
            new Vector3(0, -1, t), new Vector3(0, 1, t), new Vector3(0, -1, -t), new Vector3(0, 1, -t),
            new Vector3(t, 0, -1), new Vector3(t, 0, 1), new Vector3(-t, 0, -1), new Vector3(-t, 0, 1) })
            verts.Add(p.normalized);
        var faces = new List<int> {
            0,11,5, 0,5,1, 0,1,7, 0,7,10, 0,10,11, 1,5,9, 5,11,4, 11,10,2, 10,7,6, 7,1,8,
            3,9,4, 3,4,2, 3,2,6, 3,6,8, 3,8,9, 4,9,5, 2,4,11, 6,2,10, 8,6,7, 9,8,1 };
        int Mid(int a, int b)
        {
            long key = a < b ? ((long)a << 32) + b : ((long)b << 32) + a;
            if (cache.TryGetValue(key, out int idx)) return idx;
            verts.Add(((verts[a] + verts[b]) * 0.5f).normalized);
            cache[key] = verts.Count - 1;
            return verts.Count - 1;
        }
        for (int s = 0; s < 3; s++)
        {
            var nf = new List<int>();
            for (int i = 0; i < faces.Count; i += 3)
            {
                int a = faces[i], b = faces[i + 1], c = faces[i + 2];
                int ab = Mid(a, b), bc = Mid(b, c), ca = Mid(c, a);
                nf.AddRange(new[] { a, ab, ca, b, bc, ab, c, ca, bc, ab, bc, ca });
            }
            faces = nf;
        }
        for (int i = 0; i < verts.Count; i++)
        {
            var d = verts[i];
            float n = Mathf.PerlinNoise(d.x * 1.6f + seed, d.y * 1.6f + d.z * 1.3f + seed * 0.37f);
            float n2 = Mathf.PerlinNoise(d.z * 4f + seed, d.x * 4f - d.y * 3f);
            var p = d * (0.75f + n * 0.4f + n2 * 0.12f);
            if (p.y < -0.2f) p.y = -0.2f + (p.y + 0.2f) * 0.5f;
            verts[i] = p;
        }
        // Reverse winding (icosphere faces above are clockwise from outside in Unity's convention check)
        for (int i = 0; i < faces.Count; i += 3) { int tmp = faces[i + 1]; faces[i + 1] = faces[i + 2]; faces[i + 2] = tmp; }
        var m = new Mesh();
        m.SetVertices(verts);
        m.SetTriangles(faces, 0);
        m.RecalculateNormals();
        return m;
    }
}
