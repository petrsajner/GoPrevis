using Godot;
using System;
using System.Collections.Generic;
using System.Linq;

namespace GoPrevis;

public sealed partial class NativeMotionTrack
{
    public float AuthoredPoseError(string animation,double time)
    {
        var raw=new Pose(_bones.Length,_nodes.Length);Copy(_authoredRest,raw);var source=_player.GetAnimation(animation);
        foreach(var binding in TrackingBindings(animation))
        {
            if(binding.Bone<0)continue;
            if(binding.Type==Animation.TrackType.Position3D)raw.Position[binding.Bone]=source.PositionTrackInterpolate(binding.Track,time);
            if(binding.Type==Animation.TrackType.Rotation3D)raw.Rotation[binding.Bone]=source.RotationTrackInterpolate(binding.Track,time);
            if(binding.Type==Animation.TrackType.Scale3D)raw.Scale[binding.Bone]=source.ScaleTrackInterpolate(binding.Track,time);
        }
        var expected=AuthoredGlobals(raw);float error=0;
        var expectedRoot=expected[_roots[0]].Origin;var actualRoot=BoneTransform(_bones[_roots[0]],_bones[_roots[0]].Index).Origin;
        for(int i=0;i<_bones.Length;i++)
        {
            if(BoneName(i).StartsWith("Control_",StringComparison.Ordinal))continue;
            var actual=BoneTransform(_bones[i],_bones[i].Index);error=Math.Max(error,(actual.Origin-actualRoot).DistanceTo(expected[i].Origin-expectedRoot));
            error=Math.Max(error,(actual.Basis.X-expected[i].Basis.X).Length());error=Math.Max(error,(actual.Basis.Y-expected[i].Basis.Y).Length());error=Math.Max(error,(actual.Basis.Z-expected[i].Basis.Z).Length());
        }
        return error;
    }
    public Vector3[] FootSkinPoints(string foot)
    {
        int root=FindPoseBone(foot);var rootBone=_bones[root];var descendants=new HashSet<int>{rootBone.Index};
        for(int pass=0;pass<rootBone.Skeleton.GetBoneCount();pass++)for(int i=0;i<rootBone.Skeleton.GetBoneCount();i++)if(descendants.Contains(rootBone.Skeleton.GetBoneParent(i)))descendants.Add(i);
        var points=new List<Vector3>();
        foreach(var mesh in Descendants(_modelRoot).OfType<MeshInstance3D>())
        {
            if(mesh.Mesh==null||mesh.Skin==null)continue;var skeleton=mesh.GetNodeOrNull<Skeleton3D>(mesh.Skeleton);if(skeleton!=rootBone.Skeleton)continue;
            for(int surface=0;surface<mesh.Mesh.GetSurfaceCount();surface++)
            {
                var arrays=mesh.Mesh.SurfaceGetArrays(surface);if(arrays[(int)Mesh.ArrayType.Bones].VariantType==Variant.Type.Nil)continue;
                var vertices=arrays[(int)Mesh.ArrayType.Vertex].AsVector3Array();var bones=arrays[(int)Mesh.ArrayType.Bones].AsInt32Array();var weights=arrays[(int)Mesh.ArrayType.Weights].AsFloat32Array();
                int influences=bones.Length/vertices.Length;
                for(int v=0;v<vertices.Length;v++)
                {
                    float footWeight=0;Vector3 skinned=Vector3.Zero;
                    for(int k=0;k<influences;k++)
                    {
                        int index=v*influences+k,bind=bones[index];float weight=weights[index];if(weight<=0)continue;
                        string bindName=mesh.Skin.GetBindName(bind);int bone=string.IsNullOrEmpty(bindName)?mesh.Skin.GetBindBone(bind):skeleton.FindBone(bindName);
                        if(bone<0)continue;if(descendants.Contains(bone))footWeight+=weight;
                        skinned+=(BoneTransform(rootBone,bone)*mesh.Skin.GetBindPose(bind)*vertices[v])*weight;
                    }
                    if(footWeight>.8f)points.Add(skeleton.GlobalTransform*skinned);
                }
            }
        }
        return points.ToArray();
    }
}
