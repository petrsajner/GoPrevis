using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;

namespace GoPrevis;

public sealed class LibraryViewState
{
    public string Query {get;set;}="";
    public string Category {get;set;}="";
    public string Family {get;set;}="";
    public string Source {get;set;}="";
    public string Scope {get;set;}="All items";
    public string Sort {get;set;}="Name";
    public string Selected {get;set;}="";
    public int Scroll {get;set;}
}
public sealed class LibraryPreferences
{
    public bool Expanded {get;set;}
    public HashSet<string> Favorites {get;set;}=new(StringComparer.Ordinal);
    public List<string> Recent {get;set;}=new();
    public Dictionary<string,HashSet<string>> Collections {get;set;}=new(StringComparer.Ordinal);
    public Dictionary<string,LibraryViewState> Views {get;set;}=new(StringComparer.Ordinal);
    public LibraryViewState View(string section)
    {if(!Views.TryGetValue(section,out var view))Views[section]=view=new();return view;}
    public void Used(string key){Recent.Remove(key);Recent.Insert(0,key);if(Recent.Count>100)Recent.RemoveRange(100,Recent.Count-100);}
    public void ToggleFavorite(string key){if(!Favorites.Add(key))Favorites.Remove(key);}
    public void Collect(string name,string key)
    {name=name.Trim();if(name.Length==0||name.Length>60)throw new ArgumentException("Use a collection name of 1–60 characters.");if(!Collections.TryGetValue(name,out var items))Collections[name]=items=new(StringComparer.Ordinal);items.Add(key);}
    public bool InScope(string key,string scope)=>scope=="All items"||scope=="Favorites"&&Favorites.Contains(key)||scope=="Recently used"&&Recent.Contains(key)||scope.StartsWith("Collection: ",StringComparison.Ordinal)&&Collections.TryGetValue(scope[12..],out var items)&&items.Contains(key);
    public static LibraryPreferences Load(string path)
    {try{return File.Exists(path)?JsonSerializer.Deserialize<LibraryPreferences>(File.ReadAllText(path))??new():new();}catch(Exception e)when(e is IOException or JsonException or UnauthorizedAccessException){return new();}}
    public void Save(string path)
    {Directory.CreateDirectory(Path.GetDirectoryName(path)!);string temp=path+".tmp";File.WriteAllText(temp,JsonSerializer.Serialize(this));File.Move(temp,path,true);}
    public static bool Matches(string text,string query)=>query.Split(' ',StringSplitOptions.RemoveEmptyEntries|StringSplitOptions.TrimEntries).All(word=>text.Contains(word,StringComparison.OrdinalIgnoreCase));
}
