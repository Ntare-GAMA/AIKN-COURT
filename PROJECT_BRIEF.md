# GAMA Court — Unity Project Brief

**Deadline: submission on 15 October 2026.** Speed matters more than polish. Cut scope before missing the date.

## Instructions for Claude (read first in every chat)
- Be direct and efficient. Explain in plain language first, then give technical detail.
- Unity 6 (6000.4.7f1 recommended), Universal 3D (URP) template. 1 Unity unit = 1 meter.
- Use the measurements in this file for all sizes. Do not substitute standard sizes unless asked.
- Prefer the fastest route that looks right. Suggest cuts when time is short.
- At the end of a chat, give a short handoff: what was decided, what is done, what is next.

## Goal
Rebuild an AI-rendered basketball court in Unity from scratch: colorful geometric floor, concrete walls behind both hoops, four colored stadium-seat sections on concrete tiers, trees, fence, lamp posts, warm golden-hour lighting. Reference images: the final render (`Colorful_Urban_Basketball_Court_with_Stadium_Seating.png`), the layout sketch, the blue seat photo, and `court_plan_from_render.png`.

## The four sections
| Section | Side | Position | Seat color |
|---|---|---|---|
| Imanzi | Left | Far end | Red |
| Jamaica | Left | Near end | Green |
| Imena | Right | Far end | Yellow |
| Intare | Right | Near end | Blue |

Each section: **4 rows x 10 seats** (40 seats, 160 total), molded plastic shell seats on stepped gray concrete. Section name on the concrete front face.

## Measurements (read from the render, +/- 10-15%)
| Item | Value |
|---|---|
| Court | 15 x 28 m |
| Walls (both ends) | 15 m wide (full baseline), 3.4 m high, 0.4 m thick, 2.2 m behind each baseline |
| Stand front | ~1.8 m outside the sideline |
| Section length | ~7.7 m |
| Seat spacing | 0.75 m (matches render; real seats are ~0.5 m) |
| Gap between sections on one side | ~3.9 m (with stairs) |
| Front concrete face | ~0.8 m tall |
| Rows | 4, about 0.85 m deep each, 0.4 m rise per row, ~3.4 m total depth |
| Hoop | Rim 3.05 m high; backboard 1.8 x 1.05 m |
| Lines | Lane 4.9 x 5.8 m, center and free-throw circles 1.8 m radius, 3-point arc 6.75 m, basket 1.575 m from baseline |

## Unity coordinates
Court center at the origin. X across the court, Z along it. Far baseline z = +14, near baseline z = -14.

| Object | Position (x, y, z) | Rotation Y | Scale |
|---|---|---|---|
| Court plane | (0, 0, 0) | 0 | (1.5, 1, 2.8) |
| Far wall | (0, 1.7, 16.2) | 0 | (15, 3.4, 0.4) |
| Near wall | (0, 1.7, -16.2) | 0 | (15, 3.4, 0.4) |
| Imanzi | (-9.3, 0, 8.4) | -90 | - |
| Jamaica | (-9.3, 0, -3.2) | -90 | - |
| Imena | (9.3, 0, 8.4) | 90 | - |
| Intare | (9.3, 0, -3.2) | 90 | - |

Section origin = front-center base of the stand. Rows climb away from the court.

## Court floor
`TEX_CourtFloor.png` (colors + lines) and `TEX_CourtLines.png` (lines only, transparent). 1800 x 3360 px, 120 px/m. URP/Lit material, Smoothness ~0.3, Max Size 4096.

## Seat generator script (`SeatSection.cs`)
Put on an empty object per section, assign the seat prefab, set the color, right-click the component > Build Section. Seat prefab: pivot at base, front facing +Z. If seats face the wrong way, change `180f` to `0f`.

```csharp
using UnityEngine;

public class SeatSection : MonoBehaviour
{
    [Header("Layout")]
    public GameObject seatPrefab;
    public Material concreteMaterial;
    public int rows = 4;
    public int seatsPerRow = 10;
    public float seatSpacing = 0.75f;
    public float rowDepth = 0.85f;
    public float riserHeight = 0.4f;
    public float baseHeight = 0.8f;

    [Header("Look")]
    public Color seatColor = Color.blue;

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

            var step = GameObject.CreatePrimitive(PrimitiveType.Cube);
            step.transform.SetParent(transform, false);
            step.transform.localScale = new Vector3(width, topY, rowDepth);
            step.transform.localPosition = new Vector3(0f, topY * 0.5f, r * rowDepth);
            if (concreteMaterial != null)
                step.GetComponent<Renderer>().sharedMaterial = concreteMaterial;

            for (int s = 0; s < seatsPerRow; s++)
            {
                var seat = Instantiate(seatPrefab, transform);
                seat.transform.localPosition = new Vector3(
                    (s - (seatsPerRow - 1) * 0.5f) * seatSpacing, topY, r * rowDepth);
                seat.transform.localRotation = Quaternion.Euler(0f, 180f, 0f);
                foreach (var rend in seat.GetComponentsInChildren<Renderer>())
                    rend.SetPropertyBlock(block);
            }
        }
    }
}
```

## Schedule
**13 Oct (today)** — Project setup and gray-box; floor material; simple hoops; seat prefab + `SeatSection` for all four sections; section name labels (TextMeshPro).
**14 Oct** — Terrain, trees, shrubs, fence, lamp posts, scoreboard; HDRI sky, warm sun, light Bloom; camera (behind near wall, ~4 m up, FOV ~45); screenshots; README.
**15 Oct** — Final check, last screenshots, export, submit.

**Cut if late:** night version, net physics, hill houses, distant stadium and tower, detailed documentation.

## Build log
- [x] Project created (Universal 3D), folders made
- [x] Gray-box: court, apron, two walls
- [x] Floor material applied
- [x] Hoops placed at both ends
- [x] Seat prefab made
- [x] Four sections built and colored
- [x] Section labels added
- [x] Terrain, trees, shrubs
- [x] Fence, lamp posts, scoreboard
- [x] Sky, sun, Bloom
- [x] Camera matched to render
- [x] Screenshots taken
- [x] README  
- [ ] Backup zip, submission

## Decisions
- Walls span the full 15 m baseline (render had ~9 m).
- Seat spacing 0.75 m to match the render.
- Editor: 6000.4.7f1 (later patch, fewer first-release bugs).
- Whole scene is generated by `Assets/GAMA/Editor/GamaCourtBuilder.cs` (menu GAMA Court > Build Everything). Tweak values there, rebuild.
- Camera at (0, 9, -25), pitch 19, FOV 50. The brief said ~4 m up, but a 3.4 m wall hides the court from that height.
- Section origin is the true front face (tiers offset by half a row in SeatSection).
- Trees, shrubs, seats are procedural low-poly placeholders; swap for Quaternius/Poly Haven models if time allows.
- Cut: night version, net physics, hill houses, distant stadium and tower.

## Asset register
| Asset | Source link | License | Credit needed? |
|---|---|---|---|
| Sky HDRI: Kloofendal 48d Partly Cloudy (Pure Sky) | https://polyhaven.com/a/kloofendal_48d_partly_cloudy_puresky | CC0 | No (credit appreciated) |
| LiberationSans font (TextMesh Pro essentials) | Unity com.unity.ugui package | SIL OFL 1.1 | No |
| All other meshes, materials, textures | Generated by GamaCourtBuilder.cs | Own work | No |

Free sources: Poly Haven (CC0), ambientCG (CC0), Kenney (CC0), Quaternius (CC0), Unity Asset Store (free filter), Sketchfab (check each license), OpenGameArt (check each license).
