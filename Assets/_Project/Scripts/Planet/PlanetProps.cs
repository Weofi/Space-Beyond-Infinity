using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
using Voidward.Data;

namespace Voidward.Planet
{
    /// <summary>
    /// Asset props for planets (#100): the catalog's <see cref="PlanetPropModel"/>s copied once into plain arrays so
    /// worker threads can bake transformed instances into per-chunk meshes (<see cref="PropBatch"/>). Families group
    /// the variants by id prefix ("Rock" = Rock01..07, "Pod" = the small alien plants). Empty without assets: callers
    /// then keep the procedural props.
    /// </summary>
    public sealed class PlanetPropLibrary
    {
        public sealed class Model
        {
            public string Id;
            public Vector3[] V, N;
            public Vector4[] T;
            public Vector2[] Uv;
            public int[][] Sub;
            public Material[] Materials;
            public Mesh Mesh;
            /// <summary>Largest half extent (rocks: 1) and height above the origin (others: 1).</summary>
            public float Radius, Height;
        }

        public readonly List<Model> Models = new List<Model>();
        readonly Dictionary<string, int[]> families = new Dictionary<string, int[]>();

        static PlanetPropLibrary shared;
        static ContentCatalog sharedFor;

        /// <summary>The library of the current catalog (built on first use, main thread).</summary>
        public static PlanetPropLibrary Get(ContentCatalog catalog)
        {
            if (shared != null && sharedFor == catalog) return shared;
            shared = new PlanetPropLibrary(catalog != null ? catalog.PlanetProps : null);
            sharedFor = catalog;
            return shared;
        }

        PlanetPropLibrary(PlanetPropModel[] props)
        {
            if (props == null) return;
            var byFamily = new Dictionary<string, List<int>>();
            foreach (var p in props)
            {
                if (p == null || p.Mesh == null || !p.Mesh.isReadable) continue;
                var m = p.Mesh;
                var model = new Model
                {
                    Id = p.Id, Mesh = m, Materials = p.Materials,
                    V = m.vertices, N = m.normals, T = m.tangents, Uv = m.uv,
                    Sub = new int[m.subMeshCount][],
                };
                for (int s = 0; s < m.subMeshCount; s++) model.Sub[s] = m.GetTriangles(s);
                if (model.N.Length != model.V.Length || model.Uv.Length != model.V.Length) continue;
                if (model.T.Length != model.V.Length) model.T = new Vector4[model.V.Length];
                var b = m.bounds;
                model.Radius = Mathf.Max(b.extents.x, Mathf.Max(b.extents.y, b.extents.z));
                model.Height = b.max.y;
                int index = Models.Count;
                Models.Add(model);
                string family = Family(p.Id);
                if (!byFamily.TryGetValue(family, out var list)) byFamily[family] = list = new List<int>();
                list.Add(index);
                // the small alien plants also form the "Pod" family (ground cover between the trees)
                if (family == "MinePlant" || family == "FistPlant" || family == "AlienPlant")
                {
                    if (!byFamily.TryGetValue("Pod", out var pods)) byFamily["Pod"] = pods = new List<int>();
                    pods.Add(index);
                }
            }
            foreach (var kv in byFamily) families[kv.Key] = kv.Value.ToArray();
        }

        /// <summary>"Rock03" -> "Rock", "AlienTree" -> "AlienTree".</summary>
        public static string Family(string id)
        {
            int end = id.Length;
            while (end > 0 && char.IsDigit(id[end - 1])) end--;
            return id.Substring(0, end);
        }

        public bool Has(string family) => families.ContainsKey(family);

        /// <summary>A variant of a family picked from `seed` (deterministic); -1 when the family has no asset.</summary>
        public int Pick(string family, int seed)
        {
            if (!families.TryGetValue(family, out var list) || list.Length == 0) return -1;
            return list[(int)((uint)seed % (uint)list.Length)];
        }

        public int[] FamilyModels(string family) => families.TryGetValue(family, out var list) ? list : System.Array.Empty<int>();
    }

    /// <summary>
    /// Transformed asset prop instances of one chunk (worker thread), uploaded as one mesh with a submesh per
    /// (model, material) on the main thread.
    /// </summary>
    public sealed class PropBatch
    {
        readonly PlanetPropLibrary lib;
        readonly List<Vector3> v = new List<Vector3>(), n = new List<Vector3>();
        readonly List<Vector4> t = new List<Vector4>();
        readonly List<Vector2> uv = new List<Vector2>();
        // key = model * 16 + submesh
        readonly SortedDictionary<int, List<int>> tris = new SortedDictionary<int, List<int>>();

        public PropBatch(PlanetPropLibrary lib) { this.lib = lib; }

        public int VertexCount => v.Count;

        /// <summary>Adds model `index` with the given placement (uniform scale).</summary>
        public void Add(int index, Vector3 pos, Quaternion rot, float scale)
        {
            var m = lib.Models[index];
            int baseV = v.Count;
            for (int i = 0; i < m.V.Length; i++)
            {
                v.Add(pos + rot * (m.V[i] * scale));
                n.Add(rot * m.N[i]);
                var tg = m.T[i];
                Vector3 td = rot * new Vector3(tg.x, tg.y, tg.z);
                t.Add(new Vector4(td.x, td.y, td.z, tg.w));
                uv.Add(m.Uv[i]);
            }
            for (int s = 0; s < m.Sub.Length && s < 16; s++)
            {
                int key = index * 16 + s;
                if (!tris.TryGetValue(key, out var list)) tris[key] = list = new List<int>();
                var src = m.Sub[s];
                for (int i = 0; i < src.Length; i++) list.Add(baseV + src[i]);
            }
        }

        /// <summary>Main thread: the chunk mesh and its materials (one per submesh, through `material`).</summary>
        public Mesh Build(string name, System.Func<int, int, Material> material, out Material[] materials)
        {
            var mesh = new Mesh { name = name, hideFlags = HideFlags.DontSave };
            if (v.Count > 65000) mesh.indexFormat = IndexFormat.UInt32;
            mesh.SetVertices(v);
            mesh.SetNormals(n);
            mesh.SetTangents(t);
            mesh.SetUVs(0, uv);
            mesh.subMeshCount = tris.Count;
            materials = new Material[tris.Count];
            int sub = 0;
            foreach (var kv in tris)
            {
                mesh.SetTriangles(kv.Value, sub, false);
                materials[sub] = material(kv.Key / 16, kv.Key % 16);
                sub++;
            }
            mesh.RecalculateBounds();
            return mesh;
        }
    }
}
