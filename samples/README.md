# Motion samples

`synthetic-walk-gesture.bvh` is an original, mathematically authored demonstration motion created for this prototype. It is **not human motion capture** and is not from CMU, Eyes JAPAN, Mixamo, or another capture library. It may be used, modified, and redistributed with this project without a separate asset fee.

- Prepared humanoid hierarchy with 21 named joints and End Sites.
- 120 frames at 30 fps (4 seconds).
- Source coordinates: Y up, centimetres; import at **0.01 m/unit**.
- Forward root travel, alternating leg/arm movement, a head turn, and a right-hand greeting.

Use it to check BVH parsing, retargeting, playback, frame scrubbing, root motion, in-place playback, and camera planning. It is not evidence of realistic locomotion or production-quality retargeting.

The prototype's BVH importer maps common humanoid names (including normalized Mixamo and common CMU names) onto the prepared humanoid. It keeps target limb lengths, honors each source joint's declared rotation order, and interpolates rotations using quaternions. Unknown source joints and missing target joints are reported in the import summary. Fingers, facial motion, arbitrary skeleton editing, animal retargeting, Z-up conversion, and foot IK are not implemented in this first humanoid prototype.

Root translation is relative to the first source frame. Looping accumulates horizontal clip displacement; in-place playback removes horizontal root translation while retaining vertical movement and turning. A floor guard prevents the mannequin's feet from penetrating the stage; it does not lock planted feet. Set the source unit scale to match your BVH file (for example, 0.01 for centimetres and 0.0254 for inches).
