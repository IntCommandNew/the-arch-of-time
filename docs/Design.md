# The Arch of Time: design

The app's design system: what it looks like, why, and the rules for new screens. (The idea of one design file
for the whole app comes from the DESIGN.md format; this one is written for this app, not copied from a brand.)

## Who uses it, and where

- Visitors and students in Iași, walking between real places with the phone held up to the camera, often
  outdoors in daylight; or anyone at home, with the academy on a table or on the floor.
- One hand on the phone, the other often busy. The camera view is the main thing on screen; the interface
  sits at the bottom, in the thumb's reach, and steps aside when it isn't needed.
- The story is read and listened to (the narrator), in English or Romanian.

## The world: Iași's printed matter, 1835 to 1858

The academy lived in the years when Iași printed its first Romanian newspapers and its first stamps. The
interface borrows from what that city printed and sealed, not from today's apps:

- **Albina Românească** (Gheorghe Asachi's newspaper, printed in Iași from 1829): rag paper, black ink,
  modern (Didone) type, thick-and-thin rules over each article.
- **The 1858 Cap de bour stamps**, printed in Iași: blue ink on rose, green and blue paper. They give the
  palette its colours instead of a modern accent colour.
- **The prince's sealed charters**: red wax, used only for the seals and secrets of the quest.
- **Engraved city plans**: ink streets on paper, a north arrow and a scale bar: the quest map.

Light paper with dark ink also reads best in the sun, which a dark interface over the camera does not.

## Colours

| Name | Value | Used for |
|---|---|---|
| Paper | rgb(230, 223, 204) | sheets, panels, menu pages (a greyish rag paper, not cream) |
| Ink | rgb(34, 27, 22) | text, rules, outlines (iron-gall brown-black) |
| Prussian | rgb(30, 57, 102) | the main action, collected pieces, "colder", water on the map (stamp ink) |
| Rose | rgb(227, 180, 170) | where you are now (the current stop), a wrong answer (stamp paper) |
| Verdigris | rgb(169, 195, 160) | a right answer, parks on the map (stamp paper) |
| Wax | rgb(140, 32, 28) | seals and secrets only; "warmer" on the guide |

Secondary text is ink at 72 % (contrast about 5.7 : 1 on paper). Nothing is grey.

## Type

- **Bodoni Moda** (Didone, the type of the period's printing; OFL), two static cuts: *Display* (optical size
  28, weight 500) for titles and place names, *Poster* (optical size 96) for the large figures: the app's
  name, the finale's years, the distance on the guide, the stop numbers.
- **EB Garamond** for everything read or narrated: the story, riddles, questions and answers.
- Unity's sans for controls and short status lines read on the move.
- Sentence case everywhere; no capitals-only labels, no small labels above headings.

## Layout

- **Title page** (language, modes): set like an 1835 title page, centred: the name, a thick-and-thin rule,
  the subtitle, then the choices as entries of a contents page (left-aligned text inside). The mode page ends
  with the language line: its name, then both languages side by side, each named in its own language
  (English, Română), the current one inked in. A tap switches at once, and the page's texts come in again in
  the new language.
- **The slip** (every camera screen): one paper slip at the bottom with an ink edge. Its first line is what
  to do now; one main button fills its width; smaller actions sit in one row under it. Nothing floats
  elsewhere over the camera.
- **The sheet** (story, secrets, knowledge, finale): a taller slip with the thick-and-thin rule at its head,
  the title in Bodoni, the text in Garamond, the voice switch and the page count at its foot.
- **The quiz**: the question in Bodoni, the answers as ruled entries lettered a, b, c.
- **The register** (progress): a ledger page, one ruled row per stop in route order with its number, the
  place (hidden until found), what was done there and the points; the five seals: each found one is the
  academy's wax seal itself (the same seal as in the scene), the others broken ink circles.
- **The plan** (map): an engraved plan of central Iași in a ruled frame, with a north arrow and a scale bar. Lettered like a plan: street names in Garamond along their streets, landmarks in Bodoni beside a small
  ring, each name on a patch of cleared paper; more street names appear as you zoom in, and names never
  overlap. The quest's places are landmarks like any other, with the same ring, type and plain name, among
  look-alikes: the map helps to find a place, but never says which places are the answers.

## The one memorable thing

**The arch, engraved.** The arch emblem is drawn like an architect's engraving: a piece still missing is
hatched in ink, as a part not yet built; a collected piece is printed solid in Prussian blue. It appears on
the title page (one per mode), on the slip while exploring, and large in the register. Everything around it
stays quiet.

## Motion

One moment per action: the sheet rises from the bottom (280 ms), the answers slide in one by one, the
finale's year fades down into place. No other animation in the interface.

In the scene, the arch never pops in or out. A missing piece stands by the academy as a see-through ghost; a
collected piece lifts off and lands solid (1.4 s). In the finale the pieces rise one after another (1845),
crumble from the top down (1890), the academy dissolves (1963), and both come back for the legacy.

## Rules for new screens

- Touch targets at least 88 px tall at the 1080 px reference (about 48 dp on the phone).
- The phone's interface is about 974 px wide, not 1080: check every screen at 1080 x 2340, in Romanian
  (about 30 % longer than English). Labels wrap; they never run out of a button.
- At most one main action per screen, and at most four choices at once.
- Two buttons in a row get fixed shares (about 1 : 2); a count or a page number never shares their row (it goes on its own line above).
- A button says what a tap does ("Turn the voice off"), never what state something is in ("Voice on").
- A choice between a few options shows all of them, with the current one marked; never one button that
  silently flips to the other option.
- Turning the voice off pauses it where it is; turning it on carries on from there.
- Text tells what is about to happen before it happens: a seal "will appear" when you go on, never "has appeared" before it has.
- Backgrounds of full-screen overlays reach the screen's edges (the notch strip included).
