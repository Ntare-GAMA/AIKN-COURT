using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEngine;

// One-off diagnostics for imported characters and animations (batch: -executeMethod GamaProbe.Run).
public static class GamaProbe
{
    public static void Run()
    {
        GamaImportSetup.ConfigureAll();
        var sb = new StringBuilder();
        foreach (var path in new[] { "Assets/genSuit/genSuit.fbx", "Assets/civilian_girl/Models/civilian_girl.FBX" })
        {
            var go = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            var avatar = AssetDatabase.LoadAllAssetsAtPath(path).OfType<Avatar>().FirstOrDefault();
            sb.AppendLine("== " + path + " avatar=" + (avatar ? avatar.name : "none") + " valid=" + (avatar && avatar.isValid) + " human=" + (avatar && avatar.isHuman));
            var inst = Object.Instantiate(go);
            var b = new Bounds(inst.transform.position, Vector3.zero);
            foreach (var r in inst.GetComponentsInChildren<Renderer>())
            {
                b.Encapsulate(r.bounds);
                sb.AppendLine("  renderer " + r.name + " mats=" + string.Join(",", r.sharedMaterials.Select(m => m ? m.name + "(" + m.shader.name + ")" : "null")));
            }
            sb.AppendLine("  bounds center=" + b.center + " size=" + b.size);
            var anim = inst.GetComponent<Animator>();
            if (anim && anim.isHuman)
                foreach (var hb in new[] { HumanBodyBones.Hips, HumanBodyBones.Head, HumanBodyBones.RightHand, HumanBodyBones.LeftFoot })
                    sb.AppendLine("  " + hb + " -> " + (anim.GetBoneTransform(hb) ? anim.GetBoneTransform(hb).name : "missing"));
            Object.DestroyImmediate(inst);
        }
        foreach (var f in Directory.GetFiles("Assets/GAMA/Animations", "*.fbx"))
        {
            var clip = AssetDatabase.LoadAllAssetsAtPath(f).OfType<AnimationClip>().FirstOrDefault(c => !c.name.StartsWith("__preview"));
            var av = AssetDatabase.LoadAllAssetsAtPath(f).OfType<Avatar>().FirstOrDefault();
            sb.AppendLine("clip " + Path.GetFileName(f) + " -> " + (clip ? clip.name + " len=" + clip.length.ToString("0.00") + " human=" + clip.humanMotion + " avgSpeed=" + clip.averageSpeed + " avgAngSpeed=" + clip.averageAngularSpeed.ToString("0.00") : "none") + " avatarHuman=" + (av && av.isHuman));
        }
        File.WriteAllText("Logs/gama_probe.txt", sb.ToString());
        Debug.Log("[GAMA] probe written");
    }
}

// Import settings for characters and Mixamo clips.
public static class GamaImportSetup
{
    public static void ConfigureAll()
    {
        SetHumanoid("Assets/genSuit/genSuit.fbx");
        SetHumanoid("Assets/civilian_girl/Models/civilian_girl.FBX");
        foreach (var f in Directory.GetFiles("Assets/GAMA/Animations", "*.fbx"))
            SetClip(f.Replace('\\', '/'));
    }

    static void SetHumanoid(string path)
    {
        var imp = AssetImporter.GetAtPath(path) as ModelImporter;
        if (imp == null || imp.animationType == ModelImporterAnimationType.Human) return;
        imp.animationType = ModelImporterAnimationType.Human;
        imp.avatarSetup = ModelImporterAvatarSetup.CreateFromThisModel;
        imp.importAnimation = false;
        imp.SaveAndReimport();
    }

    static void SetClip(string path)
    {
        var imp = AssetImporter.GetAtPath(path) as ModelImporter;
        if (imp == null) return;
        if (imp.animationType == ModelImporterAnimationType.Human && imp.userData == "gama2") return;
        imp.animationType = ModelImporterAnimationType.Human;
        imp.avatarSetup = ModelImporterAvatarSetup.CreateFromThisModel;
        imp.importAnimation = true;
        imp.SaveAndReimport();
        var clips = imp.defaultClipAnimations;
        foreach (var c in clips)
        {
            c.name = Path.GetFileNameWithoutExtension(path);
            c.loopTime = true;
            // Root rotation and XZ go to root motion, which the Animator discards (applyRootMotion off):
            // scripts steer the characters, clips only animate the body. Height is baked so feet stay planted.
            c.lockRootRotation = false;
            c.lockRootPositionXZ = false;
            c.lockRootHeightY = true;
            c.keepOriginalPositionY = true;
        }
        imp.clipAnimations = clips;
        imp.userData = "gama2";
        imp.SaveAndReimport();
    }
}
