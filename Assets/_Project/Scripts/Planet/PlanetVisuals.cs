using System.Collections.Generic;
using System.Threading.Tasks;
using UnityEngine;
using UnityEngine.Rendering;
using Voidward.Core;
using Voidward.Data;

namespace Voidward.Planet
{
    /// <summary>
    /// Renders and streams a procedural planet: six chunked quadtrees (terrain LOD around the camera, built on
    /// worker threads), collider tiles streamed around <see cref="TerrainAnchor"/>s, per-chunk props, landmark set
    /// pieces, ocean, atmosphere and cloud shells. Not networked; every input is deterministic.
    /// </summary>
    public class PlanetVisuals : MonoBehaviour, ISurface
    {
        /// <summary>Terrain, landmarks and decor colliders all live below this object: shots kick up dirt.</summary>
        public SurfaceKind Surface => SurfaceKind.Dirt;

        [Tooltip("Optional: build automatically on Start (menu backdrop, previews).")]
        [SerializeField] bool buildOnStart;
        [SerializeField] PlanetShapeDef shape;
        [SerializeField] int seed = 1;
        [SerializeField] float radius = 200f;

        public static readonly List<PlanetVisuals> All = new List<PlanetVisuals>();

        public PlanetTerrain Terrain { get; private set; }
        public PlanetShapeDef Shape => shape;
        public float BuildMilliseconds { get; private set; }
        public int MaxLevel { get; private set; }
        public int VisibleChunks { get; private set; }
        public int ColliderTiles => tiles.Count;
        public int PendingJobs => pending.Count + running.Count;
        /// <summary>Highest LOD level currently shown (debug).</summary>
        public int DeepestVisible { get; private set; }
        /// <summary>Extra collider anchors besides <see cref="TerrainAnchor.All"/> (dev preview probes).</summary>
        public readonly List<Transform> ExtraAnchors = new List<Transform>();
        /// <summary>Landmark set pieces (giant rock formations, crystals, arches...) of this planet.</summary>
        public PlanetLandmarks Landmarks { get; private set; }

        // ---------------------------------------------------------------- quadtree

        sealed class Node
        {
            public int Face, Level, X, Y;
            public Node Parent;
            public Node[] Kids;
            public Vector3 Center;
            public float Size, Bound;
            public Job Job;
            public GameObject Go, DecorGo;
            public MeshRenderer Renderer;
            public Mesh Mesh, DecorMesh;
            public GameObject PropGo;
            public Mesh PropMesh;
            public bool Dead;
            public bool Ready => Mesh != null;
        }

        sealed class Tile
        {
            public long Key;
            public int Face, X, Y;
            public Job Job;
            public GameObject Go;
            public Mesh Mesh;
            public float LastWanted;
            public bool Ready => Mesh != null;
        }

        sealed class Job
        {
            public Node Node;
            public Tile Tile;
            public float Priority;
            public Task<ChunkData> Task;
            public ChunkData Result;
        }

        const int MaxUploadsPerFrame = 12;
        const float UploadBudgetMs = 4f;

        readonly Node[] roots = new Node[6];
        readonly List<Job> pending = new List<Job>();
        readonly List<Job> running = new List<Job>();
        readonly Dictionary<long, Tile> tiles = new Dictionary<long, Tile>();
        readonly List<long> tileScratch = new List<long>();
        readonly HashSet<long> wantedScratch = new HashSet<long>();
        int chunkN, smallDecorLevel, largeDecorLevel;
        float faceArc, lodQuality, nextColliderUpdate;
        bool streamColliders;
        Vector3 camLocal;
        int maxRunning;

        GameObject ocean, atmosphere, clouds;
        Mesh oceanMesh, atmosphereMesh, cloudMesh;
        Material terrainMat, oceanMat, atmosphereMat, cloudMat;
        /// <summary>This planet's terrain material instance (dev tests toggle its keywords).</summary>
        public Material TerrainMat => terrainMat;
        Material decorMat;
        PlanetPropLibrary props;
        readonly Dictionary<(Material, string), Material> propMats = new Dictionary<(Material, string), Material>();
        Texture2D ramp;

