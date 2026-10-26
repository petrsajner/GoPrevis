using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;

namespace GoPrevis;

public sealed record AiDirectorConnection(string Provider,string Model,string Key)
{
    public override string ToString()=>Provider+" / "+Model+" (credentials redacted)";
}
public sealed record AiDirectorResult(AiScenePlan Plan,int Requests,long InputTokens,long OutputTokens,decimal? EstimatedUsd,IReadOnlyList<string> Actions)
{
    public IReadOnlyList<string> VerifiedOperations {get;init;}=Array.Empty<string>();
}
public sealed partial class AiDirectorClient:IDisposable
{
    readonly HttpClient _http;
    public static readonly JsonSerializerOptions Json=new(){PropertyNamingPolicy=JsonNamingPolicy.CamelCase,PropertyNameCaseInsensitive=true};
    public AiDirectorClient(HttpMessageHandler? handler=null)=>_http=handler==null?new HttpClient{Timeout=System.Threading.Timeout.InfiniteTimeSpan}:new HttpClient(handler){Timeout=System.Threading.Timeout.InfiniteTimeSpan};
    public void Dispose()=>_http.Dispose();
    public const string Instructions="""
        You direct editable 3D film previs in GoPrevis. Understand English or Czech instructions, but write your summary in English.
        Use get_program_guide to learn the editor's optimal workflows, get_scene for current state, search_library for exact IDs and get_asset_details for real geometry and rig information. Never invent library IDs.
        Animated dog models are Husky and Shiba Inu. The built-in asset "dog" is a static prop, so use a rigged breed for animal motion.
        Finish by calling inspect_scene_plan with your full proposal. Read its computed errors, warnings and operation report. Correct errors by inspecting a revised plan. Commit ONLY by calling apply_scene_plan with the planId from a valid review. No prose answer or unreviewed plan changes the editor.
        Inspection rehearses the proposal in the real program and sends actual rendered camera frames and a side-view gait sequence. YOU MUST LOOK at those images, not merely trust direction metadata. Check limb stepping, toe/front direction, sliding, framing and occlusion. Side-view actors travel left-to-right; their grounded feet must move opposite their translation relative to the hips.
        After inspecting the pictures, call assess_preview with planId, Accept/Revise and concrete observations of the frames. Only an accepted visual preview can be committed. If visuals or measurements are wrong, revise the scene and inspect it again. Never approve backward sliding, clipped subjects or an obviously wrong composition just because the plan is structurally valid.
        Build an attractive coherent shot with restrained scene detail, meaningful actor/object animation and a useful camera.
        For large scenes, work incrementally: stage_scene_changes saves a batch of objects/cameras into a persistent uncommitted draft. Continue staging sections, inspect the draft with get_staged_scene, then inspect_staged_scene to rehearse the complete shot. Subsequent batches replace an item with the same ID, so send its complete desired definition. Nothing is lost between requests. Do not simplify away requested content just to reduce output.
        User floor-plan/side-view/reference images are part of the scene brief. Interpret their caption and view type; use the written request for timing and resolve spatial ambiguity from both.
        Units: metres, Y up, actor/camera forward -Z. Rotation XYZ is in degrees. Place figures on Y=0 unless instructed otherwise.
        IDs: existing IDs update those items; new short IDs create items. Existing object assetId="" preserves its model. Keep unrelated items when editing.
        Empty transform/color arrays mean keep current/default. New object scale defaults to its library scale; nonempty scale is absolute.
        Duration/fps=0 preserves the project value. Stay within the supplied project duration or explicitly extend it (max 600 seconds).
        width/height=0 keeps the current aspect/resolution. Set both to even pixel sizes only when the user requests a format change. sceneSettings=null keeps the normal scene setup.
        path is a list of absolute world positions and arrival times in seconds. replacePath=true replaces/clears the path. A path needs >=2 points and strictly increasing times.
        orientToPath=true follows the curve; manual base rotation adds an artistic correction, so use rotation [0,0,0] for natural facing.
        A held start/end can be made with repeated endpoint positions at different times. Use Ease in/out or Stop to accelerate/decelerate at a stop.
        Object position/rotation KEYS are additive corrections above its base/path; scale keys are multiplicative factors. Camera position/rotation keys are ABSOLUTE world transforms. Include starting and ending keys.
        replaceKeys clears old transform keys; replacePoseKeys clears old additive corrections. Leave them false to preserve existing animation. pathOffset is a constant additive XYZ path correction; [] preserves it.
        When creating a new scene, removeIds should be empty. sceneSettings.backgroundColor is RGBA [r,g,b,1] from zero to one, not a three-element RGB array.
        Easing values: Linear, Ease in/out, Curved, Stop, Hold. The easing controls the interval leaving that key.
        ALWAYS use a path for traveling characters and driving cars. Set orientToPath=true, travelFacing=Forward, rotation [0,0,0], and in-place mocap. Position/rotation keys are only small manual corrections, NEVER a walking route. Use pathTiming=Constant speed for steady travel; Smooth authored for timed acceleration, pauses and cinematic flights (continuous velocity through intermediate points); Authored for deliberately hand-shaped per-key easing. Never ease to a stop at every intermediate curve point.
        Forward walking must face its velocity. Put glances/head turns in poseKeys, not whole-body yaw. Backward/Left/Right/Fixed are explicit requested artistic exceptions. Manual and InPlace must NEVER be used just to bypass the route checks.
        Camera orbit/oblet/circle MUST use intent=Orbit and movement.mode=Orbit, an exact analytic circle. Give targetId, radius, startAngleDegrees, orbitDegrees (360 for a full orbit), heights, start/duration/easing. Use Linear for continuous angular speed, aimAtTarget=true, follow=false, replaceKeys=true, keys=[]. Do NOT approximate the orbit with any sampled position/rotation keys.
        Camera dolly/truck/crane uses intent=Dolly/Crane/Path and movement.mode=Path with timed world positions and aimAtTarget. Constant speed is the preferred pathTiming; camera travel no longer needs transform-key approximation. movement=null preserves current procedural movement; mode=None disables it before switching to Follow.
        Motions require an exact matching actor family. Static props move through paths/transform keys, never mocap.
        motion duration is timeline seconds, speed is 0.1..4, sourceIn is seconds after the catalog's source start. Loop=true repeats a short cycle for the requested duration.
        replaceMotions=true replaces the source sequence. Otherwise motions append at the explicitly given start times. Overlap adjacent clips by blendIn to crossfade.
        poseId sets a static library pose. A seated figure can use placeOnId with the supporting chair ID; GoPrevis fits the pelvis to its seat. It must have no travel path or position keys.
        poseKeys are ADDITIVE bone XYZ rotation degrees over mocap, with an identity [0,0,0] start key before the gesture. Bone="Root" instead means pelvis XYZ translation in metres. Only use exact bone names returned by get_scene; standard human names include Head, RightUpperArm, LeftUpperArm, RightLowerArm, LeftLowerArm.
        Each camera may have an optional targetId. Follow=true moves with it, LookAt=true aims at it. Use both for reliable tracking; position keys don't control Follow. Offsets are [side,height,Z] in target-local axes if relativeOffsets=true; +Z is behind a forward -Z actor. Use a side offset and enough distance to keep the complete subject in view.
        lookAtPoint sets the initial camera orientation toward a world point when target tracking is unused. Keep the camera outside models and above ground. Lens range 8..300mm; 28..50mm is a good normal shot.
        For an establishing car-follow shot prefer 10–16 metres following distance and 3–6 metres height. Put trees/buildings outside the camera's travel sweep so they do not obscure the hero.
        Camera movement.aimPath is a separate timed world-space target curve. Use it for independent pan/tilt while the camera flies along its own path; AimPath times are absolute shot seconds; use aimPathTiming=Smooth authored for a continuous pan/tilt. Keep follow=false and lookAt=false when movement owns both path and aim. Lens keys remain independent. For leashes, use a cylinder object with Link endpoints attached to a human RightHand bone and a dog Head/Body bone from get_asset_details; offsets are in target/bone space and width is metres. The runtime keeps the line connected while both targets move.
        For a static wide use intent=Static with a fixed position/lookAtPoint. Avoid changing camera rotation while live LookAt is enabled. Do not combine Follow with procedural movement; movement owns position and aimAtTarget can own its rotation.
        Light fields are only for point_light/directional_light assets. Use low intensity and keep neutral ambient fill unless dramatic lighting is requested.
        All schema fields must be present. For unused fields use "", [], false, or null as appropriate. Camera default offsets [0,2,6]/[0,0.8,0], focalLength 35, smoothness 0.
        You may submit corrections when validation returns an error. Do not claim success without a valid apply_scene_plan call.
        """;
    static readonly (string Name,string Description,Func<JsonObject> Schema)[] Tools={
        ("get_program_guide","Read the program's authoritative rules, operation choices and examples for paths, facing, smooth camera moves and animation layers.",AiToolSchema.Empty),
        ("get_scene","Read actual scene objects, cameras, geometry bounds and available bone names.",AiToolSchema.Empty),
        ("search_library","Search real library items by English keywords; kind models, motions or poses; family can be empty. Keywords are OR-matched and ranked.",AiToolSchema.Search),
        ("get_asset_details","Measure actual asset bounds, default scale, rig bones and motion compatibility before spatial placement.",()=>AiToolSchema.StringArgument("assetId")),
        ("stage_scene_changes","Persist a complete batch of item definitions in the uncommitted working draft. Use multiple batches for complex scenes; same item ID replaces its previous draft definition.",()=>AiToolSchema.For(typeof(AiScenePlan))),
        ("get_staged_scene","Read all draft sections already saved during this request.",AiToolSchema.Empty),
        ("inspect_staged_scene","Rehearse and inspect the complete saved working draft; no need to resend the entire scene.",AiToolSchema.Empty),
        ("inspect_scene_plan","Propose the complete shot. The program computes semantic checks for travel, facing and camera motion. A valid review returns a planId; invalid plans cannot be committed.",()=>AiToolSchema.For(typeof(AiScenePlan))),
        ("assess_preview","Inspect the actual rendered preview images and gait measurements. Accept only a visually coherent result; otherwise revise and inspect a corrected proposal.",AiToolSchema.VisualAssessment),
        ("apply_scene_plan","Commit a previously inspected valid proposal by its returned planId. The whole change is one Undo step.",()=>AiToolSchema.StringArgument("planId"))};

