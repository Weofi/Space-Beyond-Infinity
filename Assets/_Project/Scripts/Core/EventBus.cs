using System;
using UnityEngine;

namespace Voidward.Core
{
    /// <summary>
    /// Global, typed event hub with primitive payloads only (so Core never depends on feature
    /// assemblies). Subscribers must unsubscribe in OnDisable/OnDestroy.
    /// </summary>
    public static class EventBus
    {
        // ---- Game flow ----
        public static event Action<GamePhase> PhaseChanged;
        public static event Action<RunSummary> RunEnded;
        public static event Action<int, int> SystemEntered;   // 1-based galaxy, 0-based system
        public static event Action<int> WarpCompleted;        // 1-based galaxy after the warp
        public static event Action BossSpawned;
        public static event Action BossDefeated;

        // ---- Local player ----
        public static event Action<GameObject> LocalPawnSpawned;   // the local player's pawn GameObject
        public static event Action LocalPawnDespawned;
        public static event Action LocalPlayerBoardedShip;         // entered any ship interior or seat
        public static event Action LocalPlayerLeftShip;

        // ---- HUD / feedback ----
        public static event Action<string> Notification;           // toast message, UI listens
        public static event Action<float> LocalPlayerHurt;         // 0..1 damage intensity for vignette
        public static event Action<bool> LowOxygenChanged;
        public static event Action<Vector3, PingKind, string> PingCreated;  // world-space ping markers (all clients)
        /// <summary>Local player wants to create a ping — the networking layer forwards it to the server.</summary>
        public static event Action<Vector3, PingKind, string> PingRequested;

        // ---- Panel open requests (raised by interactables, served by UI) ----
        public static event Action<ulong> TradePanelRequested;     // station NetworkObjectId
        public static event Action CraftPanelRequested;
        public static event Action DrivePanelRequested;              // crew warp drive tab (#78)
        public static event Action<ulong> ShipPanelRequested;      // ship NetworkObjectId (cargo/modules)
        public static event Action<ulong> EngineerPanelRequested;  // ship NetworkObjectId (power/repair)

        public static void RaisePhaseChanged(GamePhase p) => PhaseChanged?.Invoke(p);
        public static void RaiseRunEnded(RunSummary s) => RunEnded?.Invoke(s);
        public static void RaiseSystemEntered(int galaxy, int system) => SystemEntered?.Invoke(galaxy, system);
        public static void RaiseWarpCompleted(int galaxy) => WarpCompleted?.Invoke(galaxy);
        public static void RaiseBossSpawned() => BossSpawned?.Invoke();
        public static void RaiseBossDefeated() => BossDefeated?.Invoke();
        public static void RaiseLocalPawnSpawned(GameObject go) => LocalPawnSpawned?.Invoke(go);
        public static void RaiseLocalPawnDespawned() => LocalPawnDespawned?.Invoke();
        public static void RaiseLocalPlayerBoardedShip() => LocalPlayerBoardedShip?.Invoke();
        public static void RaiseLocalPlayerLeftShip() => LocalPlayerLeftShip?.Invoke();
        public static void RaiseNotification(string msg) => Notification?.Invoke(msg);
        public static void RaiseLocalPlayerHurt(float intensity) => LocalPlayerHurt?.Invoke(intensity);
        public static void RaiseLowOxygenChanged(bool low) => LowOxygenChanged?.Invoke(low);
        public static void RaisePingCreated(Vector3 pos, PingKind kind, string label) => PingCreated?.Invoke(pos, kind, label);
        public static void RaisePingRequested(Vector3 pos, PingKind kind, string label) => PingRequested?.Invoke(pos, kind, label);
        public static void RaiseTradePanelRequested(ulong stationId) => TradePanelRequested?.Invoke(stationId);
        public static void RaiseCraftPanelRequested() => CraftPanelRequested?.Invoke();
        public static void RaiseDrivePanelRequested() => DrivePanelRequested?.Invoke();
        public static void RaiseShipPanelRequested(ulong shipId) => ShipPanelRequested?.Invoke(shipId);
        public static void RaiseEngineerPanelRequested(ulong shipId) => EngineerPanelRequested?.Invoke(shipId);
    }
}