        static readonly int RampId = Shader.PropertyToID("_Ramp");
        static readonly int DetailTexId = Shader.PropertyToID("_DetailTex");
        static readonly int DetailNormalId = Shader.PropertyToID("_DetailNormal");
        static readonly int DetailScaleId = Shader.PropertyToID("_DetailScale");
        static readonly int DetailStrengthId = Shader.PropertyToID("_DetailStrength");
        static readonly int DetailNormalStrengthId = Shader.PropertyToID("_DetailNormalStrength");
        static readonly int GlossId = Shader.PropertyToID("_Gloss");
        static readonly int CliffSlopeId = Shader.PropertyToID("_CliffSlope");
        static readonly int RegionStrengthId = Shader.PropertyToID("_RegionStrength");
        static readonly int ShallowId = Shader.PropertyToID("_ShallowColor");
        static readonly int DeepId = Shader.PropertyToID("_DeepColor");
        static readonly int WaterDepthId = Shader.PropertyToID("_WaterDepth");
        static readonly int EmissionId = Shader.PropertyToID("_EmissionColor");
        static readonly int WaveId = Shader.PropertyToID("_WaveStrength");
        static readonly int ColorId = Shader.PropertyToID("_Color");
        static readonly int SunsetId = Shader.PropertyToID("_SunsetColor");
        static readonly int PlanetRadiusId = Shader.PropertyToID("_PlanetRadius");
        static readonly int AtmoRadiusId = Shader.PropertyToID("_AtmoRadius");
        static readonly int GroundRadiusId = Shader.PropertyToID("_GroundRadius");
        static readonly int DensityId = Shader.PropertyToID("_Density");
        static readonly int FalloffId = Shader.PropertyToID("_Falloff");
        static readonly int CoverageId = Shader.PropertyToID("_Coverage");
        static readonly int ShadeId = Shader.PropertyToID("_ShadeColor");
        static readonly int ScaleId = Shader.PropertyToID("_Scale");
        static readonly int StretchId = Shader.PropertyToID("_Stretch");
        static readonly int SeedId = Shader.PropertyToID("_Seed");
        static readonly int HazeId = Shader.PropertyToID("_HazeColor");
        static readonly int PlanetCenterId = Shader.PropertyToID("_PlanetCenter");
        static readonly int PeakStartId = Shader.PropertyToID("_PeakStart");
        static readonly int ShoreBandId = Shader.PropertyToID("_ShoreBand");
        static readonly int SkyColorId = Shader.PropertyToID("_SkyColor");
        static readonly int HorizonColorId = Shader.PropertyToID("_HorizonColor");
        static readonly int HasSkyId = Shader.PropertyToID("_HasSky");
        static readonly int LavaId = Shader.PropertyToID("_Lava");

        void OnEnable() => All.Add(this);
        void OnDisable() => All.Remove(this);

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

        /// <summary>Generates the planet. withColliders streams collider tiles around anchors.</summary>
        public void Build(PlanetShapeDef planetShape, int planetSeed, float planetRadius, bool withColliders)
        {
            Clear();
            var sw = System.Diagnostics.Stopwatch.StartNew();
            shape = planetShape; seed = planetSeed; radius = planetRadius;
            streamColliders = withColliders;
            IcoSphere.GetBaseTris(); // lazy statics must exist before worker threads use MeshBuilder
            Terrain = new PlanetTerrain(planetShape, planetSeed, planetRadius);

            chunkN = Mathf.Clamp(planetShape.ChunkResolution, 8, 64);
            faceArc = planetRadius * Mathf.PI * 0.5f;
            lodQuality = planetShape.LodQuality;
            MaxLevel = Mathf.Clamp(Mathf.RoundToInt(Mathf.Log(faceArc / (chunkN * planetShape.LeafSpacing), 2f)), 0, 12);
            smallDecorLevel = MaxLevel;
            largeDecorLevel = Mathf.Max(0, MaxLevel - 1);
#if UNITY_WEBGL && !UNITY_EDITOR
            maxRunning = 0;
#else
            maxRunning = Mathf.Clamp(SystemInfo.processorCount - 2, 2, 8);
#endif

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
            terrainMat.SetFloat(RegionStrengthId, planetShape.RegionStrength);
            terrainMat.SetVector(PlanetCenterId, transform.position);
            ApplySurfaceLayers(terrainMat, planetShape);
            decorMat = catalog.TerrainMaterial != null ? catalog.TerrainMaterial : catalog.ToonMaterial;
            props = PlanetPropLibrary.Get(catalog);
            if (props.Models.Count == 0) props = null;

            for (int f = 0; f < 6; f++)
            {
                roots[f] = NewNode(null, f, 0, 0, 0);
                CompleteNode(roots[f], RunJob(new Job { Node = roots[f] }, Terrain));
            }

            if (Terrain.HasOcean) BuildOcean(planetShape, catalog);
            if (planetShape.HasAtmosphere) BuildAtmosphere(planetShape, catalog);
            Landmarks = new PlanetLandmarks(this, Terrain, decorMat, props);
            Landmarks.Build();

            BuildMilliseconds = (float)sw.Elapsed.TotalMilliseconds;
            PlanetAmbience.Ensure();
            UpdateCamera();
        }

