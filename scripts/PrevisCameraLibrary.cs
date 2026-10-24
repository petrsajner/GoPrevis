using Godot;
using System;
using System.Linq;
using System.Threading.Tasks;

namespace Previs;

public partial class PrevisApp
{
    void BuildIllustratedCameraLibrary()
    {
        _library.AddChild(Text("Active · "+ActiveCameraName,11,true));
        _library.AddChild(Row(Button("Add camera",()=>AddCameraFromView(false)),Button("Duplicate",()=>AddCameraFromView(true))));
        var note=Text("Choose a move to apply it from frame 0. Your current composition is the starting view.",11,true);note.AutowrapMode=TextServer.AutowrapMode.WordSmart;_library.AddChild(note);
        var filter=new OptionButton{FitToLongestItem=false};filter.AddItem("All movements");filter.AddItem("Basic");filter.AddItem("Aerial");_library.AddChild(filter);
        var search=new LineEdit{PlaceholderText="Search camera moves…",ClearButtonEnabled=true};_library.AddChild(search);
        var sections=new VBoxContainer{SizeFlagsHorizontal=SizeFlags.ExpandFill};sections.AddThemeConstantOverride("separation",8);_library.AddChild(sections);
        void Populate()
        {
            foreach(Node child in sections.GetChildren()){sections.RemoveChild(child);child.QueueFree();}
            foreach(string group in new[]{"Basic","Aerial"})
            {
                if(filter.Selected>0&&filter.GetItemText(filter.Selected)!=group)continue;
                var moves=CameraMoveCatalog.All.Where(p=>p.Category==group&&(search.Text==""||(p.Name+" "+p.Description).Contains(search.Text,StringComparison.OrdinalIgnoreCase))).ToArray();if(moves.Length==0)continue;
                sections.AddChild(Text(group.ToUpperInvariant()+" · "+moves.Length,11,true));var grid=new GridContainer{Columns=2,SizeFlagsHorizontal=SizeFlags.ExpandFill};sections.AddChild(grid);
                foreach(var preset in moves)
                {
                    var item=preset;var card=CreateCameraPresetCard(item.Name,item.Id,item.Description+"\nApply from frame 0 to "+ActiveCameraName+".");
                    card.Pressed+=()=>{try{ApplyIllustratedCameraMove(item.Id);}catch(Exception ex){Error(ex.Message);}};grid.AddChild(card);
                }
            }
            if(sections.GetChildCount()==0)sections.AddChild(Text("No matching camera moves.",11,true));
        }
        filter.ItemSelected+=_=>Populate();search.TextChanged+=_=>Populate();Populate();
        var framing=new GridContainer{Columns=2,Visible=false,SizeFlagsHorizontal=SizeFlags.ExpandFill};
        var framingHeader=Button("▸ Framing",()=>{},true);framingHeader.Alignment=HorizontalAlignment.Left;framingHeader.Pressed+=()=>{framing.Visible=!framing.Visible;framingHeader.Text=(framing.Visible?"▾ ":"▸ ")+"Framing";framingHeader.ButtonPressed=framing.Visible;};_library.AddChild(framingHeader);_library.AddChild(framing);
        foreach(string name in new[]{"Current view","Wide shot","Medium shot","Close-up","High angle"})
        {
            string captured=name;var card=CreateCameraPresetCard(name,"frame-"+name.ToLowerInvariant().Replace(' ','-'),"Apply a camera framing preset.");card.Pressed+=()=>ApplyShot(captured);framing.AddChild(card);
        }
        _library.AddChild(Button("Clear camera keys",ClearCameraAnimation));
    }

    Button CreateCameraPresetCard(string name,string diagram,string detail)
    {
        var card=new Button{Text="",CustomMinimumSize=new Vector2(88,106),SizeFlagsHorizontal=SizeFlags.ExpandFill,FocusMode=FocusModeEnum.None,TooltipText=name+"\n"+detail};
        card.AddThemeStyleboxOverride("normal",CompactStyle(new Color("202025"),3,3));card.AddThemeStyleboxOverride("hover",CompactStyle(new Color("302b3a"),3,3));
        var column=new VBoxContainer{MouseFilter=MouseFilterEnum.Ignore};column.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);column.OffsetLeft=4;column.OffsetRight=-4;column.OffsetTop=3;column.OffsetBottom=-4;column.AddThemeConstantOverride("separation",2);card.AddChild(column);
        column.AddChild(new CameraMoveDiagram{PresetId=diagram,CustomMinimumSize=new Vector2(80,68),SizeFlagsVertical=SizeFlags.ExpandFill,MouseFilter=MouseFilterEnum.Ignore,ClipContents=true});
        var label=Text(name,11);label.AutowrapMode=TextServer.AutowrapMode.WordSmart;label.MaxLinesVisible=2;label.HorizontalAlignment=HorizontalAlignment.Center;label.CustomMinimumSize=new Vector2(0,28);label.MouseFilter=MouseFilterEnum.Ignore;column.AddChild(label);return card;
    }

    void ApplyIllustratedCameraMove(string id)
    {
        Vector3 target=CameraMovementTarget();var camera=_cameras.First(c=>c.Rig==_shot);PushUndo();_playing=false;_play.Text="▶";
        camera.Rig.SetMovementPreset(id,_duration,target);SyncCameraFromRig(camera);SetTime(0);SelectCamera(camera);
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
