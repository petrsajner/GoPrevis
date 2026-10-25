using Godot;
using System;
using System.Collections.Generic;
using System.Linq;

namespace GoPrevis;

/// <summary>Transient editor-only world transforms; the scene's animation layers remain authoritative.</summary>
public partial class GoPrevisApp
{
    enum ObjectGizmoOperation { None, Move, ScreenMove, Scale, Rotate }
    ObjectGizmoOverlay? _objectGizmoOverlay;
    ObjectGizmoOperation _objectGizmoDrag,_objectGizmoHover;
    int _objectGizmoAxis,_objectGizmoHoverAxis;
    bool _objectGizmoGestureStarted,_objectGizmoAxisValid,_objectGizmoPlaneValid,_objectGizmoRotationValid;
    Node3D? _objectGizmoNode;
    Vector2 _objectGizmoMouse;
    Vector3 _objectGizmoOrigin,_objectGizmoAxisWorld,_objectGizmoPlaneNormal,_objectGizmoPlaneStart,_objectGizmoRotationStart;
    Transform3D _objectGizmoStartWorld,_objectGizmoStartLocal;
    float _objectGizmoLength,_objectGizmoAxisStart,_objectGizmoFine=1;
    static readonly Color[] ObjectGizmoColors={new("ef817d"),new("8ed0a3"),new("84acf4")};
    bool ObjectGizmoDragging=>_objectGizmoDrag!=ObjectGizmoOperation.None;
    Node3D? ObjectGizmoTarget=>_selectedCamera?.Rig??_selected?.Node;
    bool ObjectGizmoAvailable=>!_poseEditing&&!_drawingPath&&ObjectGizmoTarget!=null;
    bool ObjectGizmoVisible=>ObjectGizmoAvailable&&(_viewportShift||ObjectGizmoDragging);

