using Godot;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json.Serialization;

namespace GenPrevis;

public sealed record PoseActorSample(string Id,string Name,RiggedActor Actor);
public sealed record NativePoseActorSample(string Id,string Name,Node3D ModelRoot);
public sealed record PosePassResult(Image Image,PosePassFrame Annotation);

public sealed class PosePassFrame
{
    public string Format { get; init; } = "OpenPose COCO-18 body";
    public double TimeSeconds { get; init; }
    public int Width { get; init; }
    public int Height { get; init; }
    public string CoordinateSystem { get; init; } = "pixels: top-left origin, x right, y down; world: metres, Y up";
    public string[] JointNames { get; init; } = ControlPassRenderer.Coco18Names;
    public string FaceLandmarks { get; init; } = "Nose, eyes and ears are geometric proxies derived from the prepared head; no facial or finger capture is present.";
    public string OcclusionMethod { get; init; } = "linear camera-Z depth with body-surface and depth-quantization tolerance";
    public float OcclusionToleranceMetres { get; init; }
    public string AnimalPolicy { get; init; } = "The image contains prepared humanoid bodies only. Animal rigs are not relabelled as human COCO joints.";
    [JsonPropertyName("people")]
    public List<PosePassPerson> People { get; init; } = new();
    [JsonPropertyName("native_rigs")]
    public List<PosePassRig> NativeRigs { get; init; } = new();
}

public sealed class PosePassRig
{
    public string Id { get; init; } = "";
    public string Name { get; init; } = "";
    public string Skeleton { get; init; } = "";
    public string Topology { get; init; } = "native-rig";
    public string[] JointNames { get; init; } = Array.Empty<string>();
    public int[] Parents { get; init; } = Array.Empty<int>();
    public List<PosePassJoint> Joints { get; init; } = new();
}

public sealed class PosePassPerson
{
    public string Id { get; init; } = "";
    public string Name { get; init; } = "";
    [JsonPropertyName("pose_keypoints_2d")]
    public float[] PoseKeypoints2D { get; init; } = Array.Empty<float>();
    public List<PosePassJoint> Joints { get; init; } = new();
}

public sealed class PosePassJoint
{
    public string Name { get; init; } = "";
    public float[] World { get; init; } = new float[3];
    public float[] Pixel { get; init; } = new float[2];
    public float[] Normalized { get; init; } = new float[2];
    public float CameraDepthMetres { get; init; }
    public bool InsideFrame { get; init; }
    public bool Visible { get; init; }
    public bool Derived { get; init; }
    public float Confidence { get; init; }
}

/// <summary>
/// Depth uses the actual posed meshes in the capture world. The material transaction
/// and capture-camera environment are always restored before beauty rendering resumes.
/// </summary>
public sealed class ControlPassRenderer : IDisposable
{
    public static readonly string[] Coco18Names={"nose","neck","right_shoulder","right_elbow","right_wrist","left_shoulder","left_elbow","left_wrist","right_hip","right_knee","right_ankle","left_hip","left_knee","left_ankle","right_eye","left_eye","right_ear","left_ear"};
    static readonly (int A,int B)[] Limbs={(1,2),(1,5),(2,3),(3,4),(5,6),(6,7),(1,8),(8,9),(9,10),(1,11),(11,12),(12,13),(1,0),(0,14),(14,16),(0,15),(15,17)};
    static readonly (byte R,byte G,byte B)[] Palette={(255,0,0),(255,85,0),(255,170,0),(255,255,0),(170,255,0),(85,255,0),(0,255,0),(0,255,85),(0,255,170),(0,255,255),(0,170,255),(0,85,255),(0,0,255),(85,0,255),(170,0,255),(255,0,255),(255,0,170),(255,0,85)};
    readonly Node _scene;
    readonly Camera3D _camera;
    readonly float _near,_far;
    readonly ShaderMaterial _depthMaterial;
    readonly Shader _depthShader;
    readonly Godot.Environment _depthEnvironment;
    readonly List<MaterialState> _materials=new();
    Godot.Environment? _originalEnvironment;
    bool _depthActive,_disposed;
    sealed record MaterialState(MeshInstance3D Mesh,Material? Override,Material? Overlay);
    readonly record struct ProjectedJoint(Vector3 World,Vector3 View,Vector2 Pixel,bool InDepth,bool Inside,bool Visible,bool Derived,float Confidence);

    public float DepthNear => _near;
    public float DepthFar => _far;
    public bool DepthPassActive => _depthActive;
    public string DepthEncoding => "RGB8 equal channels; white=near, black=far/background; grayscale=(far-camera_z)/(far-near), clipped to [0,1]";

