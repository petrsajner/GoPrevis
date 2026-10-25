using System;
using System.Collections.Generic;

namespace GenPrevis;

public sealed class AiScenePlan
{
    public string Summary {get;set;}="";
    public double Duration {get;set;}
    public int Fps {get;set;}
    public int Width {get;set;}
    public int Height {get;set;}
    public SceneSettingsData? SceneSettings {get;set;}
    public string ActiveCameraId {get;set;}="";
    public List<string> RemoveIds {get;set;}=new();
    public List<AiObject> Objects {get;set;}=new();
    public List<AiCamera> Cameras {get;set;}=new();
}
public sealed class AiObject
{
    public string Id {get;set;}="";
    public string AssetId {get;set;}="";
    public string Name {get;set;}="";
    public float[] Position {get;set;}=Array.Empty<float>();
    public float[] Rotation {get;set;}=Array.Empty<float>();
    public float[] Scale {get;set;}=Array.Empty<float>();
    public float[] Color {get;set;}=Array.Empty<float>();
    public float[] PathOffset {get;set;}=Array.Empty<float>();
    public string PoseId {get;set;}="";
    public string PlaceOnId {get;set;}="";
    public bool ReplaceMotions {get;set;}
    public List<AiMotion> Motions {get;set;}=new();
    public bool ReplacePath {get;set;}
    public bool OrientToPath {get;set;}=true;
    public List<AiPathPoint> Path {get;set;}=new();
    public List<AiKey> Keys {get;set;}=new();
    public bool ReplaceKeys {get;set;}
    public bool ReplacePoseKeys {get;set;}
    public List<AiPoseKey> PoseKeys {get;set;}=new();
    public SceneLightData? Light {get;set;}
}
public sealed class AiPathPoint
{
    public double Time {get;set;}
    public float[] Position {get;set;}=Array.Empty<float>();
    public string Easing {get;set;}="Linear";
}
public sealed class AiKey
{
    public string Channel {get;set;}="Position";
    public double Time {get;set;}
    public float[] Value {get;set;}=Array.Empty<float>();
    public string Easing {get;set;}="Linear";
}
public sealed class AiPoseKey
{
    public string Bone {get;set;}="Head";
    public double Time {get;set;}
    public float[] Value {get;set;}=Array.Empty<float>();
    public string Easing {get;set;}="Linear";
}
public sealed class AiMotion
{
    public string MotionId {get;set;}="";
    public double Start {get;set;}
    public double Duration {get;set;}=3;
    public double Speed {get;set;}=1;
    public double SourceIn {get;set;}
    public double BlendIn {get;set;}=.3;
    public bool Loop {get;set;}=true;
    public bool InPlace {get;set;}=true;
}
public sealed class AiCamera
{
    public string Id {get;set;}="";
    public string Name {get;set;}="";
    public float[] Position {get;set;}=Array.Empty<float>();
    public float[] Rotation {get;set;}=Array.Empty<float>();
    public float[] LookAtPoint {get;set;}=Array.Empty<float>();
    public float FocalLength {get;set;}=35;
    public string TargetId {get;set;}="";
    public bool Follow {get;set;}
    public bool LookAt {get;set;}
    public bool RelativeOffsets {get;set;}=true;
    public float[] FollowOffset {get;set;}=new[]{0f,2f,6f};
    public float[] LookAtOffset {get;set;}=new[]{0f,.8f,0f};
    public double Smoothness {get;set;}
    public bool ReplaceKeys {get;set;}
    public List<AiKey> Keys {get;set;}=new();
}
