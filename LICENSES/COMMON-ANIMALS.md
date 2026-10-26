# Additional common animal models

Retrieved and verified 2026-10-26. All six additions use **CC0 1.0 Universal**,
https://creativecommons.org/publicdomain/zero/1.0/. No GobKit asset is included.

- **Cat, Dog, Eagle**: Quaternius, Animal Pack Vol.2. Original creator upload:
  https://opengameart.org/content/animated-animales-low-poly
- **Pig, Sheep**: Quaternius, Farm Animals. Original creator upload:
  https://opengameart.org/content/lowpoly-animated-farm-animal-pack
  and https://quaternius.com/packs/farmanimal.html
- **Rabbit**: CDmir, with TinyWorlds; original creator upload:
  https://opengameart.org/content/rabbit-0 . Source includes its diffuse/normal
  textures and FBX rig/animations. The GLB embeds the used textures.

Original archives are cached under ignored output/library-expansion-2026-10-26.
Their hashes, packed model hashes, authors and source links are recorded in
assets/content/animals/common/provenance.json. The original Farm Animals license
notice is retained alongside this document.

Godot's native FBX importer/exporter converted the original self-contained model
data into GLB. Source joint names on cat/dog/eagle were replaced with descriptive
English names; unweighted helpers are hidden from the pose editor. A constant
source-axis correction aligns the visible animal front with its native +Z forward.
Catalog scaling uses the evaluated skin's actual bounds. The source geometry and
recorded/authored clip channels are retained. Detached deforming pig/sheep feet
are linked at runtime to their lower legs while preserving the authored sampled
world pose, as for the earlier prepared animals.

These are authored animation cycles, not animal mocap. Cat/Dog provide Idle and
Walking; Pig/Sheep provide Idle and Jump; Eagle provides Flying and Idle. Rabbit
provides the usable source actions including Running, Jump, Guarding, Sitting and
Dying. Empty/static calibration actions are excluded. No missing walking/run
action is advertised for a species whose source does not contain it.

Reproduction: cache the publisher archives, run the Godot source FBX conversion,
tools/content/prepare_common_animals.py, then the source application with
--prepare-library-content-test to measure normalization and render thumbnails.
