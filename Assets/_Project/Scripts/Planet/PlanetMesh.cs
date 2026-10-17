using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
using Voidward.Core;

namespace Voidward.Planet
{
    /// <summary>CPU-side result of a chunk build (worker thread). Uploaded to a Mesh on the main thread.</summary>
    public sealed class ChunkData
    {
        public int Face, Level, X, Y, N;
        public bool ColliderOnly;
        public Vector3 Origin;
        public Vector3[] Verts, Normals;
        public Vector2[] Uvs;
        /// <summary>UV1.x = cavity (+ concave, - convex) relative to the vertex spacing, for AO and edge light.</summary>
        public Vector2[] Uvs2;
        public Bounds Bounds;
        public float MinRadius, MaxRadius;
        /// <summary>Optional surface props for this chunk (positions relative to Origin).</summary>
        public MeshBuilder Decor;
        /// <summary>Optional asset props (#100) for this chunk (positions relative to Origin).</summary>
        public PropBatch DecorProps;
        /// <summary>Sphere colliders for large props: xyz = center relative to Origin, w = radius.</summary>
        public List<Vector4> DecorColliders;
    }

    /// <summary>
    /// Planet geometry helpers. Chunks live on a tangent-adjusted cube (near-uniform cells, cheap inverse mapping),
    /// six quadtrees deep. Shared edges between equal-level chunks use bit-identical lattice coordinates; skirts hide
    /// T-junction gaps between LOD levels.
    /// </summary>
    public static class PlanetMesh
    {
        // Face frames: normal axis plus two in-plane axes, ordered so (u x v) points along the face normal.
        static readonly Vector3Int[] FaceNormal = { new(1, 0, 0), new(-1, 0, 0), new(0, 1, 0), new(0, -1, 0), new(0, 0, 1), new(0, 0, -1) };
        static readonly Vector3Int[] FaceU = { new(0, 0, -1), new(0, 0, 1), new(1, 0, 0), new(1, 0, 0), new(1, 0, 0), new(-1, 0, 0) };
        static readonly Vector3Int[] FaceV = { new(0, 1, 0), new(0, 1, 0), new(0, 0, -1), new(0, 0, 1), new(0, 1, 0), new(0, 1, 0) };

        const float QuarterPi = Mathf.PI * 0.25f;

        /// <summary>Unit direction of cube-face coordinate (a, b) in [-1, 1].</summary>
        public static Vector3 CubeDir(int face, float a, float b)
        {
            Vector3 n = FaceNormal[face], u = FaceU[face], v = FaceV[face];
            float ta = (float)System.Math.Tan(a * QuarterPi), tb = (float)System.Math.Tan(b * QuarterPi);
            return (n + u * ta + v * tb).normalized;
        }

        /// <summary>Inverse of <see cref="CubeDir"/>.</summary>
        public static void DirToCube(Vector3 d, out int face, out float a, out float b)
        {
            float ax = Mathf.Abs(d.x), ay = Mathf.Abs(d.y), az = Mathf.Abs(d.z);
            if (ax >= ay && ax >= az) face = d.x >= 0f ? 0 : 1;
            else if (ay >= az) face = d.y >= 0f ? 2 : 3;
            else face = d.z >= 0f ? 4 : 5;
            Vector3 n = FaceNormal[face];
            Vector3 p = d / Vector3.Dot(d, n);
            a = Mathf.Atan(Vector3.Dot(p, FaceU[face])) / QuarterPi;
            b = Mathf.Atan(Vector3.Dot(p, FaceV[face])) / QuarterPi;
        }

        /// <summary>Chunk index containing a direction at the given level.</summary>
        public static void DirToChunk(Vector3 d, int level, out int face, out int x, out int y)
        {
            DirToCube(d, out face, out float a, out float b);
            int cells = 1 << level;
            x = Mathf.Clamp((int)((a + 1f) * 0.5f * cells), 0, cells - 1);
            y = Mathf.Clamp((int)((b + 1f) * 0.5f * cells), 0, cells - 1);
        }

        public static long Key(int face, int level, int x, int y) => ((long)face << 58) | ((long)level << 52) | ((long)x << 26) | (long)y;

        static float Coord(int cell, int i, int n, int level) => 2f * (cell * n + i) / (n << level) - 1f;

        /// <summary>Center direction of a chunk.</summary>
        public static Vector3 ChunkCenterDir(int face, int level, int x, int y)
        {
            float a = (2f * x + 1f) / (1 << level) - 1f, b = (2f * y + 1f) / (1 << level) - 1f;
            return CubeDir(face, a, b);
        }

        // ---------------------------------------------------------------- chunk build (thread-safe)

