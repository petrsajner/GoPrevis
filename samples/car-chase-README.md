# Car chase demo

Choose **File → Open car chase demo**, or launch **Open car chase demo.cmd** beside GenPrevis.exe. The shot opens in Camera view at frame zero; press **Space** to play. You can also open **car-chase.genprevis** manually and enter **Camera view**. The file is a self-contained 12-second, 1920 × 1080 shot at **25 fps**, requiring no motion file or downloaded model.

- **0–1 s:** the blue car waits.
- **1–3 s:** it accelerates smoothly from rest.
- **About 3–8 s:** it drives at steady speed through a sweeping 90° turn.
- **About 8–10 s:** it slows to a complete stop.
- **The final two seconds:** both car and camera hold still.

**Car follow** is the active camera. Its live **Follow** and **Look at** settings keep a front three-quarter view of the vehicle, with no baked camera keys. Edit the car's route and the camera continues following automatically. Select the camera to change its target, distance, height, side offset and smoothness. The supplied composition uses zero smoothing for exact framing; increase it to try a gentler response. Choose **Wide overview** in the camera selector to see the road and complete route.

Select **Blue car · eased drive** and click its **Path** diamonds on the timeline to inspect arrival times. The first and final moving intervals use custom **Curved** Bezier timing; intermediate keys remain Linear, so the car does not stop at each waypoint. The car's front faces its path direction automatically.

The road, markings, pavilion, trees and vehicle are editable built-in objects. The menu/launcher opens an untitled working copy: **Ctrl+S** asks where to save your own version.
