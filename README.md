# Previs 0.2.1 — functional Godot prototype

Previs plans actors, space and cameras for AI video reference. This desktop prototype uses **Godot 4.7.2 .NET and C# / .NET 10**. All application UI, dialogs and authored errors are English.

## Run

Open `build/Previs-0.2.1-Windows/Previs.exe`. Keep the executable, `Previs.pck` and `data_Previs_windows_x86_64` together. The Windows export includes the .NET runtime; the Godot editor and .NET SDK are not needed to use it.

The source project is `project.godot`; open it with Godot **4.7.2 .NET**. Local development tools are in the ignored `.tools` directory. `run.ps1` starts the source project; `build.ps1 -Test` builds, runs headless checks and exports Windows x64.

## Try the complete workflow

1. **Build a scene.** Open Props or Environment and click a model. Click an object in the viewport to select it. With Move active, drag on the ground plane; hold Ctrl to snap to 0.25 metres. The Object inspector edits position, rotation and scale. The prepared human, furniture, car and environment are real 3D geometry.
2. **Append mocap clips.** Select a human and choose Import mocap or Motion → Append BVH…. Each import adds a clip to that actor's timeline rather than replacing the track. Motions also contains a real captured CMU walk and an explicitly synthetic walk/gesture demo. The included CMU source uses **0.055 metres/unit**; the synthetic source uses **0.01**. Set Source metres/unit for each imported clip as needed.
3. **Arrange and blend the action.** Click a timeline clip to open its Motion inspector. Drag its body to move it or its right edge to trim duration; edits snap to scene frames. Overlap adjacent clips to crossfade. The highlighted overlap marks the blend. Start, Duration, Source offset, Blend in, Speed, Loop source and In-place clip are also editable numerically. Changing Blend in moves the selected clip back against the previous clip's end to create that overlap. Source offset trims the beginning without changing the source file.
4. **Draw and time a travel path.** Select a character, click Draw path, then click ground points in the viewport. Finish path or Enter applies a path with at least two points; Esc cancels and restores the previous path. Ctrl snaps points to 0.25 metres. A separate **actor · Path** timeline row shows diamond keys **P1, P2, P3…** for the curve points. Drag a diamond to change its arrival time; the character reaches the same spatial point at the new time. Keys cannot cross their neighbours. Click a key to seek to it and open Path → Arrival time for numeric editing. Path start moves the whole timed path; Travel time rescales its keys. BVH X/Z displacement is suppressed so travel is not applied twice, and Face travel direction follows the curve. Object position moves the entire path. Use Draw / replace to redraw, or Clear to remove it.
5. **Choose and move cameras.** The startup scene contains independent Camera A, B and C. Select a camera in the dropdown above the preview, its timeline row, or Cameras. Add creates a camera from the working view; Duplicate copies the active camera and its keys; Remove keeps at least one camera. Rename the active camera in Cameras and press Enter. Each camera retains its own lens, pose and keys. Use framing or Camera moves presets, or navigate the working view, click Use current view, then Add key. Scrub to another time and capture another key. Enter camera view to pilot the active camera directly; the 16:9 guide matches the clean preview.
6. **Save and reopen.** Save creates a version 2 `.previs` JSON project plus a neighbouring `.assets` folder containing every used BVH clip and supported imported model. Keep both together when moving a project. Cameras, active camera, motion tracks, paths and the optional light state are preserved. Version 1 projects open as one camera and one clip per previously animated human. Overwriting an existing project keeps its previous JSON as `.previs.bak`. Undo/redo covers scene edits, motion tracks, paths and camera changes.
7. **Export a reference frame.** Export frame produces a clean **1920 × 1080 PNG from the active camera** plus JSON shot data. Editor grid, paths, camera icons and selection outlines are excluded. Switch cameras and export again for another view of the same action.

The startup scene includes a mannequin with synthetic motion, furniture, a parked car, a wall and a tree. Camera A has a dolly move; B and C provide other views.

The portable package also includes `examples/Cameras-Blends-Path.previs`: three cameras, two blended BVH clips, and a three-point path with editable P1/P2/P3 timing keys. Open it to try the complete workflow immediately. Keep its neighbouring `roundtrip.assets` folder with the example.

## Neutral scene lighting

The default scene uses neutral gray hemisphere illumination and reflections. **No directional light is active by default.** Objects retain visible form and material colors without requiring a lighting setup for blockout work. The same environment lights library thumbnails.

Environment → Directional light enables an optional shadow-casting light. The project saves this choice; New returns to the neutral setup.

## Navigation

- LMB: select; drag with Move to place on the ground.
- RMB: orbit. While held, WASD and Q/E fly; Shift moves faster.
- MMB: pan. Wheel: dolly/zoom the working position.
- F: frame selection. Space: play/pause.
- In camera view: RMB looks around, WASD/QE flies the shot, MMB pans; Add key records the current frame.
- Draw path: LMB adds ground points; Enter finishes; Esc cancels.
- Timeline: click a camera row to activate it; drag a human clip to move it; drag its right edge to trim it; drag a diamond on the separate Path row to retime arrival at that point.
- Ctrl+S/O: save/open. Ctrl+D: duplicate object. Ctrl+Z/Y: undo/redo. Delete: remove selected object.

## Import support and current limits

- **BVH humanoid tracks:** Y-up skeletons with standard Mixamo/CMU-style names. Original rotation order/sample rate, source offsets, speed, looping, deterministic seeking, root-continuous clip crossfades and path playback are supported. The target is a fixed-proportion 21-bone mannequin. The importer reports unmapped joints and rejects unsupported/nonhumanoid rigs.
- **FBX, GLB and glTF models:** runtime import preserves the model's own rig and embedded animations. Choose one native animation in Motion; these models do not use the multi-clip BVH track or humanoid retargeting. Imported animated models can follow a drawn object path; their embedded root animation is not retargeted or automatically converted to in-place motion, so use an in-place source for predictable travel.
- Use **self-contained GLB** for portable imported models. External `.gltf` dependencies are not bundled; saving such a model is refused with an English explanation. FBX may also need embedded textures to travel correctly.
- Path keys initially use approximately constant travel speed. Retiming keys changes travel speed between points; repeated positions at different times can hold a position. Adjust mocap Speed to match the travel. There is no foot-contact IK, automatic foot-slip correction, collision avoidance, terrain following or prop-contact solving. Finger animation is not supported by the supplied mannequin.
- Camera tracks provide alternative views over one shared action. A camera-cut edit, multi-camera video export, video plates, video/depth/edge/mask export and direct AI generation are not implemented. Current export is an active-camera PNG plus JSON metadata.
- Dog is explicitly a **static blockout prop**. Supplied animated animal models, animal motion libraries and animal retargeting are not implemented.
- Windows x64 is the delivery platform. macOS/Linux builds have not been verified.

## Validation and samples

See `VALIDATION.md` for current checks and delivery verification. Core smoke checks cover scene creation, three independent cameras, neutral/optional lighting, BVH import, overlapping clips, deterministic blended path playback, version 2 roundtrip and version 1 migration. Separate motion-track and arc-length path probes exercise continuity, source reuse, timing and repeated seeking.

The real captured sample and attribution are documented in `samples/real/SOURCE-AND-LICENSE.md`. The synthetic sample is original procedural test data, not a recorded actor. See `THIRD-PARTY-NOTICES.md` for engine/runtime notices.
