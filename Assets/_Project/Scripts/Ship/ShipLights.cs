using UnityEngine;
using Voidward.Core;

namespace Voidward.Ship
{
    /// <summary>Headlights fade in while someone sits in the pilot seat (client-local, driven by the synced seat occupant).</summary>
    public class ShipLights : MonoBehaviour
    {
        public Light[] Headlights = new Light[0];

        ShipSeat pilot;
        float[] baseIntensity;
        float level = -1f;

        void Awake()
        {
            foreach (var s in GetComponentsInChildren<ShipSeat>(true))
                if (s.SeatRole == StationRole.Pilot) { pilot = s; break; }
            baseIntensity = new float[Headlights.Length];
            for (int i = 0; i < Headlights.Length; i++) baseIntensity[i] = Headlights[i] != null ? Headlights[i].intensity : 0f;
        }

        void Update()
        {
            float target = pilot != null && pilot.IsSpawned && pilot.IsOccupied ? 1f : 0f;
            if (Mathf.Approximately(level, target)) return;
            level = level < 0f ? target : Mathf.MoveTowards(level, target, Time.deltaTime * 2.5f);
            for (int i = 0; i < Headlights.Length; i++)
            {
                if (Headlights[i] == null) continue;
                Headlights[i].intensity = baseIntensity[i] * level;
                Headlights[i].enabled = level > 0.001f;
            }
        }
    }
}
