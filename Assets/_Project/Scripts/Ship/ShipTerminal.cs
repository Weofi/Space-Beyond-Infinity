using UnityEngine;
using Voidward.Core;

namespace Voidward.Ship
{
    /// <summary>Interactable ship console: opens the ship (cargo/modules/services), fabricator or warp drive panel.</summary>
    public class ShipTerminal : MonoBehaviour, IInteractable
    {
        public enum Mode { ShipPanel, Fabricator, WarpDrive }
        public Mode TerminalMode = Mode.ShipPanel;

        ShipCore ship;
        void Awake() => ship = GetComponentInParent<ShipCore>();

        public string Prompt => ship != null && ship.Derelict.Value
            ? $"Restore derelict power ({ShipCore.DerelictRepairCost} Scrap)"
            : TerminalMode == Mode.Fabricator ? "Use fabricator"
            : TerminalMode == Mode.WarpDrive ? "Warp drive: parts & Exotic Matter" : "Ship cargo & modules";

        public bool CanInteract(IInteractAgent agent) => ship != null && ship.IsSpawned;

        public void Interact(IInteractAgent agent)
        {
            if (ship.Derelict.Value) { ship.ClaimServerRpc(); return; }
            if (TerminalMode == Mode.Fabricator) EventBus.RaiseCraftPanelRequested();
            else if (TerminalMode == Mode.WarpDrive) EventBus.RaiseDrivePanelRequested();
            else EventBus.RaiseShipPanelRequested(ship.NetworkObjectId);
        }
    }
}
