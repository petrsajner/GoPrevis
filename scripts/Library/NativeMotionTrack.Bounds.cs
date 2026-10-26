using Godot;
using System;
using System.Linq;

namespace GoPrevis;

public sealed partial class NativeMotionTrack
{
    Aabb? _skinBounds;
    public Aabb SkinnedBounds(Node3D relativeTo)
    {
        if(_skinBounds==null)
        {
            var root=(Node3D)_modelRoot;var inverse=root.GlobalTransform.AffineInverse();bool found=false;Vector3 min=Vector3.Zero,max=Vector3.Zero;
            void Include(Vector3 world){var point=inverse*world;if(!found){min=max=point;found=true;}else{min=min.Min(point);max=max.Max(point);}}
            foreach(var mesh in Descendants(_modelRoot).OfType<MeshInstance3D>())
            {
                if(mesh.Mesh==null)continue;var skeleton=mesh.GetNodeOrNull<Skeleton3D>(mesh.Skeleton);var skin=mesh.Skin;Transform3D[]? binds=null;
                if(skeleton!=null&&skin!=null)
                {
                    binds=new Transform3D[skin.GetBindCount()];var reference=_bones.First(b=>b.Skeleton==skeleton);
                    for(int bind=0;bind<binds.Length;bind++){string name=skin.GetBindName(bind);int bone=name==""?skin.GetBindBone(bind):skeleton.FindBone(name);if(bone>=0)binds[bind]=skeleton.GlobalTransform*BoneTransform(reference,bone)*skin.GetBindPose(bind);}
                }
                for(int surface=0;surface<mesh.Mesh.GetSurfaceCount();surface++)
                {
                    var arrays=mesh.Mesh.SurfaceGetArrays(surface);var vertices=arrays[(int)Mesh.ArrayType.Vertex].AsVector3Array();bool weighted=binds!=null&&arrays[(int)Mesh.ArrayType.Bones].VariantType!=Variant.Type.Nil;
                    if(!weighted){foreach(var point in vertices)Include(mesh.GlobalTransform*point);continue;}
                    var bones=arrays[(int)Mesh.ArrayType.Bones].AsInt32Array();var weights=arrays[(int)Mesh.ArrayType.Weights].AsFloat32Array();int influences=bones.Length/vertices.Length;
                    for(int v=0;v<vertices.Length;v++){var point=Vector3.Zero;float total=0;for(int k=0;k<influences;k++){int index=v*influences+k;float weight=weights[index];if(weight<=0)continue;point+=(binds![bones[index]]*vertices[v])*weight;total+=weight;}Include(total>0?point/total:mesh.GlobalTransform*vertices[v]);}
                }
            }
            _skinBounds=found?new Aabb(min,max-min):new Aabb(Vector3.Zero,Vector3.One);
        }
        return relativeTo.GlobalTransform.AffineInverse()*((Node3D)_modelRoot).GlobalTransform*_skinBounds.Value;
    }
}
