"""Fetch and verify the pinned, separately invoked Windows video tools.

Run from any directory: python tools/export/setup_ffmpeg.py [--offline]
The archive cache is deliberately outside the deployable runtime directory.
"""
from __future__ import annotations

import argparse
import datetime as dt
import hashlib
import json
from pathlib import Path
import subprocess
import urllib.request
import zipfile

ROOT = Path(__file__).resolve().parents[2]
RUNTIME = ROOT / ".tools" / "ffmpeg"
CACHE = ROOT / ".tools" / "ffmpeg-cache"
VERSION = "9.0.2"
ARCHIVE_NAME = f"ffmpeg-{VERSION}-essentials_build.zip"
ARCHIVE_URL = f"https://www.gyan.dev/ffmpeg/builds/packages/{ARCHIVE_NAME}"
ARCHIVE_SHA256 = "60f467265b1e312373dbcd92200c2618a74850f98d3d078e94296bb3fa2047ba"
SOURCE_URL = "https://github.com/FFmpeg/FFmpeg/commit/946fcce07b"


def sha256(path: Path) -> str:
    with path.open("rb") as stream:
        return hashlib.file_digest(stream, "sha256").hexdigest()


def main() -> None:
    args = argparse.ArgumentParser()
    args.add_argument("--offline", action="store_true", help="Use only the previously verified cached archive")
    options = args.parse_args()
    RUNTIME.mkdir(parents=True, exist_ok=True)
    CACHE.mkdir(parents=True, exist_ok=True)
    archive = CACHE / ARCHIVE_NAME
    old_cache = RUNTIME / ARCHIVE_NAME
    if not archive.exists() and old_cache.exists():
        old_cache.replace(archive)
    if not archive.exists():
        if options.offline:
            raise FileNotFoundError("The pinned FFmpeg archive is not cached.")
        urllib.request.urlretrieve(ARCHIVE_URL, archive)
    actual = sha256(archive)
    if actual != ARCHIVE_SHA256:
        raise ValueError(f"FFmpeg archive checksum mismatch: {actual}")
    names = {"/bin/ffmpeg.exe": "ffmpeg.exe", "/bin/ffprobe.exe": "ffprobe.exe",
             "/LICENSE": "GPL-v3.txt", "/README.txt": "vendor-README.txt"}
    with zipfile.ZipFile(archive) as container:
        for suffix, destination in names.items():
            matches = [name for name in container.namelist() if name.endswith(suffix)]
            if len(matches) != 1:
                raise ValueError(f"Unexpected FFmpeg archive member: {suffix}")
            (RUNTIME / destination).write_bytes(container.read(matches[0]))
    version = subprocess.check_output([str(RUNTIME / "ffmpeg.exe"), "-version"],
                                      text=True, encoding="utf-8")
    encoders = subprocess.run([str(RUNTIME / "ffmpeg.exe"), "-hide_banner", "-encoders"],
                             check=True, text=True, encoding="utf-8", capture_output=True).stdout
    if "libx264" not in encoders or not version.startswith(f"ffmpeg version {VERSION}-essentials_build"):
        raise ValueError("The pinned runtime does not provide the expected software H.264 encoder.")
    (RUNTIME / "version.txt").write_text(version, encoding="utf-8")
    (RUNTIME / "provenance.json").write_text(json.dumps({
        "Version": VERSION, "Platform": "Windows x64", "Vendor": "Gyan Doshi / gyan.dev",
        "VendorPage": "https://www.gyan.dev/ffmpeg/builds/",
        "OfficialDownloadDirectory": "https://ffmpeg.org/download.html",
        "ArchiveUrl": ARCHIVE_URL, "ArchiveSha256": ARCHIVE_SHA256,
        "PublisherChecksumUrl": ARCHIVE_URL + ".sha256", "CoreSourceCommit": SOURCE_URL,
        "CoreSourceArchive": "https://ffmpeg.org/releases/ffmpeg-9.0.2.tar.xz",
        "License": "GPL-3.0-or-later", "Invocation": "Separate command-line processes; no library linking",
        "VerifiedUtc": dt.datetime.now(dt.timezone.utc).isoformat(),
        "FilesSha256": {name: sha256(RUNTIME / name) for name in names.values()},
        "SoftwareH264Encoder": "libx264", "UnmodifiedVendorBinaries": True,
    }, indent=2) + "\n", encoding="utf-8")
    notice = ROOT / "LICENSES" / "FFMPEG-GPL-v3.txt"
    notice.write_bytes((RUNTIME / "GPL-v3.txt").read_bytes())
    (ROOT / "LICENSES" / "FFMPEG-VENDOR-README.txt").write_bytes((RUNTIME / "vendor-README.txt").read_bytes())
    (ROOT / "LICENSES" / "FFMPEG-PROVENANCE.json").write_bytes((RUNTIME / "provenance.json").read_bytes())
    print(f"FFmpeg {VERSION}: verified archive and software H.264 encoder")
    print(f"Runtime: {RUNTIME}")
    print("Deploy ffmpeg.exe, ffprobe.exe, GPL-v3.txt, vendor-README.txt, version.txt and provenance.json.")


if __name__ == "__main__":
    main()
