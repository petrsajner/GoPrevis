using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace GoPrevis;

public sealed class AiReviewedProposal
{
    public AiScenePlan Plan {get;set;}=new();
    public string[] Operations {get;set;}=Array.Empty<string>();
}
public sealed class AiGenerationSession
{
    public string Id {get;set;}=Guid.NewGuid().ToString("N");
    public string Provider {get;set;}="";
    public string Model {get;set;}="";
    public string Prompt {get;set;}="";
    public string SceneSignature {get;set;}="";
    public ProjectData? OriginalScene {get;set;}
    public string OriginalProjectPath {get;set;}="";
    public double OriginalTime {get;set;}
    public bool ReplaceScene {get;set;}
    public string Status {get;set;}="Planning";
    public JsonArray Chat {get;set;}=new();
    public JsonArray Responses {get;set;}=new();
    public JsonArray Gemini {get;set;}=new();
    public JsonObject? PendingResponse {get;set;}
    public bool ResponseAdded {get;set;}
    public int NextTool {get;set;}
    public JsonArray GeminiResults {get;set;}=new();
    public List<AiPreviewImage> RenderedImages {get;set;}=new();
    public Dictionary<string,AiReviewedProposal> Reviewed {get;set;}=new();
    public HashSet<string> Approved {get;set;}=new();
    public int ReviewIndex {get;set;}
    public int Requests {get;set;}
    public long InputTokens {get;set;}
    public long OutputTokens {get;set;}
    public decimal? EstimatedUsd {get;set;}=0;
    public List<string> Actions {get;set;}=new();
    public string LastIssue {get;set;}="";
    public List<JsonObject> IncompleteResponses {get;set;}=new();
    public List<AiPreviewImage> References {get;set;}=new();
    public AiScenePlan? Draft {get;set;}
    public AiScenePlan? ReadyPlan {get;set;}
    public string[] ReadyOperations {get;set;}=Array.Empty<string>();
    public DateTime UpdatedUtc {get;set;}
    public void Save(string path)
    {
        UpdatedUtc=DateTime.UtcNow;Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        string temporary=path+".tmp";File.WriteAllText(temporary,JsonSerializer.Serialize(this));File.Move(temporary,path,true);
    }
    public static AiGenerationSession? Load(string path)
    {try{return File.Exists(path)?JsonSerializer.Deserialize<AiGenerationSession>(File.ReadAllText(path)):null;}catch(Exception error)when(error is IOException or JsonException or UnauthorizedAccessException){return null;}}
    public AiDirectorResult Result()=>new(ReadyPlan??throw new InvalidOperationException("The session has no ready plan."),Requests,InputTokens,OutputTokens,EstimatedUsd,Actions){VerifiedOperations=ReadyOperations};
    public void Stage(AiScenePlan batch)
    {
        Draft??=new();if(batch.Summary!="")Draft.Summary=batch.Summary;if(batch.Duration!=0)Draft.Duration=batch.Duration;if(batch.Fps!=0)Draft.Fps=batch.Fps;
        if(batch.Width!=0||batch.Height!=0){Draft.Width=batch.Width;Draft.Height=batch.Height;}if(batch.SceneSettings!=null)Draft.SceneSettings=batch.SceneSettings;
        if(batch.ActiveCameraId!="")Draft.ActiveCameraId=batch.ActiveCameraId;
        foreach(string id in batch.RemoveIds){Draft.Objects.RemoveAll(o=>o.Id==id);Draft.Cameras.RemoveAll(c=>c.Id==id);if(!Draft.RemoveIds.Contains(id))Draft.RemoveIds.Add(id);}
        foreach(var item in batch.Objects){Draft.Objects.RemoveAll(o=>o.Id==item.Id);Draft.Objects.Add(item);Draft.RemoveIds.Remove(item.Id);}
        foreach(var camera in batch.Cameras){Draft.Cameras.RemoveAll(c=>c.Id==camera.Id);Draft.Cameras.Add(camera);Draft.RemoveIds.Remove(camera.Id);}
        Reviewed.Clear();Approved.Clear();
    }
}
