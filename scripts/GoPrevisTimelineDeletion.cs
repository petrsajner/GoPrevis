using Godot;
using System;
using System.Linq;

namespace GoPrevis;

public partial class GoPrevisApp
{
    void RemoveMotionData(Entry entry,string? clipId)
    {
        if(clipId==null)entry.Data.MotionClips.Clear();else entry.Data.MotionClips.RemoveAll(c=>c.Id==clipId);
        var clips=entry.Data.MotionClips.OrderBy(c=>c.Start).ToArray();
        for(int i=0;i<clips.Length;i++)clips[i].BlendIn=i==0?0:Math.Min(clips[i].BlendIn,Math.Max(0,Math.Min(clips[i].Duration,clips[i-1].Start+clips[i-1].Duration-clips[i].Start)));
        _selectedClipId=clips.FirstOrDefault()?.Id??"";
        if(entry.NativeTrack==null&&entry.Player!=null){entry.Player.Stop();entry.Animation="";entry.Data.ModelAnimation="";}
        RebuildTrack(entry);
    }
    partial class TimelineView
    {
        sealed record TimelineItem(string Kind,string Target,string Channel="All",string Id="");
        TimelineItem? _selectedItem;
        PopupMenu? _itemMenu;
        internal string SelectedItemKind=>_selectedItem?.Kind??"";
        void ClearItemSelection(){_selectedItem=null;_itemMenu?.Hide();}
        void SelectItem(TimelineItem item)
        {ClearKeySelection();_selectedItem=item;App._interpolationSelection=null;App.RefreshKeyInterpolationInspector();QueueRedraw();}
        internal void SelectMotionClip(Entry entry,MotionClipData clip)
        {App.SelectTimelineClip(entry,clip);}
        internal void MarkMotionSelected(Entry entry,MotionClipData clip)=>SelectItem(new("MotionClip",entry.Data.Id,Id:clip.Id));
        void SelectTrack(TrackRow row)
        {
            if(row.Camera!=null)App.SelectCamera(row.Camera);else if(row.Kind=="Pose")App.SelectPoseCorrectionTimeline(row.Object!,row.Channel,App._time);else App.Select(row.Object);
            if(row.Kind is "Motion" or "Path")App.ShowInspectorSection(row.Kind);
            SelectItem(new("Track"+row.Kind,row.Camera?.Id??row.Object!.Data.Id,row.Channel));
        }
        Rect2 CameraMoveRect(TrackRow row)
        {
            if(row.Camera is not {} camera||row.Child||!App.CameraMovementActive(camera))return new();
            int index=Rows.FindIndex(r=>r.Kind=="Transform"&&!r.Child&&r.Camera?.Id==camera.Id);
            float start=X(camera.Movement.Start),end=X(Math.Min(App._duration,camera.Movement.Start+camera.Movement.Duration));
            return new Rect2(start,Top+index*RowHeight+2,Math.Max(2,end-start),19);
        }
        internal Rect2 CameraMoveRect(string cameraId)=>CameraMoveRect(new TrackRow("Transform","",Camera:App._cameras.FirstOrDefault(c=>c.Id==cameraId)));
        bool HitCameraMove(TrackRow row,float x)=>CameraMoveRect(row) is var rect&&rect.Size.X>0&&x>=rect.Position.X&&x<=rect.End.X;
        bool HitPathSpan(TrackRow row,float x)
        {
            if(row.Kind!="Path")return false;var path=row.Object!.Data.Path;var times=ActorPathRuntime.PointTimes(path);if(times.Length==0)return false;
            return x>=X(Math.Clamp(path.Start+times[0],0,App._duration))&&x<=X(Math.Clamp(path.Start+times[^1],0,App._duration));
        }
        string DeleteItemLabel=>_selectedItem?.Kind switch{
            "MotionClip"=>"Delete motion clip","CameraMove"=>"Delete camera move","TrackMotion"=>"Delete all motion clips","TrackPath"=>"Delete path",
            "TrackPose"=>_selectedItem.Channel=="All"?"Delete pose corrections":"Delete joint correction keys",
            "TrackTransform" when _selectedItem.Channel=="All"&&App._cameras.Any(c=>c.Id==_selectedItem.Target)=>"Delete camera animation",_=>"Delete transform keys"};
        void ShowItemMenu(Vector2 point)
        {
            _itemMenu??=CreateItemMenu();_itemMenu.Clear();AddMotionClipboardMenuItems(_itemMenu);_itemMenu.AddItem(DeleteItemLabel,0);
            _itemMenu.Position=(Vector2I)(GetViewport().GuiEmbedSubwindows?GetGlobalTransformWithCanvas()*point:GetScreenTransform()*point);_itemMenu.Popup();
        }
        PopupMenu CreateItemMenu()
        {var menu=new PopupMenu();AddChild(menu);menu.IdPressed+=id=>InvokeMotionClipboardAction((int)id);return menu;}
        internal bool ItemMenuVisible=>_itemMenu?.Visible==true;
        internal void InvokeItemMenuDelete()=>_itemMenu!.EmitSignal(PopupMenu.SignalName.IdPressed,0L);

