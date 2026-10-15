using TMPro;
using UnityEngine;

// One stand section: stepped concrete tiers, rows of seats, and the section name on the front face.
// Origin = front-center base of the stand. Rows climb away from the court along local +Z.
public class SeatSection : MonoBehaviour
{
    [Header("Layout")]
    public GameObject seatPrefab;
    public Material concreteMaterial;
    public Material railMaterial;
    [Tooltip("Guardrail height above the top tier and along the open sides.")]
    public float guardHeight = 1.1f;
    public int rows = 4;
    public int seatsPerRow = 10;
    public float seatSpacing = 0.75f;
    public float rowDepth = 0.85f;
    public float riserHeight = 0.4f;
    public float baseHeight = 0.8f;
    [Tooltip("Seats sit toward the back of each step, leaving legroom in front.")]
    public float seatSetback = 0.12f;

    [Header("Look")]
    public Color seatColor = Color.blue;
    [Tooltip("Saved per-section material (survives scene reloads). If empty, a property block tints the seats.")]
    public Material seatMaterial;
    public string sectionName = "";
    public Color labelColor = new Color(0.93f, 0.93f, 0.9f);

    [ContextMenu("Build Section")]
    public void Build()
    {
        for (int i = transform.childCount - 1; i >= 0; i--)
            DestroyImmediate(transform.GetChild(i).gameObject);

        var block = new MaterialPropertyBlock();
        block.SetColor("_BaseColor", seatColor);

        float width = seatsPerRow * seatSpacing + 0.4f;

        for (int r = 0; r < rows; r++)
        {
            float topY = baseHeight + r * riserHeight;
            float rowZ = r * rowDepth + rowDepth * 0.5f;

            var step = GameObject.CreatePrimitive(PrimitiveType.Cube);
            step.name = "Tier" + r;
            step.transform.SetParent(transform, false);
            step.transform.localScale = new Vector3(width, topY, rowDepth);
            step.transform.localPosition = new Vector3(0f, topY * 0.5f, rowZ);
            if (concreteMaterial != null)
                step.GetComponent<Renderer>().sharedMaterial = concreteMaterial;

            if (seatPrefab == null) continue;
            for (int s = 0; s < seatsPerRow; s++)
            {
                var seat = Instantiate(seatPrefab, transform);
                seat.name = "Seat_R" + (r + 1) + "_" + (s + 1);
                seat.transform.localPosition = new Vector3(
                    (s - (seatsPerRow - 1) * 0.5f) * seatSpacing, topY, rowZ + seatSetback);
                seat.transform.localRotation = Quaternion.Euler(0f, 180f, 0f);
                foreach (var rend in seat.GetComponentsInChildren<Renderer>())
                {
                    if (seatMaterial != null) rend.sharedMaterial = seatMaterial;
                    else rend.SetPropertyBlock(block);
                }
            }
        }

        BuildGuardrails(width);

        if (!string.IsNullOrEmpty(sectionName))
            BuildLabel(width);
    }

    // 1.1 m guardrail along the back (top tier is 2.0 m above ground) and sloped rails on both open sides.
    void BuildGuardrails(float width)
    {
        float depth = rows * rowDepth;
        float top = baseHeight + (rows - 1) * riserHeight;
        float half = width * 0.5f - 0.05f;
        int posts = Mathf.Max(2, Mathf.CeilToInt(width / 1.5f) + 1);
        for (int i = 0; i < posts; i++)
        {
            float x = Mathf.Lerp(-half, half, i / (float)(posts - 1));
            Rod(new Vector3(x, top, depth - 0.06f), new Vector3(x, top + guardHeight, depth - 0.06f), 0.025f);
        }
        Rod(new Vector3(-half, top + guardHeight, depth - 0.06f), new Vector3(half, top + guardHeight, depth - 0.06f), 0.03f);
        Rod(new Vector3(-half, top + guardHeight * 0.5f, depth - 0.06f), new Vector3(half, top + guardHeight * 0.5f, depth - 0.06f), 0.02f);
        foreach (float sx in new[] { -half, half })
        {
            for (int r = 0; r < rows; r++)
            {
                float y = baseHeight + r * riserHeight;
                float z = r * rowDepth + rowDepth * 0.5f;
                Rod(new Vector3(sx, y, z), new Vector3(sx, y + guardHeight, z), 0.025f);
            }
            Rod(new Vector3(sx, baseHeight + guardHeight, rowDepth * 0.5f), new Vector3(sx, top + guardHeight, depth - 0.06f), 0.03f);
        }
    }

    void Rod(Vector3 a, Vector3 b, float radius)
    {
        var go = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
        go.name = "Guardrail";
        DestroyImmediate(go.GetComponent<Collider>());
        go.transform.SetParent(transform, false);
        go.transform.localPosition = (a + b) * 0.5f;
        go.transform.localRotation = Quaternion.FromToRotation(Vector3.up, b - a);
        go.transform.localScale = new Vector3(radius * 2f, Vector3.Distance(a, b) * 0.5f, radius * 2f);
        if (railMaterial != null) go.GetComponent<Renderer>().sharedMaterial = railMaterial;
    }

    void BuildLabel(float width)
    {
        var go = new GameObject("Label_" + sectionName);
        go.transform.SetParent(transform, false);
        // Front face of the first tier is at local z = 0; text faces -Z (toward the court).
        go.transform.localPosition = new Vector3(0f, baseHeight * 0.5f, -0.01f);

        var tmp = go.AddComponent<TextMeshPro>();
        tmp.text = sectionName.ToUpperInvariant();
        tmp.fontStyle = FontStyles.Bold;
        tmp.alignment = TextAlignmentOptions.Center;
        tmp.color = labelColor;
        tmp.characterSpacing = 4f;
        tmp.enableAutoSizing = true;
        tmp.fontSizeMin = 0.5f;
        tmp.fontSizeMax = 40f;
        tmp.rectTransform.sizeDelta = new Vector2(Mathf.Min(width * 0.45f, 3.5f), baseHeight * 0.55f);
        var mr = go.GetComponent<MeshRenderer>();
        mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
    }
}
