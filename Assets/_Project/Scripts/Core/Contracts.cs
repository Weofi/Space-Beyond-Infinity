using Unity.Netcode;
using UnityEngine;

namespace Voidward.Core
{
    /// <summary>One damage application. Always processed on the server.</summary>
    public struct DamageInfo : INetworkSerializable
    {
        public float Amount;
        public DamageType Type;
        public Vector3 Point;
        public Vector3 Direction;
        public ulong SourceClientId;   // 0 = world/AI
        public bool IsWeakPoint;       // damage multipliers may apply
        public bool ByPlayer;          // SourceClientId is only meaningful when true

        public void NetworkSerialize<T>(BufferSerializer<T> s) where T : IReaderWriter
        {
            s.SerializeValue(ref Amount);
            s.SerializeValue(ref Type);
            s.SerializeValue(ref Point);
            s.SerializeValue(ref Direction);
            s.SerializeValue(ref SourceClientId);
            s.SerializeValue(ref IsWeakPoint);
            s.SerializeValue(ref ByPlayer);
        }
    }

    /// <summary>Anything with health that can take damage. Implemented by HealthComponent.</summary>
    public interface IDamageable
    {
        Team Team { get; }
        bool IsAlive { get; }
        /// <summary>Server-only entry point (call via ServerRpc from clients).</summary>
        void ApplyDamage(DamageInfo info);
        GameObject gameObject { get; }
    }

    /// <summary>
    /// The agent performing an interaction (the player pawn). Defined in Core so
    /// interactables never depend on the Player assembly.
    /// </summary>
    public interface IInteractAgent
    {
        GameObject GameObject { get; }
        Vector3 Position { get; }
        ulong OwnerClientId { get; }
    }

    /// <summary>
    /// Anything the player can interact with (F key). Interact() is invoked locally;
    /// networked objects forward to the server via ServerRpc themselves.
    /// </summary>
    public interface IInteractable
    {
        string Prompt { get; }
        bool CanInteract(IInteractAgent agent);
        void Interact(IInteractAgent agent);
    }

    /// <summary>Implemented by things that can seat a player (ship stations).</summary>
    public interface ISeat
    {
        StationRole Role { get; }
        bool IsOccupied { get; }
        GameObject GameObject { get; }
        /// <summary>Where the seated body sits.</summary>
        Transform SeatAnchor { get; }
        /// <summary>Camera pose while seated (turrets rotate this).</summary>
        Transform ViewPoint { get; }
        /// <summary>Called every frame on the occupant's client while seated (flight/turret input).</summary>
        void TickOccupant(float dt);
        /// <summary>Local occupant asks to leave.</summary>
        void RequestLeave();
    }

    /// <summary>Local-client UI/game state flags shared across assemblies.</summary>
    public static class GameState
    {
        /// <summary>A modal UI panel is open — gameplay input (look/fire/interact) is suppressed.</summary>
        public static bool UIOpen;
        public static GamePhase Phase = GamePhase.MainMenu;
        public static int SectorIndex = 1;
        /// <summary>Camera shake offset applied by the VFX service to the local view.</summary>
        public static float ShakeIntensity;
        /// <summary>World X of the Collapse front (everything with x below is consumed).</summary>
        public static float CollapseX = -99999f;
        public static Vector3 GatePosition;
        public static bool HasGate;
        public static string Objective = "";
        public static float FieldOfView = 75f;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void Reset() { UIOpen = false; Phase = GamePhase.MainMenu; SectorIndex = 1; ShakeIntensity = 0f; CollapseX = -99999f; HasGate = false; Objective = ""; }

        public static bool GameplayInputAllowed => !UIOpen && Phase == GamePhase.Playing;
    }
}
