using Godot;
using System;
using System.Collections.Generic;
using System.Linq;

namespace Previs;

/// <summary>Screen-sized pose handles operate on the selected rig, never on its scene-object transform.</summary>
public partial class PrevisApp
{
    PoseViewportOverlay? _poseViewportOverlay;
    enum PoseViewportDrag { None, ScreenMove, AxisMove, FreeRotate, AxisRotate }
    PoseViewportDrag _poseViewportDrag;
    bool _poseViewportGestureStarted;
    string _poseViewportDragBone="",_poseViewportActorId="",_poseViewportHoverBone="";
    int _poseViewportAxis;
    Vector2 _poseViewportDragMouse;
    Vector3 _poseViewportOrigin,_poseViewportAxisWorld,_poseViewportPlaneNormal,_poseViewportPlaneStart,_poseViewportRotateStart;
    Quaternion _poseViewportStartRotation=Quaternion.Identity;
    float _poseViewportAxisStart,_poseViewportHandleLength;
    bool _poseViewportAxisStartValid,_poseViewportPlaneStartValid,_poseViewportRotateStartValid;
    bool _poseShiftHeld;
    bool PoseViewportDragging=>_poseViewportDrag!=PoseViewportDrag.None;
    bool PoseShiftActive=>_poseShiftHeld||Input.IsPhysicalKeyPressed(Key.Shift);
    string EffectivePoseTool=>_poseViewportDrag is PoseViewportDrag.ScreenMove or PoseViewportDrag.AxisMove?"Move":_poseViewportDrag is PoseViewportDrag.FreeRotate or PoseViewportDrag.AxisRotate?"Rotate":PoseShiftActive?(_poseTool=="Move"?"Rotate":"Move"):_poseTool;
    void UpdatePoseModifierState(InputEvent input)
    {
        bool before=_poseShiftHeld;
        if(!_poseEditing)_poseShiftHeld=false;
        else if(input is InputEventKey key&&((key.Keycode==Key.Shift)||(key.PhysicalKeycode==Key.Shift)))_poseShiftHeld=key.Pressed;
        else if(input is InputEventWithModifiers modifiers)_poseShiftHeld=modifiers.ShiftPressed;
        if(before!=_poseShiftHeld){UpdatePoseInspector();_poseViewportOverlay?.QueueRedraw();}
    }
    static readonly string[] PoseViewportBones={"Hips","Chest","Neck","Head","LeftUpperArm","LeftLowerArm","LeftHand","RightUpperArm","RightLowerArm","RightHand","LeftUpperLeg","LeftLowerLeg","LeftFoot","RightUpperLeg","RightLowerLeg","RightFoot"};
    static readonly (string,string)[] PoseViewportLinks={
        ("Hips","Chest"),("Chest","Neck"),("Neck","Head"),
        ("Chest","LeftUpperArm"),("LeftUpperArm","LeftLowerArm"),("LeftLowerArm","LeftHand"),
        ("Chest","RightUpperArm"),("RightUpperArm","RightLowerArm"),("RightLowerArm","RightHand"),
        ("Hips","LeftUpperLeg"),("LeftUpperLeg","LeftLowerLeg"),("LeftLowerLeg","LeftFoot"),
        ("Hips","RightUpperLeg"),("RightUpperLeg","RightLowerLeg"),("RightLowerLeg","RightFoot")};
    static readonly Color[] PoseViewportAxisColors={new("ee817e"),new("8ccba1"),new("83acf2")};

