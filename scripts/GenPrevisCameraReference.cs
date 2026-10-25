using Godot;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace GenPrevis;

public partial class GenPrevisApp
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
    string _referenceLoadedPath="",_referenceLoadedCameraId="";
    double _referenceDuration,_referenceSourceFps=24,_referenceRequestedTime=-1,_referenceDisplayedTime=-1;
    int _referenceGeneration;
    ReferenceVideoStream? _referenceVideo;
    ReferenceVideoStream.Frame? _referenceCurrentFrame;
    readonly Dictionary<string,(ReferenceVideoStream Stream,long Used)> _referenceVideos=new();
    long _referenceVideoUse;
    Task _referenceLoadTask=Task.CompletedTask;

    Task LoadCameraReferenceAsync()
    {
        _referenceLoadTask=LoadCameraReferenceCoreAsync();return _referenceLoadTask;
    }
    static string ReferenceSourceKey(string path)
    {
        var file=new FileInfo(path);return Path.GetFullPath(path)+"|"+file.Length+"|"+file.LastWriteTimeUtc.Ticks;
    }
    async Task LoadCameraReferenceCoreAsync()
    {
        _referenceCancellation?.Cancel();_referenceCancellation?.Dispose();_referenceCancellation=new();var cancellation=_referenceCancellation.Token;int generation=++_referenceGeneration;
        var previous=_referenceVideo;_referenceVideo=null;_referenceCurrentFrame=null;
        _cameraReferenceTexture?.Dispose();_cameraReferenceTexture=null;_referenceLoadedPath=ActiveCameraReference.Path;_referenceLoadedCameraId=ActiveReferenceCamera?.Id??"";
        _referenceRequestedTime=_referenceDisplayedTime=-1;_referenceDuration=0;_referenceIsVideo=false;_referenceDecoding=false;
        RedrawCameraReference();
        try
        {
            if(string.IsNullOrEmpty(_referenceLoadedPath)||_referenceDisposed){previous?.Pause();return;}
            if(!File.Exists(_referenceLoadedPath)){previous?.Pause();throw new FileNotFoundException("Camera reference is missing: "+Path.GetFileName(_referenceLoadedPath));}
            string extension=Path.GetExtension(_referenceLoadedPath).ToLowerInvariant();_referenceIsVideo=extension is ".mp4" or ".mov" or ".mkv" or ".webm" or ".avi" or ".m4v" or ".ogv";
            if(_referenceIsVideo)
            {
                Status("Loading camera video · "+Path.GetFileName(_referenceLoadedPath)+"…");string key=ReferenceSourceKey(_referenceLoadedPath);
                ReferenceVideoStream stream;
                if(_referenceVideos.TryGetValue(key,out var cached)&&cached.Stream.Error==null)stream=cached.Stream;
                else
                {
                    if(cached.Stream!=null){cached.Stream.Dispose();_referenceVideos.Remove(key);}
                    previous?.Pause();stream=await ReferenceVideoStream.OpenAsync(ShotVideoEncoder.FindBinary("ffmpeg"),ShotVideoEncoder.FindBinary("ffprobe"),_referenceLoadedPath,cancellation);
                    if(generation!=_referenceGeneration||_referenceDisposed){stream.Dispose();return;}
                    _referenceVideos[key]=(stream,++_referenceVideoUse);
                    while(_referenceVideos.Count>2)
                    {
                        var oldest=_referenceVideos.Where(pair=>pair.Key!=key).OrderBy(pair=>pair.Value.Used).First();oldest.Value.Stream.Dispose();_referenceVideos.Remove(oldest.Key);
                    }
                }
                if(previous!=stream)previous?.Pause();_referenceVideos[key]=(stream,++_referenceVideoUse);_referenceVideo=stream;
                _referenceDuration=stream.Source.Duration;_referenceSourceFps=stream.Source.Fps;_referenceRequestedTime=ReferenceSampleTime();_referenceDecoding=true;
                while(generation==_referenceGeneration&&!_referenceDisposed)
                {
                    cancellation.ThrowIfCancellationRequested();
                    if(_exportBusy||!ActiveCameraReference.Enabled){stream.Pause();await ToSignal(GetTree(),SceneTree.SignalName.ProcessFrame);continue;}
                    _referenceRequestedTime=ReferenceSampleTime();
                    var frame=stream.Request(stream.IndexAt(_referenceRequestedTime));
                    if(frame!=null){DisplayReferenceVideoFrame(frame);break;}
                    if(stream.Error is {} error)throw new IOException("Camera video decoding failed.",error);
                    await ToSignal(GetTree(),SceneTree.SignalName.ProcessFrame);
                }
            }
            else
            {
                previous?.Pause();using var image=Image.LoadFromFile(_referenceLoadedPath);if(image==null||image.IsEmpty())throw new InvalidDataException("The reference image could not be read.");
                _cameraReferenceTexture=ImageTexture.CreateFromImage(image);RedrawCameraReference();
            }
            if(generation==_referenceGeneration&&!_referenceDisposed)Status("Camera background loaded · "+Path.GetFileName(_referenceLoadedPath)+" · editor only.");
        }
        catch(OperationCanceledException){}
        catch(Exception ex){if(generation==_referenceGeneration&&!_referenceDisposed){_referenceDecoding=false;Status(ex.Message);}}
    }
    double ReferenceSampleTime()
    {
        double seconds=Math.Clamp(_time+ActiveCameraReference.OffsetSeconds,0,Math.Max(0,_referenceDuration-1.0/_referenceSourceFps));
        return _referenceVideo is {} video?video.IndexAt(seconds)/_referenceSourceFps:Math.Floor(seconds*_referenceSourceFps+.000001)/_referenceSourceFps;
    }
    void UpdateCameraReference()
    {
        if(_referenceDisposed)return;
        if(_referenceLoadedCameraId!=(ActiveReferenceCamera?.Id??"")||_referenceLoadedPath!=ActiveCameraReference.Path){_=LoadCameraReferenceAsync();RefreshCameraBackgroundControls();return;}
        UpdateReferenceEnvironment();
        if(_exportBusy||!ActiveCameraReference.Enabled){_referenceVideo?.Pause();return;}
        if(!_referenceIsVideo||_referenceVideo is not {} video)return;
        _referenceRequestedTime=ReferenceSampleTime();int index=video.IndexAt(_referenceRequestedTime);var frame=video.Request(index);
        _referenceDecoding=frame==null;
        if(frame==null&&video.Error is {} error){_referenceDecoding=false;Status("Camera video: "+error.Message);return;}
        if(frame!=null&&(_referenceCurrentFrame?.Index!=frame.Index||_cameraReferenceTexture==null))DisplayReferenceVideoFrame(frame);
    }
    void DisplayReferenceVideoFrame(ReferenceVideoStream.Frame frame)
    {
        using var image=Image.CreateFromData(frame.Width,frame.Height,false,Image.Format.Rgb8,frame.Pixels);
        if(_cameraReferenceTexture==null)_cameraReferenceTexture=ImageTexture.CreateFromImage(image);
        else if(_cameraReferenceTexture.GetWidth()==frame.Width&&_cameraReferenceTexture.GetHeight()==frame.Height)_cameraReferenceTexture.Update(image);
        else _cameraReferenceTexture.SetImage(image);
        _referenceCurrentFrame=frame;_referenceDisplayedTime=frame.Time;_referenceDecoding=false;RedrawCameraReference();
    }
    Image CurrentReferenceVideoImage()
    {
        var shown=_referenceCurrentFrame??throw new InvalidOperationException("No camera video frame is displayed.");
        var frame=_referenceVideo?.CopyCachedFrame(shown.Index)??throw new InvalidOperationException("The displayed frame is no longer in the decoder cache.");
        return Image.CreateFromData(frame.Width,frame.Height,false,Image.Format.Rgb8,frame.Pixels);
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
        _cameraReferenceTexture?.Dispose();_cameraReferenceTexture=null;_referenceCurrentFrame=null;foreach(var cached in _referenceVideos.Values)cached.Stream.Dispose();_referenceVideos.Clear();_referenceVideo=null;
        if(_cameraReferencePlane!=null&&GodotObject.IsInstanceValid(_cameraReferencePlane))_cameraReferencePlane.QueueFree();_cameraReferencePlane=null;_cameraReferenceMaterial?.Dispose();_cameraReferenceMaterial=null;
    }
}
