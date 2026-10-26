using Godot;
using System;
using System.Linq;

namespace GoPrevis;

public partial class GoPrevisApp
{
    void UpdateObjectLinks()
    {
        foreach(var entry in _entries.Where(e=>e.Data.Link!=null))
        {
            var link=entry.Data.Link!;var from=_entries.FirstOrDefault(e=>e.Data.Id==link.StartTargetId);var to=_entries.FirstOrDefault(e=>e.Data.Id==link.EndTargetId);if(from==null||to==null)continue;
            Vector3 End(Entry actor,string bone,float[] offset)
            {
                if(actor.Node is RiggedActor human&&bone!=""&&human.PoseBoneNames.Contains(bone))return human.GlobalTransform*(human.GetPoseJointPosition(bone)+human.GetPoseJointGlobalRotation(bone)*Vector(offset));
                if(actor.NativeTrack!=null&&bone!=""&&actor.NativeTrack.PoseBoneNames.Contains(bone))return actor.NativeTrack.GetPoseJointWorld(bone)+actor.NativeTrack.GetPoseJointWorldBasis(bone)*Vector(offset);
                return actor.Node.GlobalTransform*Vector(offset);
            }
            var a=End(from,link.StartBone,link.StartOffset);var b=End(to,link.EndBone,link.EndOffset);var delta=b-a;if(delta.LengthSquared()<.000001f)continue;
            var y=delta.Normalized();var x=(Math.Abs(y.Dot(Vector3.Forward))>.95f?Vector3.Right:Vector3.Forward).Cross(y).Normalized();var z=x.Cross(y).Normalized();
            entry.Node.GlobalTransform=new Transform3D(new Basis(x*link.Width/.9f,y*delta.Length(),z*link.Width/.9f),a);
        }
    }
}
