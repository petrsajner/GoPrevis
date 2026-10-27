using Godot;
using System;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;

namespace GoPrevis;

/// <summary>
/// "→ Vividia" end to end on the built program (0.20.0): a Vividia on this computer is found, a short
/// shot is exported, the export's end offers the Vividia, and pressing it hands the folder over. The
/// receiving Vividia's import is checked on its side.
/// </summary>
public partial class GoPrevisApp
{
    async Task RunVividiaLinkChecksAndQuit()
    {try{await RunVividiaLinkChecks();GetTree().Quit(0);}catch(Exception e){GD.PushError("VIVIDIA_LINK_FAILED "+e);GetTree().Quit(1);}}
    async Task RunVividiaLinkChecks()
    {
        string root=ProjectSettings.GlobalizePath("res://output/vividia-link-checks/run-"+Guid.NewGuid().ToString("N")[..10]);Directory.CreateDirectory(root);int checks=0;
        void Check(bool yes,string message){Require(yes,"Vividia link: "+message);checks++;GD.Print("PASS Vividia link: "+message);}
        async Task Settle(int frames=3){for(int i=0;i<frames;i++)await ToSignal(GetTree(),SceneTree.SignalName.ProcessFrame);}
        await Settle();
        var apps=FindVividia();Check(apps.Count>0,"a Vividia on this computer says where it is (vividia-app.json)");
        var settings=new ShotExportSettings{OutputDirectory=Path.Combine(root,"Link test-"+Guid.NewGuid().ToString("N")[..6]),Width=640,Height=360,Fps=24,StartSeconds=0,EndSeconds=Math.Min(_duration,2),ExportMp4=true,ExportPngSequence=false,IncludeColor=true,IncludeDepth=true,IncludePose=true,IncludeNativeRigs=true,DepthNear=.05f,DepthFar=30};
        var result=await ExportShotAsync(settings);
        Check(File.Exists(Path.Combine(result.Directory,"color.mp4"))&&File.Exists(result.ManifestPath)&&File.ReadAllText(result.ManifestPath).Contains("\"shot\"",StringComparison.Ordinal),"a short shot is exported with its content in the manifest");
        ShowShotExported(result);await Settle();
        Check(_shotExportedButtons.Count==apps.Count&&_shotExportedButtons[0].Text=="→ "+apps[0].Product,"the export's end offers → "+apps[0].Product);
        _shotExportedButtons[0].EmitSignal(BaseButton.SignalName.Pressed);await Settle();
        Check(_status.Text.StartsWith("Shot sent to "+apps[0].Product,StringComparison.Ordinal),"→ "+apps[0].Product+" hands the export folder over");
        File.WriteAllText(Path.Combine(root,"report.json"),JsonSerializer.Serialize(new{status="passed",checks,exported=result.Directory,vividia=apps[0].Product},_json));
        GD.Print("VIVIDIA_LINK_OK checks="+checks+" exported="+result.Directory);
    }
}
