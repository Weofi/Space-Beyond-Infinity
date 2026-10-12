using System.Collections.Generic;
using UnityEngine;

namespace Voidward.Core
{
    /// <summary>
    /// Builds low-poly, flat-shaded, vertex-colored meshes from primitives (boxes, prisms,
    /// wedges, rocks). Every face gets its own vertices so RecalculateNormals yields crisp
    /// facets — the Astroneer-style chunky look with a single shared vertex-color material.
    /// All Add* calls take a Matrix4x4 local transform (TRS). Primitives are wound outward-facing
    /// relative to their own centre, so any convex part renders correctly with back-face culling.
    /// Per-vertex surface data goes to UV0: x = gloss (specular), y = glow (emission), read by VW_ToonFlat.
    /// </summary>
    public class MeshBuilder
    {
        public const float DefaultGloss = 0.3f;

        readonly List<Vector3> verts = new List<Vector3>();
        readonly List<int> tris = new List<int>();
        readonly List<Color> colors = new List<Color>();
        readonly List<Vector2> surface = new List<Vector2>();

        /// <summary>Chamfer size for AddBox as a fraction of the box's smallest dimension (0 = sharp edges, max ~0.35).</summary>
        public float Bevel;
        /// <summary>Specular strength for subsequently added geometry (0 = matte rubber, 1 = mirror-like visor).</summary>
        public float Gloss = DefaultGloss;
        /// <summary>Emission for subsequently added geometry (vertex color drives the glow tint).</summary>
        public float Glow;

        public int VertexCount => verts.Count;

        public static Matrix4x4 TRS(Vector3 pos, Quaternion rot, Vector3 scale) =>
            Matrix4x4.TRS(pos, rot, scale);

        public static Matrix4x4 TRS(Vector3 pos) => Matrix4x4.TRS(pos, Quaternion.identity, Vector3.one);

        /// <summary>Sets gloss/glow for the following Add* calls. Call without args to reset to defaults.</summary>
        public MeshBuilder Surface(float gloss = DefaultGloss, float glow = 0f)
        {
            Gloss = gloss; Glow = glow;
            return this;
        }

        void AddVert(Vector3 v, Color c) { verts.Add(v); colors.Add(c); surface.Add(new Vector2(Gloss, Glow)); }

        void AddTri(Vector3 a, Vector3 b, Vector3 c, Color col)
        {
            int i = verts.Count;
            AddVert(a, col); AddVert(b, col); AddVert(c, col);
            tris.Add(i); tris.Add(i + 1); tris.Add(i + 2);
        }

        public void AddTriangle(Vector3 a, Vector3 b, Vector3 c, Color col) => AddTri(a, b, c, col);

        public void AddQuad(Vector3 a, Vector3 b, Vector3 c, Vector3 d, Color col)
        {
            AddTri(a, b, c, col);
            AddTri(a, c, d, col);
        }

        // Unity's front face normal is cross(b - a, c - a); flip whenever it points towards the part's centre.
        void TriOut(Vector3 a, Vector3 b, Vector3 c, Vector3 center, Color col)
        {
            if (Vector3.Dot(Vector3.Cross(b - a, c - a), (a + b + c) / 3f - center) < 0f) AddTri(a, c, b, col);
            else AddTri(a, b, c, col);
        }

        void QuadOut(Vector3 a, Vector3 b, Vector3 c, Vector3 d, Vector3 center, Color col)
        {
            if (Vector3.Dot(Vector3.Cross(b - a, c - a), (a + b + c + d) * 0.25f - center) < 0f) AddQuad(a, d, c, b, col);
            else AddQuad(a, b, c, d, col);
        }

        /// <summary>Box of given size, transformed by m. Edges are chamfered when <see cref="Bevel"/> &gt; 0.</summary>
        public void AddBox(Matrix4x4 m, Vector3 size, Color col)
        {
            Vector3 h = size * 0.5f;
            float b = Mathf.Clamp(Bevel, 0f, 0.35f) * Mathf.Min(size.x, Mathf.Min(size.y, size.z));
            Vector3 center = m.MultiplyPoint3x4(Vector3.zero);
            if (b > 0.004f) { AddChamferBox(m, h, b, center, col); return; }
            Vector3[] c =
            {
                new Vector3(-h.x,-h.y,-h.z), new Vector3( h.x,-h.y,-h.z),
                new Vector3( h.x, h.y,-h.z), new Vector3(-h.x, h.y,-h.z),
                new Vector3(-h.x,-h.y, h.z), new Vector3( h.x,-h.y, h.z),
                new Vector3( h.x, h.y, h.z), new Vector3(-h.x, h.y, h.z),
            };
            for (int i = 0; i < 8; i++) c[i] = m.MultiplyPoint3x4(c[i]);
            QuadOut(c[0], c[1], c[2], c[3], center, col); // back
            QuadOut(c[5], c[4], c[7], c[6], center, col); // front
            QuadOut(c[4], c[0], c[3], c[7], center, col); // left
            QuadOut(c[1], c[5], c[6], c[2], center, col); // right
            QuadOut(c[3], c[2], c[6], c[7], center, col); // top
            QuadOut(c[4], c[5], c[1], c[0], center, col); // bottom
        }

