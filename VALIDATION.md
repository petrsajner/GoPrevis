# Previs 0.9.0 validation

Validation date: **2026-10-25**. Development/verified platform: **Windows x64**.

This revision adds complete active-camera shot export, synchronized depth and pose guidance, and illustrated camera movements. Ordinary object/camera dragging preserves Y; Shift transform handles retain unrestricted 3D adjustment. Application UI and authored messages are English. See README.md for output semantics and limits.

## Toolchain

- Godot **4.7.2 stable mono**, official build ed1daf0bf.
- C# targeting .NET 10; local SDK **10.0.402**.
- Rendered source checks use the Compatibility renderer on the development Windows machine.
- Godot locale and fallback locale are en.

## Whole-shot export and control passes for 0.9.0 — source evidence

The rendered source whole-shot suite passes **31 assertions**. Evidence: `output/shot-export-source-0.9.0.log` and `output/shot-export-report.json`. It exports a complete 8-second shot from **Camera B**, with 192 frames at 640×360/24 fps in color, depth and pose. Each H.264 MP4 is independently checked using ffprobe for codec, dimensions, frame count, rate and duration. The PNG sequences include frame 000191 and match the end-exclusive timestamp array. The fixture includes mocap, a keyed head correction, a path, animated camera keys and a native animal rig.

The suite checks exact pose projection through the exported camera, evaluated world joint coordinates, native topology, grayscale depth, colored pose pixels, changing rendered frames, restoration of time/camera/selection/materials/Undo, protection of an existing export folder, two full-HD PNG frames, MP4-only scratch cleanup, PNG-only output, and cancellation with an accurate partial manifest.

The separate real-GPU control-pass suite passes **17 assertions** (`output/control-pass-probe.log`). It checks actual surface depth, posed/native skinned animation, human guidance coordinates, foreground occlusion, native animal topology and end-exclusive sampling. These are graphics results, not headless substitutes.

The isolated real encoder API suite passes **8 assertions** using the pinned FFmpeg 9.0.2 runtime: H.264 dimensions/timing, completion progress, no overwrite, odd-size rejection, pre-cancellation, in-flight cancellation and partial cleanup, plus missing-input-frame rejection. The source fixture is under the ignored `.tools/encoder-probe/` directory. Runtime setup is reproducible with `tools/export/setup_ffmpeg.py --offline` after the verified archive is cached.

The Cameras library contains 24 original illustrated Basic/Aerial movement cards. Their runtime checks are recorded separately when executed; this section does not infer a pass from catalog size. Current 0.9.0 source evidence does **not** yet claim an independent exported-package pass. The final delivery audit adds that result after the actual executable has run.

## Historical regression evidence

The versioned sections below retain previous tested behavior and provenance. Earlier free-Y ordinary dragging and still-only export descriptions are historical; the 0.9.0 workflow and output definitions are given in README.md.

## Unified workflow and correction layers for 0.8.0 — source evidence

The new integrated source builds with zero warnings and errors. The current correction suite passes **55 assertions**, and the actual correction timeline passes **13 assertions**. The revised interaction suite passes **38 assertions**, invoking **15 precision-number checks** and **14 motion-trim checks**. Static-pose, model/motion library and desktop layout reruns pass **278**, **225** and **24** assertions respectively. This section records source execution; independent exported-executable results are added by the final delivery audit.

The revision verifies:

- One Arrange interaction selects and drags object bodies/boxes and cameras in the view plane; holding Shift adds the complete XYZ move/scale/rotation gizmo. Separate Select and Move toolbar modes are absent.
- Exactly five library sections remain: Models, Objects, Poses, Motions and Cameras. Human/animal models and prop/world assets use visible scrollable subgroups; camera moves are part of Cameras. The inspector shows a context and back control rather than parallel task tabs.
- Elbow IK solves upward and retains descendant local poses. Forearm/hand children follow the moved elbow; shin/foot children follow a moved knee instead of being fixed at their old endpoints.
- Manual pose edits create sparse additive local-quaternion and root-offset keys without removing or replacing mocap clips. The source assets, speed, clip timing, blends and base static pose remain intact when the editor is entered.
- First late edits get a neutral time-zero key; affected channels alone are written during a gesture. Quaternion interpolation, exact key evaluation, non-accumulating neutral/static/mocap playback, reverse seeking, source-facing path alignment and zero-correction behavior are tested.
- Pose-correction groups expand to keyed joint/root rows. Actual timeline selection, capture, retiming, collision handling, removal and Undo/Redo are exercised separately from motion clip trimming and object Transform channels.
- Scene format **5** embeds corrections and accepts formats **1–4**. Duplication and project save/reopen retain source motion and correction channels. Static library capture includes the visible corrected pose while the original actor continues using its source motion.

