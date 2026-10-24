# Bundled animated animals

The files in `assets/content/animals/*.glb.bytes` come from **Ultimate Animated
Animal Pack** by **Quaternius**. These are ready-rigged low-poly animal models with
authored skeletal animation cycles. They are **not motion-capture recordings**.

- Creator's pack page: https://quaternius.com/packs/ultimateanimatedanimals.html
- Creator's download folder (linked by that page): https://drive.google.com/drive/folders/1uJ3N5HfB7jKTseJUNQr3N4YaN0UuEtHk
- glTF subfolder: https://drive.google.com/drive/folders/1yJXdB1iSrI8Db7hG77zxZ66vKsqIt0ry
- Source license file: https://drive.google.com/file/d/1F2uy8T2fRpdc6gZ4mnS02_C2E63WvKtn/view
- License: **CC0 1.0 Universal / Public Domain Dedication**
- License text: https://creativecommons.org/publicdomain/zero/1.0/legalcode
- Retrieved: 2026-10-24

The creator's page identifies this pack as CC0 and permits personal and commercial
use. The source `License.txt` independently identifies the models as CC0 1.0.
An unabridged copy is bundled in `assets/content/animals/SOURCE-LICENSE.txt`.
Attribution is retained in the catalog as a courtesy.

## Content and preparation

Twelve models are bundled: Alpaca, Bull, Cow, Deer, Donkey, Fox, Horse, White Horse,
Husky, Shiba Inu, Stag and Wolf. Together they contain **152 native skeletal
animation clips**, including locomotion, idle, eating, attacks, jumps and reactions.
Each catalog motion references its own model's native clip; animal skeletons are
not treated as interchangeable.

The creator's self-contained glTF files were repacked into GLB containers. Geometry,
materials, skin weights, bones, animations and animation names are preserved. No
external textures are required. The `.bytes` suffix prevents Godot from importing
all models at editor startup; the application materializes each GLB on demand.

Source models have different artistic scales. Catalog scale values normalize their
neutral heights, including ears or antlers, to practical blocking sizes: Fox and
Shiba Inu 0.65 m, Husky 0.85 m, Wolf 1.0 m, Deer and Donkey 1.55 m, Cow 1.6 m,
Bull 1.65 m, Alpaca 1.75 m, Horse and White Horse 2.1 m, Stag 2.2 m. These are
convenient scene-blocking proportions, not anatomical reference measurements.
`+Z` is the models' forward direction. Motion SourceScale is 1: animations play
inside the scaled model and must not be scaled a second time.

`assets/content/animals/provenance.json` records every original download URL, source
SHA-256, packed SHA-256, byte size, bone/animation count and normalization value.
`tools/content/populate_animals.py` reproduces the content and manifests from the
creator's downloads. `tools/content/validate_animals.gd` imports every GLB with
Godot, verifies its rig and named animations, and checks that every clip changes
the skeletal pose.
