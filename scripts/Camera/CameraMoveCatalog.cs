using Godot;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace Previs;

public sealed record CameraMovePreset(string Id,string Name,string Category,string Description);

/// <summary>Deterministic camera moves relative to the camera's current composition.</summary>
public static class CameraMoveCatalog
{
    public static IReadOnlyList<CameraMovePreset> All { get; }=new CameraMovePreset[]
    {
        new("static","Static shot","Basic","Hold the current camera position, angle and lens."),
        new("tilt-up","Tilt up","Basic","Turn the camera upward without moving it."),
        new("tilt-down","Tilt down","Basic","Turn the camera downward without moving it."),
        new("pan-left","Pan left","Basic","Turn left from the current framing."),
        new("pan-right","Pan right","Basic","Turn right from the current framing."),
        new("crane-up","Crane up","Basic","Raise the camera while keeping its subject composition."),
        new("crane-down","Crane down","Basic","Lower the camera while keeping its subject composition."),
        new("truck-left","Truck left","Basic","Move left on the camera's local side axis."),
        new("truck-right","Truck right","Basic","Move right on the camera's local side axis."),
        new("dolly-in","Dolly in","Basic","Move forward along the viewing direction."),
        new("dolly-out","Dolly out","Basic","Move backward along the viewing direction."),
        new("zoom-in","Zoom in","Basic","Increase focal length without moving the camera."),
        new("zoom-out","Zoom out","Basic","Decrease focal length without moving the camera."),
        new("handheld","Handheld","Basic","Add repeatable, gentle positional and angular motion."),
        new("slider-reveal","Slider reveal","Basic","Slide sideways while retaining the subject composition."),
        new("push-through","Push through","Basic","Move through the subject distance along the viewing axis."),
        new("whip-pan","Whip pan","Basic","Make a fast lateral turn in the middle of the shot."),
        new("crash-zoom","Crash zoom","Basic","Make a fast focal-length change in the middle of the shot."),
        new("orbit","Orbit","Aerial","Move through a quarter-circle around the subject."),
        new("ascending-orbit","Ascending orbit","Aerial","Circle the subject while gaining height."),
        new("descending-orbit","Descending orbit","Aerial","Circle the subject while losing height."),
        new("aerial-establishing","Aerial establishing","Aerial","Rise and pull back to reveal a wider view."),
        new("dive","Dive","Aerial","Move forward and downward toward the subject."),
        new("pull-away","Pull away","Aerial","Pull backward and slightly upward from the subject.")
    };

    public static CameraMovePreset? Find(string id)=>All.FirstOrDefault(p=>p.Id.Equals(id,StringComparison.OrdinalIgnoreCase)||p.Name.Equals(id,StringComparison.OrdinalIgnoreCase));

