using UnityEngine;
using Voidward.Core;
using Voidward.Data;

namespace Voidward.Planet
{
    /// <summary>
    /// Deterministic procedural spherical planet mesh generation. Both visual and collider
    /// meshes share the same <see cref="SampleHeight01"/> source of truth so every client
    /// produces identical geometry from the same network-synced seed — zero terrain bandwidth.
    /// </summary>
    public static class PlanetMeshGenerator
    {
        // ---- Height ----

        /// <summary>
        /// Single source of truth for terrain height at a direction on the unit sphere.
        /// Returns a value in [0,1] combining fbm noise with a ridged component and a
        /// smoothstep basin floor at <paramref name="biome"/>.SeaLevel.
        /// </summary>
        public static float SampleHeight01(int seed, BiomeDef biome, Vector3 unitDir)
        {
            // Base fractal noise (~[-1,1]). Frequency 2 matches the intended default
            // (contract gap: MathUtils.Fbm3D requires a frequency arg with no default).
            float n = Mathf.Clamp(MathUtils.Fbm3D(seed, unitDir * biome.Frequency, biome.Octaves, 2f) * 1.9f, -1f, 1f);
            // Crunchy small-scale detail so the low-poly facets read on foot.
            n += MathUtils.Fbm3D(seed + 977, unitDir * biome.Frequency, 2, 14f) * 0.22f;

            // Ridged component: 1-|n| creates sharp mountain ridges where noise crosses zero.
            float ridge = 1f - Mathf.Abs(n);

            // Mix ~35% ridged for mountain character.
            float h = n * 0.65f + ridge * 0.35f;

            // Apply sea level as smoothstep basin floor: below-sea areas form a gentle basin,
            // above-sea areas have full terrain expression. Normalize to [0,1].
            float t = Mathf.InverseLerp(-1f, 1f, h);
            float seaT = Mathf.InverseLerp(-1f, 1f, biome.SeaLevel);

            float height01 = (t <= seaT)
                ? Mathf.SmoothStep(0f, seaT, t) * 0.35f
                : 0.35f + Mathf.SmoothStep(seaT, 1f, t) * 0.65f;

            return Mathf.Clamp01(height01 + CraterOffset(seed, unitDir));
        }

        const int CraterCount = 12;

        /// <summary>Bowl-shaped craters with raised rims at seed-hashed spots (height01 units).</summary>
        static float CraterOffset(int seed, Vector3 dir)
        {
            float off = 0f;
            for (int k = 0; k < CraterCount; k++)
            {
                var c = new Vector3(
                    MathUtils.Hash3(seed, k, 11, 0) * 2f - 1f,
                    MathUtils.Hash3(seed, k, 12, 0) * 2f - 1f,
                    MathUtils.Hash3(seed, k, 13, 0) * 2f - 1f);
                if (c.sqrMagnitude < 1e-4f) continue;
                c.Normalize();
                float dot = Vector3.Dot(dir, c);
                if (dot < 0.9f) continue; // beyond the largest crater radius
                float size = Mathf.Lerp(0.09f, 0.24f, MathUtils.Hash3(seed, k, 14, 0)); // angular radius (rad)
                float d = Mathf.Acos(Mathf.Min(dot, 1f)) / size;
                if (d > 1.7f) continue;
                float bowl = d < 1f ? (d * d - 1f) * 0.22f : 0f;
                float rim = Mathf.Exp(-(d - 1f) * (d - 1f) * 9f) * 0.1f;
                off += bowl + rim;
            }
            return off;
        }

        /// <summary>
        /// Local-space surface position at a direction on the unit sphere (displaced by terrain).
        /// </summary>
        public static Vector3 SurfacePoint(int seed, float radius, BiomeDef biome, Vector3 unitDir)
        {
            float h = SampleHeight01(seed, biome, unitDir);
            return unitDir * (radius * (1f + biome.Amplitude * h));
        }

        // ---- Meshes ----

        /// <summary>
        /// High-detail visual mesh: icosphere subdiv 4 (2562 verts), displaced, vertex-colored,
        /// with radial normals. Flat shading is achieved in the ToonFlat shader via _FLATSHADING_ON.
        /// </summary>
        public static Mesh GenerateVisual(int seed, float radius, BiomeDef biome)
            => GenerateVisualAt(seed, radius, biome, 4);

