using System;
using System.Linq;

namespace GoPrevis;

public sealed class ObjectLinkData
{
    public string StartTargetId {get;set;}="";
    public string EndTargetId {get;set;}="";
    public string StartBone {get;set;}="";
    public string EndBone {get;set;}="";
    public float[] StartOffset {get;set;}=new float[3];
    public float[] EndOffset {get;set;}=new float[3];
    public float Width {get;set;}=.018f;
    public ObjectLinkData ValidatedCopy()
    {
        static float[] V(float[] v){if(v is not{Length:3}||v.Any(x=>!float.IsFinite(x)||Math.Abs(x)>1000))throw new ArgumentException("A linked line needs finite XYZ endpoint offsets.");return v.ToArray();}
        if(string.IsNullOrWhiteSpace(StartTargetId)||string.IsNullOrWhiteSpace(EndTargetId)||StartTargetId==EndTargetId||!float.IsFinite(Width)||Width<=0||Width>1)throw new ArgumentException("A linked line needs two different targets and a width of 0–1 metre.");
        return new(){StartTargetId=StartTargetId,EndTargetId=EndTargetId,StartBone=StartBone??"",EndBone=EndBone??"",StartOffset=V(StartOffset),EndOffset=V(EndOffset),Width=Width};
    }
}
