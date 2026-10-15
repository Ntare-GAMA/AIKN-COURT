using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using TMPro;
using UnityEditor;
using UnityEditor.Animations;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.UI;
using Object = UnityEngine.Object;

// Builds the court (GamaCourtBuilder) and then the story layer on top: cast, outfits, ball, chairs,
// day/night, scoreboard, story director, free-play controller and UI.
// Menu: GAMA Court > Build Story Scene.
public static class GamaStoryBuilder
{
    const string CharDir = "Assets/GAMA/Characters";
    const string AnimDir = "Assets/GAMA/Animations";
    const string AudioDir = "Assets/GAMA/Audio";
    const string SuitPath = "Assets/genSuit/genSuit.fbx";
    const string GirlPath = "Assets/civilian_girl/Models/civilian_girl.FBX";
    const string BallPath = "Assets/Saritasa/Models/Sport_Balls/Basketball.prefab";

    static readonly Color KitRed = new Color(0.80f, 0.08f, 0.10f);
    static readonly Color KitBlack = new Color(0.06f, 0.06f, 0.07f);

    [MenuItem("GAMA Court/Build Story Scene", priority = 1)]
    public static void BuildStory()
    {
        Directory.CreateDirectory(CharDir);
        Directory.CreateDirectory(AudioDir + "/Lines");
        Directory.CreateDirectory(AudioDir + "/Music");
        Directory.CreateDirectory(AudioDir + "/SFX");
        GamaImportSetup.ConfigureAll();
        var ctrl = BuildController();

        GamaCourtBuilder.BuildEverything();
        var scene = EditorSceneManager.GetActiveScene();

        var story = new GameObject("Story").transform;
        var cast = new GameObject("Cast").transform;
        cast.SetParent(story);

        // --- Outfits ---
        var suitBlackD = Tex("Assets/genSuit/Textures/Black/genSuit_Black_D.tif", true);
        var kitRed = MakeKit("TEX_Kit_Red", suitBlackD, KitRed, 1.0f);
        var kitBlack = MakeKit("TEX_Kit_Black", suitBlackD, KitBlack, 0.55f);
        var matKitRed = SuitMat("M_Kit_Red", kitRed, "Black", 0.25f);
        var matKitBlack = SuitMat("M_Kit_Black", kitBlack, "Black", 0.3f);
        var matCoachA = SuitMat("M_Suit_Coach_Gatete", Tex("Assets/genSuit/Textures/Black/genSuit_Black_D.tif"), "Black");
        var matCoachB = SuitMat("M_Suit_Coach_Nshuti", Tex("Assets/genSuit/Textures/Blue/genSuit_Blue_D.tif"), "Blue");
        var matInvA = SuitMat("M_Suit_Investor_A", Tex("Assets/genSuit/Textures/Beige/genSuit_Beige_D.tif"), "Beige");
        var matInvB = SuitMat("M_Suit_Investor_B", Tex("Assets/genSuit/Textures/Striped/genSuit_Striped_D.tif"), "Striped");

        // --- Cast ---
        // Players: rigged characters from the NPC casual set (their own t-shirts, pants and sneakers)
        var kalisa = SpawnNpc("Kalisa", "npc_csl_00_character_01m_02", 1.83f, ctrl, cast);
        var mugisha = SpawnNpc("Mugisha", "npc_csl_00_character_01m_03", 1.86f, ctrl, cast);
        var gatete = SpawnSuit("Coach_Gatete", matCoachA, 1.78f, ctrl, cast);
        var nshuti = SpawnSuit("Coach_Nshuti", matCoachB, 1.76f, ctrl, cast);
        var invA = SpawnSuit("Investor_A", matInvA, 1.80f, ctrl, cast);
        var invB = SpawnSuit("Investor_B", matInvB, 1.77f, ctrl, cast);
        var woman = SpawnGirl("Ms_Uwase", 1.68f, ctrl, cast);
        kalisa.team = 1; mugisha.team = 2;
        // Downloaded kits and sneakers (the kits carry their own numbers, so no name labels)

        // --- Props ---
        var ball = SpawnBall(story);
        var chairG = Chair("Chair_Gatete", story, new Vector3(-8.45f, 0, 2.6f), 90f, KitRed);
        var chairN = Chair("Chair_Nshuti", story, new Vector3(8.45f, 0, 2.6f), -90f, new Color(0.12f, 0.12f, 0.14f));
        var intare = GameObject.Find("Section_Intare").transform;

        // --- Day / night ---
        var lighting = GameObject.Find("Lighting");
        var dn = lighting.AddComponent<GamaDayNight>();
        dn.sun = GamaCourtBuilder.Sun;
        dn.daySky = AssetDatabase.LoadAssetAtPath<Material>(GamaCourtBuilder.DaySkyPath);
        dn.nightSky = AssetDatabase.LoadAssetAtPath<Material>(GamaCourtBuilder.NightSkyPath);
        dn.lampLights = GamaCourtBuilder.LampLights.ToArray();
        dn.lampHeadMaterial = GamaCourtBuilder.LampMaterial;
        GamaWallArt.Build(dn);

        // --- Scoreboard ---
        var sbGo = GameObject.Find("Scoreboard");
        var sb = sbGo.AddComponent<GamaScoreboard>();
        sb.display = sbGo.GetComponentInChildren<TextMeshPro>();
        sb.homeName = "KALISA"; sb.awayName = "MUGISHA"; sb.Set(0, 0, "Q2", 300f);

        // --- Story camera ---
        var cam = GamaCourtBuilder.MakeCamera("Cam_Story", new Vector3(0, 9.5f, -24.5f), new Vector3(18, 0, 0), 48f, true);
        cam.transform.SetParent(story);
        cam.AddComponent<AudioListener>();
        var mainCam = GameObject.Find("Cam_Main");
        Object.DestroyImmediate(mainCam.GetComponent<ScreenshotKey>());

        // --- UI ---
        var ui = BuildUI(story);

        // --- Director ---
        var dirGo = new GameObject("StoryDirector");
        dirGo.transform.SetParent(story);
        var dir = dirGo.AddComponent<GamaStoryDirector>();
        dirGo.AddComponent<ScreenshotKey>();
        var inter = dirGo.AddComponent<GamaInteractive>();
        inter.enabled = false;
        inter.hud = ui.hud;
        inter.toast = ui.toast;
        var audio = dirGo.AddComponent<AudioSource>();
        audio.playOnAwake = false;
        audio.spatialBlend = 0;
        audio.clip = FindNarration();
        dir.lineClips = Enumerable.Range(1, 30).Select(i => FindLine(i)).ToArray();
        GamaAudioPrep.Wire(dir, dirGo);

        dir.kalisa = kalisa; dir.mugisha = mugisha; dir.coachGatete = gatete; dir.coachNshuti = nshuti;
        dir.investorA = invA; dir.investorB = invB; dir.woman = woman; dir.ball = ball;
        dir.seatInvestorA = intare.Find("Seat_R1_4");
        dir.seatWoman = intare.Find("Seat_R1_5");
        dir.seatInvestorB = intare.Find("Seat_R1_6");
        dir.chairGatete = chairG; dir.chairNshuti = chairN;
        dir.storyCam = cam.GetComponent<Camera>();
        dir.otherCams = new[] { mainCam.GetComponent<Camera>(), Find<Camera>("Cam_Overview"), Find<Camera>("Cam_Courtside") };
        dir.dayNight = dn; dir.scoreboard = sb; dir.interactive = inter; dir.narration = audio;
        dir.volume = GamaCourtBuilder.PostVolume;
        dir.barTop = ui.barTop; dir.barBottom = ui.barBottom; dir.fade = ui.fade;
        dir.subtitle = ui.subtitle; dir.title = ui.title; dir.hint = ui.hint;
        if (dir.kalisa.HasState("Jump Shot")) dir.shootState = "Jump Shot";

        // Pose everything at t = 4 s so the edit-mode scene looks alive, then reset to day.
        dir.EvaluateAt(4f);
        dn.Apply(0f, 0f);
        RenderSettings.skybox = dn.daySky;
        ui.canvas.gameObject.SetActive(true);
        cam.SetActive(false);
        mainCam.SetActive(true);

        EditorSceneManager.MarkSceneDirty(scene);
        EditorSceneManager.SaveScene(scene);
        AssetDatabase.SaveAssets();
        Debug.Log("[GAMA] Story scene built. Press Play.");
    }