    public ControlPassRenderer(Node sceneRoot,Camera3D captureCamera,float depthNear=.05f,float depthFar=30)
    {
        if(!float.IsFinite(depthNear)||!float.IsFinite(depthFar)||depthNear<0||depthFar<=depthNear)throw new ArgumentException("Invalid depth normalization range.");
        _scene=sceneRoot??throw new ArgumentNullException(nameof(sceneRoot));_camera=captureCamera??throw new ArgumentNullException(nameof(captureCamera));_near=depthNear;_far=depthFar;
        _depthShader=new Shader{Code=DepthShader};_depthMaterial=new ShaderMaterial{Shader=_depthShader};_depthMaterial.SetShaderParameter("depth_near",_near);_depthMaterial.SetShaderParameter("depth_far",_far);
        _depthEnvironment=new Godot.Environment{BackgroundMode=Godot.Environment.BGMode.Color,BackgroundColor=Colors.Black,BackgroundEnergyMultiplier=0,AmbientLightSource=Godot.Environment.AmbientSource.Color,AmbientLightColor=Colors.Black,AmbientLightEnergy=0,ReflectedLightSource=Godot.Environment.ReflectionSource.Disabled,TonemapMode=Godot.Environment.ToneMapper.Linear,TonemapExposure=1,GlowEnabled=false,AdjustmentEnabled=false,FogEnabled=false};
    }

    public void BeginDepthPass()
    {
        ObjectDisposedException.ThrowIf(_disposed,this);if(_depthActive)return;
        _originalEnvironment=_camera.Environment;_materials.Clear();_depthActive=true;
        try
        {
            _camera.Environment=_depthEnvironment;
            foreach(var mesh in Descendants(_scene).OfType<MeshInstance3D>().Where(mesh=>mesh.Mesh!=null&&(mesh.Layers&1u)!=0&&mesh.IsVisibleInTree()))
            {
                _materials.Add(new MaterialState(mesh,mesh.MaterialOverride,mesh.MaterialOverlay));mesh.MaterialOverlay=null;mesh.MaterialOverride=_depthMaterial;
            }
        }
        catch{EndDepthPass();throw;}
    }
    public void EndDepthPass()
    {
        if(!_depthActive)return;
        foreach(var state in _materials)if(GodotObject.IsInstanceValid(state.Mesh)){state.Mesh.MaterialOverride=state.Override;state.Mesh.MaterialOverlay=state.Overlay;}
        if(GodotObject.IsInstanceValid(_camera))_camera.Environment=_originalEnvironment;
        _materials.Clear();_originalEnvironment=null;_depthActive=false;
    }
    public void Dispose()
    {
        if(_disposed)return;EndDepthPass();_disposed=true;_depthMaterial.Dispose();_depthShader.Dispose();_depthEnvironment.Dispose();
    }

    /// <summary>Decodes the linear camera-axis depth represented by an interior surface pixel.</summary>
    public float DecodeDepth(Color pixel)=>_far-Math.Clamp(pixel.R,0,1)*(_far-_near);

