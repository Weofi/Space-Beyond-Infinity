using UnityEngine;
using Voidward.Core;

namespace Voidward.Ship
{
    /// <summary>
    /// Cryo pod in the home ship's cryo bay (#49): a fallen crew member who is not revived wakes up here (one pod per
    /// player slot, costs a cryo charge, RunManager). Wake() = the client effect: frost vapour and the hiss of the lid.
    /// </summary>
    public class CryoPod : MonoBehaviour
    {
        public int Index;
        [Tooltip("Where the woken crew member stands, facing out of the pod.")]
        public Transform Exit;
        [Tooltip("Pod light, flares up while it wakes someone.")]
        public Light Glow;

        float flare;

        /// <summary>Every client: someone wakes up in this pod.</summary>
        public void Wake()
        {
            var at = Exit != null ? Exit.position : transform.position;
            Vfx.LootSparkle(at + transform.up * 1.1f, new Color(0.75f, 0.92f, 1f), 1.6f, 1);
            Sfx.Play("atmo_entry", at, 0.7f, 1.6f);
            flare = 1f;
        }

        void Update()
        {
            if (Glow == null || flare <= 0f) return;
            flare = Mathf.Max(0f, flare - Time.deltaTime * 0.5f);
            Glow.intensity = 1.2f + flare * 6f;
        }
    }
}