        public static ChunkData BuildChunk(PlanetTerrain terrain, int face, int level, int x, int y, int n, bool colliderOnly)
        {
            var data = new ChunkData { Face = face, Level = level, X = x, Y = y, N = n, ColliderOnly = colliderOnly };
            float R = terrain.Radius;
            Vector3 centerDir = ChunkCenterDir(face, level, x, y);
            data.Origin = centerDir * terrain.SurfaceRadius(centerDir);

            int side = n + 1;
            int border = colliderOnly ? 0 : 1;
            int s = side + 2 * border;
            var pos = new Vector3[s * s];
            var dirs = new Vector3[side * side];
            var elev = new float[side * side];
            var region = colliderOnly ? null : new float[side * side];
            float minR = float.MaxValue, maxR = float.MinValue;
            // Band limit (#115): detail finer than ~2 vertex spacings can't be represented and would only alias.
            float bandLimit = R * Mathf.PI * 0.5f / (n << level) * 2.2f;
            for (int jj = 0; jj < s; jj++)
            {
                int j = jj - border;
                float b = Coord(y, j, n, level);
                for (int ii = 0; ii < s; ii++)
                {
                    int i = ii - border;
                    Vector3 d = CubeDir(face, Coord(x, i, n, level), b);
                    bool inner = i >= 0 && i <= n && j >= 0 && j <= n;
                    float reg = 0f;
                    float e = inner && region != null ? terrain.ElevationFrac(d, bandLimit, out reg) : terrain.ElevationFrac(d, bandLimit);
                    float r = R * (1f + e);
                    pos[jj * s + ii] = d * r - data.Origin;
                    if (i >= 0 && i <= n && j >= 0 && j <= n)
                    {
                        dirs[j * side + i] = d;
                        elev[j * side + i] = e;
                        if (region != null) region[j * side + i] = reg;
                        if (r < minR) minR = r;
                        if (r > maxR) maxR = r;
                    }
                }
            }
            data.MinRadius = minR;
            data.MaxRadius = maxR;

            int skirt = colliderOnly ? 0 : 4 * n;
            var verts = new Vector3[side * side + skirt];
            for (int j = 0; j <= n; j++)
                for (int i = 0; i <= n; i++)
                    verts[j * side + i] = pos[(j + border) * s + i + border];

            if (!colliderOnly)
            {
                var normals = new Vector3[verts.Length];
                var uvs = new Vector2[verts.Length];
                var uvs2 = new Vector2[verts.Length];
                float spacing = bandLimit / 2.2f;
                Vector3 o = data.Origin;
                for (int j = 0; j <= n; j++)
                    for (int i = 0; i <= n; i++)
                    {
                        int c = (j + 1) * s + i + 1;
                        Vector3 nn = Vector3.Cross(pos[c + 1] - pos[c - 1], pos[c + s] - pos[c - s]).normalized;
                        int vi = j * side + i;
                        if (Vector3.Dot(nn, dirs[vi]) < 0f) nn = -nn;
                        normals[vi] = nn;
                        uvs[vi] = new Vector2(terrain.GradientCoord(elev[vi]), region[vi]);
                        // Discrete Laplacian of the radius: how far the neighbours rise above this vertex.
                        float rc = (pos[c] + o).magnitude;
                        float avg = ((pos[c + 1] + o).magnitude + (pos[c - 1] + o).magnitude + (pos[c + s] + o).magnitude + (pos[c - s] + o).magnitude) * 0.25f;
                        uvs2[vi] = new Vector2((avg - rc) / spacing, 0f);
                    }
                // Skirt: duplicate the boundary loop, pushed toward the planet center.
                float chunkSize = R * Mathf.PI * 0.5f / (1 << level);
                // Coarse chunks skip erosion octaves (band limit), so neighbours across a LOD step differ by up to the
                // gully depth: the skirt has to reach below that.
                // Province relief, noise octaves and erosion (scaled up to ~1.4 per province, gullies 1.25x) all differ
                // across a LOD step: generous margin.
                float depth = 2f + chunkSize * 0.06f + terrain.ErosionDepthMeters * 2.4f;
                var loop = EdgeLoop(n);
                for (int k = 0; k < loop.Length; k++)
                {
                    int e = loop[k];
                    int vi = side * side + k;
                    verts[vi] = verts[e] - dirs[e] * depth;
                    normals[vi] = normals[e];
                    uvs[vi] = uvs[e];
                    uvs2[vi] = uvs2[e];
                }
                data.Normals = normals;
                data.Uvs = uvs;
                data.Uvs2 = uvs2;
            }
            data.Verts = verts;
            Vector3 min = verts[0], max = verts[0];
            for (int i = 1; i < verts.Length; i++) { min = Vector3.Min(min, verts[i]); max = Vector3.Max(max, verts[i]); }
            data.Bounds = new Bounds((min + max) * 0.5f, max - min);
            return data;
        }

        static readonly Dictionary<int, int[]> loopCache = new Dictionary<int, int[]>();
        static readonly Dictionary<int, int[]> trisCache = new Dictionary<int, int[]>();
        static readonly Dictionary<int, int[]> colliderTrisCache = new Dictionary<int, int[]>();

