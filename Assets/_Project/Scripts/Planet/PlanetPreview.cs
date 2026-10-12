using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;
using Voidward.Data;

namespace Voidward.Planet
{
    /// <summary>
    /// Developer preview for procedural planets (scene Scenes/PlanetPreview, enter Play mode).
    /// Orbit/zoom down to the surface, cycle presets and seeds, drop physics probes onto the collider.
    /// Changes to the inspector or to the selected PlanetShapeDef asset regenerate automatically.
    /// </summary>
    public class PlanetPreview : MonoBehaviour
    {
        public PlanetShapeDef[] Presets;
        public int PresetIndex;
        public int Seed = 1234;
        public float Radius = 200f;
        [Tooltip("0 = use the preset's resolution.")]
        [Range(0, 160)] public int ResolutionOverride;
        public bool AutoRotate = true;
        public float RotateSpeed = 3f;
        public Camera Cam;

        PlanetVisuals visuals;
        float yaw = 30f, pitch = 15f, altitude = 400f;
        int builtHash;
        float nextHashCheck;
        bool dirty = true;
        readonly List<Rigidbody> probes = new List<Rigidbody>();

        public PlanetShapeDef Current => Presets != null && Presets.Length > 0 ? Presets[Mathf.Clamp(PresetIndex, 0, Presets.Length - 1)] : null;
        public PlanetTerrain Terrain => visuals != null ? visuals.Terrain : null;

        void OnValidate() => dirty = true;

        void Start()
        {
            var go = new GameObject("Planet");
            go.transform.SetParent(transform, false);
            visuals = go.AddComponent<PlanetVisuals>();
            altitude = Radius * 1.4f;
        }

        public void Regenerate() => dirty = true;

        public void RandomSeed()
        {
            Seed = Random.Range(1, 999999);
            dirty = true;
        }

        public void SelectPreset(int index)
        {
            if (Presets == null || Presets.Length == 0) return;
            PresetIndex = (index % Presets.Length + Presets.Length) % Presets.Length;
            dirty = true;
        }

        void Update()
        {
            HandleInput();

            // Poll the shape asset so edits in its inspector show up live.
            if (Time.unscaledTime >= nextHashCheck && Current != null)
            {
                nextHashCheck = Time.unscaledTime + 0.3f;
                if (JsonUtility.ToJson(Current).GetHashCode() != builtHash) dirty = true;
            }
            if (dirty && Current != null) Build();

            if (AutoRotate && visuals != null) visuals.transform.Rotate(0f, RotateSpeed * Time.deltaTime, 0f, Space.Self);
            UpdateCamera();
        }

        void Build()
        {
            dirty = false;
            builtHash = JsonUtility.ToJson(Current).GetHashCode();
            ClearProbes();
            visuals.Build(Current, Seed, Radius, true, ResolutionOverride);
        }

        void HandleInput()
        {
            var kb = Keyboard.current;
            var mouse = Mouse.current;
            if (kb != null)
            {
                if (kb.tabKey.wasPressedThisFrame) SelectPreset(PresetIndex + (kb.shiftKey.isPressed ? -1 : 1));
                for (int i = 0; i < 9; i++)
                    if (kb[Key.Digit1 + i].wasPressedThisFrame && Presets != null && i < Presets.Length) SelectPreset(i);
                if (kb.rKey.wasPressedThisFrame) RandomSeed();
                if (kb.rightArrowKey.wasPressedThisFrame) { Seed++; dirty = true; }
                if (kb.leftArrowKey.wasPressedThisFrame) { Seed--; dirty = true; }
                if (kb.spaceKey.wasPressedThisFrame) dirty = true;
                if (kb.aKey.wasPressedThisFrame) AutoRotate = !AutoRotate;
                if (kb.bKey.wasPressedThisFrame) DropProbes();
            }
            if (mouse != null)
            {
                if (mouse.leftButton.isPressed || mouse.rightButton.isPressed)
                {
                    Vector2 d = mouse.delta.ReadValue();
                    // Slower orbit near the surface so the ground doesn't whip past.
                    float k = Mathf.Lerp(0.04f, 0.25f, Mathf.Clamp01(altitude / Radius));
                    yaw += d.x * k;
                    pitch = Mathf.Clamp(pitch - d.y * k, -89f, 89f);
                }
                float scroll = mouse.scroll.ReadValue().y;
                if (Mathf.Abs(scroll) > 0.01f) altitude *= Mathf.Pow(0.9f, Mathf.Sign(scroll) * Mathf.Min(3f, Mathf.Abs(scroll) / 40f + 1f));
            }
            altitude = Mathf.Clamp(altitude, 1.5f, Radius * 8f);
        }

