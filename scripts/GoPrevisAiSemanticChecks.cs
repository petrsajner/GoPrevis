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
    async Task RunAiSemanticChecksAndQuit(bool live)
    {try{if(live)await RunAiSemanticLiveChecks();else await RunAiSemanticChecks();GetTree().Quit(0);}catch(Exception error){GD.PushError("AI_SEMANTIC_FAILED "+error);GetTree().Quit(1);}}
    static AiScenePlan AiSemanticFixture()=>new(){Summary="Forward walking on a curve with an exact full camera orbit.",Duration=8,Objects=new(){
        new(){Id="orbit-subject",AssetId="box",Name="Orbit subject"},
        new(){Id="forward-walker",AssetId="human-average",Name="Forward walker",PathTiming="Constant speed",ReplacePath=true,OrientToPath=true,
            Path=new(){new(){Time=0,Position=new[]{-3f,0f,2f}},new(){Time=4,Position=new[]{-1f,0f,0f}},new(){Time=8,Position=new[]{3f,0f,-2f}}},
            ReplaceMotions=true,Motions=new(){new(){MotionId="human-walk-gesture",Duration=8,Loop=true,InPlace=true}}}},
        Cameras=new(){new(){Id="native-orbit",Name="Full orbit",Intent="Orbit",ReplaceKeys=true,Movement=new(){Mode="Orbit",TargetId="orbit-subject",TargetOffset=new[]{0f,.5f,0f},Radius=6,OrbitDegrees=360,Duration=8,StartHeight=2,EndHeight=2,Easing="Linear"}}},ActiveCameraId="native-orbit"};

    public async Task<int> RunAiSemanticChecks()
    {
        var original=Snapshot();string project=_projectPath;double time=_time;var undo=_undo.ToArray();var redo=_redo.ToArray();int checks=0;bool passed=false;string? failure=null;var observations=new List<object>();
        string output=ProjectSettings.GlobalizePath("res://output/ai-semantic-checks/run-"+Guid.NewGuid().ToString("N")[..10]);Directory.CreateDirectory(output);
        void Check(bool value,string message){Require(value,"AI semantics: "+message);checks++;observations.Add(new{message,passed=value});GD.Print("PASS AI semantics: "+message);}
        try
        {
            StopPoseEditing();StopHistoryPlayback();var good=AiSemanticFixture();var reviewed=ReviewAiPlan(good,true);
            Check(reviewed.Valid&&reviewed.Operations.Any(o=>o.Contains("analytic orbit")),"review recognizes native orbit and oriented curve travel as separate program operations");
            var bad=AiClone(good);bad.Objects[1].Path.Clear();bad.Objects[1].Keys=new(){new(){Time=0,Value=new float[3]},new(){Time=8,Value=new[]{0f,0f,-5f}}};
            Check(ReviewAiPlan(bad,true).Errors.Any(e=>e.Code=="travel_requires_path"),"walking translated by transform keys is rejected with a path-based repair instruction");
            bad=AiClone(good);bad.Objects[1].OrientToPath=false;Check(ReviewAiPlan(bad,true).Errors.Any(e=>e.Code=="path_facing_disabled"),"forward travel with tangent orientation disabled is rejected");
            bad=AiClone(good);bad.Objects[1].Rotation=new[]{0f,180f,0f};Check(ReviewAiPlan(bad,true).Errors.Any(e=>e.Code=="travel_heading_mismatch"),"a forward walk whose base yaw turns it backward is rejected");
            bad.Objects[1].TravelFacing="Backward";Check(!ReviewAiPlan(bad,true).Errors.Any(e=>e.Code=="travel_heading_mismatch"),"explicit backward travel retains an intentional 180-degree correction");
            bad=AiClone(good);bad.Objects[1].Motions[0].InPlace=false;Check(ReviewAiPlan(bad,true).Errors.Any(e=>e.Code=="double_root_translation"),"double translation from mocap plus path is rejected");
            bad=AiClone(good);bad.Cameras[0].Movement=null;bad.Cameras[0].Keys=new(){new(){Time=0,Value=new[]{0f,2f,6f}},new(){Time=2,Value=new[]{6f,2f,0f}},new(){Time=4,Value=new[]{0f,2f,-6f}},new(){Time=6,Value=new[]{-6f,2f,0f}}};
            Check(ReviewAiPlan(bad,true).Errors.Any(e=>e.Code=="orbit_requires_native_motion"),"a four-key polygon pretending to be an orbit is rejected");
            bad=AiClone(good);bad.Cameras[0].Movement!.Radius=.2f;Check(ReviewAiPlan(bad,true).Errors.Any(e=>e.Code=="orbit_inside_subject"),"an orbit intersecting actual subject geometry is rejected");
            string geometry=AiAssetDetails("chair");Check(geometry.Contains("localBoundsAtObjectScaleOne")&&geometry.Contains("Chair"),"asset inspection measures real prepared geometry without adding objects to the user's scene");
            int history=_undo.Count;await ApplyAiPlan(good,true);Check(_undo.Count==history+1,"native orbit and travel commit together in one Undo step");
            var camera=_cameras.Single();var data=camera.Movement;var center=new Vector3(0,.5f,0);var walker=_entries.Single(e=>e.Data.Id=="forward-walker");
            double minSpeed=double.PositiveInfinity,maxSpeed=0;float minFacing=1;var before=camera.Rig.GlobalPosition;Quaternion startRotation=camera.Rig.Quaternion;
            for(int i=0;i<=800;i++)
            {
                double t=i*.01;SetTime(t);var position=camera.Rig.GlobalPosition;
                CheckFinite(position);
                if(Math.Abs(new Vector2(position.X-center.X,position.Z-center.Z).Length()-6)>.0001f)throw new Exception("Orbit radius is not constant.");
                if(i>0){double speed=position.DistanceTo(before)/.01;minSpeed=Math.Min(minSpeed,speed);maxSpeed=Math.Max(maxSpeed,speed);}before=position;
                var tangent=ActorPathRuntime.Evaluate(walker.Data.Path,t).Tangent;var facing=walker.Node.Basis*((RiggedActor)walker.Node).AnimatedForward;tangent.Y=0;facing.Y=0;
                if(tangent.LengthSquared()>.00001&&facing.LengthSquared()>.00001)minFacing=Math.Min(minFacing,tangent.Normalized().Dot(facing.Normalized()));
                if((-camera.Rig.GlobalBasis.Z).Normalized().Dot((center-position).Normalized())<.9999f)throw new Exception("Orbit aim drifted away from its center.");
            }
            Check(camera.Animation.Position.Count==0&&camera.Animation.Rotation.Count==0,"the full orbit contains zero baked position or rotation keys");
            Check(maxSpeed/minSpeed<1.003,"801 actual camera samples maintain continuous constant orbit speed");
            Check(minFacing>.99,"the actual displayed walk faces the curve tangent throughout the route");
            Check(camera.Rig.GlobalPosition.DistanceTo(new Vector3(0,2.5f,6))<.0001f&&Math.Abs(startRotation.Dot(camera.Rig.Quaternion))>.99999,"a 360-degree orbit closes its position and viewing angle exactly");
            var expected=CameraBasePosition(camera,3.127);SetTime(7);SetTime(.4);SetTime(3.127);Check(camera.Rig.GlobalPosition.DistanceTo(expected)<.000001,"orbit evaluation is deterministic under reverse and fractional-time seeking");
            SelectCamera(camera);var originalPosition=camera.Rig.GlobalPosition;BeginSceneTransformGesture();SetSelectedTransformFromGizmo("Position",camera.Rig.Position+Vector3.Up*.4f);EndSceneTransformGesture();Check(Math.Abs(camera.Rig.GlobalPosition.Y-originalPosition.Y-.4f)<.001f&&camera.Movement.Mode=="Orbit","viewport transforms adjust a native move without replacing it with keys");
            string saved=Path.Combine(output,"smooth-orbit");SaveTo(saved);LoadProjectFromPath(saved+".goprevis");Check(_cameras.Single().Movement.Mode=="Orbit"&&Snapshot().Version==ProjectData.CurrentVersion,"native motion parameters and complete actor routes survive portable save/reopen");
            if(DisplayServer.GetName()!="headless")
            {
                SelectCamera(_cameras.Single());_movementExpanded=true;RefreshCameraMovementInspector();for(int i=0;i<4;i++)await ToSignal(GetTree(),SceneTree.SignalName.ProcessFrame);GetViewport().GetTexture().GetImage().SavePng(Path.Combine(output,"orbit-inspector.png"));
                var exported=await ExportShotAsync(new(){OutputDirectory=Path.Combine(output,"shot-preview"),Width=960,Height=540,Fps=24,EndSeconds=8,IncludeColor=true,IncludeDepth=true,IncludePose=true,ExportMp4=true,ExportPngSequence=true});
                Check(exported.FrameCount==192&&exported.Videos.Count==3,"the exact orbit renders as a 192-frame color/depth/pose shot");
                var captured=AiClone(good);captured.Objects[1].Motions[0].MotionId="human-cmu-walk";string signature=StableAiSceneSignature();int historyBefore=_undo.Count,redoBefore=_redo.Count;
                var rehearsed=await PreviewAiPlan(captured,true,System.Threading.CancellationToken.None);var gait=(GaitMeasurement)rehearsed.Measurements.Single();
                GD.Print("ACTUAL_GAIT opposition="+gait.OppositionScore+" toe="+gait.ToeDirectionDot+" face="+gait.FaceDirectionDot+" contacts="+gait.ContactSamples);
                Check(rehearsed.Passed&&gait.ContactSamples>=10&&gait.OppositionScore>.2f&&gait.ToeDirectionDot>.6f,"recorded CMU walking passes an independent actual-foot stance/toe check, not only the internal heading vector");
                Check(StableAiSceneSignature()==signature&&_undo.Count==historyBefore&&_redo.Count==redoBefore,"rendering a proposed shot leaves the user's scene and history unchanged");
                Check(rehearsed.Images.Count==2&&rehearsed.Images.All(image=>image.DataUrl.StartsWith("data:image/png;base64,")),"review supplies real camera and side-view gait filmstrips to the agent");
                captured.Objects[1].Rotation=new[]{0f,180f,0f};var backward=await PreviewAiPlan(captured,true,System.Threading.CancellationToken.None);
                Check(!backward.Passed&&backward.Errors.Count>0,"the actual-foot diagnostic rejects deliberately backward sliding despite any claimed facing intent");
            }
            passed=true;GD.Print("AI_SEMANTIC_OK checks="+checks+" speedRatio="+(maxSpeed/minSpeed).ToString("0.000000",System.Globalization.CultureInfo.InvariantCulture)+" minimumFacingDot="+minFacing+" output="+output);return checks;
        }
        catch(Exception error){failure=error.ToString();throw;}
        finally
        {File.WriteAllText(Path.Combine(output,"report.json"),JsonSerializer.Serialize(new{status=passed?"passed":"failed",checks,failure,observations},_json));Restore(original,"");_projectPath=project;SetTime(time);_undo.Clear();_redo.Clear();foreach(var state in undo.Reverse())_undo.Push(state);foreach(var state in redo.Reverse())_redo.Push(state);}
        static void CheckFinite(Vector3 value){if(!value.IsFinite())throw new Exception("Nonfinite orbit point.");}
    }
    public async Task RunAiSemanticLiveChecks()
    {
        string output=ProjectSettings.GlobalizePath("res://output/ai-semantic-live/run-"+Guid.NewGuid().ToString("N")[..10]);Directory.CreateDirectory(output);int checks=0;bool passed=false;string? failure=null;
        void Check(bool value,string message){Require(value,"Live AI semantics: "+message);checks++;GD.Print("PASS live AI semantics: "+message);}
        try
        {
            OpenAiDirector();AiSettings.Selection=4;_aiModelChoice.Select(4);RefreshAiConnection();_aiModeChoice.Select(1);
            _aiPrompt.Text="Vytvoř 8sekundový záběr. Jedna zarigovaná postava jde normálně dopředu po mírně zahnuté trase od [-3,0,1] přes [0,0,0] do [3,0,-1]. Použij mocap chůze. Vedle je statická krabice. Kamera udělá plynulý celý oblet 360 stupňů kolem krabice, konstantní rychlostí a stále se na ni dívá. Postava má být otočená ve směru své chůze. Nová scéna, jen dva objekty a jedna kamera.";
            _aiLastResult=null;await BuildAiShot();var result=_aiLastResult??throw new Exception(_aiResult.Text);
            Check(result.Actions.Contains("inspect_scene_plan")&&result.Actions.Contains("apply_scene_plan"),"the live agent inspects its actual proposal before committing it");
            Check(result.Actions.Contains("assess_preview")&&result.VerifiedOperations.Any(operation=>operation.StartsWith("Visual rehearsal:",StringComparison.Ordinal)),"the live agent receives rendered filmstrips and explicitly evaluates them before commit");
            var person=_entries.Single(e=>e.Node is RiggedActor);var camera=_cameras.Single(c=>c.Id==ActiveCameraId);
            Check(person.Data.Path.Enabled&&person.Data.Path.OrientToPath&&person.Data.TransformAnimation.Position.Count==0,"live forward walking uses a path with tangent orientation and no position-key route");
            Check(camera.Movement.Mode=="Orbit"&&Math.Abs(camera.Movement.OrbitDegrees)>=359&&camera.Animation.Position.Count==0&&camera.Animation.Rotation.Count==0,"the live 360-degree orbit is native and unbaked");
            float minimumFacing=1,minimumVisibleFace=1;double minimumSpeed=double.PositiveInfinity,maximumSpeed=0;Vector3 previous=Vector3.Zero;
            for(int i=0;i<=240;i++)
            {
                double t=_duration*i/240;SetTime(t);var tangent=ActorPathRuntime.Evaluate(person.Data.Path,t).Tangent;var facing=person.Node.Basis*((RiggedActor)person.Node).AnimatedForward;tangent.Y=0;facing.Y=0;if(tangent.LengthSquared()>.0001)minimumFacing=Math.Min(minimumFacing,tangent.Normalized().Dot(facing.Normalized()));
                var visibleFace=((RiggedActor)person.Node).VisibleFaceDirectionWorld();visibleFace.Y=0;if(tangent.LengthSquared()>.0001&&visibleFace.LengthSquared()>.0001)minimumVisibleFace=Math.Min(minimumVisibleFace,tangent.Normalized().Dot(visibleFace.Normalized()));
                if(i>0){double speed=camera.Rig.GlobalPosition.DistanceTo(previous)/(_duration/240);minimumSpeed=Math.Min(minimumSpeed,speed);maximumSpeed=Math.Max(maximumSpeed,speed);}previous=camera.Rig.GlobalPosition;
            }
            Check(minimumFacing>.95,"the generated actor's real forward direction agrees with its velocity throughout the shot");Check(maximumSpeed/minimumSpeed<1.01,"the live camera maintains smooth angular motion without key-boundary jumps");
            Check(minimumVisibleFace>.65,"the actual rendered face stays toward the travel direction, independently of the program heading metadata");
            SaveTo(Path.Combine(output,"live-smooth-orbit"));File.WriteAllText(Path.Combine(output,"plan.json"),JsonSerializer.Serialize(result.Plan,AiDirectorClient.Json));File.WriteAllText(Path.Combine(output,"usage.json"),JsonSerializer.Serialize(new{result.Requests,result.InputTokens,result.OutputTokens,result.EstimatedUsd,result.Actions,result.VerifiedOperations},_json));
            for(int i=0;i<4;i++)await ToSignal(GetTree(),SceneTree.SignalName.ProcessFrame);GetViewport().GetTexture().GetImage().SavePng(Path.Combine(output,"ai-director.png"));_aiDialog!.Hide();
            var exported=await ExportShotAsync(new(){OutputDirectory=Path.Combine(output,"shot-preview"),Width=960,Height=540,Fps=24,EndSeconds=8,IncludeColor=true,IncludeDepth=true,IncludePose=true,ExportMp4=true,ExportPngSequence=true});Check(exported.FrameCount==192,"the live generated orbit exports all 192 frames and control passes");
            passed=true;GD.Print("AI_SEMANTIC_LIVE_OK checks="+checks+" output="+output);
        }
        catch(Exception error){failure=error.ToString();throw;}
        finally{File.WriteAllText(Path.Combine(output,"report.json"),JsonSerializer.Serialize(new{status=passed?"passed":"failed",checks,failure},_json));}
    }
}
