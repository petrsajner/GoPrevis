# Previs 0.2.1 validation

Validation date: **2026-10-24**. Verified platform: **Windows x64**.

This version adds independent cameras, editable BVH clip tracks with crossfades,
drawn character paths and neutral default lighting. Application UI, dialogs,
status messages and authored errors are English. Feature limits are recorded
in [README.md](README.md).

## Toolchain

- Godot **4.7.2 stable mono**, official build `ed1daf0bf`.
- C# targeting **.NET 10**; local SDK **10.0.402**.
- Render checks use the Compatibility renderer, OpenGL 3.3, NVIDIA driver
  610.88 and an NVIDIA GeForce RTX 5090.
- `PrevisApp` sets locale `en`; the project fallback locale is also `en`.

## Automated source checks

The C# build and source headless smoke run passed during 0.2.1 development.
The smoke suite retains the earlier BVH/parser, object-selection, scene-restore,
camera-pilot and project checks, and now verifies:

- Three cameras have independent identities. Static Camera B retains its pose
  while the scene evaluates Camera A's dolly motion.
- The neutral scene has no visible directional light. Enabling then disabling
  the optional light reuses one light without accumulating extra nodes.
- Sequential synthetic/real CMU motion clips overlap and receive a blend.
- The drawn path controls character position and suppresses duplicate BVH X/Z
  displacement. Pose, position and orientation reproduce after reverse seeking.
- Version 2 roundtrip preserves three cameras, two BVH clips and path points.
- Version 1 input migrates into one camera and one BVH clip for the human.
- Removing the final motion clip survives scene restore.

Successful core completion prints
`PREVIS_SMOKE_OK objects=7 bones=21 keys=2`.

`scripts/Testing/ActorPathProbe.cs` passed **1,307 checks**. It verifies empty and
degenerate paths, duplicate points, exact endpoints, before/after holds,
approximately constant arc-length travel with uneven control points, finite
normalized headings, reverse seeking and geometry-cache invalidation after
point edits. Timing edits are evaluated without rebuilding unchanged geometry.

`scripts/Testing/MotionTrackProbe.cs` passed **29 checks**, including the real
CMU plus synthetic-source regression. It verifies quaternion crossfades,
continuous root position at transitions and source-loop boundaries, source
offset/speed/clamping, gaps holding the previous endpoint, path/in-place root
filtering, deterministic reverse seeking, reuse of prepared sources, atomic
failure handling and clearing an empty track. Successful completion prints:

```text
MOTION_TRACK_REAL_CMU_OK mixed_sources=2
MOTION_TRACK_PROBE_OK checks=29
```

The source smoke suite runs through `build.ps1 -Test`. The separate probes use
these Godot arguments:

```text
--headless --path . --script res://scripts/Testing/ActorPathProbe.cs
--headless --path . --script res://scripts/Testing/MotionTrackProbe.cs
```

## Rendered lighting checks

The 0.2.1 neutral environment was rendered and inspected with the Compatibility
renderer. Gray hemisphere illumination retains curved forms and material color
without a default directional light or hard cast shadows. The scene and library
thumbnails share that environment. An isolated lighting probe also checked the
default absence of a directional node, repeated toggle reuse, sky ambient and
reflection sources, and unboosted exposure. Its neutral and optional-light
images are retained under ignored `.tools/lighting-probe/`.

## Prior native-model compatibility evidence

The 0.1.0 native-model probe imported the Khronos Fox GLB with its own skeleton
and embedded animations:

```text
MODEL_NATIVE_CLIP_OK index=0 name=Run duration=1.158 bones=24
MODEL_NATIVE_CLIP_OK index=1 name=Survey duration=3.417 bones=24
MODEL_IMPORT_OK skeletons=1 clips=3 names=Run,Survey,Walk
```

That probe verifies changing and repeated bone-pose seeks for the first two
clips. It is prior compatibility evidence, not a claim that 0.2.1 provides
animal retargeting or native-model BVH clip sequencing. The external GLB and
source metadata are retained under ignored `output/testassets/` and are
excluded from delivery. The rerunnable probe is
`scripts/Testing/ModelImportProbe.cs` with `-- --model=<absolute GLB path>`.

## Delivery verification

The exported Windows executable in `build/Previs-0.2.1-Windows/` passed its
rendered smoke run independently, including full HD export. Evidence is retained
in `output/packaged-0.2.1-render.log`; it ends in `PREVIS_SMOKE_OK`.

Scene checks verify three cameras, independent playback, optional lighting,
blended clips, path-driven position without duplicate mocap root translation,
retimed arrivals at exact curve points, persistence of edited key times,
version 1 migration, and deletion of the last motion clip surviving restore.
The path probe now passes **1397 checks**; the motion-track probe passes **29**.

Direct Windows interaction verified appending the real CMU clip, opening its
Motion inspector, moving a clip from 0 to 0.5 seconds by dragging, selecting
Camera B through its timeline row, drawing three ground points and applying the
curve, and scrubbing the character along it. In 0.2.1 the separate `Alex · Path`
row displays P1/P2/P3. Dragging P2 from 3 to 4 seconds visibly moved the key and
playhead while keeping the character at that curve point. The existing 0.2.0
session was left open when the user began working in it.

The package includes a complete keyed-path example under `examples/`.
`BUILD-INFO.json` records the source commit and hashes of the executable,
resource pack and managed application assembly. The portable archive is
`build/Previs-0.2.1-Windows.zip`; its entries are checked with ZIP CRC validation.

Windows x64 is the only platform exercised. No macOS or Linux build/runtime
result is claimed. Video export, animal-motion retargeting and automatic
foot/contact correction remain outside this prototype's implemented scope.
