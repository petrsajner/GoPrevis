# Previs 0.7.0 — film shot planning

Previs is a desktop blockout tool for preparing movement and camera references for AI video. It uses Godot 4.7.2 .NET, C# and .NET 10. The application UI, dialogs and authored errors are English.

## Run

The Windows portable output is `build/Previs-0.7.0-Windows/Previs.exe`. Keep Previs.exe, Previs.pck and data_Previs_windows_x86_64 together. The portable Windows build includes its runtime; using it does not require the Godot editor or .NET SDK.

For development, open project.godot with Godot 4.7.2 .NET. Local tools are in the ignored .tools directory. The existing run.ps1 starts the source project; build.ps1 -Test builds, runs source checks and exports Windows x64.

## The main workflow

1. **Drag from a library.** Drag a person, animal or prop into the scene. Drag a card from Motions onto a compatible actor to append that motion. Dropping a motion on empty ground creates its matching actor there. Clicking a card is also available.
2. **Place it and key it.** Select an object or camera in SCENE, in the viewport, or through its timeline track. Adjust Position, Rotation and Scale in Object. Press **K** or **Key transform** to capture them together; the **◇** button beside a parameter captures all three axes of that parameter.
3. **Move in time and adjust.** Scrub to another frame and change the values. A channel that already has keys creates or updates a key at the current frame. Auto key also allows this on a previously unkeyed channel. Play to review the shot.

In **Select** mode, hold **Shift** to show the complete transform gizmo for the selected object or camera: **arrows move**, **cubes scale**, and **rings rotate**. The Y arrow changes height freely; this is not a floor-constrained drag. The active drag continues until mouse release even if Shift is released first. Each drag is one Undo/Redo action. Existing transform keys and path corrections remain the values being edited.

Transform tracks are collapsed by default. Expand an object's **Transform** row to reveal **Position, Rotation and Scale**; a camera also exposes **Focal length**. Each Position/Rotation/Scale diamond stores XYZ together. Drag a diamond to retime it, right-click one to remove it, or double-click an empty parameter lane to add a key.

## Model and motion libraries

The library contains **284 ready-to-place model entries**: 255 new props, 12 new rigged animals, the original animated Fox, six prepared human mannequin variants and ten original procedural blockout models. The new props are organized as follows:

| Category | Models |
| --- | ---: |
| Furniture | 71 |
| Vehicles | 20 |
| Architecture | 63 |
| Nature | 55 |
| Props | 46 |

The new animals are Alpaca, Bull, Cow, Deer, Donkey, Fox, Horse, White Horse, Husky, Shiba Inu, Stag and Wolf. The selection includes breed and color variants. Models are bundled locally and use placement scales appropriate for a metre-based scene.

**Motions has 457 cards**:

| Prepared actors | Cards | Content |
| --- | ---: | --- |
| Human | 302 | 300 newly bundled recorded CMU takes, plus the existing synthetic test and captured-walk sample cards |
| New animals | 152 | Quaternius native skeletal animation cycles |
| Original Fox | 3 | Walk, Run and Look around (native Survey) |

The 300 newly bundled CMU takes provide approximately **85 minutes of recorded human motion**, including locomotion, sports, interactions, actions, dance, climbing, everyday activities, sitting and gestures. The 152 new animal clips are **authored animation cycles, not recorded mocap**.

Use library sections, category filters and search to narrow the library. Pages contain 12 cards. PNG covers show the actual models or sampled poses; animated hover previews initialize lazily. Drag an animal into the scene, or drop one of its motion cards onto empty ground to create the matching actor. Human motions apply to the prepared human rig; native animal cards apply to their matching prepared actor family. An incompatible drop does not retarget between species or unrelated rigs.

Import a personal BVH through **File → Import motion…** or the import action in Motions. Supported humanoid names include standard Mixamo/CMU-style skeletons. Bundled CMU sources use 0.055 metres per source unit; the synthetic source uses 0.01. Source scale is editable per clip.

The CMU cards use a source offset that skips the conversion's first calibration T-pose without modifying the BVH files. Their displayed duration excludes that frame, and **Loop source defaults to off** because recorded takes are not guaranteed to loop seamlessly. Several takes contain multiple actions; trim the desired section on the timeline.

Sources, licenses and preparation notes are in [LICENSES/PROP-CONTENT.md](LICENSES/PROP-CONTENT.md), [LICENSES/ANIMAL-CONTENT.md](LICENSES/ANIMAL-CONTENT.md) and [LICENSES/CMU-MOTION-CONTENT.md](LICENSES/CMU-MOTION-CONTENT.md). The original Fox has separate attributions in [LICENSES/Fox-NOTICE.md](LICENSES/Fox-NOTICE.md); the original walk sample is documented in [samples/real/SOURCE-AND-LICENSE.md](samples/real/SOURCE-AND-LICENSE.md).

## Static poses

**Poses** contains **118 ready-made human body poses** in nine categories: standing and gestures, chair and floor sitting, kneeling, crouching, lying down and action/interaction positions. Search by name or category and drag a pose onto a prepared human figure. Dropping a pose onto empty ground creates a posed average-adult figure there. The other human body variants retain their proportions when a pose is applied.

