using Godot;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;

namespace GoPrevis;

public partial class GoPrevisApp
{
    public async Task RunInteractionChecks()
    {
        var original=Snapshot();string projectPath=_projectPath;double time=_time;var undo=_undo.ToArray();var redo=_redo.ToArray();var view=_editorCamera.Transform;int checks=0;bool passed=false;string? failure=null;var observations=new List<object>();
        void Check(bool value,string description){observations.Add(new{description,passed=value});Require(value,"Interaction: "+description);checks++;}
        async Task Settle(){for(int i=0;i<3;i++)await ToSignal(GetTree(),SceneTree.SignalName.ProcessFrame);}
        void Send(InputEvent input)=>_sceneContainer.EmitSignal(Control.SignalName.GuiInput,input);
        void Shift(bool pressed)=>_Input(new InputEventKey{Keycode=Key.Shift,Pressed=pressed});
        Vector2 Project(Vector3 world)=>ObjectGizmoToInput(_editorCamera.UnprojectPosition(world));
        void Drag(Vector2 from,Vector2 to,bool shift=true)
        {
            Send(new InputEventMouseButton{ButtonIndex=MouseButton.Left,Pressed=true,Position=from,ShiftPressed=shift});
            Send(new InputEventMouseMotion{Position=to,Relative=to-from,ShiftPressed=shift});
            Send(new InputEventMouseButton{ButtonIndex=MouseButton.Left,Pressed=false,Position=to,ShiftPressed=shift});
        }
        try
        {
            Check(_tabs.Keys.Order().SequenceEqual(new[]{"Models","Objects","Poses","Motions","Cameras"}.Order()),"five coherent library sections replace the fragmented menu");
            Check(!_arrangeButton.GetParent().GetChildren().OfType<Button>().Any(b=>b.Text is "Select" or "Move"),"the viewport has one Arrange tool rather than competing Select and Move tools");
            ShowCategory("Models");Check(Descendants(_library).OfType<LibraryCard>().Count()==19&&Descendants(_library).OfType<Button>().Any(b=>b.Text.Contains("People"))&&Descendants(_library).OfType<Button>().Any(b=>b.Text.Contains("Animals")),"Models contains both visibly grouped people and animals");
            ShowCategory("Objects");Check(Descendants(_library).OfType<LibraryCard>().Count()==265,"Objects includes all props and environment models in one scrollable library");
            ShowCategory("Cameras");Check(CameraMoveCatalog.All.Count==24&&Descendants(_library).OfType<Label>().Any(b=>b.Text=="Dolly in")&&Descendants(_library).OfType<Label>().Any(b=>b.Text=="Orbit"),"camera movement presets live with the camera tools");
            _playing=false;StopPoseEditing();ClearObjects();ClearCameras();CreateCamera("Camera test",null,true);SetTime(0);
            await Settle();DropPose("pose-sit-upright",null,new Vector3(-2,0,0));var actorEntry=_selected!;var actor=(RiggedActor)actorEntry.Node;StartPoseEditing();await Settle();await ToSignal(GetTree(),SceneTree.SignalName.PhysicsFrame);SelectPoseBone("RightHand");SetPoseTool("Move");
            Shift(true);Check(EffectivePoseTool=="Rotate","Shift switches a terminal hand to rotation before any pointer movement");
            var hand=actor.GetPoseJointPosition("RightHand");var oldRotation=actor.GetPoseJointGlobalRotation("RightHand");var point=PoseViewportToInput(_editorCamera.UnprojectPosition(actor.GlobalTransform*hand));
            Send(new InputEventMouseButton{ButtonIndex=MouseButton.Left,Pressed=true,Position=point,ShiftPressed=true});Shift(false);
            Send(new InputEventMouseMotion{Position=point+new Vector2(22,-11),Relative=new Vector2(22,-11)});
            Check(EffectivePoseTool=="Rotate","releasing Shift during a drag does not change its latched operation");
            Send(new InputEventMouseButton{ButtonIndex=MouseButton.Left,Pressed=false,Position=point+new Vector2(22,-11)});
            Check(Math.Abs(oldRotation.Dot(actor.GetPoseJointGlobalRotation("RightHand")))<.999&&actor.GetPoseJointPosition("RightHand").DistanceTo(hand)<.001f,"terminal-joint rotation changes its orientation without moving the wrist");
            Check(EffectivePoseTool=="Move","the base pose tool returns after mouse release");
            SetPoseTool("Rotate");SelectPoseBone("RightHand");Check(EffectivePoseTool=="Rotate","reselecting a hand retains an explicit Rotate choice");
            foreach(string middle in new[]{"RightLowerArm","RightLowerLeg"})
            {
                SelectPoseBone(middle);SetPoseTool("Rotate");Shift(true);Check(EffectivePoseTool=="Move",middle+" switches to Move with Shift");
                string end=middle.EndsWith("Arm",StringComparison.Ordinal)?"RightHand":"RightFoot";var endpoint=actor.GetPoseJointPosition(end);var before=actor.GetPoseJointPosition(middle);
                BeginPoseGesture();UpdatePoseEffectorWorld(middle,actor.GlobalTransform*(before+new Vector3(.1f,.03f,.07f)));EndPoseGesture();Shift(false);
                Check(actor.GetPoseJointPosition(middle).DistanceTo(before)>.005f,middle+" can be repositioned through the application callback");
                Check(actor.GetPoseJointPosition(end).DistanceTo(endpoint)>.005f,middle+" moves its child hand or foot with the edited joint");
            }
            SelectPoseBone("Head");_UnhandledInput(new InputEventKey{Keycode=Key.W,Pressed=true});Check(_poseTool=="Move","W selects Move while posing");_UnhandledInput(new InputEventKey{Keycode=Key.E,Pressed=true});Check(_poseTool=="Rotate","E selects Rotate while posing");
            var camera=_cameras[0];camera.Rig.SetPose(new Vector3(0,1.5f,0),new Vector3(0,1,-2));SyncCameraFromRig(camera);_editorCamera.Position=new Vector3(3,3,4);_editorCamera.LookAt(new Vector3(0,1.5f,0));SetTime(0);await Settle();await ToSignal(GetTree(),SceneTree.SignalName.PhysicsFrame);
            var body=camera.Rig.GetNode<MeshInstance3D>("CameraBody");var cameraPoint=Project(body.GlobalTransform*body.GetAabb().GetCenter());
            Check(PickViewportCamera(cameraPoint)==camera,"the visible camera body is pickable from the working view");
            Send(new InputEventMouseButton{ButtonIndex=MouseButton.Left,Pressed=true,Position=cameraPoint});Send(new InputEventMouseButton{ButtonIndex=MouseButton.Left,Pressed=false,Position=cameraPoint});
            Check(_selectedCamera==camera&&!_poseEditing&&_selected==null,"clicking a camera exits pose mode and opens its own properties");
            var cameraStart=camera.Rig.GlobalPosition;int history=_undo.Count;Drag(cameraPoint,cameraPoint+new Vector2(12,-26),false);
            Check(Math.Abs(camera.Rig.GlobalPosition.Y-cameraStart.Y)<.0001f&&camera.Rig.GlobalPosition.DistanceTo(cameraStart)>.01f,"ordinary camera dragging preserves height while Shift axes remain unrestricted");Check(_undo.Count==history+1,"one camera move creates one undo item");
            _editorCamera.Position=new Vector3(3,4,5);_editorCamera.LookAt(camera.Rig.GlobalPosition);await Settle();Shift(true);Check(ObjectGizmoVisible,"Shift in Arrange reveals the complete scene transform gizmo for a camera");
            var origin=camera.Rig.GlobalPosition;float length=ObjectGizmoWorldLength(origin);var from=Project(origin+Vector3.Up*length*.95f);var to=Project(origin+Vector3.Up*(length*.95f+.25f));
            history=_undo.Count;Send(new InputEventMouseButton{ButtonIndex=MouseButton.Left,Pressed=true,Position=from,ShiftPressed=true});Shift(false);Send(new InputEventMouseMotion{Position=to,Relative=to-from});Send(new InputEventMouseButton{ButtonIndex=MouseButton.Left,Pressed=false,Position=to});
            Check(Math.Abs(camera.Rig.GlobalPosition.Y-origin.Y-.25f)<.003,"camera Y-axis translation has no ground lock and survives Shift release mid-drag");Check(_undo.Count==history+1,"the scene gizmo groups its updates into one undo action");
            Shift(true);origin=camera.Rig.GlobalPosition;length=ObjectGizmoWorldLength(origin);int ringIndex=-1;var ring=ObjectGizmoRing(origin,1,length*.82f);
            for(int i=1;i<50;i++)if(ring[i].HasValue&&HitObjectGizmo(ring[i]!.Value)==(ObjectGizmoOperation.Rotate,1)){ringIndex=i;break;}
            Check(ringIndex>=0,"a camera rotation ring is selectable independently of arrows and scale cubes");
            var basis=camera.Rig.GlobalBasis;var position=camera.Rig.GlobalPosition;Drag(ring[ringIndex]!.Value,ring[(ringIndex+4)%64]!.Value);
            Check(!basis.IsEqualApprox(camera.Rig.GlobalBasis)&&position.DistanceTo(camera.Rig.GlobalPosition)<.001f,"camera rotation works directly in the viewport without moving its origin");
            var box=AddObject("box",new Vector3(1,.2f,0));Select(box);FrameSelected();await Settle();await ToSignal(GetTree(),SceneTree.SignalName.PhysicsFrame);Shift(false);
            var boxBefore=box.Node.Position;history=_undo.Count;var boxPoint=Project(box.Node.GlobalTransform*PropFactory.CalculateBounds(box.Node).GetCenter());Drag(boxPoint,boxPoint+new Vector2(18,-15),false);
            Check(_selected==box&&box.Node.Position.DistanceTo(boxBefore)>.02f&&Math.Abs(box.Node.Position.Y-boxBefore.Y)<.0001f,"ordinary box dragging selects and moves an object while preserving its existing height");Check(_undo.Count==history+1,"ordinary object and camera drags share the same single-undo workflow");
            box.Data.Position=new float[]{1,.2f,0};SetTime(_time);Shift(true);origin=box.Node.GlobalPosition;length=ObjectGizmoWorldLength(origin);
            Drag(Project(origin+Vector3.Up*length*.95f),Project(origin+Vector3.Up*(length*.95f+.4f)));
            Check(Math.Abs(box.Node.Position.Y-.6f)<.003f,"an ordinary object can be lifted above the floor with the Y arrow");
            origin=box.Node.GlobalPosition;length=ObjectGizmoWorldLength(origin);var axis=ObjectGizmoScaleAxis(box.Node,0);var cube=Project(origin+axis*length*.58f);Check(HitObjectGizmo(cube)==(ObjectGizmoOperation.Scale,0),"the cube is a scale handle rather than another move handle");
            Drag(cube,Project(origin+axis*length*.58f*1.25f));Check(Math.Abs(box.Node.Scale.X-1.25f)<.01f&&Math.Abs(box.Node.Scale.Y-1)<.001f,"scale cubes change only their chosen axis");Shift(false);
            box.Data.Path=new ActorPathData{Enabled=true,Duration=8,Points=new(){new float[]{0,0,0},new float[]{2,.5f,0}}};SetTime(4);var curve=JsonSerializer.Serialize(box.Data.Path.Points);var beforePosition=box.Node.Position;
            BeginSceneTransformGesture();SetSelectedTransformFromGizmo("Position",beforePosition+Vector3.Up*.3f);EndSceneTransformGesture();
            Check(Math.Abs(box.Node.Position.Y-beforePosition.Y-.3f)<.001f&&JsonSerializer.Serialize(box.Data.Path.Points)==curve,"path-driven objects accept a vertical manual correction without changing the drawn curve");
            var field=(PrecisionSpinBox)_fields["Position0"];double oldValue=field.Value;history=_undo.Count;
            field.ArrowControl.EmitSignal(Control.SignalName.GuiInput,new InputEventMouseButton{ButtonIndex=MouseButton.Left,Pressed=true,Position=new Vector2(8,4)});field._Input(new InputEventMouseMotion{Relative=new Vector2(0,-20)});field._Input(new InputEventMouseMotion{Relative=new Vector2(0,-10)});field._Input(new InputEventMouseMotion{Relative=new Vector2(0,5)});field._Input(new InputEventMouseButton{ButtonIndex=MouseButton.Left,Pressed=false});
            Check(field.Value>oldValue&&field.Value-oldValue<.04,"position scrubbing is a fine adjustment instead of a jump toward 1000");Check(_undo.Count==history+1&&!_numericGesture,"one numeric scrub records only one undo action");
            StopPoseEditing();float distance=_distance;var target=_orbitTarget;var cameraBefore=_editorCamera.Transform;
            Send(new InputEventMouseButton{ButtonIndex=MouseButton.Middle,Pressed=true,Position=Vector2.One*100,AltPressed=true});Send(new InputEventMouseMotion{Position=Vector2.One*100+Vector2.Down*40,Relative=Vector2.Down*40,AltPressed=true});Send(new InputEventMouseButton{ButtonIndex=MouseButton.Middle,Pressed=false});
            Check(_distance>distance&&!_editorCamera.Transform.IsEqualApprox(cameraBefore)&&_orbitTarget.IsEqualApprox(target),"Alt plus middle-mouse dragging zooms without panning the orbit target");
            int numericChecks=await PrecisionSpinProbe.RunChecks(GetTree(),this);Check(numericChecks>=13,"standalone numeric boundary and text-entry checks pass");
            int timelineChecks=await RunTimelineTrimChecks();Check(timelineChecks>=14,"actual IN/OUT widget, source timing and undo checks pass");
            passed=true;GD.Print("INTERACTION_OK checks="+checks+" numeric="+numericChecks+" timeline="+timelineChecks);
        }
        catch(Exception ex){failure=ex.ToString();throw;}
        finally
        {
            ReleasePoseViewportDrag();ReleaseObjectGizmoDrag();EndArrangeDrag();EndNumericGesture();_middleZoom=false;_viewportShift=false;_poseShiftHeld=false;_poseEditing=false;
            string path=ProjectSettings.GlobalizePath("res://output/interaction-report.json");Directory.CreateDirectory(Path.GetDirectoryName(path)!);File.WriteAllText(path,JsonSerializer.Serialize(new{status=passed?"passed":"failed",checks,failure,observations},new JsonSerializerOptions{WriteIndented=true}));
            Restore(original,"");_projectPath=projectPath;SetTime(time);_editorCamera.Transform=view;_undo.Clear();_redo.Clear();foreach(var item in undo.Reverse())_undo.Push(item);foreach(var item in redo.Reverse())_redo.Push(item);
        }
    }
}
