using System.IO;
using UnityEngine;
using UnityEngine.InputSystem;

// Press F12 in Play mode to save a screenshot to <project>/Screenshots.
public class ScreenshotKey : MonoBehaviour
{
    public int superSize = 1;

    void Update()
    {
        if (Keyboard.current == null || !Keyboard.current.f12Key.wasPressedThisFrame) return;
        string dir = Path.Combine(Application.dataPath, "..", "Screenshots");
        Directory.CreateDirectory(dir);
        string file = Path.Combine(dir, "play_" + System.DateTime.Now.ToString("yyyyMMdd_HHmmss") + ".png");
        ScreenCapture.CaptureScreenshot(file, superSize);
        Debug.Log("Saved " + file);
    }
}
