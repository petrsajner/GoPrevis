using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace GenPrevis;

/// <summary>
/// A CPU decoder with a persistent FFmpeg raw-RGB pipe. The worker decodes ahead
/// into a bounded sliding window, then lets the pipe back-pressure FFmpeg. No
/// PNG encoding or process launch is performed for an ordinary playback frame.
/// </summary>
public sealed class ReferenceVideoStream : IDisposable
{
    public sealed record Metadata(int Width,int Height,double Fps,int FrameCount,double Duration);
    // Pixels are consumed synchronously by the requesting UI before its next Request.
    // Evicted buffers are reused only after their frame is no longer requested.
    public sealed record Frame(int Index,double Time,int Width,int Height,byte[] Pixels);
    const long MemoryBudget=48L*1024*1024;
    readonly object _gate=new();
    readonly string _binary,_source;
    readonly Dictionary<int,Frame> _frames=new();
    readonly Queue<byte[]> _spares=new();
    readonly SemaphoreSlim _changed=new(0,1);
    readonly CancellationTokenSource _lifetime=new();
    TaskCompletionSource _frameChanged=NewSignal();
    readonly Task _worker;
    Process? _process;
    CancellationTokenSource? _session;
    int _requested,_next=-1,_anchor=-1,_lastIndex;
    bool _active,_prefetch=true,_disposed;
    Exception? _error;
    int _launches,_decoded;
    public Metadata Source {get;}
    public int Width {get;}
    public int Height {get;}
    public int FrameBytes {get;}
    public int Capacity {get;}
    public int DecoderLaunches=>Volatile.Read(ref _launches);
    public int DecodedFrames=>Volatile.Read(ref _decoded);
    public int CachedFrames {get{lock(_gate)return _frames.Count;}}
    public Exception? Error {get{lock(_gate)return _error;}}
    public long BufferedBytes {get{lock(_gate)return (long)(_frames.Count+_spares.Count)*FrameBytes;}}
    public static long MaximumBufferBytes=>MemoryBudget;

    static TaskCompletionSource NewSignal()=>new(TaskCreationOptions.RunContinuationsAsynchronously);
    ReferenceVideoStream(string binary,string source,Metadata metadata)
    {
        _binary=binary;_source=source;Source=metadata;_lastIndex=metadata.FrameCount-1;
        double scale=Math.Min(1,960.0/Math.Max(metadata.Width,metadata.Height));
        Width=Math.Max(2,(int)Math.Round(metadata.Width*scale));Height=Math.Max(2,(int)Math.Round(metadata.Height*scale));
        FrameBytes=checked(Width*Height*3);Capacity=Math.Clamp((int)(MemoryBudget/FrameBytes),8,72);
        _worker=Task.Run(WorkerAsync);
    }

    public static async Task<ReferenceVideoStream> OpenAsync(string ffmpeg,string ffprobe,string source,CancellationToken cancellation=default)
    {
        source=Path.GetFullPath(source);if(!File.Exists(source))throw new FileNotFoundException("The camera background video is missing.",source);
        using var process=new Process{StartInfo=new ProcessStartInfo(ffprobe){UseShellExecute=false,CreateNoWindow=true,RedirectStandardOutput=true,RedirectStandardError=true}};
        foreach(string arg in new[]{"-v","error","-select_streams","v:0","-show_entries","stream=width,height,avg_frame_rate,r_frame_rate,nb_frames,duration,sample_aspect_ratio:stream_tags=rotate:stream_side_data=rotation:format=duration","-of","json",source})process.StartInfo.ArgumentList.Add(arg);
        process.Start();using var registration=cancellation.Register(()=>QueueKill(process));
        Task<string> output=process.StandardOutput.ReadToEndAsync(cancellation),error=process.StandardError.ReadToEndAsync(cancellation);
        await process.WaitForExitAsync(cancellation).ConfigureAwait(false);string json=await output.ConfigureAwait(false),message=await error.ConfigureAwait(false);cancellation.ThrowIfCancellationRequested();
        if(process.ExitCode!=0)throw new InvalidDataException("The camera background video could not be read. "+message.Trim());
        using var doc=JsonDocument.Parse(json);var root=doc.RootElement;
        if(!root.TryGetProperty("streams",out var streams)||streams.GetArrayLength()==0)throw new InvalidDataException("The camera background has no video stream.");
        var stream=streams[0];string Text(string name)=>stream.TryGetProperty(name,out var value)?value.ToString():"";
        static double Number(string text)=>double.TryParse(text,NumberStyles.Float,CultureInfo.InvariantCulture,out double value)&&double.IsFinite(value)?value:0;
        static double Rate(string text){var parts=text.Split('/');return parts.Length==2&&Number(parts[1])>0?Number(parts[0])/Number(parts[1]):Number(text);}
        double fps=Rate(Text("avg_frame_rate"));if(fps<=0)fps=Rate(Text("r_frame_rate"));
        double duration=Number(Text("duration"));if(duration<=0&&root.TryGetProperty("format",out var format)&&format.TryGetProperty("duration",out var d))duration=Number(d.ToString());
        int width=(int)Number(Text("width")),height=(int)Number(Text("height")),count=(int)Number(Text("nb_frames"));
        if(width<1||height<1||fps<=0||fps>1000||duration<=0)throw new InvalidDataException("The camera background video has incomplete dimensions or timing.");
        // FFmpeg autorotates before the filter. Allocate the matching display
        // aspect, including common phone display matrices and non-square pixels.
        double sar=Rate(Text("sample_aspect_ratio").Replace(':','/'));if(sar>0&&sar<100)width=Math.Max(1,(int)Math.Round(width*sar));
        double rotation=0;
        if(stream.TryGetProperty("tags",out var tags)&&tags.TryGetProperty("rotate",out var rotate))rotation=Number(rotate.ToString());
        if(stream.TryGetProperty("side_data_list",out var sideData))foreach(var side in sideData.EnumerateArray())if(side.TryGetProperty("rotation",out rotate))rotation=Number(rotate.ToString());
        double normalized=((Math.Abs(rotation)%180)+180)%180;if(Math.Abs(normalized-90)<.01)(width,height)=(height,width);
        if(count<1)count=Math.Max(1,(int)Math.Ceiling(duration*fps-.000001));
        return new ReferenceVideoStream(ffmpeg,source,new Metadata(width,height,fps,count,duration));
    }

