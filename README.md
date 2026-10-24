# Previs 0.8.0 — film shot planning

Previs is a desktop blockout tool for preparing movement and camera references for AI video. It uses Godot 4.7.2 .NET, C# and .NET 10. The application UI, dialogs and authored errors are English.

## Run

The Windows portable output is `build/Previs-0.8.0-Windows/Previs.exe`. Keep Previs.exe, Previs.pck and data_Previs_windows_x86_64 together. The portable Windows build includes its runtime; using it does not require the Godot editor or .NET SDK.

For development, open project.godot with Godot 4.7.2 .NET. Local tools are in the ignored .tools directory. The existing run.ps1 starts the source project; build.ps1 -Test builds, runs source checks and exports Windows x64.

## One placement workflow

**Arrange** combines selection and movement. Click an object or camera, then drag its visible body or selection box to move it in the current view plane. Height is available immediately. Hold **Shift** to use the complete XYZ transform gizmo: arrows move, cubes scale and rings rotate. There is no separate Select/Move workflow and no ground-only object drag. Each continuous drag is one Undo/Redo action.

Use **Pose** on a selected human to edit its body at the playhead. The inspector shows the current task and a back control rather than separate Object/Motion/Path/Pose tabs. Selecting a clip, path key, camera or figure opens the relevant context.

The left library has exactly five sections:

| Library | Visible groups and purpose |
| --- | --- |
| Models | People and Animals in one scrollable library |
| Objects | Furniture, Vehicles, Architecture, Nature and Props |
| Poses | Human poses arranged by posture and activity |
| Motions | Prepared human and animal motion clips |
| Cameras | Scene cameras, framing choices and camera movement presets |

The SCENE object list remains available below the library; Scene is no longer a separate library tab. Models and Objects use visible group headings, category filters and search. Camera moves live with Cameras.

Drag a model or object into the scene, adjust it with Arrange, and drag a compatible motion onto a figure when animation is needed. Dropping a motion onto empty space creates its matching figure. The application remains entirely English.

Object **Transform** tracks remain collapsed by default. Expand one to see grouped XYZ **Position, Rotation and Scale**; cameras also expose **Focal length**. Outside Pose mode, **K** or **Key transform** captures object transforms. Drag timeline diamonds to retime them or right-click to remove them.

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

Use visible library groups, category filters and search to narrow the library. Long grouped lists scroll; motion/pose card pages remain bounded for performance. PNG covers show the actual models or sampled poses; animated hover previews initialize lazily. Drag an animal into the scene, or drop one of its motion cards onto empty ground to create the matching actor. Human motions apply to the prepared human rig; native animal cards apply to their matching prepared actor family. An incompatible drop does not retarget between species or unrelated rigs.

Import a personal BVH through **File → Import motion…** or the import action in Motions. Supported humanoid names include standard Mixamo/CMU-style skeletons. Bundled CMU sources use 0.055 metres per source unit; the synthetic source uses 0.01. Source scale is editable per clip.

The CMU cards use a source offset that skips the conversion's first calibration T-pose without modifying the BVH files. Their displayed duration excludes that frame, and **Loop source defaults to off** because recorded takes are not guaranteed to loop seamlessly. Several takes contain multiple actions; trim the desired section on the timeline.

Sources, licenses and preparation notes are in [LICENSES/PROP-CONTENT.md](LICENSES/PROP-CONTENT.md), [LICENSES/ANIMAL-CONTENT.md](LICENSES/ANIMAL-CONTENT.md) and [LICENSES/CMU-MOTION-CONTENT.md](LICENSES/CMU-MOTION-CONTENT.md). The original Fox has separate attributions in [LICENSES/Fox-NOTICE.md](LICENSES/Fox-NOTICE.md); the original walk sample is documented in [samples/real/SOURCE-AND-LICENSE.md](samples/real/SOURCE-AND-LICENSE.md).

## Static poses

**Poses** contains **118 ready-made human body poses** in nine categories: standing and gestures, chair and floor sitting, kneeling, crouching, lying down and action/interaction positions. Search by name or category and drag a pose onto a prepared human figure. Dropping a pose onto empty ground creates a posed average-adult figure there. The other human body variants retain their proportions when a pose is applied.

A posed figure holds its shape for the entire scene without a motion clip. Applying a library pose deliberately replaces that figure's current source clips and clears its correction tracks; Undo restores the previous state. Entering the Pose editor itself preserves source clips. Position, Rotation, Scale, drawn paths and their manual correction keys remain available above the body pose.

