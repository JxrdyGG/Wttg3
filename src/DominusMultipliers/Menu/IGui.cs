namespace DominusMultipliers.Menu
{
    /// <summary>
    /// The handful of Unity IMGUI calls the menu needs, all with absolute positions (no layout options or delegates), so the
    /// same calls work by reflection on both Mono and IL2CPP builds of the game.
    /// </summary>
    internal interface IGui
    {
        string EventType { get; }   // "Layout", "Repaint", "KeyDown", "MouseDown", "MouseUp", "MouseDrag", ...
        string KeyCode { get; }     // e.g. "F8"
        float MouseX { get; }
        float MouseY { get; }
        float DeltaX { get; }
        float DeltaY { get; }
        void UseEvent();
        void BeginFrame();          // called once at the start of every OnGUI

        float ScreenWidth { get; }
        float ScreenHeight { get; }

        void PushFontSize(int size);
        void PopFontSize();

        void Box(float x, float y, float w, float h, string text);
        void Label(float x, float y, float w, float h, string text);
        bool Button(float x, float y, float w, float h, string text);
        bool Toggle(float x, float y, float w, float h, bool value, string text);
        float Slider(float x, float y, float w, float h, float value, float min, float max);
    }
}
