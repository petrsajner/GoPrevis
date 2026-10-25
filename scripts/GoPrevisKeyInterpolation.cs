using Godot;
using System;
using System.Collections.Generic;
using System.Linq;

namespace GoPrevis;

public partial class GoPrevisApp
{
    sealed record InterpolationSelection(string Kind,string Target,string Channel,double Time,int Point=-1);
    InterpolationSelection? _interpolationSelection;
    VBoxContainer _keyInterpolationPanel=null!;
    OptionButton _keyInterpolationMode=null!;
    Label _keyInterpolationTitle=null!,_keyInterpolationHint=null!;
    TimingCurveView _keyTimingCurve=null!;
    bool _refreshingInterpolation;

    void BuildKeyInterpolationInspector()
    {
        _keyInterpolationPanel=new VBoxContainer{Visible=false};_keyInterpolationPanel.AddThemeConstantOverride("separation",5);_inspector.AddChild(_keyInterpolationPanel);
        _keyInterpolationTitle=Text("KEYFRAME",11,true);_keyInterpolationPanel.AddChild(_keyInterpolationTitle);
        _keyInterpolationMode=new OptionButton{FitToLongestItem=false,ClipText=true,CustomMinimumSize=new Vector2(132,28),SizeFlagsHorizontal=SizeFlags.ExpandFill};foreach(string mode in KeyInterpolation.Modes)_keyInterpolationMode.AddItem(mode);
        _keyInterpolationMode.TooltipText="Timing from the selected key to the next key. Hold keeps its value until the next key.";
        _keyInterpolationMode.ItemSelected+=index=>{if(_refreshingInterpolation||index<0||index>=KeyInterpolation.Modes.Length)return;ChangeKeyInterpolation(data=>data.Mode=KeyInterpolation.Modes[index]);};
        _keyInterpolationPanel.AddChild(Row(Text("Interpolation",11,true),_keyInterpolationMode));
        _keyTimingCurve=new TimingCurveView{CustomMinimumSize=new Vector2(0,106),SizeFlagsHorizontal=SizeFlags.ExpandFill,MouseFilter=MouseFilterEnum.Stop};
        _keyTimingCurve.GestureStarted=BeginNumericGesture;_keyTimingCurve.GestureEnded=EndNumericGesture;
        _keyTimingCurve.Changed=data=>ChangeKeyInterpolation(target=>{target.Mode="Curved";target.Control1=data.Control1;target.Control2=data.Control2;});_keyInterpolationPanel.AddChild(_keyTimingCurve);
        _keyInterpolationHint=Text("",11,true);_keyInterpolationHint.AutowrapMode=TextServer.AutowrapMode.WordSmart;_keyInterpolationPanel.AddChild(_keyInterpolationHint);
    }

    void SelectKeyInterpolation(string kind,Entry? entry,CameraEntry? camera,string channel,double time,int point=-1)
    {
        _interpolationSelection=new(kind,camera?.Id??entry!.Data.Id,channel,time,point);RefreshKeyInterpolationInspector();
    }

    IEnumerable<(KeyInterpolationData Data,Action<KeyInterpolationData> Set)> InterpolationTargets()
    {
        var selection=_interpolationSelection;if(selection==null)yield break;
        var entry=_entries.FirstOrDefault(e=>e.Data.Id==selection.Target);
        var camera=_cameras.FirstOrDefault(c=>c.Id==selection.Target);
        if(entry!=null&&_selected!=entry||camera!=null&&_selectedCamera!=camera||entry==null&&camera==null)yield break;
        bool At(double time)=>Math.Abs(time-selection.Time)<.00001;
        if(selection.Kind=="Path"&&entry!=null)
        {
            var path=entry.Data.Path;int point=selection.Point;
            if(point<0||point>=path.Points.Count)yield break;
            yield return (path.PointInterpolation!=null&&point<path.PointInterpolation.Count?path.PointInterpolation[point]??new():new(),data=>{path.PointInterpolation??=new();while(path.PointInterpolation.Count<=point)path.PointInterpolation.Add(new());path.PointInterpolation[point]=data;});yield break;
        }
        if(selection.Kind=="Pose"&&entry!=null)
        {
            var pose=entry.Data.PoseCorrections;
            if(selection.Channel is "All" or "Root")foreach(var key in pose.Root.Where(k=>At(k.Time)))yield return(key.Interpolation??new(),data=>key.Interpolation=data);
            foreach(var pair in pose.Bones.Where(pair=>selection.Channel=="All"||pair.Key==selection.Channel))
                foreach(var key in pair.Value.Where(k=>At(k.Time)))yield return(key.Interpolation??new(),data=>key.Interpolation=data);
            yield break;
        }
        var animation=camera?.Animation??entry?.Data.TransformAnimation;if(animation==null)yield break;
        foreach(string channel in selection.Channel=="All"?new[]{"Position","Rotation","Scale"}:selection.Channel=="Lens"?Array.Empty<string>():new[]{selection.Channel})
            foreach(var key in TransformLayerRuntime.Channel(animation,channel).Where(k=>At(k.Time)))yield return(key.Interpolation??new(),data=>key.Interpolation=data);
        if(camera!=null&&selection.Channel is "All" or "Lens")foreach(var key in camera.LensKeys.Where(k=>At(k.Time)))yield return(key.Interpolation??new(),data=>key.Interpolation=data);
    }

