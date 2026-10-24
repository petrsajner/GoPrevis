"""Reproduce and validate the bundled CMU/cgspeed human motion library.

Run from the repository root with Python 3.11+. Downloads are cached under the
ignored output/ directory. Original BVH member bytes are never rewritten.
"""
from __future__ import annotations

import argparse
import collections
import hashlib
import json
import math
from pathlib import Path
import re
import urllib.request
import zipfile


ROOT = Path(__file__).resolve().parents[2]
CACHE = ROOT / "output/content-downloads"
DESTINATION = ROOT / "assets/content/motions/human"
MANIFEST = ROOT / "assets/content/manifests/human-motions.json"
PUBLISHER = (
    "https://sites.google.com/a/cgspeed.com/cgspeed/motion-capture/"
    "the-motionbuilder-friendly-bvh-conversion-release-of-cmus-motion-capture-database"
)
ARCHIVES = {
    "cmuconvert-mb2-01-09.zip": "o6ncxuu1oq5r6z9",
    "cmuconvert-mb2-10-14.zip": "z6pr5bk3h1o5045",
    "cmuconvert-mb2-15-19.zip": "cu0a99w1day8hnr",
}
ATTRIBUTION = (
    "The motion data was obtained from the CMU Graphics Lab Motion Capture "
    "Database (mocap.cs.cmu.edu), created with funding from NSF EIA-0196217. "
    "BVH conversion by Bruce Hahne / cgspeed.com."
)


def download_archive(filename: str, key: str) -> Path:
    CACHE.mkdir(parents=True, exist_ok=True)
    path = CACHE / filename
    if not path.exists():
        page = urllib.request.urlopen(
            "https://www.mediafire.com/?" + key, timeout=45
        ).read().decode("utf-8")
        match = re.search(r'href="([^"]+)"[^>]*id="downloadButton"', page)
        match = match or re.search(r'id="downloadButton"[^>]*href="([^"]+)"', page)
        if not match:
            raise RuntimeError(f"Publisher's archive download is unavailable: {filename}")
        temporary = path.with_suffix(".zip.tmp")
        urllib.request.urlretrieve(match.group(1), temporary)
        temporary.replace(path)
    with zipfile.ZipFile(path) as archive:
        bad = archive.testzip()
        if bad:
            raise ValueError(f"Corrupt archive member: {bad}")
    return path


def describe(index: bytes) -> dict[str, str]:
    text = index.decode("utf-8-sig")
    return {
        match.group(1): match.group(2).strip()
        for match in re.finditer(r"^(\d+_\d+)\s+([^\r\n]+)", text, re.MULTILINE)
    }


def validate_bvh(data: bytes, label: str) -> tuple[int, float, int]:
    text = data.decode("ascii")
    hierarchy, motion = text.split("MOTION", 1)
    if not hierarchy.lstrip().startswith("HIERARCHY") or "ROOT Hips" not in hierarchy:
        raise ValueError(f"Unexpected hierarchy: {label}")
    if hierarchy.count("{") != hierarchy.count("}"):
        raise ValueError(f"Unbalanced hierarchy: {label}")
    channels = sum(int(n) for n in re.findall(r"CHANNELS\s+(\d+)", hierarchy))
    header = re.match(r"\s*Frames:\s*(\d+)\s+Frame Time:\s*([.0-9]+)\s*\n", motion)
    if not header:
        raise ValueError(f"Missing frame metadata: {label}")
    frames = int(header.group(1))
    frame_time = float(header.group(2))
    if frames < 2 or not 0 < frame_time < 1 or channels < 60:
        raise ValueError(f"Invalid frame metadata: {label}")
    rows = motion[header.end():].splitlines()
    rows = [row for row in rows if row.strip()]
    if len(rows) != frames:
        raise ValueError(f"Expected {frames} frames, found {len(rows)}: {label}")
    for frame, row in enumerate(rows):
        values = row.split()
        if len(values) != channels:
            raise ValueError(f"Invalid channel count at frame {frame}: {label}")
        if not all(map(math.isfinite, map(float, values))):
            raise ValueError(f"Non-finite value at frame {frame}: {label}")
    return frames, frame_time, channels


