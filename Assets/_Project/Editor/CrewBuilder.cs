using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;
using Voidward.Player;
using Voidward.Ship;

namespace Voidward.EditorTools
{
    /// <summary>
    /// Crew bodies (#95): a rigged humanoid (the CoP spacesuit from Tools/Blender/crew_suit.py, rigged to the Quaternius
    /// UAL skeleton; until that FBX exists the UAL mannequin itself) animated by the Quaternius Universal Animation
    /// Library (CC0). Generated here: Humanoid importer settings for both FBX files, the used clips (looping, root baked
    /// into the pose), an upper-body mask, the on-foot controller (locomotion / air / EVA + an arms layer for held
    /// tools) and the seated controller (sitting loop with an IK pass for the hands on stick and throttle).
    /// Returns null everywhere when the UAL FBX is missing, then the procedural astronaut stays.
    /// </summary>
    public static partial class ContentBuilder
    {
        const string AnimDir = ArtDir + "/Animations";
        const string UalPath = AnimDir + "/UAL/UAL_Standard.fbx";
        const string CrewModelDir = ModelDir + "/Crew";
        const string SuitPath = CrewModelDir + "/Crew_Suit.fbx";

        static readonly string[] UalClips =
        {
            "Idle_Loop", "Walk_Loop", "Jog_Fwd_Loop", "Sprint_Loop", "Jump_Start", "Jump_Loop", "Jump_Land",
            "Swim_Idle_Loop", "Swim_Fwd_Loop", "Sitting_Idle_Loop", "Driving_Loop", "Pistol_Idle_Loop", "Pistol_Shoot",
            "Pistol_Aim_Neutral", "Interact", "Death01",
        };

        static bool ualReady;
        static readonly Dictionary<string, AnimationClip> ualClips = new Dictionary<string, AnimationClip>();

        /// <summary>Humanoid import of the animation library: only the used clips, loops looping, root motion baked.</summary>
        static bool PrepareUal()
        {
            if (ualReady) return true;
            if (!(AssetImporter.GetAtPath(UalPath) is ModelImporter imp)) return false;
            HumanoidImporter(imp, true);
            var clips = new List<ModelImporterClipAnimation>();
            foreach (var c in imp.defaultClipAnimations)
            {
                string name = c.name.Substring(c.name.LastIndexOf('|') + 1);
                if (!UalClips.Contains(name)) continue;
                c.name = name;
                bool loop = name.EndsWith("_Loop");
                c.loopTime = loop;
                c.loopPose = loop;
                c.lockRootRotation = true;
                c.lockRootHeightY = true;
                c.lockRootPositionXZ = true;
                c.keepOriginalOrientation = true;
                c.keepOriginalPositionY = true;
                c.keepOriginalPositionXZ = true;
                clips.Add(c);
            }
            imp.clipAnimations = clips.ToArray();
            imp.SaveAndReimport();
            ualClips.Clear();
            foreach (var o in AssetDatabase.LoadAllAssetsAtPath(UalPath))
                if (o is AnimationClip clip && !clip.name.StartsWith("__preview")) ualClips[clip.name] = clip;
            ualReady = ualClips.Count > 0;
            return ualReady;
        }

        static void HumanoidImporter(ModelImporter imp, bool animations)
        {
            imp.animationType = ModelImporterAnimationType.Human;
            imp.avatarSetup = ModelImporterAvatarSetup.CreateFromThisModel;
            imp.bakeAxisConversion = true;
            if (animations) imp.materialImportMode = ModelImporterMaterialImportMode.None;   // the library's mannequin: CrewMaterial
            imp.importAnimation = animations;
            imp.importCameras = false;
            imp.importLights = false;
            imp.importBlendShapes = false;
            imp.importTangents = ModelImporterTangents.CalculateMikk;
        }

        static AnimationClip UalClip(string name) => ualClips.TryGetValue(name, out var c) ? c : null;

        /// <summary>The body source: the rigged suit if it was exported, else the UAL mannequin.</summary>
        static string CrewModelPath()
        {
            if (AssetImporter.GetAtPath(SuitPath) is ModelImporter imp)
            {
                if (imp.animationType != ModelImporterAnimationType.Human || imp.importAnimation)
                {
                    HumanoidImporter(imp, false);
                    imp.SaveAndReimport();
                }
                RemapMaterials(imp, SuitMaterials());
                return SuitPath;
            }
            return UalPath;
        }

