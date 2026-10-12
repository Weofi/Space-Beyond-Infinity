using UnityEngine;
using UnityEngine.Rendering;

namespace Voidward.Planet
{
    /// <summary>
    /// Welded spherified-cube meshes. Cube-face edges share vertices, so recalculated normals have no seams,
    /// and the spherify mapping keeps cells evenly sized (no pole pinching).
    /// </summary>
    public static class PlanetMesh
    {
        /// <summary>Terrain mesh: radially displaced, UV0.x = color ramp coordinate.</summary>
        public static Mesh BuildTerrain(PlanetTerrain terrain, int resolution, string name)
        {
            BuildLattice(resolution, out var dirs, out var tris);
            int count = dirs.Length;
            var verts = new Vector3[count];
            var uvs = new Vector2[count];
            float r = terrain.Radius;
            for (int i = 0; i < count; i++)
            {
                float e = terrain.ElevationFrac(dirs[i]);
                verts[i] = dirs[i] * (r * (1f + e));
                uvs[i] = new Vector2(terrain.GradientCoord(e), 0f);
            }
            var mesh = NewMesh(name, count);
            mesh.SetVertices(verts);
            mesh.SetUVs(0, uvs);
            mesh.SetTriangles(tris, 0);
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();
            return mesh;
        }

        /// <summary>Smooth sphere (ocean surface) with exact radial normals.</summary>
        public static Mesh BuildSphere(float radius, int resolution, string name)
        {
            BuildLattice(resolution, out var dirs, out var tris);
            var verts = new Vector3[dirs.Length];
            for (int i = 0; i < dirs.Length; i++) verts[i] = dirs[i] * radius;
            var mesh = NewMesh(name, dirs.Length);
            mesh.SetVertices(verts);
            mesh.SetNormals(dirs);
            mesh.SetTriangles(tris, 0);
            mesh.RecalculateBounds();
            return mesh;
        }

        static Mesh NewMesh(string name, int vertexCount) => new Mesh
        {
            name = name,
            hideFlags = HideFlags.DontSave,
            indexFormat = vertexCount > 65535 ? IndexFormat.UInt32 : IndexFormat.UInt16,
        };

        // Face frames: normal axis plus two in-plane axes, ordered so (u x v) points along the face normal.
        static readonly Vector3Int[] FaceNormal = { new(1, 0, 0), new(-1, 0, 0), new(0, 1, 0), new(0, -1, 0), new(0, 0, 1), new(0, 0, -1) };
        static readonly Vector3Int[] FaceU = { new(0, 0, -1), new(0, 0, 1), new(1, 0, 0), new(1, 0, 0), new(1, 0, 0), new(-1, 0, 0) };
        static readonly Vector3Int[] FaceV = { new(0, 1, 0), new(0, 1, 0), new(0, 0, -1), new(0, 0, 1), new(0, 1, 0), new(0, 1, 0) };

        /// <summary>Unit directions of all unique lattice points on the cube surface plus outward-wound triangles.</summary>
        static void BuildLattice(int n, out Vector3[] dirs, out int[] tris)
        {
            n = Mathf.Max(2, n);
            int side = n + 1;
            var index = new int[side * side * side];
            for (int i = 0; i < index.Length; i++) index[i] = -1;
            dirs = new Vector3[6 * n * n + 2];
            tris = new int[6 * n * n * 6];
            int vCount = 0, t = 0;
            var faceIdx = new int[side * side];

            for (int f = 0; f < 6; f++)
            {
                Vector3Int nrm = FaceNormal[f], u = FaceU[f], v = FaceV[f];
                for (int b = 0; b <= n; b++)
                    for (int a = 0; a <= n; a++)
                    {
                        // Lattice coordinate in [0,n]^3 on this face.
                        int ix = Coord(nrm.x, u.x, v.x, a, b, n);
                        int iy = Coord(nrm.y, u.y, v.y, a, b, n);
                        int iz = Coord(nrm.z, u.z, v.z, a, b, n);
                        int key = (ix * side + iy) * side + iz;
                        int vi = index[key];
                        if (vi < 0)
                        {
                            vi = index[key] = vCount;
                            dirs[vCount++] = Spherify(ix * 2f / n - 1f, iy * 2f / n - 1f, iz * 2f / n - 1f);
                        }
                        faceIdx[b * side + a] = vi;
                    }

                for (int b = 0; b < n; b++)
                    for (int a = 0; a < n; a++)
                    {
                        int i00 = faceIdx[b * side + a], i10 = faceIdx[b * side + a + 1];
                        int i01 = faceIdx[(b + 1) * side + a], i11 = faceIdx[(b + 1) * side + a + 1];
                        // Front face normal = cross(b - a, c - a) = u x v = outward.
                        tris[t++] = i00; tris[t++] = i10; tris[t++] = i11;
                        tris[t++] = i00; tris[t++] = i11; tris[t++] = i01;
                    }
            }
        }

        // Component of the face point: normal axis sits at 0 or n, in-plane axes run 0..n (mirrored for negative axes).
        static int Coord(int nAxis, int uAxis, int vAxis, int a, int b, int n)
        {
            if (nAxis != 0) return nAxis > 0 ? n : 0;
            if (uAxis != 0) return uAxis > 0 ? a : n - a;
            return vAxis > 0 ? b : n - b;
        }

        static Vector3 Spherify(float x, float y, float z)
        {
            float x2 = x * x, y2 = y * y, z2 = z * z;
            return new Vector3(
                x * Mathf.Sqrt(Mathf.Max(0f, 1f - y2 * 0.5f - z2 * 0.5f + y2 * z2 / 3f)),
                y * Mathf.Sqrt(Mathf.Max(0f, 1f - x2 * 0.5f - z2 * 0.5f + x2 * z2 / 3f)),
                z * Mathf.Sqrt(Mathf.Max(0f, 1f - x2 * 0.5f - y2 * 0.5f + x2 * y2 / 3f))).normalized;
        }
    }
}
