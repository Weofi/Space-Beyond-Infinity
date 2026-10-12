using System;
using System.Collections.Generic;

namespace Voidward.Core
{
    /// <summary>
    /// Minimal service locator. Systems register themselves on startup and unregister on shutdown.
    /// Keeps the asmdef graph acyclic: consumers look up interfaces/types at runtime instead of
    /// taking compile-time dependencies on every feature assembly.
    /// </summary>
    public static class G
    {
        static readonly Dictionary<Type, object> Services = new Dictionary<Type, object>();

        public static void Register<T>(T service) where T : class
        {
            if (service == null) return;
            Services[typeof(T)] = service;
        }

        public static void Unregister<T>() where T : class => Services.Remove(typeof(T));

        public static T Get<T>() where T : class
        {
            return Services.TryGetValue(typeof(T), out var o) ? o as T : null;
        }

        public static bool TryGet<T>(out T service) where T : class
        {
            service = Get<T>();
            return service != null;
        }

        public static void Clear() => Services.Clear();
    }
}
