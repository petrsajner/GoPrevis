# Animation editor - GoPrevis 0.21.0

This version adds a shared property-animation system, independent axes, and editable value and signed-speed graphs. The UI is English. Existing projects load with their previous animation; new saves use project schema 14 and need GoPrevis 0.21.0 or newer.

## First animation

1. Select an object and move the playhead to the starting frame.
2. Click the diamond next to Position X to key that axis, or the Position group diamond to key XYZ together. **Key transform / K** captures the transform groups.
3. Move to another time and change X. An already animated property writes a new key at the playhead. **Auto key** also starts keys for newly edited scene properties.
4. Open **Dope sheet** below the viewport. Expand the object, **Transform**, then **Position**. X, Y and Z have independent keys and times.
5. Select a property and switch to **Graph**. Choose **Value graph** or **Speed graph**.

On a path-driven object, the transform channels remain the manual correction layer above the path. Pose corrections remain above the blended source motion. Camera Follow / Look at and native movement expose animatable offsets and parameters; their Position/Rotation key buttons key the live controller offsets.

## One animation workspace

- **Dope sheet** shows the parameter hierarchy and its key diamonds.
- **Graph** shares that hierarchy and displays the selected property curves.
- **Clips** keeps source-motion sequencing, IN/OUT trimming, overlaps, paths and camera movement spans available.
- **Animated only** shows keyed properties; **Selected only** follows the selected scene object. A property's star pins it, and **Pinned** filters to those channels.
- Search narrows the property list. **Expand** offers all properties, animated properties, or collapse all. Individual object and group arrows work independently.
- Drag the horizontal divider to give the animation workspace more room. Its toolbar adapts to the window width.

Click a property row to select it; Ctrl/Shift extends selection. A group row can select its descendants. Double-click a dope-sheet row to add a key at that frame. Drag keys to retime; drag a rectangle from empty key space to select several keys. Delete removes selected animation. Undo/Redo restores the operation.

## Graph editing

Select one or more property rows, then choose Graph. XYZ curves are independent. Position uses metres, rotation uses degrees, and scalar properties retain their own units. The speed view is the signed derivative of the selected value, in units per second. For a playback-rate property, its value is the rate and its derivative is the change of that rate.

- Drag a value-graph key to change time and value. Double-click empty graph space to add a key.
- Drag the incoming/outgoing tangent handles to adjust influence and slope. In the speed graph, handles change the underlying key velocities.
- Use the **Time**, **Value** and **Speed** fields for exact selected-key edits.
- The interpolation menu and key context menu offer Linear, Bezier, Ease in/out, Stop, Hold, Auto and Flat. Tangents can be broken or unified.
- Wheel zooms, middle-button drag pans, and **Fit / F** fits the curves.
- Ctrl+C / Ctrl+V copies and pastes selected keys at the playhead. Ctrl+D duplicates selected animation when the animation editor has focus. The graph and dope sheet share the clipboard.

Linear keeps a constant interval rate. Bezier allows independent weighted handles and overshoot within the property's valid range. Ease in/out accelerates and settles. Stop settles at the interval ends. Hold keeps a value until the next key. Auto computes smooth tangents; Flat zeroes the selected tangents. Rotation channels retain authored turns: 0 to 720 degrees is two turns.

## Other scene properties

Expand **Animate parameters** in Properties, or browse the object's property hierarchy below. Each property has a key action. The system covers:

- Position, Rotation and Scale XYZ on objects and cameras, plus final camera correction axes.
- Prepared human/animal joint correction XYZ and pelvis/root offset XYZ, layered over source animation.
- Object visibility, labels and sketch-color RGB.
- Light intensity, color, range, enabled state and shadow switch.
- Camera focal length, Follow / Look at offsets and smoothness, native movement parameters, camera path/aim point coordinates, and camera-reference display values.
- Scene background, brightness, ground height/opacity/visibility, labels and studio light switch.
- Path settings, control-point coordinates and **Path progress**.
- Motion playback rate, source offset, blend-in, loop and in-place flags.

Discrete values use held keys. Parameters have valid ranges, such as opacity 0-1. Project image dimensions/FPS, file paths, asset/rig topology, names and clip IN/OUT scheduling remain document/editing operations rather than value curves.

## Paths and motion time

**Path progress** is a normalized 0-1 curve driving travel along the route. A key of 0 is the beginning and 1 the end. Its easing changes travel speed. When progress keys are absent, the authored waypoint arrivals control travel. Travel-duration changes rescale arrival timing. Path geometry and final manual corrections remain separate.

Animated mocap playback rate is integrated over time. It does not multiply absolute time by the instantaneous rate, so easing the rate avoids source-time jumps. A keyed rate of zero freezes the motion exactly. Source offset also provides a time-remapping adjustment. The native animal and human BVH runtimes use the same clock as tracking cameras. Clip start/end/source scale remain editorial settings.

Entering Pose preserves the source clips. Existing IK edits update split joint channels when present; pose Reset edits restores the session's correction baseline. Applying a static pose clears the figure's body-correction animation as before.

## Persistence and export

Curves are serialized separately from base values. Scrubbing, playback and export evaluate the curves without baking sampled values into the saved document. Undo and Save As include the channels. Existing grouped XYZ and quaternion corrections remain usable; editing an axis materializes its own curve while untouched axes retain their prior source.

Color, depth and pose export evaluate the same absolute time. The shot manifest includes property-animation data for scene objects, motion clips, cameras and scene settings. Bounds caching and visible-object tracking account for animated properties. The existing Vividia handoff and manual export workflow remain available.

## Scope

This is an animation system for the scene properties and prepared rigs exposed by GoPrevis. It adds the property, curve and timeline workflows requested here. Mesh modeling, creating arbitrary rigs, shape keys, expressions/drivers, simulation baking and a general NLA/constraint editor are separate DCC capabilities and are not introduced by this version. It should not be described as feature-equivalent to all of Blender or Maya.

Runtime and UI checks cover independent axes, multi-turn rotation, graph and dope-sheet input, animation above mocap and paths, integrated source time, original-inspector edits, Undo, save/reopen, legacy projects, responsive layouts and real color/depth/pose exports. Exact results for the delivered build are recorded in VALIDATION.md.