A rendered source UI run verifies a keyed head change while the mocap leg motion continues. The older manual editor compatibility fixture is being updated to assert preservation of live source data rather than the obsolete freeze-and-clear contract; it is not counted as a new pass until rerun.

## Previous interaction checks for 0.7.0

The integrated source builds with zero warnings and errors. `RunInteractionChecks` passes **31 application assertions**, and invokes **15 precision-numeric checks** plus **14 timeline-trim checks**. Evidence: `output/interaction-source-0.7.0.log` ends in `INTERACTION_OK checks=31 numeric=15 timeline=14`. The standalone precision and trim probes also pass. These are historical 0.7.0 source results; the new Arrange workflow supersedes the former Select/Move split.

- Shift switches a hand from Move to Rotate before pointer motion; releasing Shift during the drag retains the latched rotation until mouse release. Wrist orientation changes without moving the wrist position, and explicit Rotate selection survives reselecting that hand. W/E select Move/Rotate while posing.
- Shift-moved elbows and knees change their bend positions while retaining the hand/foot endpoint and fixed limb lengths.
- A visible camera body can be selected through actual viewport callbacks, including leaving pose mode. Camera Move can change height, and a complete Shift-in-Select gizmo exposes XYZ movement, axis scale cubes and rotation rings. Camera Y translation and rotation are exercised; one drag creates one undo action and remains active after Shift is released.
- Ordinary objects can be lifted with the Y arrow and scaled on a chosen axis. Path-driven vertical corrections leave the drawn curve unchanged.
- Alt+MMB zoom changes working-view distance without panning its orbit target.
- Real numeric arrow-area scrubbing stays fine and creates one undo action. Boundary tests cover a huge over-drag to both limits followed by an immediate opposite pixel, including sub-step resolution. Right-click does not jump to 1000; typed numbers/expressions and programmatic-value/text synchronization remain correct. Disabled fields and Escape cancellation are checked.
- Real timeline-widget IN trimming advances source offset by timeline delta times Speed and preserves both OUT values. A retargeted skeleton comparison proves the new source IN is actually played. Limits, loop state, reversible fade intersections, selected OUT handles through overlaps, Undo/Redo and handle tooltips are checked.

The report is `output/interaction-report.json`. Source probes: `scripts/Testing/PrecisionSpinProbe.cs`, `scripts/Testing/TimelineTrimProbe.cs`, and `scripts/PrevisInteractionChecks.cs`.

A rendered source UI probe also passes: the actual camera body is selected, its height changes by 0.35 m, a precision-field scrub changes the value by 0.006, and the full Shift transform gizmo is rendered. Evidence: `output/interaction-ui-0.7.0.log` and `output/interaction-ui-0.7.0.png`. Current source regression reruns pass 70 manual-pose, 278 static-pose, 225 content-library and 24 responsive-layout assertions. These source results do not yet claim a new portable-package run.

## Historical regression evidence (0.4–0.6)

The following sections retain prior verification and content provenance. Their versioned logs are historical evidence, not claims about the new 0.8.0 exported executable.

## Responsive layout

The actual integrated source, without test stubs, passed LayoutProbe with **24 checks**. Evidence: output/layout-0.6.0.log.

| Logical window | Header height | Play-button height | 3D viewport | Library / inspector |
| --- | --- | --- | --- | --- |
| 1280 × 800 | 35 px | 31 px | 721 × 529 | 198 / 286 px |
| 1920 × 1080 | 35 px | 31 px | 1343 × 776 | 216 / 286 px |
| 2560 × 1440 | 35 px | 31 px | 1967 × 1136 | 232 / 286 px |

Checks cover actual window/viewport resolution agreement, constant compact chrome sizes, work-area coverage, bounded inspector and viewport geometry, and compact library width. The layout no longer depends on stretching a fixed 1440 × 900 canvas.

The development renderer reports DPI 144. A 3840-pixel physical desktop width at that 150% scale corresponds to a 2560-pixel logical working width; these measurements are logical UI pixels, not a claim that Windows DPI is ignored.

## Source animation probes

The integrated C# source builds with **zero warnings and zero errors**. The source scene's rendered smoke run passes.