        /// <summary>PBR terrain layers (#100): textures + (scale, tint, normal, valid) per slot, keyword on when the ground layer exists.</summary>
        static void ApplySurfaceLayers(Material m, PlanetShapeDef s)
        {
            if (!s.HasSurfaceLayers)
            {
                m.DisableKeyword("_SURFACE_LAYERS");
                return;
            }
            m.EnableKeyword("_SURFACE_LAYERS");
            SetLayer(m, "Ground", s.GroundLayer);
            SetLayer(m, "Cliff", s.CliffLayer);
            SetLayer(m, "Region", s.RegionLayer);
            SetLayer(m, "Low", s.LowLayer);
            SetLayer(m, "Peak", s.PeakLayer);
            m.SetFloat(PeakStartId, s.PeakStart);
            m.SetFloat(ShoreBandId, s.ShoreBand);
        }

        static void SetLayer(Material m, string slot, PlanetShapeDef.SurfaceLayer l)
        {
            bool valid = l != null && l.Valid;
            m.SetVector("_" + slot + "Params", valid ? new Vector4(Mathf.Max(0.1f, l.Scale), l.Tint, l.NormalStrength, 1f) : new Vector4(4f, 0f, 0f, 0f));
            if (!valid) return;
            m.SetTexture("_" + slot + "Albedo", l.Albedo);
            if (l.Normal != null) m.SetTexture("_" + slot + "Normal", l.Normal);
            if (l.Mask != null) m.SetTexture("_" + slot + "Mask", l.Mask);
            // alpha = contrast (Material.SetColor leaves alpha linear)
            m.SetColor("_" + slot + "Mean", new Color(l.Mean.r, l.Mean.g, l.Mean.b, l.Contrast));
        }

        void BuildOcean(PlanetShapeDef s, ContentCatalog catalog)
        {
            oceanMat = new Material(catalog.OceanMaterial) { name = "PlanetOcean (Instance)", hideFlags = HideFlags.DontSave };
            oceanMat.SetColor(ShallowId, s.ShallowWater);
            oceanMat.SetColor(DeepId, s.DeepWater);
            oceanMat.SetFloat(WaterDepthId, s.WaterDepth);
            oceanMat.SetColor(EmissionId, s.WaterEmission);
            oceanMat.SetFloat(GlossId, s.WaterGloss);
            oceanMat.SetFloat(WaveId, s.WaveStrength);
            if (s.DetailNormal != null) oceanMat.SetTexture(DetailNormalId, s.DetailNormal);
            if (s.DetailTexture != null) oceanMat.SetTexture(DetailTexId, s.DetailTexture);
            // The sea reflects this planet's sky, not the space skybox.
            oceanMat.SetColor(SkyColorId, s.AtmosphereColor);
            oceanMat.SetColor(HorizonColorId, Color.Lerp(s.HazeColor, Color.white, 0.25f));
            oceanMat.SetFloat(HasSkyId, s.HasAtmosphere ? 1f : 0f);
            oceanMat.SetFloat(LavaId, s.Lava ? 1f : 0f);
            // Before the atmosphere shell (which hazes the sea through the ground sphere).
            oceanMat.renderQueue = 2850;
            oceanMesh = PlanetMesh.BuildSphere(Terrain.SeaRadius, 96, "PlanetOcean");
            ocean = Part("Ocean", oceanMesh, oceanMat);
            ocean.GetComponent<MeshRenderer>().receiveShadows = false;
        }

        void BuildAtmosphere(PlanetShapeDef s, ContentCatalog catalog)
        {
            float atmoR = Terrain.AtmosphereRadius;
            if (catalog.AtmosphereMaterial != null)
            {
                atmosphereMat = new Material(catalog.AtmosphereMaterial) { name = "Atmosphere (Instance)", hideFlags = HideFlags.DontSave };
                atmosphereMat.SetColor(ColorId, s.AtmosphereColor);
                atmosphereMat.SetColor(SunsetId, s.SunsetColor);
                atmosphereMat.SetColor(HazeId, s.HazeColor);
                atmosphereMat.SetFloat(PlanetRadiusId, radius);
                atmosphereMat.SetFloat(AtmoRadiusId, atmoR);
                atmosphereMat.SetFloat(GroundRadiusId, Terrain.GroundFloorRadius);
                atmosphereMat.SetFloat(DensityId, s.AtmosphereDensity);
                atmosphereMat.SetFloat(FalloffId, s.AtmosphereFalloff);
                atmosphereMesh = PlanetMesh.BuildSphere(atmoR, 24, "Atmosphere");
                atmosphere = Part("Atmosphere", atmosphereMesh, atmosphereMat);
                var mr = atmosphere.GetComponent<MeshRenderer>();
                mr.receiveShadows = false;
                mr.lightProbeUsage = LightProbeUsage.Off;
            }
            if (s.CloudCoverage > 0.01f && catalog.CloudMaterial != null)
            {
                float cloudR = Mathf.Max(Terrain.MaxRadius + 25f, Mathf.Lerp(radius, atmoR, s.CloudAltitude));
                cloudMat = new Material(catalog.CloudMaterial) { name = "Clouds (Instance)", hideFlags = HideFlags.DontSave };
                cloudMat.SetColor(ColorId, s.CloudColor);
                cloudMat.SetColor(ShadeId, s.CloudShadeColor);
                cloudMat.SetFloat(CoverageId, s.CloudCoverage);
                cloudMat.SetFloat(ScaleId, s.CloudScale);
                cloudMat.SetFloat(StretchId, s.CloudStretch);
                cloudMat.SetFloat(SeedId, (seed & 1023) * 0.731f);
                cloudMat.SetFloat(PlanetRadiusId, cloudR);
                cloudMesh = PlanetMesh.BuildSphere(cloudR, 48, "Clouds");
                clouds = Part("Clouds", cloudMesh, cloudMat);
                clouds.GetComponent<MeshRenderer>().receiveShadows = false;
            }
        }

