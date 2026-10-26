using Godot;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;

namespace GoPrevis;

public partial class GoPrevisApp
{
    static T AiClone<T>(T value)=>JsonSerializer.Deserialize<T>(JsonSerializer.Serialize(value))!;
    static readonly string[] AiHumanBones={"Hips","Spine","Chest","Neck","Head","LeftShoulder","LeftUpperArm","LeftLowerArm","LeftHand","RightShoulder","RightUpperArm","RightLowerArm","RightHand","LeftUpperLeg","LeftLowerLeg","LeftFoot","LeftToe","RightUpperLeg","RightLowerLeg","RightFoot","RightToe"};
    string AiSceneContext(bool replace)
    {
        var context=new{
            mode=replace?"new scene; replace the current shot reversibly":"edit current scene; preserve unrelated objects and cameras",
            duration=_duration,fps=_fps,width=ProjectResolution.X,height=ProjectResolution.Y,activeCameraId=replace?"":ActiveCameraId,
            coordinates="metres; Y up; forward -Z; Euler degrees; path points world-space",
            commonModels=new[]{"human-average","human-child","car","table","chair","box","wall","tree"},
            humanBones=AiHumanBones,
            animalModels=ContentCatalog.ForCategory("Animals").Select(m=>new{id=m.Id,name=m.Name,family=m.ActorFamily}),
            objects=(replace?Enumerable.Empty<Entry>():_entries).Select(e=>new{id=e.Data.Id,name=e.Data.Name,assetId=e.Data.LibraryModelId??e.Data.Kind,family=e.Data.ActorFamily,
                position=e.Data.Position,rotation=e.Data.Rotation,scale=e.Data.Scale,
                worldBounds=new{min=Array3(PlacementWorldBounds(e).Position),size=Array3(PlacementWorldBounds(e).Size)},
                path=e.Data.Path,pathOffset=e.Data.PathOffset,keys=e.Data.TransformAnimation,poseKeys=e.Data.PoseCorrections,
                motions=e.Data.MotionClips.Select(c=>new{name=c.Name,start=c.Start,duration=c.Duration,speed=c.Speed,loop=c.Loop,blendIn=c.BlendIn}),
                bones=e.NativeTrack?.PoseBoneNames??(e.Node is RiggedActor?AiHumanBones:Array.Empty<string>())}),
            cameras=(replace?Enumerable.Empty<CameraEntry>():_cameras).Select(c=>new{id=c.Id,name=c.Name,position=Array3(c.BasePosition),rotation=Array3(c.BaseRotation),focalLength=c.BaseLens,tracking=c.Tracking,movement=c.Movement,keys=c.Animation}),
            capabilities=new{travel="paths with tangent orientation; transform keys are correction layers",camera="exact parametric Orbit, smooth Path, live Follow/Look at",workflow="get_program_guide, get_asset_details, inspect_scene_plan, then apply_scene_plan by reviewed planId"}};
        string json=JsonSerializer.Serialize(context,AiDirectorClient.Json);return json;
    }
    Task<string> AiReadTool(string name,JsonObject args)
    {
        if(name=="get_scene")return Task.FromResult(AiSceneContext(_aiReplaceScene));
        if(name=="get_program_guide")return Task.FromResult(AiProgramGuide.Json());
        if(name=="get_asset_details")return Task.FromResult(AiAssetDetails(args["assetId"]?.GetValue<string>()??""));
        if(name=="inspect_scene_plan")return Task.FromResult(JsonSerializer.Serialize(ReviewAiPlan(args.Deserialize<AiScenePlan>(AiDirectorClient.Json)??throw new ArgumentException("A plan is required."),_aiReplaceScene),AiDirectorClient.Json));
        string kind=args["kind"]?.GetValue<string>()??"models",query=args["query"]?.GetValue<string>()??"",family=args["family"]?.GetValue<string>()??"";
        var words=Regex.Split(query.ToLowerInvariant(),@"[^\p{L}\p{N}]+").Where(w=>w.Length>1).Take(12).SelectMany(word=>word switch{"dog" or "dogs"=>new[]{"dog","husky","shiba"},"run" or "running"=>new[]{"run","gallop"},"walk" or "walking"=>new[]{"walk","walking"},"person" or "people" or "character"=>new[]{"human","adult","person"},"sit" or "seated"=>new[]{"sit","sitting","seated"},_=>new[]{word}}).Distinct().ToArray();
        int Rank(string text)=>words.Length==0?1:words.Count(w=>text.Contains(w,StringComparison.OrdinalIgnoreCase));
        object[] items;
        if(kind=="motions")items=MotionCatalog.All.Where(m=>family==""||AiFamilyMatches(m.ActorFamily,family)).Select(m=>new{item=m,score=Rank(m.Name+" "+m.Category+" "+m.Description+" "+string.Join(' ',m.Tags))}).Where(x=>x.score>0).OrderByDescending(x=>x.score).Take(35).Select(x=>(object)new{id=x.item.Id,name=x.item.Name,family=x.item.ActorFamily,duration=x.item.Duration,loop=x.item.Loop,category=x.item.Category,description=x.item.Description}).ToArray();
        else if(kind=="poses")items=AllLibraryPoses.Where(p=>family==""||family=="human"&&p.Pose.RigModelId==null||p.Pose.RigModelId!=null&&AiFamilyMatches(ContentCatalog.Find(p.Pose.RigModelId)?.ActorFamily??"",family)).Select(p=>new{item=p,score=Rank(p.Name+" "+p.Category+" "+p.Description+" "+string.Join(' ',p.Tags))}).Where(x=>x.score>0).OrderByDescending(x=>x.score).Take(25).Select(x=>(object)new{id=x.item.Id,name=x.item.Name,modelId=x.item.Pose.RigModelId??"human",description=x.item.Description}).ToArray();
        else if(kind=="models")items=ContentCatalog.All.Where(m=>family==""||AiFamilyMatches(m.ActorFamily,family)).Select(m=>new{item=m,score=Rank(m.Name+" "+m.Category+" "+m.ActorFamily)}).Where(x=>x.score>0).OrderByDescending(x=>x.score).ThenByDescending(x=>x.item.Animated).Take(25).Select(x=>(object)new{id=x.item.Id,name=x.item.Name,category=x.item.Category,family=x.item.ActorFamily,animated=x.item.Animated,defaultScale=x.item.BodyScale}).Concat(query.Contains("light",StringComparison.OrdinalIgnoreCase)?new object[]{new{id="point_light",name="Point light",family=""},new{id="directional_light",name="Directional light",family=""}}:Array.Empty<object>()).ToArray();
        else throw new ArgumentException("Library kind must be models, motions or poses.");
        return Task.FromResult(JsonSerializer.Serialize(new{kind,query,family,items,note="Only use returned IDs. Broaden the English query if no results match."},AiDirectorClient.Json));
    }
    static void AiVector(float[]? value,string name,bool optional=true,bool scale=false)
    {
        if(optional&&value is {Length:0})return;
        if(value is not{Length:3}||value.Any(x=>!float.IsFinite(x)||Math.Abs(x)>1000)||scale&&value.Any(x=>x<.05f))throw new ArgumentException(name+" needs three finite XYZ values"+(scale?" between 0.05 and 1000.":" within 1000 metres/degrees."));
    }
    static bool AiFamilyMatches(string actual,string requested)
    {
        requested=requested.Trim().ToLowerInvariant();
        if(requested is "dog" or "dogs" or "canine")return actual.EndsWith("-husky",StringComparison.Ordinal)||actual.EndsWith("-shiba-inu",StringComparison.Ordinal);
        if(requested is "people" or "person")requested="human";
        return actual.Equals(requested,StringComparison.OrdinalIgnoreCase)||actual.EndsWith("-"+requested,StringComparison.OrdinalIgnoreCase)||ContentCatalog.Find(requested)?.ActorFamily==actual;
    }
    static void AiTime(double time,double duration,string name)
    {if(!double.IsFinite(time)||time<0||time>duration+1e-7)throw new ArgumentException(name+" must be within the shot's duration.");}
    static void AiId(string id)
    {if(!Regex.IsMatch(id??"",@"^[A-Za-z0-9][A-Za-z0-9_.:\-]{0,95}$"))throw new ArgumentException("Use a non-empty ID of up to 96 letters, digits, underscores, hyphens, colons or dots.");}
    static KeyInterpolationData AiEasing(string name)
    {if(!KeyInterpolation.Modes.Contains(name))throw new ArgumentException("Easing must be Linear, Ease in/out, Curved, Stop or Hold.");return new(){Mode=name};}
    static void AiPathTiming(ActorPathData path,string mode)
    {
        if(mode is not("Authored" or "Constant speed" or "Smooth authored"))throw new ArgumentException("Path timing must be Authored, Smooth authored or Constant speed.");
        if(path.PointTimes.Count<2)return;
        if(mode=="Smooth authored")
        {
            var f=ActorPathRuntime.WaypointFractions(path);int n=f.Length;var slopes=new double[n];var speeds=new double[n-1];
            for(int i=0;i<n-1;i++){double dt=path.PointTimes[i+1]-path.PointTimes[i];if(dt<=0)throw new ArgumentException("Smooth timing needs increasing arrival times.");speeds[i]=(f[i+1]-f[i])/dt;}
            for(int i=1;i<n-1;i++)if(speeds[i-1]>0&&speeds[i]>0)slopes[i]=Math.Min(2*speeds[i-1]*speeds[i]/(speeds[i-1]+speeds[i]),3*Math.Min(speeds[i-1],speeds[i]));
            path.PointInterpolation.Clear();
            for(int i=0;i<n;i++)
            {
                if(i==n-1||f[i+1]-f[i]<1e-9){path.PointInterpolation.Add(new(){Mode="Hold"});continue;}
                double scale=(path.PointTimes[i+1]-path.PointTimes[i])/(f[i+1]-f[i]);
                path.PointInterpolation.Add(new(){Mode="Curved",Control1=new[]{1f/3,(float)(slopes[i]*scale/3)},Control2=new[]{2f/3,(float)(1-slopes[i+1]*scale/3)}});
            }
            return;
        }
        if(mode!="Constant speed")return;
        double start=path.PointTimes[0],duration=path.PointTimes[^1]-start;var fractions=ActorPathRuntime.WaypointFractions(path);
        path.PointTimes=fractions.Select(f=>start+duration*f).ToList();path.PointInterpolation=fractions.Select(_=>new KeyInterpolationData()).ToList();
    }
    static void AiKeys(TransformAnimationData target,IEnumerable<AiKey> keys,double duration,bool camera,List<LensKeyData>? lenses=null)
    {
        foreach(var key in keys)
        {
            AiTime(key.Time,duration,"Key time");
            if(camera&&key.Channel=="Lens")
            {
                if(key.Value is not{Length:1}||!float.IsFinite(key.Value[0])||key.Value[0]<8||key.Value[0]>300)throw new ArgumentException("Lens keys need one value between 8 and 300 mm.");
                lenses!.RemoveAll(k=>Math.Abs(k.Time-key.Time)<.000001);lenses.Add(new(){Time=key.Time,Value=key.Value[0],Interpolation=AiEasing(key.Easing)});continue;
            }
            if(key.Channel is not("Position" or "Rotation" or "Scale"))throw new ArgumentException("Key channels are Position, Rotation, Scale, and camera Lens.");
            AiVector(key.Value,"Key "+key.Channel,false,key.Channel=="Scale");var created=TransformLayerRuntime.Upsert(TransformLayerRuntime.Channel(target,key.Channel),key.Time,Vector(key.Value));created.Interpolation=AiEasing(key.Easing);
        }
    }
    ProjectData CompileAiPlan(AiScenePlan plan,bool replace)
    {
        if(plan.Objects==null||plan.Cameras==null||plan.RemoveIds==null||plan.Objects.Count>500||plan.Cameras.Count>100||plan.RemoveIds.Count>500)throw new ArgumentException("A plan supports up to 500 object changes, 100 cameras and 500 removals.");
        if(plan.Objects.Count+plan.Cameras.Count+plan.RemoveIds.Count==0)throw new ArgumentException("The plan must modify at least one scene item.");
        if(plan.Summary==null||plan.Summary.Length>3000)throw new ArgumentException("Keep the scene summary within 3000 characters.");
        var document=AiClone(Snapshot());
        if(replace){document.Objects.Clear();document.Cameras.Clear();document.CameraKeys.Clear();document.DirectionalLightEnabled=false;document.SceneSettings=new();}
        if(plan.Duration!=0){if(!double.IsFinite(plan.Duration)||plan.Duration<1||plan.Duration>MaxSceneDuration)throw new ArgumentException("Shot duration must be 1–600 seconds, or zero to keep it.");document.Duration=plan.Duration;}
        if(plan.Fps!=0){if(plan.Fps<1||plan.Fps>120)throw new ArgumentException("FPS must be 1–120, or zero to keep it.");document.Fps=plan.Fps;}
        if(plan.Width!=0||plan.Height!=0){document.Setup.Width=plan.Width;document.Setup.Height=plan.Height;document.Setup=document.Setup.ValidatedCopy();}
        if(plan.SceneSettings!=null){var settings=AiClone(plan.SceneSettings);if(settings.BackgroundColor is{Length:3})settings.BackgroundColor=settings.BackgroundColor.Concat(new[]{1f}).ToArray();document.SceneSettings=settings.ValidatedCopy();}
        foreach(string id in plan.RemoveIds.Where(_=>!replace)){if(!document.Objects.Any(o=>o.Id==id)&&!document.Cameras.Any(c=>c.Id==id))throw new ArgumentException("Cannot remove unknown item: "+id);document.Objects.RemoveAll(o=>o.Id==id);document.Cameras.RemoveAll(c=>c.Id==id);}
        var changedIds=new HashSet<string>(StringComparer.Ordinal);
        foreach(var item in plan.Objects)
        {
            AiId(item.Id);if(!changedIds.Add(item.Id)||document.Cameras.Any(c=>c.Id==item.Id))throw new ArgumentException("Duplicate object/camera ID: "+item.Id);
            if(item.Name==null||item.Name.Length>120)throw new ArgumentException("Object names must have up to 120 characters.");
            var data=document.Objects.FirstOrDefault(o=>o.Id==item.Id);
            if(data==null)
            {
                var model=ContentCatalog.Find(item.AssetId);
                if(model==null&&!IsSceneLightKind(item.AssetId))throw new ArgumentException("Unknown model ID: "+item.AssetId+". Search the library first.");
                data=new ObjectData{Id=item.Id,Name=item.Name==""?model?.Name??SceneLightName(item.AssetId):item.Name,LibraryModelId=model?.Id,ActorFamily=model?.ActorFamily??""};
                if(IsSceneLightKind(item.AssetId)){data.Kind=item.AssetId;data.Light=new();data.Position=new[]{0f,3f,0f};if(item.AssetId=="directional_light")data.Rotation=new[]{-48f,-28f,0f};}
                else if(model!.IsBuiltin){data.Kind=model.BuiltinKind;data.Scale=model.BodyScale.ToArray();}
                else{data.Kind="imported";data.ModelPath=ContentCatalog.MaterializeSource(model);data.ModelUnitScale=model.Scale;data.ModelGrounded=true;data.ModelAnimation=model.DefaultAnimation;}
                document.Objects.Add(data);
            }
            else if(item.AssetId!=""&&item.AssetId!=(data.LibraryModelId??data.Kind))throw new ArgumentException("An existing object's model cannot be changed in place. Remove it and create a new ID.");
            if(item.Name!="")data.Name=item.Name;
            AiVector(item.Position,"Object position");AiVector(item.Rotation,"Object rotation");AiVector(item.Scale,"Object scale",scale:true);
            if(item.Position.Length==3)data.Position=item.Position.ToArray();if(item.Rotation.Length==3)data.Rotation=item.Rotation.ToArray();if(item.Scale.Length==3)data.Scale=item.Scale.ToArray();
            AiVector(item.PathOffset,"Path correction");if(item.PathOffset.Length==3)data.PathOffset=item.PathOffset.ToArray();
            if(item.ReplaceKeys)data.TransformAnimation=new();if(item.ReplacePoseKeys)data.PoseCorrections=new();
            if(item.Color is{Length:4})item.Color=item.Color.Take(3).ToArray();if(item.Color is not{Length:0}){AiVector(item.Color,"Object color",false);if(item.Color.Any(x=>x<0||x>1))throw new ArgumentException("Object RGB colors are zero to one.");data.Appearance.ColorOverride=item.Color.ToArray();}
            if(item.Link!=null){if(data.Kind!="cylinder")throw new ArgumentException("Linked lines use the cylinder asset.");data.Link=item.Link.ValidatedCopy();}
            if(item.Light!=null){if(!IsSceneLightKind(data.Kind))throw new ArgumentException("Only light assets accept Light properties.");data.Light=item.Light.ValidatedCopy();}
            if(item.PoseId!="")
            {
                var pose=FindLibraryPose(item.PoseId)??throw new ArgumentException("Unknown pose ID: "+item.PoseId);
                if(pose.Pose.RigModelId!=null?pose.Pose.RigModelId!=data.LibraryModelId:data.Kind!="actor")throw new ArgumentException("The pose does not match the object's rig.");
                data.StaticPose=AiClone(pose.Pose);data.MotionClips.Clear();data.PoseCorrections=new();
            }
            if(item.Motions==null||item.Keys==null||item.Path==null||item.PoseKeys==null||item.Motions.Count>100||item.Keys.Count>1000||item.Path.Count>128||item.PoseKeys.Count>500)throw new ArgumentException("The object's animation exceeds plan limits.");
            if(item.ReplaceMotions)data.MotionClips.Clear();
            foreach(var motion in item.Motions)
            {
                var source=MotionCatalog.Find(motion.MotionId)??throw new ArgumentException("Unknown motion ID: "+motion.MotionId);
                if(source.ActorFamily!=data.ActorFamily||data.Kind!="actor"&&data.Kind!="imported")throw new ArgumentException("Motion "+source.Name+" is incompatible with "+data.Name+". Choose the exact actor family.");
                AiTime(motion.Start,document.Duration,"Clip start");
                if(!double.IsFinite(motion.Duration)||motion.Duration<1.0/document.Fps||motion.Start+motion.Duration>document.Duration+1e-6)throw new ArgumentException("Clip duration must fit inside the shot.");
                if(!double.IsFinite(motion.Speed)||motion.Speed<.1||motion.Speed>4||!double.IsFinite(motion.SourceIn)||motion.SourceIn<0||motion.SourceIn>=source.Duration||!double.IsFinite(motion.BlendIn)||motion.BlendIn<0||motion.BlendIn>motion.Duration)throw new ArgumentException("Invalid clip speed, source IN or blend. Speed must be 0.1–4 and IN must be inside the source.");
                if(!motion.Loop&&motion.Duration*motion.Speed>source.Duration-motion.SourceIn+.05)throw new ArgumentException("A one-shot clip cannot exceed its source duration. Shorten it or enable Loop.");
                data.MotionClips.Add(new(){MotionPath=data.Kind=="actor"?MotionCatalog.MaterializeSource(source):data.ModelPath!,Name=source.Name,SourceAnimation=source.SourceAnimation,SourceScale=source.SourceScale,SourceOffset=source.SourceOffset+motion.SourceIn,Start=motion.Start,Duration=motion.Duration,Speed=motion.Speed,BlendIn=motion.BlendIn,Loop=motion.Loop,InPlace=motion.InPlace});
            }
            data.MotionPath=data.MotionClips.FirstOrDefault()?.MotionPath;data.MotionName=data.MotionClips.FirstOrDefault()?.Name;
            if(item.ReplacePath||item.Path.Count>0)
            {
                if(item.Path.Count==1)throw new ArgumentException("A path needs at least two points, or an empty list to clear it.");
                data.Path=new(){Enabled=item.Path.Count>1,Start=0,Duration=document.Duration,OrientToPath=item.OrientToPath};
                double previous=-1;
                foreach(var point in item.Path){AiTime(point.Time,document.Duration,"Path arrival");AiVector(point.Position,"Path point",false);if(point.Time<=previous)throw new ArgumentException("Path arrival times must strictly increase.");previous=point.Time;data.Path.Points.Add(point.Position.ToArray());data.Path.PointTimes.Add(point.Time);data.Path.PointInterpolation.Add(AiEasing(point.Easing));}
                if(item.Path.Count>0)data.Path.Duration=item.Path[^1].Time;
                AiPathTiming(data.Path,item.PathTiming);
            }
            AiKeys(data.TransformAnimation,item.Keys,document.Duration,false);
            foreach(var key in item.PoseKeys)
            {
                if(data.Kind!="actor"&&data.ActorFamily=="")throw new ArgumentException("Pose keys require a rigged actor.");
                if(data.Kind=="actor"&&key.Bone!="Root"&&!AiHumanBones.Contains(key.Bone))throw new ArgumentException("Unknown human bone: "+key.Bone);
                AiTime(key.Time,document.Duration,"Pose key time");AiVector(key.Value,"Pose key XYZ",false);
                if(key.Bone=="Root"){var created=TransformLayerRuntime.Upsert(data.PoseCorrections.Root,key.Time,Vector(key.Value));created.Interpolation=AiEasing(key.Easing);}
                else{if(!data.PoseCorrections.Bones.TryGetValue(key.Bone,out var keys))data.PoseCorrections.Bones[key.Bone]=keys=new();var created=PoseCorrectionRuntime.UpsertRotation(keys,key.Time,Basis.FromEuler(Vector(key.Value)*Mathf.Pi/180).GetRotationQuaternion());created.Interpolation=AiEasing(key.Easing);}
            }
            if(item.PlaceOnId!=""&&(data.Path.Enabled||data.TransformAnimation.Position.Count>0))throw new ArgumentException("Place on object/ground requires a static placement without travel paths or position keys.");
        }
        foreach(var item in plan.Objects.Where(o=>o.PlaceOnId!=""&&o.PlaceOnId!="ground"))
            if(item.PlaceOnId==item.Id||!document.Objects.Any(o=>o.Id==item.PlaceOnId&&!IsSceneLightKind(o.Kind)))throw new ArgumentException("Unknown or invalid supporting object: "+item.PlaceOnId);
        foreach(var item in plan.Cameras)
        {
            AiId(item.Id);if(!changedIds.Add(item.Id)||document.Objects.Any(o=>o.Id==item.Id))throw new ArgumentException("Duplicate object/camera ID: "+item.Id);
            var camera=document.Cameras.FirstOrDefault(c=>c.Id==item.Id);
            if(camera==null){camera=new(){Id=item.Id,Name=item.Name==""?"Camera "+(document.Cameras.Count+1):item.Name};document.Cameras.Add(camera);}
            if(item.Name==null||item.Name.Length>120)throw new ArgumentException("Camera names must have up to 120 characters.");if(item.Name!="")camera.Name=item.Name;
            AiVector(item.Position,"Camera position");AiVector(item.Rotation,"Camera rotation");AiVector(item.LookAtPoint,"Camera look-at point");
            if(item.Position.Length==3)camera.Position=item.Position.ToArray();if(item.Rotation.Length==3)camera.Rotation=item.Rotation.ToArray();
            if(item.LookAtPoint.Length==3)
            {
                var direction=Vector(item.LookAtPoint)-Vector(camera.Position);if(direction.LengthSquared()<.00001f||Math.Abs(direction.Normalized().Dot(Vector3.Up))>.9999f)throw new ArgumentException("Camera look-at point must differ from its position and avoid a vertical singularity.");
                camera.Rotation=Array3(Basis.LookingAt(direction,Vector3.Up).GetEuler()*180/Mathf.Pi);
            }
            if(!float.IsFinite(item.FocalLength)||item.FocalLength<8||item.FocalLength>300)throw new ArgumentException("Camera lens must be 8–300 mm.");camera.FocalLength=item.FocalLength;
            if((item.Follow||item.LookAt)&&!document.Objects.Any(o=>o.Id==item.TargetId))throw new ArgumentException("Camera tracking target is missing: "+item.TargetId);
            camera.Tracking=new CameraTrackingData{FollowEnabled=item.Follow,LookAtEnabled=item.LookAt,FollowTargetId=item.Follow?item.TargetId:"",LookAtTargetId=item.LookAt?item.TargetId:"",FollowOffset=item.FollowOffset,LookAtOffset=item.LookAtOffset,FollowSmoothness=item.Smoothness,LookAtSmoothness=item.Smoothness,TargetRelativeOffsets=item.RelativeOffsets}.ValidatedCopy();
            if(item.Keys==null||item.Keys.Count>1000)throw new ArgumentException("Camera key limit exceeded.");
            if(item.ReplaceKeys){camera.TransformAnimation=new();camera.LensKeys.Clear();camera.Keys.Clear();}
            AiKeys(camera.TransformAnimation,item.Keys,document.Duration,true,camera.LensKeys);
            if(item.Movement is {} movement)
            {
                var curve=new ActorPathData{Enabled=movement.Mode=="Path",Start=movement.Start,Duration=movement.Duration};
                if(movement.Path==null||movement.Path.Count>256)throw new ArgumentException("A camera movement path supports up to 256 points.");
                foreach(var point in movement.Path){AiVector(point.Position,"Camera path point",false);AiTime(point.Time,document.Duration,"Camera path arrival");curve.Points.Add(point.Position.ToArray());curve.PointTimes.Add(point.Time-movement.Start);curve.PointInterpolation.Add(AiEasing(point.Easing));}
                AiPathTiming(curve,movement.PathTiming);
                if(movement.TargetId!=""&&!document.Objects.Any(o=>o.Id==movement.TargetId))throw new ArgumentException("Camera movement target is missing: "+movement.TargetId);
                AiTime(movement.Start,document.Duration,"Camera movement start");AiTime(movement.Start+movement.Duration,document.Duration,"Camera movement end");
                camera.Movement=new CameraMovementData{Mode=movement.Mode,TargetId=movement.TargetId,TargetPosition=movement.TargetPosition,TargetOffset=movement.TargetOffset,AimAtTarget=movement.AimAtTarget,Start=movement.Start,Duration=movement.Duration,Radius=movement.Radius,StartAngleDegrees=movement.StartAngleDegrees,OrbitDegrees=movement.OrbitDegrees,StartHeight=movement.StartHeight,EndHeight=movement.EndHeight,Easing=movement.Easing,Path=curve}.ValidatedCopy();
                if(movement.AimPath.Count>0){var aim=new ActorPathData{Enabled=true,Start=movement.Start,Duration=movement.Duration};foreach(var point in movement.AimPath){AiVector(point.Position,"Aim path point",false);AiTime(point.Time,document.Duration,"Aim path arrival");aim.Points.Add(point.Position.ToArray());aim.PointTimes.Add(point.Time-movement.Start);aim.PointInterpolation.Add(AiEasing(point.Easing));}AiPathTiming(aim,movement.AimPathTiming);camera.Movement.AimPathEnabled=true;camera.Movement.AimPath=aim;camera.Movement=camera.Movement.ValidatedCopy();}
                if(movement.Mode!="None"&&item.Follow)throw new ArgumentException("Camera movement and Follow cannot both control position. Use Orbit/Path plus aim-at-target, or disable movement for Follow.");
            }
        }
        foreach(var linked in document.Objects.Where(o=>o.Link!=null)){var link=linked.Link!;if(!document.Objects.Any(o=>o.Id==link.StartTargetId)||!document.Objects.Any(o=>o.Id==link.EndTargetId))throw new ArgumentException("Linked-line endpoint target is missing.");}
        if(document.Objects.Count>500||document.Cameras.Count>100)throw new ArgumentException("Project object/camera limit exceeded.");
        if(document.Cameras.Count==0)document.Cameras.Add(new(){Name="Camera A",Position=new[]{6f,3f,7f},Rotation=Array3(Basis.LookingAt(new Vector3(-6,-2,-7),Vector3.Up).GetEuler()*180/Mathf.Pi)});
        if(plan.ActiveCameraId!=""){if(!document.Cameras.Any(c=>c.Id==plan.ActiveCameraId))throw new ArgumentException("Active camera is missing: "+plan.ActiveCameraId);document.ActiveCameraId=plan.ActiveCameraId;}
        else if(!document.Cameras.Any(c=>c.Id==document.ActiveCameraId))document.ActiveCameraId=document.Cameras[0].Id;
        foreach(var camera in document.Cameras)
        {
            if(!document.Objects.Any(o=>o.Id==camera.Tracking.FollowTargetId)){camera.Tracking.FollowEnabled=false;camera.Tracking.FollowTargetId="";}
            if(!document.Objects.Any(o=>o.Id==camera.Tracking.LookAtTargetId)){camera.Tracking.LookAtEnabled=false;camera.Tracking.LookAtTargetId="";}
        }
        return document;
    }
    async Task ApplyAiPlan(AiScenePlan plan,bool replace,CancellationToken cancellation=default)
    {
        cancellation.ThrowIfCancellationRequested();var document=CompileAiPlan(plan,replace);var before=Snapshot();string path=_projectPath;double time=_time;
        string? objectId=_selected?.Data.Id,cameraId=_selectedCamera?.Id;var undo=_undo.ToArray();var redo=_redo.ToArray();
        try
        {
            Restore(document,"");_projectPath=replace?"":path;SetTime(0);
            await ToSignal(GetTree(),SceneTree.SignalName.PhysicsFrame);cancellation.ThrowIfCancellationRequested();
            foreach(var item in plan.Objects)
            {
                var entry=_entries.Single(e=>e.Data.Id==item.Id);
                if(entry.NativeTrack!=null&&item.PoseKeys.Any(k=>k.Bone!="Root"&&!entry.NativeTrack.PoseBoneNames.Contains(k.Bone)))throw new ArgumentException("Unknown native bone on "+entry.Data.Name+". Read the rig's bone names before adding corrections.");
                if(item.PlaceOnId=="")continue;var support=PlacementSupport(entry,item.PlaceOnId!="ground");Vector3 destination;
                if(item.PlaceOnId=="ground")destination=new Vector3(support.X,SceneGroundHeight,support.Z);
                else
                {
                    var target=_entries.Single(e=>e.Data.Id==item.PlaceOnId);var bounds=PlacementWorldBounds(target);var center=bounds.GetCenter();
                    var query=PhysicsRayQueryParameters3D.Create(new Vector3(center.X,bounds.End.Y+1,center.Z),new Vector3(center.X,bounds.Position.Y-.1f,center.Z),1);
                    query.Exclude=new Godot.Collections.Array<Rid>(_entries.Where(e=>e!=target).SelectMany(e=>Descendants(e.Node).OfType<CollisionObject3D>()).Concat(_cameras.SelectMany(c=>Descendants(c.Rig).OfType<CollisionObject3D>())).Select(body=>body.GetRid()));
                    var hit=_world.GetWorld3D().DirectSpaceState.IntersectRay(query);if(hit.Count==0)throw new ArgumentException("No supporting surface found on "+target.Data.Name+".");destination=hit["position"].AsVector3();
                }
                entry.Data.Position=Array3(entry.Node.Position+destination-support);entry.Node.Position=Vector(entry.Data.Position);
            }
            SetTime(0);_undo.Clear();foreach(var state in undo.Reverse())_undo.Push(state);_undo.Push((before,path));_redo.Clear();RefreshHistoryControls();
            Select(_entries.FirstOrDefault(e=>plan.Objects.Any(item=>item.Id==e.Data.Id)));if(_selected==null&&_cameras.Count>0)SelectCamera(_cameras.First(c=>c.Id==ActiveCameraId));
            RefreshTimelineSize();Status("AI Director · "+plan.Summary+" · Ctrl+Z to undo this shot.");
        }
        catch
        {
            Restore(before,"");_projectPath=path;SetTime(time);RestoreInteractionSelection(objectId,cameraId);_undo.Clear();_redo.Clear();foreach(var state in undo.Reverse())_undo.Push(state);foreach(var state in redo.Reverse())_redo.Push(state);RefreshHistoryControls();throw;
        }
    }
}
