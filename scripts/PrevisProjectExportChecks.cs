using Godot;
using System;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text.Json;
using System.Threading.Tasks;

namespace Previs;

public partial class PrevisApp
{
    public async Task<int> RunProjectExportChecks()
    {
        if(DisplayServer.GetName()=="headless")return 0;
        var original=Snapshot();double time=_time;string project=_projectPath;bool playing=_playing;
        var undo=_undo.ToArray();var redo=_redo.ToArray();int checks=0;
        string output=ProjectSettings.GlobalizePath("res://output/project-export-checks");Directory.CreateDirectory(output);
        void Check(bool condition,string message){Require(condition,"Project export: "+message);checks++;}
        try
        {
            _playing=false;SetTime(0);ChangeProjectFps(15);
            string reference=Path.Combine(output,"reference.png");using(var image=Image.CreateEmpty(32,32,false,Image.Format.Rgb8)){image.Fill(new Color(1,0,1));image.SavePng(reference);}
            ApplyProjectSetup(new ProjectSetupData{Width=288,Height=512});ApplyActiveCameraReference(new CameraReferenceData{Path=reference,Opacity=1});await _referenceLoadTask;
            Check(_cameraReferenceTexture!=null,"a camera reference is loaded during the portrait video export");
            string baseline=Path.Combine(output,"clean.png");await RenderFrame(baseline);
            var settings=new ShotExportSettings{OutputDirectory=Path.Combine(output,"portrait-"+Guid.NewGuid().ToString("N")[..8]),Width=ProjectResolution.X,Height=ProjectResolution.Y,Fps=_fps,EndSeconds=.2,IncludeColor=true,IncludeDepth=true,IncludePose=true,IncludeNativeRigs=true,ExportMp4=true,ExportPngSequence=true};
            var result=await ExportShotAsync(settings);
            Check(result.FrameCount==3&&result.Videos.Count==3,"15 fps samples three frames over an end-exclusive 0.2-second shot");
            Check(result.Videos.Values.All(video=>video.Width==288&&video.Height==512&&video.FrameCount==3&&Math.Abs(video.Fps-15)<.001),"FFprobe verifies 9:16 dimensions and 15 fps for Color, Depth and Pose MP4s");
            using var clean=Image.LoadFromFile(baseline);using var exported=Image.LoadFromFile(Path.Combine(result.Directory,"color","000000.png"));clean.Convert(Image.Format.Rgb8);exported.Convert(Image.Format.Rgb8);
            Check(SHA256.HashData(clean.GetData()).SequenceEqual(SHA256.HashData(exported.GetData())),"the portrait whole-shot color pass excludes reference pixels and matches a clean still");
            File.WriteAllText(Path.Combine(output,"report.json"),JsonSerializer.Serialize(new{status="passed",checks,width=288,height=512,fps=15,frameCount=result.FrameCount,videos=result.Videos,manifest=result.ManifestPath},_json));
            GD.Print("PROJECT_EXPORT_OK checks="+checks);return checks;
        }
        finally
        {
            Restore(original,"");_projectPath=project;SetTime(time);_playing=playing;
            _undo.Clear();_redo.Clear();foreach(var item in undo.Reverse())_undo.Push(item);foreach(var item in redo.Reverse())_redo.Push(item);RefreshHistoryControls();
        }
    }
}
