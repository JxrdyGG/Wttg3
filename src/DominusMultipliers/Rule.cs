using System;
using System.Collections.Generic;
using System.Reflection;
using System.Text.RegularExpressions;

namespace DominusMultipliers
{
    internal enum TargetKind { FirstNumeric, AllNumeric, Parameter, Return, Delta }

    /// <summary>One "TypeGlob::MethodGlob[@target][!side]" entry from the config.</summary>
    internal sealed class Rule
    {
        public string Raw;
        public Regex TypeRx;
        public Regex MethodRx;
        public TargetKind Kind = TargetKind.FirstNumeric;
        public string ParamSelector;   // name or index, for TargetKind.Parameter
        public string[] MemberPath;    // optional .a.b path inside the selected parameter
        public string SideSelector;    // null = instance (or first object param for static methods)
        public bool BuiltIn;           // built-in game rule: precise, so ExcludeRules don't apply

        public static List<Rule> ParseList(string text)
        {
            var list = new List<Rule>();
            if (string.IsNullOrEmpty(text)) return list;
            foreach (var part in text.Split(new[] { ';', '\n' }, StringSplitOptions.RemoveEmptyEntries))
            {
                var entry = part.Trim();
                if (entry.Length == 0) continue;
                try { list.Add(Parse(entry)); }
                catch (Exception e) { Mod.Log.LogWarning($"Ignoring bad rule '{entry}': {e.Message}"); }
            }
            return list;
        }

        public static Rule Parse(string entry)
        {
            var r = new Rule { Raw = entry };
            var s = entry;

            int bang = s.LastIndexOf('!');
            if (bang >= 0) { r.SideSelector = s.Substring(bang + 1).Trim(); s = s.Substring(0, bang); }

            int at = s.IndexOf('@');
            if (at >= 0)
            {
                var target = s.Substring(at + 1).Trim();
                s = s.Substring(0, at);
                switch (target.ToLowerInvariant())
                {
                    case "": case "first": r.Kind = TargetKind.FirstNumeric; break;
                    case "all": r.Kind = TargetKind.AllNumeric; break;
                    case "return": case "result": r.Kind = TargetKind.Return; break;
                    case "delta": r.Kind = TargetKind.Delta; break;
                    default:
                        r.Kind = TargetKind.Parameter;
                        var segs = target.Split('.');
                        r.ParamSelector = segs[0];
                        if (segs.Length > 1) { r.MemberPath = new string[segs.Length - 1]; Array.Copy(segs, 1, r.MemberPath, 0, segs.Length - 1); }
                        break;
                }
            }

            string typeGlob = "*", methodGlob;
            int sep = s.IndexOf("::", StringComparison.Ordinal);
            if (sep >= 0) { typeGlob = s.Substring(0, sep).Trim(); methodGlob = s.Substring(sep + 2).Trim(); }
            else methodGlob = s.Trim();
            if (methodGlob.Length == 0) throw new FormatException("missing method name");
            if (typeGlob.Length == 0) typeGlob = "*";

            r.TypeRx = Glob(typeGlob);
            r.MethodRx = Glob(methodGlob);
            return r;
        }

        public bool Matches(Type type, MethodBase method) =>
            MethodRx.IsMatch(method.Name) && (TypeRx.IsMatch(type.Name) || TypeRx.IsMatch(type.FullName ?? type.Name));

        private static Regex Glob(string glob) =>
            new Regex("^" + Regex.Escape(glob).Replace("\\*", ".*").Replace("\\?", ".") + "$",
                RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
    }
}
