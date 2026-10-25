using Godot;
using System;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;

namespace GoPrevis;

public partial class GoPrevisApp
{
    async Task<int> RunAiVisualContractChecks()
    {
        int checks=0;void Check(bool value,string message){Require(value,"Visual AI contract: "+message);checks++;}
        const string pixel="data:image/png;base64,iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVQIHWP4z8DwHwAFgAI/ScLbtAAAAABJRU5ErkJggg==";
        foreach(string provider in new[]{"openrouter","openai","gemini"})
        {
            var plan=AiFixturePlan();var handler=new AiFixtureHandler();int rehearsals=0;
            handler.Reply=(request,body,round)=>
            {
                if(round==2)
                {
                    string json=body.ToJsonString();Check(json.Contains(provider=="gemini"?"inlineData":provider=="openai"?"input_image":"image_url",StringComparison.Ordinal),provider+" includes rendered image input in the model's conversation");
                }
                if(round==3)Check(body.ToJsonString().Contains("Assess the actual rendered preview",StringComparison.Ordinal),provider+" refuses a commit before the visual assessment");
                string tool=round==1?"inspect_scene_plan":round==3?"assess_preview":"apply_scene_plan";
                JsonNode args=round==1?JsonSerializer.SerializeToNode(plan,AiDirectorClient.Json)!:round==3?new JsonObject{["planId"]="review-1",["decision"]="Accept",["notes"]="I inspected the chronological preview frames and the actor's stepping and camera composition are coherent."}:new JsonObject{["planId"]="review-1"};
                if(provider=="gemini")return new(){["candidates"]=new JsonArray(new JsonObject{["content"]=new JsonObject{["role"]="model",["parts"]=new JsonArray(new JsonObject{["functionCall"]=new JsonObject{["id"]="c"+round,["name"]=tool,["args"]=args}})}})};
                if(provider=="openai")return new(){["output"]=new JsonArray(new JsonObject{["type"]="function_call",["call_id"]="c"+round,["name"]=tool,["arguments"]=args.ToJsonString()})};
                return new(){["choices"]=new JsonArray(new JsonObject{["message"]=new JsonObject{["role"]="assistant",["tool_calls"]=new JsonArray(new JsonObject{["id"]="c"+round,["type"]="function",["function"]=new JsonObject{["name"]=tool,["arguments"]=args.ToJsonString()}})}})};
            };
            using var client=new AiDirectorClient(handler);
            var result=await client.GenerateAsync(new(provider,provider=="gemini"?"gemini-3.8-flash":provider=="openai"?"gpt-6-luna":"openai/gpt-6-luna","fixture-only-not-a-real-key"),"Create a scene",AiSceneContext(true),AiReadTool,p=>{_=CompileAiPlan(p,true);},_=>{},CancellationToken.None,(proposal,token)=>
            {rehearsals++;var visual=new AiVisualReview{Summary="Actual-render fixture"};visual.Images.Add(new("Chronological render frames",pixel));return Task.FromResult(visual);});
            Check(rehearsals==1&&result.Requests==4&&result.Actions.Contains("assess_preview"),provider+" requires a rendered rehearsal, assessment and commit in order");
            Check(result.VerifiedOperations.Any(operation=>operation.StartsWith("Visual rehearsal:",StringComparison.Ordinal)),provider+" records the agent's concrete visual assessment");
        }
        GD.Print("AI_VISUAL_CONTRACT_OK checks="+checks);return checks;
    }
}
