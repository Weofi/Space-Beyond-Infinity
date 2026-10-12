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
            float n = MathUtils.Fbm3D(seed, unitDir * biome.Frequency, biome.Octaves, 2f);

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

            return height01;
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
        /// Collider mesh: icosphere subdiv 3 (642 verts), displaced identically to the visual mesh.
        /// </summary>
        public static Mesh GenerateCollider(int seed, float radius, BiomeDef biome)
        {
            Mesh mesh = IcoSphere.Create(radius, 3, "PlanetCollider");

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
        internal static Mesh GenerateVisualAt(int seed, float radius, BiomeDef biome, int subdivisions)
        {
            Mesh mesh = IcoSphere.Create(radius, subdivisions, "PlanetVisual_LOD" + subdivisions);

            Vector3[] verts = mesh.vertices;
            int[] tris = mesh.triangles;
            int count = verts.Length;

            Vector3[] dirs = new Vector3[count];
            float[] heights = new float[count];
            Vector3[] normals = new Vector3[count];

            // Displace vertices and cache heights/directions.
            for (int i = 0; i < count; i++)
            {
                Vector3 dir = verts[i].normalized;
                dirs[i] = dir;
                float h = SampleHeight01(seed, biome, dir);
                heights[i] = h;
                float r = radius * (1f + biome.Amplitude * h);
                verts[i] = dir * r;
                normals[i] = dir; // welded-sphere radial normals
            }

            // Geometric face normals for slope-based coloring (not stored in the mesh).
            Vector3[] geoNormals = new Vector3[count];
            for (int i = 0; i < tris.Length; i += 3)
            {
                int a = tris[i], b = tris[i + 1], c = tris[i + 2];
                Vector3 faceNormal = Vector3.Cross(verts[b] - verts[a], verts[c] - verts[a]).normalized;
                geoNormals[a] += faceNormal;
                geoNormals[b] += faceNormal;
                geoNormals[c] += faceNormal;
            }

            // Vertex colors by elevation, slope darkening, and per-vertex hash variation.
            Color[] colors = new Color[count];
            for (int i = 0; i < count; i++)
            {
                Color col = GetBiomeColor(biome, heights[i]);

                // Darken ~15% on steep slopes (geometric normal deviates from radial).
                Vector3 geoN = geoNormals[i].normalized;
                float slopeDot = Vector3.Dot(geoN, dirs[i]);
                if (slopeDot < 0.75f)
                    col *= 0.85f;

                // Subtle per-vertex hash variation ±5%.
                float hash = MathUtils.Hash3(seed, i, 0, 0);
                col *= 1f + (hash * 2f - 1f) * 0.05f;

                colors[i] = col;
            }

            mesh.SetVertices(verts);
            mesh.SetNormals(normals);
            mesh.SetColors(colors);
            mesh.RecalculateBounds();
            return mesh;
        }

        /// <summary>
        /// Three-band elevation color: LowColor below 0.35, HighColor above 0.75, smooth lerps through MidColor.
        /// </summary>
        static Color GetBiomeColor(BiomeDef biome, float height01)
        {
            if (height01 <= 0.35f)
                return biome.LowColor;
            if (height01 >= 0.75f)
                return biome.HighColor;
            if (height01 < 0.55f)
                return Color.Lerp(biome.LowColor, biome.MidColor, Mathf.InverseLerp(0.35f, 0.55f, height01));
            return Color.Lerp(biome.MidColor, biome.HighColor, Mathf.InverseLerp(0.55f, 0.75f, height01));
        }
    }
}
