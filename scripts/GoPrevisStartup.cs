using Godot;
using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;

namespace GoPrevis;

public partial class GoPrevisApp
{
    void ApplyStartupProjectArguments()
    {
        var arguments=OS.GetCmdlineUserArgs();
        if(arguments.Contains("--demo=car-chase")){OpenCarChaseDemo();return;}
        var open=arguments.FirstOrDefault(value=>value.StartsWith("--open=",StringComparison.Ordinal));
        if(open==null)return;
        try
        {
            LoadProjectFromPath(open[7..]);SetTime(0);
            if(arguments.Contains("--camera-view")){SelectCamera(_cameras.First(camera=>camera.Rig==_shot));if(!_pilot)TogglePilot();}
            else if(_selected?.Data.Path.Points.Count>0)ShowInspectorSection("Path");
        }
        catch(Exception ex){Error(ex.Message);}
    }
    void OpenCarChaseDemo()
    {
        if(_exportBusy)return;
        try
        {
            GetViewport().GuiReleaseFocus();if(_drawingPath)CancelDrawingPath();StopPoseEditing();StopHistoryPlayback();
            string adjacent=Path.Combine(Path.GetDirectoryName(OS.GetExecutablePath())!,"examples","car-chase.goprevis");
            string path=File.Exists(adjacent)?adjacent:SamplePath("samples/car-chase.goprevis");
            LoadProjectFromPath(path);_projectPath="";SetTime(0);
            SelectCamera(_cameras.First(camera=>camera.Rig==_shot));if(!_pilot)TogglePilot();
            Status("Car chase demo · Space: play / pause · Camera background…: this camera's image or video · Save creates your own project.");
        }
        catch(Exception ex){Error("The car chase demo could not be opened: "+ex.Message);}
    }

    async Task RunDemoLaunchChecksAndQuit()
    {
        try
        {
            int checks=0;
            void Check(bool condition,string name){Require(condition,"Demo launch: "+name);checks++;}
            var menu=Descendants(this).OfType<MenuButton>().Single(button=>button.Text=="File").GetPopup();
            Check(menu.GetItemText(menu.GetItemIndex(7))=="Open car chase demo","the example is discoverable in the File menu");
            _name.GrabFocus();
            menu.EmitSignal(PopupMenu.SignalName.IdPressed,7L);
            Check(_pilot&&ActiveCameraId==DemoFollowCameraId&&_selectedCamera?.Id==DemoFollowCameraId,"opening the example selects its following camera and enters camera view");
            Check(_fps==25&&Math.Abs(_duration-12)<.0001&&Math.Abs(_time)<.0001,"the shot opens at frame zero with its authored duration and frame rate");
            Check(_projectPath==""&&!_playing&&GetViewport().GuiGetFocusOwner() is not LineEdit,"the demo releases text entry, waits for Play and saves into the user's own copy");
            Check(Descendants(this).OfType<Button>().Any(button=>button.Text=="Camera background…"&&button.IsVisibleInTree()),"the active camera background action is visible immediately");
            _UnhandledInput(new InputEventKey{Pressed=true,Keycode=Key.Space});Check(_playing,"Space starts the ready-made shot without further setup");
            _UnhandledInput(new InputEventKey{Pressed=true,Keycode=Key.Space});Check(!_playing,"Space pauses the shot");
            if(DisplayServer.GetName()!="headless")
            {
                for(int i=0;i<3;i++)await ToSignal(GetTree(),SceneTree.SignalName.ProcessFrame);
                await ToSignal(RenderingServer.Singleton,RenderingServer.SignalName.FramePostDraw);
                Directory.CreateDirectory("output");GetViewport().GetTexture().GetImage().SavePng("output/car-demo-ready.png");
            }
            GD.Print("DEMO_LAUNCH_OK checks="+checks);GetTree().Quit(0);
        }
        catch(Exception ex){GD.PushError("DEMO_LAUNCH_FAILED "+ex);GetTree().Quit(1);}
    }
}
