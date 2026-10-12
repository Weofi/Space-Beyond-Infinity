using UnityEngine;
using UnityEngine.Rendering;
using Voidward.Data;

namespace Voidward.Planet
{
    /// <summary>
    /// Builds and owns the renderable parts of a procedural planet: near/far terrain LODs, ocean sphere and
    /// (optionally) a MeshCollider matching the near LOD exactly. Not networked; all inputs are deterministic.
    /// </summary>
    public class PlanetVisuals : MonoBehaviour
    {
        [Tooltip("Optional: build automatically on Start (menu backdrop, previews).")]
        [SerializeField] bool buildOnStart;
        [SerializeField] PlanetShapeDef shape;
        [SerializeField] int seed = 1;
        [SerializeField] float radius = 200f;
        [Tooltip("Camera distance (in planet radii) below which the near LOD is shown.")]
        public float NearLodDistance = 2.5f;

        public PlanetTerrain Terrain { get; private set; }
        public bool NearLodActive => near != null && near.activeSelf;
        public int NearVertexCount => nearMesh != null ? nearMesh.vertexCount : 0;
        public float BuildMilliseconds { get; private set; }

        GameObject near, far, ocean;
        Mesh nearMesh, farMesh, oceanMesh;
        Material terrainMat, oceanMat;
        Texture2D ramp;

        static readonly int RampId = Shader.PropertyToID("_Ramp");
        static readonly int DetailTexId = Shader.PropertyToID("_DetailTex");
        static readonly int DetailNormalId = Shader.PropertyToID("_DetailNormal");
        static readonly int DetailScaleId = Shader.PropertyToID("_DetailScale");
        static readonly int DetailStrengthId = Shader.PropertyToID("_DetailStrength");
        static readonly int DetailNormalStrengthId = Shader.PropertyToID("_DetailNormalStrength");
        static readonly int GlossId = Shader.PropertyToID("_Gloss");
        static readonly int CliffSlopeId = Shader.PropertyToID("_CliffSlope");
        static readonly int ShallowId = Shader.PropertyToID("_ShallowColor");
        static readonly int DeepId = Shader.PropertyToID("_DeepColor");
        static readonly int WaterDepthId = Shader.PropertyToID("_WaterDepth");
        static readonly int EmissionId = Shader.PropertyToID("_EmissionColor");
        static readonly int WaveId = Shader.PropertyToID("_WaveStrength");

        void Start()
        {
            if (buildOnStart && Terrain == null && shape != null) Build(shape, seed, radius, false);
        }

        /// <summary>Editor setup for scene-placed planets that generate themselves on Start (no collider).</summary>
        public void SetAutoBuild(PlanetShapeDef planetShape, int planetSeed, float planetRadius)
        {
            shape = planetShape; seed = planetSeed; radius = planetRadius;
            buildOnStart = true;
        }