| Probe | Checks | Scope |
| --- | --- | --- |
| TransformLayerProbe | 267 | Position/Rotation/Scale channels, additive corrections, scale composition, angular interpolation, deterministic seeking |
| ActorPathProbe | 1397 | Arc-length paths, timed waypoint arrivals, holds, pauses, reverse seeking and geometry/timing changes |
| MotionTrackProbe | 29 | BVH source blends, continuous roots, source timing, in-place filtering, cache reuse and failure handling |
| NativeMotionTrackProbe | 64 | Native skeletal clip sampling, Fox cycles, crossfades, source timing, deterministic seeking and root filtering |
| LayoutProbe | 24 | Actual integrated desktop geometry at three resolutions |

The BVH checks include the genuine recorded CMU walk and the separate original synthetic test motion. The native Fox checks use its supplied Walk, Run and Survey cycles; they do not assert that these are recorded animal mocap.

## Integrated layer/camera/library workflow

The scene-level workflow samples **17 points across a mocap crossfade**. At every sample it verifies:

- A static manual 90° yaw and an animated yaw correction remain above the evaluated path heading.
- Manual position offsets remain above the path position.
- The final body heading is taken from the blended pose, avoiding the previous source-facing reversal during a crossfade.

Additional source checks verify grouped scale keys after motion/path evaluation, reverse-seek determinism of the final transform, camera Object inspector editing and independent rotation/lens keys, unified object-list camera counts, native Fox Walk/Run sequencing, animal travel above the native blend, and serialization/restoration of transform/lens channels.

These tests exercise the evaluation order **source blend → path → final manual correction**, rather than independently applying source transforms after the manual layer.

## Scene and file checks

Core smoke checks retain BVH parsing, malformed-source handling, deterministic scrubbing, prepared-rig mapping, independent cameras, neutral/optional lighting, source asset bundling and full HD clean-frame rendering. Project format 4 adds embedded static poses to final transform channels and camera lens channels. Project formats 1–3 remain migration inputs. Earlier releases refuse format 4 instead of silently discarding poses on save.

Earlier tests of version 2 cover path timing persistence, source BVH bundling, last-clip deletion surviving restore and version 1 migration. Current source workflow also verifies the new transform and lens fields through scene restore.

The neutral lighting implementation remains a gray hemisphere with reflections and no active directional light by default. Its optional directional light reuses one node. It does not add a hidden default key light to recover contrast.

## Populated content and runtime validation

The catalog has **284 model entries and 457 motion cards**. It includes 255 new Kenney props, 12 new Quaternius rigged animals with 152 authored native clips, and 300 new recorded CMU human takes (about 85 minutes). Six human body variants, ten original procedural props, the original Fox and the original five motion cards remain available. Animal models include breed/color variants; they are not 12 distinct biological species.

- Every one of the **300 CMU BVHs** passes original-byte SHA-256, hierarchy/frame/channel validation and the actual `BvhMotion` / `RiggedActor` runtime. Each maps 31 animated source joints to 21 prepared bones. Six samples per take verify finite normalized poses, changing bones, deterministic seeking and in-place roots. Evidence: `output/content-human-validation.json` (300 passed, zero failures).
- All **255 static models and 12 new animal models** pass actual Godot GLTF import, nonempty mesh/bounds and material checks. A real GPU produces their covers. Evidence: `output/content-audit.json` and rendered contact sheets.
- All **152 animal native clips** resolve by exact imported animation name and animate real bones. Evidence: `assets/content/animals/validation.json`.
- **859 distinct PNG covers** match every catalog model/motion/pose ID. Cards initially load these images; source parsing and live skeletal previews initialize only on hover. Pages contain at most twelve cards.
- The integrated content workflow passes **225 assertions**, including representative metre-scale grounded props, all six humanoids, every new animal rig, compatible/incompatible drops, empty-ground actor creation, native bone motion, crossfades under paths/manual corrections, cache reuse, and portable save/reopen of identities, proportions, units, final transforms and bone poses. Evidence: `output/content-workflow-report.json` and `output/content-workflow-0.5.0.log`.
- A standalone resource inventory against the exported PCK confirms **567 unique new source files and all 859 covers**, with zero missing resources. Evidence: `output/package-inventory-0.5.0.log`; reproducible script: `tools/content/check_packaged_library.gd`.

