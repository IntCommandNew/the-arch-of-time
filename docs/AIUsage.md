# AI Usage Acknowledgement

This document states how AI tools were used to create **The Arch of Time**. It is updated whenever
the way AI is used in the project changes.

_Last updated: 1 November 2026_

## Tools

| Tool | Provider | Used for |
|---|---|---|
| **Claude** (web, claude.ai) | Anthropic | Creating the 3D model of the academy, drawing the storyboard images and writing the first version of the code |
| **Claude Code** (command-line interface) | Anthropic | Writing code from the Place step on, and part of the Unity Editor work |

## How AI was used

| Area | Tool | What AI did |
|---|---|---|
| 3D model of the academy | Claude (web) | Built the 3D model of the Academia Mihăileană used in the app from scratch, using references provided by the author: two phone scans of the academy's 3D replica from a museum (made by a third person) and the author's own photos of that replica. The scans were references only; they are not part of the model. A second iteration (also Claude web) fixed overlapping walls on the octagonal tower, updated the figure, levelled the grass and gravel, and added a texture to the base. The material settings (e.g. how shiny each surface is) were set by the author |
| Storyboard images | Claude (web) | Drew the ten storyboard images in [MainModules.md](MainModules.md) (`docs/storyboard/`) with a Python script, following the author's description of the app. They are abstract sketches of the key moments, not final designs |
| Code (first version) | Claude (web) | Wrote the app's first C# scripts: AR startup and permissions, app state machine, UI, Geospatial status overlay |
| Code (Place step) | Claude Code | Wrote the placement code (reticle on the ground, "Place here", anchoring the model) and extended the state machine and UI for it |
| Code (Adjust and Explore steps) | Claude Code | Wrote the adjust code (drag and twist gestures, move / turn / size / height controls, size presets, compass bearing of the facade) and the explore screen, and extended the state machine and UI for them |
| Code (saving and restoring) | Claude Code | Wrote the save / restore code (cloud anchor hosting and resolving, directions to the saved spot from the location, the save file on the phone, the "walk around the spot" quality meter, the save and restore screens) |
| Code (arch pieces test mode) | Claude Code | Wrote the first version of collecting the arch pieces: a placeholder piece spawned at a random spot around the user, directions to it, aiming with the centre of the screen, collecting with a counter (5 pieces, remembered), and its screen |
| Code (see-through while adjusting) | Claude Code | Wrote the semi-transparent ("See-through") look of the building while adjusting, in the building's shader, and its on/off button; and the Cancel button that undoes an edit |
| Code (footprint outline) | Claude Code | Wrote the footprint outline shown while adjusting: an Editor tool that cuts the building's outline out of the 3D model, the outline shader and the script that shows it |
| Code (hidden by real objects) | Claude Code | Wrote the optional depth-occlusion switch ("Hidden by real objects") on the explore screen |
| Code (fade near the camera) | Claude Code | Wrote the near-camera fade: a copy of Unity's URP Lit shader with a dissolve near the camera, and the script that sets its distance |
| Code (light and shadow) | Claude Code | Wrote the ARCore light estimation code and the shadow on the real ground (including the shadow-catcher shader) |
| Code (rendering fixes) | Claude Code | Wrote the lighting code (neutral ambient light, shadow range fitted to the model), hiding of the detected planes, and UI contrast changes |
| Unity Editor work | Claude Code | Did part of the project setup and configuration in the Unity Editor |

## Human role

All AI work was done under the administration and validation of the project author:

- the author decided what was built and how, and directed every task;
- the author reviewed, tested and validated the results (including testing the app on a phone);
- the author is responsible for the final content of the project.

## Future updates

This document will be updated as the project evolves, for example when:

- new 3D models are created with AI;
- AI is used in other areas of the project;
- AI features are added to the app itself (such as the planned AI narrator).

## Change log

| Date | Change |
|---|---|
| 30 October 2026 | First version: academy model and code (Claude web), part of the Unity Editor work (Claude Code). |
| 30 October 2026 | Place step code written with Claude Code. |
| 30 October 2026 | Rendering fixes (lighting, shadows, plane visuals, button contrast) made with Claude Code. |
| 31 October 2026 | Adjust and Explore steps written with Claude Code. |
| 31 October 2026 | Saving and restoring the placement written with Claude Code (revised twice after phone tests; the restore approach, camera recognition only with the location used for directions, was the author's decision). |
| 31 October 2026 | Light estimation and the shadow on the real ground written with Claude Code (light fixes after the phone test). |
| 31 October 2026 | Fade near the camera written with Claude Code (widened after the phone test); the start light now remembers the last light level. |
| 31 October 2026 | Optional "Hidden by real objects" (depth occlusion) written with Claude Code. |
| 31 October 2026 | Footprint outline while adjusting written with Claude Code. |
| 1 November 2026 | See-through building while adjusting, and a Cancel button for edits, written with Claude Code. |
| 1 November 2026 | Arch pieces test mode (spawn, find, aim, collect) written with Claude Code; the remembered light level made robust (averaged, never the last moments before closing). |
| 1 November 2026 | Clarified how the academy model was made: built from scratch by Claude (web) from the author's references (two phone scans of a museum replica and photos). |
| 1 November 2026 | Second iteration of the academy model made with Claude (web): tower overlap fixed, figure updated, ground levelled, textured base. Swapped into the app with Claude Code. |
| 1 November 2026 | Recorded the storyboard images in MainModules.md, drawn by Claude (web). |
| 1 November 2026 | Code clean-up with Claude Code: comments and files use keyboard characters only, simpler comment formatting, consistent code style. |
