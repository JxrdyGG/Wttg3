using System;
using System.Reflection;
using HarmonyLib;

namespace DominusMultipliers
{
    /// <summary>
    /// Generic Harmony patches shared by every hooked method. A per-category re-entrancy counter makes sure a value is
    /// scaled only once even when hooked methods call each other (e.g. Ludus.AddGold -> Wallet.AddMoney, or an
    /// override calling base.TakeDamage).
    /// </summary>
    public static class Hooks
    {
        [ThreadStatic] private static int[] _depth;
        private static int[] Depth => _depth ?? (_depth = new int[3]);

        public static void Prefix(MethodBase __originalMethod, object __instance, object[] __args)
        {
            if (!Patcher.Bindings.TryGetValue(__originalMethod, out var b)) return;
            if (++Depth[(int)b.Category] != 1 || b.Kind == TargetKind.Return) return;
            if (!Mod.Cfg.Enabled.Value) return;
            try { ScaleArgs(b, __instance, __args); }
            catch (Exception e) { ReportError(b, e); }
        }

        public static Exception Finalizer(MethodBase __originalMethod, Exception __exception)
        {
            if (Patcher.Bindings.TryGetValue(__originalMethod, out var b)) Depth[(int)b.Category]--;
            return __exception;
        }

        // ---- return-value postfixes (Harmony needs the exact __result type) ----
        public static void PostInt(MethodBase __originalMethod, object __instance, object[] __args, ref int __result) => Post(__originalMethod, __instance, __args, ref __result);
        public static void PostLong(MethodBase __originalMethod, object __instance, object[] __args, ref long __result) => Post(__originalMethod, __instance, __args, ref __result);
        public static void PostShort(MethodBase __originalMethod, object __instance, object[] __args, ref short __result) => Post(__originalMethod, __instance, __args, ref __result);
        public static void PostUInt(MethodBase __originalMethod, object __instance, object[] __args, ref uint __result) => Post(__originalMethod, __instance, __args, ref __result);
        public static void PostULong(MethodBase __originalMethod, object __instance, object[] __args, ref ulong __result) => Post(__originalMethod, __instance, __args, ref __result);
        public static void PostUShort(MethodBase __originalMethod, object __instance, object[] __args, ref ushort __result) => Post(__originalMethod, __instance, __args, ref __result);
        public static void PostFloat(MethodBase __originalMethod, object __instance, object[] __args, ref float __result) => Post(__originalMethod, __instance, __args, ref __result);
        public static void PostDouble(MethodBase __originalMethod, object __instance, object[] __args, ref double __result) => Post(__originalMethod, __instance, __args, ref __result);
        public static void PostDecimal(MethodBase __originalMethod, object __instance, object[] __args, ref decimal __result) => Post(__originalMethod, __instance, __args, ref __result);

        internal static MethodInfo PostfixFor(Type t)
        {
            string name =
                t == typeof(int) ? nameof(PostInt) : t == typeof(long) ? nameof(PostLong) : t == typeof(short) ? nameof(PostShort) :
                t == typeof(uint) ? nameof(PostUInt) : t == typeof(ulong) ? nameof(PostULong) : t == typeof(ushort) ? nameof(PostUShort) :
                t == typeof(float) ? nameof(PostFloat) : t == typeof(double) ? nameof(PostDouble) : t == typeof(decimal) ? nameof(PostDecimal) :
                throw new NotSupportedException(t.FullName);
            return AccessTools.Method(typeof(Hooks), name);
        }

        private static void Post<T>(MethodBase original, object instance, object[] args, ref T result)
        {
            if (!Patcher.Bindings.TryGetValue(original, out var b) || Depth[(int)b.Category] != 1 || !Mod.Cfg.Enabled.Value) return;
            try
            {
                var m = Multiplier(b, instance, args);
                if (m == null) return;
                var scaled = Numeric.Scale(result, m.Value, OnlyPositive(b.Category));
                if (scaled == null) return;
                Trace(b, result, scaled, m.Value);
                result = (T)scaled;
            }
            catch (Exception e) { ReportError(b, e); }
        }