    public PosePassResult RenderPose(IEnumerable<PoseActorSample> actors,Image? depthImage,double timeSeconds,IEnumerable<NativePoseActorSample>? nativeRigs=null,float occlusionTolerance=.18f)
    {
        ObjectDisposedException.ThrowIf(_disposed,this);
        if(!double.IsFinite(timeSeconds))throw new ArgumentException("Pose time must be finite.");
        int width=(int)_camera.GetViewport().GetVisibleRect().Size.X;
        int height=(int)_camera.GetViewport().GetVisibleRect().Size.Y;
        if(width<1||height<1)throw new InvalidOperationException("The capture camera must belong to a sized viewport.");
        if(depthImage!=null&&(depthImage.GetWidth()!=width||depthImage.GetHeight()!=height))throw new ArgumentException("Pose and depth frames must have identical dimensions.");
        byte[]? depth=null;if(depthImage!=null){using var copy=(Image)depthImage.Duplicate();copy.Convert(Image.Format.Rgb8);depth=copy.GetData();}
        var nativeSamples=nativeRigs?.ToArray()??Array.Empty<NativePoseActorSample>();bool hasNative=nativeSamples.Length>0;
        var pixels=new byte[checked(width*height*3)];var annotation=new PosePassFrame{TimeSeconds=timeSeconds,Width=width,Height=height,OcclusionToleranceMetres=occlusionTolerance,Format=!hasNative?"OpenPose COCO-18 body":"COCO-18 human body + native-rig topology",AnimalPolicy=!hasNative?"The image contains prepared humanoid bodies only. Animal rigs are not relabelled as human COCO joints.":"Native rigs retain their own bone names and parent indices. This hybrid map is not a standard human OpenPose topology."};
        var inverse=_camera.GetCameraTransform().AffineInverse();float quantization=(_far-_near)/255f*.55f;
        foreach(var sample in actors)
        {
            if(sample.Actor==null||!GodotObject.IsInstanceValid(sample.Actor)||!sample.Actor.IsVisibleInTree())continue;
            var actor=sample.Actor;var world=HumanJoints(actor);var points=new ProjectedJoint[18];
            float bodyScale=Math.Max(actor.GlobalBasis.Scale.X,Math.Max(actor.GlobalBasis.Scale.Y,actor.GlobalBasis.Scale.Z));float tolerance=Math.Max(0,occlusionTolerance)*Math.Max(.05f,bodyScale)+quantization;
            var person=new PosePassPerson{Id=sample.Id,Name=sample.Name,PoseKeypoints2D=new float[54]};
            for(int index=0;index<18;index++)
            {
                var position=world[index];var view=inverse*position;float z=-view.Z;bool inDepth=z>=_camera.Near&&z<=_camera.Far;
                var projected=inDepth?_camera.UnprojectPosition(position):Vector2.Zero;bool inside=inDepth&&projected.X>=0&&projected.Y>=0&&projected.X<width&&projected.Y<height;
                bool visible=inside&&DepthVisible(projected,z,depth,width,height,tolerance);bool derived=index is 0 or 14 or 15 or 16 or 17;float confidence=visible?(derived?.5f:1):0;
                points[index]=new ProjectedJoint(position,view,projected,inDepth,inside,visible,derived,confidence);
                person.PoseKeypoints2D[index*3]=visible?projected.X:0;person.PoseKeypoints2D[index*3+1]=visible?projected.Y:0;person.PoseKeypoints2D[index*3+2]=confidence;
                person.Joints.Add(new PosePassJoint{Name=Coco18Names[index],World=new[]{position.X,position.Y,position.Z},Pixel=new[]{projected.X,projected.Y},Normalized=new[]{projected.X/width,projected.Y/height},CameraDepthMetres=z,InsideFrame=inside,Visible=visible,Derived=derived,Confidence=confidence});
            }
            annotation.People.Add(person);
            float lineRadius=Math.Max(1,height/360f),jointRadius=Math.Max(2,height/240f);
            for(int limb=0;limb<Limbs.Length;limb++)
            {
                var pair=Limbs[limb];var a=points[pair.A];var b=points[pair.B];
                if(!ClipCameraSegment(ref a,ref b,inverse))continue;
                var start=a.Pixel;var finish=b.Pixel;float t0=0,t1=1;if(!ClipScreen(start,finish,width,height,ref t0,ref t1))continue;
                var clippedStart=start.Lerp(finish,t0);var clippedFinish=start.Lerp(finish,t1);int steps=Math.Max(1,(int)Math.Ceiling(clippedStart.DistanceTo(clippedFinish)));
                for(int step=0;step<=steps;step++)
                {
                    float t=Mathf.Lerp(t0,t1,step/(float)steps);var pixel=start.Lerp(finish,t);float z=PerspectiveDepth(-a.View.Z,-b.View.Z,t);
                    if(DepthVisible(pixel,z,depth,width,height,tolerance))PaintDisk(pixels,width,height,pixel,lineRadius,Palette[limb%Palette.Length]);
                }
            }
            for(int index=0;index<points.Length;index++)if(points[index].Visible)PaintDisk(pixels,width,height,points[index].Pixel,jointRadius,Palette[index]);
        }
        foreach(var sample in nativeSamples)
        {
            if(sample.ModelRoot==null||!GodotObject.IsInstanceValid(sample.ModelRoot)||!sample.ModelRoot.IsVisibleInTree())continue;
            foreach(var skeleton in Descendants(sample.ModelRoot).OfType<Skeleton3D>().Where(s=>s.IsVisibleInTree()))
            {
                int count=skeleton.GetBoneCount();if(count==0)continue;
                var names=Enumerable.Range(0,count).Select(skeleton.GetBoneName).Select(n=>n.ToString()).ToArray();var parents=Enumerable.Range(0,count).Select(skeleton.GetBoneParent).ToArray();
                var rig=new PosePassRig{Id=sample.Id,Name=sample.Name,Skeleton=skeleton.Name.ToString(),JointNames=names,Parents=parents};var points=new ProjectedJoint[count];
                // Native packs have arbitrary source units. Use their placed world
                // height, not raw armature scale, for the body-surface tolerance.
                float tolerance=Math.Max(Math.Max(0,occlusionTolerance),WorldMeshHeight(sample.ModelRoot)*.12f)+quantization;
                for(int index=0;index<count;index++)
                {
                    var world=skeleton.GlobalTransform*skeleton.GetBoneGlobalPose(index).Origin;var view=inverse*world;float z=-view.Z;bool inDepth=z>=_camera.Near&&z<=_camera.Far;var pixel=inDepth?_camera.UnprojectPosition(world):Vector2.Zero;
                    bool inside=inDepth&&pixel.X>=0&&pixel.Y>=0&&pixel.X<width&&pixel.Y<height;bool visible=inside&&DepthVisible(pixel,z,depth,width,height,tolerance);
                    points[index]=new ProjectedJoint(world,view,pixel,inDepth,inside,visible,false,visible?1:0);
                    rig.Joints.Add(new PosePassJoint{Name=names[index],World=new[]{world.X,world.Y,world.Z},Pixel=new[]{pixel.X,pixel.Y},Normalized=new[]{pixel.X/width,pixel.Y/height},CameraDepthMetres=z,InsideFrame=inside,Visible=visible,Derived=false,Confidence=visible?1:0});
                }
                annotation.NativeRigs.Add(rig);
                float radius=Math.Max(1,height/400f);
                for(int index=0;index<count;index++)
                {
                    int parent=parents[index];if(parent<0)continue;var a=points[parent];var b=points[index];if(!ClipCameraSegment(ref a,ref b,inverse))continue;
                    float t0=0,t1=1;if(!ClipScreen(a.Pixel,b.Pixel,width,height,ref t0,ref t1))continue;int steps=Math.Max(1,(int)Math.Ceiling(a.Pixel.Lerp(b.Pixel,t0).DistanceTo(a.Pixel.Lerp(b.Pixel,t1))));
                    for(int step=0;step<=steps;step++)
                    {
                        float t=Mathf.Lerp(t0,t1,step/(float)steps);var pixel=a.Pixel.Lerp(b.Pixel,t);if(DepthVisible(pixel,PerspectiveDepth(-a.View.Z,-b.View.Z,t),depth,width,height,tolerance))PaintDisk(pixels,width,height,pixel,radius,Palette[index%Palette.Length]);
                    }
                }
                for(int index=0;index<count;index++)if(points[index].Visible)PaintDisk(pixels,width,height,points[index].Pixel,radius*1.5f,Palette[index%Palette.Length]);
            }
        }
        return new PosePassResult(Image.CreateFromData(width,height,false,Image.Format.Rgb8,pixels),annotation);
    }

