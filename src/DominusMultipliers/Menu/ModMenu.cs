using System;
using System.Collections.Generic;
using System.Linq;
using BepInEx.Configuration;

namespace DominusMultipliers.Menu
{
    /// <summary>The in-game settings window (default key F8). Changes apply immediately and are saved to the .cfg file.</summary>
    internal sealed class ModMenu
    {
        private sealed class Row
        {
            public string Label;
            public ConfigEntry<float> Entry;
            public CategoryDef Def;      // used for the "not hooked" status
            public float Min, Max, Step;
        }

        private sealed class Tab
        {
            public string Name;
            public List<Row> Rows = new List<Row>();
        }

        private const float W = 480, TitleH = 28, RowH = 30;
        private static readonly string[] PresetNames = { "All 1x", "Defaults", "Strong", "Godlike" };

        private readonly IGui _gui;
        private readonly ConfigFile _file;
        private readonly List<Tab> _tabs = new List<Tab>();
        private readonly Dictionary<CategoryDef, int> _hookCounts = new Dictionary<CategoryDef, int>();

        private bool _open, _dragging, _dirty, _disabled, _saveFlag;
        private int _tab, _errors, _hintFrames = 900;
        private float _x = 40, _y = 40;
        private string _hover;

        public bool IsOpen => _open;

        public ModMenu(IGui gui, ConfigFile file)
        {
            _gui = gui;
            _file = file;
            var c = Mod.Cfg;

            Row R(string label, Category cat) => MakeRow(label, c[cat].Multiplier, c[cat]);
            _tabs.Add(new Tab { Name = "Economy", Rows = { R("Money gained", Category.Money), R("Purchase costs", Category.Cost), R("Sell prices", Category.SellPrice) } });
            _tabs.Add(new Tab
            {
                Name = "Combat",
                Rows =
                {
                    R("Damage dealt", Category.Damage),
                    MakeRow("Defense (dmg taken /)", c.DefenseMultiplier, c[Category.Damage]),
                    R("Max health", Category.MaxHealth),
                    R("Hit/crit/dodge chance", Category.CombatChance),
                }
            });
            _tabs.Add(new Tab
            {
                Name = "Care",
                Rows =
                {
                    R("Healing", Category.Healing), R("Stamina regen", Category.Stamina), R("Stamina cost", Category.StaminaCost),
                    R("Fatigue", Category.Fatigue), R("Injury & death chance", Category.Injury),
                }
            });
            _tabs.Add(new Tab
            {
                Name = "Progress",
                Rows = { R("Training / XP", Category.Training), R("Fame & crowd favor", Category.Fame), R("Morale & loyalty", Category.Morale), R("Durations", Category.Duration) }
            });
            _tabs.Add(new Tab { Name = "Info" });
        }

        private static Row MakeRow(string label, ConfigEntry<float> entry, CategoryDef def)
        {
            // Things you want less of (costs, fatigue, durations...) get a 0-1 slider; gains get 0-10. Bigger values: +/- or the .cfg.
            bool reducer = (float)entry.DefaultValue < 1f;
            return reducer
                ? new Row { Label = label, Entry = entry, Def = def, Min = 0f, Max = 1f, Step = 0.05f }
                : new Row { Label = label, Entry = entry, Def = def, Min = 0f, Max = 10f, Step = 0.25f };
        }

        // ------------------------------------------------------------------------------------------------------------

        public void OnGUI()
        {
            if (_disabled) return;
            try
            {
                _gui.BeginFrame();
                Draw();
            }
            catch (Exception e)
            {
                Mod.Log.LogError("Menu error: " + e);
                if (++_errors >= 3)
                {
                    _disabled = true;
                    Mod.Log.LogError("In-game menu disabled after repeated errors. Edit the .cfg file instead.");
                }
            }
        }

        private void Draw()
        {
            var cfg = Mod.Cfg;
            string ev = _gui.EventType;
            if (ev == "KeyDown" && string.Equals(_gui.KeyCode, cfg.MenuKey.Value.Trim(), StringComparison.OrdinalIgnoreCase))
            {
                SetOpen(!_open);
                _gui.UseEvent();
                return;
            }

            float s = Scale();
            if (!_open)
            {
                if (cfg.ShowStartupHint.Value && _hintFrames > 0)
                {
                    if (ev == "Repaint") _hintFrames--;
                    _gui.PushFontSize((int)Math.Round(13 * s));
                    try { _gui.Box(10, 10, 360 * s, 26 * s, $"Dominus Multipliers loaded - press {cfg.MenuKey.Value} for the menu"); }
                    finally { _gui.PopFontSize(); }
                }
                return;
            }

            _gui.PushFontSize((int)Math.Round(13 * s));
            try { DrawWindow(s, ev); }
            finally { _gui.PopFontSize(); }
        }

