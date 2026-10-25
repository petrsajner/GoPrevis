using Godot;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;

namespace GoPrevis;

public sealed record AiPlanIssue(string Code,string Target,string Message,string Fix);
public sealed class AiPlanReview
{
    public bool Valid {get;set;}
    public List<AiPlanIssue> Errors {get;}=new();
    public List<AiPlanIssue> Warnings {get;}=new();
    public List<string> Operations {get;}=new();
    public List<object> Measurements {get;}=new();
}
public partial class GoPrevisApp
{
    sealed record AiGeometry(Aabb Bounds,string[] Bones,bool Animated);
    readonly Dictionary<string,AiGeometry> _aiGeometry=new();
    AiGeometry AiAssetGeometry(string id)
    {
        if(_aiGeometry.TryGetValue(id,out var cached))return cached;
        var model=ContentCatalog.Find(id)??throw new ArgumentException("Unknown model ID: "+id);
        var stage=new SubViewport{OwnWorld3D=true,RenderTargetUpdateMode=SubViewport.UpdateMode.Disabled};AddChild(stage);
        var root=new Node3D();stage.AddChild(root);
        try
        {
            Aabb bounds;string[] bones;
            if(model.BuiltinKind=="actor")
            {var actor=new RiggedActor();root.AddChild(actor);bounds=actor.GetPreparedPoseBounds();bones=actor.PoseBoneNames.ToArray();}
            else if(model.IsBuiltin)
            {var prop=PropFactory.Create(model.BuiltinKind);root.AddChild(prop);bounds=PropFactory.CalculateBounds(root);bones=Array.Empty<string>();}
            else
            {
                var document=new GltfDocument();var state=new GltfState();var error=document.AppendFromFile(ContentCatalog.MaterializeSource(model),state);if(error!=Godot.Error.Ok)throw new InvalidOperationException("Could not inspect model geometry: "+error);
                var node=document.GenerateScene(state) as Node3D??throw new InvalidOperationException("Model has no 3D root.");root.AddChild(node);node.Scale*=model.Scale;
                var initial=PropFactory.CalculateBounds(root);node.Position-=Vector3.Up*initial.Position.Y;bounds=PropFactory.CalculateBounds(root);
                bones=Descendants(node).OfType<Skeleton3D>().SelectMany(s=>Enumerable.Range(0,s.GetBoneCount()).Select(s.GetBoneName)).Where(name=>!name.StartsWith("PoleTarget",StringComparison.Ordinal)).ToArray();
                foreach(var player in Descendants(node).OfType<AnimationPlayer>())player.Stop();
            }
            return _aiGeometry[id]=new(bounds,bones,model.Animated);
        }
        finally{RemoveChild(stage);stage.QueueFree();}
    }
    string AiAssetDetails(string id)
    {
        if(IsSceneLightKind(id))return JsonSerializer.Serialize(new{id,kind="light",travel="position/rotation or a path",rigged=false},AiDirectorClient.Json);
        var model=ContentCatalog.Find(id)??throw new ArgumentException("Unknown asset ID: "+id);var geometry=AiAssetGeometry(id);
        return JsonSerializer.Serialize(new{id,name=model.Name,family=model.ActorFamily,animated=geometry.Animated,defaultScale=model.BodyScale,
            localBoundsAtObjectScaleOne=new{minimum=Array3(geometry.Bounds.Position),size=Array3(geometry.Bounds.Size)},bones=geometry.Bones,
            compatibleMotionCount=MotionCatalog.ForFamily(model.ActorFamily).Count(),placement="Place on ground/on object fits actual support; static sitting pose plus placeOnId seats a human on a chair.",forward="-Z"},AiDirectorClient.Json);
    }
    static bool AiLocomotion(string name)=>System.Text.RegularExpressions.Regex.IsMatch(name,@"walk|running|\brun\b|gallop|trot|jog|crawl",System.Text.RegularExpressions.RegexOptions.IgnoreCase);
    static Vector3 AiObjectPosition(ObjectData data,double time)
    {
        var result=TransformLayerRuntime.Evaluate(Vector(data.Position),Vector(data.Rotation),Vector(data.Scale,Vector3.One),data.TransformAnimation,data.Path,time,Vector3.Forward);
        return result.Position+(data.Path.Enabled?Vector(data.PathOffset):Vector3.Zero);
    }
    AiPlanReview ReviewAiPlan(AiScenePlan plan,bool replace)
    {
        var document=CompileAiPlan(plan,replace);var review=new AiPlanReview();
        void Error(string code,string target,string message,string fix)=>review.Errors.Add(new(code,target,message,fix));
        foreach(var item in plan.Objects)
        {
            var data=document.Objects.Single(o=>o.Id==item.Id);var times=Enumerable.Range(0,81).Select(i=>document.Duration*i/80).ToArray();var positions=times.Select(t=>AiObjectPosition(data,t)).ToArray();
            float distance=Enumerable.Range(1,positions.Length-1).Sum(i=>positions[i].DistanceTo(positions[i-1]));
            bool locomotion=data.MotionClips.Any(c=>AiLocomotion(c.Name)),traveler=data.ActorFamily!=""||data.Kind=="car";
            if(item.TravelFacing is not("Forward" or "Backward" or "Left" or "Right" or "Fixed" or "InPlace" or "Manual"))throw new ArgumentException("Travel facing must be Forward, Backward, Left, Right, Fixed, InPlace or Manual.");
            if(locomotion&&!data.Path.Enabled&&item.TravelFacing is not("Manual" or "InPlace"))
                Error("locomotion_requires_route",item.Id,"A walking/running clip has no world-space route.","Create a timed path with orientToPath=true and in-place mocap. Set InPlace only when the user explicitly requests walking on the spot.");
            if(traveler&&distance>.15f&&item.TravelFacing is not("Manual" or "InPlace"))
            {
                if(!data.Path.Enabled||data.Path.Points.Count<2)
                    Error("travel_requires_path",item.Id,"A traveling character/vehicle is being moved by transform keys.","Use replacePath=true, a timed path and orientToPath=true. Remove route position/rotation keys with replaceKeys=true; keep skeletal mocap separate.");
                else
                {
                    if(!data.Path.OrientToPath&&item.TravelFacing!="Fixed")Error("path_facing_disabled",item.Id,"The route is enabled but tangent orientation is disabled.","Enable orientToPath. For normal forward motion use rotation [0,0,0] and no manual yaw correction.");
                    double expected=item.TravelFacing=="Backward"?180:item.TravelFacing=="Left"?-90:item.TravelFacing=="Right"?90:0;
                    double worst=times.Max(t=>Math.Abs(Mathf.Wrap(Vector(data.Rotation).Y+TransformLayerRuntime.EvaluateVector(data.TransformAnimation.Rotation,t,Vector3.Zero,true).Y-(float)expected,-180,180)));
                    if(data.Path.OrientToPath&&item.TravelFacing!="Fixed"&&worst>30)Error("travel_heading_mismatch",item.Id,"Base/manual yaw turns the actor away from the travel-facing intent (up to "+worst.ToString("0")+" degrees).","Use zero base/manual yaw for Forward. Put head turns in poseKeys. Use explicit Backward/Left/Right only when the user requests it.");
                    if(locomotion&&data.MotionClips.Any(c=>AiLocomotion(c.Name)&&!c.InPlace))Error("double_root_translation",item.Id,"Locomotion and path translation are both active.","Set inPlace=true on the walking/running clips so the path owns world travel.");
                    if(data.TransformAnimation.Position.Count>1&&positions[0].DistanceTo(positions[^1])>.15f&&item.Keys.Count(k=>k.Channel=="Position")>1)
                        review.Warnings.Add(new("route_plus_correction_keys",item.Id,"Position keys also modify this route.","Keep them only for intentional small offsets, not a duplicate travel trajectory."));
                    review.Operations.Add(data.Name+": path travel · "+item.TravelFacing+" · tangent orientation "+(data.Path.OrientToPath?"On":"Off"));
                }
            }
            if(data.Path.Enabled&&data.Path.Points.Count>2&&data.Path.PointInterpolation.Skip(1).Take(Math.Max(0,data.Path.PointInterpolation.Count-2)).Any(k=>k.Mode is "Ease in/out" or "Stop"))
                review.Warnings.Add(new("intermediate_speed_stops",item.Id,"Easing at intermediate route points can cause repeated speed changes.","For continuous travel use pathTiming=Constant speed; keep Authored timing only for intentional acceleration/stops."));
            review.Measurements.Add(new{target=item.Id,travelDistanceMetres=distance,path=data.Path.Enabled,orientToPath=data.Path.OrientToPath,mocapClips=data.MotionClips.Count});
        }
        foreach(var item in plan.Cameras)
        {
            var camera=document.Cameras.Single(c=>c.Id==item.Id);var movement=camera.Movement;
            if(item.Intent is not("Auto" or "Static" or "Follow" or "Orbit" or "Dolly" or "Crane" or "Path" or "Manual"))throw new ArgumentException("Unknown camera intent.");
            if(item.Intent=="Orbit"&&movement.Mode!="Orbit")Error("orbit_requires_native_motion",item.Id,"An orbit is being approximated without native orbit motion.","Set movement.mode=Orbit, targetId, radius, orbitDegrees and duration; replaceKeys=true, keys=[], follow=false. Use aimAtTarget=true.");
            if(movement.Mode!="None"&&camera.TransformAnimation.Position.Count>0)Error("camera_position_ownership",item.Id,"Both procedural motion and absolute position keys are present.","For a clean native move use replaceKeys=true and keys=[]. Procedural Orbit/Path controls position.");
            if(movement.Mode!="None"&&movement.AimAtTarget&&camera.TransformAnimation.Rotation.Count>0)Error("camera_aim_ownership",item.Id,"Live camera aim is combined with absolute rotation keys.","Remove rotation keys; the native aim continuously tracks the target.");
            if(movement.Mode!="None"&&movement.AimAtTarget&&camera.Tracking.LookAtEnabled&&camera.Tracking.LookAtTargetId!=movement.TargetId)Error("conflicting_camera_aim_targets",item.Id,"The movement and Look at constraint aim at different subjects.","Use aimAtTarget with the movement target, or disable it when intentionally aiming at a separate Look at target.");
            if(item.Intent is "Dolly" or "Crane" or "Path"&&movement.Mode!="Path")Error("camera_travel_requires_path",item.Id,"A continuous camera move is using sparse transform keys.","Use camera movement.mode=Path with timed positions and aimAtTarget. Prefer Constant speed.");
            if(movement.Mode=="Orbit")
            {
                if(movement.Easing=="Hold")Error("orbit_hold",item.Id,"Hold easing freezes the orbit then jumps at its end.","Use Linear for continuous angular velocity, or Ease in/out for a deliberate start/stop.");
                review.Operations.Add(camera.Name+": analytic orbit · "+movement.OrbitDegrees.ToString("0.#")+"° · radius "+movement.Radius.ToString("0.#")+" m · "+movement.Easing+" · zero sampled transform keys");
                var target=document.Objects.FirstOrDefault(o=>o.Id==movement.TargetId);
                if(target!=null)
                {
                    Aabb bounds;
                    try{bounds=AiAssetGeometry(target.LibraryModelId??target.Kind).Bounds;}catch(ArgumentException){bounds=new Aabb(new Vector3(-.5f,0,-.5f),new Vector3(1,1.8f,1));}
                    float safeRadius=new Vector2(bounds.Size.X*Math.Abs(target.Scale[0]),bounds.Size.Z*Math.Abs(target.Scale[2])).Length()*.5f+.25f;
                    if(movement.Radius<safeRadius)Error("orbit_inside_subject",item.Id,"The orbit radius intersects the target's bounds.","Increase radius beyond "+safeRadius.ToString("0.##")+" metres.");
                }
                review.Measurements.Add(new{target=item.Id,mode="Orbit",angularSpeedDegreesPerSecond=movement.OrbitDegrees/movement.Duration,sampledPositionKeys=camera.TransformAnimation.Position.Count});
            }
            else if(movement.Mode=="Path")review.Operations.Add(camera.Name+": continuous camera path · "+movement.Path.Points.Count+" points · live aim "+movement.AimAtTarget);
            else if(camera.Tracking.FollowEnabled)review.Operations.Add(camera.Name+": live Follow / Look at · "+camera.Tracking.FollowTargetId);
            else if(item.Keys.Count(k=>k.Channel=="Position")>=3&&item.Intent!="Manual")
            {
                Error("sparse_camera_curve",item.Id,"Several position keys approximate a curved camera move.","Use native Orbit for a circle, or native Path for a continuous curve. Use Intent=Manual only for explicitly requested hand-keyed motion.");
            }
        }
        review.Valid=review.Errors.Count==0;return review;
    }
}
