using Godot;
using System;
using System.Linq;
using System.IO;

namespace Previs;

public partial class PrevisApp
{
    SubViewportContainer CreateSceneViewportContainer()=>new SceneDropViewport{App=this};
    partial class SceneDropViewport:SubViewportContainer
    {
        public PrevisApp App=null!;
        public override bool _CanDropData(Vector2 position,Variant data)
        {
            if(data.VariantType!=Variant.Type.Dictionary)return false;var d=data.AsGodotDictionary();if(!d.ContainsKey("previs_type"))return false;
            if(d["previs_type"].AsString()=="asset")return true;
            if(d["previs_type"].AsString()=="pose")return App.FindLibraryPose(d["id"].AsString()) is {} pose&&(App.PickMotionTarget(position) is not {} poseTarget||App.CompatiblePose(poseTarget,pose));
            var motion=MotionCatalog.Find(d["id"].AsString());var target=App.PickMotionTarget(position);return motion!=null&&(target==null||App.CompatibleMotion(target,motion));
        }
        public override void _DropData(Vector2 position,Variant data)
        {
            var d=data.AsGodotDictionary();try{if(d["previs_type"].AsString()=="asset")App.DropAsset(d["id"].AsString(),App.GroundPoint(position));else if(d["previs_type"].AsString()=="pose")App.DropPose(d["id"].AsString(),App.PickMotionTarget(position),App.GroundPoint(position));else App.DropMotion(d["id"].AsString(),App.PickMotionTarget(position),App.GroundPoint(position));}catch(Exception ex){App.Error(ex.Message);}
        }
    }
    partial class LibraryCard:Button
    {
        public string Kind="asset",Id="",Caption="";
        public override Variant _GetDragData(Vector2 atPosition)
        {
            var preview=new Label{Text=Caption==""?Text:Caption,Modulate=new Color("e9e5f4")};SetDragPreview(preview);
            return new Godot.Collections.Dictionary{{"previs_type",Kind},{"id",Id}};
        }
    }
    Vector3 GroundPoint(Vector2 screen)
    {
        var origin=_editorCamera.ProjectRayOrigin(screen);var direction=_editorCamera.ProjectRayNormal(screen);if(Math.Abs(direction.Y)<.0001f)return Vector3.Zero;float distance=(SceneGroundHeight-origin.Y)/direction.Y;return distance>0?origin+direction*distance:Vector3.Zero;
    }
    CameraEntry? PickCamera(Vector2 point)
    {
        var start=_editorCamera.ProjectRayOrigin(point);var hit=_world.GetWorld3D().DirectSpaceState.IntersectRay(PhysicsRayQueryParameters3D.Create(start,start+_editorCamera.ProjectRayNormal(point)*500,1));
        if(hit.Count>0&&hit["collider"].AsGodotObject() is Node n&&n.HasMeta("camera_id")){string id=n.GetMeta("camera_id").AsString();return _cameras.FirstOrDefault(c=>c.Id==id);}return null;
    }
    Entry? PickMotionTarget(Vector2 point)
    {
        var physical=Pick(point);if(physical!=null)return physical;
        // A rig's hands and moving limbs can extend outside its simplified body collider.
        // Motion drops also accept the visible projected rig bounds, avoiding accidental actors.
        return _entries.Where(e=>e.Node is RiggedActor||e.NativeTrack!=null)
            .Where(e=>ProjectedActorBounds(e).Grow(5).HasPoint(point))
            .OrderBy(e=>e.Node.GlobalPosition.DistanceSquaredTo(_editorCamera.GlobalPosition)).FirstOrDefault();
    }
    Rect2 ProjectedActorBounds(Entry entry)
    {
        bool found=false;Vector2 min=Vector2.Zero,max=Vector2.Zero;
        foreach(var mesh in Descendants(entry.Node).OfType<MeshInstance3D>())
        {
            if(mesh.Mesh==null)continue;var bounds=mesh.GetAabb();for(int i=0;i<8;i++){var world=mesh.GlobalTransform*bounds.GetEndpoint(i);if(_editorCamera.IsPositionBehind(world))continue;var p=_editorCamera.UnprojectPosition(world);if(!found){min=max=p;found=true;}else{min=min.Min(p);max=max.Max(p);}}
        }
        return found?new Rect2(min,max-min):new Rect2(new Vector2(-100000,-100000),Vector2.Zero);
    }
    bool CompatibleMotion(Entry entry,LibraryMotion motion)=>motion.ActorFamily=="human"?entry.Node is RiggedActor:entry.Data.ActorFamily==motion.ActorFamily&&entry.Player?.HasAnimation(motion.SourceAnimation??"")==true;
    void DropAsset(string kind,Vector3 position)
    {
        PushUndo();var model=ContentCatalog.Find(kind);Entry entry=model!=null?AddCatalogModel(model,position,true):AddObject(kind,position);Select(entry);SetTime(_time);Status("Added "+entry.Data.Name+". Press K to key its transform.");
    }
    void DropMotion(string id,Entry? target,Vector3 position)
    {
        var motion=MotionCatalog.Find(id)??throw new InvalidDataException("Unknown library motion.");if(target!=null&&!CompatibleMotion(target,motion)){Status("Choose a matching "+motion.ActorFamily+" actor for this motion.");return;}PushUndo();
        target??=AddCatalogModel(ContentCatalog.ForFamily(motion.ActorFamily).FirstOrDefault()??throw new InvalidDataException("No prepared model matches this motion."),position,false);
        string source=motion.ActorFamily=="human"?MotionCatalog.MaterializeSource(motion):target.Data.ModelPath!;
        var previous=target.Data.MotionClips.OrderBy(c=>c.Start).LastOrDefault();double blend=previous==null?0:Math.Min(.3,Math.Min(previous.Duration,motion.Duration)*.25);
        var clip=new MotionClipData{MotionPath=source,Name=motion.Name,SourceAnimation=motion.SourceAnimation,SourceScale=motion.SourceScale,SourceOffset=motion.SourceOffset,Duration=motion.Duration,Start=previous==null?_time:Math.Max(0,previous.Start+previous.Duration-blend),BlendIn=blend,Loop=motion.Loop};
        target.Data.MotionClips.Add(clip);try{RebuildTrack(target);}catch{target.Data.MotionClips.Remove(clip);throw;}_selectedClipId=clip.Id;target.Data.SourceScale=motion.SourceScale;EnsureDuration(clip.Start+clip.Duration);
        Select(target);ShowInspectorSection("Motion");SetTime(SelectedClip?.Start??_time);Status(motion.Name+" added to "+target.Data.Name+".");
    }
    Entry AddFox(Vector3 position,bool defaultMotion)
    {
        var walk=MotionCatalog.Find("fox-walk")!;var data=new ObjectData{Kind="imported",ActorFamily="fox",Name="Fox",ModelPath=MotionCatalog.MaterializeSource(walk),Position=Array3(position),ModelAnimation=walk.SourceAnimation};
        var entry=ImportModelData(data);entry.Node.Position=position;
        if(defaultMotion){data.MotionClips.Add(new MotionClipData{MotionPath=data.ModelPath,Name="Fox walk",SourceAnimation="Walk",SourceScale=1,Start=0,Duration=_duration,Loop=true,BlendIn=0});RebuildTrack(entry);}
        RefreshObjects();return entry;
    }
    void InitializeNativeClip(Entry entry)
    {
        if(entry.NativeTrack==null||entry.Player==null||entry.Animation==""||entry.Data.MotionClips.Count>0)return;var animation=entry.Player.GetAnimation(entry.Animation);
        entry.Data.MotionClips.Add(new MotionClipData{MotionPath=entry.Data.ModelPath!,SourceAnimation=entry.Animation,Name=entry.Animation,SourceScale=1,Duration=animation.Length/Math.Max(.1,entry.Data.Speed),Speed=entry.Data.Speed,Loop=entry.Data.Loop,BlendIn=0});RebuildTrack(entry);
    }
}
