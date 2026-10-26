using System;
using System.Linq;
using System.Reflection;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace GoPrevis;

public static class AiToolSchema
{
    public static JsonObject For(Type type)
    {
        if(type==typeof(string))return new(){["type"]="string"};
        if(type==typeof(bool))return new(){["type"]="boolean"};
        if(type==typeof(int))return new(){["type"]="integer"};
        if(type==typeof(float)||type==typeof(double))return new(){["type"]="number"};
        if(type.IsArray)return new(){["type"]="array",["items"]=For(type.GetElementType()!)};
        if(type.IsGenericType&&type.GetGenericTypeDefinition()==typeof(System.Collections.Generic.List<>))return new(){["type"]="array",["items"]=For(type.GetGenericArguments()[0])};
        var properties=new JsonObject();var required=new JsonArray();
        foreach(var property in type.GetProperties(BindingFlags.Public|BindingFlags.Instance))
        {
            string name=JsonNamingPolicy.CamelCase.ConvertName(property.Name);var schema=For(property.PropertyType);
            if(property.Name=="BackgroundColor"){schema["minItems"]=4;schema["maxItems"]=4;}
            if(property.Name=="Easing")schema["enum"]=new JsonArray(KeyInterpolation.Modes.Select(mode=>(JsonNode)JsonValue.Create(mode)!).ToArray());
            if(property.Name=="Channel")schema["enum"]=new JsonArray("Position","Rotation","Scale","Lens");
            if(property.Name=="TravelFacing")schema["enum"]=new JsonArray("Forward","Backward","Left","Right","Fixed","InPlace","Manual");
            if(property.Name is "PathTiming" or "AimPathTiming")schema["enum"]=new JsonArray("Authored","Constant speed","Smooth authored");
            if(property.Name=="Intent")schema["enum"]=new JsonArray("Auto","Static","Follow","Orbit","Dolly","Crane","Path","Manual");
            if(type==typeof(AiCameraMovement)&&property.Name=="Mode")schema["enum"]=new JsonArray("None","Orbit","Path");
            if(new NullabilityInfoContext().Create(property).ReadState==NullabilityState.Nullable)schema=new JsonObject{["anyOf"]=new JsonArray(schema,new JsonObject{["type"]="null"})};
            properties[name]=schema;required.Add(name);
        }
        return new(){["type"]="object",["properties"]=properties,["required"]=required,["additionalProperties"]=false};
    }
    public static JsonObject Search()=>new(){["type"]="object",["properties"]=new JsonObject{["kind"]=new JsonObject{["type"]="string",["enum"]=new JsonArray("models","motions","poses")},["query"]=new JsonObject{["type"]="string"},["family"]=new JsonObject{["type"]="string"}},["required"]=new JsonArray("kind","query","family"),["additionalProperties"]=false};
    public static JsonObject Empty()=>new(){["type"]="object",["properties"]=new JsonObject(),["required"]=new JsonArray(),["additionalProperties"]=false};
    public static JsonObject StringArgument(string name)=>new(){["type"]="object",["properties"]=new JsonObject{[name]=new JsonObject{["type"]="string"}},["required"]=new JsonArray(name),["additionalProperties"]=false};
    public static JsonObject VisualAssessment()=>new(){["type"]="object",["properties"]=new JsonObject{["planId"]=new JsonObject{["type"]="string"},["decision"]=new JsonObject{["type"]="string",["enum"]=new JsonArray("Accept","Revise")},["notes"]=new JsonObject{["type"]="string"}},["required"]=new JsonArray("planId","decision","notes"),["additionalProperties"]=false};
}
