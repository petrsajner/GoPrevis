using Godot;
using System;
using System.Linq;
using System.Threading.Tasks;

namespace GenPrevis;

public partial class GenPrevisApp
{
    Button? _undoButton, _redoButton;
    HBoxContainer BuildHistoryControls()
    {
        _undoButton=Button("↶ Undo",()=>{GetViewport().GuiReleaseFocus();Undo();});
        _redoButton=Button("↷ Redo",()=>{GetViewport().GuiReleaseFocus();Redo();});
        _undoButton.TooltipText="Undo the last edit (Ctrl+Z)";
        _redoButton.TooltipText="Redo the last undone edit (Ctrl+Shift+Z or Ctrl+Y)";
        var controls=Row(_undoButton,_redoButton);controls.AddThemeConstantOverride("separation",3);
        RefreshHistoryControls();return controls;
    }
    void RefreshHistoryControls()
    {
        if(_undoButton!=null)_undoButton.Disabled=_undo.Count==0||_exportBusy;
        if(_redoButton!=null)_redoButton.Disabled=_redo.Count==0||_exportBusy;
    }
    void StopHistoryPlayback(){_playing=false;if(_play!=null)_play.Text="▶";}

    async Task RunHistoryChecksAndQuit()
    {
        try{await RunHistoryChecks();GetTree().Quit(0);}
        catch(Exception ex){GD.PushError("HISTORY_FAILED "+ex);GetTree().Quit(1);}
    }
    async Task RunProjectWorkflowChecksAndQuit()
    {
        try
        {
            int history=await RunHistoryChecks();
            int motion=await RunObjectMotionChecks();
            int animals=await RunAnimalPoseChecks();
            int setup=await RunProjectSetupChecks();
            int export=await RunProjectExportChecks();
            GD.Print($"PROJECT_WORKFLOW_OK history={history} motion={motion} animals={animals} setup={setup} export={export}");GetTree().Quit(0);
        }
        catch(Exception ex){GD.PushError("PROJECT_WORKFLOW_FAILED "+ex);GetTree().Quit(1);}
    }

    public Task<int> RunHistoryChecks()
    {
        var original=Snapshot();var originalPath=_projectPath;double time=_time;
        var undo=_undo.ToArray();var redo=_redo.ToArray();int checks=0;
        void Check(bool condition,string name){if(!condition)throw new InvalidOperationException("History: "+name);checks++;GD.Print("PASS History: "+name);}
        try
        {
            _undo.Clear();_redo.Clear();RefreshHistoryControls();
            Check(_undoButton?.Disabled==true&&_redoButton?.Disabled==true,"empty history disables both visible controls");
            var box=AddObject("box",new Vector3(3,1,2),"History check");string id=box.Data.Id;Select(box);
            PushUndo();box.Data.Position=Array3(new Vector3(5,2,3));SetTime(_time);RefreshHistoryControls();
            Check(_undoButton?.Disabled==false&&_redoButton?.Disabled==true,"an edit enables Undo only");
            _playing=true;_undoButton!.EmitSignal(BaseButton.SignalName.Pressed);RefreshHistoryControls();
            Check(Vector(_entries.Single(e=>e.Data.Id==id).Data.Position).IsEqualApprox(new Vector3(3,1,2)),"visible Undo restores the prior object transform");
            Check(!_playing&&_redoButton?.Disabled==false,"Undo stops playback and enables Redo");
            _redoButton!.EmitSignal(BaseButton.SignalName.Pressed);RefreshHistoryControls();
            Check(Vector(_entries.Single(e=>e.Data.Id==id).Data.Position).IsEqualApprox(new Vector3(5,2,3)),"visible Redo reapplies the edit");
            _UnhandledInput(new InputEventKey{Pressed=true,Keycode=Key.Z,CtrlPressed=true});
            Check(_redo.Count==1,"Ctrl+Z routes to scene Undo");
            _UnhandledInput(new InputEventKey{Pressed=true,Keycode=Key.Z,CtrlPressed=true,ShiftPressed=true});
            Check(_redo.Count==0&&Vector(_entries.Single(e=>e.Data.Id==id).Data.Position).X==5,"Ctrl+Shift+Z routes to Redo");
            Undo();_UnhandledInput(new InputEventKey{Pressed=true,Keycode=Key.Y,CtrlPressed=true});
            Check(_redo.Count==0,"Ctrl+Y remains a Redo shortcut");
            Undo();PushUndo();Check(_redo.Count==0,"a new edit clears the redo branch");
            GD.Print("HISTORY_OK checks="+checks);return Task.FromResult(checks);
        }
        finally
        {
            Restore(original,"");_projectPath=originalPath;SetTime(time);
            _undo.Clear();_redo.Clear();foreach(var item in undo.Reverse())_undo.Push(item);foreach(var item in redo.Reverse())_redo.Push(item);
            RefreshHistoryControls();
        }
    }
}
