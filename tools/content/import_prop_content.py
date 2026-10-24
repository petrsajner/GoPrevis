"""Rebuild Previs's curated CC0 static model library from official Kenney ZIPs.

Python 3.11+, standard library only. Run from the repository root. Downloads stay
in ignored .tools/prop-content; only selected, self-contained GLBs are shipped.
"""
from __future__ import annotations

import argparse
import hashlib
import itertools
import json
import math
import pathlib
import posixpath
import re
import struct
import urllib.request
import zipfile
from collections import Counter

ROOT = pathlib.Path(__file__).resolve().parents[2]
OUT = ROOT / "assets/content/props"
MANIFEST = ROOT / "assets/content/manifests/prop-models.json"
PACKS = {
    "furniture": ("Furniture Kit", "2.0", "furniture-kit", "440e0608a4-1677580847", "Models/GLTF format/"),
    "cars": ("Car Kit", "3.1", "car-kit", "1a312ec241-1775131960", "Models/GLB format/"),
    "nature": ("Nature Kit", "2.1", "nature-kit", "37ac38a37b-1677698939", "Models/GLTF format/"),
    "buildings": ("Modular Buildings", "2.1", "modular-buildings", "3253b4219a-1707397411", "Models/GLB format/"),
}

# Different furniture geometry is retained; loose drawers, color-only pillows
# and duplicate sectional pieces are omitted from the main placement library.
FURNITURE_EXCLUDE = set("""bathroomCabinetDrawer cabinetBedDrawer cabinetBedDrawerTable
floorCornerRound floorHalf pillowBlue pillowBlueLong pillowLong
coatRack loungeDesignSofaCorner loungeSofaCorner loungeSofaOttoman
plantSmall2 plantSmall3 rugRounded rugSquare sideTableDrawers
stairsOpenSingle tableCrossCloth toiletSquare wallCornerRond""".split())
VEHICLES = """ambulance delivery-flat delivery firetruck garbage-truck hatchback-sports
police race-future race sedan-sports sedan suv-luxury suv taxi tractor-shovel tractor
truck-flat truck van""".split()
NATURE = """bridge_stone bridge_stoneNarrow bridge_wood bridge_woodNarrow
cactus_short cactus_tall campfire_logs campfire_stones canoe canoe_paddle
cliff_block_rock cliff_blockSlope_rock cliff_cave_rock cliff_corner_rock cliff_top_rock
crops_bambooStageB crops_cornStageD crops_wheatStageB crop_carrot crop_melon crop_pumpkin
fence_corner fence_gate fence_planks fence_simple
flower_purpleA flower_redC flower_yellowB grass grass_large grass_leafs
ground_grass ground_pathStraight ground_pathCorner ground_pathCross
ground_riverStraight ground_riverCorner lily_large log log_stack mushroom_red
path_stone path_wood plant_bushDetailed plant_bushLarge plant_bushSmall pot_large pot_small
rock_largeA rock_largeB rock_largeC rock_smallA rock_smallFlatA rock_tallA rock_tallF
sign statue_column statue_head statue_obelisk stump_round
tent_detailedOpen tent_smallClosed tree_blocks tree_cone tree_default tree_detailed
tree_oak tree_palmBend tree_palmDetailedTall tree_palmShort tree_pineDefaultA
tree_pineRoundC tree_pineSmallA tree_pineTallA_detailed tree_plateau tree_small tree_tall tree_thin""".split()
BUILDINGS = """building-block building-corner building-door building-door-window
building-sample-house-a building-sample-house-b building-sample-house-c
building-sample-tower-a building-sample-tower-b building-sample-tower-c
building-steps-narrow building-steps-wide building-window-awnings
building-window-balcony building-window-door-window building-window-large
building-window-sill building-window-wide building-windows-round building-windows
detail-ac-a door-brown door-brown-glass door-white-window
roof-flat-center roof-flat-corner roof-flat-border-straight roof-gable
roof-gable-end roof-slanted roof-slanted-corner-a roof-slanted-window
window-brown window-brown-large window-brown-round-wide window-brown-tall
window-white-detailed""".split()

