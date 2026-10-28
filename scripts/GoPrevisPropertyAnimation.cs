using Godot;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;

namespace GoPrevis;

public partial class GoPrevisApp
{
    PropertyAnimationData _sceneAnimation=new();
    bool _propertyEvaluating;
    VBoxContainer? _animatedPropertyInspector;
    LineEdit? _animatedPropertySearch;
    string _propertyInspectorIdentity="";
    readonly List<(AnimationChannelBinding Binding,SpinBox Value,Button Key)> _propertyInspectorFields=new();
    static readonly Color[] AxisCurveColors={new("ef7373"),new("71cf91"),new("77aaff"),new("c7a4ef")};

    AnimationChannelBinding BindProperty(string id,string name,string group,string path,string label,string unit,
        PropertyAnimationData data,Func<double> readBase,Action<double> writeBase,double min=-1e9,double max=1e9,bool discrete=false,
        Func<ScalarAnimationCurve>? migrate=null)
    {
        ScalarAnimationCurve? preview=null;bool previewBuilt=false;
        ScalarAnimationCurve? Curve()
        {
            if(data.Channels.TryGetValue(path,out var curve))return curve;
            if(migrate==null)return null;
            if(!previewBuilt){preview=migrate();previewBuilt=true;}
            return preview;
        }
        ScalarAnimationCurve Ensure()
        {
            if(!data.Channels.TryGetValue(path,out var curve)){curve=PropertyCurveRuntime.Copy(Curve()??new ScalarAnimationCurve());curve.Discrete=discrete;data.Channels[path]=curve;}
            return curve;
        }
        var result=new AnimationChannelBinding{TargetId=id,TargetName=name,Group=group,Path=path,Label=label,Unit=unit,Minimum=min,Maximum=max,Discrete=discrete,
            Color=AxisCurveColors[label=="X"||label=="R"?0:label=="Y"||label=="G"?1:label=="Z"||label=="B"?2:3],Curve=Curve,EnsureCurve=Ensure,
            Read=()=>Math.Clamp(PropertyCurveRuntime.Evaluate(Curve(),_time,readBase()),min,max),Apply=writeBase,BaseRead=readBase,
            Clear=()=>{data.Channels[path]=new ScalarAnimationCurve{Discrete=discrete};}};
        result.Write=value=>WriteAnimationBinding(result,value,writeBase);return result;
    }
    void WriteAnimationBinding(AnimationChannelBinding binding,double value,Action<double> writeBase)
    {
        if(_refreshing||_propertyEvaluating||!double.IsFinite(value))return;
        PushUndo();value=Math.Clamp(binding.Discrete?Math.Round(value):value,binding.Minimum,binding.Maximum);
        if(_autoKey||binding.Curve()?.Keys.Count>0)PropertyCurveRuntime.Upsert(binding.EnsureCurve(),_time,value);
        else writeBase(value);
        SetTime(_time);RefreshAnimatedProperties();
    }
    void KeyAnimationBinding(AnimationChannelBinding binding)
    {
        if(_exportBusy||_aiBusy)return;PushUndo();PropertyCurveRuntime.Upsert(binding.EnsureCurve(),_time,binding.Read());
        SetTime(_time);RefreshAnimatedProperties();Status(binding.TargetName+" · "+binding.Group+" / "+binding.Label+" keyed.");
    }
    IEnumerable<AnimationChannelBinding> BindVector(string id,string name,string group,string prefix,PropertyAnimationData data,
        Func<float[]> read,Action<float[]> write,string unit="",double min=-10000,double max=10000,bool color=false)
    {
        int count=read().Length;
        for(int a=0;a<count;a++)
        {
            int axis=a;string label=(color?"RGBA":"XYZW")[a].ToString();
            yield return BindProperty(id,name,group,prefix+"."+label,label,unit,data,()=>read()[axis],value=>{var v=read().ToArray();v[axis]=(float)value;write(v);},min,max);
        }
    }
    static ScalarAnimationCurve LegacyVectorCurve(List<TransformKeyData> keys,int axis,Func<Vector3,Vector3> convert,bool angle=false)
    {
        var curve=new ScalarAnimationCurve();double? before=null;
        foreach(var key in keys.Where(k=>k.Value?.Length==3).OrderBy(k=>k.Time))
        {
            double value=convert(Vector(key.Value))[axis];
            if(angle&&before.HasValue)value=before.Value+Mathf.Wrap((float)(value-before.Value),-180,180);
            var k=PropertyCurveRuntime.Upsert(curve,key.Time,value);k.Id=key.Id+"/"+axis;k.Interpolation=key.Interpolation.Mode=="Curved"?"Bezier":key.Interpolation.Mode;
            if(k.Interpolation=="Bezier")
            {
                var next=keys.Where(n=>n.Time>key.Time).OrderBy(n=>n.Time).FirstOrDefault();
                if(next!=null){double dt=next.Time-key.Time,dy=angle?Mathf.Wrap((float)(convert(Vector(next.Value))[axis]-value),-180,180):convert(Vector(next.Value))[axis]-value;var c1=key.Interpolation.Control1;var c2=key.Interpolation.Control2;k.OutWeight=c1[0];k.OutSlope=dy*c1[1]/Math.Max(1e-9,dt*c1[0]);var n=PropertyCurveRuntime.Upsert(curve,next.Time,convert(Vector(next.Value))[axis]);n.InWeight=1-c2[0];n.InSlope=dy*(1-c2[1])/Math.Max(1e-9,dt*(1-c2[0]));}
            }
            before=value;
        }
        return curve;
    }
    Vector3 LegacyObjectManual(Entry entry,string group,double time)
    {
        var value=TransformLayerRuntime.EvaluateVector(TransformLayerRuntime.Channel(entry.Data.TransformAnimation,group),time,group=="Scale"?Vector3.One:Vector3.Zero,group=="Rotation");
        return group=="Position"?(entry.Data.Path.Enabled?Vector(entry.Data.PathOffset):Vector(entry.Data.Position))+value:group=="Rotation"?Vector(entry.Data.Rotation)+value:Vector(entry.Data.Scale,Vector3.One)*value;
    }
    Vector3 AnimatedObjectManual(Entry entry,string group,double time)
    {
        var value=LegacyObjectManual(entry,group,time);var basis=group=="Position"?(entry.Data.Path.Enabled?Vector(entry.Data.PathOffset):Vector(entry.Data.Position)):group=="Rotation"?Vector(entry.Data.Rotation):Vector(entry.Data.Scale,Vector3.One);
        return PropertyVector(entry.Data.PropertyAnimation,"Transform."+group,time,value,basis);
    }
    float[] PropertyBaseColor(Entry entry)
    {
        if(entry.Data.Appearance.ColorOverride is {} value)return value;
        var state=EnsureObjectAppearanceState(entry);var first=state.Meshes.FirstOrDefault();
        var material=first?.OriginalOverride??first?.OriginalSurfaces.FirstOrDefault()??first?.Mesh.Mesh?.SurfaceGetMaterial(0);
        var color=material is BaseMaterial3D basic?basic.AlbedoColor:new Color("b8bcc8");return new[]{color.R,color.G,color.B};
    }
    static Vector3 PropertyVector(PropertyAnimationData data,string path,double time,Vector3 fallback,Vector3? emptyFallback=null)
    {
        for(int a=0;a<3;a++)if(data.Channels.TryGetValue(path+"."+"XYZ"[a],out var curve))fallback[a]=(float)PropertyCurveRuntime.Evaluate(curve,time,(emptyFallback??fallback)[a]);
        return fallback;
    }
    List<AnimationChannelBinding> AnimationBindings(string? onlyTarget=null)
    {
        var all=new List<AnimationChannelBinding>();
        foreach(var e in _entries.Where(e=>onlyTarget==null||e.Data.Id==onlyTarget))
        {
            var p=e.Data.PropertyAnimation;string id=e.Data.Id,name=e.Data.Name;
            foreach(string group in new[]{"Position","Rotation","Scale"})for(int a=0;a<3;a++)
            {
                int axis=a;string g=group;var list=TransformLayerRuntime.Channel(e.Data.TransformAnimation,g);
                all.Add(BindProperty(id,name,"Transform/"+g,"Transform."+g+"."+"XYZ"[a],"XYZ"[a].ToString(),g=="Rotation"?"°":g=="Position"?"m":"",p,()=>p.Channels.ContainsKey("Transform."+g+"."+"XYZ"[axis])?(g=="Position"?(e.Data.Path.Enabled?Vector(e.Data.PathOffset):Vector(e.Data.Position)):g=="Rotation"?Vector(e.Data.Rotation):Vector(e.Data.Scale,Vector3.One))[axis]:LegacyObjectManual(e,g,_time)[axis],value=>
                {var v=g=="Position"?(e.Data.Path.Enabled?Vector(e.Data.PathOffset):Vector(e.Data.Position)):g=="Rotation"?Vector(e.Data.Rotation):Vector(e.Data.Scale,Vector3.One);v[axis]=(float)value;if(g=="Position"){if(e.Data.Path.Enabled)e.Data.PathOffset=Array3(v);else e.Data.Position=Array3(v);}else if(g=="Rotation")e.Data.Rotation=Array3(v);else e.Data.Scale=Array3(v);},g=="Scale"?.001:-100000,g=="Scale"?1000:100000,false,
                    ()=>LegacyVectorCurve(list,axis,v=>g=="Scale"?Vector(e.Data.Scale,Vector3.One)*v:(g=="Position"?(e.Data.Path.Enabled?Vector(e.Data.PathOffset):Vector(e.Data.Position)):Vector(e.Data.Rotation))+v,g=="Rotation")));
            }
            all.Add(BindProperty(id,name,"Display","Visible","Visible","",p,()=>e.Data.Visible?1:0,v=>e.Data.Visible=v>=.5,0,1,true));
            all.Add(BindProperty(id,name,"Display","ShowLabel","Show label","",p,()=>e.Data.Appearance.ShowLabel?1:0,v=>e.Data.Appearance.ShowLabel=v>=.5,0,1,true));
            all.AddRange(BindVector(id,name,"Material/Color","Color",p,()=>PropertyBaseColor(e),v=>e.Data.Appearance.ColorOverride=v,"",0,1,true));
            if(e.Data.Light is {} light)
            {
                all.Add(BindProperty(id,name,"Light","Light.Enabled","Enabled","",p,()=>e.Data.Light!.Enabled?1:0,v=>e.Data.Light!.Enabled=v>=.5,0,1,true));
                all.Add(BindProperty(id,name,"Light","Light.Energy","Intensity","",p,()=>e.Data.Light!.Energy,v=>e.Data.Light!.Energy=(float)v,0,16));
                all.Add(BindProperty(id,name,"Light","Light.Range","Range","m",p,()=>e.Data.Light!.Range,v=>e.Data.Light!.Range=(float)v,.1,500));
                all.Add(BindProperty(id,name,"Light","Light.Shadows","Cast shadows","",p,()=>e.Data.Light!.Shadows?1:0,v=>e.Data.Light!.Shadows=v>=.5,0,1,true));
                all.AddRange(BindVector(id,name,"Light/Color","Light.Color",p,()=>e.Data.Light!.Color,v=>e.Data.Light!.Color=v,"",0,1,true));
            }
            if(e.Data.Path.Points.Count>0)all.AddRange(PathPropertyBindings(id,name,"Path","Path",p,()=>e.Data.Path));
            foreach(var clip in e.Data.MotionClips)
            {
                string clipId=clip.Id;MotionClipData CurrentClip()=>e.Data.MotionClips.First(c=>c.Id==clipId);
                string group="Motion/"+(e.Data.MotionClips.IndexOf(clip)+1)+" "+clip.Name;var data=clip.PropertyAnimation;int first=all.Count;
                all.Add(BindProperty(id,name,group,"Speed","Playback rate","×",data,()=>CurrentClip().Speed,v=>{if(v<=0){if(!data.Channels.TryGetValue("Speed",out var rate))data.Channels["Speed"]=rate=new();PropertyCurveRuntime.Upsert(rate,_time,0);}else CurrentClip().Speed=v;RebuildTrack(e);},0,100));
                all.Add(BindProperty(id,name,group,"SourceOffset","Source offset","s",data,()=>CurrentClip().SourceOffset,v=>{CurrentClip().SourceOffset=v;RebuildTrack(e);},0,86400));
                all.Add(BindProperty(id,name,group,"BlendIn","Blend in","s",data,()=>CurrentClip().BlendIn,v=>{CurrentClip().BlendIn=v;RebuildTrack(e);},0,600));
                all.Add(BindProperty(id,name,group,"Loop","Loop","",data,()=>CurrentClip().Loop?1:0,v=>{CurrentClip().Loop=v>=.5;RebuildTrack(e);},0,1,true));
                all.Add(BindProperty(id,name,group,"InPlace","In place","",data,()=>CurrentClip().InPlace?1:0,v=>{CurrentClip().InPlace=v>=.5;RebuildTrack(e);},0,1,true));
                // Clip-local storage uses short paths; make UI identities unique across clips.
                foreach(var b in all.Skip(first)){b.Path="Motion."+clip.Id+"."+b.Path;}
            }
            if(e.Node is RiggedActor||e.NativeTrack!=null)all.AddRange(PoseAnimationBindings(e));
            if(e.Data.Link is {} link)all.AddRange(LinkPropertyBindings(e));
        }
        foreach(var c in _cameras.Where(c=>onlyTarget==null||c.Id==onlyTarget))all.AddRange(CameraPropertyBindings(c));
        if(onlyTarget==null||onlyTarget=="scene")all.AddRange(ScenePropertyBindings());return all;
    }
    IEnumerable<AnimationChannelBinding> PathPropertyBindings(string id,string name,string group,string prefix,PropertyAnimationData p,Func<ActorPathData> path)
    {
        yield return BindProperty(id,name,group,prefix+".Progress","Path progress","0–1",p,()=>ActorPathRuntime.Progress(path(),_time),v=>path().ProgressOverride=(float)v,0,1);
        yield return BindProperty(id,name,group,prefix+".Enabled","Enabled","",p,()=>path().Enabled?1:0,v=>path().Enabled=v>=.5,0,1,true);
        yield return BindProperty(id,name,group,prefix+".OrientToPath","Face travel direction","",p,()=>path().OrientToPath?1:0,v=>path().OrientToPath=v>=.5,0,1,true);
        yield return BindProperty(id,name,group,prefix+".Start","Start","s",p,()=>path().Start,v=>path().Start=v,0,600);
        yield return BindProperty(id,name,group,prefix+".Duration","Travel time","s",p,()=>path().Duration,v=>SetAnimatedPathDuration(path(),v),.01,600);
        for(int i=0;i<path().Points.Count;i++)
        {int point=i;foreach(var b in BindVector(id,name,group+"/Point "+(i+1),prefix+".Points."+i,p,()=>path().Points[point],v=>path().Points[point]=v,"m"))yield return b;}
    }
    IEnumerable<AnimationChannelBinding> LinkPropertyBindings(Entry e)
    {
        var p=e.Data.PropertyAnimation;
        foreach(var b in BindVector(e.Data.Id,e.Data.Name,"Link/Start offset","Link.StartOffset",p,()=>e.Data.Link!.StartOffset,v=>e.Data.Link!.StartOffset=v,"m"))yield return b;
        foreach(var b in BindVector(e.Data.Id,e.Data.Name,"Link/End offset","Link.EndOffset",p,()=>e.Data.Link!.EndOffset,v=>e.Data.Link!.EndOffset=v,"m"))yield return b;
    }
}
