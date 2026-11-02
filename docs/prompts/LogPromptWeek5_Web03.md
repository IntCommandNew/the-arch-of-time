# Prompt log: Week 5, Claude web conversation 03

| | |
|---|---|
| Week | 5 |
| Tool | Claude (web, claude.ai) |
| Conversation | Web 03 |
| Topic | Facade description and material colours for a 3D building model |
| Date | 2026-10-25 |
| Prompts | 4 |

## Summary

The user shared photos of a physical scale model of a building and asked for a detailed description of its four facades and roof, to give to another AI that was generating a 3D model. Claude wrote the description, put it into a document and exported it as a Markdown file. Claude then compared screenshots of the AI-generated 3D model and a photogrammetry scan, and wrote a prompt for the 3D AI on colours, materials and lighting.

## Prompts

### Prompt 1

**Prompt:**

> I have some pictures of a model of a building. I use an AI to create the 3D model based on this pictures. however, he keeps getting the facade of the building wrong because it is rather complex, having many subtle decorations and layers of depth with many things extruded or inseted. what I want is for you to analyze the images and describe in as much detail as possible the 4 sides of the building so I can give them to the AI to recrete the model right. the front wall is the one with the octagon construction on it and a poster pancard, the right wall is the one with the human silhouette, the left wall is the one with 2 posters pancarde on it and a pilar next to them. the other wall is the back one and the roof is easy to identify.

**Attachments:**

- 1792923497124_image.png: photo of the scale model's front wall, with the central octagonal bay/tower and one poster board.
- 1792923517438_image.png: photo of the side wall with the entrance door, canopy and a scale figure in a top hat.
- 1792923570611_image.png: photo of the other side wall, with two poster boards and a corner pillar.
- 1792923611566_image.png: photo of the back wall, taken at an angle.
- 1792924241732_image.png: top-down photo of the roof, showing the tower roof, chimneys and roof vents.

**What was done:**

- Analysed the five photos and pointed out that the user's "right wall" and "left wall" are named from the building's own point of view, so they are mirrored for a viewer facing the front. Introduced compass names to avoid this: South (front), West (door wall), East (pillar wall), North (back).
- Wrote a detailed description in the reply, covering:
    - massing and approximate dimensions, estimated from the scale figure (about 13 m by 16 m, floor heights, bay size, East wall step, sloping ground);
    - shared elements: colours with hex values, plinth, floor belt, upper-floor panel framework, four window types (A to D), downpipes, and a table of depth offsets;
    - each of the four facades, including a bay-by-bay table for the West wall;
    - the roof: hipped main roof, faceted tower roof, 6 chimneys, 4 vents;
    - items to leave out of the model (scale figure, poster boards, grass, gravel and display base).
- Suggested giving the description to the 3D AI in stages: massing first, then one wall at a time.

**Files created or changed:** None (text in the reply only)

### Prompt 2

**Prompt:**

> put all the descriptions in a document so I can export them to my AI

**Attachments:** None

**What was done:**

- Created a Claude Docs document titled "Building Model – Facade Description" with the full description from prompt 1, organised as an orientation key plus eight numbered sections.
- Put the dimensions, colours, window types, depth offsets and West wall bay layout into tables.
- Left one comment in the document asking the user to confirm that the door wall is on the left when facing the front. No reply to it is visible in this conversation.

**Files created or changed:** "Building Model – Facade Description" (Claude Docs document on claude.ai)

### Prompt 3

**Prompt:**

> export it with the best format

**Attachments:** None

**What was done:**

- Chose Markdown as the best format for passing the text to another AI, because it is plain text and keeps headings and tables.
- Exported the document to Markdown and sent it to the user as a downloadable file (about 14 KB).
- Offered a PDF or Word export if the user's AI did not accept Markdown.

**Files created or changed:** Building_Model_Facade_Description.md

### Prompt 4

**Prompt:**

> now I have a problem regarding the model look and color. the model has corect texture images, but the colors look to cartoonish and the model doesn't have the real looking fell of the scaned model I have. what should I tell the AI to make the colors look more like the model ones? I atached bot my model and scaned model pictures for reference.

**Attachments:**

- 1792951225045_image.png: Blender viewport screenshot of the user's AI-generated 3D model, three-quarter view of the door wall and front.
- 1792951254247_image.png: Blender viewport screenshot of the photogrammetry scan of the physical model, from a similar angle.

**What was done:**

- The first attempt at a reply stopped before any analysis ran. An automatic continuation message (not written by the user) resumed it.
- Sampled pixel colours from both screenshots with a Python script, using grid overlays and zoomed crops to place the samples.
- Compared materials in a table (generated model vs scan):
    - sunlit wall: #BEBCB9 vs #ABA295;
    - shaded wall: #BEBEBE vs #89949B;
    - main roof: #7E5C54 vs #5E4A3E;
    - tower roof: #876359 vs #6B605B;
    - plinth: #9D9C93 vs #78756C;
    - window glass: #8E8985 vs #343538;
    - downpipes: #C0C0C0 vs #4B4E50;
    - door canopy: #85594D vs #534B4C.
- Explained the cause: the generated model is too bright and uniform, with pale windows, white downpipes and no light direction. The scan has lighting, shadow and dirt baked into its texture.
- Noted that the "milky glass" in the earlier description had contributed to the pale windows.
- Wrote a prompt for the 3D AI covering:
    - colour management (AgX or Filmic) and sun-plus-sky lighting;
    - Principled BSDF base colours and roughness for each material;
    - noise-based colour variation;
    - ambient occlusion and grime;
    - target colours to check in the rendered image.
- Recommended attaching the scan screenshot to the 3D AI. Offered to update the colours in the description document to the scan-based values. This was not done in this conversation.

**Files created or changed:** Temporary working files that were not sent to the user: sample.py, gen_grid.png, scan_grid.png, gen_west.png, scan_west.png, scan_south.png. The prompt for the 3D AI is text in the reply only.

## Outputs used in the project

| Output | Made in prompt | Notes |
|---|---|---|
| Facade description (text in reply) | 1 | First version; its content was moved into the document in prompt 2 |
| "Building Model – Facade Description" (Claude Docs document) | 2 | Same content as prompt 1, reorganised into sections and tables; colours not updated after prompt 4 |
| Building_Model_Facade_Description.md | 3 | Markdown export of the document, made for the 3D modelling AI; its colour values differ from the scan-based values in prompt 4 |
| Material, colour and lighting prompt for the 3D AI (text in reply) | 4 | Based on colours sampled from the scan screenshot |