    public int IndexAt(double seconds)=>Math.Clamp((int)Math.Floor(Math.Clamp(seconds,0,Math.Max(0,Source.Duration-1/Source.Fps))*Source.Fps+.000001),0,Volatile.Read(ref _lastIndex));
    public Frame? Request(int index,bool prefetch=true)
    {
        CancellationTokenSource? cancel=null;Frame? frame;
        lock(_gate)
        {
            if(_disposed)return null;index=Math.Clamp(index,0,_lastIndex);_requested=index;
            _frames.TryGetValue(index,out frame);
            if(_error!=null)return frame;
            _active=true;_prefetch=prefetch;
            if(_session!=null&&frame==null&&(_anchor>index||(_next>=0&&index<_next)||index>_next+Math.Max(6,(int)Math.Ceiling(Source.Fps*.5))))cancel=_session;
        }
        try{cancel?.Cancel();}catch(ObjectDisposedException){}
        Wake();return frame;
    }

    public async Task<Frame> WaitForFrameAsync(int index,CancellationToken cancellation=default)
    {
        index=Math.Clamp(index,0,Volatile.Read(ref _lastIndex));
        Request(index);
        while(true)
        {
            Task changed;
            lock(_gate)
            {
                if(_disposed)throw new ObjectDisposedException(nameof(ReferenceVideoStream));
                if(_frames.TryGetValue(index,out var frame))return frame with{Pixels=(byte[])frame.Pixels.Clone()};
                if(_requested!=index)throw new OperationCanceledException("The requested video frame was replaced by a newer seek.");
                if(_error!=null)throw new IOException("Camera background decoding failed.",_error);
                changed=_frameChanged.Task;
            }
            await changed.WaitAsync(cancellation).ConfigureAwait(false);
        }
    }
    public Frame? CopyCachedFrame(int index)
    {
        lock(_gate)return _frames.TryGetValue(index,out var frame)?frame with{Pixels=(byte[])frame.Pixels.Clone()}:null;
    }
    public void Pause()
    {
        CancellationTokenSource? cancel;lock(_gate){_active=false;cancel=_session;}
        try{cancel?.Cancel();}catch(ObjectDisposedException){}Wake();
    }
    void Wake(){try{_changed.Release();}catch(SemaphoreFullException){}catch(ObjectDisposedException){}}
    void PublishChange()
    {
        TaskCompletionSource signal;lock(_gate){signal=_frameChanged;_frameChanged=NewSignal();}signal.TrySetResult();
    }
    int AheadFrames=>Math.Max(2,Math.Min(Capacity-3,(int)Math.Ceiling(Source.Fps*1.25)));

