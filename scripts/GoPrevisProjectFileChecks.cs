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
    async Task RunProjectFileChecksAndQuit()
    {try{await RunProjectFileChecks();GetTree().Quit(0);}catch(Exception error){GD.PushError("PROJECT_FILES_FAILED "+error);GetTree().Quit(1);}}

    public async Task RunProjectFileChecks()
    {
        var original=Snapshot();string project=_projectPath;double time=_time;var undo=_undo.ToArray();var redo=_redo.ToArray();int checks=0;bool passed=false;string? failure=null;var observations=new List<object>();
        string folder=Path.GetFullPath(ProjectSettings.GlobalizePath("res://output/project-file-checks/run-"+Guid.NewGuid().ToString("N")[..10]));Directory.CreateDirectory(folder);
        void Check(bool value,string message){Require(value,"Project files: "+message);checks++;observations.Add(new{message,passed=value});GD.Print("PASS Project files: "+message);}
        async Task Settle(){for(int i=0;i<5;i++)await ToSignal(GetTree(),SceneTree.SignalName.ProcessFrame);}
        void Shortcut(bool shift){GetViewport().GuiReleaseFocus();_UnhandledInput(new InputEventKey{Pressed=true,CtrlPressed=true,ShiftPressed=shift,Keycode=Key.S});}
        async Task Screenshot(string name){await Settle();if(DisplayServer.GetName()!="headless"){await ToSignal(RenderingServer.Singleton,RenderingServer.SignalName.FramePostDraw);GetViewport().GetTexture().GetImage().SavePng(Path.Combine(folder,name));}}
        try
        {
            StopHistoryPlayback();_projectPath="";Check(_projectNameLabel!.Text=="Untitled"&&GetWindow().Title.Contains(" — Untitled —",StringComparison.Ordinal),"an unsaved project is identified in the header and native title");
            string firstDirectory=Path.Combine(folder,"original"),secondDirectory=Path.Combine(folder,"copy");Directory.CreateDirectory(firstDirectory);Directory.CreateDirectory(secondDirectory);
            string first=Path.Combine(firstDirectory,"Street shot.v1.goprevis");SaveTo(first);byte[] originalBytes=File.ReadAllBytes(first);
            Check(_projectNameLabel.Text=="Street shot.v1"&&GetWindow().Title.Contains(" — Street shot.v1 —",StringComparison.Ordinal),"saving updates both names without dropping dots in the shot name");
            Check(_projectNameLabel.TooltipText==Path.GetFullPath(first),"the header tooltip exposes the complete project path");
            var menu=Descendants(this).OfType<MenuButton>().Single(b=>b.Text=="File").GetPopup();int saveAs=menu.GetItemIndex(8);
            Check(saveAs==3&&menu.GetItemText(saveAs)=="Save project as…","Save project as is directly below Save project in File");
            menu.EmitSignal(PopupMenu.SignalName.IdPressed,8L);await Settle();
            Check(_fileDialog.Visible&&_fileDialog.FileMode==FileDialog.FileModeEnum.SaveFile&&_fileDialog.Title=="Save project as","the actual menu opens the Save As picker for an already saved project");
            Check(Path.GetFullPath(_fileDialog.CurrentDir)==firstDirectory&&_fileDialog.CurrentFile==Path.GetFileName(first),"Save As starts in the current project's folder with its current name");
            await Screenshot("save-as-dialog.png");_fileDialog.GetCancelButton().EmitSignal(BaseButton.SignalName.Pressed);await Settle();
            Check(_projectPath==first&&_projectNameLabel.Text=="Street shot.v1"&&originalBytes.SequenceEqual(File.ReadAllBytes(first)),"cancelling Save As preserves project identity and original file bytes");
            PushUndo();AddObject("box",new Vector3(2,0,2),"Copy-only object");Shortcut(true);await Settle();
            Check(_fileDialog.Visible&&_fileDialog.Title=="Save project as","Ctrl+Shift+S opens Save As rather than overwriting the original");
            _fileDialog.CurrentDir=secondDirectory;_fileDialog.GetLineEdit().Text="Street shot.v2";_fileDialog.GetOkButton().EmitSignal(BaseButton.SignalName.Pressed);await Settle();
            string second=Path.Combine(secondDirectory,"Street shot.v2.goprevis");
            Check(File.Exists(second)&&Path.GetFullPath(_projectPath)==second&&_projectNameLabel.Text=="Street shot.v2","accepting a bare new name creates the extension and makes the copy active");
            Check(originalBytes.SequenceEqual(File.ReadAllBytes(first)),"Save As leaves the original project unchanged");
            Undo();Check(Path.GetFullPath(_projectPath)==second&&_projectNameLabel.Text=="Street shot.v2"&&!_entries.Any(e=>e.Data.Name=="Copy-only object"),"undoing an edit made before Save As keeps the copy active");
            Redo();Check(Path.GetFullPath(_projectPath)==second&&_entries.Any(e=>e.Data.Name=="Copy-only object"),"redoing the older edit keeps the copy active too");
            var copy=JsonSerializer.Deserialize<ProjectData>(File.ReadAllText(second))!;
            var sources=copy.Objects.SelectMany(o=>new[]{o.ModelPath,o.MotionPath}.Concat(o.MotionClips.Select(c=>c.MotionPath))).Concat(copy.Cameras.Select(c=>c.Reference.Path)).Where(p=>!string.IsNullOrEmpty(p)).ToArray();
            Check(sources.Length>0&&sources.All(p=>!Path.IsPathRooted(p!)&&File.Exists(Path.Combine(secondDirectory,p!))),"the copy has its own portable model/motion/reference sources in the destination folder");
            LoadProjectFromPath(second);Check(_entries.Any(e=>e.Data.Name=="Copy-only object")&&_projectNameLabel.Text=="Street shot.v2","the copied project reopens with its scene and header name intact");
            AddObject("box",new Vector3(3,0,2),"Later edit");Shortcut(false);await Settle();
            Check(!_fileDialog.Visible&&JsonSerializer.Deserialize<ProjectData>(File.ReadAllText(second))!.Objects.Any(o=>o.Name=="Later edit")&&originalBytes.SequenceEqual(File.ReadAllBytes(first)),"ordinary Ctrl+S writes later edits to the new active copy only");
            string blocker=Path.Combine(secondDirectory,"blocked");File.WriteAllText(blocker,"fixture");bool rejected=false;try{SaveTo(Path.Combine(blocker,"Failed copy"));}catch(IOException){rejected=true;}
            Check(rejected&&Path.GetFullPath(_projectPath)==second&&_projectNameLabel.Text=="Street shot.v2","a failed save cannot rename the active project");
            LoadProjectFromPath(first);Check(_projectNameLabel.Text=="Street shot.v1","opening another file updates the header immediately");Undo();
            Check(_projectNameLabel.Text=="Street shot.v2"&&GetWindow().Title.Contains("Street shot.v2",StringComparison.Ordinal),"Undo restores the previous project name and native title");Redo();
            Check(_projectNameLabel.Text=="Street shot.v1","Redo restores the newly opened project's identity");NewScene();
            Check(_projectNameLabel.Text=="Untitled","New scene clears the previous filename");Undo();
            Check(_projectNameLabel.Text=="Street shot.v1","Undo of New scene restores its saved project name");
            string longName="A very long shot name - "+new string('x',150);_projectPath=Path.Combine(folder,longName+".goprevis");int layout=await RunResponsiveLayoutChecks();await Settle();
            Check(layout==24&&_projectNameLabel!.Text==longName&&_projectNameLabel.GetGlobalRect().End.X<=_projectSetupButton!.GetGlobalRect().Position.X,"long project names retain the full value and fit the responsive header at all three tested resolutions");
            LoadProjectFromPath(second);await Screenshot("named-project-header.png");passed=true;GD.Print("PROJECT_FILES_OK checks="+checks+" layout="+layout+" output="+folder);
        }
        catch(Exception error){failure=error.ToString();throw;}
        finally
        {
            _fileDialog.Hide();File.WriteAllText(Path.Combine(folder,"report.json"),JsonSerializer.Serialize(new{status=passed?"passed":"failed",checks,failure,observations},_json));Restore(original,"");_projectPath=project;SetTime(time);
            _undo.Clear();_redo.Clear();foreach(var item in undo.Reverse())_undo.Push(item);foreach(var item in redo.Reverse())_redo.Push(item);RefreshHistoryControls();
        }
    }
}