    void BuildPoseViewportOverlay()
    {
        _poseViewportOverlay=new PoseViewportOverlay{App=this,MouseFilter=MouseFilterEnum.Ignore,ClipContents=true};
        _poseViewportOverlay.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);_sceneContainer.AddChild(_poseViewportOverlay);
    }
    partial class PoseViewportOverlay:Control
    {
        public PrevisApp App=null!;
        public override void _Process(double delta){Visible=App._poseEditing&&(App._selected?.Node is RiggedActor||App._selected?.NativeTrack!=null);if(Visible)QueueRedraw();}
        public override void _Draw()=>App.DrawPoseViewport(this);
    }
    static bool PoseViewportCanMove(string bone)=>PoseCanMove(bone);
    static bool PoseViewportDirectBody(string? bone)=>bone is "Head" or "LeftHand" or "RightHand" or "LeftFoot" or "RightFoot";
    static Vector3 PoseViewportAxisVector(int index)=>index==0?Vector3.Right:index==1?Vector3.Up:Vector3.Back;
    Vector2 PoseViewportToInput(Vector2 point)=>point*_sceneContainer.Size/new Vector2(Math.Max(1,_sceneViewport.Size.X),Math.Max(1,_sceneViewport.Size.Y));
    Vector2 PoseInputToViewport(Vector2 point)=>point*new Vector2(_sceneViewport.Size.X,_sceneViewport.Size.Y)/new Vector2(Math.Max(1,_sceneContainer.Size.X),Math.Max(1,_sceneContainer.Size.Y));
    Vector3 PoseViewportWorldJoint(RiggedActor actor,string bone)=>actor.GlobalTransform*actor.GetPoseJointPosition(bone);
    bool PoseViewportProject(Vector3 world,out Vector2 point)
    {
        point=Vector2.Zero;if(_editorCamera.IsPositionBehind(world))return false;
        point=PoseViewportToInput(_editorCamera.UnprojectPosition(world));return point.IsFinite();
    }
    float PoseViewportLength(Vector3 world)
    {
        // Choose a real world length that projects to about 62 UI pixels at the joint's depth.
        float depth=Math.Max(.05f,-(_editorCamera.GlobalTransform.AffineInverse()*world).Z);
        Vector2 screen=_editorCamera.UnprojectPosition(world);
        float length=_editorCamera.ProjectPosition(screen+new Vector2(62*_sceneViewport.Size.X/Math.Max(1,_sceneContainer.Size.X),0),depth).DistanceTo(world);
        return Math.Clamp(float.IsFinite(length)?length:.35f,.025f,10);
    }
    Basis PoseViewportBoneBasis(RiggedActor actor,string bone)
    {
        return (actor.GlobalBasis.Orthonormalized()*new Basis(actor.GetPoseJointGlobalRotation(bone))).Orthonormalized();
    }
    static string PoseViewportBoneLabel(string bone)=>bone switch{
        "LeftUpperArm"=>"Left shoulder","RightUpperArm"=>"Right shoulder","LeftLowerArm"=>"Left elbow","RightLowerArm"=>"Right elbow",
        "LeftUpperLeg"=>"Left hip","RightUpperLeg"=>"Right hip","LeftLowerLeg"=>"Left knee","RightLowerLeg"=>"Right knee",
        "LeftHand"=>"Left hand","RightHand"=>"Right hand","LeftFoot"=>"Left foot","RightFoot"=>"Right foot",_=>bone};

    void DrawPoseViewport(Control canvas)
    {
        if(_poseEditing&&_selected?.NativeTrack is {} native){DrawAnimalPoseViewport(canvas,native);return;}
        if(!_poseEditing||_selected?.Node is not RiggedActor actor)return;
        string highlighted=PoseViewportDragging?_poseViewportDragBone:_poseViewportHoverBone;
        if(highlighted!="")DrawPoseViewportBodyHighlight(canvas,actor,highlighted);
        var points=new Dictionary<string,Vector2>();
        foreach(string bone in PoseViewportBones.Prepend(_poseBone).Distinct())if(PoseViewportProject(PoseViewportWorldJoint(actor,bone),out var p))points[bone]=p;
        foreach(var link in PoseViewportLinks)if(points.TryGetValue(link.Item1,out var a)&&points.TryGetValue(link.Item2,out var b))canvas.DrawLine(a,b,new Color(.76f,.72f,.93f,.35f),1.2f,true);
        foreach(var pair in points)
        {
            bool selected=pair.Key==_poseBone,hovered=pair.Key==_poseViewportHoverBone;
            canvas.DrawCircle(pair.Value,selected?7:hovered?6:4.5f,new Color("15151c"));
            canvas.DrawCircle(pair.Value,selected?5.2f:hovered?4.6f:3.2f,selected?new Color("f2d291"):hovered?Colors.White:new Color("c9bff0"));
        }
        Vector3 origin=PoseViewportWorldJoint(actor,_poseBone);if(!PoseViewportProject(origin,out var center))return;
        float length=PoseViewportLength(origin);bool move=EffectivePoseTool=="Move"&&PoseViewportCanMove(_poseBone);
        if(move)
        {
            for(int axis=0;axis<3;axis++)
            {
                if(!PoseViewportProject(origin+PoseViewportAxisVector(axis)*length,out var end)||(end-center).Length()<12)continue;
                var direction=(end-center).Normalized();var side=new Vector2(-direction.Y,direction.X);var color=PoseViewportAxisColors[axis];
                canvas.DrawLine(center+direction*10,end,color,2.2f,true);
                canvas.DrawColoredPolygon(new[]{end,end-direction*10+side*4,end-direction*10-side*4},color);
                canvas.DrawString(ThemeDB.FallbackFont,end+direction*11+new Vector2(-4,4),"XYZ"[axis].ToString(),fontSize:12,modulate:color);
            }
            canvas.DrawArc(center,9,0,Mathf.Tau,32,new Color("f2d291"),1.6f,true);
        }
        else
        {
            var basis=PoseViewportBoneBasis(actor,_poseBone);
            for(int axis=0;axis<3;axis++)
            {
                var ring=PoseViewportRing(origin,basis,axis,length*.83f);
                for(int i=1;i<ring.Length;i++)if(ring[i-1].HasValue&&ring[i].HasValue)canvas.DrawLine(ring[i-1]!.Value,ring[i]!.Value,PoseViewportAxisColors[axis],1.5f,true);
                var label=ring[axis==0?8:axis==1?3:15];if(label.HasValue)canvas.DrawString(ThemeDB.FallbackFont,label.Value+new Vector2(4,-3),"XYZ"[axis].ToString(),fontSize:11,modulate:PoseViewportAxisColors[axis]);
            }
        }
        canvas.DrawString(ThemeDB.FallbackFont,center+new Vector2(12,-15),PoseViewportBoneLabel(_poseBone),fontSize:12,modulate:new Color("f2dfb7"));
        canvas.DrawString(ThemeDB.FallbackFont,new Vector2(12,22),"POSE EDIT · "+PoseViewportBoneLabel(_poseBone),fontSize:12,modulate:new Color("e1d7f7"));
        canvas.DrawString(ThemeDB.FallbackFont,new Vector2(12,41),(move?"MOVE · XYZ arrows constrain":"ROTATE · colored rings constrain")+" · Shift: alternate tool · Alt: fine · Ctrl: snap",fontSize:11,modulate:new Color("b7b2c4"));
    }
    Vector2?[] PoseViewportRing(Vector3 origin,Basis basis,int axis,float radius)
    {
        Vector3 normal=(basis*PoseViewportAxisVector(axis)).Normalized();Vector3 u=(basis*PoseViewportAxisVector((axis+1)%3)).Normalized();Vector3 v=normal.Cross(u).Normalized();
        var result=new Vector2?[49];for(int i=0;i<result.Length;i++){float angle=Mathf.Tau*i/(result.Length-1);if(PoseViewportProject(origin+(u*Mathf.Cos(angle)+v*Mathf.Sin(angle))*radius,out var p))result[i]=p;}return result;
    }
    static float PoseViewportSegmentDistance(Vector2 point,Vector2 a,Vector2 b)
    {
        Vector2 d=b-a;float length=d.LengthSquared();return point.DistanceTo(length<.0001f?a:a+d*Math.Clamp((point-a).Dot(d)/length,0,1));
    }
    int PoseViewportHitAxis(RiggedActor actor,Vector2 point,bool move)
    {
        Vector3 origin=PoseViewportWorldJoint(actor,_poseBone);if(!PoseViewportProject(origin,out var center)||point.DistanceTo(center)<12)return -1;
        float best=7;int hit=-1;float length=PoseViewportLength(origin);var basis=PoseViewportBoneBasis(actor,_poseBone);
        for(int axis=0;axis<3;axis++)
        {
            float distance=float.PositiveInfinity;
            if(move)
            {
                if(PoseViewportProject(origin+PoseViewportAxisVector(axis)*length,out var end)&&(end-center).Length()>12)distance=PoseViewportSegmentDistance(point,center+(end-center).Normalized()*12,end);
            }
            else
            {
                var ring=PoseViewportRing(origin,basis,axis,length*.83f);for(int i=1;i<ring.Length;i++)if(ring[i-1].HasValue&&ring[i].HasValue)distance=Math.Min(distance,PoseViewportSegmentDistance(point,ring[i-1]!.Value,ring[i]!.Value));
            }
            if(distance<best){best=distance;hit=axis;}
        }
        return hit;
    }
    string? PoseViewportHitBone(RiggedActor actor,Vector2 point)
    {
        string? best=null;float distance=12;
        foreach(string bone in PoseViewportBones.Prepend(_poseBone).Distinct())
        {
            if(!PoseViewportProject(PoseViewportWorldJoint(actor,bone),out var p))continue;float candidate=p.DistanceTo(point);if(candidate<distance){best=bone;distance=candidate;if(bone==_poseBone&&candidate<8)return best;}
        }
        return best;
    }

    readonly record struct PoseViewportBodyMesh(string Bone,MeshInstance3D Mesh,Transform3D World);
    IEnumerable<PoseViewportBodyMesh> PoseViewportBodyMeshes(RiggedActor actor)
    {
        // BoneAttachment3D updates are deferred. Build each visible mesh transform
        // directly from the current rig pose so picking follows the drag immediately.
        var skeleton=actor.GetNode<Skeleton3D>("PreparedHumanoid");
        foreach(var attachment in skeleton.GetChildren().OfType<BoneAttachment3D>())
        {
            string bone=attachment.BoneName.ToString();
            if(string.IsNullOrEmpty(bone)||!actor.PoseBoneNames.Contains(bone))continue;
            var bonePose=new Transform3D(new Basis(actor.GetPoseJointGlobalRotation(bone)),actor.GetPoseJointPosition(bone));
            foreach(var part in PoseViewportChildMeshes(attachment,actor.GlobalTransform*bonePose,bone))yield return part;
        }
    }
    static IEnumerable<PoseViewportBodyMesh> PoseViewportChildMeshes(Node parent,Transform3D transform,string bone)
    {
        foreach(Node child in parent.GetChildren())
        {
            if(child is Node3D {Visible:false})continue;
            var world=child is Node3D spatial?transform*spatial.Transform:transform;
            if(child is MeshInstance3D {Mesh:not null} mesh)yield return new PoseViewportBodyMesh(bone,mesh,world);
            foreach(var nested in PoseViewportChildMeshes(child,world,bone))yield return nested;
        }
    }
    string? PoseViewportHitBody(RiggedActor actor,Vector2 point)
    {
        Vector2 screen=PoseInputToViewport(point);
        Vector3 origin=_editorCamera.ProjectRayOrigin(screen),direction=_editorCamera.ProjectRayNormal(screen);
        string? hit=null;float closest=_editorCamera.Far;
        foreach(var part in PoseViewportBodyMeshes(actor))
        {
            if(Math.Abs(part.World.Basis.Determinant())<.00000001f)continue;
            var inverse=part.World.AffineInverse();
            // Deliberately do not normalize this transformed ray: t remains a
            // world-ray distance even for scaled or rotated mannequin variants.
            Vector3 localOrigin=inverse*origin,localDirection=inverse.Basis*direction;
            if(PoseViewportRayBox(localOrigin,localDirection,part.Mesh.GetAabb(),closest,out float distance))
            {
                hit=part.Bone;closest=distance;
            }
        }
        return hit;
    }
    static bool PoseViewportRayBox(Vector3 origin,Vector3 direction,Aabb bounds,float maximum,out float distance)
    {
        float near=0,far=maximum;Vector3 end=bounds.End;distance=0;
        for(int axis=0;axis<3;axis++)
        {
            if(Math.Abs(direction[axis])<.0000001f)
            {
                if(origin[axis]<bounds.Position[axis]||origin[axis]>end[axis])return false;
                continue;
            }
            float first=(bounds.Position[axis]-origin[axis])/direction[axis],last=(end[axis]-origin[axis])/direction[axis];
            if(first>last)(first,last)=(last,first);near=Math.Max(near,first);far=Math.Min(far,last);if(near>far)return false;
        }
        distance=near;return float.IsFinite(near)&&near<=maximum&&far>=0;
    }
    void DrawPoseViewportBodyHighlight(Control canvas,RiggedActor actor,string bone)
    {
        bool found=false;Vector2 minimum=Vector2.Zero,maximum=Vector2.Zero;
        foreach(var part in PoseViewportBodyMeshes(actor).Where(part=>part.Bone==bone))
        {
            var points=new Vector2?[8];var bounds=part.Mesh.GetAabb();
            for(int i=0;i<8;i++)
            {
                if(!PoseViewportProject(part.World*bounds.GetEndpoint(i),out var point))continue;
                points[i]=point;if(!found){minimum=maximum=point;found=true;}else{minimum=minimum.Min(point);maximum=maximum.Max(point);}
            }
            for(int i=0;i<8;i++)foreach(int bit in new[]{1,2,4})if((i&bit)==0&&points[i].HasValue&&points[i|bit].HasValue)
                canvas.DrawLine(points[i]!.Value,points[i|bit]!.Value,new Color(.98f,.86f,.59f,.85f),1.5f,true);
        }
        if(found&&bone!=_poseBone)canvas.DrawString(ThemeDB.FallbackFont,new Vector2(maximum.X+6,minimum.Y-4),PoseViewportBoneLabel(bone),fontSize:12,modulate:new Color("f2dfb7"));
    }

    bool HandlePoseViewportInput(InputEvent input)
    {
        UpdatePoseModifierState(input);
        if(_poseEditing&&_selected?.NativeTrack is {} native)return HandleAnimalPoseViewportInput(input,native);
        if(!_poseEditing||_selected?.Node is not RiggedActor actor){ReleasePoseViewportDrag();return false;}
        if(input is InputEventMouseButton {ButtonIndex:MouseButton.Left} button)
        {
            if(!button.Pressed){ReleasePoseViewportDrag();return true;}
            ReleasePoseViewportDrag();bool move=EffectivePoseTool=="Move"&&PoseViewportCanMove(_poseBone);
            string? body=PoseViewportHitBody(actor,button.Position),bone;int axis=-1;
            if(PoseViewportDirectBody(body))
            {
                // A visible palm, foot or head must beat unrelated x-ray joint
                // markers projected over it (for example a hip behind the hand).
                int selectedAxis=body==_poseBone?PoseViewportHitAxis(actor,button.Position,move):-1;
                if(selectedAxis>=0){bone=_poseBone;axis=selectedAxis;}
                else{bone=body;}
            }
            else
            {
                bone=PoseViewportHitBone(actor,button.Position);
                if(bone==null){axis=PoseViewportHitAxis(actor,button.Position,move);bone=axis>=0?_poseBone:body;}
            }
            if(bone==null)return true; // Pose mode must never start the ordinary whole-object Move drag.
            if(bone!=_poseBone)SelectPoseBone(bone);move=EffectivePoseTool=="Move"&&PoseViewportCanMove(bone);
            _poseViewportActorId=_selected.Data.Id;_poseViewportDragBone=bone;_poseViewportDragMouse=button.Position;_poseViewportAxis=axis;
            _poseViewportDrag=move?(axis<0?PoseViewportDrag.ScreenMove:PoseViewportDrag.AxisMove):(axis<0?PoseViewportDrag.FreeRotate:PoseViewportDrag.AxisRotate);
            return true;
        }
        if(input is InputEventMouseButton {ButtonIndex:MouseButton.Right or MouseButton.Middle,Pressed:true}){ReleasePoseViewportDrag();return false;}
        if(input is not InputEventMouseMotion motion)return false;
        string? hoveredBody=PoseViewportHitBody(actor,motion.Position);
        _poseViewportHoverBone=PoseViewportDirectBody(hoveredBody)?hoveredBody!:PoseViewportHitBone(actor,motion.Position)??hoveredBody??"";
        if(_poseViewportDrag==PoseViewportDrag.None)return false;
        if(_selected.Data.Id!=_poseViewportActorId){ReleasePoseViewportDrag();return true;}
        if(!_poseViewportGestureStarted)
        {
            if(motion.Position.DistanceTo(_poseViewportDragMouse)<2)return true;
            BeginPoseGesture();_poseViewportGestureStarted=true;InitializePoseViewportDrag(actor);
        }
        try
        {
            float fine=motion.AltPressed ? .2f : 1f;
            if(_poseViewportDrag is PoseViewportDrag.ScreenMove or PoseViewportDrag.AxisMove)
            {
                Vector3 target=_poseViewportOrigin;
                if(_poseViewportDrag==PoseViewportDrag.ScreenMove)
                {
                    if(!_poseViewportPlaneStartValid||!PoseViewportPlanePoint(motion.Position,_poseViewportOrigin,_poseViewportPlaneNormal,out var point))return true;
                    target+=(point-_poseViewportPlaneStart)*fine;
                }
                else
                {
                    float amount;
                    if(_poseViewportAxisStartValid&&PoseViewportRayAxis(motion.Position,_poseViewportOrigin,_poseViewportAxisWorld,out var parameter))amount=parameter-_poseViewportAxisStart;
                    else
                    {
                        if(!PoseViewportProject(_poseViewportOrigin,out var a)||!PoseViewportProject(_poseViewportOrigin+_poseViewportAxisWorld*_poseViewportHandleLength,out var b)||(b-a).Length()<1)return true;
                        amount=(motion.Position-_poseViewportDragMouse).Dot((b-a).Normalized())/(b-a).Length()*_poseViewportHandleLength;
                    }
                    amount*=fine;if(motion.CtrlPressed)amount=Mathf.Snapped(amount,.01f);target+=_poseViewportAxisWorld*amount;
                }
                if(motion.CtrlPressed&&_poseViewportDrag==PoseViewportDrag.ScreenMove)target=new Vector3(Mathf.Snapped(target.X,.01f),Mathf.Snapped(target.Y,.01f),Mathf.Snapped(target.Z,.01f));
                UpdatePoseEffectorWorld(_poseViewportDragBone,target);
            }
            else
            {
                Quaternion rotation;
                if(_poseViewportDrag==PoseViewportDrag.FreeRotate)
                {
                    Vector2 delta=(motion.Position-_poseViewportDragMouse)*.45f*fine;
                    if(motion.CtrlPressed)delta=new Vector2(Mathf.Snapped(delta.X,5),Mathf.Snapped(delta.Y,5));
                    rotation=_poseViewportStartRotation*new Quaternion(Vector3.Up,Mathf.DegToRad(delta.X))*new Quaternion(Vector3.Right,Mathf.DegToRad(-delta.Y));
                }
                else
                {
                    float angle;
                    if(_poseViewportRotateStartValid&&PoseViewportPlanePoint(motion.Position,_poseViewportOrigin,_poseViewportAxisWorld,out var point)&&(point-_poseViewportOrigin).LengthSquared()>.000001f)
                    {
                        Vector3 current=(point-_poseViewportOrigin).Normalized();angle=Mathf.Atan2(_poseViewportAxisWorld.Dot(_poseViewportRotateStart.Cross(current)),_poseViewportRotateStart.Dot(current));
                    }
                    else angle=(motion.Position-_poseViewportDragMouse).Dot(new Vector2(1,-1))*.012f;
                    angle*=fine;if(motion.CtrlPressed)angle=Mathf.Snapped(angle,Mathf.DegToRad(15));rotation=_poseViewportStartRotation*new Quaternion(PoseViewportAxisVector(_poseViewportAxis),angle);
                }
                UpdatePoseBoneRotation(new Basis(rotation.Normalized()).GetEuler()*180f/Mathf.Pi);
            }
        }
        catch(Exception error){ReleasePoseViewportDrag();Error(error.Message);}
        return true;
    }
    void InitializePoseViewportDrag(RiggedActor actor)
    {
        _poseViewportOrigin=PoseViewportWorldJoint(actor,_poseViewportDragBone);_poseViewportHandleLength=PoseViewportLength(_poseViewportOrigin);
        var skeleton=actor.GetNode<Skeleton3D>("PreparedHumanoid");int bone=skeleton.FindBone(_poseViewportDragBone);_poseViewportStartRotation=skeleton.GetBonePoseRotation(bone);
        _poseViewportPlaneNormal=_editorCamera.GlobalBasis.Z.Normalized();_poseViewportPlaneStartValid=PoseViewportPlanePoint(_poseViewportDragMouse,_poseViewportOrigin,_poseViewportPlaneNormal,out _poseViewportPlaneStart);
        _poseViewportAxisWorld=_poseViewportDrag==PoseViewportDrag.AxisRotate?(PoseViewportBoneBasis(actor,_poseViewportDragBone)*PoseViewportAxisVector(_poseViewportAxis)).Normalized():PoseViewportAxisVector(_poseViewportAxis);
        _poseViewportAxisStartValid=PoseViewportRayAxis(_poseViewportDragMouse,_poseViewportOrigin,_poseViewportAxisWorld,out _poseViewportAxisStart);
        _poseViewportRotateStartValid=PoseViewportPlanePoint(_poseViewportDragMouse,_poseViewportOrigin,_poseViewportAxisWorld,out var rotateStart)&&(rotateStart-_poseViewportOrigin).LengthSquared()>.000001f;
        _poseViewportRotateStart=_poseViewportRotateStartValid?(rotateStart-_poseViewportOrigin).Normalized():Vector3.Right;
    }
    bool PoseViewportPlanePoint(Vector2 input,Vector3 origin,Vector3 normal,out Vector3 point)
    {
        Vector2 screen=PoseInputToViewport(input);Vector3 rayOrigin=_editorCamera.ProjectRayOrigin(screen),direction=_editorCamera.ProjectRayNormal(screen);float denominator=normal.Dot(direction);point=origin;
        if(Math.Abs(denominator)<.00001f)return false;float distance=normal.Dot(origin-rayOrigin)/denominator;if(!float.IsFinite(distance)||distance<0)return false;
        point=rayOrigin+direction*distance;return point.IsFinite();
    }
    bool PoseViewportRayAxis(Vector2 input,Vector3 origin,Vector3 axis,out float parameter)
    {
        Vector2 screen=PoseInputToViewport(input);Vector3 rayOrigin=_editorCamera.ProjectRayOrigin(screen),direction=_editorCamera.ProjectRayNormal(screen),delta=rayOrigin-origin;float dot=axis.Dot(direction),denominator=1-dot*dot;parameter=0;
        if(denominator<.0001f)return false;parameter=(axis.Dot(delta)-dot*direction.Dot(delta))/denominator;return float.IsFinite(parameter);
    }
    void ReleasePoseViewportDrag()
    {
        bool started=_poseViewportGestureStarted;_poseViewportGestureStarted=false;_poseViewportDrag=PoseViewportDrag.None;_poseViewportHoverBone="";
        if(started)EndPoseGesture();
    }
}
