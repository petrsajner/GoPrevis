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

namespace GenPrevis;

public sealed record AiDirectorConnection(string Provider,string Model,string Key)
{
    public override string ToString()=>Provider+" / "+Model+" (credentials redacted)";
}
public sealed record AiDirectorResult(AiScenePlan Plan,int Requests,long InputTokens,long OutputTokens,decimal? EstimatedUsd,IReadOnlyList<string> Actions);
public sealed class AiDirectorClient:IDisposable
{
    readonly HttpClient _http;
    public static readonly JsonSerializerOptions Json=new(){PropertyNamingPolicy=JsonNamingPolicy.CamelCase,PropertyNameCaseInsensitive=true};
    public AiDirectorClient(HttpMessageHandler? handler=null)=>_http=handler==null?new HttpClient{Timeout=TimeSpan.FromMinutes(4)}:new HttpClient(handler){Timeout=TimeSpan.FromMinutes(4)};
    public void Dispose()=>_http.Dispose();
    public const string Instructions="""
        You direct editable 3D film previs in GenPrevis. Understand English or Czech instructions, but write your summary in English.
        Use get_scene and search_library to inspect actual assets, poses and compatible motions. Never invent library IDs.
        Animated dog models are Husky and Shiba Inu. The built-in asset "dog" is a static prop, so use a rigged breed for animal motion.
        Finish by calling apply_scene_plan. It submits one atomic editable plan; it does not render or generate AI video.
        Build an attractive coherent shot with restrained scene detail, meaningful actor/object animation and a useful camera.
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
        Prefer a path for traveling actors/vehicles and in-place mocap to avoid double translation. Motions require an exact matching actor family. Static props move through paths/transform keys, never mocap.
        motion duration is timeline seconds, speed is 0.1..4, sourceIn is seconds after the catalog's source start. Loop=true repeats a short cycle for the requested duration.
        replaceMotions=true replaces the source sequence. Otherwise motions append at the explicitly given start times. Overlap adjacent clips by blendIn to crossfade.
        poseId sets a static library pose. A seated figure can use placeOnId with the supporting chair ID; GenPrevis fits the pelvis to its seat. It must have no travel path or position keys.
        poseKeys are ADDITIVE bone XYZ rotation degrees over mocap, with an identity [0,0,0] start key before the gesture. Bone="Root" instead means pelvis XYZ translation in metres. Only use exact bone names returned by get_scene; standard human names include Head, RightUpperArm, LeftUpperArm, RightLowerArm, LeftLowerArm.
        Each camera may have an optional targetId. Follow=true moves with it, LookAt=true aims at it. Use both for reliable tracking; position keys don't control Follow. Offsets are [side,height,Z] in target-local axes if relativeOffsets=true; +Z is behind a forward -Z actor. Use a side offset and enough distance to keep the complete subject in view.
        lookAtPoint sets the initial camera orientation toward a world point when target tracking is unused. Keep the camera outside models and above ground. Lens range 8..300mm; 28..50mm is a good normal shot.
        For an establishing car-follow shot prefer 10–16 metres following distance and 3–6 metres height. Put trees/buildings outside the camera's travel sweep so they do not obscure the hero.
        For a dolly or crane use absolute camera position keys plus LookAt=true and Follow=false. For a static wide use a fixed position/lookAtPoint. Avoid changing camera rotation while live LookAt is enabled.
        Light fields are only for point_light/directional_light assets. Use low intensity and keep neutral ambient fill unless dramatic lighting is requested.
        All schema fields must be present. For unused fields use "", [], false, or null as appropriate. Camera default offsets [0,2,6]/[0,0.8,0], focalLength 35, smoothness 0.
        You may submit corrections when validation returns an error. Do not claim success without a valid apply_scene_plan call.
        """;
    static readonly (string Name,string Description,Func<JsonObject> Schema)[] Tools={
        ("get_scene","Read actual scene objects, cameras, geometry bounds and available bone names.",AiToolSchema.Empty),
        ("search_library","Search real library items by English keywords; kind models, motions or poses; family can be empty. Keywords are OR-matched and ranked.",AiToolSchema.Search),
        ("apply_scene_plan","Submit the complete validated plan to create or edit the shot in one Undo step.",()=>AiToolSchema.For(typeof(AiScenePlan)))};

