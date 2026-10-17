using UnityEngine;

namespace Voidward.Ship
{
    /// <summary>
    /// A docking clamp on the home ship (#49): a small ship docked here rides along at this transform (its root on the
    /// clamp's pose). Darts dock for every warp flight (WarpFlight) and whenever a pilot eases into a free clamp; holding
    /// thrust launches them again. Hatch = where the pilot steps out inside the home ship (and boards from, DockHatch).
    /// </summary>
    public class DockClamp : MonoBehaviour
    {
        public int Index;
        [Tooltip("Inside the home ship: the pilot of the docked ship leaves the seat here.")]
        public Transform Hatch;

        /// <summary>Distance (m) between a ship's root and a free clamp at which the clamp grabs it.</summary>
        public const float CaptureRange = 16f;
        /// <summary>Highest speed (m/s) relative to the home ship at which a clamp grabs a ship.</summary>
        public const float CaptureSpeed = 14f;

        public ShipCore Home { get; private set; }

        void Awake() => Home = GetComponentInParent<ShipCore>();

        /// <summary>The ship docked at this clamp (any peer), or null.</summary>
        public ShipCore Docked
        {
            get
            {
                if (Home == null) return null;
                foreach (var s in ShipCore.All)
                    if (s != null && s.IsSpawned && s.DockedTo.Value == Home.NetworkObjectId && s.DockIndex.Value == Index) return s;
                return null;
            }
        }
    }
}
