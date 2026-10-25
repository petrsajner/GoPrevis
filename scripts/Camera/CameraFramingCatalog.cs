using Godot;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace GoPrevis;

public sealed record CameraFramingPreset(string Id,string Name,string Description);

/// <summary>World-space subject geometry; Forward points out of the subject's front.</summary>
public sealed record FramingSubject(Aabb WorldBounds,Vector3 Forward,Vector3 Head,Vector3 HeadForward,
    Vector3 LeftShoulder,Vector3 RightShoulder,bool IsHuman=false)
{
    public Vector3 Pelvis { get; init; } = WorldBounds.GetCenter();
    public Vector3 Center=>WorldBounds.GetCenter();
}

/// <summary>Ready-made, static compositions relative to the chosen subject, never the world origin.</summary>
public static class CameraFramingCatalog
{
    public static IReadOnlyList<CameraFramingPreset> All { get; }=new CameraFramingPreset[]
    {
        new("current-view","Current view","Use the working view's position, angle and field of view."),
        new("front-medium","Front medium","Frame the subject from the waist to the head, from the front."),
        new("front-close-up","Front close-up","A portrait-sized view of the subject's head."),
        new("front-wide","Front wide","Fit the whole subject from the front, with room around it."),
        new("side-follow","Side follow","A full-body side composition, ready for a following camera move."),
        new("side-close","Side close","A close profile view of the subject's head and upper body."),
        new("back-medium","Back medium","Frame the upper body from behind the subject."),
        new("top-wide","Top wide","Fit the whole subject from directly overhead."),
        new("high-angle-45","45° high angle","A full-subject view looking downward at 45 degrees."),
        new("low-angle","Low angle","Look up toward the subject from near ground level."),
        new("low-wide","Low wide","A low, wide-angle view with the whole subject visible."),
        new("over-shoulder-left","Over shoulder left","Look past the subject's left shoulder toward the scene ahead."),
        new("over-shoulder-right","Over shoulder right","Look past the subject's right shoulder toward the scene ahead."),
        new("birds-eye","Bird's-eye","An elevated overhead establishing composition with more surrounding space."),
        new("dutch-angle","Dutch angle","A front composition with a fifteen-degree camera roll.")
    };

    public static CameraFramingPreset? Find(string id)
    {
        string normalized=id.Trim().ToLowerInvariant() switch
        {
            "wide shot"=>"front-wide","medium shot"=>"front-medium","close-up"=>"front-close-up",
            "high angle"=>"high-angle-45","current view"=>"current-view","bird's eye"=>"birds-eye",_=>id
        };
        return All.FirstOrDefault(p=>p.Id.Equals(normalized,StringComparison.OrdinalIgnoreCase)||p.Name.Equals(normalized,StringComparison.OrdinalIgnoreCase));
    }

    public static void Apply(StudioCameraRig rig,string id,FramingSubject subject,Transform3D workingView,float horizontalWorkingFov=50,float aspectRatio=16f/9)
    {
        var preset=Find(id)??throw new InvalidDataException("Unknown camera framing preset: "+id);
        if(!float.IsFinite(aspectRatio)||aspectRatio<=0)throw new ArgumentException("Camera aspect ratio must be positive.");
        if(!subject.WorldBounds.Position.IsFinite()||!subject.WorldBounds.Size.IsFinite()||subject.WorldBounds.Size.X<0||subject.WorldBounds.Size.Y<0||subject.WorldBounds.Size.Z<0)
            throw new ArgumentException("The framing subject has invalid bounds.");
        var scale=rig.Scale;
        if(preset.Id=="current-view"&&(!workingView.Origin.IsFinite()||!workingView.Basis.IsFinite()))throw new ArgumentException("The working camera view is invalid.");
        rig.ClearKeys();
        if(preset.Id=="current-view")
        {
            if(!workingView.Origin.IsFinite()||!workingView.Basis.IsFinite())throw new ArgumentException("The working camera view is invalid.");
            float fov=float.IsFinite(horizontalWorkingFov)?Math.Clamp(horizontalWorkingFov,7,132):50;
            rig.GlobalTransform=new Transform3D(workingView.Basis.Orthonormalized(),workingView.Origin);rig.Scale=scale;
            rig.FocalLengthMm=Math.Clamp(StudioCameraRig.SensorWidthMm/(2*Mathf.Tan(Mathf.DegToRad(fov)*.5f)),8,300);return;
        }
        var bounds=subject.WorldBounds;float extent=Math.Max(.2f,bounds.Size.Length());
        var forward=Horizontal(subject.Forward,Vector3.Forward);
        var right=Vector3.Up.Cross(forward).Normalized();
        var headForward=Horizontal(subject.HeadForward,forward);
        var focus=bounds.GetCenter();var region=bounds;var direction=forward;var up=Vector3.Up;float lens=35,padding=1.18f,roll=0;
        switch(preset.Id)
        {
            case "front-medium":region=UpperBody(subject);focus=region.GetCenter();lens=50;break;
            case "front-close-up":region=HeadRegion(subject);focus=region.GetCenter();direction=headForward;lens=85;padding=1.12f;break;
            case "side-follow":direction=right;lens=40;break;
            case "side-close":region=subject.IsHuman?UpperBody(subject):HeadRegion(subject);focus=region.GetCenter();direction=right;lens=70;padding=1.12f;break;
            case "back-medium":region=UpperBody(subject);focus=region.GetCenter();direction=-forward;lens=50;break;
            case "top-wide":direction=Vector3.Up;up=forward;lens=28;padding=1.25f;break;
            case "high-angle-45":direction=(forward+Vector3.Up).Normalized();lens=35;padding=1.22f;break;
            case "low-angle":direction=(forward-Vector3.Up*.32f).Normalized();lens=35;padding=1.13f;break;
            case "low-wide":direction=(forward-Vector3.Up*.20f).Normalized();lens=24;padding=1.30f;break;
            case "over-shoulder-left":case "over-shoulder-right":
            {
                bool left=preset.Id=="over-shoulder-left";var shoulder=left?subject.LeftShoulder:subject.RightShoulder;
                float width=Math.Max(.2f,subject.LeftShoulder.DistanceTo(subject.RightShoulder));
                float retreat=Math.Max(.55f,extent*.32f);var side=(left?-right:right)*width*.16f;
                var position=shoulder-forward*retreat+side+Vector3.Up*Math.Max(.04f,extent*.025f);
                var target=shoulder+forward*Math.Max(1,extent*1.6f)+Vector3.Up*Math.Max(.03f,extent*.03f);
                rig.GlobalTransform=Looking(position,target,Vector3.Up);rig.Scale=scale;rig.FocalLengthMm=50;return;
            }
            case "birds-eye":direction=Vector3.Up;up=forward;lens=24;padding=2.05f;break;
            case "dutch-angle":roll=Mathf.DegToRad(15);padding=1.42f;lens=35;break;
        }
        var orientation=Looking(Vector3.Zero,-direction,up).Basis;
        if(roll!=0)orientation*=new Basis(Vector3.Back,roll);
        float distance=FitDistance(region,focus,orientation,lens,padding,aspectRatio);
        var origin=focus+orientation.Z*distance;
        // Low-angle cameras stay above the subject's support plane. Fitting is
        // recalculated after lifting so the silhouette still stays inside frame.
        if(preset.Id is "low-angle" or "low-wide")
        {
            float minimumY=bounds.Position.Y+Math.Min(.12f,Math.Max(.035f,bounds.Size.Y*.06f));
            if(origin.Y<minimumY)
            {
                float horizontal=new Vector2(origin.X-focus.X,origin.Z-focus.Z).Length();
                direction=(forward*horizontal+Vector3.Up*(minimumY-focus.Y)).Normalized();orientation=Looking(Vector3.Zero,-direction,Vector3.Up).Basis;
                distance=FitDistance(region,focus,orientation,lens,padding,aspectRatio);origin=focus+orientation.Z*distance;
                if(origin.Y<minimumY)origin.Y=minimumY;
                orientation=Looking(origin,focus,Vector3.Up).Basis;
            }
        }
        rig.GlobalTransform=new Transform3D(orientation,origin);rig.Scale=scale;rig.FocalLengthMm=lens;
    }

