# Third-party notices

Previs uses Godot Engine 4.7.2 under the MIT licence. Engine and bundled third-party notices are included in `LICENSES/GODOT-LICENSE.txt` and `LICENSES/GODOT-COPYRIGHT.txt`.

Official source: https://github.com/godotengine/godot/tree/4.7.2-stable

The Windows package includes the Microsoft .NET runtime. Its MIT licence and third-party notices are included under `LICENSES`.

Official source: https://github.com/dotnet/runtime

The 300 newly bundled human BVH takes are recorded data from the [Carnegie Mellon University Graphics Lab Motion Capture Database](http://mocap.cs.cmu.edu/), converted to MotionBuilder-friendly BVH by Bruce Hahne / cgspeed.com. Their source-specific permissions, acknowledgments, original publisher notices and archive checksums are documented in `LICENSES/CMU-MOTION-CONTENT.md`. CMU permits inclusion in commercially sold products but prohibits directly reselling the motion data, including converted data; these files are not CC0. The [conversion publisher's release page](https://sites.google.com/a/cgspeed.com/cgspeed/motion-capture/the-motionbuilder-friendly-bvh-conversion-release-of-cmus-motion-capture-database) supplies the source archives. The BVH files are unchanged; catalog offsets skip their added calibration frame during playback.

The original real CMU walk sample is additionally documented in `samples/real/SOURCE-AND-LICENSE.md` and is governed by the applicable CMU source terms. The synthetic BVH is authored test data created for this project.

The 255 new static models are selected from Kenney's [Furniture Kit](https://kenney.nl/assets/furniture-kit), [Car Kit](https://kenney.nl/assets/car-kit), [Nature Kit](https://kenney.nl/assets/nature-kit) and [Modular Buildings](https://kenney.nl/assets/modular-buildings), all under CC0 1.0 Universal. See `LICENSES/PROP-CONTENT.md` for original package notices, model/source metadata, placement scales and self-contained GLB packaging details. Original notices are retained under `assets/content/props/*-LICENSE.txt`.

The 12 new rigged animal models and their 152 authored skeletal animation cycles are by Quaternius, from the [Ultimate Animated Animal Pack](https://quaternius.com/packs/ultimateanimatedanimals.html), under CC0 1.0 Universal. They are not recorded motion-capture data. See `LICENSES/ANIMAL-CONTENT.md`, `assets/content/animals/SOURCE-LICENSE.txt` and `assets/content/animals/provenance.json` for the creator's original license, source URLs, checksums and lossless glTF-to-GLB container repack details. These assets are distinct from the original Fox below.

The bundled animated Fox model and its Walk/Run/Survey animation use the attributions and licences in `LICENSES/Fox-NOTICE.md` (CC0 model; CC-BY-4.0 rig, animation and conversion). The source GLB is unchanged, including its raw-safe packaging copy.

The original procedural mannequins and blockout props, project logic and UI are project assets; no Topview source, images or models were copied.

The 118 prepared static humanoid poses are authored project assets, independent of the motion datasets. Preparation and scope are recorded in `LICENSES/POSE-CONTENT.md`.


## FFmpeg shot encoding

The portable Windows package invokes unmodified **FFmpeg and ffprobe 9.0.2** executables from Gyan Doshi's release-essentials build as separate command-line processes. It does not link their libraries into the Previs managed assembly or Godot process. This build includes libx264 and is supplied under GPL version 3 or later. The FFmpeg project's [download directory](https://ffmpeg.org/download.html) links to the [build provider](https://www.gyan.dev/ffmpeg/builds/).

[LICENSES/FFMPEG-NOTICE.md](LICENSES/FFMPEG-NOTICE.md) records the pinned package, publisher checksum, matching FFmpeg core source location and integration boundary. The complete license text, original vendor README/build configuration/dependency versions, and provenance/binary hashes are retained in `LICENSES/FFMPEG-GPL-v3.txt`, `LICENSES/FFMPEG-VENDOR-README.txt` and `LICENSES/FFMPEG-PROVENANCE.json`. The notice distinguishes the FFmpeg core source archive from corresponding source for every statically linked dependency; it does not invent a complete-source claim or an author source offer. Redistribution of those tools remains subject to their license terms.

The camera movement diagrams and control-pass renderer are project-authored. Topview screenshots inform workflow/layout references only; no Topview illustrations, source or model assets are bundled.