        static AvatarMask ArmsMask()
        {
            string path = AnimDir + "/Crew_Arms.mask";
            var m = AssetDatabase.LoadAssetAtPath<AvatarMask>(path);
            if (m == null) { m = new AvatarMask(); AssetDatabase.CreateAsset(m, path); }
            for (int i = 0; i < (int)AvatarMaskBodyPart.LastBodyPart; i++) m.SetHumanoidBodyPartActive((AvatarMaskBodyPart)i, false);
            foreach (var p in new[] { AvatarMaskBodyPart.LeftArm, AvatarMaskBodyPart.RightArm, AvatarMaskBodyPart.LeftFingers, AvatarMaskBodyPart.RightFingers })
                m.SetHumanoidBodyPartActive(p, true);
            EditorUtility.SetDirty(m);
            return m;
        }

        static AnimatorController NewController(string name)
        {
            string path = AnimDir + "/" + name + ".controller";
            if (AssetDatabase.LoadAssetAtPath<AnimatorController>(path) != null) AssetDatabase.DeleteAsset(path);
            return AnimatorController.CreateAnimatorControllerAtPath(path);
        }

        static BlendTree Blend(AnimatorController ctrl, AnimatorState state, string param, params (string clip, float at, float speed)[] children)
        {
            var tree = new BlendTree { name = state.name, blendParameter = param, blendType = BlendTreeType.Simple1D, useAutomaticThresholds = false };
            AssetDatabase.AddObjectToAsset(tree, ctrl);
            foreach (var (clip, at, speed) in children) tree.AddChild(UalClip(clip), at);
            var list = tree.children;
            for (int i = 0; i < list.Length; i++) list[i].timeScale = children[i].speed;
            tree.children = list;
            state.motion = tree;
            return tree;
        }

        static AnimatorStateTransition To(AnimatorState from, AnimatorState to, float duration, string param, AnimatorConditionMode mode, float value)
        {
            var t = from.AddTransition(to);
            t.hasExitTime = false;
            t.duration = duration;
            t.AddCondition(mode, value, param);
            return t;
        }

        /// <summary>
        /// On foot: Speed (signed forward m/s, negative = backwards), Mode (0 ground, 1 air, 2 EVA), Fire (trigger).
        /// Layer 1 "Arms" (weight set by CrewAnimator) holds the tool and kicks on Fire.
        /// </summary>
        static AnimatorController CrewController()
        {
            if (!PrepareUal()) return null;
            var ctrl = NewController("Crew_Locomotion");
            ctrl.AddParameter("Speed", AnimatorControllerParameterType.Float);
            ctrl.AddParameter("Mode", AnimatorControllerParameterType.Int);
            ctrl.AddParameter("Fire", AnimatorControllerParameterType.Trigger);
            var sm = ctrl.layers[0].stateMachine;

            var move = sm.AddState("Move");
            Blend(ctrl, move, "Speed",
                ("Jog_Fwd_Loop", -6f, -1.2f), ("Walk_Loop", -1.6f, -1f), ("Idle_Loop", 0f, 1f),
                ("Walk_Loop", 1.6f, 1f), ("Jog_Fwd_Loop", 6f, 1.25f), ("Sprint_Loop", 9.5f, 1.1f));
            var air = sm.AddState("Air");
            air.motion = UalClip("Jump_Loop");
            var land = sm.AddState("Land");
            land.motion = UalClip("Jump_Land");
            land.speed = 1.6f;
            var eva = sm.AddState("Eva");
            Blend(ctrl, eva, "Speed", ("Swim_Idle_Loop", 0f, 0.8f), ("Swim_Idle_Loop", 4f, 1.4f));
            sm.defaultState = move;

            var anyAir = sm.AddAnyStateTransition(air);
            anyAir.hasExitTime = false; anyAir.duration = 0.25f; anyAir.canTransitionToSelf = false;
            anyAir.AddCondition(AnimatorConditionMode.Equals, 1, "Mode");
            var anyEva = sm.AddAnyStateTransition(eva);
            anyEva.hasExitTime = false; anyEva.duration = 0.4f; anyEva.canTransitionToSelf = false;
            anyEva.AddCondition(AnimatorConditionMode.Equals, 2, "Mode");
            To(air, land, 0.08f, "Mode", AnimatorConditionMode.Equals, 0);
            To(eva, move, 0.35f, "Mode", AnimatorConditionMode.Equals, 0);
            var landed = land.AddTransition(move);
            landed.hasExitTime = true; landed.exitTime = 0.35f; landed.duration = 0.25f;
            To(land, move, 0.2f, "Speed", AnimatorConditionMode.Greater, 2.5f);

            ctrl.AddLayer("Arms");
            var layers = ctrl.layers;
            layers[1].avatarMask = ArmsMask();
            layers[1].defaultWeight = 0f;
            layers[1].blendingMode = AnimatorLayerBlendingMode.Override;
            ctrl.layers = layers;
            var arms = layers[1].stateMachine;
            var hold = arms.AddState("Hold");
            hold.motion = UalClip("Pistol_Idle_Loop");
            var shoot = arms.AddState("Shoot");
            shoot.motion = UalClip("Pistol_Shoot");
            shoot.speed = 1.5f;
            arms.defaultState = hold;
            var fire = hold.AddTransition(shoot);
            fire.hasExitTime = false; fire.duration = 0.05f;
            fire.AddCondition(AnimatorConditionMode.If, 0, "Fire");
            var back = shoot.AddTransition(hold);
            back.hasExitTime = true; back.exitTime = 0.8f; back.duration = 0.15f;
            EditorUtility.SetDirty(ctrl);
            return ctrl;
        }