    async Task WorkerAsync()
    {
        try
        {
            while(!_lifetime.IsCancellationRequested)
            {
                bool wait,close;int start=0;
                lock(_gate)
                {
                    close=_process!=null&&(!_active||_session?.IsCancellationRequested==true);
                    wait=!_active;
                    if(!wait&&_process==null)
                    {
                        bool cached=_frames.ContainsKey(_requested);start=_requested;
                        if(cached){while(_frames.ContainsKey(start)&&start<=_lastIndex)start++;}
                        wait=cached&&(!_prefetch||start>Math.Min(_lastIndex,_requested+AheadFrames))||start>_lastIndex;
                    }
                    else if(!wait&&_process!=null)
                        wait=_frames.ContainsKey(_requested)&&(!_prefetch||_next>Math.Min(_lastIndex,_requested+AheadFrames));
                }
                if(close){await CloseSessionAsync().ConfigureAwait(false);continue;}
                if(wait){await _changed.WaitAsync(_lifetime.Token).ConfigureAwait(false);continue;}
                if(_process==null)Start(start);
                CancellationToken token;Process process;int index;byte[] pixels;
                lock(_gate){token=_session!.Token;process=_process!;index=_next;pixels=RentBuffer();}
                try
                {
                    int read=0;
                    while(read<pixels.Length)
                    {
                        int bytes=await process.StandardOutput.BaseStream.ReadAsync(pixels.AsMemory(read,pixels.Length-read),token).ConfigureAwait(false);
                        if(bytes==0)break;read+=bytes;
                    }
                    token.ThrowIfCancellationRequested();
                    if(read!=FrameBytes)
                    {
                        lock(_gate)
                        {
                            _spares.Enqueue(pixels);
                            if(index>_anchor){_lastIndex=Math.Min(_lastIndex,index-1);_requested=Math.Min(_requested,_lastIndex);}
                            else _error=new InvalidDataException("No decoded frame was returned at the requested video time.");
                            _active=false;
                        }
                        await CloseSessionAsync().ConfigureAwait(false);PublishChange();continue;
                    }
                    lock(_gate)
                    {
                        _frames[index]=new Frame(index,index/Source.Fps,Width,Height,pixels);_next=index+1;
                    }
                    Interlocked.Increment(ref _decoded);PublishChange();
                    if(index>=Volatile.Read(ref _lastIndex))await CloseSessionAsync().ConfigureAwait(false);
                }
                catch(OperationCanceledException)
                {
                    lock(_gate)_spares.Enqueue(pixels);
                    await CloseSessionAsync().ConfigureAwait(false);PublishChange();
                }
            }
        }
        catch(OperationCanceledException){}
        catch(Exception ex){lock(_gate){_error=ex;_active=false;}PublishChange();}
        finally{await CloseSessionAsync().ConfigureAwait(false);PublishChange();}
    }
    byte[] RentBuffer()
    {
        if(_spares.Count>0)return _spares.Dequeue();
        if(_frames.Count>=Capacity)
        {
            var behind=_frames.Keys.Where(index=>index<_requested-2).ToArray();
            int evict=behind.Length>0?behind.Min():_frames.Keys.Where(index=>index!=_requested).OrderByDescending(index=>Math.Abs(index-_requested)).First();
            var pixels=_frames[evict].Pixels;_frames.Remove(evict);return pixels;
        }
        return new byte[FrameBytes];
    }
    void Start(int index)
    {
        var session=CancellationTokenSource.CreateLinkedTokenSource(_lifetime.Token);
        var process=new Process{StartInfo=new ProcessStartInfo(_binary){UseShellExecute=false,CreateNoWindow=true,RedirectStandardOutput=true,RedirectStandardError=true}};
        string time=(index/Source.Fps).ToString("0.##########",CultureInfo.InvariantCulture),fps=Source.Fps.ToString("0.##########",CultureInfo.InvariantCulture);
        foreach(string arg in new[]{"-hide_banner","-loglevel","error","-nostdin","-threads","2","-ss",time,"-i",_source,"-an","-sn","-dn","-filter_threads","1","-vf",$"scale={Width}:{Height}:flags=fast_bilinear,setsar=1,fps={fps}","-pix_fmt","rgb24","-threads","1","-f","rawvideo","pipe:1"})process.StartInfo.ArgumentList.Add(arg);
        lock(_gate)
        {
            if(_disposed||_lifetime.IsCancellationRequested){session.Dispose();process.Dispose();throw new OperationCanceledException();}
            // Register the started child while holding the same lock as Dispose,
            // so application exit cannot miss a just-spawned decoder.
            try{process.Start();}catch{session.Dispose();process.Dispose();throw;}
            _session=session;_process=process;_anchor=index;_next=index;_error=null;
        }
        _ = process.StandardError.ReadToEndAsync();session.Token.Register(()=>QueueKill(process));
        Interlocked.Increment(ref _launches);
    }
    async Task CloseSessionAsync()
    {
        Process? process;CancellationTokenSource? session;
        lock(_gate){process=_process;session=_session;_process=null;_session=null;_next=-1;_anchor=-1;}
        if(process!=null){Kill(process);try{await process.WaitForExitAsync().ConfigureAwait(false);}catch(InvalidOperationException){}process.Dispose();}
        session?.Dispose();
    }
    static void QueueKill(Process process)=>ThreadPool.QueueUserWorkItem(_=>Kill(process));
    static void Kill(Process process){try{if(!process.HasExited)process.Kill(true);}catch(InvalidOperationException){}catch(System.ComponentModel.Win32Exception){}}
    public void Dispose()
    {
        Process? process;lock(_gate){if(_disposed)return;_disposed=true;_active=false;process=_process;_frames.Clear();_spares.Clear();}
        _lifetime.Cancel();if(process!=null)Kill(process);Wake();PublishChange();
        // The worker owns disposal of its pipe/process. Its background task cannot
        // keep the application alive; Kill above prevents an orphan decoder on exit.
    }
}
