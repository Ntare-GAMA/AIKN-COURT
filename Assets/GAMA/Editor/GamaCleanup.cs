using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;

// Lists (and optionally deletes) assets that neither the scene nor the GAMA build scripts use.
// Batch: -executeMethod GamaCleanup.List  /  GamaCleanup.Delete
public static class GamaCleanup
{
    static readonly string[] Roots =
    {
        "Assets/Scenes/GamaCourt.unity",
        "Assets/AllSkyFree/Cartoon Base BlueSky/Day_BlueSky_Nothing Equirect.mat",
        "Assets/AllSkyFree/Night MoonBurst/AllSky_Night_MoonBurst Equirect.mat",
        "Assets/Saritasa/Models/Sport_Balls/Basketball.prefab",
        "Assets/genSuit/genSuit.fbx",
        "Assets/civilian_girl/Models/civilian_girl.FBX",
        "Assets/npc_casual_set_00/Prefabs/npc_csl_00_character_01m_02.prefab",
        "Assets/npc_casual_set_00/Prefabs/npc_csl_00_character_01m_03.prefab",
        "Assets/InputSystem_Actions.inputactions",
    };
    // Kept whole: our own content, TMP, render settings, textures the builder reads by path, UPR twins of used materials.
    static readonly string[] KeepFolders = { "Assets/GAMA/", "Assets/TextMesh Pro/", "Assets/Settings/", "Assets/genSuit/Textures/" };

    static List<string> Unused()
    {
        var roots = Roots.Where(p => File.Exists(p)).ToList();
        var used = new HashSet<string>(AssetDatabase.GetDependencies(roots.ToArray(), true));
        foreach (var p in roots) used.Add(p);
        // URP twins of every npc material the used prefabs reference
        foreach (var m in used.Where(u => u.StartsWith("Assets/npc_casual_set_00/Materials/")).ToList())
        {
            var twin = "Assets/npc_casual_set_00/MaterialsUPR/" + Path.GetFileName(m);
            if (File.Exists(twin)) foreach (var d in AssetDatabase.GetDependencies(twin, true)) used.Add(d);
        }
        foreach (var g in AssetDatabase.FindAssets("", new[] { "Assets/GAMA" }))
            foreach (var d in AssetDatabase.GetDependencies(AssetDatabase.GUIDToAssetPath(g), true)) used.Add(d);
        return AssetDatabase.GetAllAssetPaths()
            .Where(p => p.StartsWith("Assets/") && !AssetDatabase.IsValidFolder(p))
            .Where(p => !used.Contains(p) && !KeepFolders.Any(k => p.StartsWith(k)))
            .Where(p => !p.StartsWith("Assets/Voice-Overs and Narrators/"))
            .OrderBy(p => p).ToList();
    }

    public static void List()
    {
        var u = Unused();
        File.WriteAllLines("Logs/unused_assets.txt", u);
        Debug.Log("[GAMA] unused assets: " + u.Count);
    }

    public static void Delete()
    {
        var u = Unused();
        var failed = new List<string>();
        AssetDatabase.DeleteAssets(u.ToArray(), failed);
        // remove folders left empty
        foreach (var dir in Directory.GetDirectories("Assets", "*", SearchOption.AllDirectories).OrderByDescending(d => d.Length))
            if (!Directory.EnumerateFileSystemEntries(dir).Any(e => !e.EndsWith(".meta"))) AssetDatabase.DeleteAsset(dir.Replace(Path.DirectorySeparatorChar, '/'));
        AssetDatabase.Refresh();
        Debug.Log("[GAMA] deleted " + (u.Count - failed.Count) + " assets, failed " + failed.Count);
    }
}
