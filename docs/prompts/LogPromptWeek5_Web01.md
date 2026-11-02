# Prompt log: Week 5, Claude web conversation 01

| | |
|---|---|
| Week | 5 |
| Tool | Claude (web, claude.ai) |
| Conversation | Web 01 |
| Topic | Unity AR project setup, AR startup and permissions, UI Toolkit, move to Claude Code with the Unity CLI |
| Date | 2026-10-27 to 2026-10-30 |
| Prompts | 34 |

## Summary

The conversation started with the question of how to place the existing Unity model of the building at a real-world location on Android, and went on to agree the placement UX and the persistence approach. It then worked through the full project setup: AR Foundation, ARCore Extensions, Google Cloud keyless authorization, URP, scene skeleton and first device builds. After that it fixed a series of startup and permission problems with a startup gate script, moved the UI to UI Toolkit, and made camera and precise location mandatory. It ended by preparing the move to Claude Code with the Unity CLI: a CLAUDE.md project file, Unity CLI connection and adb setup for the phone.

## Prompts

### Prompt 1

**Prompt:**

> I want to create an android AR application with unity. I have the 3D model of a historical model already in unity. what I want to happen is that you can set a real world spacial coordinate for the model, along with the scale and orientation then you will be able to move around to see the model all around. those are some specifications for you:
>
> [pasted: model orientation specification for the Unity axes (front, back and side facades), about 5 lines]
>
> how can we do this?

**Attachments:** attachment.txt: project and model context document (Unity version, folder structure, model size, pivot, hierarchy, materials, available export formats).

**What was done:**

- Looked up the ARCore Extensions Geospatial API reference (terrain anchors, east-up-north rotation, AREarthManager).
- Explained two placement modes: local placement on a detected surface (tap, pinch, twist, lock) and geospatial placement at fixed latitude/longitude with a compass bearing and scale.
- Wrote three scripts: an orientation helper for the model's axes, a tap-to-place script, and a geospatial placement script.
- Gave setup steps for AR Foundation, ARCore, URP and ARCore Extensions, plus visual tips for the model.

**Files created or changed:** MachetaOrientation.cs, MachetaTapPlacer.cs, MachetaGeoPlacer.cs

### Prompt 2

**Prompt:**

> let's first think a bit about the UX and how everything works out. the user will have the phone camera open and will have to select the original location of the model. we have to think how he will do that and were exactly will the model be placed (ath the eaxt phone location, a bit in front of him, on a scande surface ?). after that he must be able to adjust the scale, orientation, and position of the model up to the real scale of a building (so it will be rather big). if he is satisfies, he will be able to set that exact location, scale and orientation and move around the model to see it. if the app closes we must have persistence and the user can chose to unpick the selected point and chose another one. what are your sugestions for aproaching all of this?

**Attachments:** None

**What was done:**

- Looked up AR Foundation persistent anchors.
- Proposed an app flow as a state machine: Scan, Place, Adjust, Confirm, Explore, with Restore on launch.
- Recommended placing with a reticle on the detected ground and a "Place here" button, starting at tabletop size.
- Described two adjust methods: A (manual controls) and B (two-corner alignment), plus footprint outline, transparency, and the raised-platform issue at real scale.
- Compared persistence options (geospatial, cloud anchors, printed marker) and recommended geospatial as the main method.
- Asked three questions: whether the building still exists, the model's scale ratio, and outdoor-only or indoor use.

**Files created or changed:** None

### Prompt 3

**Prompt:**

> I will go with A for positioning (manual method). the rest of your recomendations look good. the building no longer exists, so I want to place it were it stood (now there is a street and an apartment building there), but also in any outhor place, like in a field. it will be nice if we could also place it indoors. is it ok like this?

**Attachments:** None

**What was done:**

- Explained what each place means technically: the original site (Street View coverage, occlusion off by default), a field (GPS only, a quick adjust after restore) and indoors (cloud anchors only).
- Designed dual persistence: save both a geospatial pose and a cloud anchor, with the anchor at the reticle point and the model stored as an offset; on restart, try the cloud anchor first, then geospatial.
- Listed added requirements: keyless authorization (cloud anchors up to 365 days), one SDK (ARCore Extensions), internet, a privacy notice and a device check.

**Files created or changed:** None

### Prompt 4

**Prompt:**

> sounds nice. first, before we write the code, what should we have setup?

**Attachments:** None

**What was done:**