    static JsonObject ParseArguments(string text)=>JsonNode.Parse(text)?.AsObject()??throw new FormatException("The model returned invalid function arguments.");
    static HttpRequestMessage BuildRequest(AiDirectorConnection c,JsonArray chat,JsonArray responses,JsonArray gemini,int? completionMaximum)
    {
        JsonObject body;string url;
        if(c.Provider=="openai")
        {
            url="https://api.openai.com/v1/responses";
            body=new(){["model"]=c.Model,["instructions"]=Instructions,["input"]=responses.DeepClone(),["store"]=false,["reasoning"]=new JsonObject{["effort"]="high"},["include"]=new JsonArray("reasoning.encrypted_content"),["tools"]=new JsonArray(Tools.Select(t=>(JsonNode)new JsonObject{["type"]="function",["name"]=t.Name,["description"]=t.Description,["strict"]=true,["parameters"]=t.Schema()}).ToArray())};
        }
        else if(c.Provider=="gemini")
        {
            url="https://generativelanguage.googleapis.com/v1beta/models/"+Uri.EscapeDataString(c.Model)+":generateContent";
            body=new(){["systemInstruction"]=new JsonObject{["parts"]=new JsonArray(new JsonObject{["text"]=Instructions})},["contents"]=gemini.DeepClone(),["generationConfig"]=new JsonObject{["thinkingConfig"]=new JsonObject{["thinkingLevel"]="HIGH"}},["tools"]=new JsonArray(new JsonObject{["functionDeclarations"]=new JsonArray(Tools.Select(t=>(JsonNode)new JsonObject{["name"]=t.Name,["description"]=t.Description,["parametersJsonSchema"]=t.Schema()}).ToArray())})};
        }
        else
        {
            url="https://openrouter.ai/api/v1/chat/completions";
            body=new(){["model"]=c.Model,["messages"]=chat.DeepClone(),["reasoning"]=new JsonObject{["effort"]="high"},["tools"]=new JsonArray(Tools.Select(t=>(JsonNode)new JsonObject{["type"]="function",["function"]=new JsonObject{["name"]=t.Name,["description"]=t.Description,["strict"]=true,["parameters"]=t.Schema()}}).ToArray())};
        }
        if(completionMaximum.HasValue){if(c.Provider=="gemini")body["generationConfig"]!["maxOutputTokens"]=completionMaximum.Value;else body[c.Provider=="openai"?"max_output_tokens":"max_tokens"]=completionMaximum.Value;}
        var request=new HttpRequestMessage(HttpMethod.Post,url){Content=new StringContent(body.ToJsonString(),Encoding.UTF8,"application/json")};
        if(c.Provider=="gemini")request.Headers.Add("x-goog-api-key",c.Key);else request.Headers.Authorization=new AuthenticationHeaderValue("Bearer",c.Key);
        if(c.Provider=="openrouter"){request.Headers.Add("HTTP-Referer","https://github.com/petrsajner/GoPrevis");request.Headers.Add("X-Title","GoPrevis");}
        return request;
    }
    static (long Input,long Output,decimal? Cost) Usage(AiDirectorConnection c,JsonObject root)
    {
        var usage=root[c.Provider=="gemini"?"usageMetadata":"usage"];
        long Get(string name)=>usage?[name]?.GetValue<long>()??0;
        long input=c.Provider=="gemini"?Get("promptTokenCount"):c.Provider=="openai"?Get("input_tokens"):Get("prompt_tokens");
        long output=c.Provider=="gemini"?Get("candidatesTokenCount")+Get("thoughtsTokenCount"):c.Provider=="openai"?Get("output_tokens"):Get("completion_tokens");
        decimal? cost=usage?["cost"]?.GetValue<decimal>();
        if(cost==null&&c.Provider=="gemini"&&c.Model=="gemini-3.8-flash")cost=(input*.75m+output*3.75m)*(DateTime.UtcNow.Year>=2027?2:1)/1_000_000;
        if(cost==null&&c.Model is "gpt-6-luna" or "openai/gpt-6-luna")cost=(input*.1m+output*.5m)/1_000_000;
        return(input,output,cost);
    }
    static string SafeError(AiDirectorConnection c,string body,int status)
    {
        string message="The provider could not complete the request.";
        try{message=JsonNode.Parse(body)?["error"]?["message"]?.GetValue<string>()??message;}catch(JsonException){}
        message=message.Replace(c.Key,"[redacted]",StringComparison.Ordinal);message=Regex.Replace(message,@"(?:sk-|AIza)[A-Za-z0-9_\-]{12,}","[redacted]");
        if(message.Length>450)message=message[..450];
        return c.Provider+" HTTP "+status+": "+message;
    }
}