CMU takes skip the conversion calibration frame and do not loop by default. Source BVHs are unchanged. Git attributes preserve their exact bytes. Recorded takes may contain multiple actions and need manual trimming. Scene length supports up to 600 seconds, including long recordings. Human and animal motion compatibility is explicit; universal animal retargeting and foot/contact solving remain unimplemented.

Provenance and terms are recorded in `LICENSES/CMU-MOTION-CONTENT.md`, `LICENSES/ANIMAL-CONTENT.md` and `LICENSES/PROP-CONTENT.md`. Original source notices are also copied into the portable package's `LICENSES/content/` directory. The legacy Fox attribution remains in `LICENSES/Fox-NOTICE.md`.

## Static-pose validation

The integrated source and independent rendered Windows executable validate **118 poses with 278 assertions** through `RunPoseLibraryChecks` / `PoseProbe`. Evidence: `output/pose-workflow-0.5.0.log` and `output/pose-workflow-report.json`. The format-4 checks include loading all three prior project versions. The checks cover:

- Every bundled pose has finite, normalized prepared-rig transforms and remains unchanged under forward/reverse scene-time evaluation.
- Empty-ground placement creates a human with no motion clip or BVH dependency; applying a pose to an existing figure retains body proportions and avoids a duplicate actor.
- Seated/lying figures use pose geometry, including a lowered pelvis and a horizontal lying body, rather than a standing selection/pick envelope.
- Source clips override stored static poses; removing the final clip restores the stored fallback. Clearing a pose retains active animation.
- Capturing a mocap frame preserves local bone rotations in a reusable source-independent snapshot. Saving a custom preset leaves the selected actor's source clips intact; tests remove only their own GUID-named temporary presets.
- Path placement, heading and final manual keys remain above the body pose. Duplication and project-format-4 save/reopen retain bone poses and final transforms. Older projects without pose fields remain valid inputs.

The original static-pose checks review contact geometry through rendered covers. Manual joint and IK editing is covered separately below; automatic chair/contact fitting and universal animal pose retargeting remain outside the scope.

## Previous 0.5.0 delivery verification

The independent portable executable is `build/Previs-0.5.0-Windows/Previs.exe`. Its content workflow uses the exported PCK and its bundled .NET runtime, including raw-safe model loading and project asset copies. The rendered packaged smoke verifies the previous layer/camera/path regressions and a clean full-HD camera export. Logs: `output/packaged-0.5.0-poses.log` (278 pose assertions), `output/packaged-0.5.0-content.log` (225 library assertions), and `output/packaged-0.5.0-render.log` (full-HD smoke). All three stderr logs are empty. A rendered source UI probe verifies the actual Poses tab and search narrowing to one Sit upright card, then restoring all 118 entries; evidence is in `output/pose-ui-probe.log` and `output/pose-library-ui.png`.

`BUILD-INFO.json` records source revision, catalog counts and executable/resource/managed-assembly hashes. The final portable ZIP is checked by reading all entries and verifying CRCs. Only Windows x64 has been exercised; no macOS/Linux runtime result is claimed. Current output remains active-camera PNG/JSON, with video/control-pass export outside this revision.

All **118 authored poses** were independently rendered with the actual rig and neutral studio environment. Their poses remain identical after a 100-second seek. `output/pose-content-validation.json` reports 118 passed, zero failures; the chair reference and contact sheets verify seated support, grounded feet and horizontal lying shapes. `samples/static-poses.previs` is a self-contained static scene example.

## Manual pose validation for 0.6.0

The source headless `RunManualPoseChecks` run passes **70 assertions**. Evidence: `output/manual-pose-source-0.6.0.log` and `output/manual-pose-workflow-report.json`. It exercises the actual rig and application editing callbacks. Its scope includes local head/torso rotations, all four hand/foot IK chains, fixed limb lengths and end orientations, attainable and fully folded/extended target limits, feet-pinned pelvis motion, finite/atomic invalid-input handling, scaled/rotated world-space edits, one Undo/Redo item per gesture, the viewport marker-drag route and real hand/foot mesh grabs through the scene viewport GuiInput signal (including visible geometry overlapped by other x-ray joint markers), current-frame mocap freezing, path-layer placement, reusable custom-preset XYZ pelvis offsets, duplication and project-format-4 save/reopen.

A real imported Kenney chair supplies a measured seat surface through a physics ray. The regression places the hips at that surface while retaining both foot positions and produces a clean full-HD chair reference on a rendering display. Its temporary custom library items use unique GUID IDs; cleanup removes only those items.

