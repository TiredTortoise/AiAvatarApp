using System.IO;
using AiAvatarApp.App;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace AiAvatarApp.EditorScaffold
{
    /// <summary>
    /// One-shot scaffold helpers that can be invoked from the Tools menu or via
    /// <c>-executeMethod</c> in batch mode. These are idempotent: re-running them
    /// overwrites the generated artefacts so the scaffold is reproducible.
    /// </summary>
    public static class ProjectScaffold
    {
        private const string ScenesDir = "Assets/Scenes";
        private const string BootScenePath = "Assets/Scenes/Boot.unity";
        private const string MainScenePath = "Assets/Scenes/Main.unity";

        [MenuItem("Tools/AiAvatarApp/Run Full Scaffold")]
        public static void RunFullScaffold()
        {
            EnsureUrpPipeline();
            CreateScenes();
            ConfigurePlayerSettings();
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            Debug.Log("[Scaffold] Completed full scaffold.");
        }

        [MenuItem("Tools/AiAvatarApp/Ensure URP Pipeline Asset")]
        public static void EnsureUrpPipeline()
        {
            const string settingsDir = "Assets/Settings";
            const string pipelinePath = "Assets/Settings/URP-Pipeline.asset";
            const string rendererPath = "Assets/Settings/URP-Renderer.asset";

            Directory.CreateDirectory(settingsDir);

            var rendererData = AssetDatabase.LoadAssetAtPath<UniversalRendererData>(rendererPath);
            if (rendererData == null)
            {
                rendererData = ScriptableObject.CreateInstance<UniversalRendererData>();
                AssetDatabase.CreateAsset(rendererData, rendererPath);
            }

            var pipeline = AssetDatabase.LoadAssetAtPath<UniversalRenderPipelineAsset>(pipelinePath);
            if (pipeline == null)
            {
                pipeline = UniversalRenderPipelineAsset.Create(rendererData);
                AssetDatabase.CreateAsset(pipeline, pipelinePath);
            }

            GraphicsSettings.defaultRenderPipeline = pipeline;
            QualitySettings.renderPipeline = pipeline;

            Debug.Log("[Scaffold] URP pipeline asset ensured and assigned.");
        }

        [MenuItem("Tools/AiAvatarApp/Create Boot + Main Scenes")]
        public static void CreateScenes()
        {
            Directory.CreateDirectory(ScenesDir);
            CreateBootScene();
            CreateMainScene();
            UpdateBuildSettings();
            AssetDatabase.SaveAssets();
        }

        [MenuItem("Tools/AiAvatarApp/Configure Player Settings")]
        public static void ConfigurePlayerSettings()
        {
            PlayerSettings.companyName = "TortoiseTech";
            PlayerSettings.productName = "AiAvatarApp";

            PlayerSettings.SetScriptingBackend(NamedBuildTarget.Android, ScriptingImplementation.IL2CPP);
            PlayerSettings.Android.targetArchitectures = AndroidArchitecture.ARM64;
            PlayerSettings.Android.minSdkVersion = AndroidSdkVersions.AndroidApiLevel26;
            PlayerSettings.Android.targetSdkVersion = AndroidSdkVersions.AndroidApiLevelAuto;
            PlayerSettings.Android.forceInternetPermission = true;
            PlayerSettings.Android.optimizedFramePacing = true;

            Debug.Log("[Scaffold] Player settings configured. Note: 'Active Input Handling' is set " +
                      "via ProjectSettings.asset directly (it has no stable PlayerSettings API).");
        }

        private static void CreateBootScene()
        {
            Scene scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

            var go = new GameObject("Bootstrap");
            go.AddComponent<Bootstrap>();

            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene, BootScenePath);
        }

        private static void CreateMainScene()
        {
            Scene scene = EditorSceneManager.NewScene(NewSceneSetup.DefaultGameObjects, NewSceneMode.Single);

            var avatarRoot = new GameObject("AvatarRoot");
            avatarRoot.transform.position = Vector3.zero;

            // Construct with typeof(RectTransform) explicitly, then AddComponent<Canvas>() after.
            // A root ScreenSpaceOverlay Canvas *drives* its RectTransform (scale/anchors) to match
            // screen size at runtime — so whatever we serialize here is overwritten on load. The
            // serialized values in batch mode come out as (0,0,0) because there's no screen; this
            // is expected and the label still renders correctly.
            var canvasGo = new GameObject("PlaceholderCanvas", typeof(RectTransform));
            var canvasRect = canvasGo.GetComponent<RectTransform>();
            canvasRect.localScale = Vector3.one;
            canvasRect.anchorMin = Vector2.zero;
            canvasRect.anchorMax = Vector2.one;
            canvasRect.offsetMin = Vector2.zero;
            canvasRect.offsetMax = Vector2.zero;
            var canvas = canvasGo.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            var scaler = canvasGo.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1080, 1920);
            scaler.matchWidthOrHeight = 0.5f;
            canvasGo.AddComponent<GraphicRaycaster>();

            var labelGo = new GameObject("ScaffoldLabel", typeof(RectTransform), typeof(CanvasRenderer), typeof(Text));
            labelGo.transform.SetParent(canvasGo.transform, false);
            var rect = labelGo.GetComponent<RectTransform>();
            rect.anchorMin = new Vector2(0f, 0.5f);
            rect.anchorMax = new Vector2(1f, 0.5f);
            rect.offsetMin = new Vector2(40f, -60f);
            rect.offsetMax = new Vector2(-40f, 60f);
            var text = labelGo.GetComponent<Text>();
            text.text = "AI Avatar App \u2014 scaffold ready.";
            text.alignment = TextAnchor.MiddleCenter;
            text.fontSize = 48;
            text.color = Color.white;
            text.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");

            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene, MainScenePath);
        }

        private static void UpdateBuildSettings()
        {
            EditorBuildSettings.scenes = new[]
            {
                new EditorBuildSettingsScene(BootScenePath, enabled: true),
                new EditorBuildSettingsScene(MainScenePath, enabled: true),
            };
        }
    }
}
