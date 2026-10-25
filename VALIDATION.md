# GenPrevis 0.15.0 validation

Validation date: **2026-10-25**. Tested platform: **Windows x64**. UI remains English. Project format **11** adds independently editable point/directional light objects; formats **1–10** are accepted.

## 0.15.0 editor and lighting checks

The editor refinement suite exercises actual Save dialog confirmation, actual timeline widget input, the scene transform gizmo, visible light inspector controls, portable file roundtrips and GPU rendering. **39 checks** cover:

- Automatic `.genprevis` extension for bare and dotted names, no double extension, retained legacy filenames, and confirming the real Save dialog with only a typed name.
- Nine unique selected keys across camera position/lens, path waypoints, object XYZ transform channels and additive pose/root corrections. Summary/expanded rows deduplicate. Delete preserves scene objects, unrelated times and coincident unselected channels. One Undo/Redo reverses/repeats the whole batch. Text input is protected; Ctrl rectangle-add and Ctrl-click toggling work.
- Actual Ctrl scale-cube dragging preserves a nonuniform XYZ ratio and one history step. Cubes turn yellow before dragging and return to axis colors on Ctrl release. Shared minimum/maximum factors preserve proportions at scale limits.
- Point/directional light creation through discoverable Objects scene controls, real property changes, range only for point lights, editor-only nonshadowing handles, neutral startup, independent save/reopen and Undo of Cast shadows.
- Geometric shadows verified by changed camera pixels for both light types. Each light also passes a paired **actual shot export** with/without shadows, including first-frame shadow rendering. Geometric depth PNGs are byte-identical across the shadow toggle. GPU proof images accompany the report. Compatibility rendering uses cubemap point shadows, 2048-pixel positional atlases, and separate point/directional bias settings to avoid floor self-shadow artifacts.

Source regressions also pass **38 interaction + 15 precision-number + 14 trim checks**, and **55 additive-pose + 13 correction-timeline checks**. Original animal source/rig assets remain unchanged. AI scene control remains a documented proposal; the model-selection and command-interface design is in section 12 of `NAVRH-GENPREVIS.md`. No AI API integration or paid evaluation is claimed by this release.

Evidence uses `output/{editor-refinements,interaction,correction}-source-0.15.0.log` and the corresponding reports; the portable validation folder records the exported executable's independent checks. `BUILD-INFO.json` identifies the exact commit and runtime hashes. Final portable ZIP entries are read back for CRC verification.

The exported **GenPrevis.exe 0.15.0.0** independently passes **39 editor refinements**, **38 interaction + 15 numeric + 14 trim**, **55 pose corrections + 13 pose-key timeline**, **163 project workflow** (history 9, object motion 41, animal posing 70, setup 39, export 4), **21 scene + 27 appearance + 72 framing + 13 GPU scene/depth**, and **31 complete-shot export checks** (192 Full-HD frames, RGB/depth/pose MP4 and PNG). All six versioned native stderr logs are empty. The two paired lighting exports are additional first-frame shadow/depth checks inside the 39-check suite. The Windows build completes with zero compiler warnings/errors and the source smoke suite passes. The portable example `examples/lights.genprevis` contains a point light and a separately editable disabled directional light.

## Historical GenPrevis 0.14.0 validation

Validation date: **2026-10-25**. Development/verified platform: **Windows x64**.

This release renames the product, C# solution/assembly, Windows executable and private GitHub repository to **GenPrevis**. UI remains English. It also repairs the complete deforming foot chains of the twelve Quaternius animals and fixes first-motion placement and looping. Project format **10** preserves repaired native pose snapshots; formats **1–9** and legacy `.previs` filenames remain supported.

## Independent 0.14.0 Windows delivery

