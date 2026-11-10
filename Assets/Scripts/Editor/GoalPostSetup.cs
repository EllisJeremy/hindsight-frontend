using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace Hindsight.EditorTools
{
    /// <summary>
    /// Turns Assets/Art/GoalPost/GoalPost.fbx into GoalPost.prefab and points the scene's
    /// HindsightApp at it; FieldBuilder puts one on each end line. Safe to re-run. The prefab's
    /// origin is on the turf in the uprights' plane, crossbar along z, base behind toward -x.
    /// The FBX is already NFL size (converted from a Blender file, see CREDITS.md): uprights
    /// 18'6" apart, crossbar 10 ft up, uprights 35 ft above it. Height is re-applied here in
    /// case the importer's unit conversion changes.
    /// </summary>
    public static class GoalPostSetup
    {
        const string ModelPath = "Assets/Art/GoalPost/GoalPost.fbx";
        const string PrefabPath = "Assets/Prefabs/GoalPost.prefab";
        /// <summary>Top of the uprights: 10 ft crossbar + 35 ft uprights (yards).</summary>
        const float Height = 15f;

        [MenuItem("Hindsight/Setup Goal Post Model")]
        public static void Setup()
        {
            GameObject prefab = Build();
            if (prefab == null) return;
            var app = Object.FindAnyObjectByType<HindsightApp>();
            if (app == null)
            {
                Debug.LogError("[Hindsight] No HindsightApp in the open scene. Run Hindsight > Setup Scene first.");
                return;
            }
            var so = new SerializedObject(app);
            so.FindProperty("goalPostPrefab").objectReferenceValue = prefab;
            so.ApplyModifiedProperties();
            EditorSceneManager.MarkSceneDirty(app.gameObject.scene);
            Debug.Log($"[Hindsight] Goal posts now use {PrefabPath}. Save the scene to keep it.");
        }

        /// <summary>The prefab Setup Scene uses: the existing one, built now, or null without the model.</summary>
        internal static GameObject LoadOrBuild()
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath);
            return prefab != null ? prefab : Build();
        }

        static GameObject Build()
        {
            var asset = AssetDatabase.LoadAssetAtPath<GameObject>(ModelPath);
            if (asset == null)
            {
                Debug.LogWarning($"[Hindsight] {ModelPath} not found; the field has no goal posts.");
                return null;
            }
            var importer = (ModelImporter)AssetImporter.GetAtPath(ModelPath);
            importer.animationType = ModelImporterAnimationType.None;
            importer.importAnimation = false;
            importer.materialImportMode = ModelImporterMaterialImportMode.ImportViaMaterialDescription;
            importer.SaveAndReimport();

            HindsightSetup.EnsureFolder("Assets/Prefabs");
            var root = new GameObject("GoalPost");
            var model = Object.Instantiate(asset, root.transform);
            model.name = "Model";
            foreach (Renderer r in model.GetComponentsInChildren<Renderer>(true))
            {
                r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                r.receiveShadows = false;
            }

            // Crossbar along z (across the field); then the base, which sits behind the uprights,
            // toward -x. The FBX origin is in the uprights' plane, so it stays on the end line.
            Bounds b = Bounds(model);
            if (b.size.x > b.size.z) model.transform.localRotation = Quaternion.Euler(0f, 90f, 0f) * model.transform.localRotation;
            b = Bounds(model);
            if (b.center.x > 0f) model.transform.localRotation = Quaternion.Euler(0f, 180f, 0f) * model.transform.localRotation;
            b = Bounds(model);
            model.transform.localScale *= Height / Mathf.Max(b.size.y, 1e-4f);
            b = Bounds(model);
            model.transform.localPosition -= new Vector3(0f, b.min.y, 0f);

            GameObject prefab = HindsightSetup.SavePrefab(root, PrefabPath);
            Debug.Log($"[Hindsight] Goal post ready ({PrefabPath}).");
            return prefab;
        }

        static Bounds Bounds(GameObject go)
        {
            Renderer[] renderers = go.GetComponentsInChildren<Renderer>(true);
            if (renderers.Length == 0) return default;
            Bounds b = renderers[0].bounds;
            foreach (Renderer r in renderers) b.Encapsulate(r.bounds);
            return b;
        }
    }
}