        void UpdateCamera()
        {
            if (Cam == null || visuals == null || visuals.Terrain == null) return;
            Vector3 center = visuals.transform.position;
            Quaternion orbit = Quaternion.Euler(pitch, yaw, 0f);
            Vector3 dirWorld = orbit * Vector3.back;
            Vector3 dirLocal = visuals.transform.InverseTransformDirection(dirWorld);
            var t = visuals.Terrain;
            float ground = Mathf.Max(t.SurfaceRadius(dirLocal), t.HasOcean ? t.SeaRadius : 0f);
            Cam.transform.position = center + dirWorld * (ground + altitude);
            // Look at the planet from afar; tilt towards the horizon when close to the surface.
            float close = 1f - Mathf.Clamp01(altitude / (Radius * 0.35f));
            Quaternion down = Quaternion.LookRotation(-dirWorld, orbit * Vector3.up);
            Cam.transform.rotation = down * Quaternion.Euler(-80f * close * close, 0f, 0f);
        }

        void DropProbes()
        {
            if (Cam == null) return;
            for (int i = 0; i < 12; i++)
            {
                var ball = GameObject.CreatePrimitive(PrimitiveType.Sphere);
                ball.name = "Probe";
                ball.transform.position = Cam.transform.position + Cam.transform.forward * 6f + Random.insideUnitSphere * 3f;
                ball.transform.localScale = Vector3.one * Mathf.Lerp(0.6f, 1.6f, Random.value);
                var rb = ball.AddComponent<Rigidbody>();
                rb.useGravity = false;
                probes.Add(rb);
            }
        }

        void FixedUpdate()
        {
            if (visuals == null) return;
            Vector3 c = visuals.transform.position;
            foreach (var rb in probes)
                if (rb != null) rb.AddForce((c - rb.position).normalized * 9.81f, ForceMode.Acceleration);
        }

        void ClearProbes()
        {
            foreach (var rb in probes) if (rb != null) Destroy(rb.gameObject);
            probes.Clear();
        }

        void OnGUI()
        {
            var t = Terrain;
            var s = Current;
            if (t == null || s == null) return;
            string text =
                $"<b>{s.DisplayName}</b>  ({PresetIndex + 1}/{Presets.Length})   seed {Seed}   radius {Radius:0} m\n" +
                $"verts {visuals.NearVertexCount:N0}   build {visuals.BuildMilliseconds:0} ms   LOD {(visuals.NearLodActive ? "near" : "far")}\n" +
                $"relief {t.MinRadius - Radius:0.0} .. {t.MaxRadius - Radius:0.0} m" +
                (t.HasOcean ? $"   sea level {t.SeaRadius - Radius:+0.0;-0.0} m ({(t.SeaRadius / Radius - 1f) * 100f:0.0}% of radius), coverage {t.OceanCoverage * 100f:0}%" : "   no ocean") +
                $"\naltitude {altitude:0.0} m\n\n" +
                "Drag: orbit   Wheel: zoom   Tab/1-9: preset   R: random seed   Left/Right: seed -/+\n" +
                "Space: regenerate   A: auto-rotate   B: drop physics probes";
            var style = new GUIStyle(GUI.skin.label) { richText = true, fontSize = 15 };
            GUI.color = new Color(0f, 0f, 0f, 0.6f);
            GUI.Label(new Rect(13, 13, 900, 200), text.Replace("<b>", "").Replace("</b>", ""), style);
            GUI.color = Color.white;
            GUI.Label(new Rect(12, 12, 900, 200), text, style);
        }
    }
}