The renamed source builds with zero warnings/errors. The exported executable reports **GenPrevis / 0.14.0.0** in native file metadata. Its embedded 256-pixel PNG icon was extracted from the PE resource tree and compared byte-for-byte as RGBA pixels with the approved ICO image (`WINDOWS_ICON_OK resources=6 approved256px=true`). The source PNG is the user-approved opaque blue 3D mannequin on a light perspective-grid floor; ICO source includes 16/24/32/48/64/128/256 sizes. Generation and final prompt are documented in `assets/genprevis-icon-notes.md`.

- **Rename checks: 13** — identity/assembly/header/native window title, `.genprevis` default saves, new and legacy filename loading, non-destructive one-time user-profile migration, opaque icon and new exported application metadata.
- **Animal repair checks: 315** — all 48 Quaternius feet lift with complete ancestor chains and move actual skin-weighted mesh vertices; all **155 native clips** (152 Quaternius + three classic Fox clips) preserve authored relative joint transforms above root-locomotion alignment at five sampled times each. The donkey gallop replaces automatic idle immediately, loops throughout the shot and remains animated after four seconds. Lifted-foot corrections survive a real save/reopen.
- **Existing animal posing: 70** — IK lengths, manual head/limb changes, viewport gestures, motion layering, Undo/Redo, saved native poses and project roundtrip.

Evidence: `output/packaged-genprevis-0.14.0-{rename,animal-repair,animal-pose}.log` and the corresponding report files. Original vendor GLB bytes are unchanged. Deforming foot/toe bones retain their stable names for project compatibility; the inspector displays friendly Front/Back foot and toes labels. Nondeforming pole helpers remain hidden. Native animation TRS tracks are sampled directly before hierarchy conversion and final source blending, so editor rest-pose changes cannot alter animation sampling.

Automatic initial native clips carry a placeholder flag. A first library drop replaces that placeholder; editing its timing or animation promotes it to an authored clip. The first looping native motion spans the remaining shot (minimum three seconds), repeating the original short source cycle. Subsequent authored clips retain append/crossfade semantics, and one-shot actions retain their source duration. Older full-shot automatic idle clips are recognized conservatively on load.

The same package runs the content/library, project workflow, camera tracking, scene/display and rendered smoke regressions; the complete-shot exporter additionally verifies a real eight-second 192-frame Full-HD Color/Depth/Pose MP4+PNG export. PCK inventory retains 567 source assets, 859 covers and 118 bundled poses without missing files. Versioned logs and reports are supplied in the portable validation folder.

The GitHub repository is **https://github.com/petrsajner/GenPrevis**, with private visibility preserved. Old build names and dated test paths in the sections below remain historical evidence under the previous product name. `BUILD-INFO.json` records the exact renamed source commit, current schema, icon/runtime hashes and test counts. The final ZIP is read back completely for CRC verification and has a SHA-256 sidecar.

## Historical Previs 0.13.0 evidence

This revision adds live camera Follow / Look at constraints, persistent background-picker folders, continuous raw-video decoding, and visible version/copyright branding. The app UI remains English. Project format **9** stores live camera bindings; formats **1–8** remain migration inputs.

## Independent 0.13.0 Windows delivery

The integrated source and exported Windows executable build with **zero warnings/errors**. Native executable checks pass:

| Suite | Checks | Scope |
| --- | ---: | --- |
| Camera Follow / Look at | 32 | Independent targets, distance/height/side offsets, changed target paths, exact/smoothed reverse seeking, frame-rate-independent evaluation, manual XYZ/gizmo correction routing, retained source keys, Undo/Redo, duplication, missing targets, save/reopen, presets and pure human/native history sampling |
| Video playback | 29 | Real 1080p25/30 playback, decoder reuse, bounded buffers, independent exact source-frame hashes, fractional 29.97 fps, rotated phone video, EOF, terminal errors, per-camera cache reuse and export pause/resume |
| Editor settings and branding | 8 | Actual background pickers across cameras, cancellation, persisted directory reload, removed-folder fallback, damaged preferences, visible version and copyright |
| Live car example | 22 | 301 exact car/camera samples, path-edit response without rebaking, zero camera TRS keys, reverse seek, portable roundtrip, stop at 10.012 seconds and a complete 300-frame rendered MP4 |