- Looked up ARCore Extensions compatibility with Unity 6 and AR Foundation 6, Geospatial setup, and keyless authorization.
- Gave a nine-part setup checklist: package name and keystore, Unity packages (including the ARCore Extensions git URL), Player Settings, XR Plug-in Management, Google Cloud Console, URP, scene skeleton, model information needed, and two test builds.

**Files created or changed:** None

### Prompt 5

**Prompt:**

> at step 1, do I really have to set up a key?

**Attachments:** None

**What was done:**

- Corrected the checklist: a custom keystore is not needed during development, because the debug key's SHA-1 can be registered for keyless authorization.
- Gave the keytool command for the debug keystore and noted that it is per computer and that a release keystore is needed for Google Play.
- Advised keeping the package name fixed and not using an API key (cloud anchors would expire after one day).

**Files created or changed:** None

### Prompt 6

**Prompt:**

> [pasted: PowerShell output of the keytool command failing with "Keystore file does not exist", about 10 lines]

**Attachments:** None

**What was done:**

- Explained that PowerShell uses `$env:USERPROFILE` instead of `%USERPROFILE%` and gave the corrected command.
- Explained that Unity creates the debug keystore on the first Android build, and which line (SHA1) to copy.

**Files created or changed:** None

### Prompt 7

**Prompt:**

> at step 2, while the package was installed, I have this error:
>
> [pasted: Unity Console error from the External Dependency Manager, unable to copy mainTemplate.gradle into Assets/Plugins/Android, about 25 lines]

**Attachments:** None

**What was done:**

- Explained that the folder Assets/Plugins/Android did not exist yet.
- Told the user to enable the custom Main Gradle, Gradle Properties and Gradle Settings templates, run Force Resolve, and accept AndroidX/Jetifier.

**Files created or changed:** None

### Prompt 8

**Prompt:**

> I did all the steps, but I don't see any google things in the file

**Attachments:** None

**What was done:**

- Suggested enabling Keyless authentication and the Geospatial optional feature, checking the resolver's patch settings, and running Force Resolve again, then checking Display Libraries.

**Files created or changed:** None

### Prompt 9

**Prompt:**

> still nothing, and the display libraries shows nothing, but now I don't get the error when I restart the project

**Attachments:** None

**What was done:**

- The first reply was interrupted ("No response requested." was shown). After the session resumed automatically, the work continued.
- Cloned the ARCore Extensions source (v1.57.0-arf6) and found that it registers its Android libraries only when an Android build starts.
- Explained that the empty list in the Editor was normal and corrected the earlier check: make one Android build, then look at mainTemplate.gradle.

**Files created or changed:** None

### Prompt 10

**Prompt:**

> now it checks up. I got a warning at build telling me I should just stick with the new input system. do I leave it to both systems?

**Attachments:** None

**What was done:**

- Recommended switching Active Input Handling to the Input System Package (New) and replacing the EventSystem's old input module.
- Noted that Google's sample scenes need the old system and that the old `Input.location.Start()` call would be removed in a later rewrite.

**Files created or changed:** None

### Prompt 11

**Prompt:**

> now to step 5 of the setup

**Attachments:** None

**What was done:**

- Looked up the current Google Auth Platform steps for OAuth clients.
- Explained creating the Google Cloud project, enabling the ARCore API, registering the app (Branding, Audience) and creating the Android OAuth client with package name and SHA-1.

**Files created or changed:** None

### Prompt 12

**Prompt:**

> let's get to part 6

**Attachments:** None

**What was done:**

- Looked up Google's Vulkan guide for AR Foundation.
- Explained how to find the URP renderer Android uses, and how to add the AR Background Renderer Feature and the AR Command Buffer Support Renderer Feature.
- Gave the remaining settings: Forward or Forward+ rendering path, no SSAO, MSAA 4x, HDR off, and post-processing off on the AR camera.

**Files created or changed:** None

### Prompt 13

**Prompt:**

> what XR Origin?

**Attachments:** None

**What was done:**

- Acknowledged that the XR Origin is only created in step 7.
- Explained how to delete the default camera and add AR Session and XR Origin (Mobile AR), and what the XR Origin is.

**Files created or changed:** None

### Prompt 14

**Prompt:**

> go on

**Attachments:** None

**What was done:**

- Explained step 7: managers on the XR Origin (plane, raycast, anchor, earth), the ARCore Extensions Config asset, the ARCore Extensions object, AR camera settings, light, UI canvas and a test cube.
- Showed the expected hierarchy.

**Files created or changed:** None

### Prompt 15

**Prompt:**

> (no text; image only)

