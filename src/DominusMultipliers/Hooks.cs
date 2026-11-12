using System;
using System.Reflection;
using HarmonyLib;

namespace DominusMultipliers
{
    /// <summary>
    /// Generic Harmony patches shared by every hooked method. A re-entrancy counter per category group makes sure a value
    /// is scaled only once even when hooked methods call each other (e.g. Ludus.AddGold -> Wallet.AddMoney,
    /// SpendGold -> AddGold(-x), or an override calling base.TakeDamage).
    /// </summary>
    public static class Hooks
    {
        [ThreadStatic] private static int[] _depth;
        private static int[] Depth => _depth ?? (_depth = new int[Mod.Cfg.GroupCount]);

        public static void Prefix(MethodBase __originalMethod, object __instance, object[] __args)
        {
            if (!Patcher.Bindings.TryGetValue(__originalMethod, out var b)) return;
            if (++Depth[b.Def.Group] != 1 || b.Kind == TargetKind.Return) return;
            if (!Mod.Cfg.Enabled.Value) return;
            try { ScaleArgs(b, __instance, __args); }
            catch (Exception e) { ReportError(b, e); }
        }

        public static Exception Finalizer(MethodBase __originalMethod, Exception __exception)
        {
            if (Patcher.Bindings.TryGetValue(__originalMethod, out var b)) Depth[b.Def.Group]--;
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
            if (!Patcher.Bindings.TryGetValue(original, out var b) || Depth[b.Def.Group] != 1 || !Mod.Cfg.Enabled.Value) return;
            try
            {
                if (!Multipliers(b, instance, args, out var pos, out var neg)) return;
                var scaled = Numeric.Scale(result, pos, neg);
                if (scaled == null) return;
                Trace(b, result, scaled);
                result = (T)scaled;
            }
            catch (Exception e) { ReportError(b, e); }
        }

        // ---- argument scaling ----
        private static void ScaleArgs(Binding b, object instance, object[] args)
        {
            if (!Multipliers(b, instance, args, out var pos, out var neg)) return;

            if (b.Kind == TargetKind.Delta)
            {
                var current = b.Getter.Invoke(b.Getter.IsStatic ? null : instance, null);
                double cur = Convert.ToDouble(current), delta = Convert.ToDouble(args[0]) - cur;
                var m = delta > 0 ? pos : delta < 0 ? neg : null;
                if (m == null) return;
                var result = Numeric.FromDouble(args[0].GetType(), cur + delta * m.Value);
                Trace(b, args[0], result);
                args[0] = result;
                return;
            }

            foreach (var i in b.ParamIndices)
            {
                if (b.MemberPath == null)
                {
                    var scaled = Numeric.Scale(args[i], pos, neg);
                    if (scaled == null) continue;
                    Trace(b, args[i], scaled);
                    args[i] = scaled;
                }
                else if (args[i] != null)
                {
                    // Works for classes and for boxed structs (the box is modified in place, then written back).
                    args[i] = ScalePath(b, args[i], 0, pos, neg);
                }
            }
        }

        private static object ScalePath(Binding b, object container, int depth, double? pos, double? neg)
        {
            var member = MemberAccess.Find(container.GetType(), b.MemberPath[depth]);
            if (member == null || !(member is FieldInfo || member is PropertyInfo))
                throw new MissingMemberException(container.GetType().FullName, b.MemberPath[depth]);
            var value = MemberAccess.Get(member, container);
            if (value == null) return container;

            object newValue;
            if (depth == b.MemberPath.Length - 1)
            {
                newValue = Numeric.Scale(value, pos, neg);
                if (newValue == null) return container;
                Trace(b, value, newValue);
            }
            else
            {
                newValue = ScalePath(b, value, depth + 1, pos, neg);
                if (!value.GetType().IsValueType) return container; // reference type was modified in place
            }
            if (MemberAccess.CanSet(member)) MemberAccess.Set(member, container, newValue);
            return container;
        }

        // ---- multipliers ----
        /// <summary>
        /// Works out the multiplier for positive values (<paramref name="pos"/>) and negative values (<paramref name="neg"/>);
        /// null means "leave alone". Returns false if nothing should be scaled at all.
        /// </summary>
        private static bool Multipliers(Binding b, object instance, object[] args, out double? pos, out double? neg)
        {
            var cfg = Mod.Cfg;
            var def = b.Def;
            pos = neg = null;

            object subject = b.SideParam >= 0 ? args[b.SideParam] : b.Method.IsStatic ? null : instance;
            bool? player = SideResolver.IsPlayer(subject, warnIfUnknown: def.Side != SideMode.PlayerSupport);
            if (player == null)
            {
                var mode = def.Side == SideMode.PlayerSupport ? cfg.UnknownSideOther.Value : cfg.UnknownSide.Value;
                if (mode == UnknownSideMode.Skip) return false;
                player = mode == UnknownSideMode.TreatAsPlayer;
            }

            double m = def.Multiplier.Value;
            switch (def.Side)
            {
                case SideMode.Combat:
                    // Damage received: enemies take more, you take less. Negative "damage" (healing) is left alone.
                    pos = player.Value ? 1.0 / Math.Max(0.01, cfg.DefenseMultiplier.Value) : m;
                    break;
                default:
                    if (!player.Value) return false; // never boost a rival's values
                    pos = m;
                    if (def.Id == Category.Money) neg = cfg[Category.Cost].Multiplier.Value; // AddGold(-price) is a cost
                    else if (def.OnlyGains != null && !def.OnlyGains.Value) neg = m;
                    break;
            }

            if (pos != null && Math.Abs(pos.Value - 1.0) < 1e-6) pos = null;
            if (neg != null && Math.Abs(neg.Value - 1.0) < 1e-6) neg = null;
            return pos != null || neg != null;
        }

        // ---- diagnostics ----
        private static void Trace(Binding b, object before, object after)
        {
            if (!Mod.Cfg.LogScaledCalls.Value || b.LoggedCalls >= 5) return;
            b.LoggedCalls++;
            Mod.Log.LogInfo($"[{b.Category}] {b.Method.DeclaringType?.Name}.{b.Method.Name}: {before} -> {after}");
        }

        private static void ReportError(Binding b, Exception e)
        {
            if (b.LoggedErrors++ >= 3) return;
            Mod.Log.LogError($"Error scaling {Binding.Signature(b.Method)}: {e}");
        }
    }
}