    Vector3[] HumanJoints(RiggedActor actor)
    {
        Vector3 Bone(string name)=>actor.GlobalTransform*actor.GetPoseJointPosition(name);
        var result=new Vector3[18];result[2]=Bone("RightUpperArm");result[3]=Bone("RightLowerArm");result[4]=Bone("RightHand");result[5]=Bone("LeftUpperArm");result[6]=Bone("LeftLowerArm");result[7]=Bone("LeftHand");result[1]=(result[2]+result[5])*.5f;
        result[8]=Bone("RightUpperLeg");result[9]=Bone("RightLowerLeg");result[10]=Bone("RightFoot");result[11]=Bone("LeftUpperLeg");result[12]=Bone("LeftLowerLeg");result[13]=Bone("LeftFoot");
        var pose=actor.CaptureEvaluatedPose();Vector3 forward=new(pose.Forward[0],0,pose.Forward[2]);forward=forward.Normalized();
        var head=actor.GlobalTransform*new Transform3D(new Basis(actor.GetPoseJointGlobalRotation("Head")),actor.GetPoseJointPosition("Head"));
        result[0]=head*(Vector3.Up*.10f+forward*.115f);result[14]=head*(Vector3.Up*.125f+forward*.095f+Vector3.Left*.045f);result[15]=head*(Vector3.Up*.125f+forward*.095f+Vector3.Right*.045f);result[16]=head*(Vector3.Up*.095f+Vector3.Left*.105f);result[17]=head*(Vector3.Up*.095f+Vector3.Right*.105f);return result;
    }
    bool DepthVisible(Vector2 point,float cameraDepth,byte[]? depth,int width,int height,float tolerance)
    {
        int x=(int)MathF.Round(point.X),y=(int)MathF.Round(point.Y);if(x<0||y<0||x>=width||y>=height)return false;
        if(depth==null)return true;byte value=depth[(y*width+x)*3];if(value<=1)return true; // Background or range-clamped far geometry: depth is unknown.
        float surface=_far-value/255f*(_far-_near);return cameraDepth<=surface+tolerance;
    }
    float PerspectiveDepth(float a,float b,float t)=>_camera.Projection==Camera3D.ProjectionType.Orthogonal?Mathf.Lerp(a,b,t):1/Math.Max(.000001f,(1-t)/a+t/b);
    bool ClipCameraSegment(ref ProjectedJoint a,ref ProjectedJoint b,Transform3D inverse)
    {
        float za=-a.View.Z,zb=-b.View.Z,t0=0,t1=1;
        if(!ClipInterval(za,zb,_camera.Near,_camera.Far,ref t0,ref t1))return false;
        var worldA=a.World.Lerp(b.World,t0);var worldB=a.World.Lerp(b.World,t1);a=a with{World=worldA,View=inverse*worldA,Pixel=_camera.UnprojectPosition(worldA)};b=b with{World=worldB,View=inverse*worldB,Pixel=_camera.UnprojectPosition(worldB)};return true;
    }
    static bool ClipInterval(float from,float to,float minimum,float maximum,ref float t0,ref float t1)
    {
        float delta=to-from;if(Math.Abs(delta)<.000001f)return from>=minimum&&from<=maximum;
        float a=(minimum-from)/delta,b=(maximum-from)/delta;if(a>b)(a,b)=(b,a);t0=Math.Max(t0,a);t1=Math.Min(t1,b);return t0<=t1;
    }
    static bool ClipScreen(Vector2 from,Vector2 to,int width,int height,ref float t0,ref float t1)
        =>ClipInterval(from.X,to.X,0,width-1,ref t0,ref t1)&&ClipInterval(from.Y,to.Y,0,height-1,ref t0,ref t1);
    static void PaintDisk(byte[] pixels,int width,int height,Vector2 center,float radius,(byte R,byte G,byte B) color)
    {
        int minX=Math.Max(0,(int)Math.Floor(center.X-radius)),maxX=Math.Min(width-1,(int)Math.Ceiling(center.X+radius)),minY=Math.Max(0,(int)Math.Floor(center.Y-radius)),maxY=Math.Min(height-1,(int)Math.Ceiling(center.Y+radius));float square=radius*radius;
        for(int y=minY;y<=maxY;y++)for(int x=minX;x<=maxX;x++)if((x-center.X)*(x-center.X)+(y-center.Y)*(y-center.Y)<=square){int offset=(y*width+x)*3;pixels[offset]=color.R;pixels[offset+1]=color.G;pixels[offset+2]=color.B;}
    }
    static float WorldMeshHeight(Node root)
    {
        float minimum=float.PositiveInfinity,maximum=float.NegativeInfinity;
        foreach(var mesh in Descendants(root).OfType<MeshInstance3D>().Where(m=>m.Mesh!=null))for(int i=0;i<8;i++){float y=(mesh.GlobalTransform*mesh.GetAabb().GetEndpoint(i)).Y;minimum=Math.Min(minimum,y);maximum=Math.Max(maximum,y);}
        return float.IsFinite(minimum)&&float.IsFinite(maximum)?maximum-minimum:1;
    }
    static IEnumerable<Node> Descendants(Node node){yield return node;foreach(Node child in node.GetChildren())foreach(var nested in Descendants(child))yield return nested;}

    // VERTEX is the final view-space fragment position, including skeletal skinning.
    // Compatibility produces sRGB output directly; Forward+/Mobile need an inverse
    // display transfer so the saved byte represents the requested linear gray value.
    const string DepthShader="""
        shader_type spatial;
        render_mode unshaded, fog_disabled, shadows_disabled, ambient_light_disabled, cull_disabled;
        uniform float depth_near = 0.05;
        uniform float depth_far = 30.0;
        vec3 gray_to_linear(vec3 c) {
            return mix(c / 12.92, pow((c + vec3(0.055)) / 1.055, vec3(2.4)), step(vec3(0.04045), c));
        }
        void fragment() {
            float gray = clamp((depth_far + VERTEX.z) / max(0.00001, depth_far - depth_near), 0.0, 1.0);
            ALBEDO = OUTPUT_IS_SRGB ? vec3(gray) : gray_to_linear(vec3(gray));
        }
        """;
}
