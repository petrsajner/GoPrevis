using Godot;
using System;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;

namespace GoPrevis;

public partial class GoPrevisApp
{
    public async Task<int> RunRenameChecks()
    {
        var original=Snapshot();string project=_projectPath;double time=_time;int checks=0;
        string folder=Path.GetFullPath(ProjectSettings.GlobalizePath("res://output/rename-checks"));Directory.CreateDirectory(folder);
        void Check(bool condition,string name){Require(condition,"Rename: "+name);checks++;}
        try
        {
            Check(ProjectSettings.GetSetting("application/config/name").AsString()=="GoPrevis","project identity is GoPrevis");
            Check(typeof(GoPrevisApp).Assembly.GetName().Name=="GoPrevis","the executable loads the renamed managed assembly");
            Check(Descendants(this).OfType<Label>().Any(label=>label.Text=="GOPREVIS "+AppVersion&&label.IsVisibleInTree()),"the English application header shows the new name and version");
            Check(GetWindow().Title.StartsWith("GoPrevis ",StringComparison.Ordinal),"the native window title has the new name");
            string modern=Path.Combine(folder,"new-shot");SaveTo(modern);modern+=".goprevis";
            Check(File.Exists(modern),"new projects default to the .goprevis extension");
            LoadProjectFromPath(modern);Check(_entries.Count==original.Objects.Count,"a new project retains its complete scene");
            string legacy=Path.Combine(folder,"legacy-shot.previs");File.Copy(modern,legacy,true);LoadProjectFromPath(legacy);
            Check(_entries.Count==original.Objects.Count&&_cameras.Count==original.Cameras.Count,"legacy .previs project filenames remain supported");
            string previous=Path.Combine(folder,"legacy-genprevis.genprevis");File.Copy(modern,previous,true);LoadProjectFromPath(previous);SaveTo(previous);
            Check(File.Exists(previous)&&_entries.Count==original.Objects.Count,"GenPrevis project filenames open and retain their explicit extension when saved");
            string scope=Guid.NewGuid().ToString("N");string oldRoot=Path.Combine(folder,"old-user-"+scope),newRoot=Path.Combine(folder,"new-user-"+scope);
            Directory.CreateDirectory(Path.Combine(oldRoot,"library/poses"));Directory.CreateDirectory(Path.Combine(oldRoot,"settings"));Directory.CreateDirectory(Path.Combine(newRoot,"settings"));
            File.WriteAllText(Path.Combine(oldRoot,"library/poses/custom.json"),"saved pose bytes");File.WriteAllText(Path.Combine(oldRoot,"settings/file-locations.json"),"old preference");
            File.WriteAllText(Path.Combine(newRoot,"settings/file-locations.json"),"current preference");
            int copied=ImportLegacyUserData(oldRoot,newRoot);
            Check(copied==1&&File.ReadAllText(Path.Combine(newRoot,"library/poses/custom.json"))=="saved pose bytes","legacy user poses are copied into the new profile");
            Check(File.ReadAllText(Path.Combine(newRoot,"settings/file-locations.json"))=="current preference","existing GoPrevis settings are never overwritten");
            Check(File.ReadAllText(Path.Combine(oldRoot,"library/poses/custom.json"))=="saved pose bytes","the original Previs user profile remains unchanged");
            File.Delete(Path.Combine(newRoot,"library/poses/custom.json"));Check(ImportLegacyUserData(oldRoot,newRoot)==0&&!File.Exists(Path.Combine(newRoot,"library/poses/custom.json")),"one-time import does not restore deliberately deleted poses on later starts");
            var icon=GD.Load<Texture2D>("res://assets/goprevis.png");using(var image=icon.GetImage())Check(image.GetWidth()==image.GetHeight()&&image.GetPixel(0,0).A>.999f,"the approved square mannequin icon has an opaque grid background");
            if(DisplayServer.GetName()!="headless")
            {
                SetTime(0);string frame=Path.Combine(folder,"frame.png");await RenderFrame(frame);
                using var data=JsonDocument.Parse(File.ReadAllText(Path.ChangeExtension(frame,".json")));
                Check(data.RootElement.GetProperty("application").GetString()=="GoPrevis","exported shot metadata uses the new product name");
                await ToSignal(RenderingServer.Singleton,RenderingServer.SignalName.FramePostDraw);GetViewport().GetTexture().GetImage().SavePng(Path.Combine(folder,"goprevis-ui.png"));
            }
            File.WriteAllText(Path.Combine(folder,"report.json"),JsonSerializer.Serialize(new{status="passed",checks,version=AppVersion,product="GoPrevis"},_json));
            GD.Print("RENAME_OK checks="+checks);return checks;
        }
        finally{Restore(original,"");_projectPath=project;SetTime(time);}
    }
    async Task RunRenameChecksAndQuit(){try{await RunRenameChecks();GetTree().Quit(0);}catch(Exception ex){GD.PushError("RENAME_FAILED "+ex);GetTree().Quit(1);}}
}
