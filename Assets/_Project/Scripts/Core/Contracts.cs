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

        public void NetworkSerialize<T>(BufferSerializer<T> s) where T : IReaderWriter
        {
            s.SerializeValue(ref Amount);
            s.SerializeValue(ref Type);
            s.SerializeValue(ref Point);
            s.SerializeValue(ref Direction);
            s.SerializeValue(ref SourceClientId);
            s.SerializeValue(ref IsWeakPoint);
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

    /// <summary>A ship weapon mount that can be told to fire (implemented in Combat).</summary>
    public interface IShipWeapon
    {
        string DisplayName { get; }
        float CooldownRemaining { get; }
        /// <summary>Called on the owning client; implementation relays to server.</summary>
        void TryFire(Vector3 direction);
    }

    /// <summary>Implemented by things that can seat a player (ship stations).</summary>
    public interface ISeat
    {
        StationRole Role { get; }
        bool IsOccupied { get; }
        GameObject GameObject { get; }
    }
}
