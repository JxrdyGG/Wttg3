using System.Collections.Generic;
using System.Reflection;
using BepInEx.Configuration;
using BepInEx.Logging;
using HarmonyLib;

namespace DominusMultipliers
{
    /// <summary>Shared state and startup, independent of the BepInEx 5 / 6 plugin base class.</summary>
    internal static class Mod
    {
        public const string Guid = "dominus.multipliers";
        public const string Name = "Dominus Multipliers";
        public const string Version = "1.3.0";

        internal static ManualLogSource Log;
        internal static ModConfig Cfg;
        internal static ConfigFile File;
        internal static Menu.ModMenu Menu;

        internal static void Init(ConfigFile config, ManualLogSource log, List<Assembly> assemblies = null)
        {
            Log = log;
            File = config;
            Cfg = new ModConfig(config);
            SideResolver.Configure();
            Cfg.PlayerTeam.SettingChanged += (s, e) => SideResolver.Configure();

            var harmony = new Harmony(Guid);
            assemblies = assemblies ?? GameAssemblies.Load(Cfg.Assemblies.Value);
            GameAssemblies.Loaded = assemblies;
            int patched = Patcher.PatchAll(harmony, assemblies);

            var summary = new List<string>();
            foreach (var c in Cfg.Categories) summary.Add($"{c.Name} x{c.Multiplier.Value}");
            summary.Insert(1, $"Defense x{Cfg.DefenseMultiplier.Value}");
            Log.LogInfo($"{Name} {Version}: hooked {patched} method(s). " + string.Join(", ", summary.ToArray()));

            if (Cfg.WriteDump.Value)
                Dumper.Write(assemblies);
        }

        /// <summary>Called from Unity's OnGUI. The menu is created on first use, once Unity's GUI is up.</summary>
        internal static void OnGUI()
        {
            if (Menu == null)
            {
                if (_menuUnavailable) return;
                var gui = DominusMultipliers.Menu.ReflectionGui.TryCreate();
                if (gui == null) { _menuUnavailable = true; return; }
                Menu = new Menu.ModMenu(gui, File);
            }
            Menu.OnGUI();
        }

        private static bool _menuUnavailable;
    }
}
