using System.Linq;
using Hindsight.Playback;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace Hindsight.EditorTools
{
    /// <summary>
    /// Turns the football model in Assets/Art/Ball into BallModel.prefab and points the scene's
    /// HindsightApp at it. Safe to re-run. Hindsight > Setup Scene builds it when it is missing.
    /// The model is scaled so its long axis is BallLength and turned so that axis is +z, which
    /// BallView points along the direction of travel.
    /// </summary>
    public static class BallModelSetup
    {
        const string ModelPath = "Assets/Art/Ball/Football.fbx";
        const string NormalMapPath = "Assets/Art/Ball/Textures/Footbal lNormal Map.png";
        const string PrefabPath = "Assets/Prefabs/BallModel.prefab";

        /// <summary>A real ball is 11 in (0.31 yd); about 1.6x so it reads from the broadcast camera.</summary>
        const float BallLength = 0.5f;

        [MenuItem("Hindsight/Setup Football Model")]
        public static void Setup()
        {
            BallView view = Build();
            if (view != null) UsePrefab(view);
        }

        /// <summary>The prefab Setup Scene uses: the existing one, or built now.</summary>
        internal static BallView LoadOrBuild()
        {
            var view = AssetDatabase.LoadAssetAtPath<BallView>(PrefabPath);
            return view != null ? view : Build();
        }

        static BallView Build()
        {
            var asset = AssetDatabase.LoadAssetAtPath<GameObject>(ModelPath);
            if (asset == null)
            {
                EditorUtility.DisplayDialog("Football model", $"{ModelPath} not found.", "OK");
                return null;
            }

            if (AssetImporter.GetAtPath(NormalMapPath) is TextureImporter normal && normal.textureType != TextureImporterType.NormalMap)
            {
                normal.textureType = TextureImporterType.NormalMap;
                normal.SaveAndReimport();
            }
            var importer = (ModelImporter)AssetImporter.GetAtPath(ModelPath);
            importer.animationType = ModelImporterAnimationType.None;
            importer.importAnimation = false;
            importer.materialImportMode = ModelImporterMaterialImportMode.ImportViaMaterialDescription;
            importer.SaveAndReimport();

            HindsightSetup.EnsureFolder("Assets/Prefabs");
            var root = new GameObject("BallModel");
            root.AddComponent<BallView>();
            var model = Object.Instantiate(asset, root.transform);
            model.name = "Model";
            foreach (Renderer r in model.GetComponentsInChildren<Renderer>(true))
            {
                r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                r.receiveShadows = false;
            }

            Bounds b = Bounds(model);
            if (b.size.x > b.size.z) model.transform.localRotation = Quaternion.Euler(0f, 90f, 0f) * model.transform.localRotation;
            b = Bounds(model);
            model.transform.localScale *= BallLength / Mathf.Max(b.size.z, 1e-4f);
            b = Bounds(model);
            model.transform.localPosition -= b.center;

            var view = HindsightSetup.SavePrefab(root, PrefabPath).GetComponent<BallView>();
            int triangles = asset.GetComponentsInChildren<MeshFilter>(true).Sum(f => f.sharedMesh == null ? 0 : (int)(f.sharedMesh.triangles.Length / 3));
            Debug.Log($"[Hindsight] Football ready ({PrefabPath}): {BallLength} yd long, {triangles:N0} triangles.");
            return view;
        }

        static void UsePrefab(BallView view)
        {
            var app = Object.FindAnyObjectByType<HindsightApp>();
            if (app == null)
            {
                Debug.LogError("[Hindsight] No HindsightApp in the open scene. Run Hindsight > Setup Scene first.");
                return;
            }
            var so = new SerializedObject(app);
            so.FindProperty("ballPrefab").objectReferenceValue = view;
            so.ApplyModifiedProperties();
            EditorSceneManager.MarkSceneDirty(app.gameObject.scene);
            Debug.Log($"[Hindsight] Ball now uses {AssetDatabase.GetAssetPath(view)}. Save the scene to keep it.");
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
