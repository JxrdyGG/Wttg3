using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;

namespace DominusMultipliers.Menu
{
    /// <summary>
    /// Unity IMGUI (UnityEngine.GUI) through reflection. On Mono this binds to the real UnityEngine assemblies, on IL2CPP to
    /// BepInEx's generated interop assemblies, which keep the same type and member names.
    /// </summary>
    internal sealed class ReflectionGui : IGui
    {
        private readonly Type _rect;
        private readonly ConstructorInfo _rectCtor;
        private readonly FieldInfo[] _rectFields; // fallback when Rect has no (x, y, w, h) constructor
        private readonly MethodInfo _box, _label, _button, _toggle, _slider;
        private readonly PropertyInfo _currentEvent, _evType, _evKey, _evMouse, _evDelta;
        private readonly MethodInfo _evUse;
        private readonly PropertyInfo _screenW, _screenH, _skin, _fontSize;
        private readonly PropertyInfo[] _skinStyles;
        private readonly Stack<int[]> _fontStack = new Stack<int[]>();
        private object _event;

        private ReflectionGui()
        {
            var gui = Find("UnityEngine.GUI", "UnityEngine.IMGUIModule");
            var evt = Find("UnityEngine.Event", "UnityEngine.IMGUIModule");
            var skin = Find("UnityEngine.GUISkin", "UnityEngine.IMGUIModule");
            var style = Find("UnityEngine.GUIStyle", "UnityEngine.IMGUIModule");
            var screen = Find("UnityEngine.Screen", "UnityEngine.CoreModule");
            _rect = Find("UnityEngine.Rect", "UnityEngine.CoreModule");

            _rectCtor = _rect.GetConstructor(new[] { typeof(float), typeof(float), typeof(float), typeof(float) });
            if (_rectCtor == null)
                _rectFields = new[] { "m_XMin", "m_YMin", "m_Width", "m_Height" }
                    .Select(n => _rect.GetField(n, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic) ?? throw Missing("Rect." + n))
                    .ToArray();

            _box = Static(gui, "Box", _rect, typeof(string));
            _label = Static(gui, "Label", _rect, typeof(string));
            _button = Static(gui, "Button", _rect, typeof(string));
            _toggle = Static(gui, "Toggle", _rect, typeof(bool), typeof(string));
            _slider = Static(gui, "HorizontalSlider", _rect, typeof(float), typeof(float), typeof(float));

            _currentEvent = Prop(evt, "current");
            _evType = Prop(evt, "type");
            _evKey = Prop(evt, "keyCode");
            _evMouse = Prop(evt, "mousePosition");
            _evDelta = Prop(evt, "delta");
            _evUse = evt.GetMethod("Use", Type.EmptyTypes) ?? throw Missing("Event.Use");

            _screenW = Prop(screen, "width");
            _screenH = Prop(screen, "height");
            _skin = Prop(gui, "skin");
            _fontSize = Prop(style, "fontSize");
            _skinStyles = new[] { "label", "button", "toggle", "box" }.Select(n => Prop(skin, n)).ToArray();
        }

        /// <summary>Returns null (and logs why) if this game build doesn't include the IMGUI calls the menu needs.</summary>
        public static ReflectionGui TryCreate()
        {
            try { return new ReflectionGui(); }
            catch (Exception e)
            {
                Mod.Log.LogWarning("In-game menu unavailable: this game build is missing Unity's IMGUI (" + e.Message + "). " +
                                   "Edit BepInEx/config/" + Mod.Guid + ".cfg instead, or use BepInEx.ConfigurationManager.");
                return null;
            }
        }

        // ---- events ----
        private object Event => _event ?? (_event = _currentEvent.GetValue(null, null));
        public void BeginFrame() => _event = null; // Event.current changes between OnGUI calls

        public string EventType => Event == null ? "" : _evType.GetValue(Event, null).ToString();
        public string KeyCode => Event == null ? "" : _evKey.GetValue(Event, null).ToString();
        public float MouseX => Vec(_evMouse, 0);
        public float MouseY => Vec(_evMouse, 1);
        public float DeltaX => Vec(_evDelta, 0);
        public float DeltaY => Vec(_evDelta, 1);
        public void UseEvent() { if (Event != null) _evUse.Invoke(Event, null); }

        private float Vec(PropertyInfo p, int axis)
        {
            if (Event == null) return 0;
            var v = p.GetValue(Event, null);
            var name = axis == 0 ? "x" : "y";
            var f = v.GetType().GetField(name);
            return Convert.ToSingle(f != null ? f.GetValue(v) : v.GetType().GetProperty(name).GetValue(v, null));
        }

        public float ScreenWidth => Convert.ToSingle(_screenW.GetValue(null, null));
        public float ScreenHeight => Convert.ToSingle(_screenH.GetValue(null, null));

        // ---- fonts ----
        public void PushFontSize(int size)
        {
            var skin = _skin.GetValue(null, null);
            var styles = _skinStyles.Select(p => p.GetValue(skin, null)).ToArray();
            _fontStack.Push(styles.Select(s => (int)_fontSize.GetValue(s, null)).ToArray());
            foreach (var s in styles) _fontSize.SetValue(s, size, null);
        }

        public void PopFontSize()
        {
            if (_fontStack.Count == 0) return;
            var old = _fontStack.Pop();
            var skin = _skin.GetValue(null, null);
            for (int i = 0; i < _skinStyles.Length; i++) _fontSize.SetValue(_skinStyles[i].GetValue(skin, null), old[i], null);
        }

        // ---- controls ----
        public void Box(float x, float y, float w, float h, string text) => _box.Invoke(null, new[] { Rect(x, y, w, h), text });
        public void Label(float x, float y, float w, float h, string text) => _label.Invoke(null, new[] { Rect(x, y, w, h), text });
        public bool Button(float x, float y, float w, float h, string text) => (bool)_button.Invoke(null, new[] { Rect(x, y, w, h), text });
        public bool Toggle(float x, float y, float w, float h, bool value, string text) =>
            (bool)_toggle.Invoke(null, new[] { Rect(x, y, w, h), value, text });
        public float Slider(float x, float y, float w, float h, float value, float min, float max) =>
            (float)_slider.Invoke(null, new[] { Rect(x, y, w, h), value, min, max });

        private object Rect(float x, float y, float w, float h)
        {
            if (_rectCtor != null) return _rectCtor.Invoke(new object[] { x, y, w, h });
            var r = Activator.CreateInstance(_rect);
            _rectFields[0].SetValue(r, x); _rectFields[1].SetValue(r, y); _rectFields[2].SetValue(r, w); _rectFields[3].SetValue(r, h);
            return r;
        }

        // ---- lookup helpers ----
        private static Type Find(string fullName, string assembly)
        {
            foreach (var a in AppDomain.CurrentDomain.GetAssemblies())
            {
                var t = a.GetType(fullName, false);
                if (t != null && !t.IsInterface) return t;
            }
            try { var t = Assembly.Load(assembly).GetType(fullName, false); if (t != null) return t; } catch { }
            throw Missing(fullName);
        }

        private static MethodInfo Static(Type t, string name, params Type[] args) =>
            t.GetMethod(name, BindingFlags.Public | BindingFlags.Static, null, args, null) ?? throw Missing($"{t.Name}.{name}");

        private static PropertyInfo Prop(Type t, string name) =>
            t.GetProperty(name, BindingFlags.Public | BindingFlags.Static | BindingFlags.Instance) ?? throw Missing($"{t.Name}.{name}");

        private static Exception Missing(string what) => new MissingMemberException(what);
    }
}
