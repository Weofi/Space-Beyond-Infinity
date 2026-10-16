using UnityEngine;

namespace Voidward.AI
{
    /// <summary>
    /// Client-side look of an enemy (#98), on every peer, nothing synced beyond what EnemyBrain already sends:
    /// the asset's Animator gets Speed (m/s, from the interpolated motion), Attack (trigger, AttackAnimClientRpc) and
    /// Alert (trigger when the brain switches from Patrol to a chase). Models without such a clip get the procedural
    /// fallback on `Body`: a lunge on attack, swimming (bob, sway, pulse) for floaters, banking into turns for ships.
    /// </summary>
    public class EnemyAnimator : MonoBehaviour
    {
        public enum Style { Walker, Floater, Ship }

        public Style Kind = Style.Walker;
        [Tooltip("Animator of the asset model (optional).")]
        public Animator Anim;
        [Tooltip("Visual root moved by the procedural motion (the model under the network root).")]
        public Transform Body;
        [Tooltip("Lunge distance (m) of the procedural attack.")]
        public float Lunge = 0.5f;
        [Tooltip("Bank angle (deg) at full turn rate (ships) / sway amplitude (floaters).")]
        public float Lean = 25f;

        static readonly int SpeedId = Animator.StringToHash("Speed");
        static readonly int AttackId = Animator.StringToHash("Attack");
        static readonly int AlertId = Animator.StringToHash("Alert");
        static readonly int CycleId = Animator.StringToHash("Cycle");

        EnemyBrain brain;
        Vector3 lastPos;
        Quaternion lastRot;
        float speed, bank, lunge, phase;
        bool hasAttack, hasAlert;
        Vector3 bodyPos;
        Quaternion bodyRot;
        Vector3 bodyScale;
        EnemyBrain.State lastState;

        void Awake()
        {
            brain = GetComponent<EnemyBrain>();
            lastPos = transform.position;
            lastRot = transform.rotation;
            phase = Mathf.Repeat(transform.position.x * 0.37f + transform.position.z * 0.19f, 10f);
            if (Body != null) { bodyPos = Body.localPosition; bodyRot = Body.localRotation; bodyScale = Body.localScale; }
            if (Anim != null)
            {
                foreach (var p in Anim.parameters)
                {
                    if (p.nameHash == AttackId) hasAttack = true;
                    if (p.nameHash == AlertId) hasAlert = true;
                    if (p.nameHash == CycleId) Anim.SetFloat(CycleId, Random.value);   // a pack does not walk in step
                }
            }
        }

        /// <summary>The brain started an attack (all peers).</summary>
        public void PlayAttack()
        {
            if (Anim != null && hasAttack) Anim.SetTrigger(AttackId);
            else lunge = 1f;
        }

        void LateUpdate()
        {
            float dt = Time.deltaTime;
            if (dt <= 0f) return;
            Vector3 v = (transform.position - lastPos) / dt;
            float yawRate = Vector3.SignedAngle(lastRot * Vector3.forward, transform.forward, transform.up) / dt;
            lastPos = transform.position;
            lastRot = transform.rotation;
            // planar speed along the body (walkers glue to the terrain, the vertical part is the ground's slope)
            float s = Kind == Style.Walker ? Vector3.ProjectOnPlane(v, transform.up).magnitude : v.magnitude;
            speed = Mathf.Lerp(speed, Mathf.Min(s, 60f), 1f - Mathf.Exp(-8f * dt));
            if (Anim != null) Anim.SetFloat(SpeedId, speed);

            if (brain != null && hasAlert)
            {
                var st = brain.CurrentState.Value;
                if (lastState == EnemyBrain.State.Patrol && st == EnemyBrain.State.Chase) Anim.SetTrigger(AlertId);
                lastState = st;
            }

            if (Body == null) return;
            lunge = Mathf.MoveTowards(lunge, 0f, dt * 3f);
            float l = Mathf.Sin(lunge * Mathf.PI) * Lunge;   // out and back over 1/3 s
            float t = Time.time + phase;
            switch (Kind)
            {
                case Style.Walker:
                    Body.localPosition = bodyPos + Vector3.forward * l;
                    break;
                case Style.Floater:
                {
                    // swimming: bob, a slow roll/pitch sway, the mantle pulses with each stroke, nose dips into fast moves
                    float stroke = Mathf.Sin(t * (1.6f + speed * 0.12f));
                    Body.localPosition = bodyPos + new Vector3(0f, Mathf.Sin(t * 0.9f) * 0.25f, l);
                    Body.localRotation = bodyRot * Quaternion.Euler(Mathf.Sin(t * 0.7f) * Lean * 0.3f + Mathf.Min(speed, 12f) * 1.2f, 0f, Mathf.Sin(t * 0.5f) * Lean * 0.4f);
                    Body.localScale = Vector3.Scale(bodyScale, new Vector3(1f + stroke * 0.03f, 1f - stroke * 0.05f, 1f + stroke * 0.04f));
                    break;
                }
                case Style.Ship:
                    bank = Mathf.Lerp(bank, Mathf.Clamp(-yawRate / 60f, -1f, 1f) * Lean, 1f - Mathf.Exp(-3f * dt));
                    Body.localPosition = bodyPos + new Vector3(0f, Mathf.Sin(t * 0.8f) * 0.15f, -l * 0.6f);   // recoil
                    Body.localRotation = bodyRot * Quaternion.Euler(0f, 0f, bank);
                    break;
            }
        }
    }
}
