using UnityEngine;

namespace Voidward.Player
{
    /// <summary>
    /// Drives the crew body's humanoid Animator on every peer (#95). No NetworkAnimator: the speed comes from the
    /// pawn's own (owner-authoritative, interpolated) motion, the rest from the owner-written <see cref="PlayerPawn.Pose"/>
    /// byte (ground / air / EVA, held tool, tool in use). Strafing and walking backwards turn the body up to 70 deg
    /// towards the motion and play the locomotion backwards (negative speed in the blend tree). The held tool is a copy
    /// of the viewmodel in the right hand (shadow-only for the owner, whose body only casts shadows).
    /// </summary>
    [RequireComponent(typeof(Animator))]
    public class CrewAnimator : MonoBehaviour
    {
        [SerializeField] Transform[] handTools = new Transform[4];
        [SerializeField] Vector3[] handToolPos = new Vector3[4];
        [SerializeField] Vector3[] handToolEuler = new Vector3[4];

        static readonly int SpeedId = Animator.StringToHash("Speed");
        static readonly int ModeId = Animator.StringToHash("Mode");
        static readonly int FireId = Animator.StringToHash("Fire");
        const int ArmsLayer = 1;
        const float MaxYaw = 70f;

        Animator anim;
        PlayerPawn pawn;
        Vector3 lastPos;
        Vector3 velocity;
        float yaw;
        float speed;
        float armsWeight;
        bool wasUsing;
        int shownTool = -2;

        void Awake()
        {
            anim = GetComponent<Animator>();
            pawn = GetComponentInParent<PlayerPawn>();
            var hand = anim.isHuman ? anim.GetBoneTransform(HumanBodyBones.RightHand) : null;
            for (int i = 0; i < handTools.Length; i++)
            {
                var t = handTools[i];
                if (t == null) continue;
                if (hand != null) t.SetParent(hand, false);
                if (i < handToolPos.Length) t.localPosition = handToolPos[i];
                if (i < handToolEuler.Length) t.localRotation = Quaternion.Euler(handToolEuler[i]);
                t.gameObject.SetActive(false);
            }
        }

        void OnEnable()
        {
            lastPos = transform.parent != null ? transform.parent.position : transform.position;
            velocity = Vector3.zero;
        }

        void Update()
        {
            if (pawn == null || !pawn.IsSpawned) return;
            float dt = Mathf.Max(Time.deltaTime, 1e-4f);
            var root = pawn.transform;
            Vector3 pos = root.position;
            Vector3 v = (pos - lastPos) / dt;
            lastPos = pos;
            if (v.sqrMagnitude > 900f) v = Vector3.zero;   // teleport / rebase
            velocity = Vector3.Lerp(velocity, v, 1f - Mathf.Exp(-12f * dt));

            PlayerPawn.UnpackPose(pawn.Pose.Value, out var mode, out int tool, out bool used);
            Vector3 local = root.InverseTransformDirection(velocity);
            Vector2 planar = new Vector2(local.x, local.z);
            float mag = planar.magnitude;

            float targetYaw = 0f, targetSpeed = 0f;
            if (mode == PlayerPawn.MotionMode.Ground && mag > 0.3f)
            {
                bool back = planar.y < -0.3f * mag;
                Vector2 d = back ? -planar : planar;
                targetYaw = Mathf.Clamp(Mathf.Atan2(d.x, d.y) * Mathf.Rad2Deg, -MaxYaw, MaxYaw);
                targetSpeed = back ? -mag : mag;
            }
            else if (mode == PlayerPawn.MotionMode.Eva) targetSpeed = velocity.magnitude;
            yaw = Mathf.LerpAngle(yaw, targetYaw, 1f - Mathf.Exp(-8f * dt));
            speed = Mathf.Lerp(speed, targetSpeed, 1f - Mathf.Exp(-10f * dt));
            transform.localRotation = Quaternion.Euler(0f, yaw, 0f);

            anim.SetFloat(SpeedId, speed);
            anim.SetInteger(ModeId, (int)mode);
            bool armed = tool == (int)PlayerPawn.Tool.Blaster || tool == (int)PlayerPawn.Tool.Rifle || tool == (int)PlayerPawn.Tool.Drill;
            armsWeight = Mathf.MoveTowards(armsWeight, armed ? 1f : 0f, 4f * dt);
            if (anim.layerCount > ArmsLayer) anim.SetLayerWeight(ArmsLayer, armsWeight);
            bool shoots = tool == (int)PlayerPawn.Tool.Blaster || tool == (int)PlayerPawn.Tool.Rifle;
            if (used && !wasUsing && shoots) anim.SetTrigger(FireId);
            wasUsing = used;

            if (tool != shownTool)
            {
                shownTool = tool;
                for (int i = 0; i < handTools.Length; i++)
                    if (handTools[i]) handTools[i].gameObject.SetActive(i == tool);
            }
        }
    }
}