NAMES = {
    "bear": "Teddy Bear", "cabinetBed": "Bedside Cabinet", "cabinetTelevision": "TV Cabinet",
    "cabinetTelevisionDoors": "TV Cabinet with Doors", "bedBunk": "Bunk Bed",
    "bedDouble": "Double Bed", "bedSingle": "Single Bed", "chairDesk": "Office Chair",
    "chairModernFrameCushion": "Modern Frame Chair", "computerScreen": "Computer Monitor",
    "hoodLarge": "Large Extractor Hood", "hoodModern": "Modern Extractor Hood",
    "kitchenFridge": "Refrigerator", "kitchenFridgeBuiltIn": "Built-in Refrigerator",
    "kitchenFridgeLarge": "Large Refrigerator", "kitchenFridgeSmall": "Small Refrigerator",
    "loungeChair": "Armchair", "loungeChairRelax": "Recliner", "loungeDesignChair": "Designer Armchair",
    "loungeSofa": "Sofa", "loungeSofaLong": "Long Sofa", "loungeDesignSofa": "Designer Sofa",
    "plantSmall1": "Small Houseplant", "stoolBar": "Bar Stool", "stoolBarSquare": "Square Bar Stool",
    "tableCoffee": "Coffee Table", "tableCoffeeGlass": "Glass Coffee Table",
    "tableCoffeeGlassSquare": "Square Glass Coffee Table", "tableCoffeeSquare": "Square Coffee Table",
    "tableGlass": "Glass Dining Table", "tableRound": "Round Dining Table", "table": "Dining Table",
    "tableCloth": "Dining Table with Cloth", "tableCross": "Trestle Table",
    "wallWindowSlide": "Wall with Sliding Window", "wallWindow": "Wall with Window",
    "wallDoorway": "Wall with Doorway", "wallDoorwayWide": "Wall with Wide Doorway",
    "sedan": "Sedan", "sedan-sports": "Sports Sedan", "hatchback-sports": "Sport Hatchback",
    "suv": "SUV", "suv-luxury": "Luxury SUV", "truck": "Pickup Truck",
    "truck-flat": "Flatbed Pickup", "delivery": "Delivery Truck", "delivery-flat": "Flatbed Delivery Truck",
    "tractor-shovel": "Loader Tractor", "race": "Race Car", "race-future": "Futuristic Race Car",
    "box": "Shipping Crate", "cone": "Traffic Cone", "tree_default": "Broadleaf Tree",
    "tree_pineDefaultA": "Pine Tree", "tree_pineTallA_detailed": "Tall Detailed Pine",
    "tree_pineRoundC": "Round Pine", "tree_pineSmallA": "Small Pine",
    "tree_palmBend": "Bent Palm", "tree_palmDetailedTall": "Tall Palm", "tree_palmShort": "Short Palm",
    "plant_bushDetailed": "Detailed Bush", "plant_bushLarge": "Large Bush", "plant_bushSmall": "Small Bush",
    "tent_detailedOpen": "Open Camping Tent", "tent_smallClosed": "Small Camping Tent",
    "crops_bambooStageB": "Bamboo", "crops_cornStageD": "Corn Plants", "crops_wheatStageB": "Wheat",
    "flower_purpleA": "Purple Flowers", "flower_redC": "Red Flowers", "flower_yellowB": "Yellow Flowers",
    "detail-ac-a": "Air Conditioner", "building-sample-house-a": "Town House A",
    "building-sample-house-b": "Town House B", "building-sample-house-c": "Town House C",
    "building-sample-tower-a": "Apartment Building A", "building-sample-tower-b": "Apartment Building B",
    "building-sample-tower-c": "Office Tower", "building-block": "Building Block",
}


def display_name(name: str) -> str:
    if name in NAMES:
        return NAMES[name]
    s = re.sub(r"([a-z0-9])([A-Z])", r"\1 \2", name).replace("_", " ").replace("-", " ")
    return s.title().replace("Television", "TV").replace("Ac ", "AC ")


def slug(name: str) -> str:
    return re.sub(r"[^a-z0-9]+", "-", re.sub(r"([a-z0-9])([A-Z])", r"\1-\2", name).lower()).strip("-")


