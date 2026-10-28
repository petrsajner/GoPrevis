# GoPrevis 0.21.0 Alpha - Animation editor update

This is an alpha development build. Extract the complete archive and launch **GoPrevis.exe**. Keep the executable, PCK, runtime directory and tools together. Godot and the .NET SDK do not need to be installed.

## Try the animation system

Open **examples/Animated properties.goprevis**, press Space, and select **Property box**. In the animation workspace, expand Transform / Position and select X or Y. Switch between **Dope sheet** and **Graph**, then choose **Value graph** or **Speed graph**. The axes have independent key times; rotation keeps full authored turns.

The example also contains a walking figure, an animated light, material color, camera lens and scene brightness. Edit source playback rate, object paths and additive body corrections using their own channels. The supplied car chase and street shots remain available as additional examples.

Read **docs/ANIMATION-EDITOR.md** for controls, keyframes, curve handles, filtering, copy/paste and source-time animation. The earlier complete English PDF manual is also included for the broader scene-building workflow.

## Included animation tools

- Independent XYZ transform and joint-correction channels.
- Expandable property hierarchy; animated/selected/search/pinned filters.
- Value and signed-speed graphs with weighted Bezier handles and numeric Time/Value/Speed editing.
- Multi-key selection, retiming, Delete, Undo/Redo, copy/paste and duplicate.
- Keyable lights, colors, visibility, camera and environment settings, paths and motion playback controls.
- Integrated mocap source time, additive body corrections and synchronized color/depth/pose exports.

Projects saved by this build use schema 14. Existing projects load; older application versions cannot open newly saved schema-14 projects. Use Save project as to keep a separate copy when needed.

All manual scene, animation, camera and export tools work without an AI key. AI Director and the optional Vividia connection have their own settings. No paid AI request is made by opening or playing an example.

GoPrevis remains focused on previs. General mesh modeling, arbitrary rig creation, shape keys, expressions/drivers, simulation baking and a general NLA/constraint editor are outside this update.

Windows x64 build. English interface. Copyright Petr Sajner 2026. See THIRD-PARTY-NOTICES.md and LICENSES for the bundled engine, runtime and content notices.
