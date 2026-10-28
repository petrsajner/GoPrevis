using System;
using System.Collections.Generic;
using System.Linq;

namespace GoPrevis;

public partial class GoPrevisApp
{
    Dictionary<string,double>? _propertyEditBefore;
    bool _capturingPropertyEdit;
    void CapturePropertyEditBaseline()
    {
        if(_world==null||_propertyEvaluating||_capturingPropertyEdit)return;
        _propertyEditBefore=AnimationBindings().Where(b=>!b.Path.StartsWith("Transform.")&&!b.Path.StartsWith("Pose.")&&b.Path!="Lens")
            .GroupBy(b=>b.Identity).ToDictionary(g=>g.Key,g=>g.First().BaseRead!());
    }
    bool CommitPendingPropertyEdits()
    {
        if(_propertyEditBefore==null||_capturingPropertyEdit||_propertyEvaluating)return false;
        var before=_propertyEditBefore;_propertyEditBefore=null;bool changed=false;_capturingPropertyEdit=true;
        try
        {
            foreach(var b in AnimationBindings())
            {
                if(!before.TryGetValue(b.Identity,out double old)||b.BaseRead==null)continue;
                double value=b.BaseRead();if(!double.IsFinite(value)||Math.Abs(value-old)<1e-8||(!_autoKey&&(b.Curve()?.Keys.Count??0)==0))continue;
                b.Apply?.Invoke(old);PropertyCurveRuntime.Upsert(b.EnsureCurve(),_time,value);changed=true;
            }
        }
        finally{_capturingPropertyEdit=false;}
        return changed;
    }
}
