# Previs 0.3.1 validation

Validation date: **2026-10-24**. Development/verified platform: **Windows x64**.

This revision targets compact desktop layout, final manual transform layers above motion/path animation, unified camera object selection, and a drag-and-drop motion library with human and animal clips. Application UI and authored messages are English. See README.md for feature limits.

## Toolchain

- Godot **4.7.2 stable mono**, official build ed1daf0bf.
- C# targeting .NET 10; local SDK **10.0.402**.
- Rendered source checks use the Compatibility renderer on the development Windows machine.
- Godot locale and fallback locale are en.

## Responsive layout

The actual integrated source, without test stubs, passed LayoutProbe with **24 checks**. Evidence: output/layout-0.3.log.

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

Core smoke checks retain BVH parsing, malformed-source handling, deterministic scrubbing, prepared-rig mapping, independent cameras, neutral/optional lighting, source asset bundling and full HD clean-frame rendering. Project format 3 stores final transform channels and camera lens channels. Prior project formats remain migration inputs.

Earlier tests of version 2 cover path timing persistence, source BVH bundling, last-clip deletion surviving restore and version 1 migration. Current source workflow also verifies the new transform and lens fields through scene restore.

The neutral lighting implementation remains a gray hemisphere with reflections and no active directional light by default. Its optional directional light reuses one node. It does not add a hidden default key light to recover contrast.

## Library and provenance

The shipped catalogue has **five prepared motions**: two human sources and three Fox native cycles. Library cards expose compatible actor families, and the source drop workflow appends to a matching actor or creates the matching actor on blank ground.

- Human capture provenance: samples/real/SOURCE-AND-LICENSE.md.
- Fox asset/animation/conversion credits: LICENSES/Fox-NOTICE.md and retained assets/library/fox.metadata.json.
- The Fox model's raw data are unchanged; runtime sampling and blending are application behavior.
- The dog prop remains static; universal animal retargeting is not implemented.

## Delivery verification

The independent exported executable in **build/Previs-0.3.1-Windows/** passed its rendered smoke run, including raw-safe Fox loading, grouped transform/lens persistence, the 17-sample crossfade-layer regression, motion-drop target resolution and full HD frame export. Evidence is retained in `output/packaged-0.3.1-render.log`, ending in `PREVIS_SMOKE_OK`.

Direct Windows UI operation verified the compact desktop layout, all five motion cards, and selecting Camera B in the unified Scene list to open its position/rotation/scale/lens controls. A native library drag was accepted and created a matching animated actor on ground. The existing-actor drop route is additionally tested through the actual viewport drop callbacks: it appends a clip without increasing the actor count, including the projected-rig fallback for moving limbs outside their simplified collider.

The previous running sessions were left open once user input was detected. The supplied `examples/layered-scene.previs` includes human and Fox tracks, camera channels, path timing and manual corrections. `BUILD-INFO.json` records the source commit and executable/resource/managed-assembly hashes. The portable `build/Previs-0.3.1-Windows.zip` is verified by reading every entry and checking its ZIP CRC.

Windows is the only platform exercised. No macOS/Linux runtime result is claimed. Current output is active-camera PNG/JSON; video/control-pass export, foot/contact solving, a large motion catalogue and universal animal retargeting remain outside this implemented scope.
