using UnityEngine;

namespace Voidward.Ship
{
    /// <summary>
    /// The seated crew body of a <see cref="ShipSeat"/> (#95): a humanoid sitting loop, hands on the stick / throttle
    /// where the seat has grips (Animator IK moves the wrists there, elbows hinted down and out; the hands are then
    /// turned so the fingers follow the grip's forward and the back of the hand its up axis), the body shifted once per
    /// sit-down so its eyes meet the seat's view point. For the local occupant the head is collapsed (the camera sits
    /// in the helmet), so the own arms, legs and hands on the controls are visible in the cockpit.
    /// </summary>
    [RequireComponent(typeof(Animator))]
    public class CrewSeatPose : MonoBehaviour
    {
        [SerializeField] Transform eye;
        [SerializeField] Transform rightGrip;
        [SerializeField] Transform leftGrip;
        [SerializeField] Vector3 eyeOffset = new Vector3(0f, 0.09f, 0.1f);   // head bone -> eyes (body space)
        [SerializeField] Vector3 elbowHint = new Vector3(0.34f, 0.7f, -0.1f); // right elbow hint (body space, x mirrored)

        Animator anim;
        Transform head;
        Hand right, left;
        Vector3 basePos;
        int frames;
        bool local;

        // curl per joint (proximal, intermediate, distal) around the grip; the thumb closes less
        static readonly float[] FingerCurl = { 55f, 70f, 45f };
        static readonly float[] ThumbCurl = { 15f, 25f, 20f };

        /// <summary>A hand bone with its bind-pose finger and back-of-hand axes (bone space) and its finger joints.</summary>
        struct Hand
        {
            public Transform Bone;
            public Quaternion Basis;     // bone rotation -> (fingers forward, back of hand up)
            public Transform[] Joints;   // 5 fingers x 3 joints, thumb first

            public static Hand Of(Animator a, bool right, Transform root)
            {
                var b = a.GetBoneTransform(right ? HumanBodyBones.RightHand : HumanBodyBones.LeftHand);
                var m = a.GetBoneTransform(right ? HumanBodyBones.RightMiddleProximal : HumanBodyBones.LeftMiddleProximal);
                if (b == null || m == null) return default;
                Vector3 fingers = Quaternion.Inverse(b.rotation) * (m.position - b.position).normalized;
                Vector3 back = Quaternion.Inverse(b.rotation) * root.up;   // T-pose: palms down, backs up
                var joints = new Transform[15];
                int first = (int)(right ? HumanBodyBones.RightThumbProximal : HumanBodyBones.LeftThumbProximal);
                for (int i = 0; i < 15; i++) joints[i] = a.GetBoneTransform((HumanBodyBones)(first + i));
                return new Hand { Bone = b, Basis = Quaternion.Inverse(Quaternion.LookRotation(fingers, back)), Joints = joints };
            }

            public void Align(Transform grip)
            {
                if (Bone == null || grip == null) return;
                Bone.rotation = Quaternion.LookRotation(grip.forward, grip.up) * Basis;
                // close the fingers towards the palm: about the hand frame's right axis, parents before children
                Vector3 axis = Bone.rotation * Quaternion.Inverse(Basis) * Vector3.right;
                for (int i = 0; i < Joints.Length; i++)
                {
                    var j = Joints[i];
                    if (j == null) continue;
                    float deg = i < 3 ? ThumbCurl[i] : FingerCurl[i % 3];
                    j.rotation = Quaternion.AngleAxis(deg, axis) * j.rotation;
                }
            }
        }

        public bool Local
        {
            get => local;
            set { local = value; if (!value && head != null) head.localScale = Vector3.one; }
        }

        void Awake()
        {
            anim = GetComponent<Animator>();
            basePos = transform.localPosition;
            if (!anim.isHuman) return;
            head = anim.GetBoneTransform(HumanBodyBones.Head);
            // still the bind pose (T-pose) here, before the first animator update
            right = Hand.Of(anim, true, transform);
            left = Hand.Of(anim, false, transform);
        }

        void OnEnable()
        {
            frames = 0;
            transform.localPosition = basePos;
        }

        void OnAnimatorIK(int layer)
        {
            Grip(AvatarIKGoal.RightHand, AvatarIKHint.RightElbow, rightGrip, 1f);
            Grip(AvatarIKGoal.LeftHand, AvatarIKHint.LeftElbow, leftGrip, -1f);
        }

        void Grip(AvatarIKGoal goal, AvatarIKHint hint, Transform t, float side)
        {
            float w = t != null ? 1f : 0f;
            anim.SetIKPositionWeight(goal, w);
            anim.SetIKHintPositionWeight(hint, w * 0.6f);
            if (t == null) return;
            anim.SetIKPosition(goal, t.position);
            anim.SetIKHintPosition(hint, transform.TransformPoint(new Vector3(elbowHint.x * side, elbowHint.y, elbowHint.z)));
        }

        void LateUpdate()
        {
            if (head == null) return;
            // after the first posed frames: move the body so the eyes sit at the seat's view point
            if (eye != null && frames < 3 && ++frames == 3)
            {
                Vector3 eyes = head.position + transform.rotation * eyeOffset;
                transform.position += eye.position - eyes;
            }
            right.Align(rightGrip);
            left.Align(leftGrip);
            if (local) head.localScale = Vector3.one * 0.001f;
        }
    }
}