Logs: `output/packaged-0.13.0-{camera-tracking,video-playback,editor-settings,car-demo}.log`. All stderr logs are empty. The example’s active camera uses Follow + Look at bindings to `demo-car`; both camera position/rotation key arrays are empty.

### Real-time video measurements

The exported executable plays real **1920×1080 H.264** sources into its actual camera-reference texture using a **960×540 editor preview**. The backend is **CPU FFmpeg**, with a persistent raw-RGB pipe and no GPU decoder dependency.

| Source | Distinct displayed frames | Real elapsed time | Maximum sampled lag | Decoder launches | Initial probe/decode/display |
| --- | ---: | ---: | ---: | ---: | ---: |
| 25 fps | 63 | 2.516 s | 0 ms | 1 | 313 ms |
| 30 fps | 76 | 2.518 s | 0 ms | 1 | 130 ms |

These are machine-specific measured results, not a guarantee for every source codec/resolution or computer. Buffers measured **49,766,400 bytes per source**, within the 48 MiB budget; at most two sources are retained. Normal playback does not launch a decoder or encode/decode PNGs for each frame. Cached requests upload synchronously to a reused texture; uncached jumps seek asynchronously. Exact frame 5 is compared against independent FFmpeg `select(n=5)` output, including reverse/fractional-rate cases. Source files remain unchanged. Performance evidence is `build/Previs-0.13.0-Windows/output/video-playback-checks/report.json`; the portable validation folder contains a copy.

The exported executable also passes **29 per-camera-background checks**, the existing **163-check project workflow**, **7 demo-opening checks**, **21 scene/placement + 27 appearance + 72 framing + 13 GPU scene/depth checks**, **38 interaction + 15 numeric + 14 trim checks**, and rendered full-HD smoke. Their logs are `output/packaged-0.13.0-{camera-reference,project-workflow,demo-launch,scene-settings,interaction,smoke}.log`, all with empty stderr. Source layout checks pass **24 assertions** at logical 1280×800, 1920×1080 and 2560×1440. PCK inventory confirms 567 source resources, 859 covers and 118 bundled poses, with no missing files.

Actual source UI interaction verifies selecting a car, selecting a camera, enabling Follow and Look at, choosing targets and showing live status (`output/CameraTrackingUiProbe-0.13.0.log`). Its target lists are cached during playback so open dropdowns are not rebuilt every frame. The application header reads **PREVIS 0.13.0**, the footer displays **© Petr Sajner 2026**, and the exported executable’s native file metadata reports version **0.13.0.0** and the same copyright.

The portable package includes the updated live-follow example, one-click launcher, MP4 preview and validation evidence. `BUILD-INFO.json` records exact source commit, live bindings, test counts, measured video performance and binary hashes. All ZIP entries are read back for CRC validation and a SHA-256 sidecar is written. Windows x64 is the tested platform.

## Historical 0.12.0 evidence

This revision supplies a ready-to-play car shot and moves image/video backgrounds from global project setup to independent camera properties. UI remains English. Project format **8** stores per-camera references; formats **1–7** remain migration inputs.

## Independent 0.12.0 Windows delivery

The final integrated build has **zero warnings/errors**. The exported Windows executable passes:

| Suite | Checks | Evidence |
| --- | ---: | --- |
| Per-camera backgrounds | 29 | Distinct images and rendered pixels, source-time offsets and opacity for a shared video, cancellation on camera switch, Undo/Redo, duplication/deletion, portable assets, content-addressed reuse safety, v7 migration without mutating inputs, unchanged clean exports |
| Ready-made car shot | 17 | 12 seconds / 25 fps, opening and final stationary beats, continuous acceleration/deceleration, near-constant cruise through the turn, correct heading and safe camera composition at all 301 sampled times, reverse seeking, portable roundtrip and a real 300-frame MP4 |
| Demo opening | 7 | Actual File menu action, automatic following-camera view, frame zero, untitled Save As behavior, visible background shortcut and Space playback after releasing text input |
| Existing project workflow | 163 | History9, object timing41, native poses70, project setup/reference39 and portrait export4 |