A posed figure holds its shape for the entire scene without a motion clip. Applying a pose replaces that figure's current source clips; Undo restores the previous state. Position, Rotation, Scale, drawn paths and their manual correction keys remain available above the body pose.

The Poses library can also **save the current body pose** from a selected figure, including a frame sampled from mocap. Saving a preset leaves the figure's existing animation intact. The saved preset can then be applied to another figure as a static pose. Custom presets are stored locally, and a project's applied body pose is embedded in the `.previs` file, so opening a static figure does not require its original motion source or the creator's preset library. **Reset pose** in the library restores the default standing body; if source clips are present, they continue playing.

The self-contained `samples/static-poses.previs` example includes a seated figure and chair, floor sitting, kneeling and lying figures. It contains no motion clips or external asset dependencies.

Seating presets are prepared around practical blocking proportions. Match the figure's placement to the actual chair, stool or floor; smaller body variants scale the pose as well. The manual pose editor below can adapt them to individual props; automatic seat/contact fitting is not provided. Existing animal animations retain their own rig compatibility.

## Adjust a body pose

Select a prepared human figure and choose **Pose** in the viewport toolbar (or **Edit body pose** in Object). The compact Pose inspector appears for humans only. If the figure is animated, entering an edit freezes the displayed frame into a static pose and keeps the figure at its visible world position. Undo can restore its source clips.

Grab the actual hand or foot in the 3D view and drag: the whole arm or leg follows through its connected joints. The viewport also provides joint markers and XYZ handles for targeted adjustments. **Rotate** turns the head, spine, shoulders, elbows, knees or any other prepared joint; drag its center/rings. **Move** repositions hands, feet and pelvis using the mesh, center handle or world XYZ arrows. Hold **Shift before grabbing** to temporarily switch the current **Move/Rotate** operation: a hand or foot normally moves, so Shift rotates its wrist/ankle; an intermediate joint normally rotates, so Shift moves it. The chosen operation is latched when the mouse is pressed and remains stable until release. **W** explicitly chooses Move and **E** chooses Rotate for the selected part. Selecting that same hand again retains an explicit Rotate choice.

Elbow and knee movement changes the limb's bend while retaining the hand or foot target. Other intermediate joints move within their parent connection's fixed length. Hand and foot moves use two-bone IK with fixed limb lengths and retain the chosen end-joint orientation. Expand **Precise values** only when numeric local angles or world coordinates are needed. **Alt** makes a pose drag finer; **Ctrl** snaps movement or angles. Orbit, pan and zoom remain available while editing.

To fit a particular chair, first place the whole figure with Object Position/Rotation. Move the pelvis directly, or expand **Precise values** and adjust **Hip height (m)**. **Keep feet in place** pins both feet while the pelvis moves within leg reach. If a requested position lies beyond that reach, the move is limited rather than stretching the legs; disable the pin to reposition the whole pose, then place each foot explicitly. Rotate the feet or hands separately when their angle needs correction.

Enter a name and choose **Save to My poses** to store the adjusted pose as a reusable library item with a rendered thumbnail. Intentional XYZ pelvis offsets and local joint rotations are retained; the scene object's separate world placement and body proportions are not baked into the preset. Applying it to another prepared human keeps that target's proportions. **Reset edits** returns to the pose at the start of the editing session; **Done** leaves the adjusted static figure in the scene. Each continuous drag is one Undo/Redo step. `samples/manual-adjusted-pose.previs` contains a manually fitted figure and a real imported chair. Joint edits are static body-pose changes, not skeletal animation keys.

## Arrange motion, paths and corrections

The evaluation order is fixed:

**Blend source motion → apply path travel/orientation → apply manual transform corrections.**

Mocap or native skeletal sources blend before the final object transform. Path heading is derived from the final blended pose. Static manual rotation and animated rotation corrections remain additive while the actor follows a path, including throughout a crossfade. Path translation is applied once: horizontal source root displacement is suppressed for supported path-driven tracks.

### Clips

Click a motion clip to open Motion. Drag its body to move it. Drag the **left edge to trim IN**, or the **right edge to trim OUT**. Trimming IN advances the source offset by the removed timeline interval multiplied by clip Speed, while retaining both its timeline OUT and source OUT. Extending the left edge stops at source time zero; IN cannot cross OUT or leave less than one scene frame. Moving the clip body leaves its source IN unchanged. Selected trim handles remain available through an overlapping crossfade. Hover a handle to see its source IN/OUT times. Overlap neighbouring clips to crossfade. Start, Duration, Source offset, Blend in, Speed, Loop source and In-place clip are available for detailed editing. Prepared animals' native clips can also be sequenced and blended within their compatible family. Scene duration can be extended to **600 seconds**.

### Paths

Select an actor, click **Draw path**, then place ground points. **Finish** or Enter applies the curve; Esc restores the previous path. Ctrl snaps points to 0.25 metres. The separate **actor · Path** row shows P1/P2/P3… keys. Drag a key to change arrival time at that point, or edit Arrival time in Path. Path start shifts all arrivals; Travel time rescales them.

