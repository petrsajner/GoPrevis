using Godot;
using System;
using System.Collections.Generic;
using System.Linq;

namespace GoPrevis;

/// <summary>
/// The shot's content in the export manifest (<c>shot</c>, schema 1, GoPrevis 0.20.0): who is in the
/// shot, how and when they move, when the exported camera sees them, and what the cameras do.
/// Vividia's Director reads it to write a prompt with the right figures, times and camera
/// (Petr, 27. 10. 2026). Times are scene seconds; <c>inFrame</c> lists the spans in which an
/// object's bounds reach into the exported frame.
/// </summary>
public partial class GoPrevisApp
{
    sealed class ShotFrameTracker
    {
        readonly Dictionary<string,List<double[]>> _closed=new(StringComparer.Ordinal);
        readonly Dictionary<string,double> _open=new(StringComparer.Ordinal);
        public HashSet<string> Tracked{get;}=new(StringComparer.Ordinal);
        public void Record(string id,bool visible,double time)
        {
            Tracked.Add(id);
            if(visible){if(!_open.ContainsKey(id))_open[id]=time;return;}
            if(!_open.Remove(id,out double since))return;
            if(!_closed.TryGetValue(id,out var list))_closed[id]=list=new();
            list.Add(new[]{since,time});
        }
        /// <summary>The spans an object was seen in, an open one ending at <paramref name="end"/>; null when it was not tracked.</summary>
        public double[][]? For(string id,double end)
        {
            if(!Tracked.Contains(id))return null;
            var list=_closed.TryGetValue(id,out var closed)?new List<double[]>(closed):new List<double[]>();
            if(_open.TryGetValue(id,out double since))list.Add(new[]{since,end});
            return list.Select(r=>new[]{Math.Round(r[0],3),Math.Round(r[1],3)}).ToArray();
        }
    }

    static int ShotKeyCount(TransformAnimationData? animation)=>animation==null?0:animation.Position.Count+animation.Rotation.Count+animation.Scale.Count;

    /// <summary>An object whose place and shape do not change during the shot.</summary>
    static bool ShotStill(Entry entry)=>entry.Node is not RiggedActor&&entry.NativeTrack==null&&entry.Data.Link==null&&entry.Data.MotionClips.Count==0
        &&!entry.Data.PropertyAnimation.Channels.Values.Any(c=>c.Keys.Count>0)
        &&!(entry.Data.Path.Enabled&&entry.Data.Path.Points.Count>1)&&ShotKeyCount(entry.Data.TransformAnimation)<=3;

    /// <summary>World bounds for the frame test; a figure moved by its mocap's root is centred on its hips.</summary>
    Aabb ShotBounds(Entry entry)
    {
        var bounds=PlacementWorldBounds(entry);
        if(entry.Node is RiggedActor actor)
        {
            var hips=actor.GlobalTransform*actor.GetPoseJointPosition("Hips");var centre=bounds.GetCenter();
            bounds.Position+=new Vector3(hips.X-centre.X,0,hips.Z-centre.Z);
        }
        return bounds;
    }

    /// <summary>Whether a box reaches into the camera's frame.</summary>
    static bool InShotView(Camera3D camera,Vector2 size,Aabb box)
    {
        float minX=float.MaxValue,minY=float.MaxValue,maxX=float.MinValue,maxY=float.MinValue;bool front=false;
        for(int i=0;i<8;i++)
        {
            var point=box.GetEndpoint(i);if(camera.IsPositionBehind(point))continue;front=true;
            var screen=camera.UnprojectPosition(point);minX=Math.Min(minX,screen.X);minY=Math.Min(minY,screen.Y);maxX=Math.Max(maxX,screen.X);maxY=Math.Max(maxY,screen.Y);
        }
        return front&&maxX>=0&&minX<=size.X&&maxY>=0&&minY<=size.Y;
    }

    /// <summary>Bounds of the objects that stand still, measured once per export.</summary>
    Dictionary<string,Aabb> ShotStillBounds()
    {
        var still=new Dictionary<string,Aabb>(StringComparer.Ordinal);
        foreach(var entry in _entries)
        {
            if(entry.Data.Light!=null||!ShotStill(entry))continue;
            try{still[entry.Data.Id]=ShotBounds(entry);}catch(Exception error){GD.PushWarning("Shot content: no bounds for "+entry.Data.Name+": "+error.Message);}
        }
        return still;
    }

