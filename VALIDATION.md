# Previs 0.1.0 validation

Validation date: **2026-10-24**. Verified platform: **Windows x64**.

This record describes the first functional prototype. It distinguishes automated
runtime checks from direct UI checks. Current feature limits and run instructions
are in [README.md](README.md).

## Toolchain

- Godot **4.7.2 stable mono**, official build `ed1daf0bf`.
- C# targeting **.NET 10**; local SDK **10.0.402** (`.tools/dotnet/dotnet.exe --version`).
- The rendered exported build reports the Compatibility renderer, OpenGL 3.3,
  NVIDIA driver 610.88, and an NVIDIA GeForce RTX 5090.
- Application interface, file dialogs, status messages, and authored error
  messages are English. `PrevisApp` explicitly sets locale `en`; the project also
  sets its fallback locale to `en`.

## Automated checks

The source project passed its C# build and headless smoke run during development.
The exported Windows x64 executable was then tested independently in headless
and rendered modes. The retained evidence is:

| Evidence | Result |
| --- | --- |
| `output/packaged-smoke.log` | Exported executable reports `PREVIS_SMOKE_OK objects=7 bones=21 keys=2`. |
| `output/packaged-render.log` | Exported executable passes the same core checks, camera-pilot regressions, and full HD rendering. It also reports `PREVIS_SMOKE_OK objects=7 bones=21 keys=2`. |
| `output/smoke/roundtrip.previs` and `roundtrip.assets/` | Saved project and copied BVH asset used by the project reopen check. |
| `output/smoke/ui.png` | Rendered application screenshot from the exported build. |
| `output/smoke/camera.png` and `camera.json` | Clean camera frame and metadata: 1920 × 1080, 24 fps, frame 30 at 1.25 seconds. |

The smoke checks cover BVH parser checks, starter-scene creation, sample loading,
deterministic BVH scrubbing, real CMU import with root motion, deterministic
camera interpolation, object selection, scene restore, project save/reopen, and
full HD camera rendering. The rendered exported run additionally verifies that
the editor follows the shot keys during camera piloting and that the piloted
camera excludes editor helpers (`CullMask == 1`).

The CMU walk animates the prepared **21-bone human rig**. This is a real captured
motion, separate from the original procedural synthetic demo. Source and license
information are retained in `samples/real/SOURCE-AND-LICENSE.md`.

An independent native-model runtime probe imported the Khronos Fox GLB with its
own rig and embedded animation. The recorded results were:

```text
MODEL_NATIVE_CLIP_OK index=0 name=Run duration=1.158 bones=24
MODEL_NATIVE_CLIP_OK index=1 name=Survey duration=3.417 bones=24
MODEL_IMPORT_OK skeletons=1 clips=3 names=Run,Survey,Walk
```

The probe checks that seeking two different times changes bone poses and that
repeating a seek reproduces the original poses, for the first two native clips.
These results validate native GLB animation import, not animal retargeting onto
a supplied Previs model. The unchanged test GLB and its source/legal metadata
are retained under ignored `output/testassets/` and are excluded from delivery.

`build.ps1 -Test` rebuilds the C# project, imports Godot resources, runs the source
headless smoke checks, and exports Windows x64. The additional model-import
probe is `scripts/Testing/ModelImportProbe.cs`; it requires an external animated
GLB supplied through `-- --model=<absolute path>`.

## Native Windows UI checks

The root implementation session directly operated the running Windows program
and verified these interactions:

- Opened the Props library and added a chair.
- Selected and dragged the chair in the real 3D viewport. The inspector updated
  to approximately **X = 1.85 m, Z = -0.05 m**.
- Selected Alex and loaded the real CMU captured walk.
- Scrubbed the timeline and observed changes in both the actor and the camera.
- In the latest packaged build, entered camera view and visually checked a clean
  16:9 framed scene matching the shot preview, with the helper body hidden.
- In that same camera view, scrubbed to 4 seconds and observed both the actor
  and the camera position change. The screenshot was reviewed directly.

An initial pilot-view inspection found the editor camera icon visible from
inside the shot. The camera-view culling mask was corrected to layer 1, and the
exported rendered smoke run now contains the explicit passing regression
`Pilot excludes editor helpers`. The later manual check above also verified
the corrected visible result. MMB piloting, camera-key playback, camera framing,
and exiting through Perspective received corresponding integration fixes.

## Scope of verification

Windows x64 is the only platform built and exercised. No macOS or Linux build
or runtime result is claimed. This remains a prototype with the documented
import, animation, camera, and output limits in [README.md](README.md), including
a static dog blockout and no animal-motion retargeting.

The delivery folder is `build/Previs-0.1.0-Windows/`; the portable archive is
`build/Previs-0.1.0-Windows.zip`. `BUILD-INFO.json` in the delivery folder records
the source commit, toolchain and executable/PCK hashes. Generated logs and test
artifacts live in ignored `output/` directories.
