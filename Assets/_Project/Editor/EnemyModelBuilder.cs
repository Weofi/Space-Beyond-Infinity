using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;

namespace Voidward.EditorTools
{
    /// <summary>
    /// Enemy models from free Sketchfab assets (#98, Tools/Blender/enemy_assets.py): static PBR ships/floaters and rigged
    /// walkers. Each asset has its FBX in Art/Models/Enemies and a texture folder of the same name with the glTF set per
    /// material (`mat_baseColor`, `mat_normal` (OpenGL), `mat_metallicRoughness`, `mat_emissive`). Generated here:
    /// URP Lit materials (remapped by the FBX material name), Generic rig import with the used clips, and one animator
    /// controller per walker (Speed blend idle/walk/run, Attack + Alert triggers, Cycle offset). Returns null when the FBX
    /// is missing; PrefabBuilder then keeps the procedural mesh.
    /// </summary>
    public static partial class ContentBuilder
    {
        const string EnemyModelDir = ModelDir + "/Enemies";

        /// <summary>Clip roles of a rigged enemy. Speeds are the ground speeds (m/s) at which walk/run play at clip speed.</summary>
        class EnemyRig
        {
            public string Idle, Walk, Run, Attack, Alert;
            public float WalkSpeed = 2f, RunSpeed = 6f, IdleRate = 1f, WalkRate = 1f, RunRate = 1f, AttackRate = 1f;
        }

        static readonly Dictionary<string, EnemyRig> EnemyRigs = new Dictionary<string, EnemyRig>
        {
            // one prowl cycle only: slow for idle, sped up for the dash (the lunge is procedural)
            ["Enemy_Crawler_Insect"] = new EnemyRig { Idle = "Prowl", Walk = "Prowl", Run = "Prowl", IdleRate = 0.15f, WalkSpeed = 3f, RunSpeed = 7f, RunRate = 1.8f },
            ["Enemy_Spitter_Stalker"] = new EnemyRig { Idle = "A_idle", Walk = "A_walk_F", Run = "A_run_F", Attack = "A_daggerthrow_01", WalkSpeed = 2f, RunSpeed = 5f, AttackRate = 1.3f },
            ["Enemy_Guardian_Beast"] = new EnemyRig { Idle = "Idle", Walk = "walk", Run = "Run", Attack = "attack", Alert = "Roar", WalkSpeed = 2.5f, RunSpeed = 5.5f, AttackRate = 1.6f },
        };

        static bool HasEnemyModel(string asset) => AssetImporter.GetAtPath(EnemyModelDir + "/" + asset + ".fbx") is ModelImporter;

        /// <summary>URP Lit materials of an asset by its glTF material name (texture folder `Enemies/asset`).</summary>
        static Dictionary<string, Material> EnemyMaterials(string asset) => GltfMaterials(EnemyModelDir + "/" + asset, asset);

        /// <summary>
        /// URP Lit materials from a texture folder holding one glTF-style set per material (`mat_baseColor`, `mat_normal`
        /// (OpenGL), `mat_metallicRoughness`, `mat_emissive`, `mat_ao`), keyed by the material name; material assets are
        /// named `prefix_mat`. Two-sided (the Sketchfab sources are full of single sheets).
        /// </summary>
        static Dictionary<string, Material> GltfMaterials(string dir, string asset)
        {
            var mats = new Dictionary<string, Material>();
            if (!Directory.Exists(dir)) return mats;
            foreach (var file in Directory.GetFiles(dir, "*_baseColor.*").Where(f => !f.EndsWith(".meta")).OrderBy(f => f))
            {
                string path = file.Replace('\\', '/');
                string name = Path.GetFileNameWithoutExtension(path);
                name = name.Substring(0, name.Length - "_baseColor".Length);
                string t(string suffix) => File.Exists(dir + "/" + name + suffix) ? dir + "/" + name + suffix : null;
                var m = Mat(asset + "_" + name, "Universal Render Pipeline/Lit");
                m.SetColor("_BaseColor", Color.white);
                m.SetTexture("_BaseMap", ImportTexture(path, true, maxSize: 2048));
                string mr = t("_metallicRoughness.jpg");
                if (mr != null)
                {
                    m.SetTexture("_MetallicGlossMap", PackGltfMetallicRoughness(mr, dir + "/" + name + "_MetallicSmoothness.png"));
                    m.SetFloat("_SmoothnessTextureChannel", 0f);
                    m.EnableKeyword("_METALLICSPECGLOSSMAP");
                    m.SetFloat("_Metallic", 1f);
                    m.SetFloat("_Smoothness", 1f);
                }
                else
                {
                    m.SetTexture("_MetallicGlossMap", null);
                    m.DisableKeyword("_METALLICSPECGLOSSMAP");
                    m.SetFloat("_Metallic", 0.2f);
                    m.SetFloat("_Smoothness", 0.55f);
                }
                string n = t("_normal.jpg");
                m.SetTexture("_BumpMap", n != null ? ImportTexture(n, false, true, false, 2048) : null);
                if (n != null) m.EnableKeyword("_NORMALMAP"); else m.DisableKeyword("_NORMALMAP");
                string e = t("_emissive.jpg");
                if (e != null)
                {
                    m.SetTexture("_EmissionMap", ImportTexture(e, true, maxSize: 2048));
                    m.SetColor("_EmissionColor", Color.white * 2.2f);
                    m.EnableKeyword("_EMISSION");
                    m.globalIlluminationFlags = MaterialGlobalIlluminationFlags.RealtimeEmissive;
                }
                string ao = t("_ao.jpg");
                m.SetTexture("_OcclusionMap", ao != null ? ImportTexture(ao, false, maxSize: 2048) : null);
                if (ao != null) m.EnableKeyword("_OCCLUSIONMAP"); else m.DisableKeyword("_OCCLUSIONMAP");
                // the Sketchfab sources are double-sided (insect wings, tentacle tips and fins are single sheets)
                m.SetFloat("_Cull", 0f);
                m.enableInstancing = true;
                EditorUtility.SetDirty(m);
                mats[name] = m;
            }
            return mats;
        }