        // ---- argument scaling ----
        private static void ScaleArgs(Binding b, object instance, object[] args)
        {
            var m = Multiplier(b, instance, args);
            if (m == null) return;
            bool onlyPositive = OnlyPositive(b.Category);

            if (b.Kind == TargetKind.Delta)
            {
                var current = b.Getter.Invoke(b.Getter.IsStatic ? null : instance, null);
                double cur = Convert.ToDouble(current), next = Convert.ToDouble(args[0]), delta = next - cur;
                if (onlyPositive && !(delta > 0)) return;
                var result = Numeric.FromDouble(args[0].GetType(), cur + delta * m.Value);
                Trace(b, args[0], result, m.Value);
                args[0] = result;
                return;
            }

            foreach (var i in b.ParamIndices)
            {
                if (b.MemberPath == null)
                {
                    var scaled = Numeric.Scale(args[i], m.Value, onlyPositive);
                    if (scaled == null) continue;
                    Trace(b, args[i], scaled, m.Value);
                    args[i] = scaled;
                }
                else if (args[i] != null)
                {
                    // Works for classes and for boxed structs (the box is modified in place, then written back).
                    args[i] = ScalePath(b, args[i], 0, m.Value, onlyPositive);
                }
            }
        }

        private static object ScalePath(Binding b, object container, int depth, double m, bool onlyPositive)
        {
            var member = MemberAccess.Find(container.GetType(), b.MemberPath[depth]);
            if (member == null || !(member is FieldInfo || member is PropertyInfo))
                throw new MissingMemberException(container.GetType().FullName, b.MemberPath[depth]);
            var value = MemberAccess.Get(member, container);
            if (value == null) return container;

            object newValue;
            if (depth == b.MemberPath.Length - 1)
            {
                newValue = Numeric.Scale(value, m, onlyPositive);
                if (newValue == null) return container;
                Trace(b, value, newValue, m);
            }
            else
            {
                newValue = ScalePath(b, value, depth + 1, m, onlyPositive);
                if (!value.GetType().IsValueType) return container; // reference type was modified in place
            }
            if (MemberAccess.CanSet(member)) MemberAccess.Set(member, container, newValue);
            return container;
        }

        // ---- multipliers ----
        private static bool OnlyPositive(Category c) =>
            c == Category.Money ? Mod.Cfg.MoneyOnlyGains.Value :
            c == Category.Training ? Mod.Cfg.TrainingOnlyGains.Value : true; // negative "damage" is usually healing

        private static double? Multiplier(Binding b, object instance, object[] args)
        {
            var cfg = Mod.Cfg;
            double m;
            switch (b.Category)
            {
                case Category.Money: m = cfg.MoneyMultiplier.Value; break;
                case Category.Training: m = cfg.TrainingMultiplier.Value; break;
                default:
                {
                    var receiver = b.SideParam >= 0 ? args[b.SideParam] : instance;
                    bool? player = SideResolver.IsPlayer(receiver);
                    if (player == null)
                    {
                        switch (cfg.UnknownSide.Value)
                        {
                            case UnknownSideMode.TreatAsPlayer: player = true; break;
                            case UnknownSideMode.TreatAsEnemy: player = false; break;
                            default: return null;
                        }
                    }
                    m = player.Value ? 1.0 / Math.Max(0.01, cfg.DefenseMultiplier.Value) : cfg.DamageMultiplier.Value;
                    break;
                }
            }
            return Math.Abs(m - 1.0) < 1e-6 ? (double?)null : m;
        }

        // ---- diagnostics ----
        private static void Trace(Binding b, object before, object after, double m)
        {
            if (!Mod.Cfg.LogScaledCalls.Value || b.LoggedCalls >= 5) return;
            b.LoggedCalls++;
            Mod.Log.LogInfo($"[{b.Category}] {b.Method.DeclaringType?.Name}.{b.Method.Name}: {before} -> {after} (x{m:0.###})");
        }

        private static void ReportError(Binding b, Exception e)
        {
            if (b.LoggedErrors++ >= 3) return;
            Mod.Log.LogError($"Error scaling {Binding.Signature(b.Method)}: {e}");
        }
    }
}
