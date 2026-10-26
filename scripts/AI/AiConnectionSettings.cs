using System;
using System.IO;
using System.Text.Json;

namespace GoPrevis;

public sealed class AiConnectionSettings
{
    public int Selection {get;set;}=4;
    public int DefaultsRevision {get;set;}=2;
    public string CustomModel {get;set;}="openai/gpt-6-luna";
    public bool UseVividia {get;set;}
    public static AiConnectionSettings Load(string path)
    {
        try
        {
            if(!File.Exists(path))return new();string text=File.ReadAllText(path);var value=JsonSerializer.Deserialize<AiConnectionSettings>(text)??new();
            if(!JsonDocument.Parse(text).RootElement.TryGetProperty(nameof(DefaultsRevision),out _)&&value.Selection==0)value.Selection=4;
            return value;
        }
        catch(Exception error)when(error is IOException or JsonException or UnauthorizedAccessException){return new();}
    }
    public void Save(string path){Directory.CreateDirectory(Path.GetDirectoryName(path)!);File.WriteAllText(path,JsonSerializer.Serialize(this));}
    public string Provider=>Selection==1?"openai":Selection==2?"gemini":"openrouter";
    public string Model=>Selection==1?"gpt-6-luna":Selection==2?"gemini-3.8-flash":Selection==3?CustomModel:Selection==4?"openai/gpt-6.1-sol":"openai/gpt-6-luna";
}