        /// <summary>Generic rig import with only the rig's clips (named without the take prefix); loops loop. Returns them by name.</summary>
        static Dictionary<string, AnimationClip> PrepareEnemyRig(string path, EnemyRig rig)
        {
            var imp = (ModelImporter)AssetImporter.GetAtPath(path);
            var used = new[] { rig.Idle, rig.Walk, rig.Run, rig.Attack, rig.Alert }.Where(c => c != null).ToList();
            bool dirty = imp.animationType != ModelImporterAnimationType.Generic || !imp.importAnimation || imp.importCameras || imp.importLights
                || imp.importBlendShapes || imp.importTangents != ModelImporterTangents.CalculateMikk;
            imp.animationType = ModelImporterAnimationType.Generic;
            imp.avatarSetup = ModelImporterAvatarSetup.CreateFromThisModel;
            imp.importAnimation = true;
            imp.importCameras = false;
            imp.importLights = false;
            imp.importBlendShapes = false;
            imp.importTangents = ModelImporterTangents.CalculateMikk;
            var clips = new List<ModelImporterClipAnimation>();
            foreach (var c in imp.defaultClipAnimations)
            {
                string name = c.takeName.Substring(c.takeName.LastIndexOf('|') + 1);
                if (!used.Contains(name)) continue;
                c.name = name;
                bool loop = name != rig.Attack && name != rig.Alert;
                c.loopTime = loop;
                c.loopPose = false;
                c.lockRootRotation = c.lockRootHeightY = c.lockRootPositionXZ = true;   // in place: the brain moves the root
                c.keepOriginalOrientation = c.keepOriginalPositionY = c.keepOriginalPositionXZ = true;
                clips.Add(c);
            }
            var cur = imp.clipAnimations;
            if (dirty || cur.Length != clips.Count || cur.Where((c, i) => c.name != clips[i].name || c.loopTime != clips[i].loopTime).Any())
            {
                imp.clipAnimations = clips.ToArray();
                imp.SaveAndReimport();
            }
            var result = new Dictionary<string, AnimationClip>();
            foreach (var o in AssetDatabase.LoadAllAssetsAtPath(path))
                if (o is AnimationClip clip && !clip.name.StartsWith("__preview")) result[clip.name] = clip;
            // in place: the run cycle's travelling node becomes the motion node, its motion is extracted and (no root
            // motion on the Animator) dropped, so the brain alone moves the enemy
            string node = rig.Run != null && result.TryGetValue(rig.Run, out var run) ? TravellingNode(run) : null;
            if (node != null && imp.motionNodeName != node)
            {
                imp.motionNodeName = node;
                var cs = imp.clipAnimations;
                foreach (var c in cs) { c.lockRootPositionXZ = false; c.lockRootHeightY = true; c.lockRootRotation = true; }
                imp.clipAnimations = cs;
                imp.SaveAndReimport();
                result.Clear();
                foreach (var o in AssetDatabase.LoadAllAssetsAtPath(path))
                    if (o is AnimationClip clip && !clip.name.StartsWith("__preview")) result[clip.name] = clip;
            }
            return result;
        }