    // Command line: -executeMethod GamaStoryBuilder.Batch
    public static void Batch()
    {
        BuildStory();
        RenderStoryFrames();
        RenderCloseups();
    }

    // ---------- Animator ----------

    static AnimatorController BuildController()
    {
        string path = AnimDir + "/GamaActor.controller";
        AssetDatabase.DeleteAsset(path);
        var ctrl = AnimatorController.CreateAnimatorControllerAtPath(path);
        var sm = ctrl.layers[0].stateMachine;
        foreach (var f in Directory.GetFiles(AnimDir, "*.fbx").OrderBy(x => x))
        {
            var clip = AssetDatabase.LoadAllAssetsAtPath(f.Replace('\\', '/')).OfType<AnimationClip>().FirstOrDefault(c => !c.name.StartsWith("__preview"));
            if (clip == null) continue;
            var st = sm.AddState(clip.name);
            st.motion = clip;
            if (clip.name == "Talking (1)") sm.defaultState = st;
        }
        AssetDatabase.SaveAssets();
        return ctrl;
    }

    // ---------- Characters ----------

    static GamaActor SpawnSuit(string name, Material mat, float height, AnimatorController ctrl, Transform parent)
    {
        var a = Spawn(name, SuitPath, height, ctrl, parent);
        foreach (var r in a.GetComponentsInChildren<SkinnedMeshRenderer>(true))
        {
            if (a.GetComponent<LODGroup>() == null && (r.name.EndsWith("LOD1") || r.name.EndsWith("LOD2")))
            {
                r.gameObject.SetActive(false);
                continue;
            }
            r.sharedMaterial = mat;
            r.updateWhenOffscreen = true;
        }
        return a;
    }

