using System.Collections.Generic;
using System.IO;
using System.Linq;
using Hindsight.Playback;
using UnityEditor;
using UnityEditor.Animations;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace Hindsight.EditorTools
{
    /// <summary>
    /// Turns the rigged player model and the Mixamo locomotion clips into PlayerAnimated.prefab
    /// and points the scene's HindsightApp at it. Safe to re-run: it rewrites the import settings,
    /// the controller and the prefab. Hindsight > Setup Scene builds it when it is missing.
    ///
    /// Clips are matched by file name (see Slots). Each is placed in a 2D blend tree at its own
    /// ground speed when the clip has root motion; "In Place" clips have none, so they get the
    /// default speeds below. Diagonal clips are mirrored for the other side.
    /// </summary>
    public static class AnimatedPlayerSetup
    {
        const string PlayersFolder = "Assets/Art/Players";
        const string ModelsFolder = PlayersFolder + "/Models";
        const string AnimationsFolder = PlayersFolder + "/Animations";
        const string ControllerPath = PlayersFolder + "/PlayerLocomotion.controller";
        const string JerseyMaterialPath = PlayersFolder + "/Jersey.mat";
        const string PrefabPath = "Assets/Prefabs/PlayerAnimated.prefab";
        const string LitShader = "Universal Render Pipeline/Lit";

        /// <summary>A typical NFL player is about 6'2" (2.05 yd).</summary>
        const float PlayerHeight = 2.05f;
        /// <summary>Meshes whose name contains one of these take the team color.</summary>
        static readonly string[] JerseyNames = { "shirt", "jersey" };

        /// <summary>Clip file name, and where it sits (x = right, y = forward, yd/s) if it has no root motion.</summary>
        static readonly (string file, Vector2 fallback, bool mirror)[] Slots =
        {
            ("Idle", Vector2.zero, false),
            ("Jogging", new Vector2(0f, 3.5f), false),
            ("Fast Run", new Vector2(0f, 7.5f), false),
            ("Jog Backward", new Vector2(0f, -3f), false),
            ("Jog Forward Diagonal", new Vector2(2.5f, 2.5f), true),
            ("Jog Backward Diagonal", new Vector2(2.2f, -2.2f), true),
        };

        [MenuItem("Hindsight/Setup Animated Player")]
        public static void Setup()
        {
            PlayerView view = Build();
            if (view != null) UsePrefab(view);
        }

        /// <summary>The prefab Setup Scene uses: the existing one, or built now.</summary>
        internal static PlayerView LoadOrBuild()
        {
            var view = AssetDatabase.LoadAssetAtPath<PlayerView>(PrefabPath);
            return view != null ? view : Build();
        }

        static PlayerView Build()
        {
            string modelPath = FindModel();
            if (modelPath == null)
            {
                EditorUtility.DisplayDialog("Animated player",
                    $"No .fbx found under {ModelsFolder}. Put a rigged (humanoid) character there and run this again.", "OK");
                return null;
            }

            Avatar avatar = ImportModel(modelPath);
            if (avatar == null) return null;

            var clips = new List<(AnimationClip clip, Vector2 fallback, bool mirror)>();
            foreach (var slot in Slots)
            {
                string path = $"{AnimationsFolder}/{slot.file}.fbx";
                if (!File.Exists(path))
                {
                    Debug.LogWarning($"[Hindsight] Missing animation {path}; the blend tree is built without it.");
                    continue;
                }
                AnimationClip clip = ImportClip(path, slot.file);
                if (clip != null) clips.Add((clip, slot.fallback, slot.mirror));
            }
            if (!clips.Any(c => c.clip.name == "Idle"))
            {
                Debug.LogError("[Hindsight] Idle.fbx is required (it is the blend tree's center).");
                return null;
            }

            GameObject prefab = BuildPrefab(modelPath, clips, out string report);
            if (prefab == null) return null;
            Debug.Log($"[Hindsight] Animated player ready ({PrefabPath}).\n{report}");
            return prefab.GetComponent<PlayerView>();
        }

        static string FindModel() =>
            Directory.Exists(ModelsFolder)
                ? Directory.GetFiles(ModelsFolder, "*.fbx", SearchOption.AllDirectories)
                    .Select(p => p.Replace('\\', '/')).OrderBy(p => p).FirstOrDefault()
                : null;

        static Avatar ImportModel(string path)
        {
            var importer = (ModelImporter)AssetImporter.GetAtPath(path);
            importer.animationType = ModelImporterAnimationType.Human;
            importer.avatarSetup = ModelImporterAvatarSetup.CreateFromThisModel;
            importer.importAnimation = false;
            importer.materialImportMode = ModelImporterMaterialImportMode.ImportViaMaterialDescription;
            importer.materialSearch = ModelImporterMaterialSearch.RecursiveUp;
            importer.SaveAndReimport();

            Avatar avatar = AssetDatabase.LoadAllAssetsAtPath(path).OfType<Avatar>().FirstOrDefault();
            if (avatar == null || !avatar.isValid || !avatar.isHuman)
            {
                Debug.LogError($"[Hindsight] {path} did not map to a humanoid skeleton. Select it, open Rig > " +
                               "Configure, fix the red bones, Apply, then run this again.");
                return null;
            }
            return avatar;
        }

        /// <summary>Humanoid, looping, body facing kept on the root. Root motion is extracted rather
        /// than baked, so a clip that runs forward reports its speed and still stays in place
        /// (the tracking data moves the player, not the animation).</summary>
        static AnimationClip ImportClip(string path, string clipName)
        {
            var importer = (ModelImporter)AssetImporter.GetAtPath(path);
            importer.animationType = ModelImporterAnimationType.Human;
            // Mixamo downloads name bones "mixamorig:Hips"; Fuse models often use "mixamorig_Hips".
            // Each clip gets its own avatar and humanoid retargeting bridges the two.
            importer.avatarSetup = ModelImporterAvatarSetup.CreateFromThisModel;
            importer.importAnimation = true;
            importer.materialImportMode = ModelImporterMaterialImportMode.None;

            ModelImporterClipAnimation[] takes = importer.defaultClipAnimations;
            if (takes.Length == 0)
            {
                Debug.LogWarning($"[Hindsight] {path} has no animation.");
                return null;
            }
            ModelImporterClipAnimation take = takes[0];
            take.name = clipName;
            take.loopTime = true;
            take.lockRootRotation = false;
            take.lockRootHeightY = true;
            take.lockRootPositionXZ = false;
            take.keepOriginalOrientation = true;
            take.keepOriginalPositionY = true;
            take.keepOriginalPositionXZ = true;
            importer.clipAnimations = new[] { take };
            importer.SaveAndReimport();

            return AssetDatabase.LoadAllAssetsAtPath(path).OfType<AnimationClip>()
                .FirstOrDefault(c => !c.name.StartsWith("__preview__"));
        }

        static GameObject BuildPrefab(string modelPath, List<(AnimationClip clip, Vector2 fallback, bool mirror)> clips,
            out string report)
        {
            report = "";
            HindsightSetup.EnsureFolder("Assets/Prefabs");
            var root = new GameObject("PlayerAnimated");
            var view = root.AddComponent<PlayerView>();

            // A plain copy, not a linked instance: loose meshes get re-parented onto bones below.
            var model = Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>(modelPath), root.transform);
            model.name = "Model";
            var animator = model.GetComponent<Animator>();
            if (animator == null) animator = model.AddComponent<Animator>();
            animator.applyRootMotion = false;
            animator.cullingMode = AnimatorCullingMode.CullUpdateTransforms;

            // Stand the model on the turf at player height.
            Bounds bounds = RendererBounds(model);
            if (bounds.size.y < 1e-4f)
            {
                Object.DestroyImmediate(root);
                Debug.LogError($"[Hindsight] {modelPath} has no visible meshes.");
                return null;
            }
            model.transform.localScale *= PlayerHeight / bounds.size.y;
            bounds = RendererBounds(model);
            model.transform.localPosition -= new Vector3(bounds.center.x, bounds.min.y, bounds.center.z);

            report += AttachLooseMeshes(model, animator);

            var renderers = model.GetComponentsInChildren<Renderer>(true);
            foreach (Renderer r in renderers)
            {
                r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                r.receiveShadows = false;
            }
            Renderer[] jersey = renderers.Where(r => JerseyNames.Any(n => r.name.ToLowerInvariant().Contains(n))).ToArray();
            Material jerseyMaterial = EnsureJerseyMaterial();
            foreach (Renderer r in jersey)
                r.sharedMaterials = Enumerable.Repeat(jerseyMaterial, r.sharedMaterials.Length).ToArray();
            report += jersey.Length > 0
                ? $"Team color on: {string.Join(", ", jersey.Select(r => r.name))}\n"
                : $"No mesh named like {string.Join("/", JerseyNames)}; players are not team-colored. Add the jersey renderer to PlayerView > Jersey Renderers.\n";

            // Humanoid root motion is normalized to hip height, so hip height in yards converts it.
            Transform hips = animator.GetBoneTransform(HumanBodyBones.Hips);
            float hipHeight = hips != null && hips.position.y > 0.1f ? hips.position.y : PlayerHeight * 0.53f;
            AnimatorController controller = BuildController(clips, hipHeight, out float topSpeed, out string speeds);
            animator.runtimeAnimatorController = controller;
            report += speeds;

            var label = HindsightSetup.CreateNumberLabel(root.transform, PlayerHeight + 0.35f);
            var so = new SerializedObject(view);
            HindsightSetup.SetArray(so.FindProperty("jerseyRenderers"), jersey.Cast<Object>().ToArray());
            HindsightSetup.SetArray(so.FindProperty("trimRenderers"));
            so.FindProperty("numberLabel").objectReferenceValue = label;
            so.FindProperty("eyeHeight").floatValue = PlayerHeight * 0.92f;
            so.FindProperty("animator").objectReferenceValue = animator;
            so.FindProperty("topClipSpeed").floatValue = topSpeed;
            so.ApplyModifiedPropertiesWithoutUndo();

            int triangles = model.GetComponentsInChildren<SkinnedMeshRenderer>(true).Sum(r => Triangles(r.sharedMesh))
                + model.GetComponentsInChildren<MeshFilter>(true).Sum(f => Triangles(f.sharedMesh));
            report += $"Model: {triangles:N0} triangles per player ({triangles * 22L:N0} for 22).";

            return HindsightSetup.SavePrefab(root, PrefabPath);
        }

        /// <summary>Meshes that are not skinned (helmets and pads often aren't) would stay put while
        /// the body animates; parent each to the nearest humanoid bone so it follows.</summary>
        static string AttachLooseMeshes(GameObject model, Animator animator)
        {
            var bones = new List<Transform>();
            for (var b = HumanBodyBones.Hips; b < HumanBodyBones.LastBone; b++)
            {
                Transform t = animator.GetBoneTransform(b);
                if (t != null) bones.Add(t);
            }
            if (bones.Count == 0) return "";

            var moved = new List<string>();
            foreach (MeshRenderer r in model.GetComponentsInChildren<MeshRenderer>(true))
            {
                if (bones.Any(b => r.transform.IsChildOf(b))) continue;
                Vector3 center = r.bounds.center;
                Transform nearest = bones.OrderBy(b => (b.position - center).sqrMagnitude).First();
                r.transform.SetParent(nearest, true);
                moved.Add($"{r.name} -> {nearest.name}");
            }
            return moved.Count > 0 ? $"Attached unskinned meshes: {string.Join(", ", moved)}\n" : "";
        }

        static AnimatorController BuildController(List<(AnimationClip clip, Vector2 fallback, bool mirror)> clips,
            float hipHeight, out float topSpeed, out string report)
        {
            AssetDatabase.DeleteAsset(ControllerPath);
            var controller = AnimatorController.CreateAnimatorControllerAtPath(ControllerPath);
            controller.AddParameter("Strafe", AnimatorControllerParameterType.Float);
            controller.AddParameter("Forward", AnimatorControllerParameterType.Float);
            controller.CreateBlendTreeInController("Locomotion", out BlendTree tree, 0);
            tree.blendType = BlendTreeType.FreeformDirectional2D;
            tree.blendParameter = "Strafe";
            tree.blendParameterY = "Forward";

            var lines = new List<string>();
            var children = new List<ChildMotion>();
            topSpeed = 0f;
            foreach (var (clip, fallback, mirror) in clips)
            {
                Vector3 v = clip.averageSpeed * hipHeight;
                bool measured = clip.name != "Idle" && new Vector2(v.x, v.z).magnitude > 0.3f;
                Vector2 at = measured ? new Vector2(v.x, v.z) : fallback;
                lines.Add($"  {clip.name}: ({at.x:0.0}, {at.y:0.0}) yd/s{(measured || clip.name == "Idle" ? "" : " (default; clip is in place)")}");
                children.Add(new ChildMotion { motion = clip, position = at, timeScale = 1f });
                if (mirror && Mathf.Abs(at.x) > 0.1f)
                    children.Add(new ChildMotion { motion = clip, position = new Vector2(-at.x, at.y), timeScale = 1f, mirror = true });
                topSpeed = Mathf.Max(topSpeed, at.magnitude);
            }
            tree.children = children.ToArray();
            AssetDatabase.SaveAssets();

            report = "Blend tree (x = right, y = forward):\n" + string.Join("\n", lines) + "\n";
            if (topSpeed < 0.1f) topSpeed = 7.5f;
            return controller;
        }

        /// <summary>A plain white lit material, so the per-team tint shows as the jersey color.</summary>
        static Material EnsureJerseyMaterial()
        {
            var material = AssetDatabase.LoadAssetAtPath<Material>(JerseyMaterialPath);
            if (material != null) return material;
            material = new Material(Shader.Find(LitShader)) { color = Color.white };
            material.SetFloat("_Smoothness", 0.35f);
            AssetDatabase.CreateAsset(material, JerseyMaterialPath);
            return material;
        }

        static void UsePrefab(PlayerView view)
        {
            var app = Object.FindAnyObjectByType<HindsightApp>();
            if (app == null)
            {
                Debug.LogError("[Hindsight] No HindsightApp in the open scene. Run Hindsight > Setup Scene first.");
                return;
            }
            var so = new SerializedObject(app);
            so.FindProperty("playerPrefab").objectReferenceValue = view;
            so.ApplyModifiedProperties();
            EditorSceneManager.MarkSceneDirty(app.gameObject.scene);
            Debug.Log($"[Hindsight] Players now use {AssetDatabase.GetAssetPath(view)}. Save the scene to keep it.");
        }

        static Bounds RendererBounds(GameObject go)
        {
            Renderer[] renderers = go.GetComponentsInChildren<Renderer>(true);
            if (renderers.Length == 0) return default;
            Bounds b = renderers[0].bounds;
            foreach (Renderer r in renderers) b.Encapsulate(r.bounds);
            return b;
        }

        static int Triangles(Mesh mesh)
        {
            if (mesh == null) return 0;
            long n = 0;
            for (int i = 0; i < mesh.subMeshCount; i++) n += mesh.GetIndexCount(i);
            return (int)(n / 3);
        }
    }
}
