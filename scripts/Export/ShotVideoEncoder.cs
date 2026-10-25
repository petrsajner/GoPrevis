using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Godot;

namespace GenPrevis;

public sealed record VideoProbeInfo
{
    public string Codec { get; init; } = "";
    public int Width { get; init; }
    public int Height { get; init; }
    public double Fps { get; init; }
    public int FrameCount { get; init; }
    public double DurationSeconds { get; init; }
}

/// <summary>Runs the separately bundled command-line tools; no FFmpeg libraries are linked into GenPrevis.</summary>
public static class ShotVideoEncoder
{
    public static string FindBinary(string name)
    {
        if(name is not ("ffmpeg" or "ffprobe"))throw new ArgumentException("Unknown video tool.",nameof(name));
        string file=name+(OperatingSystem.IsWindows()?".exe":"");
        string executableDirectory=Path.GetDirectoryName(System.Environment.ProcessPath??"")??AppContext.BaseDirectory;
        string[] candidates=
        {
            Path.Combine(executableDirectory,"tools","ffmpeg",file),
            Path.Combine(AppContext.BaseDirectory,"tools","ffmpeg",file),
            Path.GetFullPath(Path.Combine(AppContext.BaseDirectory,"..","tools","ffmpeg",file)),
            Path.Combine(Directory.GetCurrentDirectory(),".tools","ffmpeg",file)
        };
        string? found=candidates.FirstOrDefault(File.Exists);if(found!=null)return found;
        // Only the editor-project fallback needs an engine API. Normal exported
        // and source runs stay entirely in .NET, including worker-thread probes.
        string projectPath=ProjectSettings.GlobalizePath("res://.tools/ffmpeg/"+file);
        return File.Exists(projectPath)?projectPath:throw new FileNotFoundException("The bundled "+name+" tool is missing. Use the complete GenPrevis package for MP4 export.");
    }

    /// <summary>Encodes zero-based 000000.png frames into an atomic, H.264/yuv420p MP4.</summary>
    public static async Task EncodeAsync(string framesDirectory,string outputPath,int fps,int width,int height,
        Action<double>? progress=null,CancellationToken cancellationToken=default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        framesDirectory=Path.GetFullPath(framesDirectory);outputPath=Path.GetFullPath(outputPath);
        if(fps<1||fps>240)throw new ArgumentOutOfRangeException(nameof(fps),"MP4 frame rate must be between 1 and 240.");
        if(width<2||height<2||width>16384||height>16384||(width&1)!=0||(height&1)!=0)throw new ArgumentException("H.264 export needs positive even image dimensions.");
        if(!Directory.Exists(framesDirectory))throw new DirectoryNotFoundException("The rendered frame directory is missing.");
        if(!Path.GetExtension(outputPath).Equals(".mp4",StringComparison.OrdinalIgnoreCase))throw new ArgumentException("Choose an MP4 output filename.");
        if(File.Exists(outputPath))throw new IOException("The MP4 output already exists; choose a new export directory.");
        var frames=Directory.EnumerateFiles(framesDirectory,"*.png").Where(path=>
        {
            string stem=Path.GetFileNameWithoutExtension(path);return stem.Length==6&&stem.All(char.IsAsciiDigit);
        }).ToArray();
        if(frames.Length==0)throw new InvalidDataException("No zero-based six-digit PNG frames were rendered.");
        for(int frame=0;frame<frames.Length;frame++)
        {
            string path=Path.Combine(framesDirectory,frame.ToString("D6",CultureInfo.InvariantCulture)+".png");
            if(!File.Exists(path)||new FileInfo(path).Length==0)throw new InvalidDataException("The rendered PNG sequence has a missing or empty frame: "+frame+".");
        }
        ValidatePngDimensions(Path.Combine(framesDirectory,"000000.png"),width,height);
        Directory.CreateDirectory(Path.GetDirectoryName(outputPath)!);
        string temporary=Path.Combine(Path.GetDirectoryName(outputPath)!,Path.GetFileNameWithoutExtension(outputPath)+".partial-"+Guid.NewGuid().ToString("N")+".mp4");
        var context=SynchronizationContext.Current;
        void Report(double value)
        {
            if(progress==null)return;value=Math.Clamp(value,0,1);
            if(context==null)progress(value);else context.Post(_=>progress(value),null);
        }
        try
        {
            Report(0);
            string pattern=Path.Combine(framesDirectory.Replace("%","%%",StringComparison.Ordinal),"%06d.png");
            string[] arguments={"-hide_banner","-loglevel","warning","-nostdin","-n","-framerate",fps.ToString(CultureInfo.InvariantCulture),"-start_number","0","-i",pattern,"-frames:v",frames.Length.ToString(CultureInfo.InvariantCulture),"-an","-vf","scale=in_range=full:out_range=tv:out_color_matrix=bt709","-c:v","libx264","-preset","fast","-crf","18","-pix_fmt","yuv420p","-color_range","tv","-color_primaries","bt709","-color_trc","bt709","-colorspace","bt709","-movflags","+faststart","-progress","pipe:1","-nostats",temporary};
            var result=await RunProcessAsync(FindBinary("ffmpeg"),arguments,line=>
            {
                int separator=line.IndexOf('=');if(separator<=0)return;
                if(line[..separator]=="frame"&&int.TryParse(line[(separator+1)..].Trim(),NumberStyles.Integer,CultureInfo.InvariantCulture,out int complete))Report(complete/(double)frames.Length);
            },cancellationToken).ConfigureAwait(false);
            if(result.ExitCode!=0)throw new IOException("MP4 encoding failed (FFmpeg exit "+result.ExitCode+"). "+result.Error.Trim());
            if(!File.Exists(temporary)||new FileInfo(temporary).Length<128)throw new IOException("The encoder did not create a valid MP4 file.");
            var probe=await ProbeAsync(temporary,cancellationToken).ConfigureAwait(false);
            if(probe.Codec!="h264"||probe.Width!=width||probe.Height!=height||probe.FrameCount!=frames.Length||Math.Abs(probe.Fps-fps)>.001||Math.Abs(probe.DurationSeconds-frames.Length/(double)fps)>Math.Max(.02,1.0/fps))
                throw new InvalidDataException("The encoded MP4 does not match the rendered size, timing or frame count.");
            cancellationToken.ThrowIfCancellationRequested();File.Move(temporary,outputPath,false);Report(1);
        }
        finally{if(File.Exists(temporary))File.Delete(temporary);}
    }