        internal bool DeleteSelectedItems()
        {
            if(_selectedItem==null){bool hadKeys=_selectedKeys.Count>0;return DeleteSelectedKeys()||hadKeys;}
            if(App._exportBusy||App._aiBusy)return false;
            var item=_selectedItem;var entry=App._entries.FirstOrDefault(e=>e.Data.Id==item.Target);var camera=App._cameras.FirstOrDefault(c=>c.Id==item.Target);
            var row=new TrackRow("Transform","",entry,camera,item.Channel);
            bool exists=item.Kind switch{
                "MotionClip"=>entry?.Data.MotionClips.Any(c=>c.Id==item.Id)==true,
                "CameraMove"=>camera!=null&&App.CameraMovementActive(camera),
                "TrackMotion"=>entry!=null&&(entry.Data.MotionClips.Count>0||entry.Player!=null&&entry.Animation!=""),
                "TrackPath"=>entry?.Data.Path.Points.Count>0,
                "TrackPose"=>entry!=null&&App.PoseCorrectionTimes(entry,item.Channel).Any(),
                "TrackTransform"=> (entry!=null||camera!=null)&&(new[]{"Position","Rotation","Scale"}.Where(c=>item.Channel=="All"||item.Channel==c).Any(c=>TransformLayerRuntime.Channel(Animation(row),c).Count>0)||camera!=null&&((item.Channel is "All" or "Lens")&&camera.LensKeys.Count>0||item.Channel=="All"&&App.CameraMovementActive(camera))),_=>false};
            if(!exists){ClearKeySelection();return true;}
            EndDrag();App.StopHistoryPlayback();App.PushUndo();
            switch(item.Kind)
            {
                case "MotionClip":App.RemoveMotionData(entry!,item.Id);break;
                case "TrackMotion":App.RemoveMotionData(entry!,null);break;
                case "CameraMove":
                    var position=camera!.Rig.GlobalPosition;var rotation=camera.Rig.GlobalRotationDegrees;camera.Movement=new();
                    if(camera.Animation.Position.Count==0)camera.BasePosition=position;if(camera.Animation.Rotation.Count==0)camera.BaseRotation=rotation;break;
                case "TrackPath":entry!.Data.Path=new(){Duration=App._duration};App._selectedPathPoint=0;break;
                case "TrackPose":
                    if(item.Channel=="All")entry!.Data.PoseCorrections=new();else if(item.Channel=="Root")entry!.Data.PoseCorrections.Root.Clear();else entry!.Data.PoseCorrections.Bones.Remove(item.Channel);break;
                case "TrackTransform":
                    if(camera!=null&&item.Channel=="All"){camera.BasePosition=camera.Rig.GlobalPosition;camera.BaseRotation=camera.Rig.GlobalRotationDegrees;camera.BaseScale=camera.Rig.Scale;camera.BaseLens=camera.Rig.FocalLengthMm;camera.Movement=new();}
                    foreach(string channel in new[]{"Position","Rotation","Scale"}.Where(c=>item.Channel=="All"||item.Channel==c))TransformLayerRuntime.Channel(Animation(row),channel).Clear();
                    if(camera!=null&&item.Channel is "All" or "Lens")camera.LensKeys.Clear();if(camera!=null&&item.Channel=="All"){camera.Rig.ClearKeys();camera.AnimationInitialized=true;}break;
            }
            ClearKeySelection();App.SetTime(App._time);App.RefreshSequenceInspector();App.RefreshCameraMovementInspector();App.RefreshCameraTrackingInspector();App.RefreshProjectDirtyState();UpdateMinimumHeight();
            GrabFocus();App.Status(DeleteItemLabelFor(item)+" · Undo restores the animation.");return true;
        }
        static string DeleteItemLabelFor(TimelineItem item)=>item.Kind is "MotionClip" or "TrackMotion"?"Motion removed":item.Kind=="CameraMove"?"Camera move removed":"Animation removed";
    }
}
