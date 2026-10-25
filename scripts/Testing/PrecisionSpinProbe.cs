using Godot;
using System;
using System.Threading.Tasks;

namespace GoPrevis;

/// <summary>Real arrow-overlay input, linear precision and reversibility at range limits.</summary>
public partial class PrecisionSpinProbe:SceneTree
{
    public override async void _Initialize()
    {
        try{int count=await RunChecks(this,Root);GD.Print("PRECISION_SPIN_OK checks="+count);Quit(0);}
        catch(Exception ex){GD.PushError("PRECISION_SPIN_FAILED "+ex);Quit(1);}
    }
    public static async Task<int> RunChecks(SceneTree tree,Node parent)
    {
        int checks=0;void Check(bool condition,string message){if(!condition)throw new InvalidOperationException(message);checks++;GD.Print("PASS Precision numeric: "+message);}
        var spin=new PrecisionSpinBox{MinValue=-1000,MaxValue=1000,Step=.001,Value=0,DragUnitsPerPixel=.001,Position=new Vector2(20,20),Size=new Vector2(100,32)};parent.AddChild(spin);
        int starts=0,ends=0;spin.GestureStarted+=()=>starts++;spin.GestureEnded+=()=>ends++;
        for(int i=0;i<2;i++)await tree.ToSignal(tree,SceneTree.SignalName.ProcessFrame);
        void Down(bool up=true)=>spin.ArrowControl.EmitSignal(Control.SignalName.GuiInput,new InputEventMouseButton{ButtonIndex=MouseButton.Left,Pressed=true,Position=new Vector2(9,up?4:spin.Size.Y-4)});
        void Move(float y,bool fine=false)=>spin._Input(new InputEventMouseMotion{Relative=new Vector2(0,y),AltPressed=fine});
        void Up()=>spin._Input(new InputEventMouseButton{ButtonIndex=MouseButton.Left,Pressed=false});
        try
        {
            Down();Up();Check(Math.Abs(spin.Value-.001)<1e-8,"arrow click changes one millimetre rather than a coarse jump");
            Down(false);Up();Check(Math.Abs(spin.Value)<1e-8,"opposite arrow reverses the exact click step");
            int before=starts;Down();Move(-10);Move(4);Up();Check(Math.Abs(spin.Value-.006)<1e-8,"vertical drag is linear and reversible");Check(starts==before+1&&starts==ends,"a continuous scrub starts and ends one gesture");
            spin.Value=spin.MaxValue;Down();Move(-100000);Move(1);Check(spin.Value<spin.MaxValue,"one opposite pixel immediately leaves the maximum after a huge over-drag");Up();
            spin.Value=spin.MinValue;Down();Move(100000);Move(-1);Check(spin.Value>spin.MinValue,"one opposite pixel immediately leaves the minimum after a huge over-drag");Up();
            spin.DragUnitsPerPixel=.0005;spin.Value=spin.MaxValue;Down();Move(-100000);Move(1);Check(spin.Value<spin.MaxValue,"range-limit reversal also works with sub-step drag resolution");Up();
            spin.DragUnitsPerPixel=.001;spin.Value=0;Down();Move(-10,true);Up();Check(Math.Abs(spin.Value-.001)<1e-8,"Alt supplies a smaller adjustment without acceleration");
            spin.Value=.25;spin.ArrowControl.EmitSignal(Control.SignalName.GuiInput,new InputEventMouseButton{ButtonIndex=MouseButton.Right,Pressed=true,Position=new Vector2(9,4)});Check(Math.Abs(spin.Value-.25)<1e-8,"right click on the arrow area never jumps to 1000");
            spin.GetLineEdit().Text="1.234";spin.Apply();Check(Math.Abs(spin.Value-1.234)<1e-8,"ordinary typed numeric entry still works");
            spin.GetLineEdit().Text="2 + 3";spin.GetLineEdit().EmitSignal(LineEdit.SignalName.TextChanged,"2 + 3");Down();Up();Check(Math.Abs(spin.Value-5.001)<1e-8,"an explicitly edited expression is committed before its arrow step");
            spin.Value=.002;spin.GetLineEdit().Text="999";spin.GetLineEdit().GrabFocus();Down(false);Up();Check(Math.Abs(spin.Value-.001)<1e-8,"a stale native text refresh cannot replace the actual programmatic value on arrow focus exit");
            double disabledValue=spin.Value;spin.Editable=false;Down();Move(-500);Up();Check(Math.Abs(spin.Value-disabledValue)<1e-8,"disabled fields ignore arrow scrubbing");spin.Editable=true;
            spin.Value=.5;Down();Move(-20);spin._Input(new InputEventKey{Keycode=Key.Escape,Pressed=true});Check(Math.Abs(spin.Value-.5)<1e-8&&!spin.ArrowDragging,"Escape restores a scrub without leaving pointer capture active");
            Check(starts==ends,"every started gesture has ended");
        }
        finally{spin.QueueFree();await tree.ToSignal(tree,SceneTree.SignalName.ProcessFrame);}
        return checks;
    }
}