    public static async Task<VideoProbeInfo> ProbeAsync(string file,CancellationToken cancellationToken=default)
    {
        file=Path.GetFullPath(file);if(!File.Exists(file))throw new FileNotFoundException("The encoded video is missing.",file);
        string[] arguments={"-v","error","-select_streams","v:0","-show_entries","stream=codec_name,width,height,r_frame_rate,avg_frame_rate,nb_frames,duration:format=duration","-of","json",file};
        var result=await RunProcessAsync(FindBinary("ffprobe"),arguments,null,cancellationToken).ConfigureAwait(false);
        if(result.ExitCode!=0)throw new InvalidDataException("The encoded video could not be verified. "+result.Error.Trim());
        using var document=JsonDocument.Parse(result.Output);var root=document.RootElement;
        if(!root.TryGetProperty("streams",out var streams)||streams.GetArrayLength()!=1)throw new InvalidDataException("The MP4 has no readable video stream.");
        var stream=streams[0];string Text(string field)=>stream.TryGetProperty(field,out var value)?value.ToString():"";
        static int Integer(string value)=>int.TryParse(value,NumberStyles.Integer,CultureInfo.InvariantCulture,out var number)?number:0;
        static double Number(string value)=>double.TryParse(value,NumberStyles.Float,CultureInfo.InvariantCulture,out var number)&&double.IsFinite(number)?number:0;
        static double Rate(string value){var parts=value.Split('/');return parts.Length==2&&Number(parts[1])>0?Number(parts[0])/Number(parts[1]):Number(value);}
        double fps=Rate(Text("avg_frame_rate"));if(fps<=0)fps=Rate(Text("r_frame_rate"));double duration=Number(Text("duration"));
        if(duration<=0&&root.TryGetProperty("format",out var format)&&format.TryGetProperty("duration",out var formatDuration))duration=Number(formatDuration.ToString());
        var info=new VideoProbeInfo{Codec=Text("codec_name"),Width=Integer(Text("width")),Height=Integer(Text("height")),Fps=fps,FrameCount=Integer(Text("nb_frames")),DurationSeconds=duration};
        if(info.Width<1||info.Height<1||info.Fps<=0||info.FrameCount<1||info.DurationSeconds<=0)throw new InvalidDataException("The video stream has incomplete size or timing metadata.");
        return info;
    }

    static void ValidatePngDimensions(string path,int width,int height)
    {
        Span<byte> header=stackalloc byte[24];using var stream=File.OpenRead(path);stream.ReadExactly(header);
        ReadOnlySpan<byte> signature=new byte[]{137,80,78,71,13,10,26,10};
        int w=System.Buffers.Binary.BinaryPrimitives.ReadInt32BigEndian(header[16..20]),h=System.Buffers.Binary.BinaryPrimitives.ReadInt32BigEndian(header[20..24]);
        if(!header[..8].SequenceEqual(signature)||w!=width||h!=height)throw new InvalidDataException("Rendered PNG dimensions do not match the requested MP4 size.");
    }
    sealed record ProcessResult(int ExitCode,string Output,string Error);
    static async Task<ProcessResult> RunProcessAsync(string executable,IEnumerable<string> arguments,Action<string>? outputLine,CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        var start=new ProcessStartInfo(executable){UseShellExecute=false,CreateNoWindow=true,RedirectStandardOutput=true,RedirectStandardError=true};foreach(string argument in arguments)start.ArgumentList.Add(argument);
        using var process=new Process{StartInfo=start};if(!process.Start())throw new IOException("The bundled video tool could not be started.");
        static void Kill(Process child){try{if(!child.HasExited)child.Kill(true);}catch(InvalidOperationException){}catch(System.ComponentModel.Win32Exception){}}
        using var cancel=token.Register(()=>Kill(process));
        var stdout=ReadBoundedAsync(process.StandardOutput,outputLine);var stderr=ReadBoundedAsync(process.StandardError,null);
        try{await process.WaitForExitAsync(token).ConfigureAwait(false);string output=await stdout.ConfigureAwait(false),error=await stderr.ConfigureAwait(false);token.ThrowIfCancellationRequested();return new ProcessResult(process.ExitCode,output,error);}
        catch(OperationCanceledException){Kill(process);try{await process.WaitForExitAsync().ConfigureAwait(false);}catch(InvalidOperationException){}await Task.WhenAll(stdout,stderr).ConfigureAwait(false);throw;}
    }
    static async Task<string> ReadBoundedAsync(StreamReader reader,Action<string>? lineAction)
    {
        var text=new StringBuilder();while(await reader.ReadLineAsync().ConfigureAwait(false) is {} line)
        {
            lineAction?.Invoke(line);text.AppendLine(line);if(text.Length>65536)text.Remove(0,text.Length-65536);
        }
        return text.ToString();
    }
}
