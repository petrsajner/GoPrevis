using Godot;
using System;

namespace GenPrevis;

public partial class TimelineTrimProbe:SceneTree
{
    public override async void _Initialize()
    {
        try
        {
            var app=new GenPrevisApp();app.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);Root.AddChild(app);
            for(int i=0;i<3;i++)await ToSignal(this,SignalName.ProcessFrame);
            await app.RunTimelineTrimChecks();Quit(0);
        }
        catch(Exception ex){GD.PushError("TIMELINE_TRIM_FAILED "+ex);Quit(1);}
    }
}
