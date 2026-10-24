# Previs 0.6.0 validation

Validation date: **2026-10-24**. Development/verified platform: **Windows x64**.

This revision adds static human poses, source-independent body snapshots and a reusable custom-pose library above the populated model and motion libraries. Compact desktop layout, final transform layers and unified camera selection remain in place. Application UI and authored messages are English. See README.md for feature limits.

## Toolchain

- Godot **4.7.2 stable mono**, official build ed1daf0bf.
- C# targeting .NET 10; local SDK **10.0.402**.
- Rendered source checks use the Compatibility renderer on the development Windows machine.
- Godot locale and fallback locale are en.

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

## Independent 0.6.0 delivery

The self-contained Windows executable passes **71 manual-pose checks**, **278 static-pose checks**, **225 model/motion library checks**, and the rendered smoke test including clean full-HD export. Evidence: `output/packaged-0.6.0-{manual,poses,content,render}.log`; all four stderr logs are empty. The actual exported resource inventory confirms 567 bundled sources, 859 covers and 118 poses with zero missing files (`output/package-inventory-0.6.0.log`). LayoutProbe passes 24 geometry checks at logical 1280x800, 1920x1080 and 2560x1440.

The portable package contains `examples/manual-adjusted-pose.previs` with its own imported chair asset. `BUILD-INFO.json` records source revision, resource/executable/managed-assembly hashes and test counts. The ZIP is read back completely for CRC verification.