        /// <summary>Seated: one sitting loop, IK pass for the grips (CrewSeatPose).</summary>
        static AnimatorController CrewSeatedController()
        {
            if (!PrepareUal()) return null;
            var ctrl = NewController("Crew_Seated");
            var layers = ctrl.layers;
            layers[0].iKPass = true;
            ctrl.layers = layers;
            var sit = layers[0].stateMachine.AddState("Sit");
            sit.motion = UalClip("Sitting_Idle_Loop");
            layers[0].stateMachine.defaultState = sit;
            EditorUtility.SetDirty(ctrl);
            return ctrl;
        }

        static AnimatorController crewCtrl, crewSeatedCtrl;

        /// <summary>Resets the per-build caches (BuildPawn calls it first).</summary>
        static void ResetCrew()
        {
            ualReady = false;
            crewCtrl = crewSeatedCtrl = null;
        }

        /// <summary>Slate mannequin material (until the suit's PBR set exists).</summary>
        static Material CrewMaterial()
        {
            var m = Mat("VW_CrewMannequin", "Universal Render Pipeline/Lit");
            m.SetColor("_BaseColor", new Color(0.30f, 0.33f, 0.38f));
            m.SetFloat("_Metallic", 0.35f);
            m.SetFloat("_Smoothness", 0.45f);
            return m;
        }

        /// <summary>
        /// An instance of the crew model under parent with its Animator (avatar from the FBX, controller given).
        /// The model faces +Z with its feet at the parent's origin. Null without the UAL FBX.
        /// </summary>
        static GameObject CrewModel(Transform parent, string name, AnimatorController controller)
        {
            if (controller == null) return null;
            string path = CrewModelPath();
            var src = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            var avatar = AssetDatabase.LoadAllAssetsAtPath(path).OfType<Avatar>().FirstOrDefault();
            if (src == null || avatar == null) return null;
            var go = (GameObject)Object.Instantiate(src, parent, false);
            go.name = name;
            go.transform.localPosition = Vector3.zero;
            go.transform.localRotation = Quaternion.identity;
            var anim = go.GetComponent<Animator>();
            if (anim == null) anim = go.AddComponent<Animator>();
            anim.avatar = avatar;
            anim.runtimeAnimatorController = controller;
            anim.applyRootMotion = false;
            anim.cullingMode = AnimatorCullingMode.CullUpdateTransforms;
            var mat = path == UalPath ? CrewMaterial() : null;   // the suit's own materials come from the importer remap
            foreach (var r in go.GetComponentsInChildren<SkinnedMeshRenderer>(true))
            {
                if (mat != null) r.sharedMaterials = Enumerable.Repeat(mat, Mathf.Max(1, r.sharedMesh != null ? r.sharedMesh.subMeshCount : 1)).ToArray();
                r.updateWhenOffscreen = false;
                r.skinnedMotionVectors = false;
                // the imported bounds sit metres off the body: a 2.4 m cube around the standing body, in root bone space
                var rb = r.rootBone != null ? r.rootBone : r.transform;
                Vector3 c = rb.InverseTransformPoint(go.transform.position + go.transform.up * 0.95f);
                Vector3 ls = rb.lossyScale;
                r.localBounds = new Bounds(c, new Vector3(2.4f / Mathf.Abs(ls.x), 2.4f / Mathf.Abs(ls.y), 2.4f / Mathf.Abs(ls.z)));
            }
            return go;
        }

