# Universal Animation Libraries 1 and 2

Retrieved 2026-10-26 from the publisher's free **Standard** archives:

- https://quaternius.itch.io/universal-animation-library
- https://quaternius.itch.io/universal-animation-library-2

Creators: **Quaternius and Gonzalo Furnier**. Both included License.txt files state
**CC0 1.0 Universal / Public Domain Dedication**. Original license and README files
are retained in LICENSES/ual/. No paid Pro/Source archive was purchased or bundled.

Each Standard GLB contains 43 animations including A_TPose. The application adds
42 playable motions from each library, **84 total**. The larger advertised totals
refer to the overall libraries/variants, not the number of distinct actions in
these downloaded Standard files. Root-motion-disabled source versions are used;
paths and final transform layers remain independently editable.

The offline tools/content/prepare_ual_motions.gd samples the source skeleton at
30 fps, calibrates global rotation deltas against its A_TPose, and exports BVH
with 21 canonical prepared-human joints. Translation and timing are retained;
limb proportions are adapted by the existing humanoid retargeter. These are
authored animations, not additional recorded CMU mocap. They do not provide
captured finger articulation on the application's simplified prepared rig.

assets/content/motions/human/ual/provenance.json records original archive/source
hashes, publisher URLs and hashes for every converted BVH. Original GLBs/archives
are development caches under ignored output/ and are not required at runtime.
