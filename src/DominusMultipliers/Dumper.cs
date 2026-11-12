using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Text.RegularExpressions;
using BepInEx;

namespace DominusMultipliers
{
    /// <summary>
    /// Writes BepInEx/DominusMultipliers-dump.txt: what got hooked, plus every game method/member whose name suggests
    /// money, combat or training, so the rules can be pointed at the right methods for the current game version.
    /// </summary>
    internal static class Dumper
    {
        private static readonly KeyValuePair<string, Regex>[] Sections =
        {
            Kw("Money / prices", "gold|money|denar|coin|currency|wallet|treasur|income|reward|wage|salary|payment|prize|purse|cash|fund|earn|bribe|sponsor|price|cost|sell|spend|buy|purchase|upkeep|bet"),
            Kw("Combat", "damage|dmg|hit|wound|armou?r|defen|block|parry|dodge|evade|evasion|attack|strike|resist|mitigat|crit|accura|maxhealth|maxhp"),
            Kw("Health / stamina / injuries", "health|hp$|^hp|heal|regen|recover|stamina|energy|fatigue|exhaust|tired|stress|injur|death|die|maim|hurt"),
            Kw("Training", "train|exp$|^exp|experience|xp|skill|stat|attribute|level|progress|improve|learn|practice|drill|potential"),
            Kw("Fame / morale", "fame|reput|popular|renown|prestige|influence|favor|favour|crowd|hype|glory|honou?r|appeal|morale|loyal|happi|motivat|mood|obedien|disciplin|confiden|trust"),
            Kw("Durations", "duration|time$|days$|delay|cooldown|recoverytime|buildtime|constructiontime"),
        };
        private static readonly Regex CombatantType = new Regex(
            "gladiator|fighter|unit|combatant|character|warrior|actor|entity|health|team|battle|arena|duel|participant|slave|ludus|owner|lanista|rival|faction",
            RegexOptions.IgnoreCase);

        private static KeyValuePair<string, Regex> Kw(string name, string rx) =>
            new KeyValuePair<string, Regex>(name, new Regex(rx, RegexOptions.IgnoreCase | RegexOptions.CultureInvariant));

        public static void Write(List<Assembly> assemblies, string directory = null)
        {
            try
            {
                var path = Path.Combine(directory ?? Paths.BepInExRootPath, "DominusMultipliers-dump.txt");
                File.WriteAllText(path, Build(assemblies), Encoding.UTF8);
                Mod.Log.LogInfo("Wrote " + path);
            }
            catch (Exception e) { Mod.Log.LogWarning("Could not write dump: " + e.Message); }
        }

        private static string Build(List<Assembly> assemblies)
        {
            var sb = new StringBuilder();
            sb.AppendLine($"{Mod.Name} {Mod.Version} dump - {DateTime.Now:yyyy-MM-dd HH:mm}");
            sb.AppendLine("Use the names below in the *Rules settings of BepInEx/config/" + Mod.Guid + ".cfg (format Type::Method). See README.md.");
            sb.AppendLine();

            sb.AppendLine("=== HOOKED METHODS ===");
            if (Patcher.Bindings.Count == 0) sb.AppendLine("  (none)");
            foreach (var b in Patcher.Bindings.Values.OrderBy(b => b.Category).ThenBy(b => b.Method.DeclaringType?.FullName))
                sb.AppendLine("  " + b.Describe());
            sb.AppendLine();

            var types = assemblies.SelectMany(GameAssemblies.TypesOf)
                .Where(t => !t.IsInterface && !GameAssemblies.IsEngineType(t) && !t.Name.StartsWith("<", StringComparison.Ordinal))
                .OrderBy(t => t.FullName).ToList();
            const BindingFlags all = BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly;

            foreach (var section in Sections)
            {
                sb.AppendLine($"=== CANDIDATE METHODS: {section.Key} ===");
                sb.AppendLine("  (* = already hooked)");
                foreach (var t in types)
                {
                    bool typeHit = section.Value.IsMatch(t.Name);
                    MethodInfo[] methods;
                    try { methods = t.GetMethods(all); } catch { continue; }
                    var hits = methods.Where(m => !m.Name.StartsWith("<", StringComparison.Ordinal) &&
                                                  (section.Value.IsMatch(m.Name) ||
                                                   (typeHit && (m.GetParameters().Any(p => Numeric.IsNumeric(p.ParameterType)) || Numeric.IsNumeric(m.ReturnType)))))
                                      .ToList();
                    if (hits.Count == 0) continue;
                    sb.AppendLine("  " + t.FullName);
                    foreach (var m in hits)
                        sb.AppendLine($"    {(Patcher.Bindings.ContainsKey(m) ? "*" : " ")} {Short(m)}");
                }
                sb.AppendLine();
            }

            sb.AppendLine("=== POSSIBLE SIDE MEMBERS (for PlayerSideMembers) ===");
            sb.AppendLine("  bool/enum members of types that look like combatants or that own a hooked method:");
            var damageTypes = new HashSet<Type>(Patcher.Bindings.Values.Select(b => b.Method.DeclaringType));
            foreach (var t in types.Where(t => damageTypes.Contains(t) || CombatantType.IsMatch(t.Name)))
            {
                var members = SideResolver.DescribeSideMembers(null, t);
                if (members.Length == 0) continue;
                sb.Append("  ").Append(t.FullName).AppendLine(members);
                foreach (var e in EnumTypesOf(t)) sb.AppendLine($"      enum {e.Name} = {string.Join(", ", Enum.GetNames(e))}");
            }
            return sb.ToString();
        }

        private static IEnumerable<Type> EnumTypesOf(Type t)
        {
            const BindingFlags flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly;
            var set = new HashSet<Type>();
            for (var c = t; c != null && c != typeof(object) && !GameAssemblies.IsEngineType(c); c = c.BaseType)
            {
                foreach (var f in c.GetFields(flags)) if (f.FieldType.IsEnum) set.Add(f.FieldType);
                foreach (var p in c.GetProperties(flags)) if (p.PropertyType.IsEnum) set.Add(p.PropertyType);
            }
            return set;
        }

        private static string Short(MethodInfo m)
        {
            var ps = string.Join(", ", m.GetParameters().Select(p => p.ParameterType.Name + " " + p.Name).ToArray());
            return $"{(m.IsStatic ? "static " : "")}{m.ReturnType.Name} {m.Name}({ps})";
        }
    }
}
