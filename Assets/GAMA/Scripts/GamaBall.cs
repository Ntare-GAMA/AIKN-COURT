using UnityEngine;

// Pure functions for ball motion so the story can be scrubbed and the game mode can reuse them.
public class GamaBall : MonoBehaviour
{
    public const float Radius = 0.12f;
    public const float FlightTime = 1.15f;
    public static readonly Vector3 FarRim = new Vector3(0f, 3.05f, 12.425f);
    public static readonly Vector3 NearRim = new Vector3(0f, 3.05f, -12.425f);

    // Bounce between the floor and just under the dribbling hand.
    public static Vector3 Dribble(GamaActor a, float t)
    {
        var hand = a.HandPos;
        var p = hand + a.transform.forward * 0.18f + a.transform.right * 0.08f;
        float top = Mathf.Max(Radius + 0.3f, hand.y - 0.12f);
        float k = Mathf.Abs(Mathf.Sin(t * Mathf.PI / 0.62f));
        p.y = Mathf.Lerp(Radius, top, 1f - (1f - k) * (1f - k));
        return p;
    }

    // Arc from release to the rim. u in [0, 1].
    public static Vector3 Arc(Vector3 from, Vector3 to, float u, float apexExtra = 1.6f)
    {
        var p = Vector3.Lerp(from, to, u);
        float apex = Mathf.Max(from.y, to.y) + apexExtra - Mathf.Lerp(from.y, to.y, u);
        p.y += 4f * apex * u * (1f - u) * 0.9f;
        return p;
    }

    public static Vector3 ShotTarget(Vector3 rim, bool made, Vector3 shooterPos)
    {
        if (made) return rim + Vector3.up * 0.06f;
        var away = (rim - shooterPos); away.y = 0;
        return rim + away.normalized * 0.24f + Vector3.up * 0.05f; // clanks off the back rim
    }

    // After the rim: drop through (or off) and bounce to rest. s = seconds since rim.
    public static Vector3 AfterRim(Vector3 rim, bool made, Vector3 shooterPos, float s)
    {
        var away = rim - shooterPos; away.y = 0; away.Normalize();
        var rest = new Vector3(rim.x, Radius, rim.z) + (made ? -away * 0.6f : away * 1.6f) + Vector3.right * 0.4f;
        float drift = Mathf.Clamp01(s / 2.2f);
        var p = Vector3.Lerp(new Vector3(rim.x, 0, rim.z), new Vector3(rest.x, 0, rest.z), 1f - (1f - drift) * (1f - drift));
        // Bounces: first drop from rim height, then decaying hops.
        float h0 = rim.y;
        float g = 9.81f;
        float tFall = Mathf.Sqrt(2f * h0 / g);
        float y;
        if (s < tFall) y = h0 - 0.5f * g * s * s;
        else
        {
            float v = g * tFall * 0.55f, tt = s - tFall;
            y = 0;
            for (int i = 0; i < 4; i++)
            {
                float dur = 2f * v / g;
                if (tt < dur) { y = v * tt - 0.5f * g * tt * tt; break; }
                tt -= dur; v *= 0.5f;
            }
        }
        p.y = Mathf.Max(Radius, y + Radius);
        return p;
    }

    public static Vector3 RestPoint(Vector3 rim, bool made, Vector3 shooterPos) => AfterRim(rim, made, shooterPos, 10f);

    public static int PointsFrom(Vector3 shooterPos, Vector3 rim)
    {
        var d = shooterPos - rim; d.y = 0;
        return d.magnitude > 6.75f ? 3 : 2;
    }
}
