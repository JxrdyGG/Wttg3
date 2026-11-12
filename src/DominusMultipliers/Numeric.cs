using System;

namespace DominusMultipliers
{
    internal static class Numeric
    {
        public static bool IsNumeric(Type t) =>
            t == typeof(int) || t == typeof(long) || t == typeof(short) || t == typeof(uint) || t == typeof(ulong) ||
            t == typeof(ushort) || t == typeof(float) || t == typeof(double) || t == typeof(decimal);

        /// <summary>Returns value * multiplier converted back to the value's own type, or null if left unchanged.</summary>
        public static object Scale(object value, double multiplier, bool onlyPositive)
        {
            if (value == null || !IsNumeric(value.GetType())) return null;
            if (value is decimal dec)
            {
                if (onlyPositive && dec <= 0m) return null;
                return dec * (decimal)multiplier;
            }
            double d = Convert.ToDouble(value);
            if (onlyPositive && !(d > 0)) return null;
            return FromDouble(value.GetType(), d * multiplier);
        }

        public static object FromDouble(Type t, double d)
        {
            if (t == typeof(float)) return (float)d;
            if (t == typeof(double)) return d;
            if (t == typeof(decimal)) return (decimal)d;
            d = Math.Round(d, MidpointRounding.AwayFromZero);
            if (t == typeof(int)) return (int)Clamp(d, int.MinValue, int.MaxValue);
            if (t == typeof(long)) return (long)Clamp(d, long.MinValue, long.MaxValue);
            if (t == typeof(short)) return (short)Clamp(d, short.MinValue, short.MaxValue);
            if (t == typeof(uint)) return (uint)Clamp(d, uint.MinValue, uint.MaxValue);
            if (t == typeof(ulong)) return (ulong)Clamp(d, ulong.MinValue, ulong.MaxValue);
            if (t == typeof(ushort)) return (ushort)Clamp(d, ushort.MinValue, ushort.MaxValue);
            throw new NotSupportedException(t.FullName);
        }

        private static double Clamp(double d, double min, double max) => double.IsNaN(d) ? 0 : d < min ? min : d > max ? max : d;
    }
}