    void BuildObjectGizmoOverlay()
    {
        _objectGizmoOverlay=new ObjectGizmoOverlay{App=this,MouseFilter=MouseFilterEnum.Ignore,ClipContents=true};
        _objectGizmoOverlay.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);_sceneContainer.AddChild(_objectGizmoOverlay);
    }
    partial class ObjectGizmoOverlay:Control
    {
        public GoPrevisApp App=null!;
        public override void _Process(double delta){Visible=App.ObjectGizmoVisible;if(Visible)QueueRedraw();}
        public override void _Draw()=>App.DrawObjectGizmo(this);
    }
    static Vector3 ObjectGizmoAxisVector(int axis)=>axis==0?Vector3.Right:axis==1?Vector3.Up:Vector3.Back;
    Vector2 ObjectGizmoToViewport(Vector2 input)=>input*new Vector2(_sceneViewport.Size.X,_sceneViewport.Size.Y)/new Vector2(Math.Max(1,_sceneContainer.Size.X),Math.Max(1,_sceneContainer.Size.Y));
    Vector2 ObjectGizmoToInput(Vector2 viewport)=>viewport*_sceneContainer.Size/new Vector2(Math.Max(1,_sceneViewport.Size.X),Math.Max(1,_sceneViewport.Size.Y));
    bool ObjectGizmoProject(Vector3 world,out Vector2 point)
    {
        point=Vector2.Zero;if(_editorCamera.IsPositionBehind(world))return false;point=ObjectGizmoToInput(_editorCamera.UnprojectPosition(world));return point.IsFinite();
    }
    float ObjectGizmoWorldLength(Vector3 origin)
    {
        float depth=Math.Max(.05f,-(_editorCamera.GlobalTransform.AffineInverse()*origin).Z);var screen=_editorCamera.UnprojectPosition(origin);
        float length=_editorCamera.ProjectPosition(screen+new Vector2(92*_sceneViewport.Size.X/Math.Max(1,_sceneContainer.Size.X),0),depth).DistanceTo(origin);
        return Math.Clamp(float.IsFinite(length)?length:.5f,.025f,100);
    }
    Vector3 ObjectGizmoScaleAxis(Node3D target,int axis)=>(target.GlobalBasis.Orthonormalized()*ObjectGizmoAxisVector(axis)).Normalized();
    Vector2?[] ObjectGizmoRing(Vector3 origin,int axis,float radius)
    {
        var normal=ObjectGizmoAxisVector(axis);var u=ObjectGizmoAxisVector((axis+1)%3);var v=normal.Cross(u).Normalized();var points=new Vector2?[65];
        for(int i=0;i<points.Length;i++){float angle=Mathf.Tau*i/(points.Length-1);if(ObjectGizmoProject(origin+(u*Mathf.Cos(angle)+v*Mathf.Sin(angle))*radius,out var p))points[i]=p;}
        return points;
    }
    static float ObjectGizmoSegmentDistance(Vector2 p,Vector2 a,Vector2 b)
    {var d=b-a;float square=d.LengthSquared();return p.DistanceTo(square<.0001f?a:a+d*Math.Clamp((p-a).Dot(d)/square,0,1));}

    void DrawObjectGizmo(Control canvas)
    {
        if(!ObjectGizmoVisible||ObjectGizmoTarget is not {} target)return;Vector3 origin=target.GlobalPosition;if(!ObjectGizmoProject(origin,out var center))return;float length=ObjectGizmoWorldLength(origin);
        for(int axis=0;axis<3;axis++)
        {
            var ring=ObjectGizmoRing(origin,axis,length*.82f);var color=ObjectGizmoColors[axis];color.A=_objectGizmoHover==ObjectGizmoOperation.Rotate&&_objectGizmoHoverAxis==axis?1:.72f;
            for(int i=1;i<ring.Length;i++)if(ring[i-1].HasValue&&ring[i].HasValue)canvas.DrawLine(ring[i-1]!.Value,ring[i]!.Value,color,1.7f,true);
        }
        for(int axis=0;axis<3;axis++)
        {
            var color=ObjectGizmoColors[axis];
            if(ObjectGizmoProject(origin+ObjectGizmoAxisVector(axis)*length,out var arrow)&&(arrow-center).Length()>15)
            {
                var direction=(arrow-center).Normalized();var side=new Vector2(-direction.Y,direction.X);canvas.DrawLine(center+direction*10,arrow,color,2.6f,true);
                canvas.DrawColoredPolygon(new[]{arrow,arrow-direction*11+side*4.5f,arrow-direction*11-side*4.5f},color);
                canvas.DrawString(ThemeDB.FallbackFont,arrow+direction*12+new Vector2(-4,4),"XYZ"[axis].ToString(),fontSize:12,modulate:color);
            }
            if(ObjectGizmoProject(origin+ObjectGizmoScaleAxis(target,axis)*length*.58f,out var cube))
            {
                canvas.DrawRect(new Rect2(cube-Vector2.One*6,Vector2.One*12),new Color("15151b"));canvas.DrawRect(new Rect2(cube-Vector2.One*4,Vector2.One*8),ObjectGizmoScaleColor(axis));
            }
        }
        canvas.DrawCircle(center,6,new Color("19191f"));canvas.DrawArc(center,6,0,Mathf.Tau,24,new Color("eee6fb"),1.5f,true);
        string name=_selectedCamera?.Name??_selected?.Data.Name??"Object";
        canvas.DrawString(ThemeDB.FallbackFont,new Vector2(12,22),name+" · Transform",fontSize:12,modulate:new Color("e5dcf5"));
        canvas.DrawString(ThemeDB.FallbackFont,new Vector2(12,41),"Arrows: move · Squares: scale · Rings: rotate · Ctrl: uniform scale / snap",fontSize:11,modulate:new Color("b8b2c7"));
        if(_objectGizmoHover!=ObjectGizmoOperation.None)canvas.DrawString(ThemeDB.FallbackFont,center+new Vector2(13,-14),_objectGizmoHover==ObjectGizmoOperation.ScreenMove?"Move":_objectGizmoHover+" "+"XYZ"[_objectGizmoHoverAxis],fontSize:11,modulate:new Color("f2dba1"));
    }

    Color ObjectGizmoScaleColor(int axis)=>_viewportCtrl?new Color("ffdc62"):ObjectGizmoColors[axis];
    static Vector3 ObjectGizmoScaled(Vector3 start,int axis,float factor,bool uniform)
    {
        if(uniform)
        {
            float minimum=Math.Min(start.X,Math.Min(start.Y,start.Z)),maximum=Math.Max(start.X,Math.Max(start.Y,start.Z));
            return start*Math.Clamp(factor,.05f/Math.Max(.000001f,minimum),1000/Math.Max(.000001f,maximum));
        }
        start[axis]=Math.Clamp(start[axis]*factor,.05f,1000);return start;
    }

    (ObjectGizmoOperation Kind,int Axis) HitObjectGizmo(Vector2 point)
    {
        if(ObjectGizmoTarget is not {} target)return(ObjectGizmoOperation.None,0);Vector3 origin=target.GlobalPosition;if(!ObjectGizmoProject(origin,out var center))return(ObjectGizmoOperation.None,0);
        float length=ObjectGizmoWorldLength(origin);
        // Local scale cubes have distinct endpoints from the longer world move arrows.
        for(int axis=0;axis<3;axis++)if(ObjectGizmoProject(origin+ObjectGizmoScaleAxis(target,axis)*length*.58f,out var cube)&&new Rect2(cube-Vector2.One*8,Vector2.One*16).HasPoint(point))return(ObjectGizmoOperation.Scale,axis);
        if(point.DistanceTo(center)<8)return(ObjectGizmoOperation.ScreenMove,0);
        float nearest=6;int selected=-1;
        for(int axis=0;axis<3;axis++)if(ObjectGizmoProject(origin+ObjectGizmoAxisVector(axis)*length,out var end)&&(end-center).Length()>15)
        {
            float distance=ObjectGizmoSegmentDistance(point,center+(end-center).Normalized()*13,end);if(distance<nearest){nearest=distance;selected=axis;}
        }
        if(selected>=0)return(ObjectGizmoOperation.Move,selected);
        nearest=5.5f;selected=-1;
        for(int axis=0;axis<3;axis++){var ring=ObjectGizmoRing(origin,axis,length*.82f);for(int i=1;i<ring.Length;i++)if(ring[i-1].HasValue&&ring[i].HasValue){float distance=ObjectGizmoSegmentDistance(point,ring[i-1]!.Value,ring[i]!.Value);if(distance<nearest){nearest=distance;selected=axis;}}}
        return selected<0?(ObjectGizmoOperation.None,0):(ObjectGizmoOperation.Rotate,selected);
    }

    bool HandleObjectGizmoInput(InputEvent input)
    {
        if(!ObjectGizmoAvailable){ReleaseObjectGizmoDrag();return false;}
        bool shift=input is InputEventWithModifiers modifiers&&modifiers.ShiftPressed;
        if(input is InputEventMouseButton {ButtonIndex:MouseButton.Left} button)
        {
            if(!button.Pressed){bool active=ObjectGizmoDragging;ReleaseObjectGizmoDrag();return active;}
            if(!_viewportShift&&!shift)return false;var hit=HitObjectGizmo(button.Position);if(hit.Kind==ObjectGizmoOperation.None)return false;
            ReleaseObjectGizmoDrag();_objectGizmoDrag=hit.Kind;_objectGizmoAxis=hit.Axis;_objectGizmoMouse=button.Position;_objectGizmoNode=ObjectGizmoTarget;_objectGizmoFine=button.AltPressed ? .2f : 1;
            _playing=false;_play.Text="▶";return true;
        }
        if(input is InputEventMouseButton {ButtonIndex:MouseButton.Right or MouseButton.Middle,Pressed:true}){ReleaseObjectGizmoDrag();return false;}
        if(input is not InputEventMouseMotion motion)return false;
        if(!ObjectGizmoDragging)
        {
            var hover=_viewportShift||shift?HitObjectGizmo(motion.Position):(ObjectGizmoOperation.None,0);_objectGizmoHover=hover.Item1;_objectGizmoHoverAxis=hover.Item2;return false;
        }
        if(_objectGizmoNode!=ObjectGizmoTarget){ReleaseObjectGizmoDrag();return true;}
        if(!_objectGizmoGestureStarted)
        {
            if(motion.Position.DistanceTo(_objectGizmoMouse)<2)return true;BeginSceneTransformGesture();_objectGizmoGestureStarted=true;InitializeObjectGizmoDrag();
        }
        try
        {
            if(_objectGizmoDrag is ObjectGizmoOperation.Move or ObjectGizmoOperation.Scale)
            {
                float amount;
                if(_objectGizmoAxisValid&&ObjectGizmoRayAxis(motion.Position,_objectGizmoOrigin,_objectGizmoAxisWorld,out var parameter))amount=parameter-_objectGizmoAxisStart;
                else if(ObjectGizmoProject(_objectGizmoOrigin,out var a)&&ObjectGizmoProject(_objectGizmoOrigin+_objectGizmoAxisWorld*_objectGizmoLength,out var b)&&(b-a).Length()>1)amount=(motion.Position-_objectGizmoMouse).Dot((b-a).Normalized())/(b-a).Length()*_objectGizmoLength;
                else return true;
                amount*=_objectGizmoFine;
                if(_objectGizmoDrag==ObjectGizmoOperation.Move)
                {
                    if(motion.CtrlPressed)amount=Mathf.Snapped(amount,.01f);SetObjectGizmoWorldPosition(_objectGizmoOrigin+_objectGizmoAxisWorld*amount);
                }
                else
                {
                    float factor=Math.Max(.000001f,1+amount/Math.Max(.0001f,_objectGizmoLength*.58f));
                    SetSelectedTransformFromGizmo("Scale",ObjectGizmoScaled(_objectGizmoStartLocal.Basis.Scale,_objectGizmoAxis,factor,motion.CtrlPressed));
                }
            }
            else if(_objectGizmoDrag==ObjectGizmoOperation.ScreenMove)
            {
                if(!_objectGizmoPlaneValid||!ObjectGizmoPlanePoint(motion.Position,_objectGizmoOrigin,_objectGizmoPlaneNormal,out var point))return true;
                var target=_objectGizmoOrigin+(point-_objectGizmoPlaneStart)*_objectGizmoFine;if(motion.CtrlPressed)target=new Vector3(Mathf.Snapped(target.X,.01f),Mathf.Snapped(target.Y,.01f),Mathf.Snapped(target.Z,.01f));SetObjectGizmoWorldPosition(target);
            }
            else if(_objectGizmoDrag==ObjectGizmoOperation.Rotate)
            {
                float angle;
                if(_objectGizmoRotationValid&&ObjectGizmoPlanePoint(motion.Position,_objectGizmoOrigin,_objectGizmoAxisWorld,out var point)&&(point-_objectGizmoOrigin).LengthSquared()>.000001f){var direction=(point-_objectGizmoOrigin).Normalized();angle=Mathf.Atan2(_objectGizmoAxisWorld.Dot(_objectGizmoRotationStart.Cross(direction)),_objectGizmoRotationStart.Dot(direction));}
                else angle=(motion.Position-_objectGizmoMouse).Dot(new Vector2(1,-1))*.01f;
                angle*=_objectGizmoFine;if(motion.CtrlPressed)angle=Mathf.Snapped(angle,Mathf.DegToRad(15));var worldBasis=new Basis(_objectGizmoAxisWorld,angle)*_objectGizmoStartWorld.Basis;
                var parent=_objectGizmoNode!.GetParentNode3D();var local=parent==null?worldBasis:parent.GlobalBasis.Inverse()*worldBasis;SetSelectedTransformFromGizmo("Rotation",local.Orthonormalized().GetEuler()*180f/Mathf.Pi);
            }
        }
        catch(Exception error){ReleaseObjectGizmoDrag();Error(error.Message);}return true;
    }
    void InitializeObjectGizmoDrag()
    {
        var target=_objectGizmoNode!;_objectGizmoStartLocal=target.Transform;_objectGizmoStartWorld=target.GlobalTransform;_objectGizmoOrigin=target.GlobalPosition;_objectGizmoLength=ObjectGizmoWorldLength(_objectGizmoOrigin);
        _objectGizmoAxisWorld=_objectGizmoDrag==ObjectGizmoOperation.Scale?ObjectGizmoScaleAxis(target,_objectGizmoAxis):ObjectGizmoAxisVector(_objectGizmoAxis);
        _objectGizmoPlaneNormal=_editorCamera.GlobalBasis.Z.Normalized();_objectGizmoAxisValid=ObjectGizmoRayAxis(_objectGizmoMouse,_objectGizmoOrigin,_objectGizmoAxisWorld,out _objectGizmoAxisStart);
        _objectGizmoPlaneValid=ObjectGizmoPlanePoint(_objectGizmoMouse,_objectGizmoOrigin,_objectGizmoPlaneNormal,out _objectGizmoPlaneStart);
        _objectGizmoRotationValid=ObjectGizmoPlanePoint(_objectGizmoMouse,_objectGizmoOrigin,_objectGizmoAxisWorld,out var start)&&(start-_objectGizmoOrigin).LengthSquared()>.000001f;
        _objectGizmoRotationStart=_objectGizmoRotationValid?(start-_objectGizmoOrigin).Normalized():Vector3.Right;
    }
    void SetObjectGizmoWorldPosition(Vector3 world)
    {
        var parent=_objectGizmoNode!.GetParentNode3D();SetSelectedTransformFromGizmo("Position",parent==null?world:parent.GlobalTransform.AffineInverse()*world);
    }
    bool ObjectGizmoPlanePoint(Vector2 point,Vector3 origin,Vector3 normal,out Vector3 hit)
    {
        var screen=ObjectGizmoToViewport(point);var from=_editorCamera.ProjectRayOrigin(screen);var direction=_editorCamera.ProjectRayNormal(screen);float denominator=normal.Dot(direction);hit=origin;
        if(Math.Abs(denominator)<.00001f)return false;float distance=normal.Dot(origin-from)/denominator;if(!float.IsFinite(distance)||distance<0)return false;hit=from+direction*distance;return hit.IsFinite();
    }
    bool ObjectGizmoRayAxis(Vector2 point,Vector3 origin,Vector3 axis,out float parameter)
    {
        var screen=ObjectGizmoToViewport(point);var from=_editorCamera.ProjectRayOrigin(screen);var direction=_editorCamera.ProjectRayNormal(screen);var delta=from-origin;float dot=axis.Dot(direction),denominator=1-dot*dot;parameter=0;
        if(denominator<.0001f)return false;parameter=(axis.Dot(delta)-dot*direction.Dot(delta))/denominator;return float.IsFinite(parameter);
    }
    void ReleaseObjectGizmoDrag()
    {
        bool started=_objectGizmoGestureStarted;_objectGizmoGestureStarted=false;_objectGizmoDrag=ObjectGizmoOperation.None;_objectGizmoNode=null;_objectGizmoHover=ObjectGizmoOperation.None;if(started)EndSceneTransformGesture();
    }

    CameraEntry? PickViewportCamera(Vector2 point)
    {
        var screen=ObjectGizmoToViewport(point);var from=_editorCamera.ProjectRayOrigin(screen);var direction=_editorCamera.ProjectRayNormal(screen);
        var query=PhysicsRayQueryParameters3D.Create(from,from+direction*_editorCamera.Far,1);var excluded=new Godot.Collections.Array<Rid>();
        // A camera created from the current view hides its editor body while the
        // working camera is inside it. Its still-present picker must not block
        // every other camera or frustum edge in that view.
        foreach(var hidden in _cameras.Where(c=>!c.Rig.IsVisibleInTree()))foreach(var body in Descendants(hidden.Rig).OfType<CollisionObject3D>())excluded.Add(body.GetRid());
        query.Exclude=excluded;var physical=_world.GetWorld3D().DirectSpaceState.IntersectRay(query);
        float physicalDistance=physical.Count>0?from.DistanceTo(physical["position"].AsVector3()):float.PositiveInfinity;
        if(physical.Count>0&&physical["collider"].AsGodotObject() is Node collider)
        {
            for(Node? node=collider;node!=null;node=node.GetParent())if(node.HasMeta("camera_id")){string id=node.GetMeta("camera_id").AsString();var camera=_cameras.FirstOrDefault(c=>c.Id==id&&c.Rig.IsVisibleInTree());if(camera!=null)return camera;}
        }
        CameraEntry? selected=null;float nearest=float.PositiveInfinity;
        foreach(var camera in _cameras.Where(c=>c.Rig.IsVisibleInTree()))
        {
            foreach(var mesh in Descendants(camera.Rig).OfType<MeshInstance3D>().Where(m=>m.Mesh!=null&&m.Name!="CameraFrustum"&&m.IsVisibleInTree()))
            {
                var inverse=mesh.GlobalTransform.AffineInverse();if(ObjectGizmoRayBox(inverse*from,inverse.Basis*direction,mesh.GetAabb(),out float distance)&&distance<nearest&&distance<=physicalDistance+.025f){nearest=distance;selected=camera;}
            }
        }
        if(selected!=null)return selected;
        float bestPixels=4.5f;
        foreach(var camera in _cameras.Where(c=>c.Rig.IsVisibleInTree()))foreach(var segment in ObjectGizmoCameraSegments(camera))
        {
            if(!ObjectGizmoProject(segment.A,out var a)||!ObjectGizmoProject(segment.B,out var b))continue;float pixels=ObjectGizmoSegmentDistance(point,a,b);if(pixels>bestPixels)continue;
            var delta=b-a;float t=delta.LengthSquared()<.001f?0:Math.Clamp((point-a).Dot(delta)/delta.LengthSquared(),0,1);var world=segment.A.Lerp(segment.B,t);float distance=(world-from).Dot(direction);
            if(distance<0||distance>physicalDistance+.04f)continue;bestPixels=pixels;selected=camera;
        }
        return selected;
    }
    IEnumerable<(Vector3 A,Vector3 B)> ObjectGizmoCameraSegments(CameraEntry camera)
    {
        const float length=2.1f;float w=length*StudioCameraRig.SensorWidthMm/(2*camera.Rig.FocalLengthMm),h=w*9/16;var corners=new[]{new Vector3(-w,-h,-length),new Vector3(w,-h,-length),new Vector3(w,h,-length),new Vector3(-w,h,-length)};var transform=camera.Rig.GlobalTransform;
        for(int i=0;i<4;i++){yield return(transform.Origin,transform*corners[i]);yield return(transform*corners[i],transform*corners[(i+1)%4]);}
        yield return(transform*new Vector3(-w*.15f,h,-length),transform*new Vector3(0,h*1.22f,-length));yield return(transform*new Vector3(0,h*1.22f,-length),transform*new Vector3(w*.15f,h,-length));
    }
    static bool ObjectGizmoRayBox(Vector3 origin,Vector3 direction,Aabb bounds,out float distance)
    {
        float near=0,far=float.PositiveInfinity;var end=bounds.End;distance=0;
        for(int axis=0;axis<3;axis++){if(Math.Abs(direction[axis])<.0000001f){if(origin[axis]<bounds.Position[axis]||origin[axis]>end[axis])return false;continue;}float a=(bounds.Position[axis]-origin[axis])/direction[axis],b=(end[axis]-origin[axis])/direction[axis];if(a>b)(a,b)=(b,a);near=Math.Max(near,a);far=Math.Min(far,b);if(near>far)return false;}
        distance=near;return float.IsFinite(near)&&far>=0;
    }
}
