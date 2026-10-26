using System.Text.Json;

namespace GoPrevis;

public static class AiProgramGuide
{
    public static string Json()=>JsonSerializer.Serialize(new{
        rules=new[]{
            "Character/car travel is an ActorPathData curve in world metres, with orientToPath=true. Mocap animates the body, not its route.",
            "For normal forward walking/running, use travelFacing=Forward, base rotation Y=0, no yaw correction keys, path enabled and in-place mocap. The runtime adds path tangent orientation after mocap blending.",
            "Use travelFacing=Backward/Left/Right only for an explicit artistic request, with the matching yaw correction. Manual and InPlace are explicit exceptions; do not use them to bypass a forward-walk request.",
            "For continuous constant-speed travel choose pathTiming=Constant speed. The program assigns arrival times by curve arc length. Authored timing is for pauses/acceleration; never ease to a full stop at every intermediate waypoint.",
            "Object position/rotation keys are additive corrections above the path. Scale keys are factors. They are not the normal route for walking characters or driving cars.",
            "For any orbit/circle/oblet, camera.intent=Orbit and camera.movement.mode=Orbit. Supply targetId, radius, startAngleDegrees, orbitDegrees, heights, start, duration and easing. Use Linear for constant angular velocity. Never approximate a circle with position/rotation keys.",
            "Orbit is evaluated analytically at every time, with a live aim toward its target. 360 degrees returns exactly to the starting composition. No transform keys are required.",
            "For dolly/truck/crane, camera.movement.mode=Path and intent=Dolly/Crane/Path; supply a smooth timed path and aimAtTarget. A two-point path is a linear dolly; more points define a continuous curve. Constant speed is preferred.",
            "For camera follow, disable procedural movement, enable Follow and Look at with the same target and suitable offsets. Follow and movement cannot both own position.",
            "Camera flight uses its own position path. movement.aimPath is a separate world-space target curve with its own times; this supports an initial stationary pan, rising street crossing and a final front-facing close-up. Use Smooth authored timing for continuous speed across timed camera waypoints, and independent Lens keys for focal length.",
            "Dogs on leads can use cylinder objects with Link endpoints: StartTargetId/person, StartBone/RightHand; EndTargetId/dog, EndBone from actual asset details (e.g. Head). Offsets are in endpoint bone space. The line follows both animated endpoints at every time.",
            "Large scenes should be staged in complete item batches with stage_scene_changes. Read get_staged_scene and inspect_staged_scene for the complete saved draft. Paid provider replies, tool progress, reviewed plans and usage are checkpointed; continue the same task rather than starting over.",
            "User floor-plan/elevation/reference images accompany the written brief. A Top view sketch uses X horizontally and -Z upwards; a Side view uses X horizontally and Y upwards. Respect explicit user notes for orientation/scale.",
            "Static cameras need a position and lookAtPoint or Look at binding. Avoid redundant rotation keys under live aiming. Inspect unfamiliar assets for real bounds and rig information.",
            "Camera path points are absolute world XYZ with absolute shot arrival times. Orbit center is the target's sampled world position plus targetOffset; heights are above this aim point. Positive angle rotates +Z toward +X.",
            "Walking clips should end or blend to Idle when travel stops. With blendIn>0 clips must overlap. Inspect the resulting plan before committing it.",
            "Workflow: read scene/guide and search assets, inspect_scene_plan with a complete proposal, fix reported errors, inspect the actual camera/gait images, assess_preview with concrete observations, then apply_scene_plan with the accepted planId. Invalid, unreviewed or visually unassessed proposals cannot be committed."
        },examples=new{
            forwardWalk=new{pathTiming="Constant speed",orientToPath=true,travelFacing="Forward",rotation=new[]{0,0,0},inPlace=true,positionKeys=0},
            fullOrbit=new{intent="Orbit",mode="Orbit",radius=7,orbitDegrees=360,startAngleDegrees=180,startHeight=1.5,endHeight=1.5,easing="Linear",positionKeys=0,rotationKeys=0},
            dolly=new{intent="Dolly",mode="Path",pathTiming="Constant speed",aimAtTarget=true,positionKeys=0}
        }},AiDirectorClient.Json);
}
