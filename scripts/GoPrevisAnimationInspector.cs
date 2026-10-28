using Godot;
using System;
using System.Collections.Generic;
using System.Linq;

namespace GoPrevis;

public partial class GoPrevisApp
{
    void BuildAnimatedPropertyInspector()
    {
        var box=new VBoxContainer();_inspector.AddChild(box);
        var header=Button("▸ Animate parameters",()=>{},true);header.Alignment=HorizontalAlignment.Left;box.AddChild(header);
        _animatedPropertyInspector=new VBoxContainer{Visible=false};box.AddChild(_animatedPropertyInspector);
        header.Pressed+=()=>{_animatedPropertyInspector.Visible=!_animatedPropertyInspector.Visible;header.Text=(_animatedPropertyInspector.Visible?"▾ ":"▸ ")+"Animate parameters";RefreshAnimatedPropertyInspector(true);};
        _animatedPropertySearch=new LineEdit{PlaceholderText="Find a parameter…",ClearButtonEnabled=true};_animatedPropertyInspector.AddChild(_animatedPropertySearch);
        _animatedPropertySearch.TextChanged+=_=>RefreshAnimatedPropertyInspector(true);
        var hint=Text("◇ keys this value. Animated values update at the playhead. Select channels below to edit their curves.",10,true);hint.AutowrapMode=TextServer.AutowrapMode.WordSmart;_animatedPropertyInspector.AddChild(hint);
    }
    void RefreshAnimatedProperties()
    {
        RefreshAnimationWorkspace();RefreshAnimatedPropertyInspector();
    }
    void RefreshAnimatedPropertyInspector(bool force=false)
    {
        if(_animatedPropertyInspector==null||!_animatedPropertyInspector.Visible)return;
        string id=_selectedCamera?.Id??_selected?.Data.Id??"scene";
        var bindings=AnimationBindings().Where(b=>b.TargetId==id&&($"{b.Group} {b.Label}".Contains(_animatedPropertySearch!.Text,StringComparison.OrdinalIgnoreCase))).ToList();
        string identity=id+"/"+_animatedPropertySearch!.Text+"/"+string.Join('|',bindings.Select(b=>b.Identity));
        if(force||_propertyInspectorIdentity!=identity)
        {
            foreach(var child in _animatedPropertyInspector.GetChildren().Skip(2).ToArray()){_animatedPropertyInspector.RemoveChild(child);child.QueueFree();}
            _propertyInspectorIdentity=identity;_propertyInspectorFields.Clear();string group="";
            foreach(var binding in bindings)
            {
                if(group!=binding.Group){group=binding.Group;_animatedPropertyInspector.AddChild(Text(group.Replace("/"," · " ),10,true));}
                var b=binding;var value=Spin(b.Minimum,b.Maximum,b.Discrete?1:b.Unit=="°"?.1:.001,b.Read());value.CustomMinimumSize=new Vector2(76,24);value.SizeFlagsHorizontal=SizeFlags.ExpandFill;
                value.ValueChanged+=v=>AnimationBindings().FirstOrDefault(current=>current.Identity==b.Identity)?.Write?.Invoke(v);
                var key=Button("◇",()=>{var current=AnimationBindings().FirstOrDefault(current=>current.Identity==b.Identity);if(current!=null)KeyAnimationBinding(current);});key.CustomMinimumSize=new Vector2(24,24);key.TooltipText="Key "+b.Group+" / "+b.Label;
                var label=Text(b.Label+(b.Unit==""?"":" ("+b.Unit+")"),10);label.ClipText=true;label.CustomMinimumSize=new Vector2(78,0);
                _animatedPropertyInspector.AddChild(Row(label,value,key));_propertyInspectorFields.Add((b,value,key));
            }
        }
        bool previous=_refreshing;_refreshing=true;
        try{var currentMap=bindings.ToDictionary(b=>b.Identity);foreach(var (oldBinding,value,key) in _propertyInspectorFields){if(!currentMap.TryGetValue(oldBinding.Identity,out var binding))continue;value.Value=binding.Read();key.Text=binding.Curve()?.Keys.Any(k=>Math.Abs(k.Time-_time)<.5/_fps)==true?"◆":"◇";}}
        finally{_refreshing=previous;}
    }
    void KeySelectedTransformAxis(string group,int axis)
    {
        string? id=_selectedCamera?.Id??_selected?.Data.Id;if(id==null)return;
        var b=SelectedAxisBinding(group,axis);
        if(b!=null)KeyAnimationBinding(b);
    }
    bool HasPropertyTransform(string group)
    {
        var p=_selectedCamera?.PropertyAnimation??_selected?.Data.PropertyAnimation;
        return p!=null&&p.Channels.Keys.Any(k=>k.StartsWith("Transform."+group+".",StringComparison.Ordinal));
    }
    bool TryWritePropertyTransform(string group,Vector3 value,int? onlyAxis=null)
    {
        if(!_autoKey&&!HasPropertyTransform(group))return false;
        string id=_selectedCamera?.Id??_selected?.Data.Id??"";if(id=="")return false;
        var channels=AnimationBindings().Where(b=>b.TargetId==id&&b.Path.StartsWith("Transform."+group+".")).ToList();
        for(int axis=0;axis<3;axis++)
        {
            if(onlyAxis.HasValue&&onlyAxis!=axis)continue;
            var binding=channels.First(b=>b.Label=="XYZ"[axis].ToString());
            if(onlyAxis==null&&Math.Abs(binding.Read()-value[axis])<.0000001)continue;
            SetAnimationBindingValue(binding,value[axis]);
        }
        SetTime(_time);return true;
    }
    void SetAnimationBindingValue(AnimationChannelBinding binding,double value)
    {
        value=Math.Clamp(value,binding.Minimum,binding.Maximum);
        if(_autoKey||binding.Curve()?.Keys.Count>0)PropertyCurveRuntime.Upsert(binding.EnsureCurve(),_time,value);
        else binding.Apply?.Invoke(value);
    }
    bool CapturePropertyTransform(string group,bool history=true)
    {
        string id=_selectedCamera?.Id??_selected?.Data.Id??"";if(id=="")return false;
        if(history)PushUndo();var channels=AnimationBindings().Where(b=>b.TargetId==id&&(group=="All"?b.Path.StartsWith("Transform.")||b.Path=="Lens":group=="Lens"?b.Path=="Lens":b.Path.StartsWith("Transform."+group+"."))).ToList();
        if(_selectedCamera is {} camera)
        {
            channels.RemoveAll(b=>b.Path.StartsWith("Transform.Position.")&&LiveAnimationPath(camera,"Position")!=null||b.Path.StartsWith("Transform.Rotation.")&&LiveAnimationPath(camera,"Rotation")!=null);
            foreach(string transform in group=="All"?new[]{"Position","Rotation"}:new[]{group})if(LiveAnimationPath(camera,transform) is string livePath)channels.AddRange(AnimationBindings().Where(b=>b.TargetId==id&&b.Path.StartsWith(livePath+".")));
        }
        foreach(var b in channels)PropertyCurveRuntime.Upsert(b.EnsureCurve(),_time,b.Read());
        SetTime(_time);RefreshAnimatedProperties();Status(group+" key added at "+_time.ToString("0.###")+" s.");return true;
    }
}
