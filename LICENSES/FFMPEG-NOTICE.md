# FFmpeg video export tools

Previs invokes **FFmpeg** and **ffprobe** as separate command-line programs for
MP4 encoding and verification. It does not link their libraries into the Previs
managed assembly or Godot process. The supplied programs are unmodified upstream
Windows x64 executables from Gyan Doshi's release-essentials build **9.0.2**.

The FFmpeg project's own download directory links to this Windows build provider:

- Official download directory: <https://ffmpeg.org/download.html>
- Build provider: <https://www.gyan.dev/ffmpeg/builds/>
- Pinned archive: <https://www.gyan.dev/ffmpeg/builds/packages/ffmpeg-9.0.2-essentials_build.zip>
- Publisher's SHA-256: <https://www.gyan.dev/ffmpeg/builds/packages/ffmpeg-9.0.2-essentials_build.zip.sha256>
- Archive SHA-256: `60f467265b1e312373dbcd92200c2618a74850f98d3d078e94296bb3fa2047ba`

This build enables GPL components, including the software H.264 encoder
**libx264**, and is distributed by the provider under **GPL version 3 or later**.
FFmpeg copyright belongs to the FFmpeg developers (2000–2026) and its respective
contributors; the included external libraries have their own copyright holders.
The full license is retained as `FFMPEG-GPL-v3.txt`. FFmpeg is a trademark of
Fabrice Bellard, originator of the project.

The original provider README is retained without modification as
`FFMPEG-VENDOR-README.txt`; it records the build configuration, enabled components
and exact reported dependency versions. `FFMPEG-PROVENANCE.json` records the
archive hash, binary hashes and verified version. The pinned download and
verification can be reproduced with `python tools/export/setup_ffmpeg.py`.

## Corresponding source locations

- FFmpeg source commit named by the binary provider:
  <https://github.com/FFmpeg/FFmpeg/commit/946fcce07b>
- Matching official FFmpeg release source archive:
  <https://ffmpeg.org/releases/ffmpeg-9.0.2.tar.xz>
- Provider's release mirror and preserved package:
  <https://github.com/GyanD/codexffmpeg/releases/tag/9.0.2>
- x264 source project: <https://code.videolan.org/videolan/x264>
- FFmpeg license documentation: <https://ffmpeg.org/legal.html>

These are source locations, not a claim that the FFmpeg core archive contains
the corresponding source of every statically linked dependency. Redistribution
of the bundled tools remains subject to their full license, including applicable
corresponding-source requirements. The local development package retains the
provider's exact build and dependency information rather than inventing a source
offer on behalf of the application author.
