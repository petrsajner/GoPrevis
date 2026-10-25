# Static human blocking poses

The pose library in `assets/content/manifests/human-poses.json` is authored for
GoPrevis' original prepared 21-bone mannequin. It contains fixed local joint
rotations and pelvis positions, not motion-capture files or animation clips.
No third-party pose photographs, scans, animation files or reference-library
data were copied to create it.

The reproducible authoring script is `tools/content/generate_human_poses.py`.
It uses fixed-length two-bone limb geometry and explicitly authored targets for
standing, chair/floor sitting, kneeling, crouching, lying, gestures, action and
interaction setups. JSON stores normalized XYZW local quaternions. The project
uses these as original project assets.

Poses are scene-blocking starting points, not anatomical or ergonomic guidance.
Hands have no articulated fingers. Chair poses target an approximately 0.45 m
seat for the average adult mannequin; model proportions and object geometry
can require placement adjustments. Contact, collision, cloth and prop-grasp
solving are not supplied by this static catalog.
