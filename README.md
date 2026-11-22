# The Arch of Time

**Academia Mihăileană in Augmented Reality**

An Android AR quest in which you become a student of the lost Academia Mihăileană, the forerunner of the Alexandru Ioan Cuza University of Iași, and search the real streets of Iași for the pieces of its demolished arch, to see the academy rise again at full scale, on the exact spot where it once stood.

| App name | Theme | Device | Module |
|---|---|---|---|
| The Arch of Time | Location-Based AR Experience | Mobile (Android) | AR - Geospatial |

**Project documentation:**

[State of the Art](docs/StateOfTheArt.md) - description, similar products, main features, user journey diagram, moodboard and technologies.

[Main Modules](docs/MainModules.md) - user story map, quest route, journey maps, storyboard and asset creation plan.

[AI Usage Acknowledgement](docs/AIUsage.md) - how AI tools were used to create this project.

## About the project

The Academia Mihăileană was inaugurated in Iași in 1835 and is considered the forerunner of today's Alexandru Ioan Cuza University. Its buildings no longer exist: the stone arch that connected its two buildings was demolished in 1890, and the main building in 1963. Today there are streets and apartment buildings where the academy once stood.

The Arch of Time turns this lost history into a location-based AR quest. Welcomed as a new student, you see the academy as it was before the arch existed, then set off across Iași to find the pieces of the arch. When the arch is complete, you return to the academy, put it in place and watch history unfold, as time undoes all your work.

### How it works

1. **Welcome**: at the academy's original site, you are greeted as a new student and see the academy as it was in 1835, without the arch.
2. **The quest**: you receive a riddle that points to a real place in Iași linked to the academy's story.
3. **The search**: hints and a hot/cold indicator on your phone guide you to the destination.
4. **At each destination**: collect a piece of the arch in AR, unlock new knowledge, answer a few questions, view your progress, and receive the next riddle. Secrets can be unlocked along the way.
5. **The finale**: back at the academy, place the completed arch and watch history unfold: 1845 the ensemble is whole, 1890 the arch is demolished, 1963 the academy is demolished.
6. **After the journey**: take a "Then & Now" photo and ask the narrator your own questions.

### Planned features

- **"Rebuild the Arch" quest**: riddles, hints and a hot/cold indicator lead you to real places in Iași, where you collect the pieces of the arch.
- **Geospatial placement**: the academy and each quest scene appear at their real locations and real scale.
- **Free placement mode**: place, scale and rotate the scenes anywhere (table, park, classroom), so the whole quest can be played outside Iași or tested safely.
- **Unlockable knowledge and questions** at each destination, with hidden secrets along the way.
- **Progress view**: the collected pieces of the arch and the knowledge unlocked so far.
- **Time journey finale** through four moments:
  - **1835**: the academy is founded
  - **1845**: the arch connecting the two buildings is completed
  - **1890**: the arch is demolished
  - **1963**: the academy is demolished
- **AI narrator**: guides the user through the story and answers questions only from a curated historical knowledge source, so answers stay historically accurate.
- **"Then & Now" photo** with the academy behind you, ready to share.
- **Languages**: English by default, with Romanian support. Other languages are planned to be supported.

## Weekly progress

Each week we add what is new, a video of the new features, and screenshots where applicable.

<!-- Copy this block for each new week, newest week on top -->

### Week 8: The whole quest, recorded voices and a more realistic academy

**What's new**

- The whole quest is now in the app, in English and Romanian, written from trusted historical sources:
  - Prince Mihail Sturdza greets you at the academy, then a riddle leads you to each of the four places of the academy's story.
  - At each place: collect the arch piece, read what the place remembers and answer three questions. With every answer right, a wax seal appears near you and opens a secret.
  - The finale: back at the academy, the arch rises (1845), is demolished (1890), then the academy is demolished too (1963). With all five seals found, the prince sends you a letter.
  - A progress screen with the pieces, the seals and the score for each place.
- Recorded voices: the prince and a narrator read every card, in both languages, made with AI voices (ElevenLabs). The voice can be paused and resumed.
- A new design for the whole interface, inspired by what Iași printed between 1835 and 1858: rag paper and ink, the colours of the first Moldavian stamps and red wax seals.
- New 3D models: the fourth iteration of the academy (bevelled edges, new colours), the academy's arch, split into the four pieces you collect, and the wax seal. The arch stands by the academy and fills in as you collect its pieces.
- A more realistic look:
  - A softer, more even light that follows the real light only a little, with a counter-light and neutral reflections.
  - A dark contact shadow where the academy and the arch meet the ground, and a stronger shadow on the real ground.
  - The base now reaches under the arch, and hiding the base lowers the academy onto the ground.