**Attachments:** Screenshot of the Unity Editor: the Main_AR hierarchy (Directional Light, AR Session, XR Origin, Camera Offset, Main Camera, ARCore Extensions, Canvas, EventSystem, Cube) and the Game view showing a grey cube on a black background.

**What was done:**

- Confirmed the hierarchy was correct and that the black background is normal in the Editor.
- Explained the first device build ("Hello AR") and what should appear on the phone.
- Asked for two model values: the scale ratio and the ground-level offset.

**Files created or changed:** None

### Prompt 16

**Prompt:**

> [pasted: Unity Console error "Cloud Anchors feature is required by CloudAnchorMode Enabled" and the BuildFailedException that followed, about 16 lines]

**Attachments:** None

**What was done:**

- Explained that the project-level Optional Features must match the config asset, and that Cloud Anchors had to be ticked next to Geospatial.

**Files created or changed:** None

### Prompt 17

**Prompt:**

> I got the application on my phone, I can see the cube and, also, a bunch of yellow shapes that appear on the floor or other surfaces over time as I move the camera. I think they are the scaned surfaces?

**Attachments:** None

**What was done:**

- Confirmed the yellow shapes are detected surfaces and that the base AR setup worked.
- Wrote a diagnostic overlay script showing location permission, Geospatial support, Earth state, tracking, accuracy and Street View (VPS) availability.
- Explained how to set it up and how to read the results.

**Files created or changed:** GeospatialStatus.cs

### Prompt 18

**Prompt:**

> before I build, I just want to be sure that these errors in play more are expected because we are playing on a pc and not phone.

**Attachments:** Screenshot of the Unity Editor in Play mode: the diagnostic overlay with red values (AR session None, Earth state ErrorSessionNotReady) and Console errors about missing ARCore session and camera subsystems and a DllNotFoundException for arcore_sdk_c.

**What was done:**

- Confirmed all the errors were expected on a PC, because ARCore exists only on Android, and explained each one.

**Files created or changed:** None

### Prompt 19

**Prompt:**

> it works, but we have a problem I saw even in the first build. when I open the applications is closes after 1-2 seconds if I don't answer and I have a permission prompt on the screen. after I anwer the app will no longer close, with the exception of the situation were I don't give it camera permission, case in wich it will crash after 1-2 seconds.

**Attachments:** None

**What was done:**

- Read the ARCore Extensions source and found the cause: it waits about 60 frames for the AR session, then calls into ARCore anyway.
- Wrote a startup gate script that asks for permissions one at a time, checks ARCore, and only then activates the AR objects. It shows a blocked panel with Try again / Open settings.
- Updated the diagnostic overlay so it no longer requests permissions.
- Gave scene changes (AR Session and ARCore Extensions start inactive) and a test plan.

**Files created or changed:** ARStartupGate.cs (new), GeospatialStatus.cs (updated)

### Prompt 20

**Prompt:**

> before the build, I see that
>
> [pasted: the C# field name requestLocation, 1 line]
>
> is never used in code.

**Attachments:** None

**What was done:**

- Explained that the field was used only in the Android-only code block, so the Editor saw it as unused.
- Changed the Editor branch to read it, so the warning disappears.

**Files created or changed:** ARStartupGate.cs (updated)

### Prompt 21

**Prompt:**

> the buttons and the new functionalities work. however, I don't know if this is intended, but testing indors (I didn't test outdors), the earth state is on not ready, earth tracking on none and street view not checked yet, but on the last build I did all 3 were green even indors. is this intended behaviour?

**Attachments:** None

**What was done:**

- Read the ARCore Extensions source and found the cause: the gate activated ARCore Extensions before the AR Session, so Extensions could not hook into the session.
- Fixed the order: AR Session first, then ARCore Extensions.

**Files created or changed:** ARStartupGate.cs (updated)

### Prompt 22

**Prompt:**

> it worked. now, before we go on, there are some things I want to discuss. first, I have this warnings when I build:
>
> [pasted: build warning about altered assets in immutable packages (TestReferenceImageLibrary), about 4 lines]
>
> [pasted: build warning about Diagnostics Data requiring Debug Symbols, about 2 lines]
>
> [pasted: build warning that a scene contains AR components but no ARSession, about 4 lines]
>
> should we address them?
>
> second, ARStartupGate exposes Events in the editor. should it be like this?

**Attachments:** None

**What was done:**

- Explained the three warnings: ignore the first, set Debug Symbols to Symbol Table for the second, and ignore the third (expected, because the AR Session is inactive by design).
- Explained that the onARStarted Unity Event is an intentional hook for later.