        GameObject Part(string partName, Mesh mesh, Material mat)
        {
            var go = new GameObject(partName) { hideFlags = HideFlags.DontSave };
            go.transform.SetParent(transform, false);
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            var mr = go.AddComponent<MeshRenderer>();
            mr.sharedMaterial = mat;
            mr.shadowCastingMode = ShadowCastingMode.Off;
            return go;
        }

        public void Clear()
        {
            foreach (var j in running) { if (j.Node != null) j.Node.Dead = true; }
            pending.Clear();
            running.Clear();
            for (int f = 0; f < 6; f++) { if (roots[f] != null) KillNode(roots[f]); roots[f] = null; }
            foreach (var t in tiles.Values) KillTile(t);
            tiles.Clear();
            Landmarks?.Clear();
            Landmarks = null;
            Kill(ocean); Kill(atmosphere); Kill(clouds);
            Kill(oceanMesh); Kill(atmosphereMesh); Kill(cloudMesh);
            Kill(terrainMat); Kill(oceanMat); Kill(atmosphereMat); Kill(cloudMat); Kill(ramp);
            foreach (var m in propMats.Values) Kill(m);
            propMats.Clear();
            ocean = atmosphere = clouds = null;
            oceanMesh = atmosphereMesh = cloudMesh = null;
            terrainMat = oceanMat = atmosphereMat = cloudMat = null;
            ramp = null;
            Terrain = null;
        }

        static void Kill(Object o)
        {
            if (o == null) return;
            if (Application.isPlaying) Destroy(o); else DestroyImmediate(o);
        }

        void OnDestroy() => Clear();

        // ---------------------------------------------------------------- per frame

        void LateUpdate()
        {
            if (Terrain == null) return;
            terrainMat.SetVector(PlanetCenterId, transform.position);
            PollJobs();
            UpdateCamera();
            if (streamColliders && Time.unscaledTime >= nextColliderUpdate) UpdateColliders();
            StartJobs();
        }

        Camera ViewCamera()
        {
            var cam = Camera.main;
            if (cam != null) return cam;
            return Camera.allCamerasCount > 0 ? Camera.allCameras[0] : null;
        }

        void UpdateCamera()
        {
            if (Terrain == null) return;
            var cam = ViewCamera();
            camLocal = cam != null ? transform.InverseTransformPoint(cam.transform.position) : Vector3.one * radius * 10f;
            int visible = 0, deepest = 0;
            for (int f = 0; f < 6; f++) UpdateNode(roots[f], ref visible, ref deepest);
            VisibleChunks = visible;
            DeepestVisible = deepest;
        }

        Node NewNode(Node parent, int face, int level, int x, int y)
        {
            var n = new Node { Parent = parent, Face = face, Level = level, X = x, Y = y };
            n.Size = faceArc / (1 << level);
            Vector3 dir = PlanetMesh.ChunkCenterDir(face, level, x, y);
            n.Center = dir * radius;
            n.Bound = n.Size * 0.75f + (Terrain.MaxRadius - Terrain.MinRadius);
            return n;
        }

        float Distance(Node n) => Mathf.Max(0f, Vector3.Distance(camLocal, n.Center) - n.Bound);