        /// <summary>The shallowest transform whose forward position travels more than 0.3 m over the clip (null if none).</summary>
        static string TravellingNode(AnimationClip clip)
        {
            string best = null;
            int depth = int.MaxValue;
            foreach (var b in AnimationUtility.GetCurveBindings(clip))
            {
                if (b.propertyName != "m_LocalPosition.z" && b.propertyName != "m_LocalPosition.y" && b.propertyName != "m_LocalPosition.x") continue;
                var keys = AnimationUtility.GetEditorCurve(clip, b).keys;
                if (keys.Length < 2) continue;
                float lo = keys.Min(k => k.value), hi = keys.Max(k => k.value);
                int d = b.path.Count(ch => ch == '/');
                if (hi - lo > 0.3f && d < depth) { depth = d; best = b.path; }
            }
            return best;
        }

        /// <summary>Speed (m/s) blends idle -> walk -> run; Attack / Alert triggers play their clip once and return.</summary>
        static AnimatorController EnemyController(string asset, EnemyRig rig, Dictionary<string, AnimationClip> clips)
        {
            AnimationClip C(string n) => n != null && clips.TryGetValue(n, out var c) ? c : null;
            var ctrl = NewController(asset);
            ctrl.AddParameter("Speed", AnimatorControllerParameterType.Float);
            ctrl.AddParameter("Cycle", AnimatorControllerParameterType.Float);
            var sm = ctrl.layers[0].stateMachine;
            var move = sm.AddState("Move");
            var tree = new BlendTree { name = "Move", blendParameter = "Speed", blendType = BlendTreeType.Simple1D, useAutomaticThresholds = false };
            AssetDatabase.AddObjectToAsset(tree, ctrl);
            tree.AddChild(C(rig.Idle), 0f);
            tree.AddChild(C(rig.Walk), rig.WalkSpeed);
            tree.AddChild(C(rig.Run), rig.RunSpeed);
            var ch = tree.children;
            ch[0].timeScale = rig.IdleRate; ch[1].timeScale = rig.WalkRate; ch[2].timeScale = rig.RunRate;
            tree.children = ch;
            move.motion = tree;
            move.cycleOffsetParameter = "Cycle";
            move.cycleOffsetParameterActive = true;
            sm.defaultState = move;
            void OneShot(string param, string clip, float rate)
            {
                if (C(clip) == null) return;
                ctrl.AddParameter(param, AnimatorControllerParameterType.Trigger);
                var s = sm.AddState(param);
                s.motion = C(clip);
                s.speed = rate;
                var go = move.AddTransition(s);
                go.hasExitTime = false; go.duration = 0.1f;
                go.AddCondition(AnimatorConditionMode.If, 0, param);
                var back = s.AddTransition(move);
                back.hasExitTime = true; back.exitTime = 0.85f; back.duration = 0.2f;
            }
            OneShot("Attack", rig.Attack, rig.AttackRate);
            OneShot("Alert", rig.Alert, 1f);
            EditorUtility.SetDirty(ctrl);
            return ctrl;
        }

        /// <summary>
        /// The asset model of an enemy under parent ("Model", facing +Z, origin as exported). Rigged assets get an Animator
        /// with their controller (no root motion). Null when the FBX is missing.
        /// </summary>
        static GameObject EnemyModel(Transform parent, string asset, out Animator anim)
        {
            anim = null;
            string path = EnemyModelDir + "/" + asset + ".fbx";
            if (!(AssetImporter.GetAtPath(path) is ModelImporter imp)) return null;
            AnimatorController ctrl = null;
            if (EnemyRigs.TryGetValue(asset, out var rig))
                ctrl = EnemyController(asset, rig, PrepareEnemyRig(path, rig));
            else if (imp.importAnimation || imp.animationType != ModelImporterAnimationType.None || imp.importTangents != ModelImporterTangents.CalculateMikk
                     || imp.importCameras || imp.importLights)
            {
                imp.importAnimation = false;
                imp.animationType = ModelImporterAnimationType.None;
                imp.importTangents = ModelImporterTangents.CalculateMikk;
                imp.importCameras = imp.importLights = false;
                imp.SaveAndReimport();
            }
            RemapMaterials(imp, EnemyMaterials(asset));
            var src = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            if (src == null) return null;
            var go = (GameObject)Object.Instantiate(src, parent, false);
            go.name = "Model";
            go.transform.localPosition = Vector3.zero;
            go.transform.localRotation = Quaternion.identity;
            go.transform.localScale = Vector3.one;
            if (ctrl != null)
            {
                anim = go.GetComponent<Animator>();
                if (anim == null) anim = go.AddComponent<Animator>();
                anim.avatar = AssetDatabase.LoadAllAssetsAtPath(path).OfType<Avatar>().FirstOrDefault();
                anim.runtimeAnimatorController = ctrl;
                anim.applyRootMotion = false;
                anim.cullingMode = AnimatorCullingMode.CullUpdateTransforms;
            }
            foreach (var r in go.GetComponentsInChildren<SkinnedMeshRenderer>(true))
            {
                r.updateWhenOffscreen = false;
                r.skinnedMotionVectors = false;
            }
            return go;
        }
    }
}
