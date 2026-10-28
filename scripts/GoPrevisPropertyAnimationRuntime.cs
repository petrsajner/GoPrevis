using Godot;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text.Json;

namespace GoPrevis;

public partial class GoPrevisApp
{
    IEnumerable<AnimationChannelBinding> CameraPropertyBindings(CameraEntry c)
    {
        var p=c.PropertyAnimation;
        foreach(string group in new[]{"Position","Rotation","Scale"})for(int a=0;a<3;a++)
        {
            int axis=a;string g=group;var list=TransformLayerRuntime.Channel(c.Animation,g);
            Vector3 Basis()=>g=="Position"?c.BasePosition:g=="Rotation"?c.BaseRotation:c.BaseScale;
            yield return BindProperty(c.Id,c.Name,"Transform/"+g,"Transform."+g+"."+"XYZ"[a],"XYZ"[a].ToString(),g=="Rotation"?"°":g=="Position"?"m":"",p,
                ()=>p.Channels.ContainsKey("Transform."+g+"."+"XYZ"[axis])?Basis()[axis]:TransformLayerRuntime.EvaluateVector(list,_time,Basis(),g=="Rotation")[axis],value=>{var v=Basis();v[axis]=(float)value;if(g=="Position")c.BasePosition=v;else if(g=="Rotation")c.BaseRotation=v;else c.BaseScale=v;},g=="Scale"?.001:-100000,g=="Scale"?1000:100000,false,()=>LegacyVectorCurve(list,axis,v=>v,g=="Rotation"));
        }
        yield return BindProperty(c.Id,c.Name,"Camera","Lens","Focal length","mm",p,()=>p.Channels.ContainsKey("Lens")?c.BaseLens:EvaluateLensLegacy(c,_time),v=>c.BaseLens=(float)v,8,300,false,()=>
        {var curve=new ScalarAnimationCurve();foreach(var k in c.LensKeys){var key=PropertyCurveRuntime.Upsert(curve,k.Time,k.Value);key.Id=k.Id;key.Interpolation=k.Interpolation.Mode=="Curved"?"Bezier":k.Interpolation.Mode;}return curve;});
        yield return BindProperty(c.Id,c.Name,"Display","ShowLabel","Show label","",p,()=>c.ShowLabel?1:0,v=>c.ShowLabel=v>=.5,0,1,true);
        foreach(var b in ReflectPropertyBindings(c.Id,c.Name,"Follow / Look at","Tracking",p,()=>c.Tracking))yield return b;
        foreach(var b in ReflectPropertyBindings(c.Id,c.Name,"Camera movement","Movement",p,()=>c.Movement))yield return b;
        foreach(var b in ReflectPropertyBindings(c.Id,c.Name,"Camera background","Reference",p,()=>c.Reference))yield return b;
        foreach(string group in new[]{"Position","Rotation"})foreach(var b in BindVector(c.Id,c.Name,"Final corrections/"+group,"Correction."+group,p,()=>Array3(group=="Position"?c.PositionCorrection:c.RotationCorrection),v=>{if(group=="Position")c.PositionCorrection=Vector(v);else c.RotationCorrection=Vector(v);},group=="Position"?"m":"°"))yield return b;
    }
    bool _studioKeyBase;
    bool StudioKeyBase=>_sceneAnimation.Channels.ContainsKey("Scene.StudioKeyEnabled")?_studioKeyBase:_lighting.DirectionalEnabled;
    void SetStudioKeyBase(bool value){_studioKeyBase=value;_lighting.DirectionalEnabled=value;}
    IEnumerable<AnimationChannelBinding> ScenePropertyBindings()
    {
        foreach(var b in ReflectPropertyBindings("scene","3D Scene","Scene","Scene",_sceneAnimation,()=>_sceneSettings))yield return b;
        var studio=BindProperty("scene","3D Scene","Scene/Lighting","Scene.StudioKeyEnabled","Studio key light","",_sceneAnimation,()=>StudioKeyBase?1:0,v=>SetStudioKeyBase(v>=.5),0,1,true);
        var ensure=studio.EnsureCurve;studio.EnsureCurve=()=>{if(!_sceneAnimation.Channels.ContainsKey("Scene.StudioKeyEnabled"))_studioKeyBase=_lighting.DirectionalEnabled;return ensure();};yield return studio;
    }

