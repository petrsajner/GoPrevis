using Godot;
using System;
using System.Linq;
using System.Threading.Tasks;

namespace GoPrevis;

public partial class GoPrevisApp
{
    string _framingSubjectId="";
    void BuildIllustratedCameraLibrary()
    {
        _library.AddChild(Text("Active · "+ActiveCameraName,11,true));
        _library.AddChild(Row(Button("Add camera",()=>AddCameraFromView(false)),Button("Duplicate",()=>AddCameraFromView(true))));
        var filter=new OptionButton{FitToLongestItem=false};foreach(string choice in new[]{"All camera presets","Framing","Basic movements","Aerial movements"})filter.AddItem(choice);_library.AddChild(filter);
        var search=new LineEdit{PlaceholderText="Search camera presets…",ClearButtonEnabled=true};_library.AddChild(search);
        var note=Text("Framing uses the selected subject. Movements start at frame 0 from your current composition.",11,true);note.AutowrapMode=TextServer.AutowrapMode.WordSmart;_library.AddChild(note);
        var sections=new VBoxContainer{SizeFlagsHorizontal=SizeFlags.ExpandFill};sections.AddThemeConstantOverride("separation",8);_library.AddChild(sections);
        void Populate()
        {
            foreach(Node child in sections.GetChildren()){sections.RemoveChild(child);child.QueueFree();}
            if(filter.Selected is 0 or 1)
            {
                var framings=CameraFramingCatalog.All.Where(p=>search.Text==""||(p.Name+" "+p.Description).Contains(search.Text,StringComparison.OrdinalIgnoreCase)).ToArray();
                if(framings.Length>0)
                {
                    sections.AddChild(Text("FRAMING · "+framings.Length,11,true));var grid=new GridContainer{Columns=2,SizeFlagsHorizontal=SizeFlags.ExpandFill};sections.AddChild(grid);
                    foreach(var preset in framings){var item=preset;var card=CreateCameraPresetCard(item.Name,"framing:"+item.Id,item.Description);card.Pressed+=()=>{try{ApplyCameraFraming(item.Id);}catch(Exception ex){Error(ex.Message);}};grid.AddChild(card);}
                }
            }
            foreach(string group in new[]{"Basic","Aerial"})
            {
                if(filter.Selected==1||filter.Selected==2&&group!="Basic"||filter.Selected==3&&group!="Aerial")continue;
                var moves=CameraMoveCatalog.All.Where(p=>p.Category==group&&(search.Text==""||(p.Name+" "+p.Description).Contains(search.Text,StringComparison.OrdinalIgnoreCase))).ToArray();if(moves.Length==0)continue;
                sections.AddChild(Text(group.ToUpperInvariant()+" MOVEMENTS · "+moves.Length,11,true));var grid=new GridContainer{Columns=2,SizeFlagsHorizontal=SizeFlags.ExpandFill};sections.AddChild(grid);
                foreach(var preset in moves){var item=preset;var card=CreateCameraPresetCard(item.Name,item.Id,item.Description+"\nApply from frame 0 to "+ActiveCameraName+".");card.Pressed+=()=>{try{ApplyIllustratedCameraMove(item.Id);}catch(Exception ex){Error(ex.Message);}};grid.AddChild(card);}
            }
            if(sections.GetChildCount()==0)sections.AddChild(Text("No matching camera presets.",11,true));
        }
        filter.ItemSelected+=_=>Populate();search.TextChanged+=_=>Populate();Populate();_library.AddChild(Button("Clear camera keys",ClearCameraAnimation));
    }

