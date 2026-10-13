using UnityEngine;

namespace Voidward.Core
{
    /// <summary>
    /// Pooled scorch / burn decals (#43): small quads laid onto the hit surface with the VW_Scorch shader.
    /// A fixed ring of decals, the oldest is recycled. Decals are parented to the object they hit (ships,
    /// asteroids, the planet) so they ride along; when that object is destroyed its decals go with it and
    /// the slot is rebuilt on its next use. Each decal glows hot, cools down, lingers and fades out.
    /// Purely local and cosmetic.
    /// </summary>
    public sealed class DecalPool
    {
        public const int Max = 48;
        const float Life = 24f, FadeTime = 5f;

        sealed class Decal
        {
            public GameObject Go;
            public MeshRenderer Renderer;
            public Transform Parent;
            public float Born, Cool;
            public Color Heat;
            public bool Live;
        }

        static readonly int HeatId = Shader.PropertyToID("_Heat");
        static readonly int HeatColorId = Shader.PropertyToID("_HeatColor");
        static readonly int FadeId = Shader.PropertyToID("_Fade");
        static readonly int DarknessId = Shader.PropertyToID("_Darkness");
        static readonly int SeedId = Shader.PropertyToID("_Seed");

        readonly Decal[] decals = new Decal[Max];
        readonly Material material;
        readonly Transform root;
        readonly MaterialPropertyBlock mpb = new MaterialPropertyBlock();
        int next;
        static Mesh quad;

        public DecalPool(Material material, Transform root)
        {
            this.material = material;
            this.root = root;
        }

        /// <summary>
        /// Lay a decal at a surface point. parent = the object it sticks to (null = world, cleared on sector change).
        /// size in metres, heat = HDR glow colour of the fresh mark, cool = seconds until the glow is gone.
        /// </summary>
        public void Place(Vector3 pos, Vector3 normal, Transform parent, float size, Color heat, float cool, float darkness = 0.85f)
        {
            if (material == null || normal.sqrMagnitude < 0.01f) return;
            normal.Normalize();
            float now = Time.time;
            // Rapid fire into one spot re-heats the existing mark instead of stacking quads on top of each other.
            for (int i = 0; i < Max; i++)
            {
                var e = decals[i];
                if (e == null || !e.Live || e.Go == null || e.Parent != parent) continue;
                if ((e.Go.transform.position - pos).sqrMagnitude > size * size * 0.09f) continue;
                e.Born = now - Mathf.Min(now - e.Born, 1f);
                e.Cool = Mathf.Max(e.Cool, cool);
                e.Heat = heat;
                return;
            }

            int slot = next;
            next = (next + 1) % Max;
            var d = decals[slot];
            if (d == null) d = decals[slot] = new Decal();
            if (d.Go == null)
            {
                d.Go = new GameObject("Scorch") { hideFlags = HideFlags.DontSave };
                d.Go.AddComponent<MeshFilter>().sharedMesh = Quad();
                d.Renderer = d.Go.AddComponent<MeshRenderer>();
                d.Renderer.sharedMaterial = material;
                d.Renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                d.Renderer.receiveShadows = false;
                d.Renderer.lightProbeUsage = UnityEngine.Rendering.LightProbeUsage.Off;
                d.Renderer.reflectionProbeUsage = UnityEngine.Rendering.ReflectionProbeUsage.Off;
            }
            var t = d.Go.transform;
            t.SetParent(parent != null ? parent : root, false);
            Vector3 tangent = Vector3.Cross(normal, Mathf.Abs(normal.y) < 0.9f ? Vector3.up : Vector3.right);
            var rot = Quaternion.LookRotation(-normal, tangent) * Quaternion.Euler(0f, 0f, Random.Range(0f, 360f));
            t.SetPositionAndRotation(pos + normal * 0.02f, rot);
            Vector3 ls = t.parent != null ? t.parent.lossyScale : Vector3.one;
            float s = size * Random.Range(0.85f, 1.15f);
            t.localScale = new Vector3(s / Mathf.Max(0.001f, Mathf.Abs(ls.x)), s / Mathf.Max(0.001f, Mathf.Abs(ls.y)), s / Mathf.Max(0.001f, Mathf.Abs(ls.z)));
            d.Go.SetActive(true);
            d.Parent = parent;
            d.Born = now;
            d.Cool = Mathf.Max(0.05f, cool);
            d.Heat = heat;
            d.Live = true;
            mpb.Clear();
            mpb.SetFloat(SeedId, Random.Range(0f, 100f));
            mpb.SetFloat(DarknessId, darkness);
            Apply(d, now);
        }

        /// <summary>Per frame: cool down and fade the live decals, retire expired ones.</summary>
        public void Tick()
        {
            float now = Time.time;
            for (int i = 0; i < Max; i++)
            {
                var d = decals[i];
                if (d == null || !d.Live) continue;
                if (d.Go == null) { d.Live = false; continue; } // parent was destroyed
                float age = now - d.Born;
                if (age >= Life) { d.Live = false; d.Go.SetActive(false); continue; }
                // Steady state (cold, not fading yet) needs no update.
                if (age > d.Cool + 0.1f && age < Life - FadeTime - 0.1f) continue;
                d.Renderer.GetPropertyBlock(mpb);
                Apply(d, now);
            }
        }

        void Apply(Decal d, float now)
        {
            float age = now - d.Born;
            float heat = Mathf.Clamp01(1f - age / d.Cool);
            float fade = Mathf.Clamp01((Life - age) / FadeTime);
            mpb.SetFloat(HeatId, heat);
            mpb.SetColor(HeatColorId, d.Heat);
            mpb.SetFloat(FadeId, fade * Mathf.Clamp01(age * 30f + 0.3f));
            d.Renderer.SetPropertyBlock(mpb);
        }

        /// <summary>Hide every decal (sector change: the floating origin moves, world-anchored marks would be wrong).</summary>
        public void Clear()
        {
            for (int i = 0; i < Max; i++)
            {
                var d = decals[i];
                if (d == null) continue;
                d.Live = false;
                if (d.Go != null) { d.Go.SetActive(false); d.Go.transform.SetParent(root, false); }
                d.Parent = null;
            }
        }

        public int LiveCount
        {
            get { int n = 0; for (int i = 0; i < Max; i++) if (decals[i] != null && decals[i].Live && decals[i].Go != null) n++; return n; }
        }

        static Mesh Quad()
        {
            if (quad != null) return quad;
            quad = new Mesh { name = "ScorchQuad", hideFlags = HideFlags.DontSave };
            quad.SetVertices(new[] { new Vector3(-0.5f, -0.5f, 0f), new Vector3(0.5f, -0.5f, 0f), new Vector3(0.5f, 0.5f, 0f), new Vector3(-0.5f, 0.5f, 0f) });
            quad.SetUVs(0, new[] { new Vector2(0, 0), new Vector2(1, 0), new Vector2(1, 1), new Vector2(0, 1) });
            quad.SetNormals(new[] { Vector3.back, Vector3.back, Vector3.back, Vector3.back });
            quad.SetTriangles(new[] { 0, 2, 1, 0, 3, 2 }, 0);
            quad.RecalculateBounds();
            return quad;
        }
    }
}