        static int[] EdgeLoop(int n)
        {
            lock (loopCache)
            {
                if (loopCache.TryGetValue(n, out var l)) return l;
                int side = n + 1;
                l = new int[4 * n];
                int k = 0;
                for (int i = 0; i < n; i++) l[k++] = i;                         // bottom, left -> right
                for (int j = 0; j < n; j++) l[k++] = j * side + n;              // right, bottom -> top
                for (int i = n; i > 0; i--) l[k++] = n * side + i;              // top, right -> left
                for (int j = n; j > 0; j--) l[k++] = j * side;                  // left, top -> bottom
                loopCache[n] = l;
                return l;
            }
        }

        /// <summary>Triangle list for a chunk; with skirts (double-sided) unless collider.</summary>
        public static int[] Triangles(int n, bool collider)
        {
            var cache = collider ? colliderTrisCache : trisCache;
            lock (cache)
            {
                if (cache.TryGetValue(n, out var t)) return t;
                int side = n + 1;
                var list = new List<int>(n * n * 6 + (collider ? 0 : n * 4 * 12));
                for (int j = 0; j < n; j++)
                    for (int i = 0; i < n; i++)
                    {
                        int i00 = j * side + i, i10 = i00 + 1, i01 = i00 + side, i11 = i01 + 1;
                        // Alternate the diagonal for a less directional facet pattern. Front = u x v = outward.
                        if (((i + j) & 1) == 0) { list.Add(i00); list.Add(i10); list.Add(i11); list.Add(i00); list.Add(i11); list.Add(i01); }
                        else { list.Add(i00); list.Add(i10); list.Add(i01); list.Add(i10); list.Add(i11); list.Add(i01); }
                    }
                if (!collider)
                {
                    var loop = EdgeLoop(n);
                    int baseIdx = side * side, m = loop.Length;
                    for (int k = 0; k < m; k++)
                    {
                        int e0 = loop[k], e1 = loop[(k + 1) % m], s0 = baseIdx + k, s1 = baseIdx + (k + 1) % m;
                        list.Add(e0); list.Add(s0); list.Add(s1); list.Add(e0); list.Add(s1); list.Add(e1);
                        list.Add(e0); list.Add(s1); list.Add(s0); list.Add(e0); list.Add(e1); list.Add(s1);
                    }
                }
                t = list.ToArray();
                cache[n] = t;
                return t;
            }
        }

        public static Mesh UploadChunk(ChunkData d, string name)
        {
            var mesh = new Mesh { name = name, hideFlags = HideFlags.DontSave, indexFormat = d.Verts.Length > 65535 ? IndexFormat.UInt32 : IndexFormat.UInt16 };
            mesh.SetVertices(d.Verts);
            if (d.Normals != null) mesh.SetNormals(d.Normals);
            if (d.Uvs != null) mesh.SetUVs(0, d.Uvs);
            if (d.Uvs2 != null) mesh.SetUVs(1, d.Uvs2);
            mesh.SetTriangles(Triangles(d.N, d.ColliderOnly), 0, false);
            mesh.bounds = d.Bounds;
            return mesh;
        }

        // ---------------------------------------------------------------- spheres

        /// <summary>Smooth sphere (ocean surface, shells) with exact radial normals.</summary>
        public static Mesh BuildSphere(float radius, int resolution, string name)
        {
            int n = Mathf.Max(2, resolution);
            int side = n + 1;
            var verts = new List<Vector3>(6 * side * side);
            var normals = new List<Vector3>(verts.Capacity);
            var tris = new List<int>(6 * n * n * 6);
            for (int f = 0; f < 6; f++)
            {
                int start = verts.Count;
                for (int j = 0; j <= n; j++)
                    for (int i = 0; i <= n; i++)
                    {
                        Vector3 d = CubeDir(f, 2f * i / n - 1f, 2f * j / n - 1f);
                        verts.Add(d * radius);
                        normals.Add(d);
                    }
                for (int j = 0; j < n; j++)
                    for (int i = 0; i < n; i++)
                    {
                        int i00 = start + j * side + i, i10 = i00 + 1, i01 = i00 + side, i11 = i01 + 1;
                        tris.Add(i00); tris.Add(i10); tris.Add(i11);
                        tris.Add(i00); tris.Add(i11); tris.Add(i01);
                    }
            }
            var mesh = new Mesh
            {
                name = name,
                hideFlags = HideFlags.DontSave,
                indexFormat = verts.Count > 65535 ? IndexFormat.UInt32 : IndexFormat.UInt16,
            };
            mesh.SetVertices(verts);
            mesh.SetNormals(normals);
            mesh.SetTriangles(tris, 0);
            mesh.RecalculateBounds();
            return mesh;
        }
    }
}
