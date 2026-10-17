using UnityEngine;

namespace Voidward.Ship
{
    /// <summary>
    /// Derelict look of a ship (#97): while ShipCore.Derelict is set, the hull renderers show their
    /// damaged material (dark, scorched, no emission) and the interior lights fall back to a dim red emergency glow
    /// that flickers now and then. Client-local, driven by the synced flag; restoring power switches everything back.
    /// </summary>
    public class ShipDerelictLook : MonoBehaviour
    {
        public Renderer[] Renderers = new Renderer[0];
        public Material[] Derelict = new Material[0];   // one per renderer (null = keep)
        public Light[] Lights = new Light[0];

        static readonly Color Emergency = new Color(1f, 0.22f, 0.12f);

        ShipCore core;
        Material[] normal;
        Color[] lightColor;
        float[] lightIntensity;
        bool? shown;
        float flicker;

        void Awake()
        {
            core = GetComponent<ShipCore>();
            normal = new Material[Renderers.Length];
            for (int i = 0; i < Renderers.Length; i++) normal[i] = Renderers[i] != null ? Renderers[i].sharedMaterial : null;
            lightColor = new Color[Lights.Length];
            lightIntensity = new float[Lights.Length];
            for (int i = 0; i < Lights.Length; i++)
            {
                if (Lights[i] == null) continue;
                lightColor[i] = Lights[i].color;
                lightIntensity[i] = Lights[i].intensity;
            }
        }

        void Update()
        {
            bool derelict = core != null && core.IsSpawned && core.Derelict.Value;
            if (shown != derelict)
            {
                shown = derelict;
                for (int i = 0; i < Renderers.Length; i++)
                    if (Renderers[i] != null && i < Derelict.Length && Derelict[i] != null)
                        Renderers[i].sharedMaterial = derelict ? Derelict[i] : normal[i];
                for (int i = 0; i < Lights.Length; i++)
                {
                    if (Lights[i] == null) continue;
                    Lights[i].color = derelict ? Emergency : lightColor[i];
                    Lights[i].intensity = derelict ? lightIntensity[i] * 0.3f : lightIntensity[i];
                }
            }
            if (!derelict) return;
            // a dying power bus: now and then one light stutters
            flicker -= Time.deltaTime;
            if (flicker > 0f || Lights.Length == 0) return;
            flicker = Random.Range(0.05f, 0.6f);
            var l = Lights[Random.Range(0, Lights.Length)];
            int k = System.Array.IndexOf(Lights, l);
            if (l != null) l.intensity = lightIntensity[k] * (Random.value < 0.35f ? 0.04f : 0.3f);
        }
    }
}
