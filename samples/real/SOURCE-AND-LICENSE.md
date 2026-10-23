# Real motion capture sample

`CMU_02_01_Walk.bvh` is recorded human motion from the Carnegie Mellon University
Graphics Lab Motion Capture Database: subject 02, motion 01, described as **walk**.
It is not a generated demonstration animation.

The file is an unchanged member (`02/02_01.bvh`) of Bruce Hahne's 2010
MotionBuilder-friendly BVH conversion, archive `cmuconvert-mb2-01-09.zip`.
Only this individual motion is bundled. Downloaded on 2026-10-24.

## Sources and usage permission

- Original capture/database and usage statement: <https://mocap.cs.cmu.edu/>
- CMU's link to the conversion: <https://mocap.cs.cmu.edu/resources.php>
- Conversion publisher and download links:
  <https://sites.google.com/a/cgspeed.com/cgspeed/motion-capture/the-motionbuilder-friendly-bvh-conversion-release-of-cmus-motion-capture-database>
- Publisher's 2010 README, including **USAGE RIGHTS**:
  <https://sites.google.com/a/cgspeed.com/cgspeed/motion-capture/the-motionbuilder-friendly-bvh-conversion-release-of-cmus-motion-capture-database/readme-motionbuilder-2010-release>
- Publisher's motion index:
  <https://sites.google.com/a/cgspeed.com/cgspeed/motion-capture/the-motionbuilder-friendly-bvh-conversion-release-of-cmus-motion-capture-database/bvh-conversion-release-motions-list>
- Download linked by the conversion publisher:
  <https://www.mediafire.com/?o6ncxuu1oq5r6z9>

CMU permits all uses of its motion data, including commercial work. Bruce Hahne
adds no restrictions to this BVH conversion. These are the publishers' own usage
statements; this file is not labeled CC0 or a Creative Commons license.
CMU requests acknowledgment of the database and NSF EIA-0196217 funding.

Acknowledgment: The motion data was obtained from the CMU Graphics Lab Motion
Capture Database (mocap.cs.cmu.edu), created with funding from NSF EIA-0196217.
BVH conversion by Bruce Hahne / cgspeed.com.

These permissions describe this sample only, not other motion libraries.

## Import notes

- 260,091 bytes; 344 frames; 96 animation channels; root plus 30 joints.
- Frame time: 0.0083333 seconds (approximately 120 fps).
- Approximately 2.86 seconds between the first and last samples.
- Frame 0 is the conversion's added calibration T-pose; subsequent frames are
  from the original capture. A single initial T-pose is expected.
- The source skeleton faces +Z in its calibration pose. Its native units need
  normalization when used in a metre-scale scene.
- Synthetic finger/thumb joints in this dataset do not contain captured finger
  articulation.
- The original motion is not guaranteed to loop seamlessly.

SHA-256:

```text
CF56DB43157ACC3D200B3D4215523F54D354A8926BEA67E1F6C8031BD335BA7E
```