        /// <summary>
        /// Welded collider mesh, displaced identically to the visual mesh of the same subdivision.
        /// </summary>
        public static Mesh GenerateCollider(int seed, float radius, BiomeDef biome, int subdivisions = 3)
        {
            Mesh mesh = IcoSphere.Create(radius, subdivisions, "PlanetCollider");

            Vector3[] verts = mesh.vertices;
            for (int i = 0; i < verts.Length; i++)
            {
                Vector3 dir = verts[i].normalized;
                float h = SampleHeight01(seed, biome, dir);
                verts[i] = dir * (radius * (1f + biome.Amplitude * h));
            }

            mesh.SetVertices(verts);
            mesh.RecalculateBounds();
            return mesh;
        }

        /// <summary>
        /// Visual mesh at a given subdivision level. subdiv 4 = LOD0 (2562 verts), subdiv 2 = LOD1 (162 verts).
        /// </summary>
        public static Mesh GenerateVisualAt(int seed, float radius, BiomeDef biome, int subdivisions)
        {
            Mesh mesh = IcoSphere.Create(radius, subdivisions, "PlanetVisual_LOD" + subdivisions);

            Vector3[] verts = mesh.vertices;
            int[] tris = mesh.triangles;
            int count = verts.Length;

            Vector3[] dirs = new Vector3[count];
            float[] heights = new float[count];

            // Displace vertices and cache heights/directions.
            for (int i = 0; i < count; i++)
            {
                Vector3 dir = verts[i].normalized;
                dirs[i] = dir;
                float h = SampleHeight01(seed, biome, dir);
                heights[i] = h;
                verts[i] = dir * (radius * (1f + biome.Amplitude * h));
            }

            // Unweld so every facet gets one flat color (stylized low-poly look) and a true face normal.
            int triCount = tris.Length;
            var fVerts = new Vector3[triCount];
            var fNormals = new Vector3[triCount];
            var fColors = new Color[triCount];
            var fTris = new int[triCount];
            for (int i = 0; i < triCount; i += 3)
            {
                int a = tris[i], b = tris[i + 1], c = tris[i + 2];
                Vector3 fn = Vector3.Cross(verts[b] - verts[a], verts[c] - verts[a]).normalized;
                Color fc = GetBiomeColor(biome, (heights[a] + heights[b] + heights[c]) / 3f);
                // Steep facets read as darker rock; a small per-facet hash keeps large areas from looking flat.
                if (Vector3.Dot(fn, (dirs[a] + dirs[b] + dirs[c]).normalized) < 0.8f) fc *= 0.82f;
                fc *= 1f + (MathUtils.Hash3(seed, i, 0, 0) * 2f - 1f) * 0.1f;
                fc.a = 1f;
                for (int j = 0; j < 3; j++)
                {
                    fVerts[i + j] = verts[tris[i + j]];
                    fNormals[i + j] = fn;
                    fColors[i + j] = fc;
                    fTris[i + j] = i + j;
                }
            }

            mesh.Clear();
            mesh.indexFormat = triCount > 65000 ? UnityEngine.Rendering.IndexFormat.UInt32 : UnityEngine.Rendering.IndexFormat.UInt16;
            mesh.SetVertices(fVerts);
            mesh.SetNormals(fNormals);
            mesh.SetColors(fColors);
            mesh.SetTriangles(fTris, 0);
            mesh.RecalculateBounds();
            return mesh;
        }

        /// <summary>
        /// Three-band elevation color: LowColor below 0.22, HighColor only on peaks above 0.85, MidColor dominates between.
        /// </summary>
        static Color GetBiomeColor(BiomeDef biome, float height01)
        {
            if (height01 <= 0.22f)
                return biome.LowColor;
            if (height01 >= 0.85f)
                return biome.HighColor;
            if (height01 < 0.5f)
                return Color.Lerp(biome.LowColor, biome.MidColor, Mathf.InverseLerp(0.22f, 0.5f, height01));
            if (height01 < 0.7f)
                return biome.MidColor;
            return Color.Lerp(biome.MidColor, biome.HighColor, Mathf.InverseLerp(0.7f, 0.85f, height01));
        }
    }
}
