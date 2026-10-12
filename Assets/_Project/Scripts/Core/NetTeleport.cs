using Unity.Netcode;
using Unity.Netcode.Components;
using UnityEngine;

namespace Voidward.Core
{
    /// <summary>
    /// Server-initiated teleport that works for both server- and owner-authoritative
    /// NetworkTransforms: the request is routed to whoever has transform authority.
    /// Used for sector jumps (world rebasing), respawns and ship placement.
    /// </summary>
    public class NetTeleport : NetworkBehaviour
    {
        Rigidbody rb;
        NetworkTransform nt;

        void Awake()
        {
            rb = GetComponent<Rigidbody>();
            nt = GetComponent<NetworkTransform>();
        }

        /// <summary>Server only.</summary>
        public void ServerTeleport(Vector3 pos, Quaternion rot)
        {
            if (!IsServer) return;
            if (HasTransformAuthority) Apply(pos, rot);
            else TeleportClientRpc(pos, rot, RpcTarget.Single(OwnerClientId, RpcTargetUse.Temp));
        }

        [Rpc(SendTo.SpecifiedInParams)]
        void TeleportClientRpc(Vector3 pos, Quaternion rot, RpcParams p) => Apply(pos, rot);

        bool HasTransformAuthority => nt != null ? nt.CanCommitToTransform : IsOwner;

        void Apply(Vector3 pos, Quaternion rot)
        {
            if (rb != null)
            {
                if (!rb.isKinematic)
                {
                    rb.linearVelocity = Vector3.zero;
                    rb.angularVelocity = Vector3.zero;
                }
                rb.position = pos;
                rb.rotation = rot;
            }
            transform.SetPositionAndRotation(pos, rot);
            if (nt != null && nt.CanCommitToTransform)
                nt.Teleport(pos, rot, transform.localScale);
            Teleported?.Invoke();
        }

        /// <summary>Raised locally on the authority after a teleport was applied.</summary>
        public event System.Action Teleported;
    }
}
