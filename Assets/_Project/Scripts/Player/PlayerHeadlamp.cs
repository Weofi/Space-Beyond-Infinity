using Unity.Netcode;
using UnityEngine;
using Voidward.Core;

namespace Voidward.Player
{
    /// <summary>
    /// Helmet flashlight. The owner toggles it (Flashlight action) and aims it with the camera;
    /// on/off and a quantized pitch are synced so other players see where the beam points.
    /// </summary>
    [RequireComponent(typeof(PlayerPawn))]
    public class PlayerHeadlamp : NetworkBehaviour
    {
        [SerializeField] Light lamp;
        [Tooltip("Lamp offset from the camera (owner) / helmet (remote).")]
        [SerializeField] Vector3 offset = new Vector3(0f, 0.12f, 0.4f);

        public NetworkVariable<bool> On = new NetworkVariable<bool>(true, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Owner);
        public NetworkVariable<sbyte> Pitch = new NetworkVariable<sbyte>(0, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Owner);

        PlayerPawn pawn;
        float sendTimer;

        void Awake()
        {
            pawn = GetComponent<PlayerPawn>();
            if (lamp != null) lamp.enabled = false;
        }

        void Update()
        {
            if (!IsSpawned || !IsOwner) return;
            bool alive = pawn.Active.Value && !pawn.IsDown.Value;
            if (alive && pawn.CurrentSeat == null && GameState.GameplayInputAllowed && VwInput.Pressed(VwInput.Flashlight))
            {
                On.Value = !On.Value;
                Sfx.PlayUI("ui_click", 0.5f);
            }

            sendTimer -= Time.deltaTime;
            if (sendTimer <= 0f && pawn.CameraPoint != null)
            {
                float p = Mathf.DeltaAngle(0f, pawn.CameraPoint.localEulerAngles.x);
                var q = (sbyte)Mathf.Clamp(Mathf.RoundToInt(p), -90, 90);
                if (Mathf.Abs(q - Pitch.Value) >= 3) { Pitch.Value = q; sendTimer = 0.1f; }
            }
        }

        void LateUpdate()
        {
            if (lamp == null) return;
            bool show = IsSpawned && On.Value && pawn.Active.Value && !pawn.IsDown.Value && pawn.CurrentSeat == null;
            if (lamp.enabled != show) lamp.enabled = show;
            if (!show) return;

            var t = lamp.transform;
            if (IsOwner && pawn.CameraPoint != null)
            {
                var cam = pawn.CameraPoint;
                t.SetPositionAndRotation(cam.TransformPoint(offset), cam.rotation);
            }
            else
            {
                var rot = transform.rotation * Quaternion.Euler(Pitch.Value, 0f, 0f);
                t.SetPositionAndRotation(transform.TransformPoint(new Vector3(0f, 1.62f, 0f)) + rot * offset, rot);
            }
        }
    }
}
