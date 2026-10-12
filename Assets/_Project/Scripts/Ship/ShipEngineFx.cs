using Unity.Netcode;
using UnityEngine;
using Voidward.Core;

namespace Voidward.Ship
{
    /// <summary>Thruster glow + engine audio for the local pilot.</summary>
    public class ShipEngineFx : MonoBehaviour
    {
        public Transform[] Thrusters = new Transform[0];
        ShipCore ship;
        Vector3[] baseScales;
        Vector3 lastPos;
        float smoothed;

        void Awake()
        {
            ship = GetComponentInParent<ShipCore>();
            baseScales = new Vector3[Thrusters.Length];
            for (int i = 0; i < Thrusters.Length; i++) if (Thrusters[i]) baseScales[i] = Thrusters[i].localScale;
        }

        void LateUpdate()
        {
            if (ship == null) return;
            Vector3 v = (transform.position - lastPos) / Mathf.Max(Time.deltaTime, 0.0001f);
            lastPos = transform.position;
            float fwd = Mathf.Clamp01(Vector3.Dot(v, transform.forward) / 60f);
            bool active = ship.IsSpawned && !ship.Derelict.Value && (PilotOccupied() || fwd > 0.05f);
            smoothed = Mathf.Lerp(smoothed, active ? 0.35f + fwd : 0f, 6f * Time.deltaTime);
            for (int i = 0; i < Thrusters.Length; i++)
            {
                if (!Thrusters[i]) continue;
                var b = baseScales[i];
                Thrusters[i].localScale = new Vector3(b.x, b.y, b.z * Mathf.Max(0.05f, smoothed * 1.6f));
                // A collapsed plume reads as a flat disc in the nozzle; hide it when the engine is cold.
                bool lit = smoothed > 0.03f;
                if (Thrusters[i].gameObject.activeSelf != lit) Thrusters[i].gameObject.SetActive(lit);
            }

            var nm = NetworkManager.Singleton;
            if (nm != null && ship.IsSpawned)
            {
                var seat = ShipSeat.SeatOf(nm.LocalClientId);
                if (seat != null && seat.Ship == ship && seat.Role == StationRole.Pilot)
                    Sfx.Engine(true, Mathf.Clamp01(smoothed));
            }
        }

        bool PilotOccupied()
        {
            foreach (var s in ShipSeat.All)
                if (s != null && s.Ship == ship && s.Role == StationRole.Pilot && s.IsOccupied) return true;
            return false;
        }
    }
}
