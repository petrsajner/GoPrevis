using Godot;
using System;
using System.Linq;

namespace Previs;

public partial class PrevisApp
{
    Button _arrangeButton=null!,_inspectorBack=null!;
    Label _inspectorContext=null!;
    bool _arrangeDragging,_arrangeGestureStarted;
    Vector2 _arrangeMouse;
    Vector3 _arrangeNodeOrigin,_arrangePlaneOrigin,_arrangeNormal,_arrangePointerStart;
    Node3D? _arrangeNode;

    Control BuildUnifiedViewportToolbar()
    {
        _arrangeButton=Button("Arrange",()=>{StopPoseEditing();ShowInspectorSection("Object");},true);
        _arrangeButton.TooltipText="Select and drag while keeping the current height. Hold Shift for unrestricted move, rotate and scale handles.";
        _poseToolbarButton=Button("Pose",()=>{if(_poseEditing){StopPoseEditing();ShowInspectorSection("Object");}else StartPoseEditing();},true);
        _poseToolbarButton.TooltipText="Adjust the figure at the playhead. Corrections are keyed above its existing motion.";
        var bar=Row(_arrangeButton,_poseToolbarButton,_drawPathButton,_finishPathButton,Space(),Button("Frame",FrameSelected),Button("Perspective",()=>{if(_pilot)TogglePilot();else ResetEditorView();}));bar.AddThemeConstantOverride("separation",4);return bar;
    }
    void RefreshUnifiedWorkflow()
    {
        if(_arrangeButton!=null)_arrangeButton.ButtonPressed=!_poseEditing;
        if(_drawPathButton!=null)_drawPathButton.Visible=_drawingPath||_selected!=null;
        if(_inspectorContext!=null)
        {
            _inspectorContext.Text=_inspectorSection switch{"Pose"=>"POSE CORRECTIONS","Motion"=>"MOTION CLIP","Path"=>"TRAVEL PATH",_=>"PROPERTIES"};
            _inspectorBack.Visible=_inspectorSection!="Object";
        }
    }
    bool ArrangePlanePoint(Vector2 input,out Vector3 point)
    {
        var screen=ObjectGizmoToViewport(input);var origin=_editorCamera.ProjectRayOrigin(screen);var direction=_editorCamera.ProjectRayNormal(screen);float denominator=_arrangeNormal.Dot(direction);point=_arrangePlaneOrigin;
        if(Math.Abs(denominator)<.00001f)return false;float distance=_arrangeNormal.Dot(_arrangePlaneOrigin-origin)/denominator;if(distance<0)return false;point=origin+direction*distance;return point.IsFinite();
    }
    void BeginArrangeDrag(Vector2 mouse)
    {
        _arrangeNode=ObjectGizmoTarget;if(_arrangeNode==null)return;_playing=false;_play.Text="▶";
        _arrangeNodeOrigin=_arrangeNode.GlobalPosition;_arrangePlaneOrigin=_arrangeNodeOrigin;
        _arrangeNormal=Vector3.Up;_arrangeMouse=mouse;_arrangeDragging=ArrangePlanePoint(mouse,out _arrangePointerStart);_arrangeGestureStarted=false;
    }
    bool HandleArrangeDrag(InputEvent input)
    {
        if(!_arrangeDragging)return false;
        if(input is InputEventMouseButton{ButtonIndex:MouseButton.Left,Pressed:false}){EndArrangeDrag();return true;}
        if(input is not InputEventMouseMotion motion)return false;
        if(_arrangeNode==null||_arrangeNode!=ObjectGizmoTarget){EndArrangeDrag();return true;}
        if(!_arrangeGestureStarted){if(motion.Position.DistanceTo(_arrangeMouse)<2)return true;BeginSceneTransformGesture();_arrangeGestureStarted=true;}
        if(ArrangePlanePoint(motion.Position,out var point))
        {
            var world=_arrangeNodeOrigin+point-_arrangePointerStart;world.Y=_arrangeNodeOrigin.Y;if(motion.CtrlPressed)world=new Vector3(Mathf.Snapped(world.X,.01f),_arrangeNodeOrigin.Y,Mathf.Snapped(world.Z,.01f));
            var parent=_arrangeNode.GetParentNode3D();SetSelectedTransformFromGizmo("Position",parent==null?world:parent.GlobalTransform.AffineInverse()*world);
        }
        return true;
    }
    void EndArrangeDrag(){_arrangeDragging=false;_arrangeNode=null;if(_arrangeGestureStarted){_arrangeGestureStarted=false;EndSceneTransformGesture();}}
    Entry? PickArrangeObject(Vector2 input)
    {
        var picked=Pick(ObjectGizmoToViewport(input));if(picked!=null)return picked;
        // The visible selection box remains a usable grab surface, including
        // empty space between chair legs or inside a posed figure's bounds.
        if(_selected is {} selected)
        {
            var box=ProjectedActorBounds(selected);var point=ObjectGizmoToViewport(input);
            if(box.Grow(5).HasPoint(point))return selected;
        }
        return _entries.Where(e=>ProjectedActorBounds(e).HasPoint(ObjectGizmoToViewport(input))).OrderBy(e=>e.Node.GlobalPosition.DistanceSquaredTo(_editorCamera.GlobalPosition)).FirstOrDefault();
    }
}