    public async Task<AiDirectorResult> GenerateAsync(AiDirectorConnection connection,string prompt,string scene,
        Func<string,JsonObject,Task<string>> readTool,Action<AiScenePlan> validate,Action<string> progress,CancellationToken cancellation)
    {
        if(string.IsNullOrWhiteSpace(connection.Key))throw new InvalidOperationException("Connect an API key first.");
        if(string.IsNullOrWhiteSpace(prompt)||prompt.Length>16000)throw new ArgumentException("Describe a shot using up to 16000 characters.");
        if(connection.Provider is not("openrouter" or "openai" or "gemini"))throw new ArgumentException("Unknown AI provider.");
        if(string.IsNullOrWhiteSpace(connection.Model)||connection.Model.Length>150||connection.Model.Any(char.IsWhiteSpace))throw new ArgumentException("Enter a valid model ID.");
        var chat=new JsonArray(new JsonObject{["role"]="system",["content"]=Instructions},new JsonObject{["role"]="user",["content"]=prompt+"\n\nCURRENT SCENE:\n"+scene});
        var responses=new JsonArray(new JsonObject{["role"]="user",["content"]=prompt+"\n\nCURRENT SCENE:\n"+scene});
        var gemini=new JsonArray(new JsonObject{["role"]="user",["parts"]=new JsonArray(new JsonObject{["text"]=prompt+"\n\nCURRENT SCENE:\n"+scene})});
        long input=0,output=0;decimal? cost=0;var actions=new List<string>();string lastIssue="";
        for(int round=1;round<=8;round++)
        {
            cancellation.ThrowIfCancellationRequested();progress("Planning · request "+round+" of 8");
            using var request=BuildRequest(connection,chat,responses,gemini);
            using var response=await _http.SendAsync(request,HttpCompletionOption.ResponseHeadersRead,cancellation);
            string text=await response.Content.ReadAsStringAsync(cancellation);
            if(text.Length>4_000_000)throw new InvalidOperationException("The AI response exceeded the size limit.");
            if(!response.IsSuccessStatusCode)throw new InvalidOperationException(SafeError(connection,text,(int)response.StatusCode));
            var root=JsonNode.Parse(text)?.AsObject()??throw new InvalidOperationException("The AI provider returned an empty response.");
            var usage=Usage(connection,root);input+=usage.Input;output+=usage.Output;cost=cost.HasValue&&usage.Cost.HasValue?cost.Value+usage.Cost.Value:null;
            if(output>45000)throw new InvalidOperationException("The planning token limit was reached. Shorten the description and try again.");
            var calls=new List<(string Id,string Name,JsonObject Arguments)>();
            if(connection.Provider=="openai")
            {
                foreach(var item in root["output"]?.AsArray()??new JsonArray())
                {
                    if(item==null)continue;responses.Add(item.DeepClone());
                    if(item["type"]?.GetValue<string>()=="function_call")calls.Add((item["call_id"]!.GetValue<string>(),item["name"]!.GetValue<string>(),ParseArguments(item["arguments"]!.GetValue<string>())));
                }
            }
            else if(connection.Provider=="gemini")
            {
                var content=root["candidates"]?[0]?["content"]??throw new InvalidOperationException("Gemini did not return a scene plan. Try a different description.");gemini.Add(content.DeepClone());
                foreach(var part in content["parts"]?.AsArray()??new JsonArray())if(part?["functionCall"] is {} call)calls.Add((call["id"]?.GetValue<string>()??"",call["name"]!.GetValue<string>(),call["args"]?.DeepClone().AsObject()??new()));
            }
            else
            {
                var message=root["choices"]?[0]?["message"]??throw new InvalidOperationException("OpenRouter did not return a scene plan.");chat.Add(message.DeepClone());
                foreach(var call in message["tool_calls"]?.AsArray()??new JsonArray())if(call!=null)calls.Add((call["id"]!.GetValue<string>(),call["function"]!["name"]!.GetValue<string>(),ParseArguments(call["function"]!["arguments"]!.GetValue<string>())));
            }
            if(calls.Count>12)throw new InvalidOperationException("The AI requested too many simultaneous actions.");
            progress("Received tools: "+(calls.Count==0?"none":string.Join(", ",calls.Select(call=>call.Name))));
            var geminiResults=new JsonArray();
            foreach(var call in calls)
            {
                cancellation.ThrowIfCancellationRequested();progress(call.Name=="search_library"?"Searching library…":call.Name=="apply_scene_plan"?"Validating scene and animation…":"Reading scene…");actions.Add(call.Name);
                string result;
                try
                {
                    if(call.Name=="apply_scene_plan")
                    {
                        var plan=call.Arguments.Deserialize<AiScenePlan>(Json)??throw new FormatException("A scene plan is required.");validate(plan);cancellation.ThrowIfCancellationRequested();
                        return new(plan,round,input,output,cost,actions);
                    }
                    if(call.Name is not("get_scene" or "search_library"))throw new ArgumentException("Unknown tool. Use get_scene, search_library or apply_scene_plan.");
                    result=await readTool(call.Name,call.Arguments);
                }
                catch(Exception error)when(error is ArgumentException or FormatException or InvalidOperationException)
                {lastIssue=error.Message;progress("Correcting plan: "+error.Message);result=JsonSerializer.Serialize(new{error=error.Message,instruction="Correct this issue and submit a complete valid scene plan."});}
                if(connection.Provider=="openai")responses.Add(new JsonObject{["type"]="function_call_output",["call_id"]=call.Id,["output"]=result});
                else if(connection.Provider=="gemini")
                {
                    var part=new JsonObject{["name"]=call.Name,["response"]=new JsonObject{["result"]=JsonNode.Parse(result)}};if(call.Id!="")part["id"]=call.Id;
                    geminiResults.Add(new JsonObject{["functionResponse"]=part});
                }
                else chat.Add(new JsonObject{["role"]="tool",["tool_call_id"]=call.Id,["content"]=result});
            }
            if(geminiResults.Count>0)gemini.Add(new JsonObject{["role"]="user",["parts"]=geminiResults});
            if(calls.Count==0)
            {
                const string reminder="Submit the scene using apply_scene_plan. A prose answer alone does not modify the program.";
                chat.Add(new JsonObject{["role"]="user",["content"]=reminder});responses.Add(new JsonObject{["role"]="user",["content"]=reminder});gemini.Add(new JsonObject{["role"]="user",["parts"]=new JsonArray(new JsonObject{["text"]=reminder})});
            }
        }
        throw new InvalidOperationException("The model did not finish a valid scene plan within eight requests. "+(lastIssue==""?"Try a shorter description.":"Last issue: "+lastIssue));
    }
    static JsonObject ParseArguments(string text)=>JsonNode.Parse(text)?.AsObject()??throw new FormatException("The model returned invalid function arguments.");
    static HttpRequestMessage BuildRequest(AiDirectorConnection c,JsonArray chat,JsonArray responses,JsonArray gemini)
    {
        JsonObject body;string url;
        if(c.Provider=="openai")
        {
            url="https://api.openai.com/v1/responses";
            body=new(){["model"]=c.Model,["instructions"]=Instructions,["input"]=responses.DeepClone(),["store"]=false,["max_output_tokens"]=8000,["reasoning"]=new JsonObject{["effort"]="medium"},["include"]=new JsonArray("reasoning.encrypted_content"),["tools"]=new JsonArray(Tools.Select(t=>(JsonNode)new JsonObject{["type"]="function",["name"]=t.Name,["description"]=t.Description,["strict"]=true,["parameters"]=t.Schema()}).ToArray())};
        }
        else if(c.Provider=="gemini")
        {
            url="https://generativelanguage.googleapis.com/v1beta/models/"+Uri.EscapeDataString(c.Model)+":generateContent";
            body=new(){["systemInstruction"]=new JsonObject{["parts"]=new JsonArray(new JsonObject{["text"]=Instructions})},["contents"]=gemini.DeepClone(),["generationConfig"]=new JsonObject{["maxOutputTokens"]=8000,["thinkingConfig"]=new JsonObject{["thinkingLevel"]="MEDIUM"}},["tools"]=new JsonArray(new JsonObject{["functionDeclarations"]=new JsonArray(Tools.Select(t=>(JsonNode)new JsonObject{["name"]=t.Name,["description"]=t.Description,["parametersJsonSchema"]=t.Schema()}).ToArray())})};
        }
        else
        {
            url="https://openrouter.ai/api/v1/chat/completions";
            body=new(){["model"]=c.Model,["messages"]=chat.DeepClone(),["max_tokens"]=8000,["reasoning"]=new JsonObject{["effort"]="medium"},["tools"]=new JsonArray(Tools.Select(t=>(JsonNode)new JsonObject{["type"]="function",["function"]=new JsonObject{["name"]=t.Name,["description"]=t.Description,["strict"]=true,["parameters"]=t.Schema()}}).ToArray())};
        }
        var request=new HttpRequestMessage(HttpMethod.Post,url){Content=new StringContent(body.ToJsonString(),Encoding.UTF8,"application/json")};
        if(c.Provider=="gemini")request.Headers.Add("x-goog-api-key",c.Key);else request.Headers.Authorization=new AuthenticationHeaderValue("Bearer",c.Key);
        if(c.Provider=="openrouter"){request.Headers.Add("HTTP-Referer","https://github.com/petrsajner/GenPrevis");request.Headers.Add("X-Title","GenPrevis");}
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
