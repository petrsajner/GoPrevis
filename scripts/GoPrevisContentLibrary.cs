using Godot;
using System;
using System.Collections.Generic;
using System.Linq;

namespace GoPrevis;

public partial class GoPrevisApp
{
    const double MaxSceneDuration=600;
    const int LibraryPageSize=12;

    Entry AddCatalogModel(LibraryModel model,Vector3 position,bool defaultMotion)
    {
        Entry entry;
        if(!string.IsNullOrEmpty(model.BuiltinKind))
        {
            entry=AddObject(model.BuiltinKind,position,model.Name);entry.Data.LibraryModelId=model.Id;
            entry.Data.Scale=model.BodyScale.ToArray();entry.Node.Scale=Vector(model.BodyScale,Vector3.One);
            if(entry.Node is RiggedActor actor)ApplyHumanAppearance(actor,model.Id);
        }
        else
        {
            var data=new ObjectData{Kind="imported",Name=model.Name,LibraryModelId=model.Id,ActorFamily=model.ActorFamily,ModelPath=ContentCatalog.MaterializeSource(model),ModelUnitScale=model.Scale,ModelGrounded=true,ModelAnimation=model.DefaultAnimation,Position=Array3(position)};
            entry=ImportModelData(data);entry.Node.Position=position;
            if(defaultMotion&&entry.NativeTrack!=null){InitializeNativeClip(entry);if(entry.Data.MotionClips.Count>0){entry.Data.MotionClips[0].Duration=_duration;RebuildTrack(entry);}}
        }
        ApplyObjectAppearance(entry);RefreshObjects();return entry;
    }
    void ApplyHumanAppearance(RiggedActor actor,string? modelId)
    {
        var color=ContentCatalog.Find(modelId??"")?.Color;
        if(color?.Length==3)actor.SetBodyColor(new Color(color[0],color[1],color[2]));
    }

    // Old category names remain an internal compatibility input, never extra tabs.
    void BuildModelLibrary(string section)=>BuildUnifiedLibrary(UnifiedLibraryCategory(section));

    void BuildMotionLibrary()
    {
        BuildLibraryBrowser("Motions",MotionCatalog.All.Select(m=>new ContentCard(m.Id,m.Name,$"{m.Category} · {m.Duration:0.##} s · {m.License}\n{m.Description}\n{m.Attribution}\nDrag onto a matching actor or empty ground.",null,m)));
        _library.AddChild(Button("Import BVH…",ImportMotion));
    }

    sealed record ContentCard(string Id,string Name,string Detail,LibraryModel? Model,LibraryMotion? Motion,LibraryPose? Pose=null);
    void BuildPagedCards(Func<ContentCard[]> query,LineEdit search,params OptionButton[] filters)
    {
        var count=Text("",11,true);_library.AddChild(count);int page=0;
        var grid=new GridContainer{Columns=2,SizeFlagsHorizontal=SizeFlags.ExpandFill};_library.AddChild(grid);
        var previous=Button("‹",()=>{});var next=Button("›",()=>{});var pageLabel=Text("",11,true);pageLabel.HorizontalAlignment=HorizontalAlignment.Center;pageLabel.SizeFlagsHorizontal=SizeFlags.ExpandFill;
        _library.AddChild(Row(previous,pageLabel,next));
        void Populate(bool reset)
        {
            if(reset)page=0;var items=query();int pages=Math.Max(1,(items.Length+LibraryPageSize-1)/LibraryPageSize);page=Math.Clamp(page,0,pages-1);
            foreach(Node child in grid.GetChildren()){grid.RemoveChild(child);child.QueueFree();}
            count.Text=items.Length+" items · drag into scene";pageLabel.Text=$"{page+1} / {pages}";previous.Disabled=page==0;next.Disabled=page==pages-1;
            foreach(var item in items.Skip(page*LibraryPageSize).Take(LibraryPageSize))
            {
                grid.AddChild(CreateContentCard(item));
            }
        }
        previous.Pressed+=()=>{page--;Populate(false);};next.Pressed+=()=>{page++;Populate(false);};search.TextChanged+=_=>Populate(true);foreach(var filter in filters)filter.ItemSelected+=_=>Populate(true);Populate(true);
    }