The Poses library can also **save the current body pose** from a selected figure, including a frame sampled from mocap. Saving a preset leaves the figure's existing animation intact. The saved preset can then be applied to another figure as a static pose. Custom presets are stored locally, and a project's applied body pose is embedded in the `.previs` file, so opening a static figure does not require its original motion source or the creator's preset library. **Reset pose** in the library restores the default standing body; if source clips are present, they continue playing.

The self-contained `samples/static-poses.previs` example includes a seated figure and chair, floor sitting, kneeling and lying figures. It contains no motion clips or external asset dependencies.

Seating presets are prepared around practical blocking proportions. Match the figure's placement to the actual chair, stool or floor; smaller body variants scale the pose as well. The manual pose editor below can adapt them to individual props; automatic seat/contact fitting is not provided. Existing animal animations retain their own rig compatibility.

## Keyed body corrections above motion

Select a prepared human and choose **Pose** in the viewport toolbar, or **Edit body pose** in its properties. Existing mocap clips, offsets, blends and speed stay active. Entering the editor does not freeze or replace the animation.

Grab a hand or foot and drag to solve its connected limb. Drag the head or another joint to rotate it. Hold **Shift before grabbing** to temporarily exchange Move and Rotate: hands/feet can rotate at the wrist/ankle, and intermediate joints can move. **W/E** choose Move/Rotate explicitly. The operation is latched at mouse-down until release. **Alt** gives a finer drag; **Ctrl** snaps. Precise values are available in a collapsed section.

Dragging an elbow runs IK **upward** through its shoulder chain. The forearm and hand retain their local poses and follow the elbow; they are not held at their old world positions. A moved knee similarly turns the hip connection and carries its shin and foot with it while the pelvis remains stable. Bone lengths remain fixed. When moving the pelvis itself, **Keep feet in place** can pin both feet within the legs' reachable range.

**Every body edit writes correction keys at the current playhead.** Move to a later frame and turn the head or lift an arm to animate that correction over the continuing source motion. A first edit later in the shot adds an identity correction at time zero, so the source starts unchanged and transitions into the edited pose. Rotation corrections use normalized local quaternions; root/pelvis corrections use local XYZ offsets.

**Key joint** captures the selected joint correction. **Key pose**, or **K while posing**, captures the current pose corrections. On the timeline, expand the figure's **Pose corrections** group to see its keyed joints and root offset. Each joint diamond stores the local rotation together; the root diamond stores XYZ together. Drag diamonds to retime; right-click removes a key. These corrections are separate from motion clips and object Transform keys.

The blend order is **blended mocap or static base → local pose corrections → path and final object transforms**. Path heading is derived from the uncorrected blended source, so a keyed manual pelvis turn remains visible rather than being cancelled by path alignment. Scrubbing and reverse playback reevaluate from the source rather than accumulating earlier corrections.

Enter a name and use **Save to My poses** to store the currently visible corrected body as a reusable static library item. Saving does not replace the scene's mocap. The preset retains intentional body offsets and local joint rotations, while separate scene-object placement and proportions remain with the target figure. **Reset edits** restores the correction layer from the start of the editing session; **Done** leaves its keys active.

`samples/manual-adjusted-pose.previs` contains a fitted figure and imported chair. For a specific seat, move the whole figure with Arrange, then adjust its pelvis and limbs in Pose. Automatic prop-contact fitting and animation foot-contact solving are not provided.

## Arrange motion, paths and corrections

The evaluation order is fixed:

**Blend source motion → apply keyed body-pose corrections → apply path travel/orientation and final object transform corrections.**

Mocap or native skeletal sources blend before the final object transform. Path heading is derived from the final blended pose. Static manual rotation and animated rotation corrections remain additive while the actor follows a path, including throughout a crossfade. Path translation is applied once: horizontal source root displacement is suppressed for supported path-driven tracks.

### Clips

Click a motion clip to open Motion. Drag its body to move it. Drag the **left edge to trim IN**, or the **right edge to trim OUT**. Trimming IN advances the source offset by the removed timeline interval multiplied by clip Speed, while retaining both its timeline OUT and source OUT. Extending the left edge stops at source time zero; IN cannot cross OUT or leave less than one scene frame. Moving the clip body leaves its source IN unchanged. Selected trim handles remain available through an overlapping crossfade. Hover a handle to see its source IN/OUT times. Overlap neighbouring clips to crossfade. Start, Duration, Source offset, Blend in, Speed, Loop source and In-place clip are available for detailed editing. Prepared animals' native clips can also be sequenced and blended within their compatible family. Scene duration can be extended to **600 seconds**.