**Files created or changed:** None

### Prompt 23

**Prompt:**

> ok. another thing, regarding UI. dooing UI now is rather awkward on how I have to do it in the editor. I heard that there is another way to do UI using some code. can this help us?

**Attachments:** None

**What was done:**

- Explained UI Toolkit (UXML for layout, USS for styles, a C# controller), its advantages and trade-offs for this project.
- Proposed moving the app's UI to it.

**Files created or changed:** None

### Prompt 24

**Prompt:**

> sounds nice. let's recreate the current state of the application with this new UI sytem so we have the structure set for the real implementation down the line.

**Attachments:** None

**What was done:**

- Wrote the UI Toolkit layout and styles with three screens (Starting, Blocked, Scan) and a debug overlay.
- Wrote a UI controller (safe-area handling, touch-over-UI check) and an app state machine controller.
- Rewrote the startup gate to report its status through events, with a "Preview In Editor" option, and rewrote the diagnostic overlay to use the new UI.
- Gave setup steps: Panel Settings, UI Document object, App object, removing the Canvas. Recommended deleting the three scripts from prompt 1.

**Files created or changed:** MainUI.uxml, MainUI.uss, AppUI.cs, AppController.cs (new); ARStartupGate.cs, GeospatialStatus.cs (rewritten)

### Prompt 25

**Prompt:**

> should we now remove the Geospatial Status script from XR Origin since it now lives in App

**Attachments:** None

**What was done:**

- Confirmed that the copy on the XR Origin should be removed, explained why two copies would conflict, and how to remove it.

**Files created or changed:** None

### Prompt 26

**Prompt:**

> it all works. I see that, if we don't provide map access, but provide camera access, the camera doesn't open. is this intended so that we don't have problems showing the model without gps permisions or is this a bug?

**Attachments:** None

**What was done:**

- Read the ARCore Extensions source and identified a bug: with Geospatial enabled and no precise location, ARCore rejects the session configuration, so the camera never starts.
- Changed the gate to start AR with Geospatial off (on a runtime copy of the config) when location is missing, and added a method to switch Geospatial on later.
- Updated the overlay to show "Geospatial: off" in that case.

**Files created or changed:** ARStartupGate.cs, GeospatialStatus.cs (updated)

### Prompt 27

**Prompt:**

> the camera now works even with not location permisions. I see that that, if I have the settings open and revoke the locations permisions, the aplication autorestarts with the new permisions, but if I add them back, the application doesn't restart, the status of the permisions in updated, but the geospation information are not shown untill I restart manually the app.

**Attachments:** None

**What was done:**

- Explained that Android restarts an app when a permission is revoked but not when one is granted.
- Added a check on returning to the app: switch Geospatial on if location is now allowed, and retry from the blocked card if the camera is now allowed.

**Files created or changed:** ARStartupGate.cs (updated; later reverted in prompt 29)

### Prompt 28

**Prompt:**

> now the application blocks after I give it location permissions

**Attachments:** None

**What was done:**

- Attributed the freeze to changing the session configuration while the session was resuming, and delayed the switch until the session had been tracking for about one second.
- Asked for Logcat output and whether "blocks" meant a freeze or the blocked card.

**Files created or changed:** ARStartupGate.cs (updated; later reverted in prompt 29)

### Prompt 29

**Prompt:**

> the freze is still here. it seams that the change we did to make the application auto update the status of the location permision without having to relaunch the app broke some things. i sugest to just reverse the changes and get back to the stable version, and, maybe, try another aproch to make the location auto apdate after we give the location permisions from settings

**Attachments:** None

**What was done:**

- Reverted the gate to the stable version from prompt 26 and marked the runtime Geospatial switch method as untested and not to be called.
- Proposed an alternative (briefly restarting AR with the new config) and a simpler fallback (ask the user to restart), and asked whether to implement it.

**Files created or changed:** ARStartupGate.cs (reverted)

### Prompt 30

**Prompt:**

> let's just use a more bulletbroof aproach. make both permissions mandatory. if one of them is revoked mid play, we delete all the information of the current session (the saved data will remain, when we get to saving data) and put the application in a state that forces the user to close and reopen the application. if we don't have the permisions at launch, we ask for them until they are given. if not, we don't let the user open the app. we will provide a close button for tha state of the app that forces a shut down because of the revoke of the permissions mid play.

**Attachments:** None

**What was done:**

- Checked the ARCore Extensions source and found that Android 12 and newer require precise and approximate location to be requested together.
- Rewrote the gate with mandatory camera and precise location: a blocked card until both are granted, auto-start after returning from settings, a once-per-second check while running, a final "Permission removed" state with a Close app button, a session-invalidated event, and an Editor test command.
- Updated the state machine (SessionEnded state, ClearSession placeholder), the UI controller and the layout (Close app button), and simplified the overlay.
- Gave a test plan.

**Files created or changed:** ARStartupGate.cs (rewritten); AppController.cs, AppUI.cs, MainUI.uxml, GeospatialStatus.cs (updated)

### Prompt 31

**Prompt:**

> now it works and we don't have to worry about lose of permissions mid works session. before we go further, I have to admit that the work process is rather slow and can be very flowed because you can very easy lose information about the current context in unity, like we did last time with the extra scrpit atached to the XR. for this I sugest we moce to CLI. I already used claude code cli and I'm happy with it. however, with unity, there are a bunch of things that claude code cli cannot do because we have an editor and not just files like a usual project in vs code. I saw you could use the unity cli to make changes in unity and that claude code cli can use the unity cli. is this a valid option for us and, if yes, how can we set it up?

**Attachments:** None

**What was done:**

- Researched the Unity CLI, the Unity Pipeline package, the deprecation of Unity's in-Editor MCP server, and the community alternative MCP for Unity.
- Confirmed it is a valid option and explained what it changes and what stays manual.
- Wrote a CLAUDE.md project file with the stack, scene, scripts, model orientation, hard-won rules, agreed UX plan and open items.
- Gave setup steps: git, installing the Unity CLI, connecting it to the project, the agent skill, adb on the PATH, and CLAUDE.md.

**Files created or changed:** CLAUDE.md

### Prompt 32

**Prompt:**

> [pasted: PowerShell output of `unity pipeline list` (unknown command), `unity status` (Editor ready on port 7800) and `unity command eval` returning 0.1.0, about 20 lines]

**Attachments:** None

**What was done:**

- Confirmed the Unity CLI connection worked.
- Explained that `unity pipeline list` does not exist in this CLI version, and that 0.1.0 is the app's own version (Application.version), not the Unity version.
- Added a Unity CLI section to CLAUDE.md and listed the remaining setup steps.

**Files created or changed:** CLAUDE.md (updated)

### Prompt 33

**Prompt:**

> I never used my phone to build directly to it on this project, so I don't have this set up. how can we do it?

**Attachments:** None

**What was done:**

- Explained how to turn on Developer options and USB debugging, add Unity's adb to the PATH, authorize the PC, handle drivers, build and run from Unity, and read Logcat.
- Mentioned wireless debugging as an option.
- Added a Phone (adb) section to CLAUDE.md.

**Files created or changed:** CLAUDE.md (updated)

### Prompt 34

**Prompt:**

> after connecting the phone

**Attachments:** Screenshot of the Unity Console with repeated warnings "Unauthorized device detected, Id: [redacted], please add debug authorization and reconnect."

**What was done:**

- Explained that the phone had not yet accepted the "Allow USB debugging?" prompt.
- Gave steps to make the prompt appear again (unlock the phone, revoke authorizations, restart adb, replug) and to check Samsung's Auto Blocker.

**Files created or changed:** None

## Outputs used in the project

| Output | Made in prompt | Notes |
|---|---|---|
| MachetaOrientation.cs | 1 | First approach; superseded, deletion recommended in prompt 24 |
| MachetaTapPlacer.cs | 1 | First approach; superseded, deletion recommended in prompt 24 |
| MachetaGeoPlacer.cs | 1 | First approach; superseded, deletion recommended in prompt 24 |
| GeospatialStatus.cs | 17, 19, 24, 26, 30 | Diagnostic overlay; final version from prompt 30 |
| ARStartupGate.cs | 19, 20, 21, 24, 26, 27, 28, 29, 30 | Startup gate; versions from prompts 27 and 28 caused a freeze and were reverted in prompt 29; final version from prompt 30, confirmed working in prompt 31 |
| MainUI.uxml | 24, 30 | UI Toolkit layout; final version from prompt 30 |
| MainUI.uss | 24 | UI Toolkit styles; unchanged after prompt 24 |
| AppUI.cs | 24, 30 | UI controller; final version from prompt 30 |
| AppController.cs | 24, 30 | App state machine; final version from prompt 30 |
| CLAUDE.md | 31, 32, 33 | Project context file for Claude Code; latest version from prompt 33 |
