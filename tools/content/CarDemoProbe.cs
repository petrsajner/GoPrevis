using Godot;
using System;

namespace Previs;

public partial class CarDemoProbe:SceneTree
{
    public override async void _Initialize()
    {
        try
        {
            var app=new PrevisApp();app.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);Root.AddChild(app);
            for(int i=0;i<5;i++)await ToSignal(this,SignalName.ProcessFrame);
            int checks=await app.RunCarDemoChecks();if(checks<16)throw new InvalidOperationException("Car demo checks were incomplete.");
            GD.Print("CAR_DEMO_PROBE_OK checks="+checks);Quit(0);
        }
        catch(Exception ex){GD.PushError("CAR_DEMO_PROBE_FAILED "+ex);Quit(1);}
    }
}