        /// <summary>True when no part of the chunk (up to the highest peak) can rise above the camera's horizon.</summary>
        bool BeyondHorizon(Node n)
        {
            if (n.Level == 0) return false;
            float camDist = camLocal.magnitude;
            float rMin = Terrain.GroundFloorRadius, rMax = Terrain.MaxRadius;
            if (camDist <= rMax) camDist = Mathf.Max(camDist, rMin + 1f);
            float horizon = Mathf.Acos(Mathf.Clamp(rMin / camDist, -1f, 1f)) + Mathf.Acos(Mathf.Clamp(rMin / rMax, -1f, 1f));
            float nodeAngle = Vector3.Angle(camLocal, n.Center) * Mathf.Deg2Rad - n.Size * 0.75f / radius;
            return nodeAngle > horizon + 0.05f;
        }

        void UpdateNode(Node n, ref int visible, ref int deepest)
        {
            if (BeyondHorizon(n))
            {
                // Hidden by the planet itself: keep it coarse and skip drawing it.
                if (n.Kids != null) { for (int k = 0; k < 4; k++) KillNode(n.Kids[k]); n.Kids = null; }
                if (!n.Ready && n.Job == null) Request(n);
                SetVisible(n, false);
                return;
            }
            float d = Distance(n);
            float splitAt = n.Size * lodQuality;
            bool canSplit = n.Level < MaxLevel;
            bool wantSplit = canSplit && (d < splitAt || (n.Kids != null && d < splitAt * 1.2f));
            if (wantSplit)
            {
                if (n.Kids == null)
                {
                    n.Kids = new Node[4];
                    for (int k = 0; k < 4; k++)
                    {
                        var c = NewNode(n, n.Face, n.Level + 1, n.X * 2 + (k & 1), n.Y * 2 + (k >> 1));
                        n.Kids[k] = c;
                        Request(c);
                    }
                }
                bool kidsReady = true;
                for (int k = 0; k < 4; k++)
                {
                    if (n.Kids[k].Ready) continue;
                    kidsReady = false;
                    if (n.Kids[k].Job != null) n.Kids[k].Job.Priority = Mathf.Min(n.Kids[k].Job.Priority, Priority(n.Kids[k]));
                }
                if (kidsReady)
                {
                    SetVisible(n, false);
                    for (int k = 0; k < 4; k++) UpdateNode(n.Kids[k], ref visible, ref deepest);
                    return;
                }
                for (int k = 0; k < 4; k++) HideSubtree(n.Kids[k]);
            }
            else if (n.Kids != null)
            {
                for (int k = 0; k < 4; k++) KillNode(n.Kids[k]);
                n.Kids = null;
            }
            if (!n.Ready && n.Job == null) Request(n);
            SetVisible(n, n.Ready);
            if (n.Ready) { visible++; deepest = Mathf.Max(deepest, n.Level); }
        }

        static void SetVisible(Node n, bool on)
        {
            if (n.Renderer != null && n.Renderer.enabled != on) n.Renderer.enabled = on;
        }

        static void HideSubtree(Node n)
        {
            SetVisible(n, false);
            if (n.Kids != null) for (int k = 0; k < 4; k++) HideSubtree(n.Kids[k]);
        }

        float Priority(Node n) => Distance(n) / n.Size - n.Level * 0.01f;

        void Request(Node n)
        {
            if (n.Job != null || n.Ready) return;
            var job = new Job { Node = n, Priority = Priority(n) };
            n.Job = job;
            pending.Add(job);
        }

        void KillNode(Node n)
        {
            n.Dead = true;
            if (n.Kids != null) for (int k = 0; k < 4; k++) KillNode(n.Kids[k]);
            n.Kids = null;
            if (n.Job != null) pending.Remove(n.Job);
            n.Job = null;
            Kill(n.Go); Kill(n.DecorGo); Kill(n.Mesh); Kill(n.DecorMesh); Kill(n.PropGo); Kill(n.PropMesh);
            n.PropGo = null;
            n.PropMesh = null;
            n.Go = n.DecorGo = null;
            n.Mesh = n.DecorMesh = null;
            n.Renderer = null;
        }

        // ---------------------------------------------------------------- jobs

        ChunkData RunJob(Job job, PlanetTerrain terrain)
        {
            if (job.Tile != null)
                return PlanetMesh.BuildChunk(terrain, job.Tile.Face, MaxLevel, job.Tile.X, job.Tile.Y, chunkN, true);
            var n = job.Node;
            var data = PlanetMesh.BuildChunk(terrain, n.Face, n.Level, n.X, n.Y, chunkN, false);
            if (n.Level == smallDecorLevel || n.Level == largeDecorLevel)
                PlanetDecor.Generate(terrain, data, n.Level == smallDecorLevel, n.Level == largeDecorLevel, props);
            return data;
        }

