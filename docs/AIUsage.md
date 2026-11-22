# AI Usage Acknowledgement

This document states how AI tools were used to create **The Arch of Time**. It is updated whenever
the way AI is used in the project changes.

_Last updated: 22 November 2026_

## Tools

| Tool | Provider | Used for |
|---|---|---|
| **Claude** (web, claude.ai) | Anthropic | Creating the 3D model of the academy, drawing the storyboard images and writing the first version of the code |
| **Claude Code** (command-line interface) | Anthropic | Writing code from the Place step on, and part of the Unity Editor work |
| **ElevenLabs** (text to speech, model Eleven v4) | ElevenLabs | The recorded voices of the narrator and of Mihail Sturdza |

## How AI was used

| Area | Tool | What AI did |
|---|---|---|
| 3D model of the academy | Claude (web) | Built the 3D model of the Academia Mihăileană used in the app from scratch, using references provided by the author: two phone scans of the academy's 3D replica from a museum (made by a third person) and the author's own photos of that replica. The scans were references only; they are not part of the model. A second iteration (also Claude web) fixed overlapping walls on the octagonal tower, updated the figure, levelled the grass and gravel, and added a texture to the base. A third iteration (also Claude web) separated the grass and gravel from the base and made the building sit directly on it. A fourth iteration (also Claude web) changed some colours and some geometry and added bevels to the edges. The material settings (e.g. how shiny each surface is) were set by the author |
| 3D model of the arch | Claude (web), Claude Code | Claude (web) built the 3D model of the academy's arch (the front with four columns, the attic with three medallions). Claude (web) also split it into the quest's four pieces (the two columns, the arc and the top beam). Claude Code brought it into the project (texture and material) and checked both versions |
| 3D model of the wax seal | Claude (web), Claude Code | Claude (web) built the academy's wax seal (an aurochs head with a star, "ACADEMIA MIHAILEANA" and 1835, an interpretation inspired by the period). Claude Code brought it into the app (the seal that floats near the player) and made the interface's seals a picture rendered from it |
| Storyboard images | Claude (web) | Drew the ten storyboard images in [MainModules.md](MainModules.md) (`docs/storyboard/`) with a Python script, following the author's description of the app. They are abstract sketches of the key moments, not final designs |
| Code (first version) | Claude (web) | Wrote the app's first C# scripts: AR startup and permissions, app state machine, UI, Geospatial status overlay |
| Code (Place step) | Claude Code | Wrote the placement code (reticle on the ground, "Place here", anchoring the model) and extended the state machine and UI for it |
| Code (Adjust and Explore steps) | Claude Code | Wrote the adjust code (drag and twist gestures, move / turn / size / height controls, size presets, compass bearing of the facade) and the explore screen, and extended the state machine and UI for them |
| Code (saving and restoring) | Claude Code | Wrote the save / restore code (cloud anchor hosting and resolving, directions to the saved spot from the location, the save file on the phone, the "walk around the spot" quality meter, the save and restore screens). Removed on 8 November 2026, when saving placements was dropped from the app |
| Code (arch pieces test mode) | Claude Code | Wrote the first version of collecting the arch pieces: a placeholder piece spawned at a random spot around the user, directions to it, aiming with the centre of the screen, collecting with a counter (5 pieces, remembered), and its screen |
| Code (modes and new interface) | Claude Code | Wrote the mode selection (In Iași / Anywhere) with separate saved progress for each mode, built collecting the 4 arch pieces into the Anywhere mode, redesigned the whole interface (colours, the IM Fell English title font, an arch emblem showing the collected pieces, drawn after the arch replica in Parcul Expoziției) following Anthropic's frontend-design guidance, added pinch-to-resize, removed the save and restore code, and changed the real size to 1:100 |
| Code (placing the city targets) | Claude Code | Set up Google's Geospatial Creator in the Unity Editor (with Cesium for Unity) and wrote an editor tool that creates the scene where the author places the academy and the arch pieces on the map, and exports their positions for the app. When Google stopped serving its 3D city map to accounts in the European Economic Area, moved that scene to Cesium ion's map (terrain, aerial imagery and OpenStreetMap buildings) and removed the Google map |
| Code (In Iași mode) | Claude Code | Wrote the In Iași mode: the list of targets, guidance with the camera off (distance and compass direction from far away, a dial with warmer / colder nearby), and, at the spot, placing the academy or the arch piece at the position the author chose, once the phone knows its place precisely enough; designed its screens following Anthropic's frontend-design guidance |
| Quest texts | Claude Code | Wrote the quest's texts in English and Romanian (the greeting, riddles, knowledge cards, quiz questions, secrets, the finale and Sturdza's letter) from the author's historical text and from web sources (Wikipedia, OpenStreetMap for the places' coordinates), listed in [docs/sources](sources/ResearchNotes.md); the author reviews them |
| Code (languages, narrator, quest progress) | Claude Code | Wrote the English / Romanian text system, the narrator voice (Android's text-to-speech), the loading of the quest's texts and the saved quest progress (stops, knowledge, quizzes, seals, scores) |
| Code (the quest) | Claude Code | Wrote the quest's flow in both modes (greeting, seals and secrets, riddles, knowledge cards, the animated quiz, the progress view, the score, a simple finale), the English / Romanian interface with a language screen, and designed the new screens following Anthropic's frontend-design guidance |
| Code (the arch by the academy) | Claude Code | Wrote the arch standing by the academy: in the Anywhere mode it shows the quest's progress (missing pieces as see-through ghosts, a collected piece lifting off and landing solid), with its own outline while adjusting; in the In Iași mode it is a ghost until the finale. Wrote the finale's time lapse on it (the arch rises, crumbles, the academy dissolves and both come back), with a per-object dissolve added to the academy's shader. Made the collectable pieces the arch model's own pieces |
| Interface redesign | Claude Code | Redesigned the whole interface from a written design brief (docs/Design.md): Iași's printed matter of 1835 to 1858 as the visual world, light paper and ink, Bodoni Moda type, the arch drawn as an engraving, the seals as wax, the map as an engraved plan; used Anthropic's frontend-design guidance and the open-source ux-designer skill (szilu/ux-designer-skill, MIT) and ideas from the impeccable skill (pbakaus/impeccable) |
| Narrator voices | ElevenLabs, Claude Code | The two voices (Mihail Sturdza, and a woman narrator) were designed by the author with ElevenLabs' Voice Design, from written descriptions suggested by Claude Code. ElevenLabs' text-to-speech (Eleven v4) recorded every spoken line of the quest with them, in English and Romanian (about 24 minutes); Android's own voice remains only as a fallback for a line without a recording. Claude Code wrote the Editor tool that records the lines and the code that plays them |
| Quest map | Claude Code | Wrote a script that downloads central Iași's streets, parks and water from OpenStreetMap, and the code that draws them as the quest map where the user points to the riddle's place, with the hint circles; later a second script that adds the names of the main streets and landmarks, and the code that letters them and the quest's places on the map |
| Code (see-through while adjusting) | Claude Code | Wrote the semi-transparent ("See-through") look of the building while adjusting, in the building's shader, and its on/off button; and the Cancel button that undoes an edit |
| Code (footprint outline) | Claude Code | Wrote the footprint outline shown while adjusting: an Editor tool that cuts the building's outline out of the 3D model, the outline shader and the script that shows it |
| Code (hidden by real objects) | Claude Code | Wrote the optional depth-occlusion switch ("Hidden by real objects") on the explore screen |
| Code (fade near the camera) | Claude Code | Wrote the near-camera fade: a copy of Unity's URP Lit shader with a dissolve near the camera, and the script that sets its distance |
| Code (light and shadow) | Claude Code | Wrote the ARCore light estimation code and the shadow on the real ground (including the shadow-catcher shader) |
| Code (rendering fixes) | Claude Code | Wrote the lighting code (neutral ambient light, shadow range fitted to the model), hiding of the detected planes, and UI contrast changes |
| Code (lighting rework) | Claude Code | Rewrote how the academy and the arch react to the real light: ARCore's estimate now only moves the light within a narrow, slightly dim band, with a counter-light and a little ground bounce in the ambient light, neutral reflections instead of Unity's default sky, a limited sun height and softer self-shadows; added a developer panel to tune these values on the phone |
| Code (contact shadow, longer base) | Claude Code | Wrote the contact shadow (a soft dark band on the ground where the academy and the arch stand, baked from the models' outlines, following the hidden base and the arch's pieces) and an Editor tool that makes the academy's base slab longer on the street side so the arch stands on it, keeping its thickness and texture |
| Code (occlusion by real buildings) | Claude Code | Wrote the In Iasi mode's occlusion: the real buildings around the phone, from Google's Streetscape Geometry (Street View data), drawn invisibly so the academy, the arch and the pieces are hidden behind real buildings; one switch for both modes |
| Report material | Claude Code | Rendered the week 8 report's screen pictures in the Unity Editor (the final letter, the finale score, the progress screen with a perfect score, the compass in its stages; English and Romanian) and cut the sound samples from the app's recorded narration (Sturdza and the narrator, English and Romanian), also as short videos of the matching screens so GitHub can play them |
| Course presentation | Claude Code | Filled slides 3 to 12 of the shared course template (docs/presentation): the layout in the app's own design language, the texts from the project's real work, Editor renders and frames from the user's recordings, the embedded videos and voices; the user reviews it |
| Unity Editor work | Claude Code | Did part of the project setup and configuration in the Unity Editor |

## Prompt logs

This page is the quick overview. For the details, each AI conversation has a prompt log in
[`docs/prompts/`](prompts/): every prompt given to the AI, word for word, and what was done or made after it.
The files are named `LogPromptWeek<N>_Web<NN>.md` (Claude web) and `LogPromptWeek<N>_Code<NN>.md`
(Claude Code), and each week in the [README](../README.md) links to its own logs.

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
| 2 November 2026 | Prompt logs added in docs/prompts/: one file per Claude web or Claude Code conversation, linked from each week in the README. |
| 8 November 2026 | Third iteration of the academy model made with Claude (web): grass and gravel separate from the base, building sitting directly on the base. Swapped into the app with Claude Code, which also updated the footprint outline tool for it. |
| 8 November 2026 | Mode selection, per-mode progress, the arch pieces in the Anywhere mode and a redesigned interface written with Claude Code; saving and restoring placements removed. |
| 8 November 2026 | Geospatial Creator set up with Claude Code, with a tool for placing and exporting the in-city targets (the author places them). |
| 8 November 2026 | The In Iași mode (targets, guidance with the camera off, the academy and the pieces at their places in the city) written with Claude Code. |
| 8 November 2026 | The Anywhere mode limited to 5 times the model's size (presets Default size and Max size), as decided by the author, and its size code simplified with Claude Code. |
| 8 November 2026 | Dragging the academy in the Anywhere mode reworked with Claude Code (it follows the finger at the model's distance, so a large academy no longer runs away), and its size buttons improved (Real size hides the base slab, goes onto the detected floor and keeps the user outside the building; Tabletop brings the model back to where it was placed). |
| 8 November 2026 | After the author's city test, with Claude Code: the academy at 35 times its model size in both modes, the fade near the camera removed, and the camera closing by itself when the user walks away from a target. |
| 20 November 2026 | The map used in the Unity Editor to place the in-city targets switched from Google's 3D tiles (no longer available to accounts in the European Economic Area) to Cesium ion's map, with a tool written with Claude Code; the Google map and its key removed. |
| 20 November 2026 | Quest texts in English and Romanian written with Claude Code from the author's text and web sources; the languages, the narrator voice and the quest progress written with Claude Code. |
| 20 November 2026 | The interface redesigned (docs/Design.md) with Claude Code, using the frontend-design and ux-designer skills. |
| 20 November 2026 | The quest's screens and flow in both modes, the English / Romanian interface, and the quest map (drawn from OpenStreetMap data) written with Claude Code. |
| 21 November 2026 | The narration recorded with two AI voices made with ElevenLabs (Sturdza and the narrator, English and Romanian); the recording tool and the playback written with Claude Code. |
| 21 November 2026 | The quest map labelled with Claude Code: the names of the main streets and landmarks (from OpenStreetMap), with the quest's four places among the landmarks, looking like the others. |
| 21 November 2026 | Fourth iteration of the academy model and the arch model made with Claude (web); swapped / brought into the project with Claude Code, which also wrote a small Editor tool for checking new models. |
| 21 November 2026 | The arch by the academy and the finale's animated time lapse written with Claude Code, using the arch model. |
| 21 November 2026 | The wax seal model made with Claude (web); brought into the app and the interface with Claude Code, which also made the highlight of an aimed piece softer. |
| 21 November 2026 | The models' lighting reworked with Claude Code: a narrow, slightly dim light band that ARCore only nudges, a counter-light, neutral reflections, softer self-shadows, and a developer panel to tune it on the phone. |
| 22 November 2026 | A contact shadow where the academy and the arch meet the ground, and a longer academy base under the arch, made with Claude Code. |
| 22 November 2026 | Occlusion by real buildings for the In Iasi mode (Google's Streetscape Geometry) written with Claude Code. |
| 22 November 2026 | A "Play the finale" button for the In Iasi mode written with Claude Code to record the progress videos, then removed; the moved In Iasi targets' heights set with Claude Code. |
| 22 November 2026 | Week 8 report pictures and sound samples made with Claude Code from the app (Editor renders; the voices are the app's ElevenLabs narration). |
| 22 November 2026 | The compass needle in the In Iasi guide removed with Claude Code (it never appeared on the phone; to be reimplemented). |
| 22 November 2026 | The team's part of the shared course presentation (slides 3 to 12 of IMR08_03) made with Claude Code. |
| 22 November 2026 | The Anywhere map screenshot rendered with Claude Code; the In Iasi targets moved back to their real places in Iasi (heights set and exported with Claude Code). |
