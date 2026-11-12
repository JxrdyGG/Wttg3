using System;
using System.Collections.Generic;
using System.Reflection;
using System.Text;

namespace DominusMultipliers
{
    /// <summary>Decides whether a damage receiver belongs to the player, using PlayerSideMembers.</summary>
    internal static class SideResolver
    {
        private sealed class Condition
        {
            public string Raw;
            public string[] Path;
            public readonly HashSet<string> Expected = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        }

        private static readonly List<Condition> Conditions = new List<Condition>();
        private static readonly HashSet<Type> Probed = new HashSet<Type>();

        public static void Configure(string text)
        {
            Conditions.Clear();
            var byPath = new Dictionary<string, Condition>(StringComparer.OrdinalIgnoreCase);
            foreach (var part in (text ?? "").Split(new[] { ';', ',', '\n' }, StringSplitOptions.RemoveEmptyEntries))
            {
                var e = part.Trim();
                if (e.Length == 0) continue;
                string path = e, expected = "true";
                int eq = e.IndexOf('=');
                if (eq >= 0) { path = e.Substring(0, eq).Trim(); expected = e.Substring(eq + 1).Trim(); }
                if (!byPath.TryGetValue(path, out var c))
                {
                    c = new Condition { Raw = path, Path = path.Split('.') };
                    byPath[path] = c;
                    Conditions.Add(c);
                }
                c.Expected.Add(expected);
            }
        }

        /// <summary>true = player's, false = enemy, null = could not tell.</summary>
        public static bool? IsPlayer(object receiver)
        {
            if (receiver == null) return null;
            foreach (var c in Conditions)
            {
                object v;
                try
                {
                    if (!MemberAccess.TryGetPath(receiver, c.Path, out v)) continue;
                }
                catch { continue; }
                // Only simple values can decide; an object-valued member (e.g. 'owner' holding a Player object) is skipped
                // so a dotted condition like owner.isPlayer further down the list can still apply.
                if (v == null || !(v is bool || v is string || v.GetType().IsEnum || Numeric.IsNumeric(v.GetType()))) continue;
                var text = v is bool b ? (b ? "true" : "false") : v.ToString();
                if (c.Expected.Contains(text)) return true;
                if (v.GetType().IsEnum && c.Expected.Contains(Convert.ToInt64(v).ToString())) return true;
                return false;
            }
            ProbeOnce(receiver);
            return null;
        }

        /// <summary>Logs the bool/enum members of an unrecognised receiver type once, to help fill in PlayerSideMembers.</summary>
        private static void ProbeOnce(object receiver)
        {
            var type = receiver.GetType();
            lock (Probed) { if (!Probed.Add(type)) return; }
            var sb = new StringBuilder();
            sb.Append("Could not tell which side '").Append(type.FullName)
              .Append("' is on (none of PlayerSideMembers exist on it). Its bool/enum members right now:");
            sb.Append(DescribeSideMembers(receiver, type));
            sb.Append("\n  -> add the right one to PlayerSideMembers, e.g. isPlayer  or  team=Player  (see README).");
            Mod.Log.LogWarning(sb.ToString());
        }

        public static string DescribeSideMembers(object instance, Type type)
        {
            var sb = new StringBuilder();
            const BindingFlags flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly;
            for (var t = type; t != null && t != typeof(object) && !GameAssemblies.IsEngineType(t); t = t.BaseType)
            {
                foreach (var f in t.GetFields(flags))
                    if (Interesting(f.FieldType)) Append(sb, f.Name, f.FieldType, instance, f);
                foreach (var p in t.GetProperties(flags))
                    if (p.CanRead && p.GetIndexParameters().Length == 0 && Interesting(p.PropertyType)) Append(sb, p.Name, p.PropertyType, instance, p);
            }
            return sb.ToString();
        }

        private static bool Interesting(Type t) => t == typeof(bool) || t.IsEnum;

        private static void Append(StringBuilder sb, string name, Type t, object instance, MemberInfo m)
        {
            sb.Append("\n    ").Append(t.IsEnum ? t.Name : "bool").Append(' ').Append(name);
            if (instance == null) return;
            try { sb.Append(" = ").Append(MemberAccess.Get(m, instance)); } catch { sb.Append(" = <error>"); }
        }
    }
}