The real palm/foot-body drags are sent through the scene viewport `GuiInput` signal, outside their own joint markers. They verify cursor tracking, upper/lower/end joint changes, fixed limb lengths, unchanged scene-object placement and one Undo item. The fixture deliberately includes unrelated projected joint markers over visible hands, so the visible mesh must win the pick. The rendered source pass also verifies full-HD output, passing **71 assertions** with an empty stderr log (`output/manual-pose-render-0.6.0.log`). A separate rendered UI probe grabs the actual palm through scene `GuiInput` events and confirms that shoulder, elbow and wrist all change while the whole figure stays fixed; it also drags the head to turn it. Evidence: `output/manual-pose-ui.log` and `output/manual-pose-ui-after.png`. The precision fields remain collapsed during this direct manipulation.

## Previous independent 0.6.0 delivery

The self-contained Windows executable passes **71 manual-pose checks**, **278 static-pose checks**, **225 model/motion library checks**, and the rendered smoke test including clean full-HD export. Evidence: `output/packaged-0.6.0-{manual,poses,content,render}.log`; all four stderr logs are empty. The actual exported resource inventory confirms 567 bundled sources, 859 covers and 118 poses with zero missing files (`output/package-inventory-0.6.0.log`). LayoutProbe passes 24 geometry checks at logical 1280x800, 1920x1080 and 2560x1440.

The portable package contains `examples/manual-adjusted-pose.previs` with its own imported chair asset. `BUILD-INFO.json` records source revision, resource/executable/managed-assembly hashes and test counts. The ZIP is read back completely for CRC verification.

## Independent 0.7.0 delivery

The exported Windows executable passes **31 integrated interaction assertions, 15 precision-number assertions and 14 IN/OUT timeline assertions** in one rendered run. It also passes 71 rendered manual-pose checks, 278 static-pose checks, 225 model/motion library checks and the clean full-HD smoke render. Logs: `output/packaged-0.7.0-{interaction,manual,poses,content,render}.log`; all five stderr logs are empty. The exported resource inventory confirms 567 sources, 859 covers and 118 poses without missing files. `BUILD-INFO.json` records the exact source revision, binary hashes and counts. The portable ZIP is fully read back for CRC verification.

## Independent 0.8.0 delivery

The exported Windows executable passes **55 additive-correction checks plus 13 real pose-key timeline checks**, **38 unified-workflow checks plus 15 numeric and 14 clip-trim checks**, 71 updated manual-pose checks, 278 static-pose checks, 225 content-library checks and the rendered full-HD smoke test. Logs are `output/packaged-0.8.0-{corrections,workflow,manual,poses,content,render}.log`; all six stderr files are empty. The PCK inventory confirms 567 motion/model sources, 859 covers and 118 poses without missing files. `examples/keyed-pose.previs` includes its original mocap sources plus head/hand/hips correction keys and path/manual layers. Project format 5 preserves these tracks; older formats 1–4 load with an identity correction layer.

The camera movement probe passes **124 checks for all 24 presets** (`output/camera-move-probe-0.9.0.log`). The rendered library test verifies all cards and a lens-only Zoom in. Export dialog geometry is checked at 550x590 logical pixels so both Export and Cancel remain accessible (`output/export-ui-0.9.0.log`). The final whole-shot portable test uses 1920x1080 at 24 fps over eight seconds (192 frames), superseding the earlier 640x360 source fixture.

## Independent 0.9.0 delivery

The exported Windows executable passes **31 complete-shot export assertions** at **1920x1080, 24fps, 192frames/eightseconds**, exporting Color/Depth/Pose to all three H.264MP4 files and all three PNG sequences. FFprobeindependentlyverifies everyvideo. Italso verifies activeCameraB, allframetimes, jointprojection, originalscene/material/time/selection/Undorestoration, 1080PNG-only, MP4-onlyscratchcleanup, nooverwrite, andcancellation. Evidence: `output/packaged-0.9.0-export.log` and the exported `shot-export-report.json`.

The same package passes 38workflow+15numeric+14trimchecks,55correction+13posekeychecks,278staticposechecks,225contentchecks andthecleanfullHDsmoke. Allsixstderrlogsareempty. SourceGPUcontrol-passcalibrationpasses17checkswithcleanstderr; 24camerapresetspass124runtimechecksandrenderedlibraryinteraction. The resourceinventoryhas567sources/859covers/118poseswithnomissingfiles. `BUILD-INFO.json` records exact source andbinaryhashes. ZIPentries arefullyreadbackforCRCverification.
