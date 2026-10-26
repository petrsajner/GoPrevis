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
    async Task RunUnsavedChangesChecksAndQuit()
    {try{await RunUnsavedChangesChecks();GetTree().Quit(0);}catch(Exception error){GD.PushError("UNSAVED_CHANGES_FAILED "+error);GetTree().Quit(1);}}
    public async Task RunUnsavedChangesChecks()
    {
        string folder=Path.GetFullPath(ProjectSettings.GlobalizePath("res://output/unsaved-changes-checks/run-"+Guid.NewGuid().ToString("N")[..10]));Directory.CreateDirectory(folder);
        int checks=0;bool passed=false;string? failure=null;var observations=new List<object>();
        void Check(bool value,string message){Require(value,"Unsaved changes: "+message);checks++;observations.Add(new{message,passed=value});GD.Print("PASS Unsaved changes: "+message);}
        async Task Settle(){for(int i=0;i<5;i++)await ToSignal(GetTree(),SceneTree.SignalName.ProcessFrame);}
        void Edit(){PushUndo();_entries[0].Data.Position[0]+=.7f;SetTime(_time);RefreshProjectDirtyState();}
        void Choose(string path)=>_fileDialog.EmitSignal(FileDialog.SignalName.FileSelected,path);
        void Cancel()=>_unsavedDialog!.GetCancelButton().EmitSignal(BaseButton.SignalName.Pressed);
        void Save()=>_unsavedDialog!.GetOkButton().EmitSignal(BaseButton.SignalName.Pressed);
        void Discard()=>_unsavedDiscard!.EmitSignal(BaseButton.SignalName.Pressed);
        async Task Screenshot(string name){await Settle();if(DisplayServer.GetName()!="headless"){await ToSignal(RenderingServer.Singleton,RenderingServer.SignalName.FramePostDraw);GetViewport().GetTexture().GetImage().SavePng(Path.Combine(folder,name));}}
        try
        {
            Check(!GetTree().AutoAcceptQuit&&!_projectDirty&&_projectNameLabel!.Text=="Untitled","native auto-quit is disabled and the untouched starter is clean");
            string first=Path.Combine(folder,"Unsaved shot.goprevis"),second=Path.Combine(folder,"Other shot.goprevis");SaveTo(first);
            for(int i=0;i<9;i++)SetTime(_duration*i/9);Select(_entries[0]);FrameSelected();RefreshProjectDirtyState();
            Check(!_projectDirty&&!_projectNameLabel!.Text.EndsWith(" *",StringComparison.Ordinal),"scrubbing and working-view navigation do not mark an animated project dirty");
            Edit();Check(_projectDirty&&_projectNameLabel!.Text=="Unsaved shot *"&&GetWindow().Title.Contains(" — Unsaved shot * —",StringComparison.Ordinal),"an object edit adds the star to both header and native title");
            Undo();Check(!_projectDirty&&_projectNameLabel!.Text=="Unsaved shot","Undo to the saved content removes the star");Redo();Check(_projectDirty,"Redo restores the dirty state");
            SaveProject();Check(!_projectDirty&&_projectNameLabel!.Text=="Unsaved shot","successful Save removes the star");
            var camera=_cameras[0];float lens=camera.BaseLens;camera.BaseLens+=1;RefreshProjectDirtyState();Check(_projectDirty,"camera lens changes are included in the saved-content comparison");camera.BaseLens=lens;RefreshProjectDirtyState();Check(!_projectDirty,"restoring the original value clears the star without relying on history counts");
            _entries[0].Data.PoseCorrections.Bones["Head"]=new(){new(){Time=1,Rotation=new[]{0f,.1f,0f,.9949874f}}};RefreshProjectDirtyState();Check(_projectDirty,"additive pose keys participate in dirty tracking");LoadProjectFromPath(first);
            double duration=_duration;_duration+=1;RefreshProjectDirtyState();Check(_projectDirty,"project duration edits are detected even without an Undo push");_duration=duration;RefreshProjectDirtyState();
            int transitions=0;RequestProjectTransition(()=>transitions++,"opening another project");Check(transitions==1&&!_projectTransitionActive,"a clean project continues without an unnecessary prompt");
            Edit();RequestProjectTransition(()=>transitions++,"closing GoPrevis");await Settle();
            Check(_unsavedDialog?.Visible==true&&_unsavedDialog.GetOkButton().Text=="Save"&&_unsavedDialog.GetCancelButton().Text=="Cancel"&&_unsavedDiscard?.Text=="Discard","the real English modal offers Save, Discard and Cancel");
            await Screenshot("unsaved-dialog.png");string modified=ProjectContentHash();Cancel();
            Check(transitions==1&&!_projectTransitionActive&&_projectDirty&&ProjectContentHash()==modified,"Cancel keeps the edited scene and aborts the requested action");
            await Settle();GetWindow().EmitSignal(Window.SignalName.CloseRequested);await Settle();GD.Print("CLOSE_GUARD visible="+_unsavedDialog!.Visible+" pending="+_projectTransitionActive+" dirty="+_projectDirty);Check(_unsavedDialog!.Visible&&_projectTransitionActive,"the actual native close signal opens the protection dialog instead of quitting");Cancel();await Settle();
            RequestProjectTransition(()=>transitions++,"closing GoPrevis");Save();
            Check(transitions==2&&!_projectDirty&&!_projectTransitionActive&&JsonSerializer.Deserialize<ProjectData>(File.ReadAllText(first))!.Objects[0].Position[0]==_entries[0].Data.Position[0],"Save writes the edited project before continuing the requested action");
            Edit();byte[] beforeDiscard=File.ReadAllBytes(first);RequestProjectTransition(()=>transitions++,"opening another project");Discard();
            Check(transitions==3&&beforeDiscard.SequenceEqual(File.ReadAllBytes(first)),"Discard continues without writing the unsaved edits to disk");
            string blocker=Path.Combine(folder,"blocked");File.WriteAllText(blocker,"fixture");RequestProjectTransition(()=>transitions++,"closing GoPrevis");_unsavedDialog!.Hide();bool rejected=false;try{SavePendingProjectTo(Path.Combine(blocker,"Failed save"));}catch(IOException){rejected=true;}
            Check(rejected&&transitions==3&&!_projectTransitionActive&&_projectDirty,"a failed save aborts the action and preserves dirty content");
            LoadProjectFromPath(first);AddObject("box",Vector3.Zero,"Only in other project");SaveTo(second);LoadProjectFromPath(first);Edit();string beforeOpen=ProjectContentHash();
            OpenProject();Choose(second);await Settle();Cancel();Check(ProjectContentHash()==beforeOpen&&Path.GetFullPath(_projectPath)==first,"cancelling Open's unsaved prompt preserves the original project");
            OpenProject();Choose(second);await Settle();Discard();
            Check(Path.GetFullPath(_projectPath)==second&&!_projectDirty&&_entries.Any(e=>e.Data.Name=="Only in other project"),"Discard then Open loads the selected file and establishes its clean state");
            Undo();Check(_projectDirty&&Path.GetFullPath(_projectPath)==first,"Undo across an Open restores the previous project's unsaved content and star");Redo();Check(!_projectDirty&&Path.GetFullPath(_projectPath)==second,"Redo across Open restores the clean target without a false star");
            NewScene();Check(_entries.Count==0&&!_projectDirty&&_projectNameLabel!.Text=="Untitled","a new empty scene begins clean");AddObject("box",Vector3.Zero,"Untitled edit");NewScene();await Settle();
            Check(_unsavedDialog!.Visible&&_entries.Count==1,"New scene also protects unsaved edits");Save();await Settle();
            Check(_fileDialog.Visible&&_projectTransitionActive,"saving an untitled project opens Save As before continuing");_fileDialog.GetCancelButton().EmitSignal(BaseButton.SignalName.Pressed);await Settle();
            Check(!_projectTransitionActive&&_entries.Count==1&&_projectPath==""&&_projectDirty,"cancelling Save As also cancels the pending New scene");
            NewScene();await Settle();Save();await Settle();string untitled=Path.Combine(folder,"Saved before new.goprevis");Choose(untitled);await Settle();
            Check(File.Exists(untitled)&&JsonSerializer.Deserialize<ProjectData>(File.ReadAllText(untitled))!.Objects.Single().Name=="Untitled edit"&&_entries.Count==0&&!_projectDirty,"untitled Save completes before the pending New scene clears the editor");
            LoadProjectFromPath(first);Edit();RequestProjectTransition(()=>transitions++,"opening another project");RequestProjectTransition(()=>transitions+=100,"closing GoPrevis");Discard();Check(transitions==4,"repeated close/open requests cannot execute duplicate pending actions");
            if(!OS.HasFeature("editor"))Check(!DirAccess.GetDirectoriesAt("res://").Any(d=>d.EndsWith(".assets",StringComparison.OrdinalIgnoreCase))&&!DirAccess.GetFilesAt("res://").Any(f=>f.EndsWith(".goprevis",StringComparison.OrdinalIgnoreCase)),"portable resources exclude user-saved projects and adjacent asset folders");
            LoadProjectFromPath(first);string clean=ProjectContentHash();await PreviewAiPlan(AiFixturePlan(),true,System.Threading.CancellationToken.None);
            Check(!_projectDirty&&ProjectContentHash()==clean,"temporary AI rehearsal restores the clean project without a false unsaved star");
            Edit();OpenAiDirector();await Settle();RequestProjectTransition(()=>transitions++,"closing GoPrevis");await Settle();
            Check(_unsavedDialog!.Visible&&!_aiDialog!.Visible,"the save prompt can open even when AI Director was an exclusive child window");Cancel();await Settle();
            LoadProjectFromPath(first);Select(_entries[0]);await Settle();_name.GrabFocus();_name.Text="Pending inspector rename";RequestProjectTransition(()=>transitions++,"closing GoPrevis");await Settle();
            Check(_unsavedDialog!.Visible&&_projectDirty&&_entries[0].Data.Name=="Pending inspector rename","a pending inspector name is committed before checking whether it needs saving");Cancel();
            LoadProjectFromPath(first);Edit();await Screenshot("dirty-project-header.png");passed=true;GD.Print("UNSAVED_CHANGES_OK checks="+checks+" output="+folder);
        }
        catch(Exception error){failure=error.ToString();throw;}
        finally{CancelProjectTransition();_fileDialog.Hide();File.WriteAllText(Path.Combine(folder,"report.json"),JsonSerializer.Serialize(new{status=passed?"passed":"failed",checks,failure,observations},_json));}
    }
}
