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
        public bool Killed;            // set by the server when this hit was the killing blow

        public void NetworkSerialize<T>(BufferSerializer<T> s) where T : IReaderWriter
        {
            s.SerializeValue(ref Amount);
            s.SerializeValue(ref Type);
            s.SerializeValue(ref Point);
            s.SerializeValue(ref Direction);
            s.SerializeValue(ref SourceClientId);
            s.SerializeValue(ref IsWeakPoint);
            s.SerializeValue(ref ByPlayer);
            s.SerializeValue(ref Killed);
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
    /// Optional on hittable objects (ships, stations, planets, asteroids): which impact effect a shot gets.
    /// Looked up with GetComponentInParent; objects without one fall back to their team (see FeedbackFx.SurfaceOf).
    /// The component's transform also anchors scorch decals, so they ride along with moving ships.
    /// </summary>
    public interface ISurface
    {
        SurfaceKind Surface { get; }
        Transform transform { get; }
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
        /// <summary>1-based galaxy of the journey home (1 = M33 ... 5 = LMC).</summary>
        public static int GalaxyIndex = 1;
        /// <summary>0-based star system inside the current galaxy.</summary>
        public static int SystemIndex;
        /// <summary>Camera shake offset applied by the VFX service to the local view.</summary>
        public static float ShakeIntensity;
        /// <summary>Local camera impulse: FOV offset in degrees (negative = punch in), decays back to 0.</summary>
        public static float FovPunch;
        /// <summary>Local, purely visual hit-stop: camera and effects hold until this unscaled time (never Time.timeScale).</summary>
        public static float HitStopUntil;
        public static bool HitStopActive => Time.unscaledTime < HitStopUntil;
        /// <summary>Local weapon recoil 0..~1.5 (viewmodel / seat camera kick), decays back to 0. Already scaled by Screen shake.</summary>
        public static float Recoil;
        public static string Objective = "";
        /// <summary>Home ETA of the crew's warp drive as display text (#78), "" outside a run.</summary>
        public static string HomeEta = "";
        public static float FieldOfView = 75f;
        /// <summary>The intro camera sequence owns the view; gameplay input is suppressed.</summary>
        public static bool Cinematic;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void Reset() { UIOpen = false; Phase = GamePhase.MainMenu; GalaxyIndex = 1; SystemIndex = 0; ShakeIntensity = 0f; FovPunch = 0f; HitStopUntil = 0f; Recoil = 0f; Objective = ""; HomeEta = ""; Cinematic = false; }

        public static bool GameplayInputAllowed => !UIOpen && !Cinematic && Phase == GamePhase.Playing;
    }
}
