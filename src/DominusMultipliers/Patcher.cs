using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text.RegularExpressions;
using HarmonyLib;

namespace DominusMultipliers
{
    /// <summary>A hooked game method and how to scale it.</summary>
    internal sealed class Binding
    {
        public Category Category;
        public Rule Rule;
        public MethodBase Method;
        public TargetKind Kind;
        public int[] ParamIndices = new int[0];   // for FirstNumeric/AllNumeric/Parameter/Delta
        public string[] MemberPath;               // for Parameter with .path
        public MethodInfo Getter;                 // for Delta
        public int SideParam = -1;                // -1 = use instance
        public int LoggedCalls;
        public int LoggedErrors;

        public string Describe()
        {
            var ps = Method.GetParameters();
            string target;
            switch (Kind)
            {
                case TargetKind.Return: target = "return value"; break;
                case TargetKind.Delta: target = "delta of " + Method.Name.Substring(4); break;
                default:
                    target = string.Join(", ", ParamIndices.Select(i => ps[i].Name).ToArray());
                    if (MemberPath != null) target += "." + string.Join(".", MemberPath);
                    break;
            }
            var side = Category == Category.Damage ? $", receiver={(SideParam < 0 ? "this" : ps[SideParam].Name)}" : "";
            return $"[{Category}] {Signature(Method)}  scales: {target}{side}  (rule {Rule.Raw})";
        }

        public static string Signature(MethodBase m)
        {
            var ret = m is MethodInfo mi ? mi.ReturnType.Name + " " : "";
            var ps = string.Join(", ", m.GetParameters().Select(p => p.ParameterType.Name + " " + p.Name).ToArray());
            return $"{ret}{m.DeclaringType?.FullName}::{m.Name}({ps})";
        }
    }

    internal static class Patcher
    {
        public static readonly Dictionary<MethodBase, Binding> Bindings = new Dictionary<MethodBase, Binding>();

