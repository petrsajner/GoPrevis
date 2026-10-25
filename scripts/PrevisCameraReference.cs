using Godot;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace Previs;

public partial class PrevisApp
{
    const uint CameraReferenceLayer=4;
    static readonly CameraReferenceData EmptyCameraReference=new();
    CameraEntry? ActiveReferenceCamera=>_cameras.FirstOrDefault(camera=>camera.Rig==_shot);
    CameraReferenceData ActiveCameraReference=>ActiveReferenceCamera?.Reference??EmptyCameraReference;
    MeshInstance3D? _cameraReferencePlane;
    ShaderMaterial? _cameraReferenceMaterial;
    ImageTexture? _cameraReferenceTexture;
    CancellationTokenSource? _referenceCancellation;
    bool _referenceIsVideo,_referenceDecoding,_referenceDisposed;
    string _referenceLoadedPath="",_referenceLoadedCameraId="",_referenceBinary="";
    double _referenceDuration,_referenceSourceFps=24,_referenceRequestedTime=-1,_referenceDisplayedTime=-1;
    int _referenceGeneration,_referenceDecoderGeneration=-1;
    readonly Dictionary<long,byte[]> _referenceFrames=new();
    readonly Queue<long> _referenceFrameOrder=new();
    Task _referenceLoadTask=Task.CompletedTask;

