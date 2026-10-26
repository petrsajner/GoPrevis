using Godot;
using System.Collections.Generic;

namespace GoPrevis;

public partial class AiSketchCanvas:Control
{
    public string ViewType="Top view";
    public readonly List<List<Vector2>> Strokes=new();
    bool _drawing;
    public override void _Ready(){MouseDefaultCursorShape=CursorShape.Cross;ClipContents=true;}
    public override void _GuiInput(InputEvent input)
    {
        if(input is InputEventMouseButton{ButtonIndex:MouseButton.Left} button)
        {if(button.Pressed){_drawing=true;Strokes.Add(new(){Normalized(button.Position)});}else _drawing=false;AcceptEvent();QueueRedraw();}
        else if(input is InputEventMouseMotion motion&&_drawing){Strokes[^1].Add(Normalized(motion.Position));AcceptEvent();QueueRedraw();}
    }
    public override void _Input(InputEvent input){if(input is InputEventMouseButton{ButtonIndex:MouseButton.Left,Pressed:false})_drawing=false;}
    Vector2 Normalized(Vector2 point)=>new(Mathf.Clamp(point.X/Mathf.Max(1,Size.X),0,1),Mathf.Clamp(point.Y/Mathf.Max(1,Size.Y),0,1));
    public void UndoStroke(){if(Strokes.Count>0)Strokes.RemoveAt(Strokes.Count-1);QueueRedraw();}
    public void Clear(){Strokes.Clear();QueueRedraw();}
    public override void _Draw()
    {
        DrawRect(new Rect2(Vector2.Zero,Size),new Color("f3f5f8"));
        for(float x=0;x<Size.X;x+=40)DrawLine(new Vector2(x,0),new Vector2(x,Size.Y),new Color("dce1e8"));
        for(float y=0;y<Size.Y;y+=40)DrawLine(new Vector2(0,y),new Vector2(Size.X,y),new Color("dce1e8"));
        DrawString(ThemeDB.FallbackFont,new Vector2(10,22),ViewType=="Top view"?"TOP VIEW · X right / -Z up":ViewType=="Side view"?"SIDE VIEW · X right / Y up":"REFERENCE SKETCH",fontSize:14,modulate:new Color("536378"));
        foreach(var stroke in Strokes){for(int i=1;i<stroke.Count;i++)DrawLine(stroke[i-1]*Size,stroke[i]*Size,new Color("183652"),3,true);}
    }
}
