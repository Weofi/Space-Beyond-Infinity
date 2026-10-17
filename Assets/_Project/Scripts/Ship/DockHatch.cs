using UnityEngine;
using Voidward.Core;

namespace Voidward.Ship
{
    /// <summary>Hatch inside the home ship up to a docking clamp (#49): [F] boards the ship docked there (its pilot seat).</summary>
    public class DockHatch : MonoBehaviour, IInteractable
    {
        public DockClamp Clamp;

        public string Prompt
        {
            get
            {
                var s = Clamp != null ? Clamp.Docked : null;
                if (s == null) return $"Docking clamp {Clamp.Index + 1} (empty)";
                var seat = PilotSeat(s);
                return seat != null && seat.IsOccupied ? $"{s.ShipName.Value} (occupied)" : $"Board {s.ShipName.Value}";
            }
        }

        public bool CanInteract(IInteractAgent agent)
        {
            var s = Clamp != null ? Clamp.Docked : null;
            var seat = s != null ? PilotSeat(s) : null;
            return seat != null && !seat.IsOccupied;
        }

        public void Interact(IInteractAgent agent)
        {
            var s = Clamp != null ? Clamp.Docked : null;
            var seat = s != null ? PilotSeat(s) : null;
            if (seat != null) seat.Interact(agent);
        }

        static ShipSeat PilotSeat(ShipCore s)
        {
            foreach (var seat in ShipSeat.All) if (seat != null && seat.Ship == s && seat.Role == StationRole.Pilot) return seat;
            return null;
        }
    }
}
