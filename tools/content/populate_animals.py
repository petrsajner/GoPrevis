"""Reproduce Previs' bundled CC0 Quaternius animal content from the creator's links.

Self-contained glTF buffers are repacked losslessly as GLB. The .bytes suffix keeps
Godot's editor from importing the whole library; the app materializes a cached GLB
on demand. This script does not generate or retarget animation.
"""
from __future__ import annotations

import base64
from concurrent.futures import ThreadPoolExecutor
import hashlib
import json
from pathlib import Path
import re
import struct
import urllib.request

ROOT = Path(__file__).resolve().parents[2]
DEST = ROOT / "assets/content/animals"
MANIFESTS = ROOT / "assets/content/manifests"
SOURCE = "https://quaternius.com/packs/ultimateanimatedanimals.html"
DRIVE = "https://drive.google.com/uc?export=download&id="
# IDs come from the glTF folder linked by the creator's pack page, not a mirror.
# Target heights include ears/antlers and are convenient blocking proportions.
ANIMALS = [
    ("alpaca", "Alpaca", "13dxCPosR1tVx24LKXr6gN44sIOY4hZgo", 1.75),
    ("bull", "Bull", "1M9gyr2UikIDW_Ynp-OfgiK-JjI7ziPd3", 1.65),
    ("cow", "Cow", "1lS3t1Sof0FVne1C1WXfdX48qaHES_pDG", 1.60),
    ("deer", "Deer", "1iGpXKrqYGyZCPGHPPSuDAoKnOXLhXJ0q", 1.55),
    ("donkey", "Donkey", "1Buic-_4vNtmwN0rtMHcdWw3TEaSPz4iW", 1.55),
    ("fox", "Fox", "1z-CWoUC2vJxrqgGFTYlMaywpE1ooV-bA", .65),
    ("horse-white", "White Horse", "1sb2XaaPjEV9Z3a1bG8ReISiGlRXrSA7W", 2.10),
    ("horse", "Horse", "1hbtY8kxnXiPdwYGVY7rWRgU0jl_-Q-LG", 2.10),
    ("husky", "Husky", "1oYn47mfq9JAdkJJDcUftbhHNsgQ_CWdt", .85),
    ("shiba-inu", "Shiba Inu", "1XWUVbmMbiG9E90OqrumueD_pBdnYdyHZ", .65),
    ("stag", "Stag", "1URNoFeIFblJXPFOV6qwPrxZr5dLZ3YGx", 2.20),
    ("wolf", "Wolf", "1lFQoQ9ln2Z2wGuFFWObj9i5jHqUl_ftG", 1.00),
]


def fetch(url: str) -> bytes:
    request = urllib.request.Request(url, headers={"User-Agent": "Previs-content-import/1.0"})
    with urllib.request.urlopen(request, timeout=90) as response:
        return response.read()


def gltf_to_glb(document: dict) -> bytes:
    assert len(document["buffers"]) == 1, "Expected a self-contained single-buffer glTF"
    uri = document["buffers"][0].pop("uri")
    assert uri.startswith("data:application/octet-stream;base64,")
    binary = base64.b64decode(uri.split(",", 1)[1], validate=True)
    assert len(binary) == document["buffers"][0]["byteLength"]
    assert not document.get("images"), "Unexpected external/textured dependency"
    # Exported Blender meshes are skinned and materials are vertex/flat colours.
    encoded = json.dumps(document, separators=(",", ":"), ensure_ascii=False).encode()
    encoded += b" " * (-len(encoded) % 4)
    binary += b"\0" * (-len(binary) % 4)
    return (struct.pack("<III", 0x46546C67, 2, 28 + len(encoded) + len(binary))
            + struct.pack("<II", len(encoded), 0x4E4F534A) + encoded
            + struct.pack("<II", len(binary), 0x004E4942) + binary)


def friendly(name: str) -> str:
    special = {"Idle_2": "Idle Variation", "Idle_Headlow": "Head Low Idle",
               "Idle_HitReact1": "Hit Reaction 1", "Idle_HitReact2": "Hit Reaction 2",
               "Jump_toIdle": "Jump to Idle", "Attack_Headbutt": "Headbutt",
               "Attack_Kick": "Kick", "Gallop_Jump": "Gallop Jump"}
    return special.get(name, re.sub(r"(?<=[a-z])(?=[A-Z])", " ", name).replace("_", " "))


def category(name: str) -> str:
    if name == "Walk":
        return "Walk"
    if name in ("Gallop", "Run"):
        return "Run"
    if name.startswith("Idle") and "HitReact" not in name:
        return "Idle"
    return "Action"


