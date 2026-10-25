using Godot;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text.Json;
using System.Threading.Tasks;

namespace GenPrevis;

public partial class GenPrevisApp
{
    public async Task<int> RunVideoPlaybackChecks()
    {
        var original=Snapshot();string project=_projectPath;double time=_time;bool pilot=_pilot,playing=_playing;
        var undo=_undo.ToArray();var redo=_redo.ToArray();int checks=0;bool passed=false;string? failure=null;var metrics=new List<object>();
        string output=ProjectSettings.GlobalizePath("res://output/video-playback-checks");Directory.CreateDirectory(output);
        void Check(bool valid,string message){Require(valid,"Video playback: "+message);checks++;}
        async Task Frame(){await ToSignal(GetTree(),SceneTree.SignalName.ProcessFrame);}
        async Task Ready()
        {
            var clock=Stopwatch.StartNew();
            while(clock.Elapsed.TotalSeconds<10)
            {
                await Frame();
                if(_referenceVideo is {} source&&_referenceCurrentFrame?.Index==source.IndexAt(ReferenceSampleTime())&&!_referenceDecoding)return;
            }
            throw new InvalidOperationException("The camera video did not reach its requested frame.");
        }
        try
        {
            _playing=false;if(_pilot)TogglePilot();ClearObjects();ClearCameras();var camera=CreateCamera("Realtime video test",null,true);SelectCamera(camera);
            var display=SnapshotSceneSettings();display.GroundVisible=false;display.ObjectLabelsVisible=false;ApplySceneSettings(display);
            string ffmpeg=ShotVideoEncoder.FindBinary("ffmpeg");
            foreach(int fps in new[]{25,30})
            {
                string source=Path.Combine(output,"motion1080p-"+fps+".mp4");
                if(!File.Exists(source))await RunReferenceProbeProcess(ffmpeg,new[]{"-hide_banner","-loglevel","error","-nostdin","-n","-f","lavfi","-i","testsrc2=size=1920x1080:rate="+fps+":duration=4","-c:v","libx264","-threads","2","-preset","veryfast","-crf","20","-pix_fmt","yuv420p",source});
                byte[] sourceHash=SHA256.HashData(File.ReadAllBytes(source));ChangeProjectFps(fps);SetTime(0);
                var load=Stopwatch.StartNew();ApplyActiveCameraReference(new CameraReferenceData{Path=source,Opacity=1,Fill=true});await Ready();double firstMs=load.Elapsed.TotalMilliseconds;
                var stream=_referenceVideo!;Check(stream.Source.Width==1920&&stream.Source.Height==1080&&Math.Abs(stream.Source.Fps-fps)<.001,fps+" fps uses a real 1920×1080 H.264 source");
                Check(stream.Width==960&&stream.Height==540,"the editor proxy is bounded to 960 pixels without changing source media");
                SetTime(0);var distinct=new HashSet<int>();int last=0;double maxLag=0;int starts=stream.DecoderLaunches;var clock=Stopwatch.StartNew();
                if(DisplayServer.GetName()!="headless")
                {
                    _playing=true;_play.Text="Ⅱ Pause";
                    while(clock.Elapsed.TotalSeconds<2.5)
                    {
                        await Frame();int desired=stream.IndexAt(_time);
                        if(_referenceCurrentFrame is {} current){last=current.Index;distinct.Add(last);}
                        maxLag=Math.Max(maxLag,Math.Max(0,desired-last)/(double)fps);
                    }
                    _playing=false;_play.Text="▶";
                    Check(Math.Abs(_time-clock.Elapsed.TotalSeconds)<.25,fps+" fps video advances with real scene playback time");
                    Check(distinct.Count>=fps*2.1,fps+" fps displays at least 84% of real-time source frames through the application viewport");
                    Check(maxLag<.18,fps+" fps playback keeps displayed reference lag below 180 ms");
                }
                else
                {
                    while(clock.Elapsed.TotalSeconds<2.5){SetTime(clock.Elapsed.TotalSeconds);await Frame();if(_referenceCurrentFrame is {} current)distinct.Add(current.Index);}
                }
                Check(stream.DecoderLaunches-starts<=1,"ordinary playback keeps one persistent decoder instead of launching one per frame");
                Check(stream.BufferedBytes<=ReferenceVideoStream.MaximumBufferBytes&&_referenceVideos.Count<=2,"raw frame buffers and retained source caches stay bounded");
                metrics.Add(new{fps,sourceWidth=1920,sourceHeight=1080,displayedFrames=distinct.Count,elapsed=clock.Elapsed.TotalSeconds,maxLagSeconds=maxLag,firstFrameMs=firstMs,decoderStarts=stream.DecoderLaunches,decodedFrames=stream.DecodedFrames,bufferBytes=stream.BufferedBytes,backend="CPU FFmpeg persistent raw RGB pipe"});

                SetTime(5.0/fps);await Ready();using(var image=CurrentReferenceVideoImage())
                {
                    byte[] actual=image.GetData();string filter="select=eq(n\\,5),scale="+stream.Width+":"+stream.Height+":flags=fast_bilinear";
                    byte[] expected=await RunReferenceProbeProcess(ffmpeg,new[]{"-hide_banner","-loglevel","error","-nostdin","-i",source,"-an","-vf",filter,"-frames:v","1","-pix_fmt","rgb24","-threads","1","-f","rawvideo","pipe:1"});
                    Check(SHA256.HashData(actual).SequenceEqual(SHA256.HashData(expected)),"an uncached reverse seek matches independently selected source frame 5 exactly at "+fps+" fps");
                }
                SetTime(8);await Ready();Check(_referenceCurrentFrame!.Index==stream.Source.FrameCount-1,"seeking beyond the video holds its actual final frame at "+fps+" fps");
                Check(SHA256.HashData(File.ReadAllBytes(source)).SequenceEqual(sourceHash),"preview decoding leaves the original video bytes unchanged");
            }

            async Task ExactSourceFrame(ReferenceVideoStream stream,string source,int index,string message)
            {
                using var timeout=new System.Threading.CancellationTokenSource(TimeSpan.FromSeconds(10));
                var frame=await stream.WaitForFrameAsync(index,timeout.Token);
                byte[] expected=await RunReferenceProbeProcess(ffmpeg,new[]{"-hide_banner","-loglevel","error","-nostdin","-i",source,"-an","-vf","select=eq(n\\,"+index+"),scale="+stream.Width+":"+stream.Height+":flags=fast_bilinear,setsar=1","-frames:v","1","-pix_fmt","rgb24","-threads","1","-f","rawvideo","pipe:1"});
                Check(SHA256.HashData(frame.Pixels).SequenceEqual(SHA256.HashData(expected)),message);
            }
            string fractional=Path.Combine(output,"fractional1080p.mp4");
            if(!File.Exists(fractional))await RunReferenceProbeProcess(ffmpeg,new[]{"-hide_banner","-loglevel","error","-nostdin","-n","-f","lavfi","-i","testsrc2=size=1920x1080:rate=30000/1001:duration=1","-c:v","libx264","-threads","2","-preset","veryfast","-crf","20","-pix_fmt","yuv420p",fractional});
            using(var stream=await ReferenceVideoStream.OpenAsync(ffmpeg,ShotVideoEncoder.FindBinary("ffprobe"),fractional))
            {
                await ExactSourceFrame(stream,fractional,5,"fractional 29.97 fps seeking matches independently selected frame 5 exactly");
                await ExactSourceFrame(stream,fractional,stream.Source.FrameCount-1,"fractional-rate EOF seeking preserves the actual final source frame");
            }
            string rotated=Path.Combine(output,"rotated1080p.mp4");
            if(!File.Exists(rotated))await RunReferenceProbeProcess(ffmpeg,new[]{"-hide_banner","-loglevel","error","-nostdin","-n","-display_rotation","90","-i",Path.Combine(output,"motion1080p-30.mp4"),"-c","copy",rotated});
            using(var stream=await ReferenceVideoStream.OpenAsync(ffmpeg,ShotVideoEncoder.FindBinary("ffprobe"),rotated))
            {
                Check(stream.Width==540&&stream.Height==960,"phone-style rotation metadata retains the portrait display aspect");
                await ExactSourceFrame(stream,rotated,5,"rotated source pixels match the independently autorotated source frame");
            }
            string corrupted=Path.Combine(output,"decode-error.mp4");File.Copy(fractional,corrupted,true);
            using(var stream=await ReferenceVideoStream.OpenAsync(ffmpeg,ShotVideoEncoder.FindBinary("ffprobe"),corrupted))
            {
                File.WriteAllText(corrupted,"Invalid video test fixture");bool failed=false;
                try{using var timeout=new System.Threading.CancellationTokenSource(TimeSpan.FromSeconds(10));await stream.WaitForFrameAsync(0,timeout.Token);}catch(IOException){failed=true;}
                for(int i=0;i<20;i++){stream.Request(0);await Frame();}
                Check(failed&&stream.DecoderLaunches==1,"a terminal decoder failure cannot relaunch FFmpeg on every UI frame");
            }

            var first=_cameras.Single();string firstId=first.Id;string sharedPath=first.Reference.Path;SetTime(3);await Ready();var reused=_referenceVideo!;int launches=reused.DecoderLaunches;
            var second=CreateCamera("Shared video second camera",null,false);second.Reference=new CameraReferenceData{Path=sharedPath,Opacity=.3f};SelectCamera(second);await Ready();
            Check(ReferenceEquals(reused,_referenceVideo)&&reused.DecoderLaunches==launches,"switching cameras reuses a buffered video source without another decoder launch");
            Check(ActiveCameraReference.Opacity==.3f&&_cameras.Single(item=>item.Id==firstId).Reference.Opacity==1,"shared decoded data keeps camera-specific display settings independent");
            int beforeExport=reused.DecoderLaunches;_exportBusy=true;SetTime(.2);for(int i=0;i<4;i++)await Frame();
            Check(reused.DecoderLaunches==beforeExport,"export mode pauses background prefetch instead of chasing render frame times");
            _exportBusy=false;SetTime(.4);await Ready();Check(_referenceCurrentFrame!.Index==reused.IndexAt(.4),"the exact working frame resumes after export mode");
            if(DisplayServer.GetName()!="headless")
            {
                if(!_pilot)TogglePilot();await Frame();await ToSignal(RenderingServer.Singleton,RenderingServer.SignalName.FramePostDraw);
                GetViewport().GetTexture().GetImage().SavePng(Path.Combine(output,"realtime-camera-video-ui.png"));
            }
            passed=true;GD.Print("VIDEO_PLAYBACK_OK checks="+checks);return checks;
        }
        catch(Exception ex){failure=ex.ToString();throw;}
        finally
        {
            _exportBusy=false;_playing=false;if(_pilot)TogglePilot();Restore(original,"");_projectPath=project;SetTime(time);if(pilot)TogglePilot();_playing=playing;
            _undo.Clear();_redo.Clear();foreach(var item in undo.Reverse())_undo.Push(item);foreach(var item in redo.Reverse())_redo.Push(item);RefreshHistoryControls();
            File.WriteAllText(Path.Combine(output,"report.json"),JsonSerializer.Serialize(new{status=passed?"passed":"failed",checks,failure,metrics},_json));
        }
    }
    static async Task<byte[]> RunReferenceProbeProcess(string binary,IEnumerable<string> arguments)
    {
        using var process=new Process{StartInfo=new ProcessStartInfo(binary){UseShellExecute=false,CreateNoWindow=true,RedirectStandardOutput=true,RedirectStandardError=true}};
        foreach(string argument in arguments)process.StartInfo.ArgumentList.Add(argument);process.Start();
        using var bytes=new MemoryStream();Task copy=process.StandardOutput.BaseStream.CopyToAsync(bytes);Task<string> error=process.StandardError.ReadToEndAsync();
        await process.WaitForExitAsync();await copy;string message=await error;if(process.ExitCode!=0)throw new IOException("Video fixture failed: "+message);return bytes.ToArray();
    }
    async Task RunVideoPlaybackChecksAndQuit()
    {try{await RunVideoPlaybackChecks();GetTree().Quit(0);}catch(Exception ex){GD.PushError("VIDEO_PLAYBACK_FAILED "+ex);GetTree().Quit(1);}}
}
