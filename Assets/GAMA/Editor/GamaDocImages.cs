using System.IO;
using UnityEditor;
using UnityEngine;

// Converts renders and field photos to resized JPEGs for the submission documents.
// Batch: -executeMethod GamaDocImages.Run
public static class GamaDocImages
{
    [MenuItem("GAMA Court/Prepare Submission Images", priority = 23)]
    public static void Run()
    {
        string root = Path.GetFullPath(Path.Combine(Application.dataPath, ".."));
        string outDir = Path.Combine(root, "Submission", "img");
        Directory.CreateDirectory(outDir);
        foreach (var f in Directory.GetFiles(Path.Combine(root, "Submission", "renders"), "*.png"))
            Convert(f, Path.Combine(outDir, Path.GetFileNameWithoutExtension(f) + ".jpg"), 1800, 86);
        foreach (var n in new[] { "image_01", "image_05", "image_09" })
            Convert(Path.Combine(root, "IMAGES FROM FIELD WORK", n + ".png"), Path.Combine(outDir, "site_" + n + ".jpg"), 1400, 80);
        Debug.Log("[GAMA] Submission images written to " + outDir);
    }

    static void Convert(string src, string dst, int maxW, int quality)
    {
        if (!File.Exists(src)) { Debug.LogWarning("[GAMA] Missing " + src); return; }
        var tex = new Texture2D(2, 2, TextureFormat.RGBA32, false);
        tex.LoadImage(File.ReadAllBytes(src));
        int w = Mathf.Min(maxW, tex.width), h = Mathf.RoundToInt(tex.height * (w / (float)tex.width));
        var rt = RenderTexture.GetTemporary(w, h, 0, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB);
        Graphics.Blit(tex, rt);
        var prev = RenderTexture.active;
        RenderTexture.active = rt;
        var outTex = new Texture2D(w, h, TextureFormat.RGB24, false);
        outTex.ReadPixels(new Rect(0, 0, w, h), 0, 0);
        outTex.Apply();
        RenderTexture.active = prev;
        RenderTexture.ReleaseTemporary(rt);
        File.WriteAllBytes(dst, outTex.EncodeToJPG(quality));
        Object.DestroyImmediate(tex);
        Object.DestroyImmediate(outTex);
    }
}