        /// <summary>Generates all meshes. resolutionOverride > 0 replaces the shape's near-LOD resolution.</summary>
        public void Build(PlanetShapeDef planetShape, int planetSeed, float planetRadius, bool withCollider, int resolutionOverride = 0)
        {
            Clear();
            var sw = System.Diagnostics.Stopwatch.StartNew();
            shape = planetShape; seed = planetSeed; radius = planetRadius;
            Terrain = new PlanetTerrain(planetShape, planetSeed, planetRadius);

            var catalog = ContentCatalog.Instance;
            ramp = Terrain.CreateRampTexture();
            terrainMat = new Material(catalog.PlanetMaterial) { name = "PlanetTerrain (Instance)", hideFlags = HideFlags.DontSave };
            terrainMat.SetTexture(RampId, ramp);
            if (planetShape.DetailTexture != null) terrainMat.SetTexture(DetailTexId, planetShape.DetailTexture);
            if (planetShape.DetailNormal != null) terrainMat.SetTexture(DetailNormalId, planetShape.DetailNormal);
            terrainMat.SetFloat(DetailScaleId, planetShape.DetailScale);
            terrainMat.SetFloat(DetailStrengthId, planetShape.DetailStrength);
            terrainMat.SetFloat(DetailNormalStrengthId, planetShape.DetailNormalStrength);
            terrainMat.SetFloat(GlossId, planetShape.Gloss);
            terrainMat.SetVector(CliffSlopeId, planetShape.CliffSlope);

            int res = resolutionOverride > 0 ? resolutionOverride : planetShape.Resolution;
            nearMesh = PlanetMesh.BuildTerrain(Terrain, res, "PlanetNear");
            farMesh = PlanetMesh.BuildTerrain(Terrain, Mathf.Min(res, planetShape.FarResolution), "PlanetFar");
            near = Part("TerrainNear", nearMesh, terrainMat);
            far = Part("TerrainFar", farMesh, terrainMat);
            far.SetActive(false);

            if (Terrain.HasOcean)
            {
                oceanMat = new Material(catalog.OceanMaterial) { name = "PlanetOcean (Instance)", hideFlags = HideFlags.DontSave };
                oceanMat.SetColor(ShallowId, planetShape.ShallowWater);
                oceanMat.SetColor(DeepId, planetShape.DeepWater);
                oceanMat.SetFloat(WaterDepthId, planetShape.WaterDepth);
                oceanMat.SetColor(EmissionId, planetShape.WaterEmission);
                oceanMat.SetFloat(GlossId, planetShape.WaterGloss);
                oceanMat.SetFloat(WaveId, planetShape.WaveStrength);
                if (planetShape.DetailNormal != null) oceanMat.SetTexture(DetailNormalId, planetShape.DetailNormal);
                oceanMesh = PlanetMesh.BuildSphere(Terrain.SeaRadius, 64, "PlanetOcean");
                ocean = Part("Ocean", oceanMesh, oceanMat);
                ocean.GetComponent<MeshRenderer>().receiveShadows = false;
            }

            if (withCollider)
            {
                var mc = GetComponent<MeshCollider>();
                if (mc == null) mc = gameObject.AddComponent<MeshCollider>();
                mc.sharedMesh = nearMesh;
            }
            BuildMilliseconds = (float)sw.Elapsed.TotalMilliseconds;
            UpdateLod();
        }

        GameObject Part(string partName, Mesh mesh, Material mat)
        {
            var go = new GameObject(partName) { hideFlags = HideFlags.DontSave };
            go.transform.SetParent(transform, false);
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            var mr = go.AddComponent<MeshRenderer>();
            mr.sharedMaterial = mat;
            // Toon lighting provides the terminator; self-shadowing a several-hundred-meter sphere only causes acne.
            mr.shadowCastingMode = ShadowCastingMode.Off;
            return go;
        }

        public void Clear()
        {
            var mc = GetComponent<MeshCollider>();
            if (mc != null && mc.sharedMesh == nearMesh) mc.sharedMesh = null;
            Kill(near); Kill(far); Kill(ocean);
            Kill(nearMesh); Kill(farMesh); Kill(oceanMesh);
            Kill(terrainMat); Kill(oceanMat); Kill(ramp);
            near = far = ocean = null;
            nearMesh = farMesh = oceanMesh = null;
            terrainMat = oceanMat = null;
            ramp = null;
            Terrain = null;
        }

        static void Kill(Object o)
        {
            if (o == null) return;
            if (Application.isPlaying) Destroy(o); else DestroyImmediate(o);
        }

        void LateUpdate() => UpdateLod();

        void UpdateLod()
        {
            if (near == null) return;
            var cam = Camera.main;
            if (cam == null) return;
            float dist = Vector3.Distance(cam.transform.position, transform.position);
            bool useNear = dist < radius * transform.lossyScale.x * NearLodDistance;
            if (near.activeSelf != useNear)
            {
                near.SetActive(useNear);
                far.SetActive(!useNear);
            }
        }

        void OnDestroy() => Clear();
    }
}