Logs: `output/packaged-0.12.0-{camera-reference,car-demo,demo-launch,project-workflow}.log`; all stderr files are empty. The active **Car follow** camera keeps a minimum normalized framing margin of **0.234** over every sampled frame. The vehicle stops at **10.012 seconds**, leaving approximately two seconds of stationary ending. Its dense transform keys are editable; no hidden live-tracking constraint is required. All scene geometry is procedural and no external model/motion assets are needed.

The same executable passes **38 interaction + 15 numeric + 14 clip-trim checks**, **21 scene-setting/placement + 27 appearance + 72 framing + 13 GPU pixel/depth checks**, and the rendered full-HD smoke test. Their logs are `output/packaged-0.12.0-{interaction,scene-settings,smoke}.log`, with empty stderr. PCK inventory confirms **567 source resources, 859 covers and 118 bundled poses**, with no missing files. Source responsive-layout checks pass 24 assertions, including 2560×1440 logical geometry.

Actual source UI probes verify the prominent **Camera background…** action, a compact nested file picker, expandable camera inspector controls with two independent file names, and the format-only 480×260 Project setup dialog. `CarDemoStartupUiProbe.gd` exercises the real `--demo=car-chase` argument and Space playback. The exported executable separately verifies the same demo-opening behavior through its actual File menu action. Evidence: `output/CameraBackgroundUiProbe-0.12.0.log`, `output/ProjectSetupUiProbe-0.12.0.log`, `output/demo-startup-ui-source-0.12.0.log`, their empty stderr files and the corresponding screenshots.

The portable package includes **Open car chase demo.cmd**, five example projects, the car-shot guide, eight full-HD proof frames, an overview and a complete 1280×720 / 25 fps MP4 preview. `BUILD-INFO.json` records the exact source commit and binary hashes. Packaging reads every ZIP entry for CRC validation and writes a SHA-256 sidecar. Windows x64 is the exercised platform.

## Historical 0.11.0 evidence

This revision makes paths and keyed transforms available to all objects, adds shared key timing curves, native animal pose editing, configurable project aspect/fps, editor-only image/video camera references and visible Undo/Redo controls. All app UI remains English. Project format **7** stores these fields; formats **1–6** remain migration inputs.

## Independent 0.11.0 Windows delivery

The source builds with **zero warnings and zero errors**. The independent exported Windows executable passes the combined **163-check** suite (`output/packaged-0.11.0-project.log`):

| Area | Checks | Evidence covered |
| --- | ---: | --- |
| History | 9 | Actual Undo/Redo buttons, enabled states, playback stop, Ctrl+Z, Ctrl+Shift+Z, Ctrl+Y and branch invalidation |
| Object motion and timing | 41 | Car path drawing/arrival keys, grouped XYZ transforms, all five modes, actual Bezier handle gesture and one Undo, camera lens/pose timing, reverse seeking and portable curve persistence |
| Native animal posing | 70 | All thirteen supplied rigs, Wolf viewport/keys/Undo/persistence/library, Horse and classic Fox IK/segment lengths, source-facing path evaluation preserving manual root turns |
| Project setup and camera references | 39 | All six preview/frustum aspects, 12/15/24/25/30/custom37 fps, format Undo/Redo, reference source bundling, custom fps reopen, real background/foreground pixels, camera gate and real MP4 seeking/offset/cache |
| Portrait shot export | 4 | Reference loaded during 288×512 / 9:16 export at 15 fps, three frames and all three MP4/PNG passes, FFprobe verification and exported RGB matching the clean still exactly |