        void AddChamferBox(Matrix4x4 m, Vector3 h, float b, Vector3 center, Color col)
        {
            // P = corner vertex lying on the face perpendicular to `axis` (0=x, 1=y, 2=z).
            Vector3 P(int sx, int sy, int sz, int axis)
            {
                var p = new Vector3(sx * (h.x - b), sy * (h.y - b), sz * (h.z - b));
                if (axis == 0) p.x = sx * h.x; else if (axis == 1) p.y = sy * h.y; else p.z = sz * h.z;
                return m.MultiplyPoint3x4(p);
            }
            for (int s = -1; s <= 1; s += 2)
            {
                QuadOut(P(s, -1, -1, 0), P(s, 1, -1, 0), P(s, 1, 1, 0), P(s, -1, 1, 0), center, col);
                QuadOut(P(-1, s, -1, 1), P(1, s, -1, 1), P(1, s, 1, 1), P(-1, s, 1, 1), center, col);
                QuadOut(P(-1, -1, s, 2), P(1, -1, s, 2), P(1, 1, s, 2), P(-1, 1, s, 2), center, col);
                for (int t = -1; t <= 1; t += 2)
                {
                    QuadOut(P(s, t, -1, 0), P(s, t, 1, 0), P(s, t, 1, 1), P(s, t, -1, 1), center, col);
                    QuadOut(P(s, -1, t, 0), P(s, 1, t, 0), P(s, 1, t, 2), P(s, -1, t, 2), center, col);
                    QuadOut(P(-1, s, t, 1), P(1, s, t, 1), P(1, s, t, 2), P(-1, s, t, 2), center, col);
                    for (int u = -1; u <= 1; u += 2)
                        TriOut(P(s, t, u, 0), P(s, t, u, 1), P(s, t, u, 2), center, col);
                }
            }
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
            Vector3 center = (b0 + b1 + b2 + b3 + t2 + t3) / 6f;
            QuadOut(b3, b2, t2, t3, center, col);       // tall front face
            QuadOut(b0, b1, b2, b3, center, col);       // bottom
            TriOut(b0, b3, t3, center, col);            // left triangle
            TriOut(b2, b1, t2, center, col);            // right triangle
            QuadOut(b1, b0, t3, t2, center, col);       // sloped top
        }

        /// <summary>Cylinder / prism / cone between two radii along +Y.</summary>
        public void AddPrism(Matrix4x4 m, float rBottom, float rTop, float height, int sides, Color col, bool capBottom = true, bool capTop = true)
        {
            sides = Mathf.Max(3, sides);
            float hy = height * 0.5f;
            Vector3 center = m.MultiplyPoint3x4(Vector3.zero);
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
                if (rBottom < 1e-4f) TriOut(ringB[i], ringT[n], ringT[i], center, col);
                else if (rTop < 1e-4f) TriOut(ringB[i], ringB[n], ringT[i], center, col);
                else QuadOut(ringB[i], ringB[n], ringT[n], ringT[i], center, col);
                if (capBottom && rBottom > 1e-4f) TriOut(m.MultiplyPoint3x4(new Vector3(0, -hy, 0)), ringB[n], ringB[i], center, col);
                if (capTop && rTop > 1e-4f) TriOut(m.MultiplyPoint3x4(new Vector3(0, hy, 0)), ringT[i], ringT[n], center, col);
            }
        }

        /// <summary>Low-poly rock: jittered icosahedron (1 subdivision), deterministic per seed.</summary>
        public void AddRock(Matrix4x4 m, float radius, int seed, Color col) => AddIco(m, radius, 1, seed, 0.45f, col);

        /// <summary>Faceted low-poly sphere (icosahedron, subdiv 0..2). Scale m for ellipsoids, domes and helmets.</summary>
        public void AddSphere(Matrix4x4 m, float radius, int subdivisions, Color col) => AddIco(m, radius, subdivisions, 0, 0f, col);

        void AddIco(Matrix4x4 m, float radius, int subdivisions, int seed, float jitter, Color col)
        {
            var srcTris = IcoSphere.GetBaseTris();
            var mid = new Dictionary<long, int>();
            var v = new List<Vector3>(IcoSphere.GetBaseVerts());
            var t = new List<int>(srcTris);
            for (int s = 0; s < Mathf.Clamp(subdivisions, 0, 3); s++)
            {
                var nt = new List<int>(t.Count * 4);
                for (int i = 0; i < t.Count; i += 3)
                {
                    int a = t[i], b = t[i + 1], c = t[i + 2];
                    int ab = Midpoint(v, a, b, mid), bc = Midpoint(v, b, c, mid), ca = Midpoint(v, c, a, mid);
                    nt.AddRange(new[] { a, ab, ca, b, bc, ab, c, ca, bc, ab, bc, ca });
                }
                t = nt;
            }
            var pts = new Vector3[v.Count];
            for (int i = 0; i < v.Count; i++)
            {
                var dir = v[i].normalized;
                float j = jitter > 0f ? 1f + MathUtils.ValueNoise3D(seed, dir * 2.3f) * jitter : 1f;
                pts[i] = m.MultiplyPoint3x4(dir * (radius * j));
            }
            Vector3 center = m.MultiplyPoint3x4(Vector3.zero);
            for (int i = 0; i < t.Count; i += 3)
                TriOut(pts[t[i]], pts[t[i + 1]], pts[t[i + 2]], center, col);
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
            mesh.SetUVs(0, surface);
            mesh.RecalculateNormals(); // duplicated verts => flat facets
            mesh.RecalculateBounds();
            return mesh;
        }
    }
}