def unpack_glb(raw: bytes) -> tuple[dict, bytes]:
    magic, version, length = struct.unpack_from("<III", raw)
    assert magic == 0x46546C67 and version == 2 and length == len(raw), "Invalid GLB header"
    offset, doc, binary = 12, None, b""
    while offset < len(raw):
        n, kind = struct.unpack_from("<II", raw, offset)
        chunk = raw[offset + 8:offset + 8 + n]
        assert len(chunk) == n and n % 4 == 0
        if kind == 0x4E4F534A:
            doc = json.loads(chunk)
        elif kind == 0x004E4942:
            binary = chunk
        else:
            raise AssertionError(f"Unsupported GLB chunk {kind:x}")
        offset += 8 + n
    assert doc and offset == len(raw)
    return doc, binary


def self_contained(raw: bytes, archive: zipfile.ZipFile, source: str) -> bytes:
    doc, binary = unpack_glb(raw)
    assert len(doc.get("buffers", [])) == 1 and "uri" not in doc["buffers"][0]
    binary = bytearray(binary[:doc["buffers"][0]["byteLength"]])
    changed = False
    for image in doc.get("images", []):
        if "uri" not in image:
            continue
        uri = image.pop("uri")
        assert not uri.startswith(("http:", "https:", "data:"))
        path = posixpath.normpath(posixpath.join(posixpath.dirname(source), uri))
        picture = archive.read(path)
        assert picture.startswith(b"\x89PNG\r\n\x1a\n"), path
        binary.extend(b"\x00" * (-len(binary) % 4))
        image["bufferView"] = len(doc.setdefault("bufferViews", []))
        image["mimeType"] = "image/png"
        doc["bufferViews"].append({"buffer": 0, "byteOffset": len(binary), "byteLength": len(picture)})
        binary.extend(picture)
        changed = True
    if not changed:
        return raw
    doc["buffers"][0]["byteLength"] = len(binary)
    encoded = json.dumps(doc, ensure_ascii=False, separators=(",", ":")).encode("utf8")
    encoded += b" " * (-len(encoded) % 4)
    binary.extend(b"\x00" * (-len(binary) % 4))
    length = 12 + 8 + len(encoded) + 8 + len(binary)
    return (struct.pack("<III", 0x46546C67, 2, length) + struct.pack("<II", len(encoded), 0x4E4F534A)
            + encoded + struct.pack("<II", len(binary), 0x004E4942) + binary)


def node_matrix(node: dict) -> list[list[float]]:
    if "matrix" in node:
        return [[node["matrix"][c * 4 + r] for c in range(4)] for r in range(4)]
    x, y, z, w = node.get("rotation", [0, 0, 0, 1])
    scale = node.get("scale", [1, 1, 1])
    t = node.get("translation", [0, 0, 0])
    result = [[1-2*y*y-2*z*z, 2*x*y-2*z*w, 2*x*z+2*y*w, t[0]],
              [2*x*y+2*z*w, 1-2*x*x-2*z*z, 2*y*z-2*x*w, t[1]],
              [2*x*z-2*y*w, 2*y*z+2*x*w, 1-2*x*x-2*y*y, t[2]], [0, 0, 0, 1]]
    for r in range(3):
        for c in range(3):
            result[r][c] *= scale[c]
    return result


def multiply(a: list, b: list) -> list:
    return [[sum(a[r][k] * b[k][c] for k in range(4)) for c in range(4)] for r in range(4)]


def scaled_bounds(doc: dict, scale: float) -> list[float]:
    points = []
    identity = [[int(r == c) for c in range(4)] for r in range(4)]

    def walk(index: int, parent: list) -> None:
        node = doc["nodes"][index]
        world = multiply(parent, node_matrix(node))
        if "mesh" in node:
            for primitive in doc["meshes"][node["mesh"]]["primitives"]:
                a = doc["accessors"][primitive["attributes"]["POSITION"]]
                for v in itertools.product(*zip(a["min"], a["max"])):
                    points.append([sum(world[ax][c] * (*v, 1)[c] for c in range(4)) * scale for ax in range(3)])
        for child in node.get("children", []):
            walk(child, world)

    for node in doc["scenes"][doc.get("scene", 0)]["nodes"]:
        walk(node, identity)
    return [round(max(p[a] for p in points) - min(p[a] for p in points), 5) for a in range(3)]