        /// <summary>
        /// The on-foot body: crew model + CrewAnimator, the tools as hand-held copies (the asset models, origin = palm
        /// on the grip, else the procedural meshes). Null without the UAL FBX (then BuildPawn keeps the astronaut).
        /// </summary>
        static GameObject CrewBody(Transform parent, Mesh[] toolMeshes)
        {
            crewCtrl = crewCtrl != null ? crewCtrl : CrewController();
            var go = CrewModel(parent, "Crew", crewCtrl);
            if (go == null) return null;
            var anim = go.GetComponent<Animator>();
            var ca = go.AddComponent<CrewAnimator>();
            HandGrip(anim, go.transform, out var gripPos, out var gripRot);
            var tools = new Object[toolMeshes.Length];
            var pos = new Vector3[toolMeshes.Length];
            var euler = new Vector3[toolMeshes.Length];
            for (int i = 0; i < toolMeshes.Length; i++)
            {
                var t = Child(go.transform, "HandTool_" + i, Vector3.zero);
                ToolVisual(t.transform, i, toolMeshes[i], 1f, out bool asset);
                tools[i] = t.transform;
                // procedural meshes have their origin mid-body: the grip sits ~6 cm under it
                pos[i] = gripPos + (asset ? Vector3.zero : gripRot * new Vector3(0f, -0.06f, 0f));
                euler[i] = gripRot.eulerAngles;
                t.SetActive(false);
            }
            SetArray(ca, "handTools", tools);
            var so = new SerializedObject(ca);
            var pp = so.FindProperty("handToolPos"); var pe = so.FindProperty("handToolEuler");
            pp.arraySize = pe.arraySize = toolMeshes.Length;
            for (int i = 0; i < toolMeshes.Length; i++)
            {
                pp.GetArrayElementAtIndex(i).vector3Value = pos[i];
                pe.GetArrayElementAtIndex(i).vector3Value = euler[i];
            }
            so.ApplyModifiedPropertiesWithoutUndo();
            return go;
        }

        /// <summary>
        /// Where a tool's grip origin sits in the right hand bone's space, from the bind pose: palm centre a little along
        /// the fingers and towards the palm, barrel along the hand (wrist -> knuckles), tool top towards the thumb.
        /// </summary>
        static void HandGrip(Animator anim, Transform root, out Vector3 pos, out Quaternion rot)
        {
            pos = Vector3.zero; rot = Quaternion.identity;
            var hand = anim.GetBoneTransform(HumanBodyBones.RightHand);
            var middle = anim.GetBoneTransform(HumanBodyBones.RightMiddleProximal);
            var thumb = anim.GetBoneTransform(HumanBodyBones.RightThumbProximal);
            if (hand == null || middle == null || thumb == null) return;
            Vector3 fingers = (middle.position - hand.position).normalized;
            Vector3 up = Vector3.ProjectOnPlane(thumb.position - hand.position, fingers).normalized;
            Vector3 palm = Vector3.Cross(fingers, up);
            // bind pose (T or A): palms face down and/or in towards the body
            Vector3 inward = Vector3.ProjectOnPlane(root.position - hand.position, root.up).normalized;
            if (Vector3.Dot(palm, inward - root.up) < 0f) palm = -palm;
            Vector3 world = hand.position + fingers * 0.06f + palm * 0.025f;
            pos = hand.InverseTransformPoint(world);
            rot = Quaternion.Inverse(hand.rotation) * Quaternion.LookRotation(fingers, up);
        }

        /// <summary>Seated body: crew model + CrewSeatPose (eyes to the view point, optional grips). Null without UAL.</summary>
        static GameObject CrewSeated(Transform anchor, Transform view)
        {
            crewSeatedCtrl = crewSeatedCtrl != null ? crewSeatedCtrl : CrewSeatedController();
            var go = CrewModel(anchor, "SeatedBody", crewSeatedCtrl);
            if (go == null) return null;
            go.GetComponent<Animator>().cullingMode = AnimatorCullingMode.AlwaysAnimate;   // IK + eye alignment run on the first frames
            var pose = go.AddComponent<CrewSeatPose>();
            Set(pose, "eye", view);
            go.SetActive(false);
            return go;
        }

        /// <summary>
        /// Wrist position for gripping a lever (mesh bounds in the lever's space): `down` below its top, beside it on the
        /// hand's side (side = +1 right hand, -1 left) and behind it, so the palm closes around the grip.
        /// </summary>
        static Vector3 GripPoint(Bounds b, float down, float side) =>
            new Vector3(b.center.x + side * 0.04f, b.max.y - down, b.center.z - 0.085f);

        /// <summary>Hand IK targets for a seat's body (e.g. the Dart's side stick and throttle).</summary>
        static void SeatGrips(ShipSeat seat, Transform right, Transform left)
        {
            var body = seat.transform.GetComponentsInChildren<CrewSeatPose>(true).FirstOrDefault();
            if (body == null) return;
            if (right != null) Set(body, "rightGrip", right);
            if (left != null) Set(body, "leftGrip", left);
        }
    }
}
