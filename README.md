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

- [Claude web 01](docs/prompts/LogPromptWeek4_Web01.md): the earlier course assignments (AR cactus, VR darts, robot hands), then defining The Arch of Time, the README and the State of the Art document (the same conversation continues in week 5, Claude web 04)

## Team

- Iacob David-Gabriel