    // A complete NPC casual-set character: young look (no beard, dark hair, darker skin), URP materials.
    static GamaActor SpawnNpc(string name, string prefabName, float height, AnimatorController ctrl, Transform parent)
    {
        var prefab = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/npc_casual_set_00/Prefabs/" + prefabName + ".prefab");
        var go = (GameObject)PrefabUtility.InstantiatePrefab(prefab);
        PrefabUtility.UnpackPrefabInstance(go, PrefabUnpackMode.Completely, InteractionMode.AutomatedAction);
        go.name = name;
        go.transform.SetParent(parent);
        go.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
        GamaNpc.ToURP(go);
        foreach (var t in go.GetComponentsInChildren<Transform>(true))
            if (t.name.Contains("hairfacial")) t.gameObject.SetActive(false);
        var skin = new Color(0.52f, 0.36f, 0.27f);
        foreach (var r in go.GetComponentsInChildren<Renderer>(true))
        {
            var mats = r.sharedMaterials;
            for (int i = 0; i < mats.Length; i++)
            {
                if (mats[i] == null) continue;
                string n = mats[i].name;
                if (n.Contains("hmn_body") || n.Contains("hmn_face")) mats[i] = Tinted(mats[i], "Player_" + n, skin);
                else if (n.Contains("hair")) mats[i] = Tinted(mats[i], "Player_" + n, new Color(0.13f, 0.11f, 0.1f));
            }
            r.sharedMaterials = mats;
            if (r is SkinnedMeshRenderer smr) smr.updateWhenOffscreen = true;
        }
        float top = 0;
        foreach (var r in go.GetComponentsInChildren<Renderer>()) top = Mathf.Max(top, r.bounds.max.y);
        if (top > 0.1f) go.transform.localScale = Vector3.one * (height / top);
        var anim = go.GetComponentInChildren<Animator>();
        if (anim.gameObject != go)
        {
            var av = anim.avatar; Object.DestroyImmediate(anim);
            anim = go.AddComponent<Animator>(); anim.avatar = av;
        }
        anim.runtimeAnimatorController = ctrl;
        anim.applyRootMotion = false;
        anim.cullingMode = AnimatorCullingMode.AlwaysAnimate;
        var actor = go.AddComponent<GamaActor>();
        actor.anim = anim; actor.displayName = name;
        return actor;
    }

    static Material Tinted(Material src, string name, Color c)
    {
        string path = CharDir + "/M_" + name + ".mat";
        var m = AssetDatabase.LoadAssetAtPath<Material>(path);
        if (m == null) { m = new Material(src); AssetDatabase.CreateAsset(m, path); }
        else m.CopyPropertiesFromMaterial(src);
        m.SetColor("_BaseColor", c);
        EditorUtility.SetDirty(m);
        return m;
    }

    static GamaActor SpawnGirl(string name, float height, AnimatorController ctrl, Transform parent)
    {
        var a = Spawn(name, GirlPath, height, ctrl, parent);
        var formal = MakeFormal("TEX_Uwase_Formal", Tex("Assets/civilian_girl/Textures/body_Albedo.tga", true));
        var cache = new Dictionary<Material, Material>();
        foreach (var r in a.GetComponentsInChildren<Renderer>(true))
        {
            var mats = r.sharedMaterials;
            for (int i = 0; i < mats.Length; i++)
            {
                var src = mats[i];
                if (src == null) continue;
                if (!cache.TryGetValue(src, out var dst))
                {
                    dst = ToURP(src, CharDir + "/M_Uwase_" + src.name + ".mat", src.name == "body" ? formal : null);
                    cache[src] = dst;
                }
                mats[i] = dst;
            }
            r.sharedMaterials = mats;
            if (r is SkinnedMeshRenderer smr) smr.updateWhenOffscreen = true;
        }
        return a;
    }

    static GamaActor Spawn(string name, string modelPath, float height, AnimatorController ctrl, Transform parent)
    {
        var model = AssetDatabase.LoadAssetAtPath<GameObject>(modelPath);
        var go = (GameObject)PrefabUtility.InstantiatePrefab(model);
        PrefabUtility.UnpackPrefabInstance(go, PrefabUnpackMode.Completely, InteractionMode.AutomatedAction);
        go.name = name;
        go.transform.SetParent(parent);
        go.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);

        // Scale to a real-world height (measured in the bind pose)
        float top = 0;
        foreach (var r in go.GetComponentsInChildren<Renderer>()) top = Mathf.Max(top, r.bounds.max.y);
        if (top > 0.1f) go.transform.localScale = Vector3.one * (height / top);

        var anim = go.GetComponent<Animator>();
        if (anim == null) anim = go.AddComponent<Animator>();
        anim.avatar = AssetDatabase.LoadAllAssetsAtPath(modelPath).OfType<Avatar>().FirstOrDefault();
        anim.runtimeAnimatorController = ctrl;
        anim.applyRootMotion = false;
        anim.cullingMode = AnimatorCullingMode.AlwaysAnimate;