Reference backgrounds render on a separate editor-only layer. GPU pixels verify that opaque foreground objects keep their original appearance, orbit mode excludes the reference and clean RGB exports are byte-identical with it enabled. Actual MP4 reference decoding is checked forward and backward using a two-color encoded video, including source-frame timing and end hold. Reference audio is not played. Cold seeks decode asynchronously; the displayed guide catches up to the requested frame without blocking scene manipulation. The frame cache is bounded to 24 entries.

The native pose test retains a real walk beneath corrections, verifies both reachable end-joint IK and moving middle joints with following children, and checks exact final bone poses after portable save/reopen. Saved native poses identify their matching catalog rig. Anatomical joint limits, generic cross-species retargeting and automatic four-foot contact constraints are outside this revision.

The same executable passes **31 full-shot export checks** at **1920×1080, 24 fps, 192 frames/eight seconds**, plus 21 scene-settings/placement + 27 appearance + 72 framing + 13 rendered scene/depth checks, 38 interaction + 15 numeric + 14 clip-trim checks, 55 correction + 13 pose-key checks, 71 rendered human manual-pose checks, 278 static-pose checks, 225 model/motion-library checks and the rendered full-HD smoke test. Logs: `output/packaged-0.11.0-{export,scene-settings,interaction,correction,manual-pose,pose,content,smoke}.log`. All nine packaged stderr files, including the combined project suite, are empty.

Independent source regressions retain **267 transform assertions**, **1,397 path assertions** and **24 responsive layout checks**. The final rendered UI probe operates the real Project setup dialog, nested image picker and 9:16 / 15 fps controls (`output/project-setup-ui-final-0.11.0.log`). Screenshots show the timing graph, native joint handles, compact project setup and portrait camera gate. The dialog is 520×570 logical pixels. At logical 2560×1440 the header stays 35 px and the play button 31 px.

The exported PCK inventory retains **567 sources, 859 covers and 118 bundled human poses**, with no missing resources. The portable package includes four example scenes, including `examples/keyed-car.previs`, validation logs/reports and representative exported media. `BUILD-INFO.json` records the exact source revision and binary hashes. Packaging reads every ZIP entry for CRC validation and writes a SHA-256 sidecar. Only Windows x64 has been exercised.

## Historical 0.10.0 evidence

This revision adds persistent scene display and object appearance settings, support-surface placement, and fifteen subject-relative framing presets. Existing full-shot export, synchronized depth/pose guidance and twenty-four camera movement presets remain in place. Ordinary dragging preserves Y; Shift transform handles retain unrestricted 3D adjustment. Application UI and authored messages are English. See README.md for output semantics and limits.

## Scene, appearance, placement and framing for 0.10.0 — source evidence

The integrated source builds with zero warnings and errors. Current source evidence comprises **20 scene-setting assertions in the headless run**, **21 in the rendered run**, **27 object-appearance/placement assertions** and **72 camera-framing assertions**. Evidence: `output/scene-settings-source-0.10.0.log`, `output/scene-settings-render-0.10.0.log` and `output/scene-settings-report.json`. These results are source/runtime checks; the final exported-package run is recorded separately after it executes.

Scene coverage includes background color, neutral-light brightness, ground visibility/height/opacity, global labels, validation and persistence. Appearance/placement coverage includes independent instance colors, original-material restoration, per-object/camera label state, real supporting-surface queries, evaluated posed bounds, seated pelvis/thigh support and saved state. Labels use editor layer 2 and remain excluded from rendered exports.

The source GPU suite additionally passes **13 actual image/depth assertions**. Full-HD active-camera pixels verify two chosen background colors, fill brightness without a directional light, ground visibility and opacity, unchanged exports when editor labels are toggled, and the configured ground height in both color and measured camera-Z depth. Evidence: `output/scene-display-render-report.json`, `output/scene-settings-render-0.10.0.log` and its empty stderr file.