    LibraryCard CreateContentCard(ContentCard item)
    {
        var card=new LibraryCard{Kind=item.Pose!=null?"pose":item.Motion==null?"asset":"motion",Id=item.Id,Caption=item.Name,CustomMinimumSize=new Vector2(88,116),SizeFlagsHorizontal=SizeFlags.ExpandFill,ClipText=true,TooltipText=item.Name+"\n"+item.Detail};
        card.Pressed+=()=>{try{if(item.Pose!=null)DropPose(item.Id,_selected!=null&&CompatiblePose(_selected,item.Pose)?_selected:null,new Vector3(0,SceneGroundHeight,0));else if(item.Motion!=null)DropMotion(item.Id,_selected!=null&&CompatibleMotion(_selected,item.Motion)?_selected:null,new Vector3(0,SceneGroundHeight,0));else DropAsset(item.Id,new Vector3(0,SceneGroundHeight,0));}catch(Exception ex){Error(ex.Message);}};
        AddContentThumbnail(card,item);return card;
    }

    void AddContentThumbnail(LibraryCard card,ContentCard item)
    {
        var box=new VBoxContainer{MouseFilter=MouseFilterEnum.Ignore};box.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);box.OffsetLeft=3;box.OffsetRight=-3;box.OffsetTop=3;box.OffsetBottom=-4;box.AddThemeConstantOverride("separation",2);card.AddChild(box);
        var texture=new TextureRect{ExpandMode=TextureRect.ExpandModeEnum.IgnoreSize,StretchMode=TextureRect.StretchModeEnum.KeepAspectCentered,CustomMinimumSize=new Vector2(80,76),SizeFlagsVertical=SizeFlags.ExpandFill,MouseFilter=MouseFilterEnum.Ignore};box.AddChild(texture);
        var label=Text(item.Name,11);label.HorizontalAlignment=HorizontalAlignment.Center;label.AutowrapMode=TextServer.AutowrapMode.WordSmart;label.MaxLinesVisible=2;label.CustomMinimumSize=new Vector2(0,28);label.MouseFilter=MouseFilterEnum.Ignore;box.AddChild(label);
        string path="res://assets/content/thumbnails/"+item.Id+".png";Texture2D? cover=ResourceLoader.Exists(path)?GD.Load<Texture2D>(path):null;if(cover==null&&item.Pose!=null){string saved=SavedPoseLibrary.ThumbnailPath(item.Id);if(System.IO.File.Exists(saved))cover=ImageTexture.CreateFromImage(Image.LoadFromFile(saved));}texture.Texture=cover;
        SubViewport? viewport=null;RiggedActor? actor=null;NativeMotionTrack? native=null;double time=.2;bool hovered=false;
        void CreatePreview()
        {
            if(viewport!=null)return;
            viewport=new SubViewport{Size=new Vector2I(160,128),OwnWorld3D=true,RenderTargetUpdateMode=SubViewport.UpdateMode.Once};card.AddChild(viewport);var world=new Node3D();viewport.AddChild(world);world.AddChild(new WorldEnvironment{Environment=StudioLighting.CreateEnvironment()});
            var wrapper=new Node3D();world.AddChild(wrapper);var model=item.Model??(item.Pose?.Pose.RigModelId is {} rigId?ContentCatalog.Find(rigId):null)??ContentCatalog.ForFamily(item.Motion?.ActorFamily??"human").First();
            if(model.IsBuiltin)
            {
                if(model.BuiltinKind=="actor")
                {
                    actor=new RiggedActor();wrapper.AddChild(actor);actor.Scale=Vector(model.BodyScale,Vector3.One);ApplyHumanAppearance(actor,model.Id);
                    if(item.Pose!=null)actor.SetStaticPose(item.Pose.Pose);
                    else if(item.Motion is { } motion){actor.SetMotionClips(new[]{new MotionClipData{MotionPath=MotionCatalog.MaterializeSource(motion),Name=motion.Name,Duration=motion.Duration,SourceOffset=motion.SourceOffset,SourceScale=motion.SourceScale,Loop=true}});actor.EvaluateTimeline(.4,true);}
                }
                else wrapper.AddChild(PropFactory.Create(model.BuiltinKind));
            }
            else
            {
                var document=new GltfDocument();var state=new GltfState();var error=document.AppendFromFile(ContentCatalog.MaterializeSource(model),state);if(error!=Godot.Error.Ok)throw new InvalidOperationException("The preview model could not be loaded.");var generated=(Node3D)document.GenerateScene(state);wrapper.AddChild(generated);generated.Scale*=model.Scale;
                var player=Descendants(generated).OfType<AnimationPlayer>().FirstOrDefault();
                if(player!=null&&Descendants(generated).OfType<Skeleton3D>().Any())
                {
                    native=new NativeMotionTrack(generated,player);string animation=item.Motion?.SourceAnimation??model.DefaultAnimation??"";
                    if(item.Pose!=null)native.Evaluate(0,true,null,item.Pose.Pose);
                    else if(player.HasAnimation(animation)){native.SetClips(new[]{new MotionClipData{Name=animation,SourceAnimation=animation,Duration=player.GetAnimation(animation).Length,Loop=true}});native.Evaluate(.2,true);}
                }
                var bounds=native!=null&&model.ActorFamily.StartsWith("animal-common-",StringComparison.Ordinal)?native.SkinnedBounds(wrapper):PropFactory.CalculateBounds(wrapper);generated.Position-=Vector3.Up*bounds.Position.Y;
            }
            var framing=actor!=null?actor.GetPreparedPoseBounds():native!=null&&model.ActorFamily.StartsWith("animal-common-",StringComparison.Ordinal)?native.SkinnedBounds(wrapper):PropFactory.CalculateBounds(wrapper);
            var camera=new Camera3D{Current=true,Fov=38,Near=.01f,Far=500};world.AddChild(camera);var target=framing.GetCenter();float radius=Math.Max(.4f,framing.Size.Length()*1.1f);var cameraPosition=target+new Vector3(radius*.65f,radius*.35f,item.Pose!=null?-radius:radius);camera.Transform=Transform3D.Identity.LookingAt(target-cameraPosition,Vector3.Up);camera.Position=cameraPosition;texture.Texture=viewport.GetTexture();
        }
        // Catalog pages load only PNGs. A source is parsed only while its motion card is hovered.
        if(cover==null){try{CreatePreview();}catch(Exception ex){GD.PushWarning("Library preview: "+ex.Message);}}
        if(item.Motion==null)return;
        card.MouseEntered+=()=>{hovered=true;try{CreatePreview();if(viewport!=null)viewport.RenderTargetUpdateMode=SubViewport.UpdateMode.Always;}catch(Exception ex){hovered=false;GD.PushWarning("Motion preview: "+ex.Message);}};
        card.MouseExited+=()=>{hovered=false;if(cover!=null){texture.Texture=cover;viewport?.QueueFree();viewport=null;actor=null;native=null;}else if(viewport!=null)viewport.RenderTargetUpdateMode=SubViewport.UpdateMode.Once;};
        var timer=new Timer{WaitTime=.05,Autostart=true};card.AddChild(timer);timer.Timeout+=()=>{if(!hovered)return;time+=.05;if(item.Motion.Duration>0)time%=item.Motion.Duration;actor?.EvaluateTimeline(time,true);native?.Evaluate(time,true);};
        card.TreeExiting+=()=>hovered=false;
    }
}
