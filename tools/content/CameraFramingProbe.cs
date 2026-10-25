using Godot;
using System;

namespace GoPrevis;

public partial class CameraFramingProbe:SceneTree
{
    public override async void _Initialize()
    {
        try
        {
            var app=new GoPrevisApp();app.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);Root.AddChild(app);
            for(int i=0;i<5;i++)await ToSignal(this,SignalName.ProcessFrame);
            int count=await app.RunCameraFramingChecks();if(count<65)throw new InvalidOperationException("Camera framing checks were incomplete.");
            app.QueueFree();await ToSignal(this,SignalName.ProcessFrame);GD.Print("CAMERA_FRAMING_PROBE_OK checks="+count);Quit(0);
        }
        catch(Exception ex){GD.PushError("CAMERA_FRAMING_PROBE_FAILED "+ex);Quit(1);}
    }
}
