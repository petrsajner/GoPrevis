using Godot;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization.Metadata;
using System.Threading.Tasks;

namespace GoPrevis;

public partial class GoPrevisApp
{
    sealed class SavedProjectState
    {
        public string FileHash="";
        public readonly HashSet<string> ContentHashes=new(StringComparer.Ordinal);
    }
    readonly Dictionary<string,SavedProjectState> _savedProjectStates=new(OperatingSystem.IsWindows()?StringComparer.OrdinalIgnoreCase:StringComparer.Ordinal);
    readonly HashSet<string> _cleanUntitledStates=new(StringComparer.Ordinal);
    static readonly JsonSerializerOptions ChangeJson=CreateChangeJson();
    bool _projectDirty,_dirtyTrackingReady,_projectTransitionActive;
    int _dirtyTrackingSuspended;
    double _dirtyPollTime;
    ConfirmationDialog? _unsavedDialog;
    Button? _unsavedDiscard;
    Action? _pendingProjectAction;

    static JsonSerializerOptions CreateChangeJson()
    {
        var resolver=new DefaultJsonTypeInfoResolver();
        // SnapshotObject persists the selected animation instead of this cached source field.
        resolver.Modifiers.Add(info=>{if(info.Type==typeof(ObjectData))foreach(var property in info.Properties)if(property.Name==nameof(ObjectData.ModelAnimation))property.ShouldSerialize=(_,_)=>false;});
        return new(){TypeInfoResolver=resolver};
    }
    string ProjectContentHash()
    {
        // Godot vectors are fields rather than properties. Arrays match persisted XYZ values.
        var serializable=new{fps=_fps,duration=_duration,setup=_projectSetup,scene=_sceneSettings,directional=_lighting.DirectionalEnabled,
            objects=_entries.Select(e=>new{data=e.Data,animation=e.Animation}),activeCamera=ActiveCameraId,
            cameras=_cameras.Select(c=>new{c.Id,c.Name,c.ShowLabel,c.Reference,c.Tracking,c.Movement,position=Array3(c.BasePosition),rotation=Array3(c.BaseRotation),scale=Array3(c.BaseScale),c.BaseLens,c.Animation,c.LensKeys})};
        return Convert.ToHexString(SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(serializable,ChangeJson)));
    }
    void MarkProjectClean(string? fileText=null)
    {
        string hash=ProjectContentHash();
        if(_projectPath=="")_cleanUntitledStates.Add(hash);
        else
        {
            string path=Path.GetFullPath(_projectPath),fileHash=fileText==null?"":Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(fileText)));
            if(!_savedProjectStates.TryGetValue(path,out var saved)||saved.FileHash!=fileHash)_savedProjectStates[path]=saved=new(){FileHash=fileHash};
            // The same saved JSON may be loaded with bundled paths or still reference original sources.
            saved.ContentHashes.Add(hash);
        }
        _dirtyTrackingReady=true;SetProjectDirty(false);
    }
    void SetProjectDirty(bool dirty)
    {if(_projectDirty==dirty)return;_projectDirty=dirty;RefreshProjectIdentity();}
    void RefreshProjectDirtyState()
    {
        if(!_dirtyTrackingReady||_dirtyTrackingSuspended>0)return;
        string hash=ProjectContentHash();bool clean=_projectPath==""?_cleanUntitledStates.Contains(hash):_savedProjectStates.TryGetValue(Path.GetFullPath(_projectPath),out var saved)&&saved.ContentHashes.Contains(hash);
        SetProjectDirty(!clean);
    }
    void PollProjectDirtyState(double delta)
    {
        if(_aiBusy||_exportBusy||_dirtyTrackingSuspended>0)return;
        _dirtyPollTime+=delta;if(_dirtyPollTime<.25)return;_dirtyPollTime=0;RefreshProjectDirtyState();
    }
    void InitializeProjectProtection()
    {
        GetTree().AutoAcceptQuit=false;GetWindow().CloseRequested+=RequestProjectClose;MarkProjectClean();
    }
    void RequestProjectClose()=>RequestProjectTransition(()=>GetTree().Quit(),"closing GoPrevis");
    async void RequestProjectTransition(Action action,string description)
    {
        if(_projectTransitionActive)return;_projectTransitionActive=true;
        try
        {
            if(_aiBusy||_exportBusy)
            {
                _aiCancellation?.Cancel();_exportCancellation?.Cancel();
                while(_aiBusy||_exportBusy)await ToSignal(GetTree(),SceneTree.SignalName.ProcessFrame);
            }
            ReleasePoseViewportDrag();ReleaseObjectGizmoDrag();EndArrangeDrag();StopHistoryPlayback();_rightDown=false;_middleDown=false;
            if(GetViewport().GuiGetFocusOwner() is LineEdit edit&&_inspector.IsAncestorOf(edit))
                if(edit!=_name||edit.Text.Trim()!=(_selectedCamera?.Name??_selected?.Data.Name))edit.EmitSignal(LineEdit.SignalName.TextSubmitted,edit.Text);
            GetViewport().GuiReleaseFocus();
            // An exclusive child (for example AI Director) must release the main window first.
            foreach(var dialog in Descendants(this).OfType<Window>().Where(w=>w.Visible).Reverse())dialog.Hide();
            _fileAction=null;_fileCancelled=null;
            RefreshProjectDirtyState();_pendingProjectAction=action;
            if(!_projectDirty){CompleteProjectTransition();return;}
            if(_unsavedDialog==null)
            {
                _unsavedDialog=new ConfirmationDialog{Title="Unsaved changes",Exclusive=true,Transient=true,WrapControls=false,DialogHideOnOk=false};AddChild(_unsavedDialog);
                _unsavedDialog.GetOkButton().Text="Save";_unsavedDialog.GetCancelButton().Text="Cancel";_unsavedDiscard=_unsavedDialog.AddButton("Discard",false,"discard");
                _unsavedDialog.Confirmed+=SaveBeforeProjectTransition;_unsavedDialog.Canceled+=CancelProjectTransition;
                _unsavedDialog.CustomAction+=name=>{if(name=="discard")CompleteProjectTransition();};
            }
            _unsavedDialog.DialogText="Save changes to \""+ProjectDisplayName+"\" before "+description+"?";_unsavedDialog.PopupCentered(new Vector2I(520,180));
        }
        catch(Exception error){CancelProjectTransition();Error(error.Message);}
    }
    void CompleteProjectTransition()
    {
        _unsavedDialog?.Hide();var action=_pendingProjectAction;_pendingProjectAction=null;_projectTransitionActive=false;
        try{action?.Invoke();}catch(Exception error){RefreshProjectDirtyState();Error(error.Message);}
    }
    void CancelProjectTransition()
    {_unsavedDialog?.Hide();_pendingProjectAction=null;_projectTransitionActive=false;}
    void SaveBeforeProjectTransition()
    {
        _unsavedDialog!.Hide();
        if(_projectPath==""){ChooseProjectSaveAs(SavePendingProjectTo,CancelProjectTransition);return;}
        try{SavePendingProjectTo(_projectPath);}catch(Exception error){Error(error.Message);}
    }
    void SavePendingProjectTo(string path)
    {try{SaveTo(path);CompleteProjectTransition();}catch{CancelProjectTransition();throw;}}
}
