using TMPro;
using UnityEngine;

// Drives the 3D scoreboard text (teams, score, quarter and clock).
public class GamaScoreboard : MonoBehaviour
{
    public TextMeshPro display;
    public string homeName = "GOLD";
    public string awayName = "BLACK";
    public int home, away;
    public string period = "Q1";
    public float clockSeconds = 600f;
    public bool final;

    public void Set(int h, int a, string p, float clock, bool isFinal = false)
    {
        home = h; away = a; period = p; clockSeconds = Mathf.Max(0, clock); final = isFinal;
        Refresh();
    }

    public void Refresh()
    {
        if (display == null) return;
        int m = Mathf.FloorToInt(clockSeconds / 60f), s = Mathf.FloorToInt(clockSeconds % 60f);
        string clock = final ? "FINAL" : period + "  " + m.ToString("00") + ":" + s.ToString("00");
        display.text =
            "<color=#F2B705>" + homeName + "</color>     <color=#E8E8E8>" + awayName + "</color>\n" +
            "<size=170%><color=#FFB000>" + home.ToString("00") + " : " + away.ToString("00") + "</color></size>\n" +
            "<size=80%><color=#FF6A1F>" + clock + "</color></size>";
    }
}
