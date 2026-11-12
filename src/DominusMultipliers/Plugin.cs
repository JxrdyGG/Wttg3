using BepInEx;
#if IL2CPP
using System;
using BepInEx.Unity.IL2CPP;
#endif

namespace DominusMultipliers
{
    [BepInPlugin(Mod.Guid, Mod.Name, Mod.Version)]
#if IL2CPP
    public sealed class Plugin : BasePlugin
    {
        public override void Load()
        {
            Mod.Init(Config, Log);
            try { AddComponent<MenuBehaviour>(); }
            catch (Exception e) { Log.LogWarning("In-game menu unavailable (could not attach to Unity): " + e.Message); }
        }
    }

    /// <summary>
    /// Unity component that gives the IL2CPP build an OnGUI callback. Compiled against a stand-in MonoBehaviour
    /// (build/IL2CPP/stubs); at runtime it binds to the game's real UnityEngine.MonoBehaviour from BepInEx/interop.
    /// </summary>
    public sealed class MenuBehaviour : UnityEngine.MonoBehaviour
    {
        public MenuBehaviour(IntPtr pointer) : base(pointer) { }
        public void OnGUI() => Mod.OnGUI();
    }
#else
    public sealed class Plugin : BaseUnityPlugin
    {
        private void Awake() => Mod.Init(Config, Logger);
        private void OnGUI() => Mod.OnGUI();
    }
#endif
}
