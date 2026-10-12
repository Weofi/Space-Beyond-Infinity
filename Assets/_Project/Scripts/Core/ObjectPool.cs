using System;
using System.Collections.Generic;
using UnityEngine;

namespace Voidward.Core
{
    /// <summary>
    /// Minimal GameObject pool for non-networked visuals (explosions, tracer lines, audio sources).
    /// Networked entities use NGO spawning instead.
    /// </summary>
    public class ObjectPool
    {
        readonly Func<GameObject> factory;
        readonly Transform parent;
        readonly Queue<GameObject> idle = new Queue<GameObject>();

        public ObjectPool(Func<GameObject> factory, Transform parent = null, int prewarm = 0)
        {
            this.factory = factory;
            this.parent = parent;
            for (int i = 0; i < prewarm; i++)
            {
                var go = factory();
                go.SetActive(false);
                if (parent) go.transform.SetParent(parent, false);
                idle.Enqueue(go);
            }
        }

        public GameObject Get(Vector3 position, Quaternion rotation)
        {
            GameObject go = idle.Count > 0 ? idle.Dequeue() : factory();
            go.transform.SetPositionAndRotation(position, rotation);
            go.SetActive(true);
            return go;
        }

        public void Return(GameObject go)
        {
            if (go == null) return;
            go.SetActive(false);
            if (parent) go.transform.SetParent(parent, false);
            idle.Enqueue(go);
        }
    }
}