def validate(raw: bytes, scale: float) -> dict:
    doc, binary = unpack_glb(raw)
    assert doc["asset"]["version"] == "2.0"
    assert doc.get("meshes") and doc.get("scenes") and doc.get("nodes")
    assert not doc.get("skins"), "Static library unexpectedly contains a skeleton"
    for b in doc.get("buffers", []):
        assert "uri" not in b and b["byteLength"] <= len(binary)
    for image in doc.get("images", []):
        assert "uri" not in image and "bufferView" in image
        view = doc["bufferViews"][image["bufferView"]]
        offset = view.get("byteOffset", 0)
        assert binary[offset:offset + 8] == b"\x89PNG\r\n\x1a\n"
    for view in doc.get("bufferViews", []):
        assert view.get("buffer", 0) == 0
        assert view.get("byteOffset", 0) + view["byteLength"] <= len(binary)
    def read_accessor(a: dict):
        view = doc["bufferViews"][a["bufferView"]]
        fmt = {5120: "b", 5121: "B", 5122: "h", 5123: "H", 5125: "I", 5126: "f"}[a["componentType"]]
        components = {"SCALAR": 1, "VEC2": 2, "VEC3": 3, "VEC4": 4, "MAT4": 16}[a["type"]]
        item = struct.Struct("<" + fmt * components)
        stride = view.get("byteStride", item.size)
        start = view.get("byteOffset", 0) + a.get("byteOffset", 0)
        for index in range(a["count"]):
            yield item.unpack_from(binary, start + index * stride)

    vertices, primitives, triangles = 0, 0, 0
    for mesh in doc["meshes"]:
        for p in mesh["primitives"]:
            assert "POSITION" in p["attributes"]
            a = doc["accessors"][p["attributes"]["POSITION"]]
            assert a["type"] == "VEC3" and a["count"] > 0 and a.get("bufferView") is not None
            assert a.get("min") is not None and a.get("max") is not None
            assert all(math.isfinite(value) for xyz in read_accessor(a) for value in xyz)
            if "indices" in p:
                indices = [item[0] for item in read_accessor(doc["accessors"][p["indices"]])]
                assert min(indices) >= 0 and max(indices) < a["count"]
                triangles += len(indices) // 3
            vertices += a["count"]
            primitives += 1
    assert vertices > 0 and primitives > 0
    return {"Vertices": vertices, "Triangles": triangles, "Primitives": primitives,
            "Materials": len(doc.get("materials", [])), "EmbeddedImages": len(doc.get("images", [])),
            "ScaledSizeMetres": scaled_bounds(doc, scale)}


def category_scale(pack: str, name: str) -> tuple[str, float]:
    if pack == "buildings":
        return "Architecture", 4.0
    if pack == "cars":
        if name in ("box", "cone"):
            return "Props", 1.0
        return "Vehicles", 1.7 if name in ("firetruck", "delivery", "delivery-flat", "garbage-truck", "tractor", "tractor-shovel") else 1.5
    if pack == "furniture":
        if name in ("table", "tableCloth", "tableGlass", "tableRound", "tableCross"):
            return "Furniture", 2.3
        if name.startswith(("wall", "stairs", "floor", "doorway", "paneling")):
            return "Architecture", 2.0
        if name.startswith(("plant", "potted")):
            return "Nature", 2.0
        if name.startswith(("computer", "television", "lamp", "rug", "pillow", "cardboard")) or name in (
                "bear", "books", "ceilingFan", "kitchenBlender", "kitchenCoffeeMachine", "kitchenMicrowave",
                "laptop", "radio", "speaker", "speakerSmall", "toaster", "trashcan"):
            return "Props", 2.0
        return "Furniture", 2.0
    if name == "canoe":
        return "Vehicles", 3.0
    if name.startswith(("bridge", "fence", "path_")):
        return "Architecture", 3.0 if name.startswith("bridge") else 2.5
    if name.startswith(("campfire", "crop_", "pot_", "statue", "tent", "sign")) or name == "canoe_paddle":
        return "Props", 3.0 if name.startswith("tent") or name == "canoe_paddle" else 2.0
    return "Nature", 3.0 if name.startswith(("tree_", "cliff")) else 2.0


