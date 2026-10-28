using Godot;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;

namespace GoPrevis;

public partial class GoPrevisApp
{
    /// <summary>Exercises the actual hierarchy numeric controls and a multi-layer selected-key drag.</summary>
    async Task RunAnimationPanelEditChecks(Action<bool,string> check)
    {await RunAnimationPanelEditingChecks(ProjectSettings.GlobalizePath("res://output/animation-panel-checks/run-"+Guid.NewGuid().ToString("N")[..10]),check);}
    async Task<int> RunAnimationPanelEditingChecks(string folder,Action<bool,string>? observe=null)
    {
        int checks=0;void Check(bool value,string message){Require(value,"Animation panel: "+message);checks++;GD.Print("PASS Animation panel: "+message);observe?.Invoke(value,"Animation panel: "+message);}
        var original=Snapshot();string project=_projectPath;double time=_time;bool auto=_autoKey,playing=_playing;
        string? objectId=_selected?.Data.Id,cameraId=_selectedCamera?.Id;var undo=_undo.ToArray();var redo=_redo.ToArray();
        string mode=_animationWorkspaceMode,search=_animationSearch!.Text;
        bool animated=_animationAnimatedOnly!.ButtonPressed,selected=_animationSelectedOnly!.ButtonPressed,pinned=_animationPinnedOnly!.ButtonPressed;
        var expanded=_animationExpanded.ToArray();var selectedChannels=_animationSelectedChannels.ToArray();
        var window=GetWindow();var windowSize=window.Size;var windowMode=window.Mode;float windowScale=window.ContentScaleFactor;
        async Task Settle(){for(int i=0;i<5;i++)await ToSignal(GetTree(),SceneTree.SignalName.ProcessFrame);}
        void Expand(){ShowAnimationExpandMenu();_animationExpandMenu!.EmitSignal(PopupMenu.SignalName.IdPressed,0L);_animationExpandMenu.Hide();}
        AnimationChannelBinding Binding(string id,string path)=>AnimationBindings(id).Single(b=>b.Path==path);
        void Click(AnimationTrackCanvas canvas,Vector2 at,bool ctrl=false,bool shift=false)
        {
            canvas._GuiInput(new InputEventMouseButton{ButtonIndex=MouseButton.Left,Pressed=true,Position=at,CtrlPressed=ctrl,ShiftPressed=shift});
            canvas._Input(new InputEventMouseButton{ButtonIndex=MouseButton.Left,Pressed=false,Position=canvas.GetGlobalTransformWithCanvas()*at});
        }
        void Drag(AnimationTrackCanvas canvas,Vector2 at,float xDelta)
        {
            canvas._GuiInput(new InputEventMouseButton{ButtonIndex=MouseButton.Left,Pressed=true,Position=at});
            canvas._Input(new InputEventMouseMotion{Position=canvas.GetGlobalTransformWithCanvas()*(at+Vector2.Right*xDelta),Relative=Vector2.Right*xDelta});
            canvas._Input(new InputEventMouseButton{ButtonIndex=MouseButton.Left,Pressed=false});
        }
        try
        {
            _playing=false;_autoKey=false;window.Mode=Window.ModeEnum.Windowed;window.ContentScaleFactor=1;window.Size=new Vector2I(1600,1100);
            var first=AddObject("box",new Vector3(1,2,3),"Panel value box");string firstId=first.Data.Id;Select(first);SetTime(1);
            _animationSearch.Text="Panel";_animationAnimatedOnly.ButtonPressed=false;_animationSelectedOnly.ButtonPressed=true;_animationPinnedOnly.ButtonPressed=false;
            SetAnimationWorkspaceMode("Dope sheet");Expand();await Settle();
            string xId=Binding(firstId,"Transform.Position.X").Identity;
            var field=_animationDope!.ValueEditor(xId);
            Check(field!=null&&field.Editable&&field.IsVisibleInTree(),"dope-sheet property values are real editable numeric fields");
            int history=_undo.Count;var entry=field!.GetLineEdit();entry.GrabFocus();entry.Text="-2.75";field.Apply();entry.EmitSignal(LineEdit.SignalName.TextSubmitted,"-2.75");await Settle();
            Check(Math.Abs(first.Node.Position.X+2.75)<.0001&&Math.Abs(first.Data.Position[0]+2.75)<.0001,"typing a negative fractional value changes the actual property and scene node");
            Check(_undo.Count==history+1,"one typed numeric edit records one Undo action");
            Undo();first=_entries.Single(e=>e.Data.Id==firstId);Select(first);await Settle();
            Check(Math.Abs(first.Node.Position.X-1)<.0001,"Undo restores the numeric edit through freshly rebound property controls");
            field=_animationDope.ValueEditor(xId);entry=field!.GetLineEdit();double before=field.Value;history=_undo.Count;entry.GrabFocus();entry.Text="999";
            entry.EmitSignal(Control.SignalName.GuiInput,new InputEventKey{Pressed=true,Keycode=Key.Escape});await Settle();
            Check(Math.Abs(first.Node.Position.X-before)<.0001&&_undo.Count==history,"Escape cancels unsubmitted numeric text without changing the scene");
            var precise=(PrecisionSpinBox)_animationDope.ValueEditor(xId)!;history=_undo.Count;before=precise.Value;
            precise.ArrowControl.EmitSignal(Control.SignalName.GuiInput,new InputEventMouseButton{ButtonIndex=MouseButton.Left,Pressed=true,Position=new Vector2(5,3)});
            precise._Input(new InputEventMouseMotion{Relative=new Vector2(0,-8)});precise._Input(new InputEventMouseMotion{Relative=new Vector2(0,-8)});
            precise._Input(new InputEventMouseButton{ButtonIndex=MouseButton.Left,Pressed=false});await Settle();
            Check(precise.Value>before&&Math.Abs(first.Node.Position.X-precise.Value)<.0001&&_undo.Count==history+1,"precise inline arrow scrubbing updates the property as one continuous Undo gesture");

            var y=Binding(firstId,"Transform.Position.Y").EnsureCurve();PropertyCurveRuntime.Upsert(y,0,2);PropertyCurveRuntime.Upsert(y,4,6);SetTime(2);
            SetAnimationWorkspaceMode("Graph");Expand();await Settle();string yId=Binding(firstId,"Transform.Position.Y").Identity;
            var graphField=_animationHierarchy!.ValueEditor(yId);Check(graphField!=null&&graphField.Editable&&graphField.IsVisibleInTree(),"graph hierarchy exposes the same editable numeric values");
            history=_undo.Count;graphField!.Value=5.25;await Settle();
            Check(y.Keys.Any(k=>Math.Abs(k.Time-2)<.000001&&Math.Abs(k.Value-5.25)<.0001)&&first.Data.Position[1]==2&&Math.Abs(first.Node.Position.Y-5.25)<.0001,"editing an animated value creates a playhead key and preserves its authored base");
            Check(_undo.Count==history+1,"an inline edit to an animated channel is one Undo action");

            var second=AddObject("box",Vector3.Zero,"Panel group box");string secondId=second.Data.Id;
            var c1=Binding(firstId,"Transform.Position.X").EnsureCurve();c1.Keys.Clear();var a=PropertyCurveRuntime.Upsert(c1,1,1);PropertyCurveRuntime.Upsert(c1,3,3);
            var c2=Binding(secondId,"Transform.Position.X").EnsureCurve();var b=PropertyCurveRuntime.Upsert(c2,1,2);PropertyCurveRuntime.Upsert(c2,3,4);
            PropertyCurveRuntime.Upsert(y,1.5,3);SetTime(1);_animationSelectedOnly.ButtonPressed=false;_animationAnimatedOnly.ButtonPressed=true;SetAnimationWorkspaceMode("Dope sheet");Expand();await Settle();
            string secondXId=Binding(secondId,"Transform.Position.X").Identity;
            var atA=_animationDope.KeyRect(xId,a.Id).GetCenter();var atB=_animationDope.KeyRect(secondXId,b.Id).GetCenter();
            Click(_animationDope,atA);Click(_animationDope,atB,true);Check(_animationDope.SelectedKeyCount==2,"keys on different objects can be selected together");
            float deltaPixels=(_animationDope.KeyRect(xId,c1.Keys.Single(k=>k.Time==3).Id).GetCenter().X-atA.X)/4;
            history=_undo.Count;Drag(_animationDope,atA,deltaPixels);
            Check(Math.Abs(c1.Keys.Single(k=>k.Id==a.Id).Time-1.5)<.000001&&Math.Abs(c2.Keys.Single(k=>k.Id==b.Id).Time-1.5)<.000001,"pressing an already selected diamond moves the complete cross-object selection");
            Check(_animationDope.SelectedKeyCount==2&&_undo.Count==history+1&&c1.Keys.Any(k=>k.Time==3)&&c2.Keys.Any(k=>k.Time==3),"group retiming keeps selection, one Undo and unselected keys");
            _animationExpanded.Remove("target:"+firstId);_animationStructureHash=0;RefreshAnimationWorkspace();await Settle();
            var summary=_animationDope.GroupRowRect(firstId);var collapsedAt=new Vector2(_animationDope.KeyRect(secondXId,b.Id).GetCenter().X,summary.Position.Y+12);
            history=_undo.Count;Drag(_animationDope,collapsedAt,deltaPixels);
            Check(Math.Abs(c1.Keys.Single(k=>k.Id==a.Id).Time-2)<.000001&&Math.Abs(c2.Keys.Single(k=>k.Id==b.Id).Time-2)<.000001&&_animationDope.SelectedKeyCount==2,"a partially selected collapsed summary preserves the original group instead of selecting only its clicked row");
            Check(y.Keys.Any(k=>Math.Abs(k.Time-1.5)<.000001)&&_undo.Count==history+1,"dragging the selected subset does not move other summary keys");
            if(DisplayServer.GetName()!="headless")
            {Directory.CreateDirectory(folder);await ToSignal(RenderingServer.Singleton,RenderingServer.SignalName.FramePostDraw);using var image=GetViewport().GetTexture().GetImage();image.SavePng(Path.Combine(folder,"inline-property-values.png"));}
            return checks;
        }
        finally
        {
            _animationDope?.GrabFocus();Restore(original,"");_projectPath=project;SetTime(time);RestoreInteractionSelection(objectId,cameraId);_autoKey=auto;_playing=playing;
            _undo.Clear();_redo.Clear();foreach(var state in undo.Reverse())_undo.Push(state);foreach(var state in redo.Reverse())_redo.Push(state);
            _animationExpanded.Clear();_animationExpanded.UnionWith(expanded);_animationSelectedChannels.Clear();_animationSelectedChannels.UnionWith(selectedChannels);
            _animationSearch!.Text=search;_animationAnimatedOnly!.ButtonPressed=animated;_animationSelectedOnly!.ButtonPressed=selected;_animationPinnedOnly!.ButtonPressed=pinned;SetAnimationWorkspaceMode(mode);
            window.Mode=windowMode;window.ContentScaleFactor=windowScale;window.Size=windowSize;
        }
    }
}
