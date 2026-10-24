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
            var motion=MotionCatalog.Find(d["id"].AsString());var target=App.PickMotionTarget(position);return motion!=null&&(target==null||App.CompatibleMotion(target,motion));
        }
        public override void _DropData(Vector2 position,Variant data)
        {
            var d=data.AsGodotDictionary();try{if(d["previs_type"].AsString()=="asset")App.DropAsset(d["id"].AsString(),App.GroundPoint(position));else App.DropMotion(d["id"].AsString(),App.PickMotionTarget(position),App.GroundPoint(position));}catch(Exception ex){App.Error(ex.Message);}
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
        var origin=_editorCamera.ProjectRayOrigin(screen);var direction=_editorCamera.ProjectRayNormal(screen);if(Math.Abs(direction.Y)<.0001f)return Vector3.Zero;float distance=-origin.Y/direction.Y;return distance>0?origin+direction*distance:Vector3.Zero;
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
        PushUndo();Entry entry=kind=="fox_actor"?AddFox(position,true):AddObject(kind,position);Select(entry);SetTime(_time);Status("Added "+entry.Data.Name+". Press K to key its transform.");
    }
    void DropMotion(string id,Entry? target,Vector3 position)
    {
        var motion=MotionCatalog.Find(id)??throw new InvalidDataException("Unknown library motion.");if(target!=null&&!CompatibleMotion(target,motion)){Status("Choose a matching "+motion.ActorFamily+" actor for this motion.");return;}PushUndo();
        target??=motion.ActorFamily=="human"?AddObject("actor",position):AddFox(position,false);
        string source=MotionCatalog.MaterializeSource(motion);
        if(motion.ActorFamily=="human")AppendMotion(target,source,motion.SourceScale);
        else
        {
            var previous=target.Data.MotionClips.OrderBy(c=>c.Start).LastOrDefault();double blend=previous==null?0:Math.Min(.25,Math.Min(previous.Duration,motion.Duration)*.25);
            var clip=new MotionClipData{MotionPath=target.Data.ModelPath!,Name=motion.Name,SourceAnimation=motion.SourceAnimation,SourceScale=1,Duration=motion.Duration,Start=previous==null?_time:Math.Max(0,previous.Start+previous.Duration-blend),BlendIn=blend,Loop=true};
            target.Data.MotionClips.Add(clip);RebuildTrack(target);_selectedClipId=clip.Id;EnsureDuration(clip.Start+clip.Duration);
        }
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
    void BuildMotionLibrary()
    {
        var filter=new OptionButton{FitToLongestItem=false};filter.AddItem("All motions");filter.AddItem("Human");filter.AddItem("Animals");_library.AddChild(filter);
        var search=new LineEdit{PlaceholderText="Search motions…"};_library.AddChild(search);var grid=new GridContainer{Columns=2,SizeFlagsHorizontal=SizeFlags.ExpandFill};_library.AddChild(grid);
        void Populate()
        {
            foreach(Node child in grid.GetChildren()){grid.RemoveChild(child);child.QueueFree();}
            foreach(var motion in MotionCatalog.All.Where(m=>(filter.Selected==0||filter.Selected==1&&m.ActorFamily=="human"||filter.Selected==2&&m.ActorFamily!="human")&&(search.Text==""||m.Name.Contains(search.Text,StringComparison.OrdinalIgnoreCase))))
            {
                var card=new LibraryCard{Text=motion.Name,Caption=motion.Name,Kind="motion",Id=motion.Id,CustomMinimumSize=new Vector2(92,104),SizeFlagsHorizontal=SizeFlags.ExpandFill,ClipText=true,TooltipText=motion.Name+" · "+motion.ActorFamily+" · drag onto a matching actor"};
                card.Pressed+=()=>{try{DropMotion(motion.Id,_selected!=null&&CompatibleMotion(_selected,motion)?_selected:null,Vector3.Zero);}catch(Exception ex){Error(ex.Message);}};
                AddMotionThumbnail(card,motion);grid.AddChild(card);
            }
        }
        filter.ItemSelected+=_=>Populate();search.TextChanged+=_=>Populate();Populate();_library.AddChild(Button("Import BVH…",ImportMotion));
    }
    void AddMotionThumbnail(LibraryCard card,LibraryMotion motion)
    {
        // Live skeletal previews use the exact library source, not category icons.
        card.Text="";var box=new VBoxContainer{MouseFilter=MouseFilterEnum.Ignore};box.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);box.OffsetLeft=4;box.OffsetRight=-4;box.OffsetTop=3;box.OffsetBottom=-3;card.AddChild(box);
        var viewport=new SubViewport{Size=new Vector2I(160,132),OwnWorld3D=true,RenderTargetUpdateMode=SubViewport.UpdateMode.Once};card.AddChild(viewport);var root=new Node3D();viewport.AddChild(root);root.AddChild(new WorldEnvironment{Environment=StudioLighting.CreateEnvironment()});
        RiggedActor? actor=null;NativeMotionTrack? native=null;Node3D model;
        if(motion.ActorFamily=="human"){actor=new RiggedActor();model=actor;root.AddChild(actor);actor.LoadBvh(MotionCatalog.MaterializeSource(motion),motion.SourceScale);actor.ApplyTime(Math.Min(.6,motion.Duration*.4),true,true);}
        else{var state=new GltfState();var document=new GltfDocument();var error=document.AppendFromFile(MotionCatalog.MaterializeSource(motion),state);if(error!=Godot.Error.Ok)return;model=(Node3D)document.GenerateScene(state);model.Scale=Vector3.One*MotionCatalog.FoxModelScale;root.AddChild(model);var player=Descendants(model).OfType<AnimationPlayer>().First();native=new NativeMotionTrack(model,player);native.SetClips(new[]{new MotionClipData{MotionPath=motion.SourcePath,SourceAnimation=motion.SourceAnimation,Name=motion.Name,Duration=motion.Duration,SourceScale=1}});native.Evaluate(.2,true);}
        var camera=new Camera3D{Current=true,Fov=37};root.AddChild(camera);var target=motion.ActorFamily=="human"?new Vector3(0,.9f,0):new Vector3(0,.4f,0);var position=target+new Vector3(2.2f,1,3);camera.Transform=Transform3D.Identity.LookingAt(target-position,Vector3.Up);camera.Position=position;
        box.AddChild(new TextureRect{Texture=viewport.GetTexture(),ExpandMode=TextureRect.ExpandModeEnum.IgnoreSize,StretchMode=TextureRect.StretchModeEnum.KeepAspectCentered,CustomMinimumSize=new Vector2(82,72),SizeFlagsVertical=SizeFlags.ExpandFill,MouseFilter=MouseFilterEnum.Ignore});var label=Text(motion.Name,11);label.HorizontalAlignment=HorizontalAlignment.Center;label.AutowrapMode=TextServer.AutowrapMode.WordSmart;label.MouseFilter=MouseFilterEnum.Ignore;box.AddChild(label);
        bool hovered=false;double time=.2;card.MouseEntered+=()=>{hovered=true;viewport.RenderTargetUpdateMode=SubViewport.UpdateMode.Always;};card.MouseExited+=()=>{hovered=false;viewport.RenderTargetUpdateMode=SubViewport.UpdateMode.Once;};
        card.TreeExiting+=()=>hovered=false;
        var timer=new Timer{WaitTime=.04,Autostart=true};card.AddChild(timer);timer.Timeout+=()=>{if(!hovered)return;time+=.04;actor?.ApplyTime(time,true,true);native?.Evaluate(time,true);};
    }
}
