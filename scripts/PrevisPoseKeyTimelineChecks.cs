using Godot;
using System;
using System.Linq;
using System.Threading.Tasks;

namespace Previs;

public partial class PrevisApp
{
    /// <summary>Actual correction-lane widget input, source preservation and reversible key editing.</summary>
    public async Task<int> RunPoseKeyTimelineChecks()
    {
        var original=Snapshot();string path=_projectPath;double time=_time;var undo=_undo.ToArray();var redo=_redo.ToArray();int checks=0;
        void Check(bool value,string message){Require(value,"Pose key timeline: "+message);checks++;}
        async Task Settle(){for(int i=0;i<3;i++)await ToSignal(GetTree(),SceneTree.SignalName.ProcessFrame);}
        try
        {
            _playing=false;StopPoseEditing();ClearObjects();_duration=8;_scrub.MaxValue=8;SetTime(0);
            var entry=AddObject("actor",Vector3.Zero,"Pose key test");string id=entry.Data.Id;
            var motion=MotionCatalog.Find("human-walk-gesture")!;AppendMotion(entry,MotionCatalog.MaterializeSource(motion),motion.SourceScale);
            var headTurn=new Quaternion(Vector3.Up,Mathf.DegToRad(40));var armRaise=new Quaternion(Vector3.Forward,Mathf.DegToRad(20));
            entry.Data.PoseCorrections.Bones["Head"]=new(){new(){Time=0,Rotation=new float[]{0,0,0,1}},new(){Time=2,Rotation=new[]{headTurn.X,headTurn.Y,headTurn.Z,headTurn.W}}};
            entry.Data.PoseCorrections.Bones["RightUpperArm"]=new(){new(){Time=0,Rotation=new float[]{0,0,0,1}},new(){Time=2,Rotation=new[]{armRaise.X,armRaise.Y,armRaise.Z,armRaise.W}}};
            entry.Data.PoseCorrections.Root.Add(new(){Time=0,Value=new float[3]});entry.Data.PoseCorrections.Root.Add(new(){Time=2,Value=new float[]{0,.1f,0}});
            Select(entry);SetTime(0);_timeline.ExpandPoseCorrections(id);await Settle();
            Check(_timeline.PoseCorrectionRowRect(id).Size.X>10,"a grouped correction row exists beside the source-motion track");
            Check(_timeline.PoseCorrectionRowRect(id,"Head").Size.X>10&&_timeline.PoseCorrectionRowRect(id,"RightUpperArm").Size.X>10&&_timeline.PoseCorrectionRowRect(id,"Root").Size.X>10,"expanding the row exposes only keyed joints and pelvis offset");
            Check(_timeline.PoseCorrectionRowRect(id,"LeftFoot").Size==Vector2.Zero,"unkeyed joints do not add empty parameter rows");
            void Reattach(){entry=_entries.Single(e=>e.Data.Id==id);}
            void Drag(string channel,double from,double to)
            {
                var rect=_timeline.PoseCorrectionRowRect(id,channel);var point=new Vector2(rect.Position.X+rect.Size.X*(float)(from/_duration),rect.Position.Y+8);
                _timeline._GuiInput(new InputEventMouseButton{ButtonIndex=MouseButton.Left,Pressed=true,Position=point});
                _timeline._GuiInput(new InputEventMouseMotion{Position=point+Vector2.Right*rect.Size.X*(float)((to-from)/_duration)});
                _timeline._GuiInput(new InputEventMouseButton{ButtonIndex=MouseButton.Left,Pressed=false,Position=point});
            }
            int history=_undo.Count;Drag("Head",2,3);
            Check(entry.Data.MotionClips.Count==1&&entry.Data.MotionClips[0].MotionPath.EndsWith("synthetic-walk-gesture.bvh",StringComparison.Ordinal),"selecting and retiming a joint never removes its mocap source");
            Check(PoseCorrectionTimes(entry,"Head").Any(value=>Math.Abs(value-3)<.00001)&&!PoseCorrectionTimes(entry,"Head").Any(value=>Math.Abs(value-2)<.00001),"a child diamond retimes its own joint");
            Check(PoseCorrectionTimes(entry,"RightUpperArm").Any(value=>Math.Abs(value-2)<.00001)&&PoseCorrectionTimes(entry,"Root").Any(value=>Math.Abs(value-2)<.00001),"retiming the head leaves the arm and pelvis keys untouched");
            Check(_undo.Count==history+1,"one timeline key drag adds exactly one Undo action");
            SetTime(3);var actor=(RiggedActor)entry.Node;var skeleton=actor.GetNode<Skeleton3D>("PreparedHumanoid");int head=skeleton.FindBone("Head");
            var source=new RiggedActor();_world.AddChild(source);source.SetMotionClips(entry.Data.MotionClips);source.EvaluateTimeline(3);
            var baseRotation=source.GetNode<Skeleton3D>("PreparedHumanoid").GetBonePoseRotation(head);var expected=(baseRotation*headTurn).Normalized();
            Check(Math.Abs(expected.Dot(skeleton.GetBonePoseRotation(head)))>.9999f,"the retimed key is applied additively above the actual source pose");source.QueueFree();
            Undo();Reattach();Check(PoseCorrectionTimes(entry,"Head").Any(value=>Math.Abs(value-2)<.00001),"Undo restores the previous correction-key time");
            Redo();Reattach();Check(PoseCorrectionTimes(entry,"Head").Any(value=>Math.Abs(value-3)<.00001),"Redo restores the correction-key drag");
            Drag("All",2,4);
            Check(PoseCorrectionTimes(entry,"RightUpperArm").Any(value=>Math.Abs(value-4)<.00001)&&PoseCorrectionTimes(entry,"Root").Any(value=>Math.Abs(value-4)<.00001)&&PoseCorrectionTimes(entry,"Head").Any(value=>Math.Abs(value-3)<.00001),"a group diamond retimes only the channels sharing that time");
            var row=_timeline.PoseCorrectionRowRect(id,"Head");var keyPoint=new Vector2(row.Position.X+row.Size.X*3/(float)_duration,row.Position.Y+8);history=_undo.Count;
            _timeline._GuiInput(new InputEventMouseButton{ButtonIndex=MouseButton.Right,Pressed=true,Position=keyPoint});
            Check(!PoseCorrectionTimes(entry,"Head").Any(value=>Math.Abs(value-3)<.00001)&&_undo.Count==history+1,"right-click removes the chosen joint key with one Undo action");
            Check(entry.Data.MotionClips.Count==1&&PoseCorrectionTimes(entry,"RightUpperArm").Any(value=>Math.Abs(value-4)<.00001),"removing a correction preserves mocap and unrelated joints");
            GD.Print("POSE_KEY_TIMELINE_OK checks="+checks);return checks;
        }
        finally
        {
            StopPoseEditing();Restore(original,"");_projectPath=path;SetTime(time);_undo.Clear();_redo.Clear();foreach(var item in undo.Reverse())_undo.Push(item);foreach(var item in redo.Reverse())_redo.Push(item);
        }
    }
}
