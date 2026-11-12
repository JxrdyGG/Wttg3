using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using BepInEx;

namespace DominusMultipliers
{
    internal static class GameAssemblies
    {
        // Engine / third-party libraries that never hold game logic worth hooking.
        private static readonly string[] SkipPrefixes =
        {
            "Unity", "UnityEngine", "UnityEditor", "System", "Mono", "mscorlib", "netstandard", "Microsoft", "BepInEx", "0Harmony",
            "Harmony", "MonoMod", "Il2Cpp", "Newtonsoft", "DOTween", "DemiLib", "Sirenix", "Rewired", "FMOD", "Steamworks", "com.rlabrecque",
            "Facepunch", "Cinemachine", "TextMeshPro", "I18N", "Mono.Cecil", "AsmResolver", "Cpp2IL", "Iced", "Purchasing", "Photon",
            "Spine", "Coffee", "Lean", "Febucci", "NaughtyAttributes", "Zenject", "UniTask", "Cysharp", "MessagePack", "protobuf",
            "Google", "Firebase", "GameAnalytics", "Assembly-CSharp-Editor", "Assembly-UnityScript", "DominusMultipliers", "Galaxy", "Discord"
        };

        public static List<Assembly> Loaded = new List<Assembly>();
        private static readonly Dictionary<string, Type> TypeCache = new Dictionary<string, Type>(StringComparer.OrdinalIgnoreCase);

        /// <summary>Finds a game type by short or full name in the scanned assemblies.</summary>
        public static Type FindType(string name)
        {
            lock (TypeCache)
            {
                if (TypeCache.TryGetValue(name, out var t)) return t;
                t = Loaded.SelectMany(TypesOf).FirstOrDefault(x => x.FullName == name)
                    ?? Loaded.SelectMany(TypesOf).FirstOrDefault(x => x.Name == name);
                TypeCache[name] = t;
                return t;
            }
        }

        public static bool IsEngineType(Type t)
        {
            var ns = t.Namespace ?? "";
            return Ns(ns, "UnityEngine") || Ns(ns, "System") || Ns(ns, "Il2CppSystem") || Ns(ns, "Il2CppInterop") ||
                   IsSkippedName(t.Assembly.GetName().Name);
        }

        private static bool Ns(string ns, string root) => ns == root || ns.StartsWith(root + ".", StringComparison.Ordinal);

        private static bool IsSkippedName(string name) =>
            SkipPrefixes.Any(p => name.StartsWith(p, StringComparison.OrdinalIgnoreCase)) &&
            !name.Equals("Assembly-CSharp", StringComparison.OrdinalIgnoreCase);

        public static List<Assembly> Load(string setting)
        {
            var names = new List<string>();
            setting = (setting ?? "").Trim();
            if (setting.Length == 0 || setting.Equals("auto", StringComparison.OrdinalIgnoreCase))
            {
                foreach (var dir in CandidateDirs())
                {
                    if (!Directory.Exists(dir)) continue;
                    foreach (var file in Directory.GetFiles(dir, "*.dll"))
                    {
                        var n = Path.GetFileNameWithoutExtension(file);
                        if (!IsSkippedName(n) && !names.Contains(n)) names.Add(n);
                    }
                }
                if (!names.Contains("Assembly-CSharp")) names.Insert(0, "Assembly-CSharp");
            }
            else
            {
                names.AddRange(setting.Split(new[] { ';', ',' }, StringSplitOptions.RemoveEmptyEntries).Select(s => s.Trim()));
            }

            var result = new List<Assembly>();
            foreach (var n in names)
            {
                try { result.Add(Assembly.Load(n)); }
                catch (Exception e) { Mod.Log.LogDebug($"Could not load assembly {n}: {e.Message}"); }
            }
            Mod.Log.LogInfo("Scanning assemblies: " + string.Join(", ", result.Select(a => a.GetName().Name).ToArray()));
            return result;
        }

        private static IEnumerable<string> CandidateDirs()
        {
#if IL2CPP
            yield return Path.Combine(Paths.BepInExRootPath, "interop");
#else
            yield return Paths.ManagedPath;
#endif
        }

        public static IEnumerable<Type> TypesOf(Assembly asm)
        {
            Type[] types;
            try { types = asm.GetTypes(); }
            catch (ReflectionTypeLoadException e) { types = e.Types.Where(t => t != null).ToArray(); }
            catch (Exception) { types = Type.EmptyTypes; }
            return types;
        }
    }
}
