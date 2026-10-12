using System.Collections.Generic;
using UnityEngine;

namespace Voidward.Core
{
    /// <summary>
    /// Builds low-poly, flat-shaded, vertex-colored meshes from primitives (boxes, prisms,
    /// wedges, rocks). Every face gets its own vertices so RecalculateNormals yields crisp
    /// facets — the Astroneer-style chunky look with a single shared vertex-color material.
    /// All Add* calls take a Matrix4x4 local transform (TRS).
    /// </summary>
    public class MeshBuilder
    {
        readonly List<Vector3> verts = new List<Vector3>();
        readonly List<int> tris = new List<int>();
        readonly List<Color> colors = new List<Color>();

        public int VertexCount => verts.Count;

        public static Matrix4x4 TRS(Vector3 pos, Quaternion rot, Vector3 scale) =>
            Matrix4x4.TRS(pos, rot, scale);

        public static Matrix4x4 TRS(Vector3 pos) => Matrix4x4.TRS(pos, Quaternion.identity, Vector3.one);

        void AddVert(Vector3 v, Color c) { verts.Add(v); colors.Add(c); }

        void AddTri(Vector3 a, Vector3 b, Vector3 c, Color col)
        {
            int i = verts.Count;
            AddVert(a, col); AddVert(b, col); AddVert(c, col);
            tris.Add(i); tris.Add(i + 1); tris.Add(i + 2);
        }

        public void AddQuad(Vector3 a, Vector3 b, Vector3 c, Vector3 d, Color col)
        {
            AddTri(a, b, c, col);
            AddTri(a, c, d, col);
        }

        /// <summary>Axis-aligned box of given size, transformed by m.</summary>
        public void AddBox(Matrix4x4 m, Vector3 size, Color col)
        {
            Vector3 h = size * 0.5f;
            Vector3[] c =
            {
                new Vector3(-h.x,-h.y,-h.z), new Vector3( h.x,-h.y,-h.z),
                new Vector3( h.x, h.y,-h.z), new Vector3(-h.x, h.y,-h.z),
                new Vector3(-h.x,-h.y, h.z), new Vector3( h.x,-h.y, h.z),
                new Vector3( h.x, h.y, h.z), new Vector3(-h.x, h.y, h.z),
            };
            for (int i = 0; i < 8; i++) c[i] = m.MultiplyPoint3x4(c[i]);
            AddQuad(c[0], c[1], c[2], c[3], col); // back
            AddQuad(c[5], c[4], c[7], c[6], col); // front
            AddQuad(c[4], c[0], c[3], c[7], col); // left
            AddQuad(c[1], c[5], c[6], c[2], col); // right
            AddQuad(c[3], c[2], c[6], c[7], col); // top
            AddQuad(c[4], c[5], c[1], c[0], col); // bottom
        }

        /// <summary>Wedge (triangular prism): a box cut diagonally. +Z stays full height, -Z slopes to the floor.</summary>
        public void AddWedge(Matrix4x4 m, Vector3 size, Color col)
        {
            Vector3 h = size * 0.5f;
            Vector3 b0 = m.MultiplyPoint3x4(new Vector3(-h.x, -h.y, -h.z));
            Vector3 b1 = m.MultiplyPoint3x4(new Vector3(h.x, -h.y, -h.z));
            Vector3 b2 = m.MultiplyPoint3x4(new Vector3(h.x, -h.y, h.z));
            Vector3 b3 = m.MultiplyPoint3x4(new Vector3(-h.x, -h.y, h.z));
            Vector3 t2 = m.MultiplyPoint3x4(new Vector3(h.x, h.y, h.z));
            Vector3 t3 = m.MultiplyPoint3x4(new Vector3(-h.x, h.y, h.z));
            AddQuad(b3, b2, t2, t3, col);       // tall front face
            AddQuad(b0, b1, b2, b3, col);       // bottom
            AddTri(b0, b3, t3, col);            // left triangle
            AddTri(b2, b1, t2, col);            // right triangle
            AddQuad(b1, b0, t3, t2, col);       // sloped top
        }

