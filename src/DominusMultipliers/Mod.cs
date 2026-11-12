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
        public const string Version = "1.0.0";

        internal static ManualLogSource Log;
        internal static ModConfig Cfg;

        internal static void Init(ConfigFile config, ManualLogSource log, List<Assembly> assemblies = null)
        {
            Log = log;
            Cfg = new ModConfig(config);
            SideResolver.Configure(Cfg.PlayerSideMembers.Value);

            var harmony = new Harmony(Guid);
            assemblies = assemblies ?? GameAssemblies.Load(Cfg.Assemblies.Value);
            int patched = Patcher.PatchAll(harmony, assemblies);

            Log.LogInfo($"{Name} {Version}: hooked {patched} method(s). " +
                        $"Money x{Cfg.MoneyMultiplier.Value}, Damage x{Cfg.DamageMultiplier.Value}, " +
                        $"Defense x{Cfg.DefenseMultiplier.Value}, Training x{Cfg.TrainingMultiplier.Value}");

            if (Cfg.WriteDump.Value)
                Dumper.Write(assemblies);
        }
    }
}