Face travel direction can stay enabled while manual Rotation offsets or keys add a turn above the path. With a path enabled, Object Position values are corrections above its evaluated position. They **do not move the curve geometry**. Use Draw / replace to redraw the curve.

## Cameras are scene objects

Camera A/B/C appear in the unified SCENE list. Click a visible camera body or helper line in the viewport to select it, including while leaving human pose editing. Selecting its timeline track also opens its Object inspector. **Move** can drag the camera in the current view plane, including vertically. For constrained positioning, select **Select**, hold **Shift**, then use its XYZ arrows, scale cubes or rotation rings. These controls also work above existing camera animation channels. Position, Rotation, Scale and Lens can be changed and animated.

Choose a framing or movement preset from the camera libraries, or use the working view and camera controls. Preset/legacy camera animation is converted into editable channels. Camera keys and lenses are independent of other cameras. Camera view pilots the active shot; its 16:9 guide matches the preview. The small Preview control can hide the preview when more inspector room is needed.

## Compact desktop layout

The interface responds to available window space rather than stretching a tablet-sized canvas across the monitor. Side docks and the timeline are resizable. File/Edit menus hold secondary actions, and the Finish control appears only while drawing.

Layout measurements are in **logical UI pixels**. At 2560 × 1440 the checked header is 35 px high, the play button is 31 px, and the 3D viewport is 1967 × 1136 with default docks. OS DPI remains respected; a 3840-pixel physical width at 150% scale corresponds to a 2560-pixel logical width. Growing the window enlarges the work area rather than every button.

## Precise numeric fields

The arrow area of a numeric field is also a vertical scrub control: **click for one small step, or hold and drag up/down**. Up increases the value and down decreases it at a constant sensitivity; horizontal travel does not accelerate it. Typical arrow steps are **0.001 m for position**, **0.1 degrees for rotation**, and **0.001 for scale**. Values remain rounded to that field's step. **Alt** gives a finer drag, and **Escape** cancels the current scrub.

A continuous numeric scrub creates one Undo action. After reaching a minimum or maximum, dragging the other way works immediately; excess mouse travel is not accumulated beyond the bound. Right-clicking the arrow area does not jump to the minimum or maximum. Direct text and expression entry remain available in the main text area.

## Save, reopen and export

Save creates a **version 4 .previs** project and neighbouring .assets folder containing used sources. Keep both together when moving a project. It preserves embedded static body poses, motion tracks, paths and path keys, final transform channels, camera channels/lenses, active camera and lighting. Version 1/2/3 scenes are migrated on opening. Overwriting an existing project keeps the previous JSON as .previs.bak.

Export frame produces a clean **1920 × 1080 PNG from the active camera**, with JSON shot data. Editor grid, path guides, camera helpers and selection outlines are excluded. Current export is PNG/JSON; video and control passes remain future work.

## Navigation and lighting

- LMB selects. Move drags ordinary scene objects on the ground and cameras in the view plane. In Select, hold Shift for unrestricted XYZ move/scale/rotation handles; the Y arrow lifts an object or camera. Ctrl snaps the ordinary ground move to 0.25 metres.
- RMB orbits; RMB + WASD/QE flies, with Shift for faster travel.
- MMB pans; **Alt + MMB drag zooms** without a mouse wheel. Drag up to move closer and down to move farther away. Wheel zoom also remains available; F frames selection.
- In Pose: **Shift** temporarily swaps Move/Rotate; **W/E** choose Move/Rotate explicitly. **Alt** is fine adjustment.
- Space plays/pauses. K captures the selected transform.
- Ctrl+S/O saves/opens. Ctrl+D duplicates. Ctrl+Z/Y undoes/redoes. Delete removes selection.
- Draw path uses LMB points, Enter to finish and Esc to cancel.

The scene and library previews use neutral hemisphere illumination, with **no directional light enabled by default**. World → Directional light adds an optional shadow-casting light. The project saves that choice.

## Current limits

- The recorded library is a curated CMU subset, not the complete database. Source quality and take lengths vary; no motion cleanup or seamless looping is implied.
- Humanoid BVH retargeting uses a fixed 21-bone mannequin. There is no universal animal retargeting, finger animation, automatic animation foot-contact/foot-slip solving, collision avoidance or automatic prop-contact solving.
- Match mocap Speed and path arrival times to the intended gait. Keyed path timing does not itself solve foot contacts.
- Imported FBX/GLB/glTF models preserve their own rigs and embedded clips. Native skeletal sequencing works on supported skeletons; library compatibility is restricted to prepared actor families.
- Use self-contained GLB for portable models. External glTF dependencies are not bundled; saving such a model is refused. FBX may require embedded textures.
- The original Dog blockout model remains a static prop. Husky and Shiba Inu are supplied rigged dogs with native animation cycles.
- Camera cuts, video plates, video/depth/edge/mask export and direct AI generation are not implemented.
- Windows x64 is the verified development platform. macOS/Linux builds have not been verified.

See VALIDATION.md for the exact checks and final delivery status.