def classify(description: str) -> tuple[str, list[str]]:
    text = description.lower()
    tags = {"human", "mocap", "recorded", "cmu"}
    groups = {
        "walking": ("walk", "stride", "wander", "navigate", "march", "hobble"),
        "running": ("run", "jog", "scramble"),
        "jumping": ("jump", "jete"),
        "sitting": ("sit", "seat", "stool"),
        "standing": ("stand", "rise"),
        "gestures": ("gesture", "wave", "point", "hand signal", "direct traffic"),
        "conversation": ("conversation", "explain", "quarrel", "meet", "friends"),
        "interaction": ("subjects", "shake hand", "pulls", "resists", "link arms"),
        "climbing": ("climb", "ladder", "playground", "dangle", "hang"),
        "sports": ("basketball", "soccer", "boxing", "tai chi", "sword", "punch", "exercise", "squat", "stretch", "jumping jack"),
        "dance": ("dance", "pirouette", "arabesque", "jete", "glissade", "coupe"),
        "everyday": ("drink", "bottle", "wash", "sweep", "mop", "paint", "scoop"),
        "reach": ("reach", "tiptoe", "grab", "lift"),
        "emotion": ("laugh", "anger", "angry", "frustration", "jauntily", "stealth"),
    }
    for tag, words in groups.items():
        if any(word in text for word in words):
            tags.add(tag)
    if "conversation" in tags:
        tags.update(("talk", "talking"))
    if "sitting" in tags:
        tags.add("seated")
    # Subject-paired clips retain their official A/B description and are useful
    # independently, but their relationship is searchable rather than hidden.
    if "subjects" in text:
        category = "Interaction"
    elif "dance" in tags:
        category = "Dance"
    elif "sports" in tags:
        category = "Sports"
    elif "sitting" in tags:
        category = "Sitting"
    elif "everyday" in tags:
        category = "Everyday"
    elif "gestures" in tags or "emotion" in tags:
        category = "Gestures"
    elif "climbing" in tags:
        category = "Climbing"
    elif "walking" in tags or "running" in tags:
        category = "Locomotion"
    else:
        category = "Action"
    return category, sorted(tags)


def rebuild() -> None:
    DESTINATION.mkdir(parents=True, exist_ok=True)
    MANIFEST.parent.mkdir(parents=True, exist_ok=True)
    entries = []
    archives = []
    for filename, key in ARCHIVES.items():
        path = download_archive(filename, key)
        archives.append({"Name": filename, "Bytes": path.stat().st_size,
                         "Sha256": hashlib.sha256(path.read_bytes()).hexdigest()})
        with zipfile.ZipFile(path) as archive:
            index = describe(archive.read("cmu-mocap-index-text.txt"))
            if filename == "cmuconvert-mb2-01-09.zip":
                (DESTINATION / "READMEFIRST-cgspeed.txt").write_bytes(archive.read("READMEFIRST.txt"))
                (DESTINATION / "cmu-mocap-index-text.txt").write_bytes(archive.read("cmu-mocap-index-text.txt"))
            for member in sorted(archive.namelist()):
                if not member.lower().endswith(".bvh"):
                    continue
                data = archive.read(member)
                frames, frame_time, channels = validate_bvh(data, member)
                identifier = Path(member).stem
                description = index[identifier]
                if "Unknown" in description:
                    raise ValueError(f"Missing official description: {identifier}")
                category, tags = classify(description)
                destination = DESTINATION / f"CMU_{identifier}.bvh"
                destination.write_bytes(data)
                entries.append({
                    "Id": "cmu-" + identifier.replace("_", "-"),
                    "Name": description[0].upper() + description[1:] + f" · CMU {identifier}",
                    "ActorFamily": "human",
                    "SourcePath": "res://" + destination.relative_to(ROOT).as_posix(),
                    "SourceAnimation": None,
                    "SourceScale": 0.055,
                    "SourceOffset": frame_time,
                    "Duration": round((frames - 1) * frame_time, 7),
                    "Loop": False,
                    "Category": category,
                    "Tags": tags,
                    "License": "CMU",
                    "Attribution": ATTRIBUTION,
                    "SourceUrl": "https://www.mediafire.com/?" + key,
                    "Description": description,
                    "SourceArchive": filename,
                    "ArchiveMember": member,
                    "Subject": int(identifier.split("_")[0]),
                    "Motion": int(identifier.split("_")[1]),
                    "FrameCount": frames,
                    "FrameTime": frame_time,
                    "Channels": channels,
                    "Bytes": len(data),
                    "Sha256": hashlib.sha256(data).hexdigest(),
                })
    entries.sort(key=lambda entry: (entry["Subject"], entry["Motion"]))
    MANIFEST.write_text(json.dumps(entries, indent=2, ensure_ascii=False) + "\n", encoding="utf-8")
    print(json.dumps({"Clips": len(entries), "Bytes": sum(entry["Bytes"] for entry in entries),
                      "Seconds": sum(entry["Duration"] for entry in entries),
                      "Categories": dict(collections.Counter(entry["Category"] for entry in entries)),
                      "Archives": archives}, indent=2))


def verify() -> None:
    entries = json.loads(MANIFEST.read_text(encoding="utf-8"))
    identifiers = set()
    for entry in entries:
        if entry["Id"] in identifiers:
            raise ValueError(f"Duplicate identifier: {entry['Id']}")
        identifiers.add(entry["Id"])
        path = ROOT / entry["SourcePath"].removeprefix("res://")
        data = path.read_bytes()
        if hashlib.sha256(data).hexdigest() != entry["Sha256"]:
            raise ValueError(f"Hash mismatch: {path}")
        frames, frame_time, channels = validate_bvh(data, str(path))
        if (frames, frame_time, channels) != (entry["FrameCount"], entry["FrameTime"], entry["Channels"]):
            raise ValueError(f"Metadata mismatch: {path}")
    print(f"Verified {len(entries)} original BVHs: hashes, hierarchy, frames, channel counts, finite numeric samples.")


if __name__ == "__main__":
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--verify", action="store_true", help="Validate bundled files without downloading/rebuilding")
    args = parser.parse_args()
    verify() if args.verify else rebuild()