        private void DrawWindow(float s, string ev)
        {
            var cfg = Mod.Cfg;
            var tab = _tabs[_tab];
            float contentH = tab.Rows.Count > 0 ? tab.Rows.Count * RowH : InfoLines().Count * 20 + 4;
            float h = TitleH + 32 + 30 + contentH + 64;

            // Keep the window on screen and let the title bar drag it.
            _x = Clamp(_x, 0, Math.Max(0, _gui.ScreenWidth - W * s));
            _y = Clamp(_y, 0, Math.Max(0, _gui.ScreenHeight - h * s));
            HandleDrag(s, ev);

            for (int i = 0; i < 3; i++) Box(0, 0, W, h, ""); // stacked boxes = more opaque background
            Label(10, 5, W - 50, 20, $"{Mod.Name} {Mod.Version}   (drag to move)");
            if (Button(W - 34, 4, 26, 20, "X")) { SetOpen(false); return; }

            // Tabs
            float tw = (W - 20) / _tabs.Count;
            for (int i = 0; i < _tabs.Count; i++)
                if (Button(10 + i * tw, TitleH + 2, tw - 4, 24, i == _tab ? $"[ {_tabs[i].Name} ]" : _tabs[i].Name)) _tab = i;

            // Master switch + presets
            float y = TitleH + 32;
            bool enabled = Toggle(10, y + 2, 130, 22, cfg.Enabled.Value, " Mod enabled");
            if (enabled != cfg.Enabled.Value) { cfg.Enabled.Value = enabled; _dirty = true; }
            for (int i = 0; i < PresetNames.Length; i++)
                if (Button(145 + i * 82, y, 78, 24, PresetNames[i])) ApplyPreset(PresetNames[i]);
            y += 30;

            _hover = null;
            if (tab.Rows.Count > 0)
            {
                foreach (var row in tab.Rows) { DrawRow(row, y, s); y += RowH; }
            }
            else
            {
                foreach (var line in InfoLines()) { Label(10, y, W - 20, 20, line); y += 20; }
                y += 4;
            }

            // Footer: description of the hovered row, then save status.
            Label(10, y + 2, W - 20, 40, _hover ?? "Hover a setting for details. Changes apply instantly.");
            Label(10, y + 42, W - 20, 20,
                $"{(_dirty ? "Unsaved changes (saved on release / close)" : "Saved to config/" + Mod.Guid + ".cfg")}   |   {cfg.MenuKey.Value} closes");

            if (_dirty && (ev == "MouseUp" || !_open)) Save();
        }

        private void DrawRow(Row row, float y, float s)
        {
            int hooks = HookCount(row.Def);
            float value = row.Entry.Value;
            Label(10, y + 3, 178, 22, hooks > 0 ? row.Label : row.Label + " (!)");

            if (Button(190, y + 2, 24, 22, "-")) Set(row, value - row.Step);
            float shown = Clamp(value, row.Min, row.Max);
            float slid = Slider(220, y + 9, 160, 16, shown, row.Min, row.Max);
            if (Math.Abs(slid - shown) > 1e-4f) Set(row, slid);
            if (Button(386, y + 2, 24, 22, "+")) Set(row, value + row.Step);
            Label(416, y + 3, 60, 22, Math.Abs(value - 1f) < 1e-4f ? "off" : "x" + value.ToString("0.##"));

            if (Inside(0, y, W, RowH, s))
            {
                var desc = row.Entry.Description?.Description ?? "";
                _hover = hooks > 0
                    ? desc
                    : "(!) Not connected to the game yet, so this does nothing. See the Info tab. " + desc;
            }
        }

        private List<string> InfoLines()
        {
            var lines = new List<string> { "Game methods connected per multiplier:" };
            var defs = Mod.Cfg.Categories;
            for (int i = 0; i < defs.Count; i += 3)
                lines.Add("   " + string.Join("     ", defs.Skip(i).Take(3).Select(d => $"{d.Name}: {HookCount(d)}").ToArray()));
            lines.Add("");
            lines.Add("(!) = 0 methods connected: that multiplier does nothing yet.");
            lines.Add("Fix: open BepInEx/DominusMultipliers-dump.txt, find the game's");
            lines.Add("method and add it to the matching *Rules line in the .cfg");
            lines.Add("(see README), then restart the game.");
            return lines;
        }

        // ------------------------------------------------------------------------------------------------------------

        private void Set(Row row, float v)
        {
            float snapped = (float)Math.Round(Math.Round(v / row.Step) * row.Step, 4);
            if (snapped < 0) snapped = 0;
            if (Math.Abs(snapped - row.Entry.Value) < 1e-5f) return;
            row.Entry.Value = snapped; // AcceptableValueRange clamps (e.g. Defense >= 0.01)
            _dirty = true;
        }

