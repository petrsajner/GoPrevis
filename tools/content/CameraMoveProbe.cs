using Godot;
using System;

namespace GenPrevis;

public partial class CameraMoveProbe:SceneTree
{
    public override async void _Initialize()
    {
        try
        {
            var app=new GenPrevisApp();app.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);Root.AddChild(app);
            for(int i=0;i<4;i++)await ToSignal(this,SignalName.ProcessFrame);
            int count=await app.RunCameraMovementChecks();if(count<120)throw new InvalidOperationException("Camera movement checks were incomplete.");
            app.QueueFree();await ToSignal(this,SignalName.ProcessFrame);GD.Print("CAMERA_MOVE_PROBE_OK checks="+count);Quit(0);
        }
        catch(Exception error){GD.PushError("CAMERA_MOVE_PROBE_FAILED "+error);Quit(1);}
    }
}