### Paths

Select an actor, click **Draw path**, then place ground points. **Finish** or Enter applies the curve; Esc restores the previous path. Ctrl snaps points to 0.25 metres. The separate **actor · Path** row shows P1/P2/P3… keys. Drag a key to change arrival time at that point, or edit Arrival time in Path. Path start shifts all arrivals; Travel time rescales them.

Face travel direction can stay enabled while manual Rotation offsets or keys add a turn above the path. With a path enabled, Object Position values are corrections above its evaluated position. They **do not move the curve geometry**. Use Draw / replace to redraw the curve.

## Cameras use the same controls

Camera A/B/C appear in the SCENE list and can be picked through their visible bodies or helper lines. Arrange selects and drags cameras using the same view-plane movement as other objects. Hold Shift for constrained XYZ movement, scale and rotation. Existing camera animation channels remain editable.

The **Cameras** library contains the camera list, framing presets and camera movement presets together. **Camera view** pilots the active shot; the 16:9 guide matches the preview. Position, Rotation, Scale and Lens are keyable. **Preview** can hide the small shot view to provide more inspector space.

## Compact desktop layout

The interface responds to available window space rather than stretching a tablet-sized canvas across the monitor. Side docks and the timeline are resizable. File/Edit menus hold secondary actions, and the Finish control appears only while drawing.

Layout measurements are in **logical UI pixels**. At 2560 × 1440 the checked header is 35 px high, the play button is 31 px, and the 3D viewport is 1967 × 1136 with default docks. OS DPI remains respected; a 3840-pixel physical width at 150% scale corresponds to a 2560-pixel logical width. Growing the window enlarges the work area rather than every button.

## Precise numeric fields

The arrow area of a numeric field is also a vertical scrub control: **click for one small step, or hold and drag up/down**. Up increases the value and down decreases it at a constant sensitivity; horizontal travel does not accelerate it. Typical arrow steps are **0.001 m for position**, **0.1 degrees for rotation**, and **0.001 for scale**. Values remain rounded to that field's step. **Alt** gives a finer drag, and **Escape** cancels the current scrub.

A continuous numeric scrub creates one Undo action. After reaching a minimum or maximum, dragging the other way works immediately; excess mouse travel is not accumulated beyond the bound. Right-clicking the arrow area does not jump to the minimum or maximum. Direct text and expression entry remain available in the main text area.

## Save, reopen and export

Save creates a **version 5 .previs** project and neighbouring .assets folder containing used sources. Keep both together when moving a project. It preserves embedded static body poses and keyed additive pose corrections, motion tracks, paths and path keys, final transform channels, camera channels/lenses, active camera and lighting. Version 1/2/3/4 scenes are accepted on opening. Older releases refuse version 5 instead of silently dropping its correction keys. Overwriting an existing project keeps the previous JSON as .previs.bak.

Export frame produces a clean **1920 × 1080 PNG from the active camera**, with JSON shot data. Editor grid, path guides, camera helpers and selection outlines are excluded. Current export is PNG/JSON; video and control passes remain future work.

## Navigation and lighting

- Arrange: click to select, drag the object or its box to move in the view plane. Hold Shift for XYZ move/scale/rotation handles, including height. Ctrl snaps movement.
- RMB orbits; RMB + WASD/QE flies, with Shift for faster travel.
- MMB pans; **Alt + MMB drag zooms** without a mouse wheel. Drag up to move closer and down to move farther away. Wheel zoom also remains available; F frames selection.
- In Pose: **Shift** temporarily swaps Move/Rotate; **W/E** choose Move/Rotate explicitly. **Alt** is fine adjustment.
- Space plays/pauses. K captures pose corrections in Pose mode, or the selected object transform in Arrange.
- Ctrl+S/O saves/opens. Ctrl+D duplicates. Ctrl+Z/Y undoes/redoes. Delete removes selection.
- Draw path uses LMB points, Enter to finish and Esc to cancel.

The scene and library previews use neutral hemisphere illumination, with **no directional light enabled by default**. The Objects library includes an optional **Directional light** control. The project saves that choice.

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