    void ChangeKeyInterpolation(Action<KeyInterpolationData> edit)
    {
        if(_refreshingInterpolation)return;var targets=InterpolationTargets().ToArray();if(targets.Length==0)return;
        PushUndo();foreach(var target in targets){var data=target.Data.Copy();edit(data);target.Set(data);}
        SetTime(_time);RefreshKeyInterpolationInspector();Status("Key timing updated. "+InterpolationDescription(InterpolationTargets().First().Data.Mode));
    }

    static string InterpolationDescription(string mode)=>mode switch
    {
        "Ease in/out"=>"Accelerate smoothly, then slow down into the next key.",
        "Curved"=>"Drag the two handles to shape timing to the next key.",
        "Stop"=>"Come to rest at each end, with zero speed and acceleration.",
        "Hold"=>"Keep this value until the next key, then change instantly.",
        _=>"Travel at a constant rate to the next key."
    };

    void RefreshKeyInterpolationInspector()
    {
        if(_keyInterpolationPanel==null)return;var targets=InterpolationTargets().ToArray();bool context=_interpolationSelection?.Kind switch{"Path"=>_inspectorSection=="Path","Pose"=>_inspectorSection=="Pose",_=>_inspectorSection=="Object"};bool show=targets.Length>0&&context&&!_drawingPath;
        _keyInterpolationPanel.Visible=show;if(!show)return;
        var selection=_interpolationSelection!;var data=targets[0].Data;bool old=_refreshingInterpolation;_refreshingInterpolation=true;
        _keyInterpolationTitle.Text=$"KEYFRAME · {(selection.Kind=="Path"?"Path point "+(selection.Point+1):selection.Channel)} · {selection.Time:0.###} s";
        int choice=Array.IndexOf(KeyInterpolation.Modes,data.Mode);_keyInterpolationMode.Select(Math.Max(0,choice));
        bool mixed=targets.Any(target=>target.Data.Mode!=data.Mode);_keyInterpolationHint.Text=(mixed?"Multiple timing types. Choose one for this group. ":"")+InterpolationDescription(data.Mode);
        _keyTimingCurve.Value=data.Copy();_keyTimingCurve.Editable=data.Mode=="Curved";_keyTimingCurve.Visible=data.Mode is "Curved" or "Ease in/out" or "Stop";_keyTimingCurve.QueueRedraw();_refreshingInterpolation=old;
    }

    sealed partial class TimingCurveView:Control
    {
        public KeyInterpolationData Value=new();public bool Editable;
        public Action? GestureStarted,GestureEnded;public Action<KeyInterpolationData>? Changed;
        int _handle=-1;
        Rect2 Graph=>new(new Vector2(16,12),new Vector2(Math.Max(1,Size.X-32),Math.Max(1,Size.Y-28)));
        Vector2 Point(float x,float y)=>Graph.Position+new Vector2(x,1-y)*Graph.Size;
        public override void _Draw()
        {
            var r=Graph;DrawStyleBox(new StyleBoxFlat{BgColor=new Color("1b1b22"),CornerRadiusTopLeft=5,CornerRadiusTopRight=5,CornerRadiusBottomLeft=5,CornerRadiusBottomRight=5},new Rect2(Vector2.Zero,Size));
            for(int i=0;i<5;i++){float v=i/4f;DrawLine(Point(v,0),Point(v,1),new Color("34333c"));DrawLine(Point(0,v),Point(1,v),new Color("34333c"));}
            var previous=Point(0,0);for(int i=1;i<=64;i++){float t=i/64f;var point=Point(t,KeyInterpolation.Weight(Value,t));DrawLine(previous,point,new Color("c9b5ff"),2,true);previous=point;}
            if(!Editable)return;var a=KeyInterpolation.Control(Value.Control1,.25f,.1f);var b=KeyInterpolation.Control(Value.Control2,.25f,1);
            DrawLine(Point(0,0),Point(a[0],a[1]),new Color("b69c73"),1,true);DrawLine(Point(1,1),Point(b[0],b[1]),new Color("b69c73"),1,true);
            DrawCircle(Point(a[0],a[1]),5,new Color("efbf78"));DrawCircle(Point(b[0],b[1]),5,new Color("efbf78"));
        }
        public override void _GuiInput(InputEvent input)
        {
            if(!Editable)return;
            if(input is InputEventMouseButton{ButtonIndex:MouseButton.Left} button)
            {
                if(!button.Pressed){if(_handle>=0){_handle=-1;GestureEnded?.Invoke();AcceptEvent();}return;}
                var a=KeyInterpolation.Control(Value.Control1,.25f,.1f);var b=KeyInterpolation.Control(Value.Control2,.25f,1);
                float first=button.Position.DistanceTo(Point(a[0],a[1])),second=button.Position.DistanceTo(Point(b[0],b[1]));
                if(Math.Min(first,second)>13)return;_handle=first<=second?0:1;GestureStarted?.Invoke();AcceptEvent();
            }
            else if(input is InputEventMouseMotion motion&&_handle>=0)
            {
                var point=(motion.Position-Graph.Position)/Graph.Size;var data=Value.Copy();var xy=new[]{Math.Clamp(point.X,0,1),Math.Clamp(1-point.Y,0,1)};
                if(_handle==0)data.Control1=xy;else data.Control2=xy;Value=data;Changed?.Invoke(data);QueueRedraw();AcceptEvent();
            }
        }
        public override void _Input(InputEvent input)
        {if(_handle>=0&&input is InputEventMouseButton{ButtonIndex:MouseButton.Left,Pressed:false}){_handle=-1;GestureEnded?.Invoke();}}
        public override void _ExitTree(){if(_handle>=0)GestureEnded?.Invoke();}
    }
}