def process(item: tuple) -> tuple:
    slug, name, file_id, height = item
    data = fetch(DRIVE + file_id)
    document = json.loads(data)
    assert document.get("skins") and len(document["skins"][0]["joints"]) > 10
    assert len(document.get("animations", [])) >= 12
    positions = [document["accessors"][p["attributes"]["POSITION"]]
                 for mesh in document["meshes"] for p in mesh["primitives"]]
    raw_min = [min(p["min"][i] for p in positions) for i in range(3)]
    raw_max = [max(p["max"][i] for p in positions) for i in range(3)]
    # All of these source mesh nodes and their armature ancestor have identity
    # transforms, so mesh-space bounds also give the neutral scene bounds.
    for node in document["nodes"]:
        if "mesh" in node:
            assert "matrix" not in node and node.get("scale", [1, 1, 1]) == [1, 1, 1]
    model_scale = height / (raw_max[1] - raw_min[1])
    family = f"animal-quaternius-{slug}"
    path = f"res://assets/content/animals/{slug}.glb"
    attribution = "Quaternius · Ultimate Animated Animal Pack (CC0)"
    model = {"Id": f"animal-{slug}", "Name": name, "Category": "Animals",
             "ActorFamily": family, "SourcePath": path, "Scale": model_scale,
             "DefaultAnimation": "Idle", "Animated": True,
             "Attribution": attribution, "License": "CC0-1.0", "SourceUrl": SOURCE}
    motions = []
    for animation in document["animations"]:
        clip = animation["name"]
        assert animation["channels"] and animation["samplers"]
        inputs = [document["accessors"][s["input"]] for s in animation["samplers"]]
        duration = max(a["max"][0] for a in inputs) - min(a["min"][0] for a in inputs)
        assert duration > 0 and all(a["count"] > 1 for a in inputs)
        motions.append({"Id": f"animal-{slug}-{clip.lower().replace('_', '-')}",
                        "Name": f"{name} · {friendly(clip)}", "ActorFamily": family,
                        "SourcePath": path, "SourceAnimation": clip, "SourceScale": 1,
                        "Duration": duration, "Category": category(clip),
                        "Loop": clip in ("Walk", "Gallop", "Eating") or (clip.startswith("Idle") and "HitReact" not in clip),
                        "Tags": ["animal", slug, "native", "authored", category(clip).lower()],
                        "Attribution": attribution, "License": "CC0-1.0", "SourceUrl": SOURCE})
    packed = gltf_to_glb(document)
    (DEST / f"{slug}.glb.bytes").write_bytes(packed)
    model["Sha256"] = hashlib.sha256(packed).hexdigest()
    original_name = {"horse-white": "Horse_White", "shiba-inu": "ShibaInu"}.get(slug, name)
    provenance = {"Id": model["Id"], "Name": name, "OriginalFileName": f"{original_name}.gltf",
                  "DownloadUrl": DRIVE + file_id, "SourceGltfSha256": hashlib.sha256(data).hexdigest(),
                  "PackedGlbSha256": model["Sha256"], "Bytes": len(packed),
                  "Bones": len(document["skins"][0]["joints"]), "AnimationCount": len(motions),
                  "RawBoundsMin": raw_min, "RawBoundsMax": raw_max,
                  "TargetHeightMetres": height, "ModelScale": model_scale,
                  "ForwardAxis": "+Z", "Transformation": "Lossless self-contained glTF-to-GLB container repack"}
    print(f"{name}: {len(motions)} clips, {provenance['Bones']} bones, {len(packed):,} bytes, scale {model_scale:.6f}", flush=True)
    return model, motions, provenance


def write_json(path: Path, value: object) -> None:
    path.write_text(json.dumps(value, ensure_ascii=False, indent=2) + "\n", encoding="utf-8")


def main() -> None:
    DEST.mkdir(parents=True, exist_ok=True)
    MANIFESTS.mkdir(parents=True, exist_ok=True)
    results = list(ThreadPoolExecutor(max_workers=4).map(process, ANIMALS))
    license_text = fetch(DRIVE + "1F2uy8T2fRpdc6gZ4mnS02_C2E63WvKtn").decode("utf-8")
    (DEST / "SOURCE-LICENSE.txt").write_text(license_text.replace("\r", ""), encoding="utf-8")
    write_json(MANIFESTS / "animal-models.json", [result[0] for result in results])
    write_json(MANIFESTS / "animal-motions.json", [motion for result in results for motion in result[1]])
    write_json(DEST / "provenance.json", {"RetrievedDate": "2026-10-24", "SourceUrl": SOURCE,
               "License": "CC0-1.0", "Models": [result[2] for result in results]})
    print(f"Bundled {len(results)} animated models and {sum(len(r[1]) for r in results)} native clips.")


if __name__ == "__main__":
    main()