        internal void ApplyPreset(string name)
        {
            var c = Mod.Cfg;
            if (name == "Defaults")
            {
                foreach (var d in c.Categories) d.Multiplier.Value = (float)d.Multiplier.DefaultValue;
                c.DefenseMultiplier.Value = (float)c.DefenseMultiplier.DefaultValue;
            }
            else
            {
                // Money, Cost, Sell, Damage, Defense, MaxHealth, Chance, Heal, StamRegen, StamCost, Fatigue, Injury, Training, Fame, Morale, Duration
                float[] v =
                    name == "All 1x" ? Enumerable.Repeat(1f, 16).ToArray() :
                    name == "Strong" ? new[] { 5f, 0.25f, 3f, 3f, 3f, 2f, 1.5f, 5f, 3f, 0.25f, 0.25f, 0.1f, 5f, 5f, 5f, 0.25f } :
                    name == "Godlike" ? new[] { 50f, 0.01f, 10f, 20f, 50f, 5f, 3f, 20f, 10f, 0f, 0f, 0f, 20f, 20f, 20f, 0.1f } :
                    throw new ArgumentException(name);
                Category[] order =
                {
                    Category.Money, Category.Cost, Category.SellPrice, Category.Damage, Category.Damage, Category.MaxHealth,
                    Category.CombatChance, Category.Healing, Category.Stamina, Category.StaminaCost, Category.Fatigue, Category.Injury,
                    Category.Training, Category.Fame, Category.Morale, Category.Duration,
                };
                for (int i = 0; i < order.Length; i++)
                {
                    if (i == 4) c.DefenseMultiplier.Value = v[i];
                    else c[order[i]].Multiplier.Value = v[i];
                }
            }
            _dirty = true;
            Save();
        }

        private void SetOpen(bool open)
        {
            if (open == _open) return;
            _open = open;
            _dragging = false;
            if (open)
            {
                _hintFrames = 0;
                _saveFlag = _file.SaveOnConfigSet;
                _file.SaveOnConfigSet = false; // don't rewrite the file on every slider tick
            }
            else
            {
                if (_dirty) Save();
                _file.SaveOnConfigSet = _saveFlag;
            }
        }

        private void Save()
        {
            _file.Save();
            _dirty = false;
        }

        private int HookCount(CategoryDef def)
        {
            if (!_hookCounts.TryGetValue(def, out var n))
                _hookCounts[def] = n = Patcher.Bindings.Values.Count(b => b.Def == def);
            return n;
        }

        private float Scale()
        {
            float s = Mod.Cfg.MenuScale.Value;
            if (s <= 0) s = Math.Max(1f, _gui.ScreenHeight / 1080f); // auto: grow on 1440p/4K screens
            return Clamp(s, 0.5f, 4f);
        }

        private void HandleDrag(float s, string ev)
        {
            if (ev == "MouseDown" && Inside(0, 0, W - 40, TitleH, s)) { _dragging = true; _gui.UseEvent(); }
            else if (ev == "MouseDrag" && _dragging) { _x += _gui.DeltaX; _y += _gui.DeltaY; _gui.UseEvent(); }
            else if (ev == "MouseUp") _dragging = false;
        }

        // ---- scaled drawing helpers (coordinates are relative to the window, in unscaled units) ----
        private float S => Scale();
        private bool Inside(float x, float y, float w, float h, float s)
        {
            float mx = _gui.MouseX, my = _gui.MouseY;
            return mx >= _x + x * s && mx <= _x + (x + w) * s && my >= _y + y * s && my <= _y + (y + h) * s;
        }
        private void Box(float x, float y, float w, float h, string t) { var s = S; _gui.Box(_x + x * s, _y + y * s, w * s, h * s, t); }
        private void Label(float x, float y, float w, float h, string t) { var s = S; _gui.Label(_x + x * s, _y + y * s, w * s, h * s, t); }
        private bool Button(float x, float y, float w, float h, string t) { var s = S; return _gui.Button(_x + x * s, _y + y * s, w * s, h * s, t); }
        private bool Toggle(float x, float y, float w, float h, bool v, string t) { var s = S; return _gui.Toggle(_x + x * s, _y + y * s, w * s, h * s, v, t); }
        private float Slider(float x, float y, float w, float h, float v, float min, float max)
        { var s = S; return _gui.Slider(_x + x * s, _y + y * s, w * s, h * s, v, min, max); }

        private static float Clamp(float v, float min, float max) => v < min ? min : v > max ? max : v;
    }
}