**CameraFramingProbe passes 72 assertions for all 15 types** (`output/camera-framing-probe.log`), including finite normalized composition, intended 16:9 subject-region containment, matching front/back/side direction, portrait-vs-wide scale, low cameras above support height, vertical top views, child-sized subjects, rotated subjects, quadruped bounds, over-shoulder matching, invalid-ID atomicity, application-channel reset and unchanged mocap. The previous **CameraMoveProbe passes 124 assertions** (`output/camera-move-regression-framing.log`).

The actual rendered camera library contains all fifteen framing cards. Its UI probe selects Front wide on the chosen subject and renders both the application and a contact sheet from the original live vector cards. Evidence: `output/camera-framing-ui.log`, `output/camera-framing-ui-err.log` (zero bytes), `output/camera-framing-library.png` and `output/camera-framings-contact.png`. The last contact sheet includes the final waist-up medium/profile illustrations.

Project format **6** stores scene display and appearance data. Versions 1–5 remain migration inputs. Framing/placement operate on existing transforms and keep source motion and keyed pose corrections intact. Placement positions the evaluated support point; it is not a body-contact or animation foot-locking solver.

## Independent 0.10.0 Windows delivery

The exported executable passes **21 scene-setting/placement checks, 27 appearance checks, 72 framing checks and 13 GPU image/depth checks**. The separate rendered application probe operates the actual scene controls and object inspector, including background, brightness, ground opacity, color, rename and Place on ground. Evidence: `output/packaged-0.10.0-scene.log`, its empty stderr file, `output/scene-settings-ui-0.10.0.log` and `output/scene-settings-ui-0.10.0.png`.

The same executable passes **31 whole-shot export checks** at **1920 × 1080, 24 fps and 192 frames / eight seconds**, with all three H.264 videos independently inspected by FFprobe and matching Color/Depth/Pose PNG sequences. Regression runs pass 38 workflow + 15 numeric + 14 clip-trim checks, 55 correction + 13 pose-key timeline checks, 71 rendered manual-pose checks, 278 static-pose checks, 225 model/motion checks and the rendered full-HD smoke test. Logs: `output/packaged-0.10.0-{export,workflow,corrections,manual,pose,content,rendered-smoke}.log`. All eight packaged stderr logs are empty.

The independent PCK inventory confirms **567 source files, 859 covers and 118 poses**, without missing resources (`output/package-inventory-0.10.0.log`). The portable package includes validation reports, actual UI images, representative exported frames/videos and the three portable example scenes. `BUILD-INFO.json` records project format 6, the exact source commit, test counts and binary hashes. The packaging audit reads every ZIP entry to verify its CRC and writes a SHA-256 sidecar. Only Windows x64 has been exercised.

## Toolchain

- Godot **4.7.2 stable mono**, official build ed1daf0bf.
- C# targeting .NET 10; local SDK **10.0.402**.
- Rendered source checks use the Compatibility renderer on the development Windows machine.
- Godot locale and fallback locale are en.

## Historical whole-shot export and control passes for 0.9.0 — source evidence

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

The exported Windows executable passes **31 complete-shot export assertions** at **1920 x 1080, 24 fps, 192 frames / eight seconds**, exporting Color, Depth and Pose to three H.264 MP4 files and three PNG sequences. FFprobe independently verifies every video. Checks also cover active Camera B, exact frame times, joint projection, original scene/material/time/selection/Undo restoration, PNG-only export, MP4-only scratch cleanup, no overwrite, and cancellation. Evidence: `output/packaged-0.9.0-export.log` and the exported `shot-export-report.json`.

The same package passes 38 workflow + 15 numeric + 14 trim checks, 55 correction + 13 pose-key checks, 278 static-pose checks, 225 content checks and the clean full-HD smoke test. All six stderr logs are empty. Source GPU control-pass calibration passes 17 checks with clean stderr; 24 camera presets pass 124 runtime checks and rendered library interaction. The resource inventory has 567 sources, 859 covers and 118 poses with no missing files. `BUILD-INFO.json` records exact source and binary hashes. ZIP entries are fully read back for CRC verification.