        /// <summary>Cylinder / prism / cone between two radii along +Y.</summary>
        public void AddPrism(Matrix4x4 m, float rBottom, float rTop, float height, int sides, Color col, bool capBottom = true, bool capTop = true)
        {
            sides = Mathf.Max(3, sides);
            float hy = height * 0.5f;
            Vector3[] ringB = new Vector3[sides];
            Vector3[] ringT = new Vector3[sides];
            for (int i = 0; i < sides; i++)
            {
                float a = (i / (float)sides) * Mathf.PI * 2f;
                float ca = Mathf.Cos(a), sa = Mathf.Sin(a);
                ringB[i] = m.MultiplyPoint3x4(new Vector3(ca * rBottom, -hy, sa * rBottom));
                ringT[i] = m.MultiplyPoint3x4(new Vector3(ca * rTop, hy, sa * rTop));
            }
            for (int i = 0; i < sides; i++)
            {
                int n = (i + 1) % sides;
                AddQuad(ringB[i], ringB[n], ringT[n], ringT[i], col);
                if (capBottom) AddTri(m.MultiplyPoint3x4(new Vector3(0, -hy, 0)), ringB[n], ringB[i], col);
                if (capTop) AddTri(m.MultiplyPoint3x4(new Vector3(0, hy, 0)), ringT[i], ringT[n], col);
            }
        }

        /// <summary>Low-poly rock: jittered icosahedron (1 subdivision), deterministic per seed.</summary>
        public void AddRock(Matrix4x4 m, float radius, int seed, Color col)
        {
            var src = IcoSphere.GetBaseVerts();
            var srcTris = IcoSphere.GetBaseTris();
            // subdivide once
            var mid = new Dictionary<long, int>();
            var v = new List<Vector3>(src);
            var t = new List<int>();
            for (int i = 0; i < srcTris.Length; i += 3)
            {
                int a = srcTris[i], b = srcTris[i + 1], c = srcTris[i + 2];
                int ab = Midpoint(v, a, b, mid), bc = Midpoint(v, b, c, mid), ca = Midpoint(v, c, a, mid);
                t.AddRange(new[] { a, ab, ca, b, bc, ab, c, ca, bc, ab, bc, ca });
            }
            // jitter each unique vertex deterministically
            var jittered = new Vector3[v.Count];
            for (int i = 0; i < v.Count; i++)
            {
                var dir = v[i].normalized;
                float j = 1f + MathUtils.ValueNoise3D(seed, dir * 2.3f) * 0.45f;
                jittered[i] = dir * (radius * j);
            }
            for (int i = 0; i < t.Count; i += 3)
            {
                var p0 = m.MultiplyPoint3x4(jittered[t[i]]);
                var p1 = m.MultiplyPoint3x4(jittered[t[i + 1]]);
                var p2 = m.MultiplyPoint3x4(jittered[t[i + 2]]);
                AddTri(p0, p1, p2, col);
            }
        }

        static int Midpoint(List<Vector3> v, int a, int b, Dictionary<long, int> cache)
        {
            long key = a < b ? ((long)a << 32) + b : ((long)b << 32) + a;
            if (cache.TryGetValue(key, out int idx)) return idx;
            idx = v.Count;
            v.Add(((v[a] + v[b]) * 0.5f).normalized);
            cache[key] = idx;
            return idx;
        }

        public Mesh Build(string name)
        {
            var mesh = new Mesh { name = name };
            if (verts.Count > 65000)
                mesh.indexFormat = UnityEngine.Rendering.IndexFormat.UInt32;
            mesh.SetVertices(verts);
            mesh.SetTriangles(tris, 0);
            mesh.SetColors(colors);
            mesh.RecalculateNormals(); // duplicated verts => flat facets
            mesh.RecalculateBounds();
            return mesh;
        }
    }
}