    Task LoadCameraReferenceAsync()
    {
        _referenceLoadTask=LoadCameraReferenceCoreAsync();return _referenceLoadTask;
    }
    async Task LoadCameraReferenceCoreAsync()
    {
        _referenceCancellation?.Cancel();_referenceCancellation?.Dispose();_referenceCancellation=new();var cancellation=_referenceCancellation.Token;int generation=++_referenceGeneration;
        _referenceFrames.Clear();_referenceFrameOrder.Clear();_cameraReferenceTexture?.Dispose();_cameraReferenceTexture=null;_referenceLoadedPath=ActiveCameraReference.Path;_referenceLoadedCameraId=ActiveReferenceCamera?.Id??"";
        _referenceRequestedTime=_referenceDisplayedTime=-1;_referenceDuration=0;_referenceIsVideo=false;
        RedrawCameraReference();
        try
        {
            if(string.IsNullOrEmpty(_referenceLoadedPath)||_referenceDisposed){RedrawCameraReference();return;}
            if(!File.Exists(_referenceLoadedPath))throw new FileNotFoundException("Camera reference is missing: "+Path.GetFileName(_referenceLoadedPath));
            Status("Loading camera reference · "+Path.GetFileName(_referenceLoadedPath)+"…");
            string extension=Path.GetExtension(_referenceLoadedPath).ToLowerInvariant();_referenceIsVideo=extension is ".mp4" or ".mov" or ".mkv" or ".webm" or ".avi" or ".m4v" or ".ogv";
            if(_referenceIsVideo)
            {
                _referenceBinary=ShotVideoEncoder.FindBinary("ffmpeg");
                var probe=await ShotVideoEncoder.ProbeAsync(_referenceLoadedPath,cancellation);
                if(generation!=_referenceGeneration||_referenceDisposed)return;
                _referenceDuration=probe.DurationSeconds;_referenceSourceFps=double.IsFinite(probe.Fps)&&probe.Fps>0?probe.Fps:24;if(_referenceDuration<=0)throw new InvalidDataException("The reference video has no readable duration.");
                _referenceRequestedTime=ReferenceSampleTime();await DecodeReferenceFramesAsync(generation,cancellation);
            }
            else
            {
                using var image=Image.LoadFromFile(_referenceLoadedPath);if(image==null||image.IsEmpty())throw new InvalidDataException("The reference image could not be read.");
                _cameraReferenceTexture=ImageTexture.CreateFromImage(image);RedrawCameraReference();
            }
            if(generation==_referenceGeneration&&!_referenceDisposed)Status("Camera reference loaded · "+Path.GetFileName(_referenceLoadedPath)+" · editor only.");
        }
        catch(OperationCanceledException){}
        catch(Exception ex){if(generation==_referenceGeneration&&!_referenceDisposed)Status(ex.Message);}
    }
    double ReferenceSampleTime()
    {
        double time=Math.Clamp(_time+ActiveCameraReference.OffsetSeconds,0,Math.Max(0,_referenceDuration-1.0/_referenceSourceFps));
        return Math.Floor(time*_referenceSourceFps+.000001)/_referenceSourceFps;
    }
    void UpdateCameraReference()
    {
        if(_referenceDisposed)return;
        if(_referenceLoadedCameraId!=(ActiveReferenceCamera?.Id??"")||_referenceLoadedPath!=ActiveCameraReference.Path){_=LoadCameraReferenceAsync();RefreshCameraBackgroundControls();return;}
        UpdateReferenceEnvironment();RedrawCameraReference();
        if(_exportBusy)return;
        if(!_referenceIsVideo||_referenceDuration<=0||!ActiveCameraReference.Enabled||_referenceLoadedPath!=ActiveCameraReference.Path)return;
        _referenceRequestedTime=ReferenceSampleTime();
        if(!_referenceDecoding&&Math.Abs(_referenceRequestedTime-_referenceDisplayedTime)>.000001)
            _referenceLoadTask=DecodeReferenceFramesAsync(_referenceGeneration,_referenceCancellation?.Token??CancellationToken.None);
    }
    async Task DecodeReferenceFramesAsync(int generation,CancellationToken cancellation)
    {
        if(_referenceDecoding&&_referenceDecoderGeneration==generation)return;_referenceDecoding=true;_referenceDecoderGeneration=generation;
        try
        {
            while(generation==_referenceGeneration&&!_referenceDisposed)
            {
                if(_exportBusy)break;
                cancellation.ThrowIfCancellationRequested();double time=_referenceRequestedTime;long key=(long)Math.Round(time*1000000);
                if(!_referenceFrames.TryGetValue(key,out var bytes))
                {
                    string source=_referenceLoadedPath,binary=_referenceBinary;
                    bytes=await Task.Run(()=>DecodeReferenceFrameAsync(binary,source,time,cancellation),cancellation);
                    if(generation!=_referenceGeneration||_referenceDisposed)return;
                    if(_referenceFrames.Count>=24){long old=_referenceFrameOrder.Dequeue();_referenceFrames.Remove(old);}
                    _referenceFrames[key]=bytes;_referenceFrameOrder.Enqueue(key);
                }
                using var image=new Image();var result=image.LoadPngFromBuffer(bytes);if(result!=Godot.Error.Ok)throw new InvalidDataException("The reference video frame could not be decoded.");
                if(_cameraReferenceTexture==null)_cameraReferenceTexture=ImageTexture.CreateFromImage(image);else _cameraReferenceTexture.SetImage(image);
                _referenceDisplayedTime=time;RedrawCameraReference();
                if(Math.Abs(ReferenceSampleTime()-time)<=.000001)break;
                _referenceRequestedTime=ReferenceSampleTime();
            }
        }
        catch(OperationCanceledException){}
        catch(Exception ex){if(generation==_referenceGeneration&&!_referenceDisposed)Status("Reference video: "+ex.Message);}
        finally{if(_referenceDecoderGeneration==generation){_referenceDecoding=false;_referenceDecoderGeneration=-1;}if(generation!=_referenceGeneration&&!_referenceDisposed&&_referenceIsVideo&&_referenceDuration>0)UpdateCameraReference();}
    }
    static async Task<byte[]> DecodeReferenceFrameAsync(string binary,string source,double time,CancellationToken cancellation)
    {
        using var process=new Process{StartInfo=new ProcessStartInfo{FileName=binary,UseShellExecute=false,CreateNoWindow=true,RedirectStandardOutput=true,RedirectStandardError=true}};
        foreach(string argument in new[]{"-hide_banner","-loglevel","error","-nostdin","-ss",time.ToString("0.######",CultureInfo.InvariantCulture),"-i",source,"-frames:v","1","-an","-vf","scale=960:960:force_original_aspect_ratio=decrease","-f","image2pipe","-vcodec","png","pipe:1"})process.StartInfo.ArgumentList.Add(argument);
        process.Start();using var registration=cancellation.Register(()=>{try{if(!process.HasExited)process.Kill(true);}catch(InvalidOperationException){}});
        using var bytes=new MemoryStream();Task errorTask=process.StandardError.ReadToEndAsync(cancellation);
        await process.StandardOutput.BaseStream.CopyToAsync(bytes,cancellation);await process.WaitForExitAsync(cancellation);await errorTask;cancellation.ThrowIfCancellationRequested();
        if(process.ExitCode!=0||bytes.Length==0)throw new InvalidDataException("No frame was returned at "+time.ToString("0.###",CultureInfo.InvariantCulture)+" s.");
        return bytes.ToArray();
    }
    void UpdateReferenceEnvironment()
    {
        if(_previewCamera==null||_editorCamera==null||_lighting==null)return;
        bool enabled=_referenceLoadedCameraId==(ActiveReferenceCamera?.Id??"")&&_referenceLoadedPath==ActiveCameraReference.Path&&ActiveCameraReference.Enabled&&_cameraReferenceTexture!=null&&ActiveCameraReference.Opacity>0;
        if(enabled&&_cameraReferencePlane==null)
        {
            _cameraReferenceMaterial=new ShaderMaterial{Shader=new Shader{Code="""
                shader_type spatial;
                render_mode unshaded, fog_disabled, shadows_disabled, cull_disabled;
                uniform sampler2D reference_image : source_color;
                uniform vec4 background_color : source_color;
                uniform float image_aspect = 1.0;
                uniform float frame_aspect = 1.0;
                uniform float opacity = 1.0;
                uniform bool fill = false;
                void fragment() {
                    vec2 uv=UV;
                    if(fill) {
                        if(image_aspect>frame_aspect)uv.x=(uv.x-.5)*frame_aspect/image_aspect+.5;
                        else uv.y=(uv.y-.5)*image_aspect/frame_aspect+.5;
                    } else {
                        if(image_aspect>frame_aspect)uv.y=(uv.y-.5)*image_aspect/frame_aspect+.5;
                        else uv.x=(uv.x-.5)*frame_aspect/image_aspect+.5;
                    }
                    vec4 image=texture(reference_image,uv);
                    float inside=float(uv.x>=0.0&&uv.x<=1.0&&uv.y>=0.0&&uv.y<=1.0);
                    ALBEDO=mix(background_color.rgb,image.rgb,inside*image.a*opacity);
                }
                """}};
            _cameraReferencePlane=new MeshInstance3D{Name="EditorCameraReference",Layers=CameraReferenceLayer,Mesh=new QuadMesh(),MaterialOverride=_cameraReferenceMaterial,CastShadow=GeometryInstance3D.ShadowCastingSetting.Off,ExtraCullMargin=2,IgnoreOcclusionCulling=true};
            _world.AddChild(_cameraReferencePlane);
        }
        _previewCamera.CullMask=enabled?1|CameraReferenceLayer:1;
        _editorCamera.CullMask=_pilot?(enabled?1|CameraReferenceLayer:1):3;
        if(_cameraReferencePlane==null)return;_cameraReferencePlane.Visible=enabled;
        if(!enabled)return;
        float distance=Math.Min(_previewCamera.Far,_shot.ShotCamera.Far)-1;
        float width=2*distance*Mathf.Tan(Mathf.DegToRad(_shot.ShotCamera.Fov)*.5f);
        ((QuadMesh)_cameraReferencePlane.Mesh).Size=new Vector2(width,width/ProjectAspect);
        _cameraReferencePlane.GlobalTransform=new Transform3D(_shot.ShotCamera.GlobalBasis.Orthonormalized(),_shot.ShotCamera.GlobalPosition-_shot.ShotCamera.GlobalBasis.Orthonormalized().Z*distance);
        var background=_sceneSettings.BackgroundColor;
        _cameraReferenceMaterial!.SetShaderParameter("reference_image",_cameraReferenceTexture!);_cameraReferenceMaterial.SetShaderParameter("background_color",new Color(background[0],background[1],background[2],1));
        _cameraReferenceMaterial.SetShaderParameter("image_aspect",_cameraReferenceTexture!.GetWidth()/(float)_cameraReferenceTexture.GetHeight());_cameraReferenceMaterial.SetShaderParameter("frame_aspect",ProjectAspect);_cameraReferenceMaterial.SetShaderParameter("opacity",ActiveCameraReference.Opacity);_cameraReferenceMaterial.SetShaderParameter("fill",ActiveCameraReference.Fill);
    }
    void RedrawCameraReference()=>UpdateReferenceEnvironment();
    void DisposeCameraReference()
    {
        _referenceDisposed=true;++_referenceGeneration;_referenceCancellation?.Cancel();_referenceCancellation?.Dispose();_referenceCancellation=null;
        _cameraReferenceTexture?.Dispose();_cameraReferenceTexture=null;_referenceFrames.Clear();_referenceFrameOrder.Clear();
        if(_cameraReferencePlane!=null&&GodotObject.IsInstanceValid(_cameraReferencePlane))_cameraReferencePlane.QueueFree();_cameraReferencePlane=null;_cameraReferenceMaterial?.Dispose();_cameraReferenceMaterial=null;
    }
}