- **Anywhere** mode: choose the next place on a map of Iași with its main streets and landmarks.
- **In Iași** mode:
  - The places are visited in the order of the story, instead of being picked from a list.
  - Real buildings can now hide the academy, the arch and the pieces, using the building shapes Google makes from Street View ("Hidden by buildings" switch).
  - Close to a place, the dial says "Warmer" or "Colder" and in which direction to go.
- The map used in the Unity Editor to place the In Iași targets is now Cesium ion's, because Google's 3D map is no longer available to accounts in the European Economic Area.

**Video**

https://github.com/user-attachments/assets/0fc632a4-ae9c-44ab-8c85-18ba8bb0dd68

https://github.com/user-attachments/assets/245804ef-4488-4d49-8f30-3912feaf0764

https://github.com/user-attachments/assets/715840b1-cc40-4358-b911-73265b9976e6

The videos have no sound. To hear the app's voices, Prince Mihail Sturdza and the narrator in English and Romanian, see the [sound samples](docs/SoundSamples.md).

**Screenshots**

The end of the quest with every answer right and all five seals found:

| The finale's last card | The prince's letter | Progress with a perfect score |
|:---:|:---:|:---:|
| <img src="docs/screenshots/week8_finale_score_en.png" width="240"> | <img src="docs/screenshots/week8_secret_letter_en.png" width="240"> | <img src="docs/screenshots/week8_progress_perfect_en.png" width="240"> |

The In Iași guide on the way to the first piece:

| Far away | Getting closer | Walking away | Arrived |
|:---:|:---:|:---:|:---:|
| <img src="docs/screenshots/week8_compass_1_far_en.png" width="180"> | <img src="docs/screenshots/week8_compass_2_warmer_en.png" width="180"> | <img src="docs/screenshots/week8_compass_3_colder_en.png" width="180"> | <img src="docs/screenshots/week8_compass_4_arrived_en.png" width="180"> |

The same screens in Romanian are in [docs/screenshots](docs/screenshots) (the files ending in `_ro`).

**AI prompt logs**

Every prompt given to an AI tool this week and what was done after it (see [AI usage](docs/AIUsage.md)):

- [Claude web 01](docs/prompts/LogPromptWeek8_Web01.md): the fourth iteration of the academy model, the arch model and its four pieces
- [Claude web 02](docs/prompts/LogPromptWeek8_Web02.md): the wax seal model
- [Claude Code 01](docs/prompts/LogPromptWeek8_Code01.md): the Cesium ion map in the Editor, the whole quest's content, the new design
- [Claude Code 02](docs/prompts/LogPromptWeek8_Code02.md): the recorded voices, the language choice, the map names, the new models and the arch by the academy
- [Claude Code 03](docs/prompts/LogPromptWeek8_Code03.md): the new lighting of the academy and the arch
- [Claude Code 04](docs/prompts/LogPromptWeek8_Code04.md): the contact shadow and the longer base, occlusion by real buildings, the report material

### Week 7

**What's new**

_No work was done this week._

**Video**

_No video this week._

**Screenshots**

_No screenshots this week._

**AI prompt logs**

_No prompt logs this week._

### Week 6: Two ways to play and the In Iași mode

**What's new**

- The app now starts with a choice of two ways to play, each with its own saved progress:
  - **In Iași**: walk to the real places of the academy's story.
  - **Anywhere**: place the academy on a floor or a table and collect the arch pieces around you.
- Redesigned the whole interface: new colours, a classic title font and an arch emblem that fills in as you collect the pieces, drawn after the arch replica in Parcul Expoziției.
- Third iteration of the academy model: the grass and gravel are separate from the base, and the building sits directly on it.
- **In Iași** mode:
  - The academy and the four arch pieces are placed on Google's 3D map of the city, in the Unity Editor (Geospatial Creator).
  - Pick a target from the list. A found piece can't be picked again until the whole arch is complete.
  - With the camera off, the app shows how far the target is and in which direction. Close to it, a dial points the way and says "Warmer" or "Colder".
  - At the spot, open the camera and look around. Once the phone knows exactly where it is (using Street View images), the academy or the piece appears at its place. Pieces are collected; the academy is there to walk around.
  - The camera closes by itself when you walk away from the target.