    public static Aabb FramingRegion(FramingSubject subject,string id)=>Find(id)?.Id switch
    {
        "front-medium" or "back-medium"=>UpperBody(subject),
        "side-close"=>subject.IsHuman?UpperBody(subject):HeadRegion(subject),
        "front-close-up"=>HeadRegion(subject),_=>subject.WorldBounds
    };
    static Vector3 Horizontal(Vector3 value,Vector3 fallback)
    {value.Y=0;return value.IsFinite()&&value.LengthSquared()>.000001f?value.Normalized():fallback;}
    static Transform3D Looking(Vector3 position,Vector3 target,Vector3 up)
    {
        var direction=target-position;if(direction.LengthSquared()<.000001f)direction=Vector3.Forward;
        if(Math.Abs(direction.Normalized().Dot(up.Normalized()))>.9999f)up=Math.Abs(direction.Normalized().Dot(Vector3.Forward))<.95f?Vector3.Forward:Vector3.Right;
        return new Transform3D(Basis.Identity,position).LookingAt(position+direction,up);
    }
    static Aabb HeadRegion(FramingSubject subject)
    {
        float radius=subject.IsHuman?Math.Clamp(subject.WorldBounds.Size.Length()*.095f,.08f,.5f):Math.Max(.1f,subject.WorldBounds.Size.Length()*.19f);
        return new Aabb(subject.Head-Vector3.One*radius,Vector3.One*radius*2);
    }
    static Aabb UpperBody(FramingSubject subject)
    {
        if(!subject.IsHuman)
        {
            var bounds=subject.WorldBounds;return new Aabb(bounds.Position+Vector3.Up*bounds.Size.Y*.35f,new Vector3(bounds.Size.X,bounds.Size.Y*.65f,bounds.Size.Z));
        }
        float radius=Math.Clamp(subject.WorldBounds.Size.Length()*.075f,.07f,.35f);
        var points=new[]{subject.Head,subject.Pelvis,subject.LeftShoulder,subject.RightShoulder};var min=points.Aggregate((a,b)=>a.Min(b));var max=points.Aggregate((a,b)=>a.Max(b));
        return new Aabb(min-Vector3.One*radius,max-min+Vector3.One*radius*2);
    }
    static float FitDistance(Aabb region,Vector3 focus,Basis camera,float lens,float padding,float aspectRatio)
    {
        float horizontal=StudioCameraRig.SensorWidthMm/(2*lens),vertical=horizontal/aspectRatio;float distance=.12f;
        var inverse=camera.Inverse();
        for(int i=0;i<8;i++)
        {
            var local=inverse*(region.GetEndpoint(i)-focus);
            distance=Math.Max(distance,local.Z+Math.Abs(local.X)*padding/horizontal);
            distance=Math.Max(distance,local.Z+Math.Abs(local.Y)*padding/vertical);
            distance=Math.Max(distance,local.Z+.12f);
        }
        return distance;
    }
}