    static (double Min,double Max,string Unit) PropertyLimits(string path)
    {
        string name=path.Split('.').Last();
        if(path.Contains("Color"))return(0,1,"");
        return name switch{
            "Brightness"=>(0,4,"×"),"Opacity" or "GroundOpacity"=>(0,1,""),"Energy"=>(0,16,""),"Range"=>(.1,500,"m"),
            "FollowSmoothness" or "LookAtSmoothness"=>(0,5,"s"),"Radius"=>(.1,2000,"m"),"Start"=>(0,600,"s"),"Duration"=>(.01,600,"s"),
            "OffsetSeconds"=>(-86400,86400,"s"),"StartAngleDegrees"=>(-36000,36000,"°"),"OrbitDegrees"=>(-3600,3600,"°"),
            "StartHeight" or "EndHeight" or "GroundHeight"=>(-1000,1000,"m"),_=>(-10000,10000,"")};
    }
    static void SetAnimatedPathDuration(ActorPathData path,double duration)
    {
        double ratio=duration/Math.Max(.000001,path.Duration);for(int i=0;i<path.PointTimes.Count;i++)path.PointTimes[i]*=ratio;path.Duration=duration;
    }
    IEnumerable<AnimationChannelBinding> ReflectPropertyBindings(string id,string name,string group,string prefix,PropertyAnimationData p,Func<object> root,int depth=0)
    {
        if(depth>4)yield break;
        foreach(var field in root().GetType().GetProperties(BindingFlags.Instance|BindingFlags.Public).Where(f=>f.CanRead&&f.CanWrite))
        {
            string path=prefix+"."+field.Name,label=System.Text.RegularExpressions.Regex.Replace(field.Name,"(?<=[a-z])([A-Z])"," $1");var limits=PropertyLimits(path);
            Type type=field.PropertyType;
            if(root() is ActorPathData&&field.Name=="ProgressOverride")yield return BindProperty(id,name,group,prefix+".Progress","Path progress","0–1",p,()=>ActorPathRuntime.Progress((ActorPathData)root(),_time),v=>((ActorPathData)root()).ProgressOverride=(float)v,0,1);
            else if(type==typeof(bool))yield return BindProperty(id,name,group,path,label,"",p,()=>Convert.ToBoolean(field.GetValue(root()))?1:0,v=>field.SetValue(root(),v>=.5),0,1,true);
            else if(type==typeof(float)||type==typeof(double)||type==typeof(int))
                yield return BindProperty(id,name,group,path,label,limits.Unit,p,()=>Convert.ToDouble(field.GetValue(root())),v=>
                {
                    if(root() is ActorPathData route&&field.Name=="Duration")SetAnimatedPathDuration(route,v);
                    else if(root() is CameraMovementData move&&field.Name=="Duration"){SetAnimatedPathDuration(move.Path,v);SetAnimatedPathDuration(move.AimPath,v);move.Duration=v;}
                    else if(root() is CameraMovementData moved&&field.Name=="Start"){moved.Start=v;moved.Path.Start=v;moved.AimPath.Start=v;}
                    else field.SetValue(root(),Convert.ChangeType(v,type));
                },limits.Min,limits.Max);
            else if(type==typeof(float[]))
            {
                var current=field.GetValue(root()) as float[];if(current==null||current.Length>4)continue;bool color=field.Name.Contains("Color");
                foreach(var b in BindVector(id,name,group+"/"+label,path,p,()=>(float[])field.GetValue(root())!,v=>field.SetValue(root(),v),color?"":field.Name.Contains("Rotation")?"°":"m",color?0:-10000,color?1:10000,color))yield return b;
            }
            else if(type==typeof(string)&&field.Name is "Mode" or "Easing")
            {
                string[] options=field.Name=="Mode"?new[]{"None","Orbit","Path"}:KeyInterpolation.Modes;
                yield return BindProperty(id,name,group,path,label+" ("+string.Join(" / ",options)+")","",p,()=>Math.Max(0,Array.IndexOf(options,(string?)field.GetValue(root()))),v=>field.SetValue(root(),options[Math.Clamp((int)Math.Round(v),0,options.Length-1)]),0,options.Length-1,true);
            }
            else if(type==typeof(ActorPathData))
            {
                foreach(var b in ReflectPropertyBindings(id,name,group+"/"+label,path,p,()=>field.GetValue(root())!,depth+1))yield return b;
            }
            else if(type==typeof(List<float[]>))
            {
                var points=(List<float[]>)field.GetValue(root())!;
                for(int i=0;i<points.Count;i++){int point=i;foreach(var b in BindVector(id,name,group+"/"+label+"/Point "+(i+1),path+"."+i,p,()=>((List<float[]>)field.GetValue(root())!)[point],v=>((List<float[]>)field.GetValue(root())!)[point]=v,"m"))yield return b;}
            }
        }
    }
    sealed class AnimatedStateScope:IDisposable
    {
        readonly List<Action> _restore=new();
        public void Add(Action restore)=>_restore.Add(restore);
        public void Dispose(){for(int i=_restore.Count-1;i>=0;i--)_restore[i]();_restore.Clear();}
    }
    static T CloneAnimationState<T>(T value)=>JsonSerializer.Deserialize<T>(JsonSerializer.Serialize(value))!;
    static bool GeneralProperty(string path)=>!path.StartsWith("Transform.")&&!path.StartsWith("Pose.")&&!path.StartsWith("Motion.")&&!path.StartsWith("Correction.")&&path!="Lens";
    static readonly MethodInfo AnimationMemberClone=typeof(object).GetMethod("MemberwiseClone",BindingFlags.Instance|BindingFlags.NonPublic)!;
    static ObjectData ClonePropertyEvaluation(ObjectData source)
    {
        var result=(ObjectData)AnimationMemberClone.Invoke(source,null)!;
        if(source.PropertyAnimation.Channels.Keys.Any(k=>k.StartsWith("Path.")))result.Path=CloneAnimationState(source.Path);result.Appearance=CloneAnimationState(source.Appearance);
        result.Light=source.Light?.ValidatedCopy();result.Link=source.Link==null?null:CloneAnimationState(source.Link);
        return result;
    }
    AnimatedStateScope EvaluateEntryPropertyState(Entry entry,double time)
    {
        var scope=new AnimatedStateScope();if(!entry.Data.PropertyAnimation.Channels.Keys.Any(GeneralProperty))return scope;
        var original=entry.Data;entry.Data=ClonePropertyEvaluation(original);scope.Add(()=>entry.Data=original);
        foreach(var b in AnimationBindings(entry.Data.Id).Where(b=>GeneralProperty(b.Path)))if(b.Curve() is {Keys.Count:>0} curve)b.Apply?.Invoke(Math.Clamp(PropertyCurveRuntime.Evaluate(curve,time,b.Read()),b.Minimum,b.Maximum));
        return scope;
    }
    AnimatedStateScope EvaluatePropertyState(double time,bool visuals)
    {
        var scope=new AnimatedStateScope();bool previous=_propertyEvaluating;_propertyEvaluating=true;scope.Add(()=>_propertyEvaluating=previous);
        bool studioBase=_studioKeyBase;scope.Add(()=>_studioKeyBase=studioBase);
        if(!_sceneAnimation.Channels.Any()&&!_entries.Any(e=>e.Data.PropertyAnimation.Channels.Keys.Any(GeneralProperty))&&!_cameras.Any(c=>c.PropertyAnimation.Channels.Keys.Any(GeneralProperty)))
        {if(visuals)foreach(var e in _entries)e.Node.Visible=e.Data.Visible;return scope;}
        foreach(var e in _entries)
        {
            if(e.Data.PropertyAnimation.Channels.Keys.Any(GeneralProperty))
            {var original=e.Data;e.Data=ClonePropertyEvaluation(original);scope.Add(()=>e.Data=original);}
        }
        foreach(var c in _cameras)
        {
            if(c.PropertyAnimation.Channels.Any(x=>x.Value.Keys.Count>0&&!x.Key.StartsWith("Transform.")&&!x.Key.StartsWith("Correction.")&&x.Key!="Lens"))
            {var tracking=c.Tracking;var movement=c.Movement;var reference=c.Reference;bool labels=c.ShowLabel;c.Tracking=CloneAnimationState(tracking);c.Movement=CloneAnimationState(movement);c.Reference=CloneAnimationState(reference);scope.Add(()=>{c.Tracking=tracking;c.Movement=movement;c.Reference=reference;c.ShowLabel=labels;});}
        }
        var scene=_sceneSettings;if(_sceneAnimation.Channels.Any(x=>x.Value.Keys.Count>0)){_sceneSettings=CloneAnimationState(scene);scope.Add(()=>_sceneSettings=scene);}
        foreach(var binding in AnimationBindings())
        {
            if(binding.Path.StartsWith("Transform.")||binding.Path.StartsWith("Correction.")||binding.Path.StartsWith("Pose.")||binding.Path.StartsWith("Motion.")||binding.Path=="Lens")continue;
            if(binding.Curve() is {Keys.Count:>0} curve)binding.Apply?.Invoke(Math.Clamp(PropertyCurveRuntime.Evaluate(curve,time,binding.Read()),binding.Minimum,binding.Maximum));
        }
        if(visuals)
        {
            if(_sceneAnimation.Channels.Count>0)ApplySceneSettings(_sceneSettings);
            foreach(var e in _entries){e.Node.Visible=e.Data.Visible;if(e.Data.PropertyAnimation.Channels.Count>0){ApplySceneLight(e);ApplyObjectAppearance(e);}}
        }
        return scope;
    }
    Vector3 AnimatedCameraVector(CameraEntry c,string group,double time,Vector3 fallback)
        =>PropertyVector(c.PropertyAnimation,"Transform."+group,time,fallback,group=="Position"?c.BasePosition:group=="Rotation"?c.BaseRotation:c.BaseScale);
    void ApplyCameraPropertyCorrections(CameraEntry c,double time)
    {
        c.Rig.GlobalPosition+=PropertyVector(c.PropertyAnimation,"Correction.Position",time,c.PositionCorrection);
        c.Rig.Quaternion=(c.Rig.Quaternion*Quaternion.FromEuler(PropertyVector(c.PropertyAnimation,"Correction.Rotation",time,c.RotationCorrection)*Mathf.Pi/180)).Normalized();
    }
}
