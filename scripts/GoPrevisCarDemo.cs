using Godot;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;

namespace GoPrevis;

public partial class GoPrevisApp
{
    const string DemoCarId="demo-car",DemoFollowCameraId="demo-car-follow",DemoOverviewCameraId="demo-car-overview";

    /// <summary>A portable authored shot. Every visible scene part uses a built-in prop.</summary>
    void CreateCarChaseDemo(string? savePath=null)
    {
        StopPoseEditing();if(_drawingPath)CancelDrawingPath();_playing=false;_play.Text="▶";_autoKey=false;
        if(_pilot)TogglePilot();ClearObjects();ClearCameras();_fps=25;_duration=12;_scrub.MaxValue=12;_scrub.Step=1.0/25;
        bool refreshing=_refreshing;_refreshing=true;_durationField.Value=12;_refreshing=refreshing;
        ApplyProjectSetup(new ProjectSetupData{Width=1920,Height=1080});
        _lighting.DirectionalEnabled=false;
        ApplySceneSettings(new SceneSettingsData{BackgroundColor=new[]{.17f,.22f,.28f,1f},Brightness=1.25f,GroundVisible=false,ObjectLabelsVisible=false});

        Entry Prop(string id,string kind,string name,Vector3 position,Vector3 scale,Color? color=null,float yaw=0)
        {
            var e=AddObject(kind,position,name,false);e.Data.Id=id;e.Node.SetMeta("entry_id",id);e.Data.Scale=Array3(scale);e.Node.Scale=scale;e.Data.Rotation=new[]{0f,yaw,0};e.Node.RotationDegrees=new Vector3(0,yaw,0);
            e.Data.Appearance.ShowLabel=false;if(color is {} c)e.Data.Appearance.ColorOverride=new[]{c.R,c.G,c.B};ApplyObjectAppearance(e);return e;
        }
        Entry Block(string id,string name,Vector3 position,Vector3 size,string color,float yaw=0)=>Prop(id,"box",name,position,size,new Color(color),yaw);

        // The first scene item is the editable hero, so its Path row stays near the top.
        var car=Prop(DemoCarId,"car","Blue car · eased drive",new Vector3(-10,.062f,12),Vector3.One);
        car.Data.Path=new ActorPathData{Enabled=true,OrientToPath=true,Start=1,Points=new(){
            new[]{-10f,.062f,12},new[]{-10f,.062f,8},new[]{-10f,.062f,2},
            new[]{-8.5f,.062f,-1.5f},new[]{-5f,.062f,-3},new[]{1.3f,.062f,-3},new[]{5.3f,.062f,-3}}};
        var path=car.Data.Path;var fractions=ActorPathRuntime.WaypointFractions(path);double length=ActorPathRuntime.Length(path);
        var distances=fractions.Select(f=>f*length).ToArray();double acceleration=2,cruiseSpeed=2*(distances[1]-distances[0])/acceleration;
        path.PointTimes.Add(0);path.PointTimes.Add(acceleration);
        for(int i=2;i<distances.Length-1;i++)path.PointTimes.Add(path.PointTimes[^1]+(distances[i]-distances[i-1])/cruiseSpeed);
        path.PointTimes.Add(path.PointTimes[^1]+2*(distances[^1]-distances[^2])/cruiseSpeed);path.Duration=path.PointTimes[^1];
        for(int i=0;i<path.Points.Count;i++)path.PointInterpolation.Add(new());
        // Quadratic easing expressed as cubic Bezier with linear time handles. Its end
        // derivative matches the cruise speed instead of stopping at every path point.
        path.PointInterpolation[0]=new(){Mode="Curved",Control1=new[]{1f/3,0},Control2=new[]{2f/3,1f/3}};
        path.PointInterpolation[^2]=new(){Mode="Curved",Control1=new[]{1f/3,2f/3},Control2=new[]{2f/3,1f}};

        Block("demo-ground","Park ground",new Vector3(-1,-.28f,3),new Vector3(66,.28f,54),"536356");
        Block("demo-road-north","Road · approach",new Vector3(-10,.006f,7),new Vector3(7,.05f,28),"353d47");
        Block("demo-road-east","Road · exit",new Vector3(1,.006f,-3),new Vector3(29,.05f,7),"353d47");
        Block("demo-curb-west","Curb · approach outside",new Vector3(-13.65f,0,7),new Vector3(.22f,.17f,28),"9da9a4");
        Block("demo-curb-east","Curb · approach inside",new Vector3(-6.35f,0,11),new Vector3(.22f,.17f,20),"9da9a4");
        Block("demo-curb-far","Curb · exit outside",new Vector3(1,0,-6.65f),new Vector3(29,.17f,.22f),"9da9a4");
        Block("demo-curb-near","Curb · exit inside",new Vector3(3.5f,0,.65f),new Vector3(20,.17f,.22f),"9da9a4");
        var geometry=new ActorPathData{Points=path.Points,Duration=1};
        for(int i=0;i<15;i++)
        {
            var sample=ActorPathRuntime.Evaluate(geometry,(i+.4)/15);var right=sample.Tangent.Cross(Vector3.Up).Normalized();
            var p=sample.Position-right*1.42f;p.Y=.057f;float yaw=Mathf.RadToDeg(Mathf.Atan2(sample.Tangent.X,sample.Tangent.Z))-180;
            Block("demo-marking-"+i,"Road marking · "+(i+1).ToString("D2"),p,new Vector3(.1f,.006f,.85f),"dce2df",yaw);
        }
        // Context is placed outside both the hero's path and the tracking camera's sweep.
        foreach(var tree in new[]{("01",new Vector3(-17,0,15)),("02",new Vector3(-17,0,4)),("03",new Vector3(-14.5f,0,-11)),("04",new Vector3(-3,0,-11)),("05",new Vector3(9,0,-11)),("06",new Vector3(15,0,3))})
            Prop("demo-tree-"+tree.Item1,"tree","Park tree · "+tree.Item1,tree.Item2,new Vector3(1.35f,1.35f,1.35f));
        Block("demo-pavilion","Park pavilion",new Vector3(3.7f,0,7.2f),new Vector3(4.4f,2.5f,3.2f),"7e9294");
        Block("demo-pavilion-roof","Pavilion roof",new Vector3(3.7f,2.5f,7.2f),new Vector3(5,.18f,3.8f),"c0c9bf");
        Block("demo-pavilion-window","Pavilion window",new Vector3(3.7f,.8f,5.57f),new Vector3(3.1f,1.1f,.05f),"344b5d");
        Prop("demo-bench","chair","Park chair",new Vector3(11,0,4.5f),new Vector3(1.4f,1.4f,1.4f),null,-50);

        var follow=CreateCamera("Car follow",new CameraTrackData{Id=DemoFollowCameraId,Name="Car follow",FocalLength=35,ShowLabel=false},true);
        follow.AnimationInitialized=true;follow.Animation=new();follow.LensKeys.Clear();follow.Rig.ClearKeys();follow.BaseLens=35;
        var opening=ActorPathRuntime.Evaluate(path,0);var openingForward=opening.Tangent.Normalized();var openingRight=openingForward.Cross(Vector3.Up).Normalized();
        var openingPosition=opening.Position+openingForward*7.9f-openingRight*5.8f+Vector3.Up*3.5f;
        follow.Rig.SetPose(openingPosition,opening.Position+Vector3.Up*.8f+openingForward*.65f);
        follow.BasePosition=follow.Rig.GlobalPosition;follow.BaseRotation=follow.Rig.GlobalRotationDegrees;
        follow.Tracking=new CameraTrackingData{FollowEnabled=true,FollowTargetId=DemoCarId,FollowOffset=new[]{-5.8f,3.5f,-7.9f},
            LookAtEnabled=true,LookAtTargetId=DemoCarId,LookAtOffset=new[]{0f,.8f,-.65f},TargetRelativeOffsets=true,FollowSmoothness=0,LookAtSmoothness=0};
        var overview=CreateCamera("Wide overview",new CameraTrackData{Id=DemoOverviewCameraId,Name="Wide overview",FocalLength=27,ShowLabel=false},false);
        overview.Rig.SetPose(new Vector3(18,25,30),new Vector3(-2,0,3));SyncCameraFromRig(overview);overview.AnimationInitialized=true;
        SetActiveCamera(follow.Id);SetTime(0);Select(car);ShowInspectorSection("Path");_selectedPathPoint=0;RefreshSequenceInspector();RefreshTimelineSize();
        _orbitTarget=new Vector3(-2,0,3);_yaw=.6f;_pitch=.7f;_distance=42;UpdateOrbit();
        _projectPath="";if(savePath!=null)SaveTo(savePath);
        _undo.Clear();_redo.Clear();RefreshHistoryControls();Status("Car chase demo · Space to play · 0–1 s parked, ease into the turn, then stop. Select Wide overview to inspect the route.");
    }

