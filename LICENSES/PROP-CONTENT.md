# Static model library

The 255 static models listed in `assets/content/manifests/prop-models.json` are
curated from official [Kenney](https://kenney.nl/) asset packages. All four
packages are distributed under **Creative Commons Zero 1.0 Universal (CC0-1.0)**.

These models may be bundled with Previs and used in personal, educational and
commercial projects, including commercial rendered images and videos. Credit
is appreciated by Kenney but is not required by CC0. Kenney does not endorse
Previs. The public-domain dedication does not grant third-party trademark or
personality rights.

License: <https://creativecommons.org/publicdomain/zero/1.0/>

Legal code: <https://creativecommons.org/publicdomain/zero/1.0/legalcode>

| Original package | Author | Original version | Included models | Official source |
| --- | --- | --- | ---: | --- |
| Furniture Kit | Kenney | 2.0 | 119 | <https://kenney.nl/assets/furniture-kit> |
| Car Kit | Kenney | 3.1 | 21 | <https://kenney.nl/assets/car-kit> |
| Nature Kit | Kenney | 2.1 | 78 | <https://kenney.nl/assets/nature-kit> |
| Modular Buildings | Kenney | 2.1 | 37 | <https://kenney.nl/assets/modular-buildings> |

Versions above are read from each downloaded ZIP's `License.txt`; the website's
changelog and package counts may use different revision numbers. Every selected
model retains per-model source, author, license and SHA-256 metadata in the
manifest. Original notices are preserved as:

- `assets/content/props/furniture-LICENSE.txt`
- `assets/content/props/cars-LICENSE.txt`
- `assets/content/props/nature-LICENSE.txt`
- `assets/content/props/buildings-LICENSE.txt`

The library includes 71 furniture models, 20 vehicles, 63 architectural models,
55 nature models and 46 props. Category counts differ from source-pack counts
because, for example, the Nature Kit also supplies a canoe, bridges and tents.

## Packaging and modifications

The original mesh geometry, palette colors, material parameters and hierarchy
are retained. In Car Kit and Modular Buildings models, the external PNG color
palette has been embedded into the GLB binary so models remain self-contained
when Previs is exported. Furniture Kit and Nature Kit models already contain
their material colors. No remote download is needed while using the library.

Models are stored as `.glb.bytes` to preserve original glTF binary data in Godot
exports. Manifest paths end in `.glb`; the application loads the adjacent
`.glb.bytes` payload. Placement scale is metadata, not a destructive geometry
edit: furniture generally uses 2x, modular architecture 4x, cars 1.5x and trees
3x source scale. Dining tables use 2.3x for a 0.75 m working height. Kenney's
modular building pieces use a consistent 4x grid.

The selection excludes standalone vehicle wheels/debris, loose furniture drawer
parts and most color-only variants. Different table/chair/building geometries
and a few useful natural shape variants are kept as separate assets.

## Reproduce and verify

Run `python tools/content/import_prop_content.py` from the repository root.
Downloads are cached in ignored `.tools/prop-content/`. The script writes only
the owned static-model content, static-model manifest and validation record.
Use `--offline` to rebuild from the existing source archives without downloads.

`assets/content/props/content-validation.json` records the official archive URLs,
archive SHA-256 checksums, per-model original checksums, output sizes, vertex
counts, material counts, embedded-image counts and scaled bounds. Validation
checks GLB 2.0 headers/chunks, finite mesh positions, valid triangle indices,
buffer ranges and that all PNG images and buffers are embedded. It is a
structural check; the application smoke
test additionally checks actual Godot loading.

Source packages were downloaded directly from Kenney on 2026-10-24.
