# Prompt log: Week 8, Claude web conversation 02

| | |
|---|---|
| Week | 8 |
| Tool | Claude (web, claude.ai) |
| Conversation | Web 02 |
| Topic | A 3D model of a red wax seal for the quest's secrets, written as an FBX file without 3D software |
| Date | 2026-11-21 |
| Prompts | 1 |

## Summary

A new conversation asked for a 3D model of a red wax seal, the seals that open the quest's secrets in the Unity AR app about the Academia Mihăileană. It needed the arms of Moldavia and the lettering "ACADEMIA MIHAILEANA" and "1835" in relief, and it had to meet Unity requirements for size, pivot, orientation, triangle count, normals and an embedded texture. No 3D software was available in Claude's workspace, so Claude built the mesh and texture procedurally in Python and wrote its own binary FBX exporter. It checked the file by reading it back and rendering previews, then delivered Seal.fbx and a preview sheet.

## Prompts

### Prompt 1

**Prompt:**

> I need a 3D model of a red wax seal for a Unity AR app about the Academia Mihăileană in Iași (opened in 1835 by Prince Mihail Sturdza of Moldavia). In the quest, players find these seals floating near them; each one opens a secret about the academy.
>
> Design:
> - A round, stamped seal of deep red sealing wax, about 6 cm across and 6–8 mm thick, with the irregular, slightly lumpy rim of wax that was poured and then pressed.
> - Pressed into the front, in relief: the arms of Moldavia (an aurochs head, seen from the front, with a six-pointed star between its horns), inside a ring of lettering "ACADEMIA MIHAILEANA" with "1835" below.
> - It's an interpretation inspired by the period, not a replica of a real seal.
> - The back is flat and plain.
> - The relief must read well at a small size on a phone screen: bold and clean, not fine detail.
>
> Technical requirements (for Unity):
> - One object named Seal, with one material named seal, and its texture embedded in the FBX (one base-colour texture, 1024 × 1024 is enough).
> - Size and units: real size in metres (about 0.06 m across).
> - Pivot: at the centre of the seal.
> - Orientation in Unity (Y up): the stamped front faces +Z, standing upright like a coin seen from the front.
> - Geometry: a closed mesh, under about 6,000 triangles. Model the relief in the mesh rather than using a normal map.
> - Normals: export them so Unity can use Normals = Import; keep the hard edges of the relief.
> - Colour: a wax red around RGB (140, 32, 28), slightly darker in the recesses and lighter on the raised parts.
>
> Export as a single FBX file.

**Attachments:** None

**What was done:**

- Checked the workspace and found no Blender or FBX SDK, so decided to build the model in Python (numpy, OpenCV, matplotlib, Pillow) and write the binary FBX directly.
- Wrote a Python port of the earcut polygon triangulation algorithm.
- Designed the relief in 2D: a frontal aurochs head with horns, ears, eyes and nostrils, a six-pointed star between the horns, an outer and an inner border ring, "ACADEMIA MIHAILEANA" along the top arc and "1835" along the bottom in DejaVu Serif Bold, and two small star separators. The design was rasterised and slightly thickened for legibility.
- Fixed a small gap where the horns met the head.
- Built the mesh. The relief outlines were simplified and given vertical walls, with flat tops raised 0.9 mm above a flat field. Around the field went a lumpy wax rim with overflow lobes, and the back was a flat disc.
- Reduced detail to bring the count from 5,998 triangles to a final 5,614.
- Computed per-corner normals with a 40-degree smoothing angle, so the relief keeps hard edges and the rim stays smooth.
- Created planar UVs for the front and a separate small UV island for the back. Wall UVs were nudged so the walls pick up the crevice and edge shading.
- Baked a 1024 x 1024 base-colour texture from the mesh heights: base wax red RGB (140, 32, 28), darker in the recesses, lighter on the raised parts, with crevice shading and slight mottling. Fine shading was later limited to the stamped area and the contrast was increased.
- Wrote a binary FBX 7.4 writer and exported one object "Seal" with one material "seal" and the texture embedded. The file is Y up with the front facing +Z, in centimetre units (UnitScaleFactor 1, so about 0.06 m in Unity with Convert Units on), with the pivot at the stamp centre and mid-thickness.
- Wrote a verification script that reads the FBX back. It confirmed a closed, consistently oriented mesh with outward normals, a size of 0.0594 x 0.0592 x 0.0067 m and the embedded texture, and rendered previews from the file's data.
- Found and fixed a bug where overlapping shapes cancelled each other (horn/head/ear notches) by filling each shape separately, then re-exported and re-verified.
- Explained the Unity import settings (Convert Units on, Normals = Import, Extract Textures if needed). Noted that a default Unity camera looks along +Z and would see the back. Noted that the lettering is only just legible at about 180 px and suggested larger or shorter text if seals will appear smaller.
- Said that the FBX was written by a custom exporter and had not been tested in Unity itself.

**Files created or changed:** Seal.fbx, Seal_preview.png. Working files, not delivered: earcut.py, design.py, build.py, tex.py, fbx.py, export.py, verify.py (scripts); design.png, seal_basecolor.png, render_front.png, render_three_quarter.png, render_back.png, render_edge.png, preview_sheet.png, preview_phone_180px.png, closeup.png, mask_zoom.png (images).

## Outputs used in the project

| Output | Made in prompt | Notes |
|---|---|---|
| Seal.fbx | 1 | Final model with embedded texture; delivered |
| Seal_preview.png | 1 | Preview sheet rendered from the FBX (front, three-quarter, back, edge); delivered |
| seal_basecolor.png | 1 | Base-colour texture; embedded in Seal.fbx, not delivered separately |
| earcut.py, design.py, build.py, tex.py, fbx.py, export.py, verify.py | 1 | Generator and verification scripts; kept in Claude's workspace, not delivered |