        void StartJobs()
        {
            if (pending.Count == 0) return;
            if (maxRunning <= 0)
            {
                // No threads (WebGL): a couple of synchronous builds per frame.
                for (int i = 0; i < 2 && pending.Count > 0; i++)
                {
                    var job = PopBest();
                    job.Result = RunJob(job, Terrain);
                    Complete(job);
                }
                return;
            }
            var terrain = Terrain;
            while (running.Count < maxRunning && pending.Count > 0)
            {
                var job = PopBest();
                job.Task = Task.Run(() => RunJob(job, terrain));
                running.Add(job);
            }
        }

        Job PopBest()
        {
            int best = 0;
            for (int i = 1; i < pending.Count; i++) if (pending[i].Priority < pending[best].Priority) best = i;
            var job = pending[best];
            pending[best] = pending[pending.Count - 1];
            pending.RemoveAt(pending.Count - 1);
            return job;
        }

        void PollJobs()
        {
            var sw = System.Diagnostics.Stopwatch.StartNew();
            int uploads = 0;
            for (int i = running.Count - 1; i >= 0; i--)
            {
                var job = running[i];
                if (!job.Task.IsCompleted) continue;
                if (uploads >= MaxUploadsPerFrame || sw.Elapsed.TotalMilliseconds > UploadBudgetMs) break;
                running.RemoveAt(i);
                if (job.Task.IsFaulted)
                {
                    Debug.LogException(job.Task.Exception);
                    if (job.Node != null) job.Node.Job = null;
                    if (job.Tile != null) job.Tile.Job = null;
                    continue;
                }
                job.Result = job.Task.Result;
                Complete(job);
                uploads++;
            }
        }

        void Complete(Job job)
        {
            if (job.Tile != null)
            {
                var t = job.Tile;
                t.Job = null;
                if (!tiles.TryGetValue(t.Key, out var live) || live != t) return;
                CompleteTile(t, job.Result);
                return;
            }
            var n = job.Node;
            if (n.Dead || n.Job != job) return;
            n.Job = null;
            CompleteNode(n, job.Result);
        }

        void CompleteNode(Node n, ChunkData d)
        {
            n.Mesh = PlanetMesh.UploadChunk(d, $"Chunk_{n.Face}_{n.Level}_{n.X}_{n.Y}");
            n.Go = new GameObject("Chunk") { hideFlags = HideFlags.DontSave };
            n.Go.transform.SetParent(transform, false);
            n.Go.transform.localPosition = d.Origin;
            n.Go.AddComponent<MeshFilter>().sharedMesh = n.Mesh;
            n.Renderer = n.Go.AddComponent<MeshRenderer>();
            n.Renderer.sharedMaterial = terrainMat;
            n.Renderer.shadowCastingMode = n.Level >= MaxLevel - 2 && MaxLevel >= 3 ? ShadowCastingMode.On : ShadowCastingMode.Off;
            n.Renderer.lightProbeUsage = LightProbeUsage.Off;
            n.Renderer.enabled = false;
            n.Center = d.Origin + d.Bounds.center;
            n.Bound = d.Bounds.extents.magnitude;

            if (d.Decor != null && d.Decor.VertexCount > 0)
            {
                n.DecorMesh = d.Decor.Build("Decor");
                n.DecorMesh.hideFlags = HideFlags.DontSave;
                n.DecorGo = new GameObject("Decor") { hideFlags = HideFlags.DontSave };
                n.DecorGo.transform.SetParent(n.Go.transform, false);
                n.DecorGo.AddComponent<MeshFilter>().sharedMesh = n.DecorMesh;
                var mr = n.DecorGo.AddComponent<MeshRenderer>();
                mr.sharedMaterial = decorMat;
                mr.lightProbeUsage = LightProbeUsage.Off;
                mr.shadowCastingMode = n.Level == largeDecorLevel ? ShadowCastingMode.On : ShadowCastingMode.Off;
                AddDecorColliders(n.DecorGo, d.DecorColliders);
                // Decor belongs to the node, not to its renderer: it stays while finer children are shown.
                n.DecorGo.transform.SetParent(transform, true);
            }

            // Asset props (#100): one mesh per chunk, a submesh per model material (tinted per planet).
            if (d.DecorProps != null && d.DecorProps.VertexCount > 0)
            {
                n.PropMesh = d.DecorProps.Build("DecorProps", PropMaterial, out var mats);
                n.PropGo = new GameObject("DecorProps") { hideFlags = HideFlags.DontSave };
                n.PropGo.transform.SetParent(n.Go.transform, false);
                n.PropGo.AddComponent<MeshFilter>().sharedMesh = n.PropMesh;
                var mr = n.PropGo.AddComponent<MeshRenderer>();
                mr.sharedMaterials = mats;
                mr.lightProbeUsage = LightProbeUsage.Off;
                mr.shadowCastingMode = n.Level == largeDecorLevel ? ShadowCastingMode.On : ShadowCastingMode.Off;
                if (n.DecorGo == null) AddDecorColliders(n.PropGo, d.DecorColliders);
                n.PropGo.transform.SetParent(transform, true);
            }
            else if (n.DecorGo == null && d.DecorColliders != null)
            {
                n.PropGo = new GameObject("DecorColliders") { hideFlags = HideFlags.DontSave };
                n.PropGo.transform.SetParent(n.Go.transform, false);
                AddDecorColliders(n.PropGo, d.DecorColliders);
                n.PropGo.transform.SetParent(transform, true);
            }
        }

