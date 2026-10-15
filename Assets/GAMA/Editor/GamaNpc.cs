using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;


public static class GamaNpc
{
    // The set's prefabs use built-in materials; swap each for its URP twin in MaterialsUPR.
    public static void ToURP(GameObject go)
    {
        foreach (var r in go.GetComponentsInChildren<Renderer>(true))
        {
            var mats = r.sharedMaterials;
            for (int i = 0; i < mats.Length; i++)
            {
                if (mats[i] == null || mats[i].shader.name.StartsWith("Universal")) continue;
                var urp = AssetDatabase.LoadAssetAtPath<Material>("Assets/npc_casual_set_00/MaterialsUPR/" + mats[i].name + ".mat");
                if (urp != null) mats[i] = urp;
            }
            r.sharedMaterials = mats;
        }
    }
}
