# CMU recorded human motion content

This notice covers the **300 BVH files** under
`assets/content/motions/human/CMU_*.bvh`, described by
`assets/content/manifests/human-motions.json`. They are real recorded human
motion, not generated demonstration clips. The source is the Carnegie Mellon
University Graphics Lab Motion Capture Database, converted to MotionBuilder
friendly BVH by Bruce Hahne / cgspeed.com in the publisher's 2010 release.

Retrieved and permissions checked on **2026-10-24**. The `CMU` catalog license
identifier refers to these source-specific terms; it is not an SPDX identifier,
CC0, or a Creative Commons license.

## Current CMU usage statement

CMU's database homepage says:

> This data is free for use in research projects. You may include this data in
> commercially-sold products, but you may not resell this data directly, even
> in converted form.

The statement permits including the motions as the working motion library of
this application. Do not sell these source files as a standalone motion dataset.
The database requests acknowledgment and notification if research results are
published. Its requested acknowledgment is:

> The data used in this project was obtained from mocap.cs.cmu.edu. The database
> was created with funding from NSF EIA-0196217.

CMU source: <http://mocap.cs.cmu.edu/>. The HTTP page was read directly; the HTTPS
endpoint currently has a certificate-chain verification problem in this Python
environment. The CMU homepage also links to the cgspeed conversions, and its
resources page is <https://mocap.cs.cmu.edu/resources.php>.

## Conversion publisher

Bruce Hahne's 2010 `READMEFIRST.txt`, section `USAGE RIGHTS`, states:

> CMU places no restrictions on the use of the original dataset, and I
> (Bruce) place no additional restrictions on the use of this particular
> BVH conversion.

That README quotes an older, broader CMU usage paragraph. The current CMU
restriction on directly reselling data, stated above, remains part of this
content notice. The conversion publisher supplies the release as-is, without
support or guarantees.

- Publisher and archive links:
  <https://sites.google.com/a/cgspeed.com/cgspeed/motion-capture/the-motionbuilder-friendly-bvh-conversion-release-of-cmus-motion-capture-database>
- Publisher's 2010 README:
  <https://sites.google.com/a/cgspeed.com/cgspeed/motion-capture/the-motionbuilder-friendly-bvh-conversion-release-of-cmus-motion-capture-database/readme-motionbuilder-2010-release>
- Publisher's complete descriptive index:
  <https://sites.google.com/a/cgspeed.com/cgspeed/motion-capture/the-motionbuilder-friendly-bvh-conversion-release-of-cmus-motion-capture-database/bvh-conversion-release-motions-list>

The original publisher README and original descriptive index are included,
unchanged, next to the BVH files as `READMEFIRST-cgspeed.txt` and
`cmu-mocap-index-text.txt`. The index describes the entire source database;
**only subjects 01–19 that exist in the three archives below are bundled**.

## Acquisition and integrity

These are the publisher-linked **2010** archives, not the older 2008 release.
Archives are a reproducible download cache under ignored `output/`, not part of
the bundled library. Each BVH member was copied byte-for-byte with no animation
edits. Its SHA-256, archive member, frame metadata, size, source URL, original
description, and attribution are recorded in the manifest.

| Archive | Publisher-linked download | Bytes | SHA-256 |
| --- | --- | ---: | --- |
| `cmuconvert-mb2-01-09.zip` | <https://www.mediafire.com/?o6ncxuu1oq5r6z9> | 33,755,687 | `8822d96dac5694cb5df35e017fa7df174a051249d48d70d6732a7b68953016d9` |
| `cmuconvert-mb2-10-14.zip` | <https://www.mediafire.com/?z6pr5bk3h1o5045> | 66,748,015 | `4d36c7f0ee005709ce98acf52926ac4b14d9a5b682a7a3ea3ced77d904c51f25` |
| `cmuconvert-mb2-15-19.zip` | <https://www.mediafire.com/?cu0a99w1day8hnr> | 67,803,387 | `24651f3c6db18ee84fcf1f00ae5c3e85204b1084d64c56a16f55cd8b5421c037` |

The 300 BVHs contain 464,133,787 bytes and approximately 85.17 minutes of
recorded takes. There are 104 locomotion, 38 sports, 30 interaction, 25 action,
22 dance, 21 climbing, 21 everyday, 20 sitting, and 19 gesture takes. Categories
are library navigation metadata; original descriptions are preserved separately.

Reproduce the library with `python tools/content/import_human_motions.py`.
Validate already bundled content without a download or rewrite with
`python tools/content/import_human_motions.py --verify`.

## Import behavior and limitations

- Frame 0 is the conversion's added calibration T-pose, facing positive Z.
  The default source offset skips exactly this frame, without modifying the
  source file. The following frames are CMU capture data.
- The files are approximately 120 fps. The manifest's `Duration` excludes the
  initial calibration frame; `FrameCount` includes it.
- Default `SourceScale` is 0.055 metres per source unit, matching the prepared
  human rig used by the application. Original subjects have different physical
  proportions. Retargeting is approximate rather than a resimulation of props
  or body contacts.
- Takes are unedited and are not guaranteed to loop seamlessly. `Loop` defaults
  to false. Several files contain a long sequence of actions rather than a
  short isolated cycle; trim them on the timeline.
- The source skeleton's finger and thumb joints do not contain captured finger
  articulation. Those joints must not be described as finger mocap.
- Two-person takes contain one actor per file. Their original descriptions
  identify subject A or B; both corresponding files are included. Interaction
  alignment and timing need to be maintained when using them together.
- CMU warns that the early numbered capture sessions can be less clean than
  later sessions. The library retains source quality; no cleanup, contacts,
  props, or guarantees of collision-free retargeting are implied.

Application acknowledgment: The motion data was obtained from the CMU Graphics
Lab Motion Capture Database (mocap.cs.cmu.edu), created with funding from NSF
EIA-0196217. BVH conversion by Bruce Hahne / cgspeed.com.
