using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;

namespace GoPrevis;

public sealed partial class AiDirectorClient
{
    sealed record StoredCall(string Id,string Name,JsonObject Arguments);
    public async Task<AiDirectorResult> GenerateAsync(AiDirectorConnection connection,string prompt,string scene,
        Func<string,JsonObject,Task<string>> readTool,Action<AiScenePlan> validate,Action<string> progress,CancellationToken cancellation,
        Func<AiScenePlan,CancellationToken,Task<AiVisualReview>>? preview=null,AiGenerationSession? session=null,
        Action<AiGenerationSession>? checkpoint=null,IReadOnlyList<AiPreviewImage>? references=null)
    {
        if(string.IsNullOrWhiteSpace(connection.Key))throw new InvalidOperationException("Connect an API key first.");
        if(string.IsNullOrWhiteSpace(prompt))throw new ArgumentException("Describe the shot first.");
        if(connection.Provider is not("openrouter" or "openai" or "gemini"))throw new ArgumentException("Unknown AI provider.");
        if(string.IsNullOrWhiteSpace(connection.Model)||connection.Model.Length>150||connection.Model.Any(char.IsWhiteSpace))throw new ArgumentException("Enter a valid model ID.");
        session??=new();
        if(session.Provider!=""&&(session.Provider!=connection.Provider||session.Model!=connection.Model))throw new ArgumentException("Resume this session using its original provider and model.");
        session.Provider=connection.Provider;session.Model=connection.Model;session.Prompt=prompt;
        void Save(){checkpoint?.Invoke(session);}
        if(session.ReadyPlan!=null)return session.Result();
        if(session.Chat.Count==0)
        {
            string input=prompt+"\n\nCURRENT SCENE:\n"+scene;
            session.Chat.Add(new JsonObject{["role"]="system",["content"]=Instructions});session.Chat.Add(new JsonObject{["role"]="user",["content"]=input});
            session.Responses.Add(new JsonObject{["role"]="user",["content"]=input});session.Gemini.Add(new JsonObject{["role"]="user",["parts"]=new JsonArray(new JsonObject{["text"]=input})});
            if(references!=null)session.References=references.ToList();
            if(session.References.Count>0)AppendImages(session,session.References,new JsonArray(),false);
            Save();
        }
        // Provider limits remain intrinsic. There is deliberately no application-wide token or turn ceiling.
        int? completionMaximum=await CompletionMaximum(connection,cancellation);
        while(true)
        {
            cancellation.ThrowIfCancellationRequested();
            if(session.PendingResponse==null)
            {
                progress("Planning · request "+(session.Requests+1)+" · "+session.InputTokens+" input / "+session.OutputTokens+" output tokens");
                using var request=BuildRequest(connection,session.Chat,session.Responses,session.Gemini,completionMaximum);
                using var response=await _http.SendAsync(request,HttpCompletionOption.ResponseHeadersRead,cancellation);
                string text=await response.Content.ReadAsStringAsync(cancellation);
                if(!response.IsSuccessStatusCode)throw new InvalidOperationException(SafeError(connection,text,(int)response.StatusCode));
                var root=JsonNode.Parse(text)?.AsObject()??throw new InvalidOperationException("The provider returned an empty response. The saved session can be resumed.");
                var usage=Usage(connection,root);session.Requests++;session.InputTokens+=usage.Input;session.OutputTokens+=usage.Output;
                session.EstimatedUsd=session.EstimatedUsd.HasValue&&usage.Cost.HasValue?session.EstimatedUsd.Value+usage.Cost.Value:null;
                session.PendingResponse=root;session.ResponseAdded=false;session.NextTool=0;Save();
            }
            var pending=session.PendingResponse!;
            if(Incomplete(connection.Provider,pending))
            {
                session.IncompleteResponses.Add((JsonObject)pending.DeepClone());
                AddText(session,"The previous completion reached the provider's native per-response limit. Its partial output and paid usage are saved. Continue this same task by staging smaller scene sections with stage_scene_changes; do not shorten the user's request or repeat completed work. Use get_staged_scene to inspect saved draft work.");
                session.LastIssue="Provider response was incomplete; continuing with saved work.";session.PendingResponse=null;session.ResponseAdded=false;Save();continue;
            }
            var calls=ReadCalls(connection.Provider,pending);
            if(!session.ResponseAdded){AppendModelOutput(session,pending);session.ResponseAdded=true;Save();}
            for(int i=session.NextTool;i<calls.Count;i++)
            {
                cancellation.ThrowIfCancellationRequested();var call=calls[i];session.Actions.Add(call.Name);
                progress(call.Name switch{"search_library"=>"Searching library…","inspect_scene_plan" or "inspect_staged_scene"=>"Rehearsing and reviewing proposed scene…","stage_scene_changes"=>"Saving draft scene section…","apply_scene_plan"=>"Applying the reviewed shot…",_=>"Using "+call.Name+"…"});
                string result;
                try{result=await ExecuteTool(connection,session,call,readTool,validate,preview,cancellation);}
                catch(Exception error)when(error is ArgumentException or FormatException or InvalidOperationException)
                {session.LastIssue=error.Message;progress("Correcting plan: "+error.Message);result=JsonSerializer.Serialize(new{error=error.Message,instruction="Continue from the staged draft. Correct the issue and inspect the revised proposal."});}
                session.NextTool=i+1;
                if(session.ReadyPlan!=null){session.Status="Ready";Save();return session.Result();}
                if(connection.Provider=="openai")session.Responses.Add(new JsonObject{["type"]="function_call_output",["call_id"]=call.Id,["output"]=result});
                else if(connection.Provider=="gemini")
                {var part=new JsonObject{["name"]=call.Name,["response"]=new JsonObject{["result"]=JsonNode.Parse(result)}};if(call.Id!="")part["id"]=call.Id;session.GeminiResults.Add(new JsonObject{["functionResponse"]=part});}
                else session.Chat.Add(new JsonObject{["role"]="tool",["tool_call_id"]=call.Id,["content"]=result});
                Save();
            }
            if(session.RenderedImages.Count>0)AppendImages(session,session.RenderedImages,session.GeminiResults,true);
            else if(session.GeminiResults.Count>0)session.Gemini.Add(new JsonObject{["role"]="user",["parts"]=session.GeminiResults.DeepClone()});
            if(calls.Count==0)AddText(session,"Continue the requested scene using the tools. Save sections with stage_scene_changes, inspect the complete draft, assess the actual previews and apply the accepted planId. Prose alone does not build the shot.");
            session.PendingResponse=null;session.ResponseAdded=false;session.NextTool=0;session.GeminiResults=new();session.RenderedImages.Clear();Save();
        }
    }
    async Task<string> ExecuteTool(AiDirectorConnection connection,AiGenerationSession session,StoredCall call,
        Func<string,JsonObject,Task<string>> readTool,Action<AiScenePlan> validate,Func<AiScenePlan,CancellationToken,Task<AiVisualReview>>? preview,CancellationToken cancellation)
    {
        if(call.Name=="stage_scene_changes")
        {
            var batch=call.Arguments.Deserialize<AiScenePlan>(Json)??throw new ArgumentException("A scene section is required.");session.Stage(batch);
            return JsonSerializer.Serialize(new{status="draft_section_saved",objects=session.Draft!.Objects.Count,cameras=session.Draft.Cameras.Count,note="This uncommitted draft is saved locally. Stage more sections or inspect_staged_scene when the full shot is ready."});
        }
        if(call.Name=="get_staged_scene")return JsonSerializer.Serialize(new{status=session.Draft==null?"empty":"saved_draft",draft=session.Draft},Json);
        if(call.Name=="apply_scene_plan")
        {
            string id=call.Arguments["planId"]?.GetValue<string>()??"";
            if(!session.Reviewed.TryGetValue(id,out var proposal))throw new ArgumentException("Inspect a complete scene plan first and use its valid planId.");
            if(!session.Approved.Contains(id))throw new ArgumentException("Assess the actual rendered preview with assess_preview before committing this plan.");
            validate(proposal.Plan);cancellation.ThrowIfCancellationRequested();session.ReadyPlan=proposal.Plan;session.ReadyOperations=proposal.Operations;return "{\"status\":\"ready\"}";
        }
        if(call.Name is "inspect_scene_plan" or "inspect_staged_scene")
        {
            session.Reviewed.Clear();session.Approved.Clear();
            var plan=call.Name=="inspect_staged_scene"?session.Draft??throw new ArgumentException("Stage some scene sections first."):call.Arguments.Deserialize<AiScenePlan>(Json)??throw new ArgumentException("A complete scene plan is required.");
            session.Draft=plan;validate(plan);
            var report=JsonNode.Parse(await readTool("inspect_scene_plan",JsonSerializer.SerializeToNode(plan,Json)!.AsObject()))!.AsObject();bool valid=report["valid"]?.GetValue<bool>()==true;
            if(valid&&preview!=null)
            {
                var visual=await preview(plan,cancellation);session.RenderedImages.AddRange(visual.Images);report["visualReview"]=JsonSerializer.SerializeToNode(new{visual.Passed,visual.Summary,visual.Errors,visual.Measurements,imageCount=visual.Images.Count},Json);
                if(!visual.Passed){valid=false;report["valid"]=false;session.LastIssue=string.Join(" ",visual.Errors);}
            }
            if(valid)
            {
                string id="review-"+(++session.ReviewIndex);session.Reviewed[id]=new(){Plan=plan,Operations=report["operations"]?.AsArray().Select(o=>o!.GetValue<string>()).ToArray()??Array.Empty<string>()};report["planId"]=id;
                if(preview==null)session.Approved.Add(id);
            }
            else session.LastIssue=report["errors"]?.ToJsonString()??session.LastIssue;
            return report.ToJsonString();
        }
        if(call.Name=="assess_preview")
        {
            string id=call.Arguments["planId"]?.GetValue<string>()??"",decision=call.Arguments["decision"]?.GetValue<string>()??"",notes=call.Arguments["notes"]?.GetValue<string>()??"";
            if(!session.Reviewed.TryGetValue(id,out var proposal))throw new ArgumentException("Inspect a valid complete plan first.");if(notes.Length<20)throw new ArgumentException("Describe concrete observations from the rendered frames.");
            if(decision=="Accept"){session.Approved.Add(id);proposal.Operations=proposal.Operations.Concat(new[]{"Visual rehearsal: "+notes}).ToArray();return JsonSerializer.Serialize(new{status="visual_preview_accepted",planId=id,notes});}
            if(decision=="Revise"){session.Reviewed.Remove(id);return JsonSerializer.Serialize(new{status="revision_required",notes,instruction="Stage or inspect a corrected proposal."});}throw new ArgumentException("Preview decision must be Accept or Revise.");
        }
        if(call.Name is not("get_scene" or "search_library" or "get_program_guide" or "get_asset_details"))throw new ArgumentException("Read get_program_guide for the available operations.");
        return await readTool(call.Name,call.Arguments);
    }
    static bool Incomplete(string provider,JsonObject root)=>provider=="openrouter"?root["choices"]?[0]?["finish_reason"]?.GetValue<string>()=="length":provider=="gemini"?root["candidates"]?[0]?["finishReason"]?.GetValue<string>()=="MAX_TOKENS":root["status"]?.GetValue<string>()=="incomplete";
    static List<StoredCall> ReadCalls(string provider,JsonObject root)
    {
        var calls=new List<StoredCall>();
        if(provider=="openai")foreach(var item in root["output"]?.AsArray()??new JsonArray()){if(item?["type"]?.GetValue<string>()=="function_call")calls.Add(new(item["call_id"]!.GetValue<string>(),item["name"]!.GetValue<string>(),ParseArguments(item["arguments"]!.GetValue<string>())));}
        else if(provider=="gemini")foreach(var part in root["candidates"]?[0]?["content"]?["parts"]?.AsArray()??new JsonArray()){if(part?["functionCall"] is {} call)calls.Add(new(call["id"]?.GetValue<string>()??"",call["name"]!.GetValue<string>(),call["args"]?.DeepClone().AsObject()??new()));}
        else foreach(var call in root["choices"]?[0]?["message"]?["tool_calls"]?.AsArray()??new JsonArray()){if(call!=null)calls.Add(new(call["id"]!.GetValue<string>(),call["function"]!["name"]!.GetValue<string>(),ParseArguments(call["function"]!["arguments"]!.GetValue<string>())));}
        return calls;
    }
    static void AppendModelOutput(AiGenerationSession session,JsonObject root)
    {
        if(session.Provider=="openai")foreach(var item in root["output"]?.AsArray()??new JsonArray()){if(item!=null)session.Responses.Add(item.DeepClone());}
        else if(session.Provider=="gemini")session.Gemini.Add(root["candidates"]?[0]?["content"]?.DeepClone()??throw new InvalidOperationException("The provider returned no content. Resume the saved session."));
        else session.Chat.Add(root["choices"]?[0]?["message"]?.DeepClone()??throw new InvalidOperationException("The provider returned no message. Resume the saved session."));
    }
    static void AddText(AiGenerationSession session,string message)
    {session.Chat.Add(new JsonObject{["role"]="user",["content"]=message});session.Responses.Add(new JsonObject{["role"]="user",["content"]=message});session.Gemini.Add(new JsonObject{["role"]="user",["parts"]=new JsonArray(new JsonObject{["text"]=message})});}
    static void AppendImages(AiGenerationSession session,IReadOnlyList<AiPreviewImage> images,JsonArray geminiResults,bool toolResponse)
    {
        var chat=new JsonArray();var responses=new JsonArray();
        foreach(var image in images)
        {
            chat.Add(new JsonObject{["type"]="text",["text"]=image.Caption});chat.Add(new JsonObject{["type"]="image_url",["image_url"]=new JsonObject{["url"]=image.DataUrl}});
            responses.Add(new JsonObject{["type"]="input_text",["text"]=image.Caption});responses.Add(new JsonObject{["type"]="input_image",["image_url"]=image.DataUrl});
            geminiResults.Add(new JsonObject{["text"]=image.Caption});geminiResults.Add(new JsonObject{["inlineData"]=new JsonObject{["mimeType"]="image/png",["data"]=image.DataUrl[(image.DataUrl.IndexOf(',')+1)..]}});
        }
        if(!toolResponse)
        {
            chat.Insert(0,new JsonObject{["type"]="text",["text"]=session.Chat[^1]!["content"]!.GetValue<string>()});session.Chat[^1]!["content"]=chat;
            responses.Insert(0,new JsonObject{["type"]="input_text",["text"]=session.Responses[^1]!["content"]!.GetValue<string>()});session.Responses[^1]!["content"]=responses;
            foreach(var part in geminiResults)session.Gemini[^1]!["parts"]!.AsArray().Add(part?.DeepClone());
        }
        else
        {session.Chat.Add(new JsonObject{["role"]="user",["content"]=chat});session.Responses.Add(new JsonObject{["role"]="user",["content"]=responses});session.Gemini.Add(new JsonObject{["role"]="user",["parts"]=geminiResults.DeepClone()});}
    }
    async Task<int?> CompletionMaximum(AiDirectorConnection connection,CancellationToken cancellation)
    {
        if(connection.Model is "openai/gpt-6.1-sol" or "gpt-6.1-sol" or "openai/gpt-6-luna" or "gpt-6-luna")return 128000;
        if(connection.Provider=="gemini")return 65536;
        if(connection.Provider!="openrouter")return null;
        try
        {
            using var response=await _http.GetAsync("https://openrouter.ai/api/v1/models",cancellation);response.EnsureSuccessStatusCode();var root=JsonNode.Parse(await response.Content.ReadAsStringAsync(cancellation));
            var model=root?["data"]?.AsArray().FirstOrDefault(item=>item?["id"]?.GetValue<string>()==connection.Model);return model?["top_provider"]?["max_completion_tokens"]?.GetValue<int>();
        }
        catch(Exception error)when(error is HttpRequestException or JsonException or InvalidOperationException){return null;}
    }
}
