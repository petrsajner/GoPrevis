using Godot;
using System;

namespace GenPrevis;

public partial class RiggedActor
{
    /// <summary>Samples camera locomotion metadata without applying a pose or changing scene time.</summary>
    public (Vector3 Root,Vector3 Facing) SampleTrackingMotion(double time,bool forceInPlace,PoseCorrectionAnimationData? corrections)
    {
        EnsureRig();time=double.IsFinite(time)?Math.Max(0,time):0;
        Vector3 root;Quaternion[] rotations=new Quaternion[_bones.Count];
        if(_motionTrack.Length==0)
        {
            root=_staticPose==null?Vector3.Zero:PoseCorrectionRuntime.RootValue(_staticPose);
            for(int i=0;i<rotations.Length;i++)rotations[i]=_staticPose==null?Quaternion.Identity:PoseCorrectionRuntime.ReadBone(_staticPose,_bones[i].Name);
        }
        else
        {
            var incoming=new Quaternion[_bones.Count];SampleTrackPose(_motionTrack[0],time,rotations);
            for(int i=1;i<_motionTrack.Length;i++)
            {
                var clip=_motionTrack[i];if(time<clip.Start)break;SampleTrackPose(clip,time,incoming);float weight=ClipWeight(clip,time);
                for(int bone=0;bone<rotations.Length;bone++)rotations[bone]=rotations[bone].Slerp(incoming[bone],weight).Normalized();
            }
            root=EvaluateTrackRoot(_motionTrack,time);if(forceInPlace){root.X=0;root.Z=0;}
            // Match the same neutral-foot ground correction used by ApplyTrackPose.
            var positions=new Vector3[_bones.Count];var globals=new Quaternion[_bones.Count];
            for(int i=0;i<_bones.Count;i++)
            {
                int parent=_bones[i].Parent;var parentRotation=parent<0?Quaternion.Identity:globals[parent];globals[i]=(parentRotation*rotations[i]).Normalized();
                positions[i]=parent<0?_bones[i].Offset+root:positions[parent]+parentRotation*_bones[i].Offset;
            }
            float minimum=float.PositiveInfinity;
            foreach(int foot in new[]{FindBone("LeftFoot"),FindBone("RightFoot")})
                foreach(float x in new[]{-.064f,.064f})foreach(float y in new[]{-.070f,.020f})foreach(float z in new[]{-.185f,.065f})
                    minimum=Math.Min(minimum,(positions[foot]+globals[foot]*new Vector3(x,y,z)).Y);
            root.Y+=Math.Max(0,.005f-minimum);
        }
        Vector3 forward=_motionTrack.Length==0&&_staticPose!=null?new Vector3(_staticPose.Forward[0],_staticPose.Forward[1],_staticPose.Forward[2]):Vector3.Forward;
        var facing=rotations[0]*forward;facing.Y=0;
        if(facing.LengthSquared()<.00001f){var right=rotations[0]*Vector3.Right;right.Y=0;facing=Vector3.Up.Cross(right);}
        root+=PoseCorrectionRuntime.EvaluateRoot(corrections,time);
        return(root,HorizontalFacing(facing));
    }
}