    public async Task<int> RunCarDemoChecks(bool saveSample=true)
    {
        var original=Snapshot();string project=_projectPath;double time=_time;bool pilot=_pilot;var editor=_editorCamera.Transform;
        var undo=_undo.ToArray();var redo=_redo.ToArray();int checks=0;bool passed=false;string? failure=null;var observations=new List<object>();
        string output=ProjectSettings.GlobalizePath("res://output/car-demo-checks");Directory.CreateDirectory(output);
        void Check(bool value,string message){Require(value,"Car demo: "+message);checks++;observations.Add(new{message,passed=value});}
        try
        {
            CreateCarChaseDemo();var car=_entries.Single(e=>e.Data.Id==DemoCarId);var path=car.Data.Path;
            double stop=path.Start+path.Duration;Check(_duration==12&&_fps==25&&ProjectResolution==new Vector2I(1920,1080),"the ready-to-play shot is 12 seconds, 25 fps and full HD");
            Check(stop>9.8&&stop<10.2,"the stop leaves a clear two-second stationary beat before twelve seconds");
            Check(_entries.All(e=>e.Data.Kind!="imported"&&e.Data.ModelPath==null)&&_cameras.Count==2,"both cameras and all scene geometry are self-contained without external model dependencies");
            Vector3 Position(double t)=>ActorPathRuntime.Evaluate(path,t).Position;
            double Speed(double t)=>Position(t+.005).DistanceTo(Position(t-.005))/.01;
            Check(Position(0).IsEqualApprox(Position(.99))&&Position(stop).IsEqualApprox(Position(11.99)),"the car stays parked during the opening and final beats");
            Check(Speed(1.25)<Speed(1.75)&&Speed(1.75)<Speed(2.5),"the departure continuously accelerates rather than jumping to cruise speed");
            Check(Speed(stop-1.5)>Speed(stop-.8)&&Speed(stop-.8)>Speed(stop-.2)&&Speed(stop+.1)<.0001,"the final segment visibly decelerates all the way to a stop");
            var cruise=new[]{3.2,4.0,5.0,6.0,7.5}.Select(Speed).ToArray();Check(cruise.Max()-cruise.Min()<.08,"intermediate path keys retain near-constant travel speed through the turn");
            var startHeading=ActorPathRuntime.Evaluate(path,1).Tangent;var endHeading=ActorPathRuntime.Evaluate(path,stop).Tangent;
            Check(Math.Abs(startHeading.Dot(endHeading))<.05&&Math.Abs(startHeading.Y)<.001&&Math.Abs(endHeading.Y)<.001,"the route makes a smooth horizontal ninety-degree turn");
            bool facing=true,visible=true,finite=true,tracking=true;float minimumMargin=1;
            var bounds=PropFactory.CalculateBounds(car.Node);var follow=_cameras.Single(c=>c.Id==DemoFollowCameraId);
            for(int frame=0;frame<=300;frame++)
            {
                double t=frame/25.0;SetTime(t);var sample=ActorPathRuntime.Evaluate(path,t);var actual=(-car.Node.GlobalBasis.Z).Normalized();facing&=actual.Dot(sample.Tangent)>.999f;
                finite&=car.Node.Transform.IsFinite()&&follow.Rig.Transform.IsFinite();
                float tan=Mathf.Tan(Mathf.DegToRad(follow.Rig.ShotCamera.Fov)/2);var inverse=follow.Rig.GlobalTransform.AffineInverse();
                for(int corner=0;corner<8;corner++)
                {
                    var point=inverse*(car.Node.GlobalTransform*bounds.GetEndpoint(corner));float z=-point.Z;
                    float x=.5f+point.X/(2*z*tan),y=.5f-point.Y/(2*z*tan/ProjectAspect);float margin=Math.Min(Math.Min(x,1-x),Math.Min(y,1-y));
                    minimumMargin=Math.Min(minimumMargin,margin);visible&=z>.05f&&margin>.06f;
                }
                var aim=(inverse*(sample.Position+Vector3.Up*.8f+sample.Tangent*.65f));tracking&=Math.Abs(aim.X)<.002f&&Math.Abs(aim.Y)<.002f;
            }
            Check(facing,"the actual car bonnet faces forward along the path at every one of 301 sampled times");
            Check(finite&&visible,"the complete car stays finite and inside the following camera with safe framing margins at all 301 samples");
            Check(tracking,"the following camera aims at the intended lead point throughout the entire turn");
            SetTime(5.32);var carTransform=car.Node.Transform;var cameraTransform=follow.Rig.Transform;SetTime(9);SetTime(5.32);Check(car.Node.Transform.IsEqualApprox(carTransform)&&follow.Rig.Transform.IsEqualApprox(cameraTransform),"car and camera are deterministic when scrubbing backward");
            SetTime(stop+.2);var endCar=car.Node.Transform;var endCamera=follow.Rig.Transform;SetTime(11.99);Check(car.Node.Transform.IsEqualApprox(endCar)&&follow.Rig.Transform.IsEqualApprox(endCamera),"the camera settles with the stopped vehicle during the final beat");
            Check(follow.Animation.Position.Count==0&&follow.Animation.Rotation.Count==0&&follow.Rig.Keys.Count==0&&follow.Tracking.FollowEnabled&&follow.Tracking.LookAtEnabled,
                "the camera uses editable live Follow and Look at settings without baked transform keys");
            var originalPoints=path.Points.Select(point=>(float[])point.Clone()).ToList();
            SetTime(5.32);var originalCarPosition=car.Node.Position;var originalCameraPosition=follow.Rig.GlobalPosition;
            string unchangedKeys=JsonSerializer.Serialize(follow.Animation);
            var translation=new Vector3(3,0,-2);foreach(var point in path.Points){point[0]+=translation.X;point[2]+=translation.Z;}
            SetTime(5.32);
            Check((car.Node.Position-originalCarPosition).IsEqualApprox(translation)&&(follow.Rig.GlobalPosition-originalCameraPosition).IsEqualApprox(translation),
                "moving the authored car path moves the following camera immediately without rebaking");
            path.Points[3][0]+=1.2f;path.Points[3][2]-=1.8f;SetTime(5.32);
            var changedSample=ActorPathRuntime.Evaluate(path,5.32);var changedAim=follow.Rig.GlobalTransform.AffineInverse()*(changedSample.Position+Vector3.Up*.8f+changedSample.Tangent*.65f);
            Check(Math.Abs(changedAim.X)<.002f&&Math.Abs(changedAim.Y)<.002f,"editing the bend still keeps Look at aimed at the moving car");
            var changedTransform=follow.Rig.GlobalTransform;SetTime(8.7);SetTime(5.32);
            Check(follow.Rig.GlobalTransform.IsEqualApprox(changedTransform),"the live camera remains deterministic after a path edit and reverse seek");
            Check(JsonSerializer.Serialize(follow.Animation)==unchangedKeys&&follow.Animation.Position.Count==0&&follow.Animation.Rotation.Count==0,
                "path edits never rewrite or generate camera animation keys");
            path.Points=originalPoints;SetTime(5.32);
            Check(car.Node.Transform.IsEqualApprox(carTransform)&&follow.Rig.Transform.IsEqualApprox(cameraTransform),"restoring path points restores the exact original live composition");

            string samplePath=Path.Combine(output,"car-chase.goprevis");SetTime(0);SaveTo(samplePath);
            if(saveSample){string sourceSample=ProjectSettings.GlobalizePath("res://samples/car-chase.goprevis");Directory.CreateDirectory(Path.GetDirectoryName(sourceSample)!);File.Copy(samplePath,sourceSample,true);}
            var saved=JsonSerializer.Deserialize<ProjectData>(File.ReadAllText(samplePath))!;var savedFollow=saved.Cameras.Single(camera=>camera.Id==DemoFollowCameraId);
            Check(saved.ActiveCameraId==DemoFollowCameraId&&saved.Objects.Single(e=>e.Id==DemoCarId).Path.PointInterpolation[0].Mode=="Curved"&&savedFollow.Tracking.FollowTargetId==DemoCarId&&savedFollow.Tracking.LookAtTargetId==DemoCarId&&savedFollow.TransformAnimation.Position.Count==0,
                "the portable scene opens with the correct active camera, live targets and acceleration timing");
            LoadProjectFromPath(samplePath);SetTime(5.32);car=_entries.Single(e=>e.Data.Id==DemoCarId);follow=_cameras.Single(c=>c.Id==DemoFollowCameraId);
            Check(car.Node.Transform.IsEqualApprox(carTransform)&&follow.Rig.Transform.IsEqualApprox(cameraTransform),"portable save/reopen preserves the exact car and camera poses");
            if(DisplayServer.GetName()!="headless")
            {
                var frames=new List<object>();foreach(double t in new[]{0,1.7,3.2,5.2,6.8,8.4,10.4,11.96})
                {
                    SetTime(t);string file=Path.Combine(output,"frame-"+((int)Math.Round(t*25)).ToString("D3")+".png");await RenderFrame(file);frames.Add(new{time=t,file});
                }
                SetActiveCamera(DemoOverviewCameraId);SetTime(5);await RenderFrame(Path.Combine(output,"overview.png"));SetActiveCamera(DemoFollowCameraId);SetTime(0);
                string videoFolder=Path.Combine(output,"video-"+Guid.NewGuid().ToString("N")[..8]);var video=await ExportShotAsync(new ShotExportSettings{OutputDirectory=videoFolder,Width=1280,Height=720,Fps=25,EndSeconds=12,ExportMp4=true,ExportPngSequence=false,IncludeColor=true,IncludeDepth=false,IncludePose=false});
                Check(video.FrameCount==300&&video.Videos["color"].FrameCount==300&&video.Videos["color"].Fps==25,"a real twelve-second 300-frame MP4 renders the complete active-camera shot");
                File.Copy(Path.Combine(video.Directory,"color.mp4"),Path.Combine(output,"car-chase-preview.mp4"),true);
                Select(car);ShowInspectorSection("Path");if(!_pilot)TogglePilot();SetTime(0);for(int i=0;i<3;i++)await ToSignal(GetTree(),SceneTree.SignalName.ProcessFrame);await ToSignal(RenderingServer.Singleton,RenderingServer.SignalName.FramePostDraw);GetViewport().GetTexture().GetImage().SavePng(Path.Combine(output,"car-chase-ui.png"));
                File.WriteAllText(Path.Combine(output,"frames.json"),JsonSerializer.Serialize(frames,_json));
            }
            passed=true;GD.Print("CAR_DEMO_OK checks="+checks+" stop="+stop.ToString("0.###")+" margin="+minimumMargin.ToString("0.###"));return checks;
        }
        catch(Exception ex){failure=ex.ToString();throw;}
        finally
        {
            if(_pilot)TogglePilot();Restore(original,"");_projectPath=project;SetTime(time);_editorCamera.Transform=editor;if(pilot)TogglePilot();
            _undo.Clear();_redo.Clear();foreach(var item in undo.Reverse())_undo.Push(item);foreach(var item in redo.Reverse())_redo.Push(item);RefreshHistoryControls();
            File.WriteAllText(Path.Combine(output,"report.json"),JsonSerializer.Serialize(new{status=passed?"passed":"failed",checks,failure,observations},_json));
        }
    }
    async Task RunCarDemoChecksAndQuit()
    {
        try{await RunCarDemoChecks(OS.HasFeature("editor"));GetTree().Quit(0);}
        catch(Exception ex){GD.PushError("CAR_DEMO_FAILED "+ex);GetTree().Quit(1);}
    }
}
