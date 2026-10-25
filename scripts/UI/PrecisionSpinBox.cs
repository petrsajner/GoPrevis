using Godot;
using System;
using System.Globalization;

namespace GenPrevis;

/// <summary>
/// A normal editable SpinBox with a bounded, linear scrub area over its arrows.
/// The overlay consumes arrow input so the native accelerating drag never starts.
/// </summary>
public partial class PrecisionSpinBox : SpinBox
{
    public double DragUnitsPerPixel { get; set; }
    public double ArrowStep { get; set; }
    public event Action? GestureStarted;
    public event Action? GestureEnded;
    ArrowSurface _arrows=null!;
    bool _pressed,_dragged,_gestureStarted,_up,_textEdited;
    float _travel;
    double _accumulator,_initial;
    internal Control ArrowControl=>_arrows;
    internal bool ArrowDragging=>_pressed;

    public override void _Ready()
    {
        _arrows=new ArrowSurface{Spin=this,Name="PrecisionArrows",MouseFilter=MouseFilterEnum.Stop,FocusMode=FocusModeEnum.None,MouseDefaultCursorShape=CursorShape.Vsize,TooltipText="Click for a small step. Drag up or down to adjust precisely. Alt: finer."};
        _arrows.AnchorLeft=1;_arrows.AnchorRight=1;_arrows.AnchorTop=0;_arrows.AnchorBottom=1;
        float width=18;if(HasThemeIcon("updown","SpinBox"))width=Math.Max(width,GetThemeIcon("updown","SpinBox").GetWidth());
        _arrows.OffsetLeft=-width;_arrows.OffsetRight=0;_arrows.OffsetTop=0;_arrows.OffsetBottom=0;
        _arrows.GuiInput+=ArrowInput;AddChild(_arrows);
        var entry=GetLineEdit();entry.TextChanged+=_=>_textEdited=true;entry.TextSubmitted+=_=>_textEdited=false;entry.FocusExited+=()=>_textEdited=false;
        VisibilityChanged+=()=>{if(!IsVisibleInTree())FinishArrowGesture();};
    }
    partial class ArrowSurface:Control
    {
        public PrecisionSpinBox Spin=null!;
        public override void _Draw()
        {
            DrawRect(new Rect2(Vector2.Zero,Size),new Color("25252b"));
            DrawLine(new Vector2(0,3),new Vector2(0,Size.Y-3),new Color("3c3b45"));
            float x=Size.X*.5f,y=Size.Y*.25f;Color color=Spin.Editable?new Color("c6c2d0"):new Color("696570");
            DrawPolyline(new[]{new Vector2(x-3,y+1),new Vector2(x,y-2),new Vector2(x+3,y+1)},color,1.3f,true);
            y=Size.Y*.75f;DrawPolyline(new[]{new Vector2(x-3,y-1),new Vector2(x,y+2),new Vector2(x+3,y-1)},color,1.3f,true);
        }
        public override void _Process(double delta)=>QueueRedraw();
    }
    void ArrowInput(InputEvent input)
    {
        if(input is InputEventMouseButton button)
        {
            _arrows.AcceptEvent();
            if(button.ButtonIndex!=MouseButton.Left)return;
            if(button.Pressed)
            {
                if(!Editable)return;FinishArrowGesture();
                var entry=GetLineEdit();
                if(_textEdited){Apply();_textEdited=false;}
                else
                {
                    // Range.Value and the native text refresh are not synchronous.
                    // Never re-apply a stale label after a programmatic value update.
                    // Focus exit may also submit text, so synchronize it first.
                    entry.Text=Prefix+Value.ToString("0.###############",CultureInfo.InvariantCulture)+Suffix;
                }
                entry.ReleaseFocus();
                _pressed=true;_dragged=false;_gestureStarted=false;_travel=0;_up=button.Position.Y<_arrows.Size.Y*.5f;_initial=_accumulator=Value;
            }
            else FinishArrowGesture();
        }
        else if(input is InputEventMouseMotion motion&&_pressed)
        {ApplyArrowMotion(motion.Relative.Y,motion.AltPressed);_arrows.AcceptEvent();}
    }
    public override void _Input(InputEvent input)
    {
        if(!_pressed)return;
        if(input is InputEventMouseMotion motion)
        {ApplyArrowMotion(motion.Relative.Y,motion.AltPressed);GetViewport().SetInputAsHandled();}
        else if(input is InputEventMouseButton {ButtonIndex:MouseButton.Left,Pressed:false})
        {FinishArrowGesture();GetViewport().SetInputAsHandled();}
        else if(input is InputEventKey {Keycode:Key.Escape,Pressed:true})
        {if(_gestureStarted)Value=_initial;_dragged=true;FinishArrowGesture();GetViewport().SetInputAsHandled();}
    }
    void ApplyArrowMotion(float relativeY,bool fine)
    {
        if(!Editable){FinishArrowGesture();return;}
        if(!float.IsFinite(relativeY))return;
        _travel+=Math.Abs(relativeY);if(!_dragged&&_travel<2)return;_dragged=true;
        double units=DragUnitsPerPixel>0?DragUnitsPerPixel:Step<=.01?Step:Step*.5;
        units=Math.Max(.0000001,units)*(fine?.1:1);
        double previous=Value,desired=ClampNumber(_accumulator-relativeY*units);
        // Discard over-drag debt at either bound. An opposite pixel must work
        // immediately, including when the drag resolution is finer than Step.
        if(Step>0)
        {
            if(!AllowGreater&&previous>=MaxValue-Step*.01&&relativeY>0)desired=Math.Min(desired,MaxValue-Step);
            if(!AllowLesser&&previous<=MinValue+Step*.01&&relativeY<0)desired=Math.Max(desired,MinValue+Step);
        }
        _accumulator=ClampNumber(desired);SetGestureValue(_accumulator);
    }
    double ClampNumber(double value)
    {
        if(!AllowLesser)value=Math.Max(MinValue,value);if(!AllowGreater)value=Math.Min(MaxValue,value);return value;
    }
    void SetGestureValue(double desired)
    {
        if(!double.IsFinite(desired))return;
        double quantized=Step>0?Mathf.Snapped(desired,Step):desired;quantized=ClampNumber(quantized);
        if(Math.Abs(quantized-Value)<Math.Max(1e-12,Math.Abs(Step)*1e-7))return;
        if(!_gestureStarted){_gestureStarted=true;GestureStarted?.Invoke();}
        Value=desired;
        // An application may constrain the edited result (for example pinned
        // feet). Continue from that actual value rather than accumulating debt.
        if(Math.Abs(Value-quantized)>Math.Max(1e-9,Math.Abs(Step)*.01))_accumulator=Value;
    }
    void FinishArrowGesture()
    {
        if(!_pressed)return;
        if(!_dragged&&Editable){double step=ArrowStep>0?ArrowStep:Step;SetGestureValue(ClampNumber(Value+(_up?step:-step)));}
        _pressed=false;_dragged=false;
        if(_gestureStarted){_gestureStarted=false;GestureEnded?.Invoke();}
    }
    public override void _Notification(int what)
    {
        if(what==NotificationWMWindowFocusOut)FinishArrowGesture();
    }
    public override void _ExitTree(){_dragged=true;FinishArrowGesture();}
}
