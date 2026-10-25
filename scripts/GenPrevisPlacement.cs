using Godot;
using System;
using System.Linq;

namespace GenPrevis;

public partial class GenPrevisApp
{
    bool _placeOnObject;
    string _placementSourceId="";
    Button _placeGroundButton=null!,_placeObjectButton=null!;
    void BuildPlacementInspector()
    {
        var row=new HBoxContainer();_placeObjectButton=Button("Place on object",BeginPlaceOnObject);_placeGroundButton=Button("Place on ground",PlaceSelectedOnGround);_placeObjectButton.SizeFlagsHorizontal=SizeFlags.ExpandFill;_placeGroundButton.SizeFlagsHorizontal=SizeFlags.ExpandFill;row.AddChild(_placeObjectButton);row.AddChild(_placeGroundButton);_inspector.AddChild(row);
    }
    void UpdatePlacementInspector(){if(_placeGroundButton==null)return;_placeGroundButton.Disabled=_selected==null;_placeObjectButton.Disabled=_selected==null;}
    Aabb PlacementWorldBounds(Entry entry)
    {
        var local=entry.Node is RiggedActor actor?actor.GetPreparedPoseBounds():PropFactory.CalculateBounds(entry.Node);var min=entry.Node.GlobalTransform*local.GetEndpoint(0);var max=min;
        for(int i=1;i<8;i++){var point=entry.Node.GlobalTransform*local.GetEndpoint(i);min=min.Min(point);max=max.Max(point);}return new Aabb(min,max-min);
    }
    Vector3 PlacementSupport(Entry entry,bool allowSeat)
    {
        var bounds=PlacementWorldBounds(entry);var support=new Vector3(bounds.GetCenter().X,bounds.Position.Y,bounds.GetCenter().Z);
        if(!allowSeat||entry.Node is not RiggedActor actor)return support;
        var hip=actor.GlobalTransform*actor.GetPoseJointPosition("Hips");var head=actor.GlobalTransform*actor.GetPoseJointPosition("Head");
        var left=actor.GlobalTransform*actor.GetPoseJointPosition("LeftUpperLeg");var right=actor.GlobalTransform*actor.GetPoseJointPosition("RightUpperLeg");
        var lk=actor.GlobalTransform*actor.GetPoseJointPosition("LeftLowerLeg");var rk=actor.GlobalTransform*actor.GetPoseJointPosition("RightLowerLeg");
        var lf=actor.GlobalTransform*actor.GetPoseJointPosition("LeftFoot");var rf=actor.GlobalTransform*actor.GetPoseJointPosition("RightFoot");
        float legScale=Math.Max(.01f,actor.GlobalBasis.Y.Length());
        bool sitting=head.Y>hip.Y+.25f*legScale&&Math.Abs(lk.Y-left.Y)<.2f*legScale&&Math.Abs(rk.Y-right.Y)<.2f*legScale&&lk.Y>lf.Y+.2f*legScale&&rk.Y>rf.Y+.2f*legScale;
        if(sitting)support=new Vector3(hip.X,Math.Min(left.Y,right.Y)-.068f*legScale,hip.Z);
        return support;
    }
    void MovePlacement(Entry entry,Vector3 worldDelta)
    {
        var parent=entry.Node.GetParentNode3D();var desired=entry.Node.GlobalPosition+worldDelta;SetSelectedTransformFromGizmo("Position",parent==null?desired:parent.GlobalTransform.AffineInverse()*desired);RefreshAllObjectLabels();
    }
    void PlaceSelectedOnGround()
    {
        if(_selected is not {} entry)return;ReleasePoseViewportDrag();EndArrangeDrag();PushUndo();var support=PlacementSupport(entry,false);MovePlacement(entry,Vector3.Up*(SceneGroundHeight-support.Y));Status(entry.Data.Name+" placed on ground at "+SceneGroundHeight.ToString("0.###")+" m.");
    }
    void BeginPlaceOnObject()
    {
        if(_selected==null)return;StopPoseEditing();EndArrangeDrag();ReleaseObjectGizmoDrag();_placeOnObject=true;_placementSourceId=_selected.Data.Id;_sceneContainer.MouseDefaultCursorShape=CursorShape.Cross;Status("Place on object · click a supporting surface · Esc cancels.");
    }
    void CancelPlacement(){_placeOnObject=false;_placementSourceId="";if(_sceneContainer!=null)_sceneContainer.MouseDefaultCursorShape=CursorShape.Arrow;}
    PhysicsRayQueryParameters3D PlacementQuery(Vector3 from,Vector3 to,Entry source)
    {
        var query=PhysicsRayQueryParameters3D.Create(from,to,1);query.Exclude=new Godot.Collections.Array<Rid>(Descendants(source.Node).OfType<CollisionObject3D>().Select(n=>n.GetRid()).Concat(_cameras.SelectMany(c=>Descendants(c.Rig).OfType<CollisionObject3D>()).Select(n=>n.GetRid())));return query;
    }
    bool TryPlacementSurface(Entry source,Vector2 input,out Vector3 point)
    {
        var screen=ObjectGizmoToViewport(input);var from=_editorCamera.ProjectRayOrigin(screen);var direction=_editorCamera.ProjectRayNormal(screen);var state=_world.GetWorld3D().DirectSpaceState;
        var hit=state.IntersectRay(PlacementQuery(from,from+direction*_editorCamera.Far,source));point=Vector3.Zero;if(hit.Count==0)return false;
        point=hit["position"].AsVector3();var normal=hit["normal"].AsVector3();
        if(normal.Dot(Vector3.Up)<.5f)
        {
            Entry? target=null;Node? node=hit["collider"].AsGodotObject() as Node;
            while(node!=null){target=_entries.FirstOrDefault(e=>e.Node==node||node.HasMeta("scene_root")&&node.GetMeta("scene_root").AsGodotObject()==e.Node);if(target!=null)break;node=node.GetParent();}
            if(target==null)return false;var bounds=PlacementWorldBounds(target);var above=new Vector3(point.X,bounds.End.Y+Math.Max(.2f,bounds.Size.Y*.1f),point.Z);var below=new Vector3(point.X,bounds.Position.Y-.2f,point.Z);
            hit=state.IntersectRay(PlacementQuery(above,below,source));if(hit.Count==0||hit["normal"].AsVector3().Dot(Vector3.Up)<.5f)return false;point=hit["position"].AsVector3();
        }
        return point.IsFinite();
    }
    bool HandlePlacementInput(InputEvent input)
    {
        if(!_placeOnObject)return false;
        if(input is not InputEventMouseButton{ButtonIndex:MouseButton.Left} button)return false;if(!button.Pressed)return true;
        var entry=_entries.FirstOrDefault(e=>e.Data.Id==_placementSourceId);if(entry==null){CancelPlacement();return true;}
        if(!TryPlacementSurface(entry,button.Position,out var surface)){Status("Choose an upward-facing object surface. Esc cancels.");return true;}
        Select(entry);PushUndo();var support=PlacementSupport(entry,true);MovePlacement(entry,surface-support);CancelPlacement();Status(entry.Data.Name+" placed on the selected surface.");return true;
    }
}
