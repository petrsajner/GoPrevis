# Previs 0.1.0 — functional Godot prototype

Previs plans actors, space and cameras for AI video reference. This is the first working desktop prototype, built with **Godot 4.7.2 .NET and C# / .NET 10**. All application UI, dialogs and errors are English.

## Run

Open `build/Previs-0.1.0-Windows/Previs.exe`. Keep the executable, `Previs.pck` and `data_Previs_windows_x86_64` together. The Windows export includes the .NET runtime; the Godot editor and .NET SDK are not needed to use it.

The source project is `project.godot`; open it with Godot **4.7.2 .NET**. Local development tools are in the ignored `.tools` directory. `run.ps1` starts the source project; `build.ps1` builds, checks and exports it.

## Try the complete workflow

1. **Build a scene.** Open Props or Environment and click a model. Click to select it in the viewport. With Move active, drag on the ground plane; hold Ctrl to snap to 0.25 metres. Edit position, rotation and scale in Properties. Duplicate or delete objects with the panel buttons. The prepared human, furniture, car and environment are real 3D geometry.
2. **Import mocap.** Select a human actor, then Import mocap and choose a BVH. Its motion is retargeted to the prepared 21-bone mannequin. Motions also offers a real captured CMU walk and an explicitly synthetic walk/gesture demo. Scrub or press Play. Set speed, Loop and In-place in the right inspector (scroll down). The included CMU clip uses 0.055 metres per source unit; the synthetic demo uses 0.01. Other sources may need scale adjustment.
3. **Move the camera.** Use camera framing or Camera moves presets. Alternatively navigate the working view, click Use current view, then Add key. Scrub to another time, find a new view and Add key again. Play or scrub to see the interpolated shot. Enter camera view to pilot the shot directly; the 16:9 guide matches the clean preview.
4. **Save and reopen.** Save creates a `.previs` JSON project plus a neighbouring `.assets` folder containing used motion and model files. Keep both together when moving a project. Undo/redo covers scene transforms, object operations, motion imports and camera keys.
5. **Export a reference frame.** Export frame produces a clean **1920 × 1080 PNG** from Camera A plus JSON shot data, without the editor grid, camera icon or selection outline.

The startup scene already includes a mannequin with the synthetic motion, furniture, a parked car, a wall and a tree, plus a dolly camera move.

## Navigation

- LMB: select; drag with Move to place on the ground.
- RMB: orbit. While held, WASD and Q/E fly; Shift moves faster.
- MMB: pan. Wheel: dolly/zoom the working position.
- F: frame selection. Space: play/pause.
- In camera view: RMB looks around, WASD/QE flies the shot, MMB pans; Add key records the current frame.
- Ctrl+S/O: save/open. Ctrl+D: duplicate. Ctrl+Z/Y: undo/redo. Delete: remove selection.

## Import support and current limits

- **BVH:** Y-up humanoids with standard Mixamo/CMU-style joint names. Rotation order, original sample rate, root motion, deterministic seeking and in-place playback are supported. Fixed target proportions; no finger animation or foot-contact IK. The import report lists unmapped joints. Arbitrary/nonhumanoid skeletons are rejected rather than presented as supported.
- **FBX, GLB and glTF models:** runtime model import preserves their own rig and embedded animation. Choose an original animation in Properties. Retargeting FBX-only motion onto the built-in mannequin is not included yet. Use BVH for that path.
- Use **self-contained GLB** for portable imported models. External `.gltf` dependencies are not bundled by this version; saving such a model is refused with an English explanation. FBX may also need embedded textures to travel correctly.
- One animated camera (Camera A), one motion per actor starting at frame zero. Clip sequencing, blends, motion paths, additional cameras, video plates, video/depth/edge/mask export and direct AI generation are later work.
- Dog is explicitly a **static blockout prop**. Animal motion libraries and retargeting are not implemented in this prototype.
- Windows x64 is built and tested. The code targets a cross-platform engine, but macOS/Linux builds have not been verified.

## Validation

`build.ps1 -Test` runs parser and headless scene checks: BVH parsing and malformed data, declared rotation order, quaternion interpolation, deterministic seeking, real CMU import, camera interpolation, object selection and project save/reopen. A rendered smoke run also writes `output/smoke/ui.png` and a full HD `camera.png`.

The real captured sample and its attribution are documented in `samples/real/SOURCE-AND-LICENSE.md`. The synthetic sample is original procedural test data, not a recorded actor. See `THIRD-PARTY-NOTICES.md` for engine/runtime notices.
