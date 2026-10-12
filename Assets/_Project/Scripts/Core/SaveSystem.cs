using System;
using System.IO;
using UnityEngine;

namespace Voidward.Core
{
    /// <summary>Simple JSON file persistence in Application.persistentDataPath (meta progression, settings).</summary>
    public static class SaveSystem
    {
        static string PathFor(string key) =>
            Path.Combine(Application.persistentDataPath, "vw_" + key + ".json");

        public static void Save<T>(string key, T data)
        {
            try
            {
                File.WriteAllText(PathFor(key), JsonUtility.ToJson(data, true));
            }
            catch (Exception e)
            {
                Debug.LogError("[SaveSystem] Save failed for " + key + ": " + e.Message);
            }
        }

        public static T Load<T>(string key) where T : new()
        {
            try
            {
                string p = PathFor(key);
                if (File.Exists(p))
                {
                    var val = JsonUtility.FromJson<T>(File.ReadAllText(p));
                    if (val != null) return val;
                }
            }
            catch (Exception e)
            {
                Debug.LogWarning("[SaveSystem] Load failed for " + key + ": " + e.Message);
            }
            return new T();
        }

        public static void Delete(string key)
        {
            string p = PathFor(key);
            if (File.Exists(p)) File.Delete(p);
        }
    }
}