        var actor = go.AddComponent<GamaActor>();
        actor.anim = anim;
        actor.displayName = name;
        return actor;
    }

    // Number on chest and back, name above the back number. Parented to the chest bone so it moves with the body.
    static void Jersey(GamaActor a, string number, string surname, Color color)
    {
        var chest = a.anim.GetBoneTransform(HumanBodyBones.UpperChest) ?? a.anim.GetBoneTransform(HumanBodyBones.Chest) ?? a.anim.GetBoneTransform(HumanBodyBones.Spine);
        if (chest == null) return;
        var fwd = a.transform.forward;
        float depth = 0.15f;
        Label(chest, number, chest.position + fwd * depth + Vector3.down * 0.05f, -fwd, new Vector2(0.22f, 0.2f), color);
        Label(chest, number, chest.position - fwd * (depth + 0.01f) + Vector3.down * 0.08f, fwd, new Vector2(0.3f, 0.28f), color);
        Label(chest, surname, chest.position - fwd * (depth + 0.01f) + Vector3.up * 0.12f, fwd, new Vector2(0.34f, 0.07f), color);
    }

    static void Label(Transform bone, string text, Vector3 pos, Vector3 facing, Vector2 size, Color color)
    {
        var go = new GameObject("Jersey_" + text);
        go.transform.SetPositionAndRotation(pos, Quaternion.LookRotation(facing));
        go.transform.SetParent(bone, true);
        var tmp = go.AddComponent<TextMeshPro>();
        tmp.text = text;
        tmp.fontStyle = FontStyles.Bold;
        tmp.alignment = TextAlignmentOptions.Center;
        tmp.color = color;
        tmp.enableAutoSizing = true;
        tmp.fontSizeMin = 0.05f;
        tmp.fontSizeMax = 10f;
        tmp.rectTransform.sizeDelta = size / Mathf.Max(0.01f, go.transform.lossyScale.x);
        go.GetComponent<MeshRenderer>().shadowCastingMode = ShadowCastingMode.Off;
    }

    // ---------- Outfit textures ----------

    static Texture2D Tex(string path, bool readable = false)
    {
        var imp = (TextureImporter)AssetImporter.GetAtPath(path);
        if (readable && imp != null && !imp.isReadable)
        {
            imp.isReadable = true;
            imp.SaveAndReimport();
        }
        return AssetDatabase.LoadAssetAtPath<Texture2D>(path);
    }

    // Suit texture -> one-colour sportswear (top and bottoms), keeping skin, white trim and shoes.
    static Texture2D MakeKit(string name, Texture2D src, Color kit, float shadeScale)
    {
        return Recolor(name, src, (u, v, c) =>
        {
            bool shoe = (u > 0.30f && u < 0.58f && v > 0.83f && v < 0.94f) || (u > 0.85f && v > 0.17f && v < 0.39f);
            Color.RGBToHSV(c, out _, out float s, out float val);
            float L = c.r * 0.3f + c.g * 0.59f + c.b * 0.11f;
            bool skin = c.r > 0.28f && c.r > c.b + 0.1f && c.r >= c.g;
            bool white = L > 0.55f && s < 0.22f;
            if (shoe || skin || L < 0.015f) return c;
            if (white) return Color.Lerp(c, Color.white, 0.5f);
            float shade = Mathf.Clamp(L / 0.16f, 0.45f, 1.6f) * shadeScale;
            return new Color(kit.r * shade + 0.02f, kit.g * shade + 0.02f, kit.b * shade + 0.02f, 1);
        });
    }

    // Casual outfit -> formal: red tank top to an ivory blouse, jeans to navy trousers.
    static Texture2D MakeFormal(string name, Texture2D src)
    {
        return Recolor(name, src, (u, v, c) =>
        {
            Color.RGBToHSV(c, out _, out float s, out _);
            float L = c.r * 0.3f + c.g * 0.59f + c.b * 0.11f;
            bool boots = u < 0.27f && v > 0.72f;
            if (boots) return c;
            if (c.r > 0.22f && c.r > c.g * 1.6f && c.r > c.b * 1.5f)
            {
                float sh = Mathf.Clamp(L / 0.2f, 0.6f, 1.12f);
                return new Color(0.93f * sh, 0.90f * sh, 0.85f * sh, c.a);
            }
            if (u < 0.68f && v < 0.73f && s < 0.25f && L < 0.45f)
            {
                float sh = Mathf.Clamp(L / 0.17f, 0.5f, 1.5f);
                return new Color(0.09f * sh, 0.11f * sh, 0.19f * sh, c.a);
            }
            return c;
        });
    }

    static Texture2D Recolor(string name, Texture2D src, Func<float, float, Color, Color> f)
    {
        string path = CharDir + "/" + name + ".png";
        if (!File.Exists(path))
        {
            const int size = 2048;
            var dst = new Texture2D(size, size, TextureFormat.RGBA32, false);
            var px = new Color[size * size];
            for (int y = 0; y < size; y++)
                for (int x = 0; x < size; x++)
                {
                    float u = (x + 0.5f) / size, v = (y + 0.5f) / size;
                    px[y * size + x] = f(u, v, src.GetPixelBilinear(u, v));
                }
            dst.SetPixels(px);
            File.WriteAllBytes(path, dst.EncodeToPNG());
            Object.DestroyImmediate(dst);
            AssetDatabase.ImportAsset(path);
        }
        return AssetDatabase.LoadAssetAtPath<Texture2D>(path);
    }

    static Material SuitMat(string name, Texture2D albedo, string variant, float smooth = 0.3f)
    {
        var m = GamaCourtBuilder.Mat(name, Color.white, smooth, albedo);
        var nm = AssetDatabase.LoadAssetAtPath<Texture2D>("Assets/genSuit/Textures/" + variant + "/genSuit_" + variant.Replace(" ", "") + "_NM.tif");
        if (nm != null)
        {
            m.SetTexture("_BumpMap", nm);
            m.EnableKeyword("_NORMALMAP");
        }
        return m;
    }

    static Material ToURP(Material src, string path, Texture overrideAlbedo = null)
    {
        var m = AssetDatabase.LoadAssetAtPath<Material>(path);
        if (m == null)
        {
            m = new Material(Shader.Find("Universal Render Pipeline/Lit"));
            AssetDatabase.CreateAsset(m, path);
        }
        m.SetTexture("_BaseMap", overrideAlbedo != null ? overrideAlbedo : src.GetTexture("_MainTex"));
        m.SetColor("_BaseColor", src.HasProperty("_Color") ? src.GetColor("_Color") : Color.white);
        if (src.HasProperty("_BumpMap") && src.GetTexture("_BumpMap") != null)
        {
            m.SetTexture("_BumpMap", src.GetTexture("_BumpMap"));
            m.EnableKeyword("_NORMALMAP");
        }
        if (src.HasProperty("_MetallicGlossMap") && src.GetTexture("_MetallicGlossMap") != null)
        {
            m.SetTexture("_MetallicGlossMap", src.GetTexture("_MetallicGlossMap"));
            m.EnableKeyword("_METALLICSPECGLOSSMAP");
            m.SetFloat("_Smoothness", src.HasProperty("_GlossMapScale") ? src.GetFloat("_GlossMapScale") : 0.5f);
        }
        else m.SetFloat("_Smoothness", src.HasProperty("_Glossiness") ? src.GetFloat("_Glossiness") : 0.3f);
        if (src.HasProperty("_Mode") && src.GetFloat("_Mode") >= 1f) GamaCourtBuilder.SetCutout(m, true);
        EditorUtility.SetDirty(m);
        return m;
    }

    // ---------- Props ----------

    static Transform SpawnBall(Transform parent)
    {
        var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(BallPath);
        GameObject go;
        if (prefab != null)
        {
            go = (GameObject)PrefabUtility.InstantiatePrefab(prefab);
            PrefabUtility.UnpackPrefabInstance(go, PrefabUnpackMode.Completely, InteractionMode.AutomatedAction);
            foreach (var c in go.GetComponentsInChildren<Collider>()) Object.DestroyImmediate(c);
            foreach (var rb in go.GetComponentsInChildren<Rigidbody>()) Object.DestroyImmediate(rb);
            foreach (var r in go.GetComponentsInChildren<Renderer>())
                r.sharedMaterials = r.sharedMaterials.Select(s => s == null || s.shader.name.StartsWith("Universal") ? s : ToURP(s, CharDir + "/M_Ball_" + s.name + ".mat")).ToArray();
        }
        else
        {
            go = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            go.GetComponent<Renderer>().sharedMaterial = GamaCourtBuilder.Mat("M_BallFallback", new Color(0.9f, 0.4f, 0.1f), 0.4f);
        }
        go.name = "Basketball";
        go.transform.SetParent(parent);

        // Normalise to a 0.24 m ball, centred on its own pivot.
        var root = new GameObject("Ball").transform;
        root.SetParent(parent);
        var b = go.GetComponentsInChildren<Renderer>().Select(r => r.bounds).Aggregate((x, y) => { x.Encapsulate(y); return x; });
        float s = 0.24f / Mathf.Max(0.001f, Mathf.Max(b.size.x, b.size.y, b.size.z));
        go.transform.localScale *= s;
        b = go.GetComponentsInChildren<Renderer>().Select(r => r.bounds).Aggregate((x, y) => { x.Encapsulate(y); return x; });
        go.transform.position -= b.center;
        go.transform.SetParent(root, true);
        root.position = new Vector3(0, GamaBall.Radius, 0);
        root.gameObject.AddComponent<GamaBall>();
        return root;
    }

    // Courtside folding chair; its forward faces the court and the origin is the floor under the seat.
    static Transform Chair(string name, Transform parent, Vector3 pos, float yaw, Color color)
    {
        var root = new GameObject(name).transform;
        root.SetParent(parent);
        root.SetPositionAndRotation(pos, Quaternion.Euler(0, yaw, 0));
        var mat = GamaCourtBuilder.Mat("M_" + name, color, 0.5f);
        var metal = GamaCourtBuilder.Mat("M_DarkMetal", new Color(0.06f, 0.06f, 0.07f), 0.5f, null, null, 0.6f);
        Part(root, new Vector3(0, 0.45f, 0), new Vector3(0.5f, 0.05f, 0.46f), Vector3.zero, mat);
        Part(root, new Vector3(0, 0.78f, -0.25f), new Vector3(0.5f, 0.42f, 0.04f), new Vector3(-8, 0, 0), mat);
        foreach (var x in new[] { -0.22f, 0.22f })
            foreach (var z in new[] { -0.2f, 0.2f })
                Part(root, new Vector3(x, 0.22f, z), new Vector3(0.035f, 0.45f, 0.035f), Vector3.zero, metal);
        foreach (var x in new[] { -0.22f, 0.22f })
            Part(root, new Vector3(x, 0.66f, -0.24f), new Vector3(0.035f, 0.42f, 0.035f), new Vector3(-8, 0, 0), metal);
        return root;
    }

    static void Part(Transform root, Vector3 localPos, Vector3 scale, Vector3 euler, Material m)
    {
        var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
        go.transform.SetParent(root, false);
        go.transform.localPosition = localPos;
        go.transform.localRotation = Quaternion.Euler(euler);
        go.transform.localScale = scale;
        go.GetComponent<Renderer>().sharedMaterial = m;
    }

    static AudioClip FindLine(int i)
    {
        string name = "L" + i.ToString("00");
        var guid = AssetDatabase.FindAssets(name + " t:AudioClip", new[] { AudioDir + "/Lines" }).FirstOrDefault(g => Path.GetFileNameWithoutExtension(AssetDatabase.GUIDToAssetPath(g)) == name);
        return guid == null ? null : AssetDatabase.LoadAssetAtPath<AudioClip>(AssetDatabase.GUIDToAssetPath(guid));
    }

    static AudioSource Src(GameObject go, AudioClip clip)
    {
        var s = go.AddComponent<AudioSource>();
        s.playOnAwake = false; s.spatialBlend = 0; s.clip = clip;
        return s;
    }

    // First clip in Audio/<folder>, or the one whose file name is <name>.
    static AudioClip Clip(string folder, string name)
    {
        string dir = AudioDir + "/" + folder;
        if (!AssetDatabase.IsValidFolder(dir)) return null;
        var guid = AssetDatabase.FindAssets("t:AudioClip", new[] { dir }).FirstOrDefault(g => name == null || Path.GetFileNameWithoutExtension(AssetDatabase.GUIDToAssetPath(g)).ToLowerInvariant() == name);
        return guid == null ? null : AssetDatabase.LoadAssetAtPath<AudioClip>(AssetDatabase.GUIDToAssetPath(guid));
    }

    static AudioClip FindNarration()
    {
        var guid = AssetDatabase.FindAssets("t:AudioClip", new[] { AudioDir }).FirstOrDefault(g => Path.GetDirectoryName(AssetDatabase.GUIDToAssetPath(g)).Replace("\\", "/") == AudioDir);
        return guid == null ? null : AssetDatabase.LoadAssetAtPath<AudioClip>(AssetDatabase.GUIDToAssetPath(guid));
    }

    static T Find<T>(string name) where T : Component
    {
        var go = GameObject.Find(name);
        if (go == null) go = Resources.FindObjectsOfTypeAll<GameObject>().FirstOrDefault(g => g.name == name && g.scene.IsValid());
        return go != null ? go.GetComponent<T>() : null;
    }

    // ---------- UI ----------

    class UIRefs
    {
        public Canvas canvas;
        public RectTransform barTop, barBottom;
        public Image fade;
        public TextMeshProUGUI subtitle, title, hint, hud, toast;
    }

    static UIRefs BuildUI(Transform parent)
    {
        var r = new UIRefs();
        var go = new GameObject("StoryUI", typeof(Canvas), typeof(CanvasScaler));
        go.transform.SetParent(parent);
        r.canvas = go.GetComponent<Canvas>();
        r.canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        var scaler = go.GetComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920, 1080);
        scaler.matchWidthOrHeight = 0.5f;

        r.barTop = Panel(go.transform, "LetterboxTop", new Vector2(0, 1), new Vector2(1, 1), new Vector2(0, -125), Vector2.zero, Color.black).rectTransform;
        r.barBottom = Panel(go.transform, "LetterboxBottom", new Vector2(0, 0), new Vector2(1, 0), Vector2.zero, new Vector2(0, 125), Color.black).rectTransform;
        r.subtitle = Text(go.transform, "Subtitle", new Vector2(0.08f, 0), new Vector2(0.92f, 0), new Vector2(0, 8), new Vector2(0, 118), 34, FontStyles.Normal);
        r.title = Text(go.transform, "Title", new Vector2(0, 0.3f), new Vector2(1, 0.7f), Vector2.zero, Vector2.zero, 120, FontStyles.Bold);
        r.title.characterSpacing = 0;
        r.title.alpha = 0;
        r.hint = Text(go.transform, "SkipHint", new Vector2(0.6f, 1), new Vector2(0.98f, 1), new Vector2(0, -120), new Vector2(0, -130 + 120), 22, FontStyles.Normal);
        r.hint.alignment = TextAlignmentOptions.TopRight;
        r.hint.rectTransform.offsetMin = new Vector2(0, -175);
        r.hint.rectTransform.offsetMax = new Vector2(0, -132);
        r.hint.text = "Enter: skip to free play";
        r.hud = Text(go.transform, "HUD", new Vector2(0.02f, 0.8f), new Vector2(0.6f, 0.98f), Vector2.zero, Vector2.zero, 26, FontStyles.Normal);
        r.hud.alignment = TextAlignmentOptions.TopLeft;
        r.hud.gameObject.SetActive(false);
        r.toast = Text(go.transform, "Toast", new Vector2(0.1f, 0.22f), new Vector2(0.9f, 0.34f), Vector2.zero, Vector2.zero, 56, FontStyles.Bold);
        r.toast.color = new Color(1f, 0.75f, 0.1f);
        r.toast.gameObject.SetActive(false);
        r.fade = Panel(go.transform, "Fade", Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero, new Color(0, 0, 0, 0));
        r.fade.raycastTarget = false;
        return r;
    }

    static Image Panel(Transform parent, string name, Vector2 aMin, Vector2 aMax, Vector2 offMin, Vector2 offMax, Color c)
    {
        var go = new GameObject(name, typeof(RectTransform), typeof(Image));
        go.transform.SetParent(parent, false);
        var rt = go.GetComponent<RectTransform>();
        rt.anchorMin = aMin; rt.anchorMax = aMax; rt.offsetMin = offMin; rt.offsetMax = offMax;
        var img = go.GetComponent<Image>();
        img.color = c;
        return img;
    }

    static TextMeshProUGUI Text(Transform parent, string name, Vector2 aMin, Vector2 aMax, Vector2 offMin, Vector2 offMax, float size, FontStyles style)
    {
        var go = new GameObject(name, typeof(RectTransform));
        go.transform.SetParent(parent, false);
        var rt = go.GetComponent<RectTransform>();
        rt.anchorMin = aMin; rt.anchorMax = aMax; rt.offsetMin = offMin; rt.offsetMax = offMax;
        var t = go.AddComponent<TextMeshProUGUI>();
        t.fontSize = size;
        t.fontStyle = style;
        t.alignment = TextAlignmentOptions.Center;
        t.color = Color.white;
        t.raycastTarget = false;
        return t;
    }

    // ---------- Preview renders (no Play mode needed) ----------

    [MenuItem("GAMA Court/Render Story Frames", priority = 21)]
    public static void RenderStoryFrames()
    {
        var dir = Object.FindFirstObjectByType<GamaStoryDirector>();
        if (dir == null) { Debug.LogError("[GAMA] Build the story scene first."); return; }
        string outDir = Path.GetFullPath(Path.Combine(Application.dataPath, "..", "Screenshots", "story"));
        Directory.CreateDirectory(outDir);
        var canvas = Object.FindFirstObjectByType<Canvas>(FindObjectsInactive.Include);
        var oldSky = RenderSettings.skybox;
        var cam = dir.storyCam;
        cam.gameObject.SetActive(true);
        canvas.renderMode = RenderMode.ScreenSpaceCamera;
        canvas.worldCamera = cam;
        canvas.planeDistance = 0.5f;
        foreach (float t in new[] { 33f, 47.2f, 2f, 7f, 50f, 51.5f, 52.6f, 53.4f, 54.0f, 54.4f, 55.5f, 87f, 89f, 4f, 13f, 19f, 24f, 28f, 33f, 37.2f, 40f, 43f, 47.2f, 52f, 54.5f, 59f, 63f, 66f, 70f, 76f, 79f, 86f })
        {
            dir.EvaluateAt(t);
            Canvas.ForceUpdateCanvases();
            GamaCourtBuilder.Shoot(cam, Path.Combine(outDir, "t" + t.ToString("00.0") + ".png"), 1280, 720);
        }
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.worldCamera = null;
        cam.gameObject.SetActive(false);
        dir.dayNight.Apply(0f, 0f);
        RenderSettings.skybox = oldSky;
        Debug.Log("[GAMA] Story frames saved to " + outDir);
    }

    // Clean presentation renders for the design submission (no letterbox or subtitles).
    public static void BatchDesign()
    {
        BuildStory();
        RenderDesignShots();
    }

    [MenuItem("GAMA Court/Render Design Shots", priority = 22)]
    public static void RenderDesignShots()
    {
        var dir = Object.FindFirstObjectByType<GamaStoryDirector>();
        var cam = dir.storyCam;
        cam.gameObject.SetActive(true);
        string outDir = Path.GetFullPath(Path.Combine(Application.dataPath, "..", "Submission", "renders"));
        Directory.CreateDirectory(outDir);
        void Shot(string name, float t, Vector3 pos, Vector3 look, float fov, int w = 1920, int h = 1080, float ortho = 0)
        {
            dir.EvaluateAt(t);
            cam.transform.SetPositionAndRotation(pos, Quaternion.LookRotation(look - pos, Mathf.Abs((look - pos).normalized.y) > 0.99f ? Vector3.forward : Vector3.up));
            cam.fieldOfView = fov;
            cam.orthographic = ortho > 0;
            cam.orthographicSize = ortho;
            GamaCourtBuilder.Shoot(cam, Path.Combine(outDir, name + ".png"), w, h);
            cam.orthographic = false;
        }
        Shot("hero_day", 43f, new Vector3(0, 9f, -25f), new Vector3(0, 0.6f, 4f), 50f);
        Shot("hero_night", 70f, new Vector3(0, 9f, -25f), new Vector3(0, 0.6f, 4f), 50f);
        Shot("stand_day", 43f, new Vector3(4.6f, 2.4f, -10.2f), new Vector3(10.2f, 1.0f, -2.4f), 45f);
        Shot("stand_profile", 43f, new Vector3(11.0f, 1.6f, -14.6f), new Vector3(11.0f, 1.3f, -3.2f), 38f);
        Shot("courtside_night", 59f, new Vector3(-6.4f, 1.6f, -8.5f), new Vector3(0f, 1.2f, 6f), 55f);
        Shot("plan_top", 43f, new Vector3(0, 60f, 0.5f), new Vector3(0, 0, 0.5f), 30f, 1400, 2000, 19.5f);
        Shot("wall_swaniker", 43f, new Vector3(-1.5f, 1.7f, 6.5f), new Vector3(-0.6f, 1.7f, 16f), 50f);
        Shot("wall_kagame", 43f, new Vector3(1.5f, 1.7f, -6.5f), new Vector3(0.6f, 1.7f, -16f), 50f);
        Shot("wall_swaniker_night", 70f, new Vector3(-1.5f, 1.7f, 6.5f), new Vector3(-0.6f, 1.7f, 16f), 50f);
        Shot("led_day", 43f, new Vector3(-3.5f, 2.0f, -25.5f), new Vector3(0f, 1.9f, -16.4f), 48f);
        Shot("led_night", 70f, new Vector3(-3.5f, 2.0f, -25.5f), new Vector3(0f, 1.9f, -16.4f), 48f);
        cam.gameObject.SetActive(false);
        dir.dayNight.Apply(0f, 0f);
        RenderSettings.skybox = dir.dayNight.daySky;
        Debug.Log("[GAMA] Design shots saved to " + outDir);
    }

    // Close checks of the seated VIPs and the players (batch only).
    public static void RenderCloseups()
    {
        var dir = Object.FindFirstObjectByType<GamaStoryDirector>();
        var cam = dir.storyCam;
        cam.gameObject.SetActive(true);
        string outDir = Path.GetFullPath(Path.Combine(Application.dataPath, "..", "Screenshots", "story"));
        void Shot(float t, Func<Vector3> posF, Func<Vector3> lookF, string name)
        {
            dir.EvaluateAt(t);
            Vector3 pos = posF(), look = lookF();
            cam.transform.SetPositionAndRotation(pos, Quaternion.LookRotation(look - pos));
            cam.fieldOfView = 35f;
            GamaCourtBuilder.Shoot(cam, Path.Combine(outDir, name + ".png"), 1280, 720);
        }
        var w = dir.woman.transform;
        Shot(43f, () => w.position + new Vector3(-3.2f, 0.9f, -0.6f), () => w.position + Vector3.up * 0.9f, "_close_vips");
        Shot(43f, () => dir.chairGatete.position + new Vector3(3.0f, 1.2f, -0.8f), () => dir.chairGatete.position + Vector3.up * 0.8f, "_close_coach_sit");
        var m = dir.mugisha.transform;
        Shot(33f, () => m.position + m.forward * 3f + Vector3.up * 1.3f, () => m.position + Vector3.up * 1.2f, "_close_mugisha");
        cam.gameObject.SetActive(false);
        dir.dayNight.Apply(0f, 0f);
    }

    // One image per animation on Kalisa, to check retargeting and pick poses.
    public static void RenderPoseSheet()
    {
        var dir = Object.FindFirstObjectByType<GamaStoryDirector>();
        var a = dir.kalisa;
        var cam = dir.storyCam;
        cam.gameObject.SetActive(true);
        var states = a.anim.runtimeAnimatorController.animationClips.Select(c => c.name).Distinct().ToList();
        const int cell = 360, cols = 5;
        int rows = Mathf.CeilToInt(states.Count / (float)cols);
        var sheet = new Texture2D(cell * cols, cell * rows, TextureFormat.RGB24, false);
        string tmp = Path.Combine(Application.temporaryCachePath, "pose.png");
        var oldPos = a.transform.position;
        a.transform.SetPositionAndRotation(new Vector3(0, 0, -2f), Quaternion.Euler(0, 200f, 0));
        cam.transform.position = new Vector3(0, 1.1f, -6.2f);
        cam.transform.rotation = Quaternion.Euler(4f, 0, 0);
        cam.fieldOfView = 32f;
        for (int i = 0; i < states.Count; i++)
        {
            a.SeekState(states[i], 1.2f);
            GamaCourtBuilder.Shoot(cam, tmp, cell, cell);
            var img = new Texture2D(2, 2);
            img.LoadImage(File.ReadAllBytes(tmp));
            sheet.SetPixels((i % cols) * cell, (rows - 1 - i / cols) * cell, cell, cell, img.GetPixels());
            Object.DestroyImmediate(img);
            Debug.Log("[GAMA] pose " + i + " = " + states[i]);
        }
        string outDir = Path.GetFullPath(Path.Combine(Application.dataPath, "..", "Screenshots", "story"));
        Directory.CreateDirectory(outDir);
        File.WriteAllBytes(Path.Combine(outDir, "_poses.png"), sheet.EncodeToPNG());
        a.transform.position = oldPos;
        cam.gameObject.SetActive(false);
    }
}
