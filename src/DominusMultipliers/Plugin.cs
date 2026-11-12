using BepInEx;
#if IL2CPP
using BepInEx.Unity.IL2CPP;
#endif

namespace DominusMultipliers
{
    [BepInPlugin(Mod.Guid, Mod.Name, Mod.Version)]
#if IL2CPP
    public sealed class Plugin : BasePlugin
    {
        public override void Load() => Mod.Init(Config, Log);
    }
#else
    public sealed class Plugin : BaseUnityPlugin
    {
        private void Awake() => Mod.Init(Config, Logger);
    }
#endif
}