        static void AddDecorColliders(GameObject go, List<Vector4> colliders)
        {
            if (colliders == null) return;
            foreach (var c in colliders)
            {
                var sc = go.AddComponent<SphereCollider>();
                sc.center = c;
                sc.radius = c.w;
            }
        }

        /// <summary>
        /// The material of a prop model's submesh for this planet: rocks take the planet's rock colour, crystals and
        /// ice its flora colour (multiplied onto the texture, so the scans keep their detail); plants keep theirs.
        /// </summary>
        public Material PropMaterial(int model, int sub)
        {
            if (props == null || model < 0 || model >= props.Models.Count) return decorMat;
            var m = props.Models[model];
            var src = m.Materials != null && sub < m.Materials.Length ? m.Materials[sub] : null;
            if (src == null) return decorMat;
            string family = PlanetPropLibrary.Family(m.Id);
            int colorIndex = family == "Rock" || family == "Basalt" || family == "DesertRock" ? 0 : family == "Crystal" || family == "Ice" ? 1 : -1;
            if (colorIndex < 0) return src;
            if (propMats.TryGetValue((src, family), out var mat) && mat != null) return mat;
            var cols = Terrain.Shape.DecorColors;
            Color c = cols != null && cols.Length > colorIndex ? Terrain.Shift(cols[colorIndex]) : Color.gray;
            // the scans are mid grey (~0.45): divide it out, so the prop ends up near the palette colour
            // the sandstone scan is already warm: only a light pull towards the palette
            float strength = family == "Rock" || family == "Basalt" ? 0.85f : family == "Crystal" ? 0.8f : family == "DesertRock" ? 0.4f : 0.5f;
            Color tint = Color.Lerp(Color.white, new Color(Mathf.Min(c.r / 0.45f, 1.6f), Mathf.Min(c.g / 0.45f, 1.6f), Mathf.Min(c.b / 0.45f, 1.6f)), strength);
            mat = new Material(src) { name = src.name + " (" + Terrain.Shape.name + ")", hideFlags = HideFlags.DontSave };
            mat.SetColor("_BaseColor", tint);
            propMats[(src, family)] = mat;
            return mat;
        }

        // ---------------------------------------------------------------- collider streaming

        void UpdateColliders()
        {
            nextColliderUpdate = Time.unscaledTime + 0.15f;
            float now = Time.unscaledTime;
            wantedScratch.Clear();
            foreach (var a in TerrainAnchor.All) if (a != null) WantAround(a.transform.position, a.Velocity, a.Radius, true);
            foreach (var t in ExtraAnchors) if (t != null) WantAround(t.position, Vector3.zero, 40f, true);

            foreach (var key in wantedScratch)
            {
                if (!tiles.TryGetValue(key, out var tile))
                {
                    tile = new Tile { Key = key, Face = (int)(key >> 58), X = (int)((key >> 26) & 0x3FFFFFF), Y = (int)(key & 0x3FFFFFF) };
                    tiles[key] = tile;
                }
                tile.LastWanted = now;
                if (!tile.Ready && tile.Job == null)
                {
                    tile.Job = new Job { Tile = tile, Priority = -10f };
                    pending.Add(tile.Job);
                }
            }

            tileScratch.Clear();
            foreach (var kv in tiles) if (now - kv.Value.LastWanted > 4f) tileScratch.Add(kv.Key);
            foreach (var k in tileScratch)
            {
                var t = tiles[k];
                if (t.Job != null) pending.Remove(t.Job);
                KillTile(t);
                tiles.Remove(k);
            }

            // Anything about to touch ground without a collider gets it built right now (no tunnelling).
            foreach (var a in TerrainAnchor.All) if (a != null) EnsureUnder(a.transform.position, a.Velocity);
            foreach (var t in ExtraAnchors) if (t != null) EnsureUnder(t.position, Vector3.zero);
        }