    Button CreateCameraPresetCard(string name,string diagram,string detail)
    {
        var card=new Button{Text="",CustomMinimumSize=new Vector2(88,106),SizeFlagsHorizontal=SizeFlags.ExpandFill,FocusMode=FocusModeEnum.None,TooltipText=name+"\n"+detail};
        card.AddThemeStyleboxOverride("normal",CompactStyle(new Color("202025"),3,3));card.AddThemeStyleboxOverride("hover",CompactStyle(new Color("302b3a"),3,3));
        var column=new VBoxContainer{MouseFilter=MouseFilterEnum.Ignore};column.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);column.OffsetLeft=4;column.OffsetRight=-4;column.OffsetTop=3;column.OffsetBottom=-4;column.AddThemeConstantOverride("separation",2);card.AddChild(column);
        Control illustration=diagram.StartsWith("framing:",StringComparison.Ordinal)?new CameraFramingDiagram{PresetId=diagram[8..]}:new CameraMoveDiagram{PresetId=diagram};
        illustration.CustomMinimumSize=new Vector2(80,68);illustration.SizeFlagsVertical=SizeFlags.ExpandFill;illustration.MouseFilter=MouseFilterEnum.Ignore;illustration.ClipContents=true;column.AddChild(illustration);
        var label=Text(name,11);label.AutowrapMode=TextServer.AutowrapMode.WordSmart;label.MaxLinesVisible=2;label.HorizontalAlignment=HorizontalAlignment.Center;label.CustomMinimumSize=new Vector2(0,28);label.MouseFilter=MouseFilterEnum.Ignore;column.AddChild(label);return card;
    }

    void ApplyCameraFraming(string id)
    {
        var preset=CameraFramingCatalog.Find(id)??throw new System.IO.InvalidDataException("Unknown framing preset: "+id);
        var subject=CameraFramingSubject();var working=_pilot?_shot.ShotCamera.GlobalTransform:_editorCamera.GlobalTransform;
        float fov=_pilot?_shot.ShotCamera.Fov:_editorCamera.Fov;
        if(!_pilot&&_editorCamera.KeepAspect==Camera3D.KeepAspectEnum.Height)
        {
            float aspect=_sceneViewport.Size.X/(float)Math.Max(1,_sceneViewport.Size.Y);fov=Mathf.RadToDeg(2*Mathf.Atan(Mathf.Tan(Mathf.DegToRad(fov)*.5f)*aspect));
        }
        PushUndo();_playing=false;_play.Text="▶";var camera=_cameras.First(c=>c.Rig==_shot);DisableCameraTrackingForPreset(camera);camera.Rig.ClearKeys();CameraFramingCatalog.Apply(camera.Rig,preset.Id,subject,working,fov,ProjectAspect);SyncCameraFromRig(camera);SetTime(_time);SelectCamera(camera);UpdatePreview();
        Status(preset.Name+" framing applied to "+camera.Name+".");
    }
    FramingSubject CameraFramingSubject()
    {
        Entry? entry=_selected;
        if(entry!=null)_framingSubjectId=entry.Data.Id;
        else if(_framingSubjectId!="")entry=_entries.FirstOrDefault(e=>e.Data.Id==_framingSubjectId);
        if(entry==null)
        {
            Vector3 position=_shot.GlobalPosition,forward=-_shot.GlobalBasis.Orthonormalized().Z;
            var candidates=_entries.Where(e=>e.Node is RiggedActor||e.NativeTrack!=null).ToArray();if(candidates.Length==0)candidates=_entries.ToArray();
            entry=candidates.OrderByDescending(e=>{var b=PreparedEntryBounds(e);var delta=e.Node.GlobalTransform*b.GetCenter()-position;return delta.LengthSquared()<.00001f?1:forward.Dot(delta.Normalized());}).FirstOrDefault();
            if(entry!=null)_framingSubjectId=entry.Data.Id;
        }
        if(entry==null)
        {
            Vector3 center=_shot.GlobalPosition-_shot.GlobalBasis.Orthonormalized().Z*6;return new FramingSubject(new Aabb(center-Vector3.One,Vector3.One*2),Vector3.Forward,center+Vector3.Up*.65f,Vector3.Forward,center+new Vector3(.3f,.45f,0),center+new Vector3(-.3f,.45f,0));
        }
        var local=PreparedEntryBounds(entry);bool found=false;Vector3 minimum=Vector3.Zero,maximum=Vector3.Zero;
        for(int i=0;i<8;i++){var point=entry.Node.GlobalTransform*local.GetEndpoint(i);if(!found){minimum=maximum=point;found=true;}else{minimum=minimum.Min(point);maximum=maximum.Max(point);}}
        var bounds=new Aabb(minimum,maximum-minimum);var direction=entry.Node.GlobalBasis*(entry.NativeTrack?.AnimatedForward??Vector3.Forward);direction.Y=0;if(direction.LengthSquared()<.00001f)direction=Vector3.Forward;direction=direction.Normalized();
        var right=Vector3.Up.Cross(direction);var centerBounds=bounds.GetCenter();var head=centerBounds+Vector3.Up*bounds.Size.Y*.3f;var leftShoulder=centerBounds+Vector3.Up*bounds.Size.Y*.15f-right*bounds.Size.X*.25f;var rightShoulder=centerBounds+Vector3.Up*bounds.Size.Y*.15f+right*bounds.Size.X*.25f;
        if(entry.Node is RiggedActor actor)
        {
            var face=actor.CaptureEvaluatedPose();var localForward=new Vector3(face.Forward[0],face.Forward[1],face.Forward[2]);var bodyForward=entry.Node.GlobalBasis*(actor.GetPoseJointGlobalRotation("Hips")*localForward);bodyForward.Y=0;if(bodyForward.LengthSquared()>.00001f)direction=bodyForward.Normalized();
            var headRotation=actor.GetPoseJointGlobalRotation("Head");head=actor.GlobalTransform*(actor.GetPoseJointPosition("Head")+headRotation*new Vector3(0,.087f,0));
            return new FramingSubject(bounds,direction,head,actor.GlobalBasis*(headRotation*localForward),actor.GlobalTransform*actor.GetPoseJointPosition("LeftUpperArm"),actor.GlobalTransform*actor.GetPoseJointPosition("RightUpperArm"),true){Pelvis=actor.GlobalTransform*actor.GetPoseJointPosition("Hips")};
        }
        var nativeSkeleton=Descendants(entry.Node).OfType<Skeleton3D>().FirstOrDefault();
        if(nativeSkeleton!=null)
        {
            int headBone=Enumerable.Range(0,nativeSkeleton.GetBoneCount()).FirstOrDefault(i=>nativeSkeleton.GetBoneName(i).ToString().Contains("head",StringComparison.OrdinalIgnoreCase),-1);
            if(headBone>=0)head=nativeSkeleton.GlobalTransform*nativeSkeleton.GetBoneGlobalPose(headBone).Origin;
        }
        return new FramingSubject(bounds,direction,head,direction,leftShoulder,rightShoulder);
    }

    void ApplyIllustratedCameraMove(string id)
    {
        Vector3 target=CameraMovementTarget();var camera=_cameras.First(c=>c.Rig==_shot);PushUndo();DisableCameraTrackingForPreset(camera);_playing=false;_play.Text="▶";
        if(!TryApplyNativeOrbitPreset(camera,id,target)){camera.Rig.SetMovementPreset(id,_duration,target);SyncCameraFromRig(camera);}SetTime(0);SelectCamera(camera);
        Status(CameraMoveCatalog.Find(id)!.Name+" applied to "+camera.Name+" from frame 0.");
    }
    Vector3 CameraMovementTarget()
    {
        if(_selected is {} selected)
        {
            var bounds=selected.Node is RiggedActor actor?actor.GetPreparedPoseBounds():PropFactory.CalculateBounds(selected.Node);return selected.Node.GlobalTransform*bounds.GetCenter();
        }
        Vector3 from=_shot.GlobalPosition,forward=-_shot.GlobalBasis.Orthonormalized().Z;
        var query=PhysicsRayQueryParameters3D.Create(from+forward*.4f,from+forward*500,1);query.Exclude=new Godot.Collections.Array<Rid>(_cameras.SelectMany(c=>Descendants(c.Rig).OfType<CollisionObject3D>()).Select(c=>c.GetRid()));
        var hit=_world.GetWorld3D().DirectSpaceState.IntersectRay(query);if(hit.Count>0){var point=hit["position"].AsVector3();if(from.DistanceSquaredTo(point)>.25f)return point;}
        return from+forward*6;
    }

    public async Task<int> RunCameraMovementChecks()
    {
        int checks=0;void Check(bool value,string label){Require(value,"Camera movement: "+label);checks++;}
        var rig=new StudioCameraRig{Visible=false};_world.AddChild(rig);
        try
        {
            Check(CameraMoveCatalog.All.Count==24&&CameraMoveCatalog.All.Select(p=>p.Id).Distinct().Count()==24,"24 distinct illustrated movement presets are available");
            var target=new Vector3(12,1,-4);rig.SetPose(new Vector3(14,5,-9),target);rig.GlobalBasis*=new Basis(Vector3.Forward,.15f);var start=rig.GlobalTransform;const float lens=48;
            foreach(var preset in CameraMoveCatalog.All)
            {
                rig.GlobalTransform=start;rig.FocalLengthMm=lens;CameraMoveCatalog.Apply(rig,preset.Id,8,target);
                Check(rig.GlobalTransform.IsEqualApprox(start)&&Math.Abs(rig.FocalLengthMm-lens)<.001f,preset.Name+" preserves the current position, angle, roll and lens at frame zero");
                Check(rig.Keys.Count>=2&&Math.Abs(rig.Keys[0].TimeSeconds)<.000001&&Math.Abs(rig.Keys[^1].TimeSeconds-8)<.000001,preset.Name+" fills the requested shot duration");
                Check(rig.Keys.All(k=>k.Position.IsFinite()&&float.IsFinite(k.Rotation.LengthSquared())&&Math.Abs(k.Rotation.LengthSquared()-1)<.0001f&&k.FocalLengthMm>=8&&k.FocalLengthMm<=300)&&rig.Keys.Zip(rig.Keys.Skip(1)).All(p=>p.First.TimeSeconds<p.Second.TimeSeconds),preset.Name+" keys are finite, normalized, ordered and within the lens range");
                rig.Evaluate(3.25);var middle=rig.GlobalTransform;float middleLens=rig.FocalLengthMm;rig.Evaluate(7);rig.Evaluate(.1);rig.Evaluate(3.25);
                Check(rig.GlobalTransform.IsEqualApprox(middle)&&Math.Abs(rig.FocalLengthMm-middleLens)<.001f,preset.Name+" seeks deterministically in either direction");
                rig.Evaluate(8);
                if(preset.Id is "zoom-in" or "zoom-out" or "crash-zoom")Check(rig.GlobalTransform.IsEqualApprox(start)&&Math.Abs(rig.FocalLengthMm-lens)>.5f,preset.Name+" changes focal length without translating or rotating the camera");
                else if(preset.Id is "tilt-up" or "tilt-down" or "pan-left" or "pan-right" or "whip-pan")Check(rig.GlobalPosition.IsEqualApprox(start.Origin)&&Math.Abs(rig.GlobalBasis.GetRotationQuaternion().Dot(start.Basis.GetRotationQuaternion()))<.999f,preset.Name+" turns the camera without moving its origin");
                else if(preset.Id=="static")Check(rig.GlobalTransform.IsEqualApprox(start)&&Math.Abs(rig.FocalLengthMm-lens)<.001f,"Static shot holds the original composition for the entire shot");
                else Check(rig.GlobalPosition.DistanceTo(start.Origin)>.005f,preset.Name+" contains an actual positional camera move");
            }
            rig.GlobalTransform=start;rig.FocalLengthMm=lens;CameraMoveCatalog.Apply(rig,"handheld",8,target);var first=rig.Keys.Select(k=>k.Position).ToArray();rig.GlobalTransform=start;rig.FocalLengthMm=lens;CameraMoveCatalog.Apply(rig,"handheld",8,target);
            Check(first.SequenceEqual(rig.Keys.Select(k=>k.Position)),"Handheld recreates exactly the same motion for repeatable exports");
            int count=rig.Keys.Count;bool invalid=false;try{CameraMoveCatalog.Apply(rig,"missing-preset",8,target);}catch(System.IO.InvalidDataException){invalid=true;}
            Check(invalid&&rig.Keys.Count==count,"an unknown preset fails without deleting the existing camera animation");
            CameraMoveCatalog.Apply(rig,"Orbit",2,rig.GlobalPosition);Check(rig.Keys.All(k=>k.Position.IsFinite()),"an orbit target coinciding with the camera gets a safe forward target");
            GD.Print("CAMERA_MOVEMENTS_OK presets="+CameraMoveCatalog.All.Count+" checks="+checks);return checks;
        }
        finally{_world.RemoveChild(rig);rig.QueueFree();await ToSignal(GetTree(),SceneTree.SignalName.ProcessFrame);}
    }

    public async Task<int> RunCameraFramingChecks()
    {
        int checks=0;void Check(bool value,string text){Require(value,"Camera framing: "+text);checks++;}
        var rig=new StudioCameraRig{Visible=false};_world.AddChild(rig);
        var viewport=new SubViewport{Size=new Vector2I(960,540),OwnWorld3D=true};AddChild(viewport);var projection=new Camera3D{Current=true,KeepAspect=Camera3D.KeepAspectEnum.Width,Near=.01f,Far=500};viewport.AddChild(projection);
        try
        {
            var forward=new Vector3(.6f,0,-.8f);var right=Vector3.Up.Cross(forward);var center=new Vector3(8,.95f,-5);var subject=new FramingSubject(new Aabb(center-new Vector3(.4f,.95f,.25f),new Vector3(.8f,1.9f,.5f)),forward,center+Vector3.Up*.78f,forward,center+Vector3.Up*.5f-right*.195f,center+Vector3.Up*.5f+right*.195f,true){Pelvis=center-Vector3.Up*.01f};
            var view=new Transform3D(Basis.Identity,new Vector3(12,3,-9)).LookingAt(center,Vector3.Up);
            Check(CameraFramingCatalog.All.Count==15&&CameraFramingCatalog.All.Select(p=>p.Id).Distinct().Count()==15,"all fifteen reference framing types are available");
            foreach(var preset in CameraFramingCatalog.All)
            {
                rig.GlobalTransform=view;rig.FocalLengthMm=35;rig.CaptureKey(0);rig.CaptureKey(8);CameraFramingCatalog.Apply(rig,preset.Id,subject,view,62);
                Check(rig.GlobalPosition.IsFinite()&&rig.GlobalBasis.IsFinite()&&rig.FocalLengthMm>=8&&rig.FocalLengthMm<=300,preset.Name+" has a finite camera pose and practical lens");
                Check(rig.Keys.Count==0,preset.Name+" replaces movement with a static composition");
                if(preset.Id=="current-view")
                {
                    Check(rig.GlobalTransform.IsEqualApprox(view)&&Math.Abs(rig.ShotCamera.Fov-62)<.001f,"Current view retains the working pose and horizontal field of view");continue;
                }
                if(preset.Id.StartsWith("over-shoulder",StringComparison.Ordinal))
                {
                    var shoulder=preset.Id.EndsWith("left",StringComparison.Ordinal)?subject.LeftShoulder:subject.RightShoulder;
                    Check((rig.GlobalPosition-shoulder).Dot(forward)<-.2f&&(-rig.GlobalBasis.Z).Dot(forward)>.95f,preset.Name+" looks forward from behind the matching shoulder");continue;
                }
                projection.GlobalTransform=rig.GlobalTransform;projection.Fov=rig.ShotCamera.Fov;var region=CameraFramingCatalog.FramingRegion(subject,preset.Id);
                bool fits=true;for(int i=0;i<8;i++){var point=region.GetEndpoint(i);var pixel=projection.UnprojectPosition(point);fits&=!projection.IsPositionBehind(point)&&pixel.X>=-1&&pixel.X<=961&&pixel.Y>=-1&&pixel.Y<=541;}
                Check(fits,preset.Name+" fits its intended subject region in the 16:9 camera");
                var aim=(region.GetCenter()-rig.GlobalPosition).Normalized();Check((-rig.GlobalBasis.Z).Dot(aim)>.98f,preset.Name+" points at its chosen subject instead of the world origin");
            }
            CameraFramingCatalog.Apply(rig,"front-wide",subject,view);var front=rig.GlobalTransform;float distance=rig.GlobalPosition.DistanceTo(center);
            Check((rig.GlobalPosition-center).Normalized().Dot(forward)>.99f,"Front wide is in front of the rotated subject");
            CameraFramingCatalog.Apply(rig,"back-medium",subject,view);Check((rig.GlobalPosition-center).Dot(forward)<0,"Back medium is behind the same subject");
            CameraFramingCatalog.Apply(rig,"side-follow",subject,view);Check(Math.Abs((rig.GlobalPosition-center).Normalized().Dot(forward))<.01f,"Side follow uses the subject's side rather than a global axis");
            CameraFramingCatalog.Apply(rig,"front-close-up",subject,view);float close=rig.GlobalPosition.DistanceTo(subject.Head);Check(close<distance&&rig.FocalLengthMm>35,"Close-up uses a tighter portrait composition than Front wide");
            CameraFramingCatalog.Apply(rig,"dutch-angle",subject,view);Check(Math.Abs(rig.GlobalBasis.X.Y)>.15f,"Dutch angle adds a visible camera roll");
            CameraFramingCatalog.Apply(rig,"low-wide",subject,view);Check(rig.GlobalPosition.Y>=subject.WorldBounds.Position.Y+.03f&&rig.GlobalPosition.Y<center.Y,"Low wide remains above the support surface and below the subject's center");
            CameraFramingCatalog.Apply(rig,"top-wide",subject,view);Check((-rig.GlobalBasis.Z).Dot(Vector3.Down)>.999f,"Top wide looks vertically downward");
            var childCenter=new Vector3(-8,.95f*.65f,11);const float childScale=.65f;
            Vector3 Child(Vector3 value)=>childCenter+(value-center)*childScale;
            var child=subject with{WorldBounds=new Aabb(Child(subject.WorldBounds.Position),subject.WorldBounds.Size*childScale),Head=Child(subject.Head),LeftShoulder=Child(subject.LeftShoulder),RightShoulder=Child(subject.RightShoulder),Pelvis=Child(subject.Pelvis)};
            CameraFramingCatalog.Apply(rig,"front-wide",child,view);Check(Math.Abs(rig.GlobalPosition.DistanceTo(childCenter)/distance-childScale)<.005f,"small figures get proportionally fitted camera distance");
            var yaw=new Basis(Vector3.Up,Mathf.Pi*.5f);Vector3 Turn(Vector3 value)=>center+yaw*(value-center);var rotated=subject with{WorldBounds=new Aabb(new Vector3(center.X-.25f,0,center.Z-.4f),new Vector3(.5f,1.9f,.8f)),Forward=yaw*subject.Forward,Head=Turn(subject.Head),HeadForward=yaw*subject.HeadForward,LeftShoulder=Turn(subject.LeftShoulder),RightShoulder=Turn(subject.RightShoulder),Pelvis=Turn(subject.Pelvis)};
            CameraFramingCatalog.Apply(rig,"front-wide",rotated,view);Check(rig.GlobalPosition.DistanceTo(Turn(front.Origin))<.005f,"rotating a subject rotates the front composition around it");
            var animal=new FramingSubject(new Aabb(new Vector3(18,0,9),new Vector3(1,1,1.8f)),Vector3.Back,new Vector3(18.5f,.8f,10.5f),Vector3.Back,new Vector3(18.7f,.6f,9.8f),new Vector3(18.3f,.6f,9.8f));
            CameraFramingCatalog.Apply(rig,"front-wide",animal,view);projection.GlobalTransform=rig.GlobalTransform;projection.Fov=rig.ShotCamera.Fov;Check(Enumerable.Range(0,8).All(i=>{var pixel=projection.UnprojectPosition(animal.WorldBounds.GetEndpoint(i));return pixel.X>=0&&pixel.X<=960&&pixel.Y>=0&&pixel.Y<=540;}),"wide framing also fits a quadruped-sized subject");
            var before=rig.GlobalTransform;rig.CaptureKey(0);int count=rig.Keys.Count;bool rejected=false;try{CameraFramingCatalog.Apply(rig,"unknown-framing",subject,view);}catch(System.IO.InvalidDataException){rejected=true;}
            Check(rejected&&rig.GlobalTransform.IsEqualApprox(before)&&rig.Keys.Count==count,"an invalid preset leaves camera pose and animation intact");
            var original=Snapshot();string selectedId=_selected?.Data.Id??"",active=ActiveCameraId;
            try
            {
                var figure=_entries.First(e=>e.Node is RiggedActor);Select(figure);string clips=System.Text.Json.JsonSerializer.Serialize(figure.Data.MotionClips);ApplyCameraFraming("front-wide");
                Check(_selectedCamera!=null&&_selectedCamera.Rig==_shot&&_selectedCamera.Animation.Position.Count==0&&_selectedCamera.Rig.Keys.Count==0,"the framing card opens its camera with a static editable base pose");
                Check(clips==System.Text.Json.JsonSerializer.Serialize(figure.Data.MotionClips),"camera framing preserves the subject's mocap and timing");
                var framedSubject=CameraFramingSubject();Check(framedSubject.WorldBounds.GetCenter().DistanceTo((figure.Node.GlobalTransform*((RiggedActor)figure.Node).GetPreparedPoseBounds().GetCenter()))<.001f,"switching camera cards retains the chosen subject");
            }
            finally{Restore(original,"");SetActiveCamera(active);if(selectedId!="")Select(_entries.FirstOrDefault(e=>e.Data.Id==selectedId));}
            GD.Print("CAMERA_FRAMING_OK presets=15 checks="+checks);return checks;
        }
        finally{viewport.QueueFree();_world.RemoveChild(rig);rig.QueueFree();await ToSignal(GetTree(),SceneTree.SignalName.ProcessFrame);}
    }

    partial class CameraFramingDiagram:Control
    {
        public string PresetId="front-wide";
        readonly Color _ink=new("d4d2dd"),_muted=new("77727f"),_accent=new("b7a5ee");
        Vector2 P(float x,float y)=>new(x*Size.X/160,y*Size.Y/90);
        void Line(float x,float y,float xx,float yy,Color color,float width=1.5f)=>DrawLine(P(x,y),P(xx,yy),color,width,true);
        void Person(float x,float y,float scale,bool profile=false,bool back=false)
        {
            DrawCircle(P(x,y-18*scale),Math.Max(2,Size.X/160*4*scale),_ink);Line(x,y-13*scale,x,y+3*scale,_ink,3);
            Line(x,y-9*scale,x-(profile?3:7)*scale,y-2*scale,_ink,2);Line(x,y-9*scale,x+7*scale,y-2*scale,_ink,2);Line(x,y+3*scale,x-6*scale,y+13*scale,_ink,2);Line(x,y+3*scale,x+6*scale,y+13*scale,_ink,2);
            if(!back)Line(x+(profile?3:1)*scale,y-18*scale,x+(profile?6:3)*scale,y-18*scale,_accent,1.3f);
        }
        void Camera(float x,float y)
        {DrawRect(new Rect2(P(x-8,y-5),P(15,10)),_ink);DrawColoredPolygon(new[]{P(x+7,y-3),P(x+13,y-7),P(x+13,y+7),P(x+7,y+3)},_accent);}
        void Frame(float x,float y,float width,float height)=>DrawRect(new Rect2(P(x,y),P(width,height)),_muted,false,1);
        public override void _Draw()
        {
            DrawRect(new Rect2(Vector2.Zero,Size),new Color("17171c"));
            if(PresetId=="dutch-angle")DrawSetTransform(Size*.5f,Mathf.DegToRad(-15),Vector2.One);
            if(PresetId=="dutch-angle")
            {
                var saved=Size;DrawRect(new Rect2(-saved*.35f,saved*.7f),_muted,false,1);DrawCircle(new Vector2(0,-saved.Y*.20f),saved.X*.04f,_ink);DrawLine(new Vector2(0,-saved.Y*.12f),new Vector2(0,saved.Y*.18f),_ink,4);DrawLine(new Vector2(0,saved.Y*.18f),new Vector2(-saved.X*.08f,saved.Y*.32f),_ink,3);DrawLine(new Vector2(0,saved.Y*.18f),new Vector2(saved.X*.08f,saved.Y*.32f),_ink,3);DrawSetTransform(Vector2.Zero,0,Vector2.One);return;
            }
            Frame(17,9,126,72);
            switch(PresetId)
            {
                case "current-view":Person(80,47,.95f);Line(27,18,39,18,_accent);Line(27,18,27,30,_accent);Line(133,18,121,18,_accent);Line(133,18,133,30,_accent);Line(27,72,39,72,_accent);Line(27,72,27,60,_accent);Line(133,72,121,72,_accent);Line(133,72,133,60,_accent);break;
                case "front-close-up":DrawCircle(P(80,35),Size.X/160*17,_ink);Line(80,51,80,66,_ink,7);Line(80,61,48,77,_ink,9);Line(80,61,112,77,_ink,9);Line(77,34,85,34,_accent,2);break;
                case "front-medium":case "back-medium":DrawCircle(P(80,28),Size.X/160*9,_ink);Line(80,38,80,48,_ink,5);Line(80,46,80,78,_ink,12);Line(80,47,60,66,_ink,5);Line(80,47,100,66,_ink,5);if(PresetId!="back-medium")Line(77,28,83,28,_accent,1.5f);break;
                case "side-follow":Person(80,48,.95f,true);Line(31,73,132,73,_accent,1);Line(120,69,132,73,_accent);Line(120,77,132,73,_accent);break;
                case "side-close":DrawCircle(P(78,28),Size.X/160*10,_ink);Line(85,28,91,28,_accent,2);Line(78,39,78,48,_ink,5);Line(78,46,78,78,_ink,12);Line(79,48,98,68,_ink,5);break;
                case "top-wide":case "birds-eye":
                    bool bird=PresetId=="birds-eye";DrawArc(P(80,46),Size.X/160*(bird?24:18),0,Mathf.Tau,32,_accent,1.2f,true);DrawCircle(P(80,43),Size.X/160*(bird?4:6),_ink);Line(80,48,80,59,_ink,4);Line(80,52,71,55,_ink,2);Line(80,52,89,55,_ink,2);Camera(80,20);Line(80,25,80,33,_muted);if(bird){Frame(26,17,18,10);Frame(114,61,18,10);}break;
                case "high-angle-45":Person(103,59,.9f);Camera(41,25);Line(52,25,93,39,_accent);Line(52,25,126,74,_muted,1);Line(93,39,126,74,_muted,1);break;
                case "low-angle":case "low-wide":Person(102,43,PresetId=="low-wide"?.82f:1.08f);Camera(36,69);Line(47,69,92,18,_accent);Line(47,69,124,62,_muted,1);Line(92,18,124,62,_muted,1);break;
                case "over-shoulder-left":case "over-shoulder-right":
                    bool left=PresetId=="over-shoulder-left";Person(82,42,.75f);float x=left?38:122;DrawCircle(P(x,61),Size.X/160*13,_muted);DrawColoredPolygon(new[]{P(x-24,81),P(x-13,72),P(x+13,72),P(x+24,81)},_muted);Line(x+(!left?-8:8),64,72,44,_accent,1);break;
                default:Person(80,46,.91f);Line(30,74,130,74,_muted,1);break;
            }
        }
    }

    /// <summary>Own vector illustrations: no screenshots or artwork copied from another product.</summary>
    partial class CameraMoveDiagram:Control
    {
        public string PresetId="static";
        readonly Color _ink=new("d4d2dd"),_soft=new("77727f"),_accent=new("b7a5ee"),_background=new("17171c");
        Vector2 P(float x,float y)=>new(x*Size.X/160,y*Size.Y/90);
        Vector2 P(Vector2 value)=>P(value.X,value.Y);
        void Line(Vector2 a,Vector2 b,Color color,float width=1.4f)=>DrawLine(P(a),P(b),color,width,true);
        void Camera(Vector2 at,bool ghost=false)
        {
            var color=ghost?_soft:_ink;var points=new[]{P(at+new Vector2(-9,-5)),P(at+new Vector2(6,-5)),P(at+new Vector2(6,5)),P(at+new Vector2(-9,5))};DrawColoredPolygon(points,color);
            DrawColoredPolygon(new[]{P(at+new Vector2(6,-3)),P(at+new Vector2(12,-6)),P(at+new Vector2(12,6)),P(at+new Vector2(6,3))},color);
        }
        void Person(Vector2 at,float scale=1,bool ghost=false)
        {
            var color=ghost?_soft:_ink;DrawCircle(P(at+new Vector2(0,-18)*scale),Math.Max(2,Size.X/160*4*scale),color);
            Line(at+new Vector2(0,-13)*scale,at+new Vector2(0,3)*scale,color,2.6f);Line(at+new Vector2(0,-9)*scale,at+new Vector2(-7,-2)*scale,color,2);Line(at+new Vector2(0,-9)*scale,at+new Vector2(7,-2)*scale,color,2);
            Line(at+new Vector2(0,3)*scale,at+new Vector2(-6,13)*scale,color,2);Line(at+new Vector2(0,3)*scale,at+new Vector2(6,13)*scale,color,2);
        }
        void Frustum(Vector2 camera,Vector2 top,Vector2 bottom,bool ghost=false)
        {
            var fill=_soft;fill.A=ghost?.08f:.19f;DrawColoredPolygon(new[]{P(camera+new Vector2(8,0)),P(top),P(bottom)},fill);var color=ghost?_soft:_ink;color.A=ghost?.42f:.65f;
            Line(camera+new Vector2(8,0),top,color,.9f);Line(camera+new Vector2(8,0),bottom,color,.9f);Line(top,bottom,color,.9f);
        }
        void Arrow(params Vector2[] points)
        {
            if(points.Length<2)return;for(int i=1;i<points.Length;i++)Line(points[i-1],points[i],_accent,2);
            var direction=(P(points[^1])-P(points[^2])).Normalized();var side=new Vector2(-direction.Y,direction.X);var end=P(points[^1]);DrawColoredPolygon(new[]{end,end-direction*6+side*3,end-direction*6-side*3},_accent);
        }
        void ArcArrow(Vector2 center,Vector2 radius,float from,float to,int steps=24)
        {
            var points=new Vector2[steps+1];for(int i=0;i<=steps;i++){float a=Mathf.Lerp(from,to,i/(float)steps);points[i]=center+new Vector2(Mathf.Cos(a)*radius.X,Mathf.Sin(a)*radius.Y);}Arrow(points);
        }
        public override void _Draw()
        {
            DrawRect(new Rect2(Vector2.Zero,Size),_background);Line(new Vector2(12,77),new Vector2(148,77),new Color("302d36"),.8f);
            if(PresetId.StartsWith("frame-",StringComparison.Ordinal)){DrawFraming();return;}
            if(PresetId.Contains("orbit",StringComparison.Ordinal))
            {
                Person(new Vector2(82,44),.78f);Frustum(new Vector2(28,63),new Vector2(78,22),new Vector2(92,66));Camera(new Vector2(28,63));
                bool up=PresetId=="ascending-orbit",down=PresetId=="descending-orbit";Camera(new Vector2(122,up?24:down?69:45),true);ArcArrow(new Vector2(82,56),new Vector2(52,22),Mathf.Pi*.88f,Mathf.Pi*1.88f);
                if(up)Arrow(new Vector2(139,64),new Vector2(139,25));if(down)Arrow(new Vector2(139,24),new Vector2(139,66));return;
            }
            if(PresetId is "pan-left" or "pan-right" or "whip-pan")
            {
                Person(new Vector2(83,36),.7f);var a=new Vector2(74,69);Frustum(a,new Vector2(56,14),new Vector2(115,32));Camera(a);
                bool left=PresetId=="pan-left";ArcArrow(new Vector2(79,65),new Vector2(44,31),left?-.1f:-2.9f,left?-2.9f:-.1f);
                if(PresetId=="whip-pan"){Arrow(new Vector2(112,52),new Vector2(132,52));Arrow(new Vector2(108,61),new Vector2(128,61));}return;
            }
            var camera=new Vector2(34,59);Person(new Vector2(116,48),.9f);Frustum(camera,new Vector2(105,18),new Vector2(134,71));Camera(camera);
            switch(PresetId)
            {
                case "static":
                    DrawRect(new Rect2(P(18,44),P(35,29)),_accent,false,1.2f);break;
                case "tilt-up":case "tilt-down":
                    bool tiltUp=PresetId=="tilt-up";Frustum(camera,new Vector2(116,tiltUp?2:39),new Vector2(141,tiltUp?42:86),true);
                    ArcArrow(new Vector2(55,53),new Vector2(25,30),tiltUp?.6f:-.9f,tiltUp?-1.1f:.9f);break;
                case "crane-up":case "crane-down":
                    bool rise=PresetId=="crane-up";var crane=new Vector2(34,rise?24:77);Camera(crane,true);Frustum(crane,new Vector2(105,18),new Vector2(134,71),true);Arrow(new Vector2(15,rise?65:24),new Vector2(15,rise?24:76));break;
                case "truck-left":case "truck-right":
                    bool right=PresetId=="truck-right";Camera(new Vector2(right?66:17,right?69:43),true);Arrow(new Vector2(right?26:62,80),new Vector2(right?69:16,right?80:63));break;
                case "dolly-in":case "dolly-out":
                    bool inward=PresetId=="dolly-in";Camera(new Vector2(inward?76:18,inward?48:69),true);Arrow(new Vector2(inward?47:82,inward?70:61),new Vector2(inward?88:25,inward?53:78));break;
                case "zoom-in":case "zoom-out":case "crash-zoom":
                    Frustum(camera,new Vector2(112,29),new Vector2(125,61),true);DrawCircle(P(76,27),Size.X/160*10,_background);DrawArc(P(76,27),Size.X/160*10,0,Mathf.Tau,28,_accent,1.7f,true);Line(new Vector2(83,34),new Vector2(90,41),_accent,2);
                    Line(new Vector2(71,27),new Vector2(81,27),_ink,1.6f);if(PresetId!="zoom-out")Line(new Vector2(76,22),new Vector2(76,32),_ink,1.6f);
                    if(PresetId=="crash-zoom"){Arrow(new Vector2(51,21),new Vector2(65,21));Arrow(new Vector2(51,33),new Vector2(65,33));}break;
                case "handheld":
                    Arrow(new Vector2(18,34),new Vector2(23,26),new Vector2(29,36),new Vector2(36,24),new Vector2(43,34),new Vector2(49,27));Camera(new Vector2(30,64),true);break;
                case "slider-reveal":
                    DrawColoredPolygon(new[]{P(72,24),P(86,30),P(86,73),P(72,66)},new Color("4b4654"));Camera(new Vector2(61,74),true);Arrow(new Vector2(19,81),new Vector2(63,81));break;
                case "push-through":
                    Camera(new Vector2(141,34),true);Arrow(new Vector2(46,63),new Vector2(87,50),new Vector2(142,44));break;
                case "aerial-establishing":
                    Camera(new Vector2(20,19),true);Arrow(new Vector2(60,53),new Vector2(24,20));break;
                case "dive":
                    Camera(new Vector2(24,18),true);Arrow(new Vector2(33,21),new Vector2(80,52));break;
                case "pull-away":
                    Camera(new Vector2(15,30),true);Arrow(new Vector2(75,45),new Vector2(17,28));break;
            }
        }
        void DrawFraming()
        {
            var color=_soft;DrawRect(new Rect2(P(19,10),P(122,70)),color,false,1);
            if(PresetId=="frame-close-up")
            {
                DrawCircle(P(80,32),Size.X/160*12,_ink);Line(new Vector2(80,44),new Vector2(80,58),_ink,7);Line(new Vector2(80,53),new Vector2(53,64),_ink,7);Line(new Vector2(80,53),new Vector2(107,64),_ink,7);Line(new Vector2(80,53),new Vector2(80,78),_ink,18);
            }
            else if(PresetId=="frame-medium-shot")Person(new Vector2(80,60),1.4f);
            else if(PresetId=="frame-high-angle"){Person(new Vector2(105,62),.85f);Camera(new Vector2(35,24));Frustum(new Vector2(35,24),new Vector2(89,45),new Vector2(128,76));Arrow(new Vector2(56,25),new Vector2(90,46));}
            else{Person(new Vector2(80,51),PresetId=="frame-wide-shot"?.72f:1);DrawArc(P(80,45),Size.X/160*21,0,Mathf.Tau,32,_accent,1.1f,true);}
        }
    }
}
