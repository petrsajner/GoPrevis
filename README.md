# Previs 0.3.1 — film shot planning

Previs is a desktop blockout tool for preparing movement and camera references for AI video. It uses Godot 4.7.2 .NET, C# and .NET 10. The application UI, dialogs and authored errors are English.

## Run

Open `build/Previs-0.3.1-Windows/Previs.exe`. Keep Previs.exe, Previs.pck and data_Previs_windows_x86_64 together. The portable Windows build includes its runtime; using it does not require the Godot editor or .NET SDK.

For development, open project.godot with Godot 4.7.2 .NET. Local tools are in the ignored .tools directory. The existing run.ps1 starts the source project; build.ps1 -Test builds, runs source checks and exports Windows x64.

## The main workflow

1. **Drag from a library.** Drag a person, animal or prop into the scene. Drag a card from Motions onto a compatible actor to append that motion. Dropping a motion on empty ground creates its matching actor there. Clicking a card is also available.
2. **Place it and key it.** Select an object or camera in SCENE, in the viewport, or through its timeline track. Adjust Position, Rotation and Scale in Object. Press **K** or **Key transform** to capture them together; the **◇** button beside a parameter captures all three axes of that parameter.
3. **Move in time and adjust.** Scrub to another frame and change the values. A channel that already has keys creates or updates a key at the current frame. Auto key also allows this on a previously unkeyed channel. Play to review the shot.

Transform tracks are collapsed by default. Expand an object's **Transform** row to reveal **Position, Rotation and Scale**; a camera also exposes **Focal length**. Each Position/Rotation/Scale diamond stores XYZ together. Drag a diamond to retime it, right-click one to remove it, or double-click an empty parameter lane to add a key.

## Motions and animals

Motions is a small prepared library, with Human/Animals filters, search and animated hover previews. It currently contains **five motions**:

| Actor family | Motion | Source |
| --- | --- | --- |
| Human | Walk & gesture | Original synthetic test motion |
| Human | Captured walk | Recorded CMU walk, Bruce Hahne conversion |
| Fox | Walk | Fox's supplied native animation |
| Fox | Run | Fox's supplied native animation |
| Fox | Look around | Fox's native Survey animation |

The Fox is an animated animal model, not the static dog prop. Its native animation cycles are not presented as recorded mocap. Drag it from Animals, or drop one of its motion cards onto empty ground. Human motions apply to the prepared human rig; Fox cards apply to the prepared Fox family. An incompatible drop does not retarget between species.

Import a personal BVH through **File → Import motion…** or the import action in Motions. Supported humanoid names include standard Mixamo/CMU-style skeletons. The bundled CMU source uses 0.055 metres per source unit; the synthetic source uses 0.01. Source scale is editable per clip.

Attribution for the Fox model, rigging, animation and conversion is in LICENSES/Fox-NOTICE.md. The CMU source and conditions are in samples/real/SOURCE-AND-LICENSE.md.

## Arrange motion, paths and corrections

The evaluation order is fixed:

**Blend source motion → apply path travel/orientation → apply manual transform corrections.**

Mocap or native skeletal sources blend before the final object transform. Path heading is derived from the final blended pose. Static manual rotation and animated rotation corrections remain additive while the actor follows a path, including throughout a crossfade. Path translation is applied once: horizontal source root displacement is suppressed for supported path-driven tracks.

### Clips

Click a motion clip to open Motion. Drag its body to move it or its right edge to trim duration. Overlap neighbouring clips to crossfade. Start, Duration, Source offset, Blend in, Speed, Loop source and In-place clip are available for detailed editing. The prepared Fox's native clips can also be sequenced and blended.

### Paths

Select an actor, click **Draw path**, then place ground points. **Finish** or Enter applies the curve; Esc restores the previous path. Ctrl snaps points to 0.25 metres. The separate **actor · Path** row shows P1/P2/P3… keys. Drag a key to change arrival time at that point, or edit Arrival time in Path. Path start shifts all arrivals; Travel time rescales them.

Face travel direction can stay enabled while manual Rotation offsets or keys add a turn above the path. With a path enabled, Object Position values are corrections above its evaluated position. They **do not move the curve geometry**. Use Draw / replace to redraw the curve.

## Cameras are scene objects

Camera A/B/C appear in the unified SCENE list and can be picked in the viewport. Selecting their timeline track also opens their Object inspector. Position, Rotation, Scale and Lens can be changed and animated.

Choose a framing or movement preset from the camera libraries, or use the working view and camera controls. Preset/legacy camera animation is converted into editable channels. Camera keys and lenses are independent of other cameras. Camera view pilots the active shot; its 16:9 guide matches the preview. The small Preview control can hide the preview when more inspector room is needed.

## Compact desktop layout

The interface responds to available window space rather than stretching a tablet-sized canvas across the monitor. Side docks and the timeline are resizable. File/Edit menus hold secondary actions, and the Finish control appears only while drawing.

Layout measurements are in **logical UI pixels**. At 2560 × 1440 the checked header is 35 px high, the play button is 31 px, and the 3D viewport is 1967 × 1136 with default docks. OS DPI remains respected; a 3840-pixel physical width at 150% scale corresponds to a 2560-pixel logical width. Growing the window enlarges the work area rather than every button.

## Save, reopen and export

Save creates a **version 3 .previs** project and neighbouring .assets folder containing used sources. Keep both together when moving a project. It preserves motion tracks, paths and path keys, final transform channels, camera channels/lenses, active camera and lighting. Version 1/2 scenes are migrated on opening. Overwriting an existing project keeps the previous JSON as .previs.bak.

Export frame produces a clean **1920 × 1080 PNG from the active camera**, with JSON shot data. Editor grid, path guides, camera helpers and selection outlines are excluded. Current export is PNG/JSON; video and control passes remain future work.

## Navigation and lighting

- LMB selects; Move drags on the ground. Ctrl snaps to 0.25 metres.
- RMB orbits; RMB + WASD/QE flies, with Shift for faster travel.
- MMB pans; Wheel dollies/zooms; F frames selection.
- Space plays/pauses. K captures the selected transform.
- Ctrl+S/O saves/opens. Ctrl+D duplicates. Ctrl+Z/Y undoes/redoes. Delete removes selection.
- Draw path uses LMB points, Enter to finish and Esc to cancel.

The scene and library previews use neutral hemisphere illumination, with **no directional light enabled by default**. World → Directional light adds an optional shadow-casting light. The project saves that choice.

## Current limits

- The supplied motion library is curated and small: two human sources and three Fox cycles, not a complete dataset catalogue.
- Humanoid BVH retargeting uses a fixed 21-bone mannequin. There is no universal animal retargeting, finger animation, foot-contact IK, automatic foot-slip correction, collision avoidance or prop-contact solving.
- Match mocap Speed and path arrival times to the intended gait. Keyed path timing does not itself solve foot contacts.
- Imported FBX/GLB/glTF models preserve their own rigs and embedded clips. Native skeletal sequencing works on supported skeletons; library compatibility is restricted to prepared actor families.
- Use self-contained GLB for portable models. External glTF dependencies are not bundled; saving such a model is refused. FBX may require embedded textures.
- The dog remains a static prop; the Fox is the supplied animated animal.
- Camera cuts, video plates, video/depth/edge/mask export and direct AI generation are not implemented.
- Windows x64 is the verified development platform. macOS/Linux builds have not been verified.

See VALIDATION.md for the exact checks and final delivery status.
