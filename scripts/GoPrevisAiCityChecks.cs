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
    const string CityShotBrief="""
        Postav mi scenu na silnici. Siroka silnice (4 pruhy). Obema smeri jedou auta. V krajnich pruzich je sem tam zaparkovane auto. Po chodnicich chodi lidi, nekteri vedou na voditku psa. Mezi silnici a chodnikem jsou stromy. Chodniky jsou mezi silnici a domy - v domech jsou obchody.
        Pred obchodem priblizne uprostrad nasi sceny stoji dvojice a koukaji se do obchodu.
        Postavime 15ti vterinovy zaber - kamera zacina pohledem z chodniku vpravo, otoci se doleva - podiva se pres silnici na druhy chodnik, kde stoji dvojice pred obchodem. Kamera se postupne vznese - preleti silnici a doleti ke dvojici pred obchodem. Klesne na uroven ramen, prileti az ke zdi obchodu a podiva se na tvare dvojice, ktera stoji pred obchodem. Zaber konci pomalym najezdem na tvare teto dvojice - dostaneme se az na polodetail
        """;
    async Task RunAiCityChecksAndQuit()
    {try{await RunAiCityChecks();GetTree().Quit(0);}catch(Exception error){GD.PushError("AI_CITY_FAILED "+error);GetTree().Quit(1);}}
    public async Task RunAiCityChecks()
    {
        string folder=ProjectSettings.GlobalizePath("res://output/ai-city-live/run-"+Guid.NewGuid().ToString("N")[..10]);Directory.CreateDirectory(folder);bool passed=false;int checks=0;string? failure=null;var observations=new List<object>();
        void Check(bool value,string message){observations.Add(new{message,passed=value});Require(value,"City AI: "+message);checks++;GD.Print("PASS City AI: "+message);}
        try
        {
            OpenAiDirector();AiSettings.Selection=4;AiSettings.UseVividia=true;_aiModelChoice.Select(4);RefreshAiConnection();_aiModeChoice.Select(1);_aiPrompt.Text=CityShotBrief;
            _aiLastResult=null;await BuildAiShot();var result=_aiLastResult??throw new InvalidOperationException(_aiResult.Text);
            Check(AiSettings.Model=="openai/gpt-6.1-sol"&&Math.Abs(_duration-15)<.001,"GPT-6.1 Sol / High builds the requested 15-second shot");
            Check(result.Actions.Contains("assess_preview"),"the agent visually evaluates actual renders before declaring the street complete");
            var cars=_entries.Where(e=>e.Data.Kind=="car"||ContentCatalog.Find(e.Data.LibraryModelId??"")?.Category=="Vehicles").ToArray();var driving=cars.Where(e=>e.Data.Path.Enabled).ToArray();
            Check(driving.Length>=4&&cars.Length-driving.Length>=2,"the street contains multiple moving cars and parked cars");
            var headings=driving.Select(e=>AiObjectPosition(e.Data,10)-AiObjectPosition(e.Data,0)).ToArray();Check(headings.Any(a=>headings.Any(b=>a.LengthSquared()>.1&&b.LengthSquared()>.1&&a.Normalized().Dot(b.Normalized())<-.7)),"traffic travels in both directions");
            Check(_entries.Count(e=>e.Node is RiggedActor&&e.Data.Path.Enabled)>=4,"pedestrians walk along the sidewalks");
            Check(_entries.Any(e=>e.NativeTrack!=null&&e.Data.Path.Enabled&&e.Data.MotionClips.Count>0)&&_entries.Any(e=>e.Data.Link!=null),"walking dogs have live leashes attached to scene targets");
            Check(_entries.Count(e=>e.Data.Name.Contains("shop",StringComparison.OrdinalIgnoreCase)||e.Data.Name.Contains("store",StringComparison.OrdinalIgnoreCase))>=2,"the scene includes identifiable shop fronts");
            var camera=_cameras.Single(c=>c.Id==ActiveCameraId);Check(camera.Movement.Mode=="Path"&&camera.Movement.AimPathEnabled,"camera flight and view direction use separate editable paths");
            SetTime(0);float startHeight=camera.Rig.GlobalPosition.Y,maxHeight=startHeight;for(int i=1;i<=60;i++){SetTime(_duration*i/60);maxHeight=Math.Max(maxHeight,camera.Rig.GlobalPosition.Y);}Check(maxHeight>startHeight+1.5f,"the camera rises above street level during the crossing");
            SetTime(_duration-1.0/_fps);var eye=camera.Rig.ShotCamera;var people=_entries.Where(e=>e.Node is RiggedActor&&!e.Data.Path.Enabled).Select(e=>new{entry=e,head=e.Node.GlobalTransform*((RiggedActor)e.Node).GetPoseJointPosition("Head")}).OrderBy(e=>e.head.DistanceSquaredTo(eye.GlobalPosition)).Take(2).ToArray();
            Check(people.Length==2&&people.All(person=>!eye.IsPositionBehind(person.head)),"the final camera sees both members of the stationary couple");
            Check(people.All(person=>((RiggedActor)person.entry.Node).VisibleFaceDirectionWorld().Dot((eye.GlobalPosition-person.head).Normalized())>.2f),"the ending camera is on the couple's face side");
            var projected=people.Select(person=>eye.UnprojectPosition(person.head)).ToArray();float width=_sceneViewport.Size.X;
            Check(projected.All(p=>p.X>=0&&p.X<=width&&p.Y>=0&&p.Y<=_sceneViewport.Size.Y)&&Math.Abs(projected[0].X-projected[1].X)>width*.1f,"both faces are framed at a useful final close distance");
            SaveTo(Path.Combine(folder,"city-street-15s"));File.WriteAllText(Path.Combine(folder,"plan.json"),JsonSerializer.Serialize(result.Plan,AiDirectorClient.Json));File.WriteAllText(Path.Combine(folder,"usage.json"),JsonSerializer.Serialize(new{model=AiSettings.Model,result.Requests,result.InputTokens,result.OutputTokens,result.EstimatedUsd,result.Actions,result.VerifiedOperations,objects=_entries.Count},_json));
            for(int i=0;i<5;i++)await ToSignal(GetTree(),SceneTree.SignalName.ProcessFrame);GetViewport().GetTexture().GetImage().SavePng(Path.Combine(folder,"ai-director-city.png"));_aiDialog!.Hide();
            var exported=await ExportShotAsync(new(){OutputDirectory=Path.Combine(folder,"shot-preview"),Width=1280,Height=720,Fps=24,EndSeconds=15,IncludeColor=true,IncludeDepth=true,IncludePose=true,IncludeNativeRigs=true,ExportMp4=true,ExportPngSequence=true});Check(exported.FrameCount==360&&exported.Videos.Count==3,"the complete street shot renders 360 color/depth/pose frames and three MP4 videos");
            passed=true;GD.Print("AI_CITY_OK checks="+checks+" objects="+_entries.Count+" output="+folder);
        }
        catch(Exception error){failure=error.ToString();throw;}
        finally{File.WriteAllText(Path.Combine(folder,"report.json"),JsonSerializer.Serialize(new{status=passed?"passed":"failed",checks,failure,observations},_json));}
    }
}
