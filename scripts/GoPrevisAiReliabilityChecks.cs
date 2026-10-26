using Godot;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;

namespace GoPrevis;

public partial class GoPrevisApp
{
    async Task RunAiReliabilityChecksAndQuit()
    {try{await RunAiReliabilityChecks();GetTree().Quit(0);}catch(Exception error){GD.PushError("AI_RELIABILITY_FAILED "+error);GetTree().Quit(1);}}
    static JsonObject FixtureTool(string name,JsonNode args,int output=100)=>new(){["choices"]=new JsonArray(new JsonObject{["message"]=new JsonObject{["role"]="assistant",["tool_calls"]=new JsonArray(new JsonObject{["id"]="call-"+Guid.NewGuid().ToString("N"),["type"]="function",["function"]=new JsonObject{["name"]=name,["arguments"]=args.ToJsonString()}})}}),["usage"]=new JsonObject{["prompt_tokens"]=100,["completion_tokens"]=output,["cost"]=.001m}};
    async Task RunAiReferenceChecksAndQuit()
    {try{await RunAiReferenceLiveChecks();GetTree().Quit(0);}catch(Exception error){GD.PushError("AI_REFERENCE_LIVE_FAILED "+error);GetTree().Quit(1);}}
    public async Task RunAiReferenceLiveChecks()
    {
        string folder=ProjectSettings.GlobalizePath("res://output/ai-reference-live/run-"+Guid.NewGuid().ToString("N")[..10]);Directory.CreateDirectory(folder);int checks=0;bool passed=false;string? failure=null;
        void Check(bool value,string message){Require(value,"Live AI reference: "+message);checks++;GD.Print("PASS Live AI reference: "+message);}
        try
        {
            OpenAiDirector();for(int i=0;i<5;i++)await ToSignal(GetTree(),SceneTree.SignalName.ProcessFrame);OpenAiReferences();for(int i=0;i<5;i++)await ToSignal(GetTree(),SceneTree.SignalName.ProcessFrame);
            _aiReferences.Clear();_aiSketch.Clear();_aiSketch.Strokes.Add(new(){new(.18f,.56f),new(.33f,.56f),new(.33f,.77f),new(.18f,.77f),new(.18f,.56f)});
            var circle=new List<Vector2>();for(int i=0;i<=48;i++){float angle=Mathf.Tau*i/48;circle.Add(new Vector2(.75f,.28f)+new Vector2(Mathf.Cos(angle),Mathf.Sin(angle))*.065f);}_aiSketch.Strokes.Add(circle);_aiSketch.QueueRedraw();
            _aiSketchNotes.Text="Rectangle = car. Circle = tree. Reproduce their spatial arrangement; use a metre-based scene.";await AddAiSketch();File.WriteAllBytes(Path.Combine(folder,"input-floor-plan.png"),Convert.FromBase64String(_aiReferences.Single().DataUrl.Split(',')[1]));
            for(int i=0;i<4;i++)await ToSignal(GetTree(),SceneTree.SignalName.ProcessFrame);GetViewport().GetTexture().GetImage().SavePng(Path.Combine(folder,"drawing-ui.png"));_aiReferencesDialog!.Hide();
            AiSettings.Selection=4;_aiModelChoice.Select(4);RefreshAiConnection();_aiModeChoice.Select(1);
            _aiPrompt.Text="Build a four-second static shot from my attached hand-drawn top-view plan. There are exactly two objects: one car and one tree. Infer their relative ground positions from the drawing; the notes explain its symbols. Use the built-in car and tree, one static camera showing both clearly. Use normal world-scale dimensions, inspect your rendered result, and correct it if it does not match the plan.";
            _aiLastResult=null;await BuildAiShot();var result=_aiLastResult??throw new InvalidOperationException(_aiResult.Text);var car=_entries.Single(e=>e.Data.Kind=="car");var tree=_entries.Single(e=>e.Data.Kind=="tree");
            Check(_entries.Count==2&&car.Node.Position.X<tree.Node.Position.X-1,"Sol places the drawn car left of the tree without a textual left/right instruction");
            Check(car.Node.Position.Z>tree.Node.Position.Z+1,"Sol interprets the top-view vertical direction as ground depth, not world height");
            Check(result.Actions.Contains("assess_preview"),"Sol checks actual rendered composition before applying the plan-derived scene");
            Check(_aiSession!.References.Count==1,"the real model session retains the input sketch for Resume");
            SaveTo(Path.Combine(folder,"from-floor-plan"));File.WriteAllText(Path.Combine(folder,"usage.json"),JsonSerializer.Serialize(new{model=AiSettings.Model,result.Requests,result.InputTokens,result.OutputTokens,result.EstimatedUsd,result.Actions,result.VerifiedOperations},_json));
            File.WriteAllText(Path.Combine(folder,"plan.json"),JsonSerializer.Serialize(result.Plan,AiDirectorClient.Json));_aiDialog!.Hide();
            var exported=await ExportShotAsync(new(){OutputDirectory=Path.Combine(folder,"shot-preview"),Width=960,Height=540,Fps=12,EndSeconds=4,IncludeColor=true,IncludeDepth=false,IncludePose=false,ExportMp4=true,ExportPngSequence=true});Check(exported.FrameCount==48,"the scene inferred from the drawing exports through the real active camera");
            passed=true;GD.Print("AI_REFERENCE_LIVE_OK checks="+checks+" output="+folder);
        }
        catch(Exception error){failure=error.ToString();throw;}
        finally{File.WriteAllText(Path.Combine(folder,"report.json"),JsonSerializer.Serialize(new{status=passed?"passed":"failed",checks,failure},_json));}
    }
    public async Task RunAiReliabilityChecks()
    {
        var original=Snapshot();string project=_projectPath;double time=_time;var undo=_undo.ToArray();var redo=_redo.ToArray();bool previousReplace=_aiReplaceScene;int checks=0;bool passed=false;string? failure=null;var observations=new List<object>();
        string folder=ProjectSettings.GlobalizePath("res://output/ai-reliability-checks/run-"+Guid.NewGuid().ToString("N")[..10]);Directory.CreateDirectory(folder);
        void Check(bool value,string message){Require(value,"AI reliability: "+message);checks++;observations.Add(new{message,passed=value});GD.Print("PASS AI reliability: "+message);}
        async Task Settle(){for(int i=0;i<5;i++)await ToSignal(GetTree(),SceneTree.SignalName.ProcessFrame);}
        try
        {
            _aiReplaceScene=true;var plan=AiFixturePlan();var connection=new AiDirectorConnection("openrouter","openai/gpt-6.1-sol","fixture-only-not-a-real-key");
            Task<string> Read(string name,JsonObject args)=>Task.FromResult(name=="inspect_scene_plan"?"{\"valid\":true,\"operations\":[\"fixture checked\"]}":"{}");
            var largeHandler=new AiFixtureHandler{Reply=(request,body,round)=>
            {
                Check(body["max_tokens"]!.GetValue<int>()==128000,"Sol requests its provider-advertised output capacity instead of the old 14000-token ceiling");
                return round==1?FixtureTool("get_scene",new JsonObject(),30000):round==2?FixtureTool("inspect_scene_plan",JsonSerializer.SerializeToNode(plan,AiDirectorClient.Json)!,30000):FixtureTool("apply_scene_plan",new JsonObject{["planId"]="review-1"},30000);
            }};
            using(var client=new AiDirectorClient(largeHandler)){var result=await client.GenerateAsync(connection,"Complete the whole shot",AiSceneContext(true),Read,p=>{_=CompileAiPlan(p,true);},_=>{},CancellationToken.None);Check(result.OutputTokens==90000,"planning continues past 45000 total output tokens and completes successfully");}
            var session=new AiGenerationSession();string saved=Path.Combine(folder,"session.json");int paidResponses=0;using var pause=new CancellationTokenSource();
            JsonObject ResumeReply(System.Net.Http.HttpRequestMessage request,JsonObject body,int round)
            {paidResponses++;return paidResponses==1?FixtureTool("get_scene",new JsonObject()):paidResponses==2?FixtureTool("inspect_scene_plan",JsonSerializer.SerializeToNode(plan,AiDirectorClient.Json)!):FixtureTool("apply_scene_plan",new JsonObject{["planId"]="review-1"});}
            bool cancelled=false;
            using(var client=new AiDirectorClient(new AiFixtureHandler{Reply=ResumeReply}))
            {try{await client.GenerateAsync(connection,"Resume this same task",AiSceneContext(true),(name,args)=>{pause.Cancel();return Read(name,args);},p=>{_=CompileAiPlan(p,true);},_=>{},pause.Token,session:session,checkpoint:state=>state.Save(saved));}catch(OperationCanceledException){cancelled=true;}}
            Check(cancelled&&File.Exists(saved)&&session.Requests==1,"a paused request saves its paid response, tool progress and usage");
            var restored=AiGenerationSession.Load(saved)!;using(var client=new AiDirectorClient(new AiFixtureHandler{Reply=ResumeReply}))
            {var result=await client.GenerateAsync(connection,"Resume this same task",AiSceneContext(true),Read,p=>{_=CompileAiPlan(p,true);},_=>{},CancellationToken.None,session:restored,checkpoint:state=>state.Save(saved));Check(result.Requests==3&&paidResponses==3,"resuming reuses the paid first response instead of restarting and charging for it again");}
            Check(!File.ReadAllText(saved).Contains(connection.Key,StringComparison.Ordinal),"saved sessions contain no provider API key");
            var truncated=new AiFixtureHandler{Reply=(request,body,round)=>round==1?new JsonObject{["choices"]=new JsonArray(new JsonObject{["finish_reason"]="length",["message"]=new JsonObject{["role"]="assistant",["tool_calls"]=new JsonArray(new JsonObject{["id"]="partial",["function"]=new JsonObject{["name"]="stage_scene_changes",["arguments"]="{incomplete"}})}}),["usage"]=new JsonObject{["completion_tokens"]=128000,["prompt_tokens"]=100}}:round==2?FixtureTool("inspect_scene_plan",JsonSerializer.SerializeToNode(plan,AiDirectorClient.Json)!):FixtureTool("apply_scene_plan",new JsonObject{["planId"]="review-1"})};
            var truncatedSession=new AiGenerationSession();using(var client=new AiDirectorClient(truncated)){var result=await client.GenerateAsync(connection,"Continue after provider truncation",AiSceneContext(true),Read,p=>{_=CompileAiPlan(p,true);},_=>{},CancellationToken.None,session:truncatedSession);Check(result.Requests==3&&truncatedSession.IncompleteResponses.Count==1,"provider-native truncation retains partial output and continues without parsing an incomplete tool call");}
            var draft=new AiGenerationSession();var section=new AiScenePlan{Summary="Street section",Objects=Enumerable.Range(0,150).Select(i=>new AiObject{Id="block-"+i,AssetId="box",Position=new[]{i*1f,0f,0f}}).ToList()};draft.Stage(section);draft.Stage(new(){Objects=new(){new(){Id="block-0",AssetId="box",Color=new[]{1f,0f,0f}}}});
            Check(draft.Draft!.Objects.Count==150&&draft.Draft.Objects.Single(o=>o.Id=="block-0").Color.Length==3,"large street drafts stage more than 100 objects and update by stable ID without duplicating them");
            Check(CompileAiPlan(draft.Draft,true).Objects.Count==150,"the street-sized draft compiles within the actual project capacity");
            var aim=AiSemanticFixture();aim.Cameras[0].Intent="Path";aim.Cameras[0].Movement=new(){Mode="Path",Duration=8,PathTiming="Smooth authored",AimPathTiming="Smooth authored",Path=new(){new(){Time=0,Position=new[]{0f,2f,8f}},new(){Time=2,Position=new[]{0f,2f,8f}},new(){Time=8,Position=new[]{0f,3f,3f}}},AimPath=new(){new(){Time=0,Position=new[]{0f,1f,-5f}},new(){Time=2,Position=new[]{-4f,1f,0f}},new(){Time=8,Position=new[]{0f,.5f,0f}}}};
            await ApplyAiPlan(aim,true);var camera=_cameras.Single();SetTime(0);var position=camera.Rig.GlobalPosition;var rotation=camera.Rig.Quaternion;SetTime(2);
            Check(camera.Rig.GlobalPosition.DistanceTo(position)<.001f&&Math.Abs(rotation.Dot(camera.Rig.Quaternion))<.99,"camera can pan independently while its own position path remains held");SetTime(6);Check(camera.Rig.GlobalPosition.DistanceTo(position)>1&&camera.Movement.AimPathEnabled,"camera flight and aim follow independent time curves");
            double boundary=2,step=.002;var a=CameraBasePosition(camera,boundary-step);var b=CameraBasePosition(camera,boundary);var c=CameraBasePosition(camera,boundary+step);Check((b-a).Length()/step<.02&&(c-b).Length()/step<.02,"smooth-authored timing starts the flight continuously from the initial hold");
            var oldPosition=CameraBasePosition(camera,6);var oldAim=CameraAimCenter(camera,6);SelectCamera(camera);RefreshCameraMovementInspector();_movementStart.Value=1;_movementDuration.Value=10;
            Check(CameraBasePosition(camera,8.5).DistanceTo(oldPosition)<.001&&CameraAimCenter(camera,8.5).DistanceTo(oldAim)<.001,"editing camera start/duration retimes both independent curves together");
            var husky=ContentCatalog.Find("animal-husky")!;var linkPlan=new AiScenePlan{Summary="Dog leash",Objects=new(){new(){Id="leash-dog",AssetId=husky.Id,Position=new[]{1f,0f,0f}},new(){Id="live-leash",AssetId="cylinder",Color=new[]{.1f,.1f,.1f},Link=new(){StartTargetId="forward-walker",EndTargetId="leash-dog",StartBone="RightHand",EndBone="Head",EndOffset=new[]{0f,.1f,0f}}}}};
            await ApplyAiPlan(linkPlan,false);SetTime(3);var leash=_entries.Single(e=>e.Data.Id=="live-leash");var walker=_entries.Single(e=>e.Data.Id=="forward-walker");var human=(RiggedActor)walker.Node;var hand=human.GlobalTransform*human.GetPoseJointPosition("RightHand");
            Check(leash.Node.GlobalPosition.DistanceTo(hand)<.001,"the leash endpoint follows the actual animated hand instead of a guessed static position");
            string document=Path.Combine(folder,"independent-camera-and-leash");SaveTo(document);LoadProjectFromPath(document+".goprevis");Check(Snapshot().Version==13&&_cameras.Single().Movement.AimPathEnabled&&_entries.Any(e=>e.Data.Link!=null),"independent aim paths and linked lines survive a real project roundtrip");
            OpenAiDirector();await Settle();OpenAiReferences();await Settle();_aiSketch.Strokes.Add(new(){new Vector2(.1f,.2f),new Vector2(.8f,.2f),new Vector2(.8f,.7f)});_aiSketchNotes.Text="Four-lane road; shops on both sides";await AddAiSketch();
            Check(_aiReferences.Count>0&&_aiReferences[^1].Caption.Contains("Top view",StringComparison.Ordinal)&&_aiReferences[^1].DataUrl.StartsWith("data:image/png;base64,",StringComparison.Ordinal),"the real sketch UI creates a floor-plan image and explanatory caption for the model");
            Check(_aiReferencesDialog!.Size.Y<750,"reference drawing controls fit a compact desktop window");_aiSketch.QueueRedraw();await Settle();
            File.WriteAllBytes(Path.Combine(folder,"floor-plan-sketch.png"),Convert.FromBase64String(_aiReferences[^1].DataUrl.Split(',')[1]));
            var imageSession=new AiGenerationSession();using(var client=new AiDirectorClient(new AiFixtureHandler{Reply=(request,body,round)=>round==1?FixtureTool("inspect_scene_plan",JsonSerializer.SerializeToNode(plan,AiDirectorClient.Json)!):FixtureTool("apply_scene_plan",new JsonObject{["planId"]="review-1"})}))
            {await client.GenerateAsync(connection,"Use my floor plan",AiSceneContext(true),Read,p=>{_=CompileAiPlan(p,true);},_=>{},CancellationToken.None,session:imageSession,references:_aiReferences);}
            Check(imageSession.Chat[1]!["content"]!.AsArray().Any(p=>p?["type"]?.GetValue<string>()=="image_url")&&imageSession.Responses[0]!["content"]!.AsArray().Any(p=>p?["type"]?.GetValue<string>()=="input_image")&&imageSession.Gemini[0]!["parts"]!.AsArray().Any(p=>p?["inlineData"]!=null),"uploaded or drawn scene references accompany the initial prompt in all three provider formats");
            Check(imageSession.References.Count==_aiReferences.Count,"the drawing remains attached to the resumable generation session");
            GetViewport().GetTexture().GetImage().SavePng(Path.Combine(folder,"references-ui.png"));_aiReferencesDialog.Hide();_aiDialog!.Hide();
            passed=true;GD.Print("AI_RELIABILITY_OK checks="+checks+" output="+folder);
        }
        catch(Exception error){failure=error.ToString();throw;}
        finally
        { _aiReferencesDialog?.Hide();_aiDialog?.Hide();File.WriteAllText(Path.Combine(folder,"report.json"),JsonSerializer.Serialize(new{status=passed?"passed":"failed",checks,failure,observations},_json));Restore(original,"");_projectPath=project;SetTime(time);_aiReplaceScene=previousReplace;_undo.Clear();_redo.Clear();foreach(var item in undo.Reverse())_undo.Push(item);foreach(var item in redo.Reverse())_redo.Push(item);}
    }
}
