using Godot;
using System;

namespace GoPrevis;

public partial class ObjectMotionProbe:SceneTree
{
    public override async void _Initialize()
    {
        try
        {
            var app=new GoPrevisApp();app.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);Root.AddChild(app);
            for(int i=0;i<4;i++)await ToSignal(this,SignalName.ProcessFrame);
            int count=await app.RunObjectMotionChecks();if(count<35)throw new InvalidOperationException("Object motion checks were incomplete.");
            app.QueueFree();await ToSignal(this,SignalName.ProcessFrame);Quit(0);
        }
        catch(Exception ex){GD.PushError("OBJECT_MOTION_FAILED "+ex);Quit(1);}
    }
}
