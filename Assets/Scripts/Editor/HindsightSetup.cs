using Hindsight.Playback;
using Hindsight.UI;
using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace Hindsight.EditorTools
{
    /// <summary>
    /// Generates the field material, builds the player and ball prefabs from the models in
    /// Assets/Art (AnimatedPlayerSetup, BallModelSetup) and adds the app to the open scene,
    /// so nothing has to be hand-edited. Safe to re-run; existing prefabs are kept (re-run
    /// Setup Animated Player / Setup Football Model to rebuild them).
    /// </summary>
    public static class HindsightSetup
    {
        const string MaterialsFolder = "Assets/Art/Materials";
        const string UnlitPath = MaterialsFolder + "/Unlit.mat";
        const string UnlitShader = "Universal Render Pipeline/Unlit";

        [MenuItem("Hindsight/Setup Scene")]
        public static void SetupScene()
        {
            if (!EnsureTmpResources()) return;

            Material unlit = EnsureMaterial(UnlitPath, Color.white);
            if (unlit == null) return;

            // Built from the models in Assets/Art the first time; later runs reuse the prefabs.
            PlayerView player = AnimatedPlayerSetup.LoadOrBuild();
            BallView ball = BallModelSetup.LoadOrBuild();
            if (player == null || ball == null) return;
            GameObject goalPost = GoalPostSetup.LoadOrBuild(); // optional; the field is fine without

            AddAppToScene(unlit, player, ball, goalPost);
        }

        static bool EnsureTmpResources()
        {
            if (Resources.Load<TMP_Settings>("TMP Settings") != null) return true;
            EditorUtility.DisplayDialog("Hindsight setup",
                "TextMeshPro Essential Resources are not imported yet.\n\n" +
                "Use Window > TextMeshPro > Import TMP Essential Resources, then run Hindsight > Setup Scene again.",
                "OK");
            return false;
        }

        static Material EnsureMaterial(string path, Color color)
        {
            EnsureFolder(MaterialsFolder);
            var material = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (material != null) return material;

            Shader shader = Shader.Find(UnlitShader);
            if (shader == null)
            {
                Debug.LogError($"[Hindsight] Shader '{UnlitShader}' not found. Is URP installed and active?");
                return null;
            }
            material = new Material(shader) { color = color };
            AssetDatabase.CreateAsset(material, path);
            return material;
        }

        /// <summary>The jersey number floating above a player, always facing the camera.</summary>
        internal static TextMeshPro CreateNumberLabel(Transform parent, float height)
        {
            var label = new GameObject("Number", typeof(RectTransform));
            label.transform.SetParent(parent, false);
            label.transform.localPosition = new Vector3(0f, height, 0f);
            label.AddComponent<Billboard>();
            var text = label.AddComponent<TextMeshPro>();
            text.text = "00";
            text.fontSize = 5f;
            text.fontStyle = FontStyles.Bold;
            text.alignment = TextAlignmentOptions.Center;
            text.textWrappingMode = TextWrappingModes.NoWrap;
            text.rectTransform.sizeDelta = new Vector2(2f, 1f);
            return text;
        }

        static void AddAppToScene(Material unlit, PlayerView player, BallView ball, GameObject goalPost)
        {
            var app = Object.FindAnyObjectByType<HindsightApp>();
            if (app == null)
            {
                var go = new GameObject("Hindsight");
                Undo.RegisterCreatedObjectUndo(go, "Add Hindsight");
                app = go.AddComponent<HindsightApp>();
            }

            var so = new SerializedObject(app);
            so.FindProperty("unlitMaterial").objectReferenceValue = unlit;
            so.FindProperty("playerPrefab").objectReferenceValue = player;
            so.FindProperty("ballPrefab").objectReferenceValue = ball;
            so.FindProperty("goalPostPrefab").objectReferenceValue = goalPost;
            so.ApplyModifiedProperties();

            // The app creates its own cameras; remove other scene cameras so they don't render over it.
            foreach (var cam in Object.FindObjectsByType<Camera>(FindObjectsInactive.Include))
            {
                if (cam.transform.IsChildOf(app.transform)) continue;
                Debug.Log($"[Hindsight] Removed scene camera '{cam.name}' (the app creates its own).");
                Undo.DestroyObjectImmediate(cam.gameObject);
            }

            EditorSceneManager.MarkSceneDirty(app.gameObject.scene);
            Selection.activeGameObject = app.gameObject;
            Debug.Log("[Hindsight] Scene ready. Save the scene and press Play.");
        }

        internal static GameObject SavePrefab(GameObject root, string path)
        {
            GameObject prefab = PrefabUtility.SaveAsPrefabAsset(root, path);
            Object.DestroyImmediate(root);
            return prefab;
        }

        internal static void SetArray(SerializedProperty array, params Object[] values)
        {
            array.arraySize = values.Length;
            for (int i = 0; i < values.Length; i++)
                array.GetArrayElementAtIndex(i).objectReferenceValue = values[i];
        }

        internal static void EnsureFolder(string path)
        {
            if (AssetDatabase.IsValidFolder(path)) return;
            string parent = System.IO.Path.GetDirectoryName(path)?.Replace('\\', '/');
            if (!string.IsNullOrEmpty(parent)) EnsureFolder(parent);
            AssetDatabase.CreateFolder(parent, System.IO.Path.GetFileName(path));
        }
    }
}
