using System.Collections.Generic;
using UnityEngine;

namespace Voidward.Core
{
    /// <summary>
    /// Welded icosphere generator (positions on unit sphere, indices). Used as the base for
    /// procedural planets (subdiv 4 = 2562 verts) and any smooth sphere. Normals of a unit
    /// sphere equal the normalized positions; planet code recolors and displaces them.
    /// </summary>
    public static class IcoSphere
    {
        static List<Vector3> baseVerts;
        static int[] baseTris;

        public static List<Vector3> GetBaseVerts()
        {
            if (baseVerts == null) BuildBase();
            return baseVerts;
        }

        public static int[] GetBaseTris()
        {
            if (baseVerts == null) BuildBase();
            return baseTris;
        }

        static void BuildBase()
        {
            float t = (1f + Mathf.Sqrt(5f)) / 2f;
            baseVerts = new List<Vector3>
            {
                new Vector3(-1,  t,  0), new Vector3( 1,  t,  0), new Vector3(-1, -t,  0), new Vector3( 1, -t,  0),
                new Vector3( 0, -1,  t), new Vector3( 0,  1,  t), new Vector3( 0, -1, -t), new Vector3( 0,  1, -t),
                new Vector3( t,  0, -1), new Vector3( t,  0,  1), new Vector3(-t,  0, -1), new Vector3(-t,  0,  1),
            };
            for (int i = 0; i < baseVerts.Count; i++) baseVerts[i] = baseVerts[i].normalized;
            baseTris = new int[]
            {
                0, 11, 5,  0, 5, 1,   0, 1, 7,   0, 7, 10,  0, 10, 11,
                1, 5, 9,   5, 11, 4,  11, 10, 2, 10, 7, 6,  7, 1, 8,
                3, 9, 4,   3, 4, 2,   3, 2, 6,   3, 6, 8,   3, 8, 9,
                4, 9, 5,   2, 4, 11,  6, 2, 10,  8, 6, 7,   9, 8, 1,
            };
        }

        /// <summary>Creates a welded sphere mesh. subdiv 3 = 642 verts, 4 = 2562, 5 = 10242.</summary>
        public static Mesh Create(float radius, int subdivisions, string name = "IcoSphere")
        {
            var verts = new List<Vector3>(GetBaseVerts());
            var tris = new List<int>(GetBaseTris());
            var midCache = new Dictionary<long, int>();

            for (int s = 0; s < subdivisions; s++)
            {
                var newTris = new List<int>(tris.Count * 4);
                for (int i = 0; i < tris.Count; i += 3)
                {
                    int a = tris[i], b = tris[i + 1], c = tris[i + 2];
                    int ab = Mid(verts, a, b, midCache);
                    int bc = Mid(verts, b, c, midCache);
                    int ca = Mid(verts, c, a, midCache);
                    newTris.AddRange(new[] { a, ab, ca, b, bc, ab, c, ca, bc, ab, bc, ca });
                }
                tris = newTris;
            }

            var mesh = new Mesh { name = name };
            if (verts.Count > 65000)
                mesh.indexFormat = UnityEngine.Rendering.IndexFormat.UInt32;
            var scaled = new Vector3[verts.Count];
            var normals = new Vector3[verts.Count];
            for (int i = 0; i < verts.Count; i++)
            {
                normals[i] = verts[i]; // unit sphere
                scaled[i] = verts[i] * radius;
            }
            mesh.SetVertices(new List<Vector3>(scaled));
            mesh.SetNormals(new List<Vector3>(normals));
            mesh.SetTriangles(tris, 0);
            mesh.RecalculateBounds();
            return mesh;
        }

        static int Mid(List<Vector3> v, int a, int b, Dictionary<long, int> cache)
        {
            long key = a < b ? ((long)a << 32) + b : ((long)b << 32) + a;
            if (cache.TryGetValue(key, out int idx)) return idx;
            idx = v.Count;
            v.Add(((v[a] + v[b]) * 0.5f).normalized);
            cache[key] = idx;
            return idx;
        }
    }
}
