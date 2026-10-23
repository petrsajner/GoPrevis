using Godot;
using System;
using System.Globalization;
using System.Collections.Generic;
using System.Linq;

namespace Previs;

public partial class PrevisApp
{
    partial class TimelineView:Control
    {
        public PrevisApp App=null!;
        const float Left=145,Top=27,RowHeight=30;
        Entry? _dragEntry;
        Entry? _dragPathEntry;
        int _dragPathIndex;
        double _pathKeyStart;
        MotionClipData? _dragClip;
        bool _trim,_scrubbing;
        bool _hasDragged;
        double _start,_length;
        float _dragX;
        float Width=>Math.Max(1,Size.X-Left-16);
        float X(double seconds)=>Left+Width*(float)(seconds/App._duration);
        double Seconds(float x)=>Math.Clamp((x-Left)/Width*App._duration,0,App._duration);
        double Snap(double value)=>Math.Round(value*App._fps)/App._fps;
        Entry[] Actors=>App._entries.Where(e=>e.Node is RiggedActor||e.Player!=null).ToArray();
        List<(CameraEntry? Camera,Entry? Actor,bool Path)> Rows
        {
            get{var rows=new List<(CameraEntry?,Entry?,bool)>();foreach(var camera in App._cameras)rows.Add((camera,null,false));foreach(var actor in Actors){rows.Add((null,actor,false));if(actor.Data.Path.Points.Count>0)rows.Add((null,actor,true));}return rows;}
        }

        public override void _Input(InputEvent input)
        {
            if(_dragClip==null&&_dragPathEntry==null&& !_scrubbing)return;
            if(input is InputEventMouseMotion){float x=GetLocalMousePosition().X;if(_dragClip!=null)UpdateDrag(x);else if(_dragPathEntry!=null)UpdatePathDrag(x);else App.SetTime(Snap(Seconds(x)));GetViewport().SetInputAsHandled();}
            else if(input is InputEventMouseButton{ButtonIndex:MouseButton.Left,Pressed:false}){_dragClip=null;_dragEntry=null;_dragPathEntry=null;_scrubbing=false;GetViewport().SetInputAsHandled();}
        }
        void UpdatePathDrag(float x)
        {
            if(_dragPathEntry==null)return;if(!_hasDragged&&Math.Abs(x-_dragX)<3)return;if(!_hasDragged){App.PushUndo();_hasDragged=true;}
            App.SetPathKeyTime(_dragPathEntry,_dragPathIndex,_pathKeyStart+(x-_dragX)/Width*App._duration,false);
        }
        void UpdateDrag(float x)
        {
            if(_dragClip==null||_dragEntry==null)return;
            if(!_hasDragged&&Math.Abs(x-_dragX)<3)return;if(!_hasDragged){App.PushUndo();_hasDragged=true;}
            if(OS.GetCmdlineUserArgs().Contains("--trace-input"))GD.Print($"TIMELINE_DRAG x={x} start={_dragClip.Start} origin={_dragX} trim={_trim}");
            double change=(x-_dragX)/Width*App._duration;
            if(_trim)_dragClip.Duration=Math.Clamp(Snap(_length+change),1.0/App._fps,Math.Max(1.0/App._fps,App._duration-_dragClip.Start));
            else{_dragClip.Start=Math.Clamp(Snap(_start+change),0,Math.Max(0,App._duration-_dragClip.Duration));var previous=_dragEntry.Data.MotionClips.Where(c=>c!=_dragClip&&c.Start<=_dragClip.Start).OrderBy(c=>c.Start).LastOrDefault();_dragClip.BlendIn=previous==null?0:Math.Max(0,Math.Min(_dragClip.Duration,previous.Start+previous.Duration-_dragClip.Start));}
            try{App.RebuildTrack(_dragEntry);App.SetTime(App._time);App.RefreshSequenceInspector();}catch(Exception ex){App.Status(ex.Message);}
        }