def run(offline: bool = False) -> None:
    cache = ROOT / ".tools/prop-content"
    cache.mkdir(parents=True, exist_ok=True)
    OUT.mkdir(parents=True, exist_ok=True)
    MANIFEST.parent.mkdir(parents=True, exist_ok=True)
    records, checks, sources = [], [], []
    for pack, (title, version, source_slug, stamp, prefix) in PACKS.items():
        url = f"https://kenney.nl/media/pages/assets/{source_slug}/{stamp}/kenney_{source_slug}.zip"
        archive_path = cache / f"{pack}.zip"
        if not archive_path.exists():
            if offline:
                raise FileNotFoundError(archive_path)
            urllib.request.urlretrieve(url, archive_path)
        with zipfile.ZipFile(archive_path) as archive:
            candidates = {pathlib.PurePosixPath(p).stem: p for p in archive.namelist() if p.startswith(prefix) and p.endswith(".glb")}
            selected = (sorted(set(candidates) - FURNITURE_EXCLUDE) if pack == "furniture" else
                        VEHICLES + ["box", "cone"] if pack == "cars" else NATURE if pack == "nature" else BUILDINGS)
            assert len(selected) == len(set(selected))
            license_bytes = archive.read("License.txt")
            license_text = license_bytes.decode("utf8", errors="replace")
            assert "CC0" in license_text
            (OUT / f"{pack}-LICENSE.txt").write_bytes(license_bytes)
            source_url = f"https://kenney.nl/assets/{source_slug}"
            sources.append({"Pack": title, "Version": version, "SourceUrl": source_url, "DownloadUrl": url,
                            "ArchiveSha256": hashlib.sha256(archive_path.read_bytes()).hexdigest(), "SelectedModels": len(selected)})
            for name in selected:
                original = archive.read(candidates[name])
                payload = self_contained(original, archive, candidates[name])
                category, scale = category_scale(pack, name)
                stats = validate(payload, scale)
                dest = OUT / pack / f"{slug(name)}.glb.bytes"
                dest.parent.mkdir(parents=True, exist_ok=True)
                dest.write_bytes(payload)
                resource = "res://" + dest.relative_to(ROOT).as_posix().removesuffix(".bytes")
                record = {"Id": f"kenney-{pack}-{slug(name)}", "Name": display_name(name), "Category": category,
                          "ActorFamily": "", "SourcePath": resource, "Scale": scale, "DefaultAnimation": None,
                          "Animated": False, "Attribution": f"Kenney — {title} {version}", "License": "CC0-1.0",
                          "SourceUrl": source_url, "Sha256": hashlib.sha256(payload).hexdigest()}
                records.append(record)
                checks.append({"Id": record["Id"], "Bytes": len(payload), "OriginalSha256": hashlib.sha256(original).hexdigest(), **stats})
    records.sort(key=lambda r: (r["Category"], r["Name"]))
    assert len({r["Id"] for r in records}) == len(records)
    MANIFEST.write_text(json.dumps(records, indent=2, ensure_ascii=False) + "\n", encoding="utf8")
    report = {"Models": len(records), "TotalBytes": sum(c["Bytes"] for c in checks),
              "Categories": dict(sorted(Counter(r["Category"] for r in records).items())), "Sources": sources,
              "Validation": "All GLB 2.0 headers, chunks, meshes, finite POSITION data, index ranges, buffer ranges and embedded PNGs checked.",
              "GeometryValidation": {"FinitePositionVertices": sum(c["Vertices"] for c in checks),
                                     "ValidIndexedTriangles": sum(c["Triangles"] for c in checks),
                                     "EmbeddedPngImages": sum(c["EmbeddedImages"] for c in checks), "AllIndicesInRange": True},
              "ModelsValidated": checks}
    (OUT / "content-validation.json").write_text(json.dumps(report, indent=2) + "\n", encoding="utf8")
    print(json.dumps({k: report[k] for k in ("Models", "TotalBytes", "Categories")}, indent=2))


if __name__ == "__main__":
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--offline", action="store_true", help="Only use existing source ZIP cache")
    run(parser.parse_args().offline)
