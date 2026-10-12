using Unity.Netcode;
using UnityEngine;
using Voidward.Core;

namespace Voidward.Planet
{
    /// <summary>
    /// Trigger volume that fires atmosphere-entry VFX/SFX when the local player pawn or ship enters.
    /// 3-second re-entry cooldown prevents repeated triggers.
    /// </summary>
    public class AtmosphereTrigger : MonoBehaviour
    {
        /// <summary>The planet this atmosphere belongs to.</summary>
        public PlanetBody Planet;

        float _lastEntryTime = -10f;

        void OnTriggerEnter(Collider other)
        {
            if (Planet == null || !Planet.HasAtmosphere) return;

            // Skip objects that are part of this planet (scatter, atmosphere visuals).
            if (other.transform.IsChildOf(transform.root)) return;

            // Check for the local player pawn or its ship: any NetworkBehaviour in the
            // parent chain with IsOwner.
            if (!IsLocalOwned(other.transform)) return;

            // 3-second re-entry cooldown.
            if (Time.time - _lastEntryTime < 3f) return;
            _lastEntryTime = Time.time;

            Vfx.AtmosphereEntry();
            Sfx.Play("atmo_entry", other.transform.position);
            Vfx.Shake(0.4f);
        }

        static bool IsLocalOwned(Transform t)
        {
            Transform current = t;
            while (current != null)
            {
                var behaviours = current.GetComponents<NetworkBehaviour>();
                for (int i = 0; i < behaviours.Length; i++)
                {
                    if (behaviours[i].IsOwner)
                        return true;
                }
                current = current.parent;
            }
            return false;
        }
    }
}