        public override void _Draw()
        {
            if(App==null||App._shot==null)return;var font=ThemeDB.FallbackFont;float end=Top+Rows.Count*RowHeight;
            for(int i=0;i<=8;i++){float x=Left+Width*i/8;DrawLine(new Vector2(x,Top-7),new Vector2(x,end),App._line);DrawString(font,new Vector2(x,16),(App._duration*i/8).ToString("0.##",CultureInfo.InvariantCulture)+" s",fontSize:11,modulate:App._muted);}
            int row=0;
            foreach(var camera in App._cameras)
            {
                float y=Top+row++*RowHeight;bool active=camera.Rig==App._shot;DrawRect(new Rect2(0,y-3,Left-4,27),active?new Color("332a44"):new Color("1c1b21"));DrawString(font,new Vector2(8,y+15),camera.Name,fontSize:12,modulate:active?App._text:App._muted);
                DrawRect(new Rect2(Left,y,Width,21),active?new Color("30263e"):new Color("24212c"));
                foreach(var key in camera.Rig.Keys.Where(k=>k.TimeSeconds<=App._duration)){float x=X(key.TimeSeconds);DrawColoredPolygon(new[]{new Vector2(x,y+3),new Vector2(x+5,y+10),new Vector2(x,y+18),new Vector2(x-5,y+10)},active?App._accent:new Color("787184"));}
            }
            foreach(var actor in Actors)
            {
                float y=Top+row++*RowHeight;bool selected=App._selected==actor;DrawRect(new Rect2(0,y-3,Left-4,27),selected?new Color("332a44"):new Color("1c1b21"));DrawString(font,new Vector2(8,y+15),actor.Data.Name,fontSize:12,modulate:App._text);
                if(actor.Node is not RiggedActor){DrawRect(new Rect2(Left,y,Width,21),new Color("3c3548"));DrawString(font,new Vector2(Left+7,y+15),actor.Animation,fontSize:11,modulate:App._text);continue;}
                var clips=actor.Data.MotionClips.OrderBy(c=>c.Start).ToList();
                foreach(var clip in clips.Where(c=>c.Start<App._duration))
                {
                    float start=X(clip.Start),finish=X(Math.Min(App._duration,clip.Start+clip.Duration));bool chosen=selected&&clip.Id==App._selectedClipId;var rect=new Rect2(start,y,Math.Max(2,finish-start),22);
                    DrawRect(rect,chosen?new Color("67507f"):new Color("43364f"));if(chosen)DrawRect(rect,App._accent,false,1);
                    var previous=clips.Where(c=>c!=clip&&c.Start<=clip.Start).LastOrDefault();double overlap=previous==null?0:Math.Min(clip.BlendIn,previous.Start+previous.Duration-clip.Start);
                    if(overlap>0){float blendEnd=Math.Min(finish,X(clip.Start+overlap));DrawRect(new Rect2(start,y,blendEnd-start,22),new Color("8a72a6"));DrawLine(new Vector2(start,y+21),new Vector2(blendEnd,y+1),new Color("c6afe5"),1);}
                    DrawLine(new Vector2(finish-3,y+4),new Vector2(finish-3,y+18),new Color("bdaec8"),2);
                    string name=clip.Name;int chars=(int)Math.Max(0,(rect.Size.X-18)/6.2f);if(chars<name.Length)name=chars>2?name[..Math.Min(name.Length,chars-1)]+"…":"";
                    if(name!="")DrawString(font,new Vector2(start+7,y+15),name,fontSize:11,modulate:App._text);
                }
                if(actor.Data.Path.Points.Count>0)
                {
                    float py=Top+row++*RowHeight;DrawRect(new Rect2(0,py-3,Left-4,27),selected?new Color("263b41"):new Color("1c1b21"));DrawString(font,new Vector2(8,py+15),actor.Data.Name+" · Path",fontSize:12,modulate:new Color("9ed8df"));
                    var times=ActorPathRuntime.PointTimes(actor.Data.Path);if(times.Length>0){float begin=X(actor.Data.Path.Start+times[0]),finish=X(actor.Data.Path.Start+times[^1]);DrawRect(new Rect2(begin,py+8,Math.Max(2,finish-begin),5),new Color("3b6570"));}
                    for(int i=0;i<times.Length;i++){float x=X(actor.Data.Path.Start+times[i]);bool chosen=selected&&i==App._selectedPathPoint;var color=actor.Data.Path.Enabled?(chosen?new Color("c2f5fc"):new Color("78c4d1")):new Color("62757a");DrawColoredPolygon(new[]{new Vector2(x,py+2),new Vector2(x+6,py+10),new Vector2(x,py+18),new Vector2(x-6,py+10)},color);DrawString(font,new Vector2(x-7,py+29),"P"+(i+1),fontSize:11,modulate:App._text);}
                }
            }
            float head=X(App._time);DrawLine(new Vector2(head,20),new Vector2(head,end+2),new Color("e8dcff"),2);DrawColoredPolygon(new[]{new Vector2(head-5,19),new Vector2(head+5,19),new Vector2(head,25)},new Color("e8dcff"));
        }
        public override void _GuiInput(InputEvent input)
        {
            if(input is InputEventMouseButton {ButtonIndex:MouseButton.Left} button)
            {
                if(OS.GetCmdlineUserArgs().Contains("--trace-input"))GD.Print($"TIMELINE_BUTTON pressed={button.Pressed} x={button.Position.X} y={button.Position.Y} clip={_dragClip?.Name}");
                if(!button.Pressed){_dragClip=null;_dragEntry=null;_dragPathEntry=null;_scrubbing=false;return;}
                int row=(int)Math.Floor((button.Position.Y-Top)/RowHeight);
                var rows=Rows;
                if(row>=0&&row<rows.Count&&rows[row].Camera is {} camera){App.SetActiveCamera(camera.Id);if(button.Position.X>=Left)App.SetTime(Snap(Seconds(button.Position.X)));AcceptEvent();return;}
                if(row>=0&&row<rows.Count&&rows[row].Actor is {} actor)
                {
                    if(rows[row].Path)
                    {
                        var times=ActorPathRuntime.PointTimes(actor.Data.Path);int point=-1;float nearest=10;
                        for(int i=0;i<times.Length;i++){float d=Math.Abs(button.Position.X-X(actor.Data.Path.Start+times[i]));if(d<nearest){nearest=d;point=i;}}
                        if(point>=0&&button.Position.X>=Left-8){App.SelectPathKey(actor,point);_dragPathEntry=actor;_dragPathIndex=point;_dragX=button.Position.X;_pathKeyStart=actor.Data.Path.Start+times[point];_hasDragged=false;AcceptEvent();return;}
                        App.Select(actor);App.ShowInspectorSection("Path");if(button.Position.X>=Left){_scrubbing=true;App.SetTime(Snap(Seconds(button.Position.X)));}AcceptEvent();return;
                    }
                    if(button.Position.X<Left){App.Select(actor);AcceptEvent();return;}
                    var clip=actor.Data.MotionClips.OrderByDescending(c=>c.Start).FirstOrDefault(c=>button.Position.X>=X(c.Start)&&button.Position.X<=X(c.Start+c.Duration));
                    if(clip!=null){App._playing=false;App._play.Text="▶ Play";App.SelectTimelineClip(actor,clip);_hasDragged=false;_dragEntry=actor;_dragClip=clip;_dragX=button.Position.X;_start=clip.Start;_length=clip.Duration;_trim=X(clip.Start+clip.Duration)-button.Position.X<9;AcceptEvent();return;}
                }
                if(button.Position.X>=Left){_scrubbing=true;App.SetTime(Snap(Seconds(button.Position.X)));AcceptEvent();}
            }
            if(input is InputEventMouseMotion motion)
            {
                if(_dragClip!=null&&_dragEntry!=null)
                {
                    UpdateDrag(motion.Position.X);
                    AcceptEvent();
                }
                else if(_dragPathEntry!=null){UpdatePathDrag(motion.Position.X);AcceptEvent();}
                else if(_scrubbing){App.SetTime(Snap(Seconds(motion.Position.X)));AcceptEvent();}
            }
        }
    }
}
