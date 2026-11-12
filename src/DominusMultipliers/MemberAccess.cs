using System;
using System.Collections.Generic;
using System.Reflection;

namespace DominusMultipliers
{
    /// <summary>Case-insensitive, cached lookup of instance fields / properties / parameterless methods.</summary>
    internal static class MemberAccess
    {
        private const BindingFlags Inst = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly;
        private static readonly Dictionary<string, MemberInfo> Cache = new Dictionary<string, MemberInfo>();
        private static readonly object Lock = new object();

        public static MemberInfo Find(Type type, string name)
        {
            var key = type.AssemblyQualifiedName + "|" + name.ToLowerInvariant();
            lock (Lock)
            {
                if (Cache.TryGetValue(key, out var cached)) return cached;
                var found = Search(type, name);
                Cache[key] = found;
                return found;
            }
        }

        private static MemberInfo Search(Type type, string name)
        {
            for (var t = type; t != null && t != typeof(object); t = t.BaseType)
            {
                foreach (var f in t.GetFields(Inst))
                    if (string.Equals(f.Name, name, StringComparison.OrdinalIgnoreCase)) return f;
                foreach (var p in t.GetProperties(Inst))
                    if (p.GetIndexParameters().Length == 0 && p.CanRead && string.Equals(p.Name, name, StringComparison.OrdinalIgnoreCase)) return p;
                foreach (var m in t.GetMethods(Inst))
                    if (m.GetParameters().Length == 0 && m.ReturnType != typeof(void) && !m.IsGenericMethodDefinition &&
                        string.Equals(m.Name, name, StringComparison.OrdinalIgnoreCase)) return m;
            }
            return null;
        }

        private static readonly string[] ComponentGetters = { "GetComponent", "GetComponentInParent", "GetComponentInChildren" };

        /// <summary>Unity GetComponent by type name on a Component/GameObject; null if not found or not a Unity object.</summary>
        public static object GetComponent(object obj, string typeName)
        {
            var type = GameAssemblies.FindType(typeName);
            if (type == null) return null;
            foreach (var name in ComponentGetters)
            {
                var getter = obj.GetType().GetMethod(name, BindingFlags.Instance | BindingFlags.Public, null, new[] { typeof(Type) }, null);
                if (getter == null) continue;
                var c = getter.Invoke(obj, new object[] { type });
                if (c != null && !c.Equals(null)) return c; // Unity's "destroyed" objects compare equal to null
            }
            return null;
        }

        public static Type ValueType(MemberInfo m) =>
            m is FieldInfo f ? f.FieldType : m is PropertyInfo p ? p.PropertyType : ((MethodInfo)m).ReturnType;

        public static object Get(MemberInfo m, object obj)
        {
            switch (m)
            {
                case FieldInfo f: return f.GetValue(obj);
                case PropertyInfo p: return p.GetValue(obj, null);
                default: return ((MethodInfo)m).Invoke(obj, null);
            }
        }

        public static bool CanSet(MemberInfo m) => m is FieldInfo f ? !f.IsInitOnly && !f.IsLiteral : m is PropertyInfo p && p.CanWrite;

        public static void Set(MemberInfo m, object obj, object value)
        {
            if (m is FieldInfo f) f.SetValue(obj, value);
            else ((PropertyInfo)m).SetValue(obj, value, null);
        }

        /// <summary>Walks a dotted path. Returns false if a member is missing (true with null value if a link is null).</summary>
        public static bool TryGetPath(object obj, string[] path, out object value)
        {
            value = obj;
            foreach (var seg in path)
            {
                if (value == null) return true;
                if (seg.Length > 1 && seg[0] == '@')
                {
                    // "@Team" = the Team component on the same GameObject (or a parent/child), for Unity components.
                    value = GetComponent(value, seg.Substring(1));
                    if (value == null) return false;
                    continue;
                }
                var m = Find(value.GetType(), seg);
                if (m == null) { value = null; return false; }
                value = Get(m, value);
            }
            return true;
        }
    }
}