        void WantAround(Vector3 world, Vector3 velocity, float baseRadius, bool lookAhead)
        {
            Vector3 local = transform.InverseTransformPoint(world);
            float dist = local.magnitude;
            if (dist < 1f) return;
            float speed = velocity.magnitude;
            float reach = baseRadius + speed * 1.5f;
            if (dist > Terrain.MaxRadius + reach) return;
            Vector3 dir = local / dist;
            float altitude = dist - Terrain.SurfaceRadius(dir);
            if (altitude > reach) return;
            float r = Mathf.Max(30f, reach - Mathf.Max(0f, altitude) * 0.5f);
            WantDisc(dir, r);
            if (lookAhead && speed > 5f)
            {
                Vector3 ahead = transform.InverseTransformPoint(world + velocity * 1.2f);
                if (ahead.sqrMagnitude > 1f) WantDisc(ahead.normalized, Mathf.Max(30f, r * 0.6f));
            }
        }

        void WantDisc(Vector3 dir, float radiusM)
        {
            float leaf = faceArc / (1 << MaxLevel);
            float spacing = leaf * 0.5f;
            int k = Mathf.CeilToInt(radiusM / spacing);
            Vector3 t1 = Vector3.Cross(dir, Mathf.Abs(dir.y) < 0.9f ? Vector3.up : Vector3.right).normalized;
            Vector3 t2 = Vector3.Cross(dir, t1);
            float step = spacing / radius;
            for (int j = -k; j <= k; j++)
                for (int i = -k; i <= k; i++)
                {
                    if ((i * i + j * j) * spacing * spacing > radiusM * radiusM + spacing * spacing) continue;
                    Vector3 d = (dir + (t1 * i + t2 * j) * step).normalized;
                    PlanetMesh.DirToChunk(d, MaxLevel, out int f, out int x, out int y);
                    wantedScratch.Add(PlanetMesh.Key(f, MaxLevel, x, y));
                }
        }

        void EnsureUnder(Vector3 world, Vector3 velocity)
        {
            Vector3 local = transform.InverseTransformPoint(world);
            float dist = local.magnitude;
            if (dist < 1f || dist > Terrain.MaxRadius + 60f) return;
            Vector3 dir = local / dist;
            float altitude = dist - Terrain.SurfaceRadius(dir);
            if (altitude > 8f + velocity.magnitude * 0.3f) return;
            PlanetMesh.DirToChunk(dir, MaxLevel, out int f, out int x, out int y);
            long key = PlanetMesh.Key(f, MaxLevel, x, y);
            if (!tiles.TryGetValue(key, out var tile))
            {
                tile = new Tile { Key = key, Face = f, X = x, Y = y, LastWanted = Time.unscaledTime };
                tiles[key] = tile;
            }
            if (tile.Ready) return;
            if (tile.Job != null) { pending.Remove(tile.Job); tile.Job = null; }
            CompleteTile(tile, PlanetMesh.BuildChunk(Terrain, f, MaxLevel, x, y, chunkN, true));
        }

        void CompleteTile(Tile t, ChunkData d)
        {
            if (t.Ready) return;
            t.Mesh = PlanetMesh.UploadChunk(d, "TerrainCollider");
            t.Go = new GameObject("TerrainCollider") { hideFlags = HideFlags.DontSave };
            t.Go.transform.SetParent(transform, false);
            t.Go.transform.localPosition = d.Origin;
            var mc = t.Go.AddComponent<MeshCollider>();
            mc.cookingOptions = MeshColliderCookingOptions.CookForFasterSimulation | MeshColliderCookingOptions.EnableMeshCleaning | MeshColliderCookingOptions.WeldColocatedVertices | MeshColliderCookingOptions.UseFastMidphase;
            mc.sharedMesh = t.Mesh;
        }

        static void KillTile(Tile t)
        {
            Kill(t.Go); Kill(t.Mesh);
            t.Go = null; t.Mesh = null;
        }

        /// <summary>Forces collider tiles around a world position to exist now (server spawn checks, teleports).</summary>
        public void EnsureColliders(Vector3 world, float radiusM)
        {
            if (Terrain == null) return;
            wantedScratch.Clear();
            Vector3 local = transform.InverseTransformPoint(world);
            if (local.sqrMagnitude < 1f) return;
            WantDisc(local.normalized, radiusM);
            foreach (var key in wantedScratch)
            {
                if (!tiles.TryGetValue(key, out var tile))
                {
                    tile = new Tile { Key = key, Face = (int)(key >> 58), X = (int)((key >> 26) & 0x3FFFFFF), Y = (int)(key & 0x3FFFFFF) };
                    tiles[key] = tile;
                }
                tile.LastWanted = Time.unscaledTime;
                if (tile.Ready) continue;
                if (tile.Job != null) { pending.Remove(tile.Job); tile.Job = null; }
                CompleteTile(tile, PlanetMesh.BuildChunk(Terrain, tile.Face, MaxLevel, tile.X, tile.Y, chunkN, true));
            }
        }
    }
}
