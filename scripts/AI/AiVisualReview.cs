using System;
using System.Collections.Generic;

namespace GoPrevis;

public sealed record AiPreviewImage(string Caption,string DataUrl);
public sealed class AiVisualReview
{
    public bool Passed {get;set;}=true;
    public string Summary {get;set;}="";
    public List<string> Errors {get;}=new();
    public List<object> Measurements {get;}=new();
    public List<AiPreviewImage> Images {get;}=new();
}
