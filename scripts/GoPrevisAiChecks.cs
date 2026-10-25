using Godot;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;

namespace GoPrevis;

public partial class GoPrevisApp
{
    async Task RunAiChecksAndQuit(bool live)
    {
        try{if(live)await RunAiLiveChecks();else await RunAiChecks();GetTree().Quit(0);}
        catch(Exception error){GD.PushError("AI_DIRECTOR_FAILED "+error);GetTree().Quit(1);}
    }
    sealed class AiFixtureHandler:HttpMessageHandler
    {
        internal Func<HttpRequestMessage,JsonObject,int,JsonObject> Reply=null!;
        internal int Requests;
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,CancellationToken cancellation)
        {
            var body=JsonNode.Parse(await request.Content!.ReadAsStringAsync(cancellation))!.AsObject();
            return new(HttpStatusCode.OK){Content=new StringContent(Reply(request,body,++Requests).ToJsonString(),Encoding.UTF8,"application/json")};
        }
    }
    static AiScenePlan AiFixturePlan()=>new(){Summary="A car and a walking person in a park, with live camera tracking.",Duration=8,Fps=25,Objects=new(){
        new(){Id="ai-car",AssetId="car",Name="Blue car",Color=new[]{.1f,.3f,.8f},ReplacePath=true,Path=new(){new(){Time=0,Position=new[]{0f,0f,3f},Easing="Stop"},new(){Time=1,Position=new[]{0f,0f,3f},Easing="Ease in/out"},new(){Time=5,Position=new[]{3f,0f,0f},Easing="Stop"},new(){Time=7,Position=new[]{5f,0f,-1f}}}},
        new(){Id="ai-walker",AssetId="human-average",Name="Walking person",ReplaceMotions=true,Motions=new(){new(){MotionId="human-walk-gesture",Start=0,Duration=8,Loop=true,InPlace=true}},ReplacePath=true,Path=new(){new(){Time=0,Position=new[]{-3f,0f,1f}},new(){Time=8,Position=new[]{1f,0f,-2f}}},PoseKeys=new(){new(){Bone="Head",Time=0,Value=new float[3]},new(){Bone="Head",Time=4,Value=new[]{0f,35f,0f}}}},
        new(){Id="ai-chair",AssetId="chair",Name="Chair",Position=new[]{-2f,0f,-2f}},
        new(){Id="ai-seated",AssetId="human-average",Name="Seated person",PoseId="pose-sit-upright",PlaceOnId="ai-chair"}},
        Cameras=new(){new(){Id="ai-follow",Name="Car follow",TargetId="ai-car",Follow=true,LookAt=true,RelativeOffsets=false,FollowOffset=new[]{4f,3f,8f},LookAtOffset=new[]{0f,.8f,0f}},new(){Id="ai-wide",Name="Overview",Position=new[]{10f,9f,13f},LookAtPoint=new[]{0f,0f,0f}}},ActiveCameraId="ai-follow"};

    public async Task RunAiChecks()
    {
        var original=Snapshot();string path=_projectPath;double time=_time;var undo=_undo.ToArray();var redo=_redo.ToArray();var editor=_editorCamera.Transform;
        int checks=0;bool passed=false;string? failure=null;var observations=new List<object>();string folder=ProjectSettings.GlobalizePath("res://output/ai-checks/run-"+Guid.NewGuid().ToString("N")[..10]);Directory.CreateDirectory(folder);
        void Check(bool value,string message){Require(value,"AI Director: "+message);checks++;observations.Add(new{message,passed=value});GD.Print("PASS AI Director: "+message);}
        async Task Settle(){for(int i=0;i<5;i++)await ToSignal(GetTree(),SceneTree.SignalName.ProcessFrame);}
        try
        {
            StopHistoryPlayback();StopPoseEditing();var plan=AiFixturePlan();string before=JsonSerializer.Serialize(Snapshot());var compiled=CompileAiPlan(plan,true);
            Check(JsonSerializer.Serialize(Snapshot())==before&&compiled.Objects.Count==4,"planning and asset validation leave the live scene unchanged");
            var invalid=AiClone(plan);invalid.Objects[0].Position=new[]{float.NaN,0f,0f};bool rejected=false;try{CompileAiPlan(invalid,true);}catch(ArgumentException){rejected=true;}Check(rejected&&JsonSerializer.Serialize(Snapshot())==before,"nonfinite spatial data is rejected before scene changes");
            invalid=AiClone(plan);invalid.Objects[0].Motions.Add(new(){MotionId="human-walk-gesture",Duration=3});rejected=false;try{CompileAiPlan(invalid,true);}catch(ArgumentException){rejected=true;}Check(rejected,"static objects cannot be assigned human mocap");
            invalid=AiClone(plan);invalid.Objects[0].Path[2].Time=.5;rejected=false;try{CompileAiPlan(invalid,true);}catch(ArgumentException){rejected=true;}Check(rejected,"invalid path arrival ordering is rejected");
            invalid=AiClone(plan);invalid.Cameras[0].TargetId="missing";rejected=false;try{CompileAiPlan(invalid,true);}catch(ArgumentException){rejected=true;}Check(rejected,"missing tracking targets are rejected");
            int history=_undo.Count;await ApplyAiPlan(plan,true);await Settle();
            Check(_entries.Count==4&&_cameras.Count==2&&_undo.Count==history+1,"a complete shot creates its objects and cameras in one Undo action");
            var car=_entries.Single(e=>e.Data.Id=="ai-car");var walker=_entries.Single(e=>e.Data.Id=="ai-walker");var seated=_entries.Single(e=>e.Data.Id=="ai-seated");var chair=_entries.Single(e=>e.Data.Id=="ai-chair");
            Check(PlacementSupport(seated,true).DistanceTo(new Vector3(chair.Node.Position.X,.47f,chair.Node.Position.Z))<.12f,"a library sitting pose is fitted to the actual chair seat");
            SetTime(.3);var parked=car.Node.Position;SetTime(.8);Check(car.Node.Position.DistanceTo(parked)<.001,"the car remains parked before its departure key");SetTime(5);Check(car.Node.Position.DistanceTo(parked)>2,"the car travels along the requested curve");SetTime(7);var stopped=car.Node.Position;SetTime(7.9);Check(car.Node.Position.DistanceTo(stopped)<.001,"the car holds its final stop through the shot end");
            SetTime(0);var source=((RiggedActor)walker.Node).CaptureEvaluatedPose();SetTime(3);var moved=((RiggedActor)walker.Node).CaptureEvaluatedPose();
            Check(walker.Data.MotionClips.Count==1&&walker.Node.Position.DistanceTo(new Vector3(-3,0,1))>1&&source.BoneRotations.Any(pair=>!pair.Value.SequenceEqual(moved.BoneRotations[pair.Key])),"actual skeletal motion and path travel run together");
            Check(walker.Data.PoseCorrections.Bones["Head"].Count==2,"head-turn corrections remain a separate additive layer above mocap");
            var follow=_cameras.Single(c=>c.Id=="ai-follow");Check(follow.Tracking.FollowEnabled&&follow.Tracking.LookAtEnabled&&ActiveCameraId==follow.Id,"the active camera uses live Follow and Look at bindings");
            var cameraBefore=follow.Rig.GlobalPosition;car.Data.Path.Points[2][0]+=2;SetTime(5);Check(follow.Rig.GlobalPosition.DistanceTo(cameraBefore)>1,"changing the car path updates the AI camera without rebaking");
            Undo();SetTime(time);File.WriteAllText(Path.Combine(folder,"before.json"),before);File.WriteAllText(Path.Combine(folder,"after-undo.json"),JsonSerializer.Serialize(Snapshot()));var expectedUndo=JsonSerializer.Deserialize<ProjectData>(before)!;expectedUndo.CameraKeys.Clear();var actualUndo=Snapshot();actualUndo.CameraKeys.Clear();Check(JsonSerializer.Serialize(actualUndo)==JsonSerializer.Serialize(expectedUndo),"one Undo restores every authoritative object and camera animation channel");Redo();Check(_entries.Any(e=>e.Data.Id=="ai-car")&&_cameras.Count==2,"Redo restores the complete AI shot");
            before=JsonSerializer.Serialize(Snapshot());history=_undo.Count;invalid=AiClone(plan);invalid.Objects[3].PoseKeys.Add(new(){Bone="missing",Time=1,Value=new float[3]});rejected=false;try{await ApplyAiPlan(invalid,true);}catch(ArgumentException){rejected=true;}Check(rejected&&JsonSerializer.Serialize(Snapshot())==before&&_undo.Count==history,"a rejected plan preserves the exact scene and history");
            using(var cancelled=new CancellationTokenSource()){cancelled.Cancel();rejected=false;try{await ApplyAiPlan(plan,true,cancelled.Token);}catch(OperationCanceledException){rejected=true;}Check(rejected&&JsonSerializer.Serialize(Snapshot())==before,"cancellation before commit leaves the scene untouched");}
            var dogModel=ContentCatalog.All.First(m=>m.Id=="animal-husky");var dogMotion=MotionCatalog.ForFamily(dogModel.ActorFamily).First(m=>m.SourceAnimation=="Gallop");
            var animalPlan=new AiScenePlan{Summary="Dog motion",Objects=new(){new(){Id="ai-dog",AssetId=dogModel.Id,Name="Dog",Motions=new(){new(){MotionId=dogMotion.Id,Duration=8,Loop=true,InPlace=true}}}}};
            await ApplyAiPlan(animalPlan,false);var dog=_entries.Single(e=>e.Data.Id=="ai-dog");SetTime(.1);var a=dog.NativeTrack!.CaptureEvaluatedPose("a");SetTime(.4);var b=dog.NativeTrack.CaptureEvaluatedPose("b");Check(a.BoneRotations.Any(pair=>!pair.Value.SequenceEqual(b.BoneRotations[pair.Key])),"a compatible animal motion deforms the real native rig");
            string saved=Path.Combine(folder,"ai-shot");SaveTo(saved);LoadProjectFromPath(saved+".goprevis");Check(_entries.Count==5&&_entries.Single(e=>e.Data.Id=="ai-walker").Data.PoseCorrections.Bones["Head"].Count==2,"AI shots save and reopen with sources, paths and correction keys");
            string dogs=await AiReadTool("search_library",new(){["kind"]="models",["query"]="dog",["family"]="dog"});Check(dogs.Contains("animal-husky",StringComparison.Ordinal)&&dogs.Contains("animal-shiba-inu",StringComparison.Ordinal),"generic dog searches resolve the actual rigged breeds and their family IDs");
            var fresh=JsonNode.Parse(AiSceneContext(true))!;Check(fresh["objects"]!.AsArray().Count==0&&fresh["cameras"]!.AsArray().Count==0,"new-scene planning sees a clean target rather than obsolete scene IDs");
            var edit=new AiScenePlan{Summary="Update car",Objects=new(){new(){Id="ai-car",Color=new[]{.8f,.2f,.1f},ReplaceKeys=true,Keys=new(){new(){Channel="Rotation",Time=2,Value=new[]{0f,10f,0f}}}}}};
            string humanBefore=JsonSerializer.Serialize(_entries.Single(e=>e.Data.Id=="ai-walker").Data);await ApplyAiPlan(edit,false);
            Check(JsonSerializer.Serialize(_entries.Single(e=>e.Data.Id=="ai-walker").Data)==humanBefore&&_entries.Count==5,"an edit preserves unrelated characters, motion clips, paths and manual pose keys");
            var clear=new AiScenePlan{Summary="Clear pose corrections",Objects=new(){new(){Id="ai-walker",ReplacePoseKeys=true}}};await ApplyAiPlan(clear,false);
            Check(_entries.Single(e=>e.Data.Id=="ai-walker").Data.PoseCorrections.Bones.Count==0&&_entries.Single(e=>e.Data.Id=="ai-walker").Data.MotionClips.Count==1,"AI can clear manual correction keys while preserving mocap");Undo();
            SetTime(0);before=JsonSerializer.Serialize(Snapshot());history=_undo.Count;
            var badNative=new AiScenePlan{Summary="Invalid native edit",Objects=new(){new(){Id="ai-dog",PoseKeys=new(){new(){Bone="nonexistent_native_bone",Time=1,Value=new float[3]}}}}};
            rejected=false;try{await ApplyAiPlan(badNative,false);}catch(ArgumentException){rejected=true;}
            Check(rejected&&JsonSerializer.Serialize(Snapshot())==before&&_undo.Count==history,"a runtime rig-validation failure rolls back already-created nodes and leaves history intact");

            foreach(string provider in new[]{"openrouter","openai","gemini"})
            {
                var handler=new AiFixtureHandler();
                handler.Reply=(request,body,round)=>
                {
                    Check(request.RequestUri!.Host==(provider=="gemini"?"generativelanguage.googleapis.com":provider=="openai"?"api.openai.com":"openrouter.ai"),provider+" uses its real provider endpoint");
                    Check(provider=="gemini"?request.Headers.Contains("x-goog-api-key"):request.Headers.Authorization?.Scheme=="Bearer",provider+" sends credentials only in request headers");
                    if(provider=="gemini")
                    {
                        if(round==2)Check(body["contents"]!.AsArray().Last()!["parts"]!.AsArray().Count==2,"Gemini groups parallel function results into one response turn");
                        return new(){["candidates"]=new JsonArray(new JsonObject{["content"]=new JsonObject{["role"]="model",["parts"]=round==1?new JsonArray(new JsonObject{["functionCall"]=new JsonObject{["name"]="get_scene",["id"]="read1",["args"]=new JsonObject()},["thoughtSignature"]="fixture-signature"},new JsonObject{["functionCall"]=new JsonObject{["name"]="search_library",["id"]="read2",["args"]=new JsonObject{["kind"]="motions",["query"]="walk",["family"]="human"}}}):new JsonArray(new JsonObject{["functionCall"]=new JsonObject{["name"]=round==2?"inspect_scene_plan":"apply_scene_plan",["id"]="apply",["args"]=round==2?JsonSerializer.SerializeToNode(plan,AiDirectorClient.Json):new JsonObject{["planId"]="review-1"}}})}}),["usageMetadata"]=new JsonObject{["promptTokenCount"]=100,["candidatesTokenCount"]=100,["thoughtsTokenCount"]=25}};
                    }
                    var arguments=round==1?"{}":round==2?JsonSerializer.Serialize(plan,AiDirectorClient.Json):"{\"planId\":\"review-1\"}";string tool=round==1?"get_scene":round==2?"inspect_scene_plan":"apply_scene_plan";
                    if(provider=="openai")return new(){["output"]=new JsonArray(new JsonObject{["type"]="function_call",["call_id"]="call"+round,["name"]=tool,["arguments"]=arguments}),["usage"]=new JsonObject{["input_tokens"]=100,["output_tokens"]=100}};
                    return new(){["choices"]=new JsonArray(new JsonObject{["message"]=new JsonObject{["role"]="assistant",["content"]=null,["tool_calls"]=new JsonArray(new JsonObject{["id"]="call"+round,["type"]="function",["function"]=new JsonObject{["name"]=tool,["arguments"]=arguments}})}}),["usage"]=new JsonObject{["prompt_tokens"]=100,["completion_tokens"]=100,["cost"]=.00006}};
                };
                using var service=new AiDirectorClient(handler);
                string model=provider=="openai"?"gpt-6-luna":provider=="gemini"?"gemini-3.8-flash":"openai/gpt-6-luna";
                var result=await service.GenerateAsync(new(provider,model,"fixture-only-not-a-real-key"),"Build a test shot",AiSceneContext(true),AiReadTool,p=>{_=CompileAiPlan(p,true);},_=>{},CancellationToken.None);
                Check(result.Requests==3&&result.Plan.Objects.Count==4&&result.InputTokens==300&&result.OutputTokens==(provider=="gemini"?375:300),provider+" completes the actual read-tool/plan loop and accounts for tokens");
            }
            int visualContracts=await RunAiVisualContractChecks();Check(visualContracts==12,"all providers send visual input and require explicit preview assessment before commit");
            OpenAiDirector();await Settle();Check(_aiDialog!.Visible&&_aiModelChoice.ItemCount==5&&_aiPrompt.IsVisibleInTree(),"the real English AI Director exposes presets, custom OpenRouter models and the shot description");
            GD.Print("AI_LAYOUT size="+_aiDialog.Size+" buttons="+_aiBuild.GetGlobalRect());Check(_aiDialog.Size.Y<=700&&_aiBuild.GetGlobalRect().End.Y<=_aiDialog.GetVisibleRect().Size.Y+2,"the actual AI window stays desktop sized with its Build and View buttons inside the window");
            OpenAiConnection();await Settle();Check(_aiConnectionDialog!.Visible&&_aiConnectionDialog.GetParent()==_aiDialog,"connection settings open as a correctly nested modal window");Check(_aiConnectionDialog.Size.Y<=450,"connection settings also fit a compact desktop dialog");_aiKeyInput.Text="temporary-test-input";_aiConnectionDialog.Hide();Check(_aiKeyInput.Text=="","closing connection settings clears the secret input without saving it");
            await Settle();if(DisplayServer.GetName()!="headless")GetViewport().GetTexture().GetImage().SavePng(Path.Combine(folder,"ai-director-ui.png"));_aiDialog.Hide();
            Check(!AiSceneContext(false).Contains("ModelPath",StringComparison.OrdinalIgnoreCase)&&!AiSceneContext(false).Contains("MotionPath",StringComparison.OrdinalIgnoreCase),"the model receives no local source-file paths or provider keys");
            int layout=await RunResponsiveLayoutChecks();Check(layout>=24,"AI Director keeps the existing responsive desktop layout");
            passed=true;GD.Print("AI_DIRECTOR_OK checks="+checks+" output="+folder);
        }
        catch(Exception error){failure=error.ToString();throw;}
        finally
        {
            _aiDialog?.Hide();File.WriteAllText(Path.Combine(folder,"report.json"),JsonSerializer.Serialize(new{status=passed?"passed":"failed",checks,failure,observations},_json));
            Restore(original,"");_projectPath=path;SetTime(time);_editorCamera.Transform=editor;_undo.Clear();_redo.Clear();foreach(var state in undo.Reverse())_undo.Push(state);foreach(var state in redo.Reverse())_redo.Push(state);
        }
    }

    public async Task RunAiLiveChecks()
    {
        string provider=OS.GetCmdlineUserArgs().Contains("--ai-provider=gemini")?"gemini":"openrouter";
        string folder=ProjectSettings.GlobalizePath("res://output/ai-live-"+provider+"/run-"+Guid.NewGuid().ToString("N")[..10]);Directory.CreateDirectory(folder);
        int checks=0;bool passed=false;string? failure=null;var observations=new List<object>();
        void Check(bool value,string message){Require(value,"AI live: "+message);checks++;observations.Add(new{message,passed=value});GD.Print("PASS AI live: "+message);}
        try
        {
            OpenAiDirector();AiSettings.UseVividia=true;AiSettings.Selection=provider=="gemini"?2:0;_aiModelChoice.Select(AiSettings.Selection);RefreshAiConnection();_aiModeChoice.Select(1);
            Check(AiCredentials.Has(provider,true),"the selected provider key is read without displaying or copying it");
            _aiPrompt.Text=provider=="gemini"?"Create a simple 8 second park shot with a dog, a standing human and two trees. The dog runs along a curved path and stops near the person. Use a compatible native dog running motion, followed by idle. A camera follows the dog and looks at it for the entire shot. Use only four objects and one camera. Search library IDs first.":"Vytvoř jednoduchý 8sekundový záběr: modré auto se rozjede, zatočí doleva po dráze a v 7. sekundě zastaví. Vedle silnice jde jedna postava po samostatné dráze s cyklickým mocapem chůze a ve 4. sekundě otočí hlavu pomocí korekčních klíčů. Přidej dva stromy. Kamera 35 mm celou dobu následuje auto a dívá se na něj. Přidej i druhou statickou přehledovou kameru. Použij základní car, human-average a tree a nanejvýš čtyři objekty. Vyhledej skutečné motion IDs.";
            int history=_undo.Count;_aiLastResult=null;await BuildAiShot();var result=_aiLastResult??throw new InvalidOperationException(_aiResult.Text);
            var allResults=new List<AiDirectorResult>{result};
            Check(result.Actions.Contains("apply_scene_plan")&&_entries.Count>=3&&_cameras.Count>=1,"the live model creates actual scene objects and cameras");
            Check(_undo.Count==history+1,"the complete live request is one Undo action");
            var moving=_entries.First(e=>provider=="gemini"?e.Data.ActorFamily!=""&&e.Data.ActorFamily!="human":e.Data.Kind=="car");
            Check(moving.Data.Path.Enabled&&moving.Data.Path.Points.Count>=2,"the live model creates an editable travel path");
            var camera=_cameras.Single(c=>c.Id==ActiveCameraId);Check(camera.Tracking.FollowEnabled&&camera.Tracking.LookAtEnabled&&camera.Tracking.FollowTargetId==moving.Data.Id,"the live model binds the active camera to the moving subject");
            SetTime(0);var first=moving.Node.Position;var cameraFirst=camera.Rig.GlobalPosition;SetTime(5);Check(moving.Node.Position.DistanceTo(first)>.5f&&camera.Rig.GlobalPosition.DistanceTo(cameraFirst)>.5f,"the generated subject and live camera really move during playback");
            if(provider=="openrouter")
            {
                var human=_entries.First(e=>e.Node is RiggedActor);Check(human.Data.MotionClips.Count>0&&human.Data.PoseCorrections.Bones.Count>0,"live Luna combines mocap with additive human pose correction keys");
                string objectsBefore=JsonSerializer.Serialize(_entries.Select(SnapshotObject));var offset=Vector(camera.Tracking.FollowOffset);string target=camera.Tracking.FollowTargetId;
                _aiPrompt.Text="Only edit the active car-follow camera: increase the distance behind the car by 30 percent and raise it by one metre. Preserve every object ID, model, motion clip, path, correction key and other camera. Do not add or delete any objects. Keep Follow and Look at bound to the same car.";_aiLastResult=null;await BuildAiShot();var edited=_aiLastResult??throw new InvalidOperationException(_aiResult.Text);allResults.Add(edited);
                Check(JsonSerializer.Serialize(_entries.Select(SnapshotObject))==objectsBefore,"a live follow-up camera request preserves all scene objects and their animation");
                camera=_cameras.Single(c=>c.Id==ActiveCameraId);Check(camera.Tracking.FollowTargetId==target&&Vector(camera.Tracking.FollowOffset).Length()>offset.Length(),"a live follow-up increases the camera distance while keeping its live subject binding");
                File.WriteAllText(Path.Combine(folder,"follow-up-plan.json"),JsonSerializer.Serialize(edited.Plan,AiDirectorClient.Json));
            }
            else Check(moving.NativeTrack!=null&&moving.Data.MotionClips.Count>=2,"live Gemini sequences compatible native animal run and idle clips");
            string example=Path.Combine(folder,"ai-shot");SaveTo(example);Check(File.Exists(example+".goprevis"),"the live AI scene saves as a normal portable project");
            File.WriteAllText(Path.Combine(folder,"plan.json"),JsonSerializer.Serialize(result.Plan,AiDirectorClient.Json));
            File.WriteAllText(Path.Combine(folder,"usage.json"),JsonSerializer.Serialize(new{provider,model=AiSettings.Model,Requests=allResults.Sum(r=>r.Requests),InputTokens=allResults.Sum(r=>r.InputTokens),OutputTokens=allResults.Sum(r=>r.OutputTokens),EstimatedUsd=allResults.All(r=>r.EstimatedUsd.HasValue)?allResults.Sum(r=>r.EstimatedUsd):null,Actions=allResults.SelectMany(r=>r.Actions).ToArray()},_json));
            if(DisplayServer.GetName()!="headless")
            {
                for(int i=0;i<4;i++)await ToSignal(GetTree(),SceneTree.SignalName.ProcessFrame);GetViewport().GetTexture().GetImage().SavePng(Path.Combine(folder,"ai-director-ui.png"));_aiDialog!.Hide();
                var exported=await ExportShotAsync(new(){OutputDirectory=Path.Combine(folder,"shot-preview"),Width=960,Height=540,Fps=12,StartSeconds=0,EndSeconds=Math.Min(8,_duration),IncludeColor=true,IncludeDepth=true,IncludePose=true,IncludeNativeRigs=true,ExportMp4=true,ExportPngSequence=true});
                Check(exported.FrameCount==96&&exported.Videos.Count==3,"the live generated shot renders all 96 color/depth/pose frames and three MP4 videos");
            }
            passed=true;GD.Print("AI_LIVE_OK provider="+provider+" checks="+checks+" requests="+result.Requests+" output="+folder);
        }
        catch(Exception error){failure=error.ToString();throw;}
        finally{File.WriteAllText(Path.Combine(folder,"report.json"),JsonSerializer.Serialize(new{status=passed?"passed":"failed",provider,checks,failure,observations},_json));}
    }
}