- **Anywhere** mode: scan, place and adjust the academy (move, turn, resize up to 5 times its size, raise or lower), then find and collect the four arch pieces that appear around you, one at a time.
- The academy stays solid up close, so you can look at its details from very near.
- Removed saving and restoring the placement: the academy is placed again each time, and only the collected pieces are kept.

**Video**

https://github.com/user-attachments/assets/a7d9f125-6053-492c-b6fe-b72ccc457fe4

**Screenshots**

_No screenshots this week._

**AI prompt logs**

Every prompt given to an AI tool this week and what was done after it (see [AI usage](docs/AIUsage.md)):

- [Claude web 01](docs/prompts/LogPromptWeek6_Web01.md): the third iteration of the academy model
- [Claude Code 01](docs/prompts/LogPromptWeek6_Code01.md): the two modes and the new interface, the In Iași mode, fixes after the phone tests

### Week 5: Main modules, the academy model and the first AR prototype

**What's new**

- Planned the app in detail in [Main Modules](docs/MainModules.md): user story map, quest route with the four stops and the hidden seals, journey maps, storyboard and the asset creation plan.
- Created the 3D model of the Academia Mihăileană, built with AI from scans and photos of the academy's replica in a museum, then improved in a second iteration.
- Built the first AR prototype for Android, a proof of concept of the basic functions:
  - **Place**: the app finds the ground and places the academy where you point, with its front facing you.
  - **Adjust**: move, turn, resize (from tabletop size up to real size, scale 1:50) and raise or lower the academy, with touch gestures or buttons. While adjusting, the building turns see-through and its footprint is drawn on the ground; Cancel undoes an edit.
  - **Explore**: walk around the academy. It fades out near the camera, so you can step through its walls, and it can optionally be hidden by real objects in front of it.
  - **Save and restore**: the placement is saved as a cloud anchor and returns on the next launch when the camera recognises the spot. Your location guides you back to it (for example "about 25 m ahead, slightly to the right").
  - **Realistic look**: the light is estimated from the camera image, and the academy casts a shadow on the real ground.
  - **Arch pieces (test mode)**: a placeholder piece appears somewhere around you; find it, aim at it and collect it. Five pieces, with progress kept between launches.
- Added the [AI Usage Acknowledgement](docs/AIUsage.md), which states how AI tools were used in the project.

**Video**

https://github.com/user-attachments/assets/437a331a-e47b-4795-af9e-9d01c37481bf

**Screenshots**

The panel at the top is a developer overlay with the location and tracking status.

| Exploring the academy at tabletop size | Collecting an arch piece (test mode) |
|:---:|:---:|
| <img src="docs/screenshots/week5_explore.png" width="300"> | <img src="docs/screenshots/week5_arch_pieces.png" width="300"> |

**AI prompt logs**

Every prompt given to an AI tool this week and what was done after it (see [AI usage](docs/AIUsage.md)):

- [Claude web 01](docs/prompts/LogPromptWeek5_Web01.md): Unity AR project setup, AR startup and permissions, UI Toolkit
- [Claude web 02](docs/prompts/LogPromptWeek5_Web02.md): building the 3D academy model and importing it into Unity
- [Claude web 03](docs/prompts/LogPromptWeek5_Web03.md): facade description and material colours for the model
- [Claude web 04](docs/prompts/LogPromptWeek5_Web04.md): the Main Modules document, storyboard and quest route
- [Claude Code 01](docs/prompts/LogPromptWeek5_Code01.md): the AR prototype in Unity, model iteration 2, documentation and code clean-up

### Week 4: Project definition

**What's new**

- Defined the project idea, theme, device and module.
- Completed the [State of the Art](docs/StateOfTheArt.md) document, including the user journey diagram and the moodboard.
- Created the Unity project and the GitHub repository.

**Video**

_No video this week._

**Screenshots**

_No screenshots this week._

**AI prompt logs**

Every prompt given to an AI tool this week and what was done after it (see [AI usage](docs/AIUsage.md)):

- [Claude web 01](docs/prompts/LogPromptWeek4_Web01.md): the earlier course assignments, defining The Arch of Time, the README and the State of the Art document

## Team

- Iacob David-Gabriel
