using System.Collections.Generic;
using DominusMultipliers.Menu;

/// <summary>Scriptable IGui: set the event, which button to "click", which slider to drag; records what was drawn.</summary>
internal sealed class FakeGui : IGui
{
    public string EventType { get; set; } = "Repaint";
    public string KeyCode { get; set; } = "None";
    public float MouseX { get; set; } = -1;
    public float MouseY { get; set; } = -1;
    public float DeltaX { get; set; }
    public float DeltaY { get; set; }
    public float ScreenWidth { get; set; } = 1920;
    public float ScreenHeight { get; set; } = 1080;

    public string ClickButton;          // text of the button to report as clicked (first match)
    public int SliderIndex = -1;        // which slider (0-based, in draw order) to move...
    public float SliderValue;           // ...to this value
    public readonly List<string> Drawn = new List<string>();
    public bool Used;
    private int _sliders;
    public int FontDepth;

    public void BeginFrame() { Drawn.Clear(); _sliders = 0; Used = false; }
    public void UseEvent() => Used = true;
    public void PushFontSize(int size) => FontDepth++;
    public void PopFontSize() => FontDepth--;
    public void Box(float x, float y, float w, float h, string text) { if (text != "") Drawn.Add("box:" + text); }
    public void Label(float x, float y, float w, float h, string text) => Drawn.Add(text);
    public bool Button(float x, float y, float w, float h, string text)
    {
        Drawn.Add("btn:" + text);
        if (ClickButton != null && text == ClickButton) { ClickButton = null; return true; }
        return false;
    }
    public bool Toggle(float x, float y, float w, float h, bool value, string text) { Drawn.Add("tgl:" + text); return value; }
    public float Slider(float x, float y, float w, float h, float value, float min, float max) =>
        _sliders++ == SliderIndex ? SliderValue : value;
}
