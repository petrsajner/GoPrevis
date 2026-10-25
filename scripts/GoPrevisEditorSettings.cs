using Godot;
using System;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;

namespace GoPrevis;

public partial class GoPrevisApp
{
    const string CameraBackgroundDirectory="camera_background";
    EditorFileLocations? _editorFileLocations;
    EditorFileLocations FileLocations
    {
        get
        {
            bool diagnostic=OS.GetCmdlineUserArgs().Any(argument=>argument.EndsWith("-test",StringComparison.Ordinal)||argument=="--test-ui");
            return _editorFileLocations??=new EditorFileLocations(ProjectSettings.GlobalizePath(diagnostic?"res://output/test-settings/file-locations.json":"user://settings/file-locations.json"));
        }
    }
    void RestoreBackgroundDirectory(FileDialog dialog,CameraEntry camera)
    {
        string? fallback=string.IsNullOrEmpty(camera.Reference.Path)?null:Path.GetDirectoryName(camera.Reference.Path);
        if(string.IsNullOrEmpty(fallback))fallback=OS.GetSystemDir(OS.SystemDir.Documents);
        string directory=FileLocations.DirectoryFor(CameraBackgroundDirectory,fallback);
        if(Directory.Exists(directory))dialog.CurrentDir=directory;
    }
    void RememberBackgroundDirectory(string? directory)=>FileLocations.Remember(CameraBackgroundDirectory,directory);

    public async Task<int> RunEditorSettingsChecks()
    {
        var original=Snapshot();string project=_projectPath;double time=_time;
        var previous=_editorFileLocations;var undo=_undo.ToArray();var redo=_redo.ToArray();int checks=0;
        string folder=Path.GetFullPath(ProjectSettings.GlobalizePath("res://output/editor-settings-checks"));Directory.CreateDirectory(folder);
        string preferences=Path.Combine(folder,"locations-"+Guid.NewGuid().ToString("N")+".json");
        void Check(bool condition,string message){Require(condition,"Editor settings: "+message);checks++;}
        async Task Settle(){for(int i=0;i<3;i++)await ToSignal(GetTree(),SceneTree.SignalName.ProcessFrame);}
        try
        {
            _editorFileLocations=new EditorFileLocations(preferences);
            string first=Path.Combine(folder,"First background folder"),second=Path.Combine(folder,"Second background folder");Directory.CreateDirectory(first);Directory.CreateDirectory(second);
            string a=Path.GetFullPath(Path.Combine(first,"reference.png")),b=Path.GetFullPath(Path.Combine(second,"reference.png"));
            using(var image=Image.CreateEmpty(32,32,false,Image.Format.Rgb8)){image.Fill(new Color(.3f,.5f,.8f));image.SavePng(a);image.SavePng(b);}
            var camera=_cameras[0];SelectCamera(camera);OpenActiveCameraBackground();ChooseCameraBackground(camera,_cameraBackgroundDialog!.GetChild<Control>(0));
            var picker=_cameraBackgroundDialog.GetChildren().OfType<FileDialog>().Single();picker.EmitSignal(FileDialog.SignalName.FileSelected,a);await Settle();
            Check(FileLocations.DirectoryFor(CameraBackgroundDirectory)==Path.GetFullPath(first),"choosing a background stores its actual containing directory");
            var next=_cameras[1];next.Reference.Path=b;SelectCamera(next);OpenActiveCameraBackground();ChooseCameraBackground(next,_cameraBackgroundDialog.GetChild<Control>(0));
            picker=_cameraBackgroundDialog.GetChildren().OfType<FileDialog>().Single();
            Check(Path.GetFullPath(picker.CurrentDir)==Path.GetFullPath(first),"another camera opens its file picker in the last-used folder rather than the project or its old source folder");
            picker.CurrentDir=second;picker.EmitSignal(FileDialog.SignalName.Canceled);await Settle();
            Check(FileLocations.DirectoryFor(CameraBackgroundDirectory)==Path.GetFullPath(second),"browsing to a folder and cancelling still remembers that location");
            var fresh=new EditorFileLocations(preferences);
            Check(fresh.DirectoryFor(CameraBackgroundDirectory)==Path.GetFullPath(second),"a fresh application preference instance restores the saved directory");
            string temporaryFolder=Path.Combine(second,"Temporary child");Directory.CreateDirectory(temporaryFolder);fresh.Remember(CameraBackgroundDirectory,temporaryFolder);Directory.Delete(temporaryFolder);
            Check(fresh.DirectoryFor(CameraBackgroundDirectory)==Path.GetFullPath(second),"a removed recent directory falls back to its existing parent");
            File.WriteAllText(preferences,"invalid json");fresh=new EditorFileLocations(preferences);
            Check(fresh.DirectoryFor(CameraBackgroundDirectory,first)==Path.GetFullPath(first),"damaged optional preferences do not prevent browsing");
            Check(Descendants(this).OfType<Label>().Any(label=>label.Text=="GOPREVIS "+AppVersion&&label.IsVisibleInTree()),"the visible program name includes its current version");
            Check(Descendants(this).OfType<Label>().Any(label=>label.Text==AppCopyright&&label.IsVisibleInTree()),"the application visibly credits Petr Sajner 2026");
            _cameraBackgroundDialog.Hide();
            if(DisplayServer.GetName()!="headless")
            {
                await Settle();await ToSignal(RenderingServer.Singleton,RenderingServer.SignalName.FramePostDraw);GetViewport().GetTexture().GetImage().SavePng(Path.Combine(folder,"branding-and-camera.png"));
            }
            File.WriteAllText(Path.Combine(folder,"report.json"),JsonSerializer.Serialize(new{status="passed",checks,version=AppVersion,copyright=AppCopyright},_json));
            GD.Print("EDITOR_SETTINGS_OK checks="+checks);return checks;
        }
        finally
        {
            _cameraBackgroundDialog?.Hide();_editorFileLocations=previous;Restore(original,"");_projectPath=project;SetTime(time);
            _undo.Clear();_redo.Clear();foreach(var item in undo.Reverse())_undo.Push(item);foreach(var item in redo.Reverse())_redo.Push(item);
        }
    }
    async Task RunEditorSettingsChecksAndQuit()
    {
        try{await RunEditorSettingsChecks();GetTree().Quit(0);}catch(Exception ex){GD.PushError("EDITOR_SETTINGS_FAILED "+ex);GetTree().Quit(1);}
    }
}