        // When a rule doesn't name a parameter, prefer ones that look like an amount and avoid ones that look like an index.
        private static readonly Regex AmountName = new Regex(
            "amount|amt|value|val$|gold|money|denar|coin|cash|currency|exp|xp|damage|dmg|point|gain|delta|progress|qty|quantity|sum|add|reward|income|hit|hp|health|change|diff|increase|inc$|num$|^v$|^x$|^a$|^n$|^d$",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
        private static readonly Regex IndexName = new Regex(
            "index|idx|^id$|id$|slot|type|kind|level|lvl|count|day|week|turn|seed|time|frame|layer|mask|flags|tier|rank|order|position|pos$",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

        public static int PatchAll(Harmony harmony, List<Assembly> assemblies)
        {
            var cfg = Mod.Cfg;
            var ruleSets = new[]
            {
                new KeyValuePair<Category, List<Rule>>(Category.Money, Rule.ParseList(cfg.MoneyRules.Value)),
                new KeyValuePair<Category, List<Rule>>(Category.Damage, Rule.ParseList(cfg.DamageRules.Value)),
                new KeyValuePair<Category, List<Rule>>(Category.Training, Rule.ParseList(cfg.TrainingRules.Value)),
            };
            var excludes = Rule.ParseList(cfg.ExcludeRules.Value);

            var prefix = new HarmonyMethod(AccessTools.Method(typeof(Hooks), nameof(Hooks.Prefix)));
            var finalizer = new HarmonyMethod(AccessTools.Method(typeof(Hooks), nameof(Hooks.Finalizer)));

            const BindingFlags flags = BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public |
                                       BindingFlags.NonPublic | BindingFlags.DeclaredOnly;
            int count = 0;
            foreach (var asm in assemblies)
            foreach (var type in GameAssemblies.TypesOf(asm))
            {
                if (type.IsInterface || type.ContainsGenericParameters || GameAssemblies.IsEngineType(type)) continue;
                MethodInfo[] methods;
                try { methods = type.GetMethods(flags); } catch { continue; }

                foreach (var method in methods)
                {
                    if (method.IsAbstract || method.ContainsGenericParameters || Bindings.ContainsKey(method)) continue;
                    if (excludes.Any(r => r.Matches(type, method))) continue;

                    Binding binding = null;
                    foreach (var set in ruleSets)
                    {
                        foreach (var rule in set.Value)
                        {
                            if (!rule.Matches(type, method)) continue;
                            binding = Bind(set.Key, rule, method);
                            if (binding != null) break;
                        }
                        if (binding != null) break;
                    }
                    if (binding == null) continue;

                    try
                    {
                        HarmonyMethod postfix = binding.Kind == TargetKind.Return
                            ? new HarmonyMethod(Hooks.PostfixFor(((MethodInfo)method).ReturnType))
                            : null;
                        Bindings[method] = binding;
                        harmony.Patch(method, prefix: prefix, postfix: postfix, finalizer: finalizer, ilmanipulator: null);
                        count++;
                        Mod.Log.LogInfo("Hooked " + binding.Describe());
                    }
                    catch (Exception e)
                    {
                        Bindings.Remove(method);
                        Mod.Log.LogWarning($"Failed to hook {Binding.Signature(method)}: {e.Message}");
                    }
                }
            }

            foreach (var set in ruleSets)
                if (!Bindings.Values.Any(b => b.Category == set.Key))
                    Mod.Log.LogWarning($"No {set.Key} methods were hooked. Open BepInEx/DominusMultipliers-dump.txt " +
                                          $"to find the right method and add it to {set.Key}Rules (see README).");
            return count;
        }

        /// <summary>Works out what to scale on <paramref name="method"/>, or returns null if the rule can't apply to it.</summary>
        private static Binding Bind(Category cat, Rule rule, MethodInfo method)
        {
            var ps = method.GetParameters();
            var b = new Binding { Category = cat, Rule = rule, Method = method, Kind = rule.Kind, MemberPath = rule.MemberPath };
            bool Usable(ParameterInfo p) => !p.ParameterType.IsByRef && !p.IsOut && Numeric.IsNumeric(p.ParameterType);

            switch (rule.Kind)
            {
                case TargetKind.Return:
                    if (!Numeric.IsNumeric(method.ReturnType)) return Skip(rule, method, "return type is not numeric");
                    break;

                case TargetKind.AllNumeric:
                    b.ParamIndices = ps.Where(Usable).Select(p => p.Position).ToArray();
                    if (b.ParamIndices.Length == 0) return Skip(rule, method, "no numeric parameters");
                    break;

                case TargetKind.FirstNumeric:
                {
                    var numeric = ps.Where(Usable).ToList();
                    var pick = numeric.Count == 1 && !IndexName.IsMatch(numeric[0].Name ?? "") ? numeric[0]
                        : numeric.FirstOrDefault(p => AmountName.IsMatch(p.Name ?? "") && !IndexName.IsMatch(p.Name ?? ""))
                          ?? numeric.FirstOrDefault(p => !IndexName.IsMatch(p.Name ?? ""));
                    if (pick == null) return Skip(rule, method, "no numeric parameter that looks like an amount");
                    b.ParamIndices = new[] { pick.Position };
                    break;
                }

                case TargetKind.Parameter:
                {
                    var p = FindParam(ps, rule.ParamSelector);
                    if (p == null || p.ParameterType.IsByRef) return Skip(rule, method, $"no by-value parameter '{rule.ParamSelector}'");
                    if (rule.MemberPath == null && !Numeric.IsNumeric(p.ParameterType)) return Skip(rule, method, $"parameter '{p.Name}' is not numeric");
                    b.ParamIndices = new[] { p.Position };
                    break;
                }

                case TargetKind.Delta:
                {
                    if (!method.Name.StartsWith("set_", StringComparison.Ordinal) || ps.Length != 1 || !Usable(ps[0]))
                        return Skip(rule, method, "@delta needs a numeric property setter (set_X)");
                    b.Getter = method.DeclaringType.GetMethod("get_" + method.Name.Substring(4),
                        BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic, null, Type.EmptyTypes, null);
                    if (b.Getter == null) return Skip(rule, method, "no matching getter");
                    b.ParamIndices = new[] { 0 };
                    break;
                }
            }

            if (cat == Category.Damage)
            {
                if (!string.IsNullOrEmpty(rule.SideSelector) && !rule.SideSelector.Equals("this", StringComparison.OrdinalIgnoreCase))
                {
                    var sp = FindParam(ps, rule.SideSelector);
                    if (sp == null) return Skip(rule, method, $"no receiver parameter '{rule.SideSelector}'");
                    b.SideParam = sp.Position;
                }
                else if (method.IsStatic)
                {
                    var sp = ps.FirstOrDefault(p => !p.ParameterType.IsValueType && p.ParameterType != typeof(string));
                    if (sp == null) return Skip(rule, method, "static method with no object parameter to use as the receiver");
                    b.SideParam = sp.Position;
                }
            }
            return b;
        }

        private static ParameterInfo FindParam(ParameterInfo[] ps, string selector)
        {
            if (int.TryParse(selector, out var idx)) return idx >= 0 && idx < ps.Length ? ps[idx] : null;
            return ps.FirstOrDefault(p => string.Equals(p.Name, selector, StringComparison.OrdinalIgnoreCase));
        }

        private static Binding Skip(Rule rule, MethodBase method, string why)
        {
            Mod.Log.LogDebug($"Rule '{rule.Raw}' matched {Binding.Signature(method)} but was skipped: {why}");
            return null;
        }
    }
}