    /// <summary>Which objects the exported camera sees at this frame.</summary>
    void TrackShotVisibility(ShotFrameTracker tracker,Camera3D camera,Vector2 size,double time,Dictionary<string,Aabb> still)
    {
        foreach(var entry in _entries)
        {
            if(entry.Data.Light!=null)continue;
            Aabb box;
            if(!still.TryGetValue(entry.Data.Id,out box))
            {
                try{box=ShotBounds(entry);}catch{continue;}
            }
            tracker.Record(entry.Data.Id,entry.Node.IsVisibleInTree()&&InShotView(camera,size,box),time);
        }
    }

    /// <summary>The manifest's <c>shot</c>: the document as exported, with the frame spans measured so far.</summary>
    static object ShotContent(ProjectData document,string cameraId,ShotFrameTracker tracker,double end)
    {
        string? NameOf(string? id)=>string.IsNullOrEmpty(id)?null:document.Objects.FirstOrDefault(o=>o.Id==id)?.Name;
        static double R(double value)=>Math.Round(value,3);
        return new
        {
            schema=1,
            animationSchema=1,
            sceneAnimation=document.PropertyAnimation,
            durationSeconds=R(document.Duration),
            fps=document.Fps,
            activeCameraId=document.ActiveCameraId,
            exportedCameraId=cameraId,
            objects=document.Objects.Select(o=>
            {
                double[] times=o.Path.Enabled&&o.Path.Points.Count>1?ActorPathRuntime.PointTimes(o.Path):Array.Empty<double>();
                return new
                {
                    id=o.Id,name=o.Name,kind=o.Kind,family=o.ActorFamily,model=o.LibraryModelId,light=o.Light!=null,
                    color=o.Appearance?.ColorOverride,
                    propertyAnimation=o.PropertyAnimation,
                    motions=o.MotionClips.Select(c=>new{name=c.Name,start=R(c.Start),end=R(c.Start+c.Duration),loop=c.Loop,speed=c.Speed,automatic=c.IsPlaceholder,propertyAnimation=c.PropertyAnimation}).ToArray(),
                    path=times.Length>1?new{start=R(o.Path.Start+times[0]),end=R(o.Path.Start+times[^1]),points=o.Path.Points.Count,orientToPath=o.Path.OrientToPath}:null,
                    keyed=ShotKeyCount(o.TransformAnimation)>3||o.PropertyAnimation.Channels.Values.Any(c=>c.Keys.Count>0),
                    link=o.Link==null?null:new{from=NameOf(o.Link.StartTargetId),to=NameOf(o.Link.EndTargetId)},
                    pose=o.StaticPose?.Name,
                    inFrame=tracker.For(o.Id,end),
                };
            }).ToArray(),
            cameras=document.Cameras.Select(c=>new
            {
                id=c.Id,name=c.Name,exported=c.Id==cameraId,focalLengthMm=c.FocalLength,propertyAnimation=c.PropertyAnimation,
                lensKeys=c.LensKeys.Select(k=>new{time=R(k.Time),mm=k.Value}).ToArray(),
                movement=c.Movement.Mode=="None"?null:new{mode=c.Movement.Mode,start=R(c.Movement.Start),end=R(c.Movement.Start+c.Movement.Duration),targetId=c.Movement.TargetId,target=NameOf(c.Movement.TargetId),orbitDegrees=c.Movement.OrbitDegrees,radius=c.Movement.Radius,startHeight=c.Movement.StartHeight,endHeight=c.Movement.EndHeight,easing=c.Movement.Easing,aimPath=c.Movement.AimPathEnabled},
                follow=c.Tracking.FollowEnabled?NameOf(c.Tracking.FollowTargetId):null,
                lookAt=c.Tracking.LookAtEnabled?NameOf(c.Tracking.LookAtTargetId):null,
                keyed=ShotKeyCount(c.TransformAnimation)>3,
            }).ToArray(),
        };
    }
}
