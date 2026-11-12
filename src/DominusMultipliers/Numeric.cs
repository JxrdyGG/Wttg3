using System;

namespace DominusMultipliers
{
    internal static class Numeric
    {
        public static bool IsNumeric(Type t) =>
            t == typeof(int) || t == typeof(long) || t == typeof(short) || t == typeof(uint) || t == typeof(ulong) ||
            t == typeof(ushort) || t == typeof(float) || t == typeof(double) || t == typeof(decimal);

        /// <summary>
        /// Scales a positive value by <paramref name="pos"/> and a negative one by <paramref name="neg"/> (null = leave as is),
        /// converted back to the value's own type. Returns null if nothing changed.
        /// </summary>
        public static object Scale(object value, double? pos, double? neg)
        {
            if (value == null || !IsNumeric(value.GetType())) return null;
            if (value is decimal dec)
            {
                var dm = dec > 0m ? pos : dec < 0m ? neg : null;
                return dm == null ? null : (object)(dec * (decimal)dm.Value);
            }
            double d = Convert.ToDouble(value);
            var m = d > 0 ? pos : d < 0 ? neg : null;
            return m == null ? null : FromDouble(value.GetType(), d * m.Value);
        }

        public static object FromDouble(Type t, double d)
        {
            if (t == typeof(float)) return (float)d;
            if (t == typeof(double)) return d;
            if (t == typeof(decimal)) return (decimal)d;
            // Random rounding keeps the average right for small whole numbers: 1 injury level x0.5 becomes 0 or 1 with
            // 50% chance each (always rounding would give 1 every time, i.e. no reduction at all).
            double floor = Math.Floor(d);
            d = floor + (Rng.NextDouble() < d - floor ? 1 : 0);
            if (t == typeof(int)) return (int)Clamp(d, int.MinValue, int.MaxValue);
            if (t == typeof(long)) return (long)Clamp(d, long.MinValue, long.MaxValue);
            if (t == typeof(short)) return (short)Clamp(d, short.MinValue, short.MaxValue);
            if (t == typeof(uint)) return (uint)Clamp(d, uint.MinValue, uint.MaxValue);
            if (t == typeof(ulong)) return (ulong)Clamp(d, ulong.MinValue, ulong.MaxValue);
            if (t == typeof(ushort)) return (ushort)Clamp(d, ushort.MinValue, ushort.MaxValue);
            throw new NotSupportedException(t.FullName);
        }

        [ThreadStatic] private static Random _rng;
        private static Random Rng => _rng ?? (_rng = new Random(Guid.NewGuid().GetHashCode()));

        private static double Clamp(double d, double min, double max) => double.IsNaN(d) ? 0 : d < min ? min : d > max ? max : d;
    }
}