    public static void Apply(StudioCameraRig rig,string id,double durationSeconds,Vector3 target)
    {
        ArgumentNullException.ThrowIfNull(rig);
        var preset=Find(id)??throw new InvalidDataException("Unknown camera movement preset: "+id);
        double duration=double.IsFinite(durationSeconds)?Math.Clamp(durationSeconds,.1,600):8;
        Vector3 start=rig.GlobalPosition;Quaternion rotation=rig.GlobalBasis.Orthonormalized().GetRotationQuaternion().Normalized();float lens=rig.FocalLengthMm;
        if(!start.IsFinite())throw new InvalidDataException("The camera position must be finite before applying a preset.");
        var basis=new Basis(rotation);Vector3 forward=-basis.Z,right=basis.X;
        if(!target.IsFinite()||target.DistanceSquaredTo(start)<.01f)target=start+forward*6;
        float distance=Math.Clamp(start.DistanceTo(target),.5f,30);
        Quaternion aimOffset=(LookAt(start,target).Inverse()*rotation).Normalized();
        int steps=preset.Id=="static"?1:preset.Id=="handheld"?Math.Clamp((int)Math.Ceiling(duration*8),24,4800):preset.Id is "whip-pan" or "crash-zoom"?40:preset.Category=="Aerial"?32:16;
        var keys=new List<ShotCameraKey>(steps+1);
        for(int i=0;i<=steps;i++)
        {
            float u=i/(float)steps;double seconds=duration*u;Vector3 position=start;Quaternion angle=rotation;float focal=lens;
            switch(preset.Id)
            {
                case "tilt-up": angle=(rotation*new Quaternion(Vector3.Right,Mathf.DegToRad(25)*u)).Normalized();break;
                case "tilt-down": angle=(rotation*new Quaternion(Vector3.Right,Mathf.DegToRad(-25)*u)).Normalized();break;
                case "pan-left": angle=(rotation*new Quaternion(Vector3.Up,Mathf.DegToRad(35)*u)).Normalized();break;
                case "pan-right": angle=(rotation*new Quaternion(Vector3.Up,Mathf.DegToRad(-35)*u)).Normalized();break;
                case "crane-up": position+=Vector3.Up*distance*.35f*u;angle=Focus(position);break;
                case "crane-down": position-=Vector3.Up*distance*.35f*u;angle=Focus(position);break;
                case "truck-left": position-=right*distance*.5f*u;break;
                case "truck-right": position+=right*distance*.5f*u;break;
                case "dolly-in": position+=forward*distance*.35f*u;break;
                case "dolly-out": position-=forward*distance*.35f*u;break;
                case "zoom-in": focal=lens*Mathf.Pow(2,u);break;
                case "zoom-out": focal=lens*Mathf.Pow(.5f,u);break;
                case "slider-reveal": position+=right*distance*.5f*u;angle=Focus(position);break;
                case "push-through": position+=forward*distance*1.1f*u;break;
                case "whip-pan": angle=(rotation*new Quaternion(Vector3.Up,Mathf.DegToRad(-120)*Smooth(u,.25f,.65f))).Normalized();break;
                case "crash-zoom": focal=lens*Mathf.Pow(3,Smooth(u,.35f,.65f));break;
                case "orbit": position=target+new Basis(Vector3.Up,Mathf.Pi*.5f*u)*(start-target);angle=Focus(position);break;
                case "ascending-orbit": position=target+new Basis(Vector3.Up,Mathf.Pi*.67f*u)*(start-target)+Vector3.Up*distance*.5f*u;angle=Focus(position);break;
                case "descending-orbit": position=target+new Basis(Vector3.Up,Mathf.Pi*.67f*u)*(start-target)-Vector3.Up*distance*.3f*u;angle=Focus(position);break;
                case "aerial-establishing": position-=forward*distance*.7f*u;position+=Vector3.Up*distance*.6f*u;angle=Focus(position);break;
                case "dive": position+=forward*distance*.55f*u-Vector3.Up*distance*.4f*u;angle=Focus(position);break;
                case "pull-away": position-=forward*distance*.8f*u;position+=Vector3.Up*distance*.2f*u;angle=Focus(position);break;
                case "handheld":
                    float t=(float)seconds,envelope=Smooth(t,0,.4f);float amplitude=Math.Clamp(distance*.002f,.006f,.03f)*envelope;
                    position+=basis*new Vector3(Mathf.Sin(t*2.8f)*amplitude,Mathf.Sin(t*3.4f)*amplitude*.8f,Mathf.Sin(t*1.3f)*amplitude*.35f);
                    angle=(rotation*Quaternion.FromEuler(new Vector3(Mathf.Sin(t*2.2f)*.6f,Mathf.Sin(t*3.1f)*.8f,Mathf.Sin(t*1.7f)*.25f)*(Mathf.Pi/180)*envelope)).Normalized();break;
            }
            // Every preset starts at exactly the composition the user was viewing.
            if(i==0){position=start;angle=rotation;focal=lens;}
            focal=Math.Clamp(focal,8,300);
            if(!position.IsFinite()||!Finite(angle)||!float.IsFinite(focal))throw new InvalidDataException("The camera movement produced an invalid key.");
            keys.Add(new ShotCameraKey(seconds,position,angle,focal));
        }
        rig.Keys.Clear();rig.Keys.AddRange(keys);rig.Evaluate(0);
        Quaternion Focus(Vector3 at)=>(LookAt(at,target)*aimOffset).Normalized();
    }
    static float Smooth(float value,float from,float to)
    {float u=Math.Clamp((value-from)/Math.Max(.00001f,to-from),0,1);return u*u*(3-2*u);}
    static Quaternion LookAt(Vector3 position,Vector3 target)
    {
        var direction=target-position;if(direction.LengthSquared()<.000001f)direction=Vector3.Forward;direction=direction.Normalized();
        var up=Math.Abs(direction.Dot(Vector3.Up))>.999f?Vector3.Back:Vector3.Up;
        return Transform3D.Identity.LookingAt(direction,up).Basis.GetRotationQuaternion().Normalized();
    }
    static bool Finite(Quaternion q)=>float.IsFinite(q.X)&&float.IsFinite(q.Y)&&float.IsFinite(q.Z)&&float.IsFinite(q.W)&&q.LengthSquared()>.000001f;
}
