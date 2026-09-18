using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.Animations;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Animations;
using UnityEngine.Playables;
using UnityEngine.SceneManagement;

namespace Qusap.NewQusap75KAnimationTest.Editor
{
    [InitializeOnLoad]
    internal static class Qusap75KPlayModeCompletion
    {
        private const string PendingKey = "Qusap.NewQusap75KAnimationTest.PlayModePending";
        private const string ProbeObjectName = "Qusap75K_PlayModeProbe_Transient";
        private const string ReportAssetPath = "Assets/_Qusap/Tests/NewQusap75KAnimationTest/Validation/PlayModeValidation.json";

        static Qusap75KPlayModeCompletion()
        {
            EditorApplication.playModeStateChanged += OnPlayModeStateChanged;
        }

        internal static void MarkPending()
        {
            SessionState.SetBool(PendingKey, true);
        }

        private static void OnPlayModeStateChanged(PlayModeStateChange state)
        {
            if (state != PlayModeStateChange.EnteredEditMode || !SessionState.GetBool(PendingKey, false))
            {
                return;
            }

            SessionState.SetBool(PendingKey, false);
            GameObject probeObject = GameObject.Find(ProbeObjectName);
            if (probeObject != null)
            {
                UnityEngine.Object.DestroyImmediate(probeObject);
            }
            EditorSceneManager.OpenScene(
                "Assets/_Qusap/Tests/NewQusap75KAnimationTest/Scene/Qusap75K_RollTest.unity",
                OpenSceneMode.Single);
            AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);

            string relative = ReportAssetPath.Substring("Assets".Length).TrimStart('/', '\\');
            string absolute = Path.GetFullPath(Path.Combine(Application.dataPath, relative));
            bool passed = false;
            string reportJson = string.Empty;
            if (File.Exists(absolute))
            {
                reportJson = File.ReadAllText(absolute);
                Qusap.NewQusap75KAnimationTest.PlayModeValidationReport report =
                    JsonUtility.FromJson<Qusap.NewQusap75KAnimationTest.PlayModeValidationReport>(reportJson);
                passed = report != null && report.passed;
                if (passed)
                {
                    ApplyStaticSceneFraming(report);
                }
            }

            if (passed)
            {
                Debug.Log("QUSAP_PLAY_MODE_TEST_PASSED " + reportJson);
            }
            else
            {
                Debug.LogError("QUSAP_PLAY_MODE_TEST_FAILED " + reportJson);
            }

            if (Application.isBatchMode)
            {
                EditorApplication.Exit(passed ? 0 : 1);
            }
        }

        private static void ApplyStaticSceneFraming(
            Qusap.NewQusap75KAnimationTest.PlayModeValidationReport report)
        {
            Scene scene = SceneManager.GetActiveScene();
            Camera camera = scene.GetRootGameObjects()
                .Select(root => root.GetComponent<Camera>())
                .FirstOrDefault(candidate => candidate != null);
            GameObject ground = scene.GetRootGameObjects()
                .FirstOrDefault(root => root.name == "VisualGround_NoCollider");
            if (camera == null)
            {
                return;
            }

            Vector3 center = report.sweptBoneBoundsCenter;
            Vector3 size = report.sweptBoneBoundsSize;
            float height = Mathf.Max(0.5f, size.y);
            float width = Mathf.Max(0.5f, size.z);
            float verticalHalfExtent = Mathf.Max(
                height * 0.5f,
                width * 0.5f / Mathf.Max(0.1f, camera.aspect));
            camera.orthographic = true;
            camera.orthographicSize = verticalHalfExtent * 2.1f;
            camera.transform.position = center + Vector3.right * Mathf.Max(2f, height * 3f);
            camera.transform.rotation = Quaternion.LookRotation(center - camera.transform.position, Vector3.up);

            if (ground != null)
            {
                Vector3 groundPosition = ground.transform.position;
                groundPosition.z = center.z;
                ground.transform.position = groundPosition;
                float requiredScale = Mathf.Max(0.25f, width * 0.3f);
                Vector3 groundScale = ground.transform.localScale;
                groundScale.x = Mathf.Max(groundScale.x, requiredScale);
                groundScale.z = Mathf.Max(groundScale.z, requiredScale);
                ground.transform.localScale = groundScale;
            }

            EditorSceneManager.SaveScene(scene,
                "Assets/_Qusap/Tests/NewQusap75KAnimationTest/Scene/Qusap75K_RollTest.unity");
        }
    }

    [InitializeOnLoad]
    internal static class Qusap75KAnimationTestAutoRunner
    {
        private const string TriggerPath = "Assets/_Qusap/Tests/NewQusap75KAnimationTest/Validation/RunRequested.txt";
        private const string ResultPath = "Assets/_Qusap/Tests/NewQusap75KAnimationTest/Validation/RunResult.txt";
        private const string FinalSceneTriggerPath = "Assets/_Qusap/Tests/NewQusap75KAnimationTest/Validation/FinalSceneRefreshRequested.txt";
        private const string FinalSceneResultPath = "Assets/_Qusap/Tests/NewQusap75KAnimationTest/Validation/FinalSceneRefreshResult.txt";

        static Qusap75KAnimationTestAutoRunner()
        {
            EditorApplication.delayCall += RunWhenReady;
        }

        private static void RunWhenReady()
        {
            if (Application.isBatchMode)
            {
                return;
            }

            string triggerAbsolutePath = ToAbsolutePath(TriggerPath);
            string finalSceneTriggerAbsolutePath = ToAbsolutePath(FinalSceneTriggerPath);
            bool buildRequested = File.Exists(triggerAbsolutePath);
            bool finalSceneRefreshRequested = File.Exists(finalSceneTriggerAbsolutePath);
            if (!buildRequested && !finalSceneRefreshRequested)
            {
                return;
            }

            if (EditorApplication.isCompiling || EditorApplication.isUpdating)
            {
                EditorApplication.delayCall += RunWhenReady;
                return;
            }

            if (finalSceneRefreshRequested)
            {
                string finalResultAbsolutePath = ToAbsolutePath(FinalSceneResultPath);
                File.Delete(finalSceneTriggerAbsolutePath);
                if (EditorApplication.isPlayingOrWillChangePlaymode)
                {
                    File.WriteAllText(finalResultAbsolutePath, "BLOCKED: Unity is in or entering Play Mode.");
                    return;
                }

                Scene sceneBeforeRefresh = SceneManager.GetActiveScene();
                if (sceneBeforeRefresh.IsValid() && sceneBeforeRefresh.isDirty)
                {
                    File.WriteAllText(finalResultAbsolutePath,
                        "BLOCKED: The currently open scene has unsaved changes. Nothing was reloaded.");
                    return;
                }

                AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
                EditorSceneManager.OpenScene(
                    "Assets/_Qusap/Tests/NewQusap75KAnimationTest/Scene/Qusap75K_RollTest.unity",
                    OpenSceneMode.Single);
                File.WriteAllText(finalResultAbsolutePath, "PASSED");
                AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
                Debug.Log("QUSAP_FINAL_SCENE_REFRESH_PASSED");
                return;
            }

            string resultAbsolutePath = ToAbsolutePath(ResultPath);
            File.Delete(triggerAbsolutePath);

            if (EditorApplication.isPlayingOrWillChangePlaymode)
            {
                File.WriteAllText(resultAbsolutePath, "BLOCKED: Unity is in or entering Play Mode.");
                Debug.LogError("QUSAP_ISOLATED_TEST_BLOCKED Unity is in or entering Play Mode.");
                return;
            }

            Scene activeScene = SceneManager.GetActiveScene();
            if (activeScene.IsValid() && activeScene.isDirty)
            {
                File.WriteAllText(resultAbsolutePath,
                    "BLOCKED: The currently open scene has unsaved changes. No scene was opened or modified.");
                Debug.LogError("QUSAP_ISOLATED_TEST_BLOCKED The currently open scene has unsaved changes.");
                return;
            }

            try
            {
                Qusap75KAnimationTestBuilder.ConfigureAndValidateAvatars();
                Qusap75KAnimationTestBuilder.BuildAndValidateIsolatedTest();
                File.WriteAllText(resultAbsolutePath, "PASSED");
                AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
                Debug.Log("QUSAP_ISOLATED_TEST_AUTORUN_PASSED");
            }
            catch (Exception exception)
            {
                File.WriteAllText(resultAbsolutePath, "FAILED: " + exception);
                AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
                Debug.LogException(exception);
            }
        }

        private static string ToAbsolutePath(string assetPath)
        {
            string relative = assetPath.Substring("Assets".Length).TrimStart('/', '\\');
            return Path.GetFullPath(Path.Combine(Application.dataPath, relative));
        }
    }

    public static class Qusap75KAnimationTestBuilder
    {
        private const string Root = "Assets/_Qusap/Tests/NewQusap75KAnimationTest";
        private const string CharacterPath = Root + "/Character/Qusap75K_Rigged_Test_v2.fbx";
        private const string AnimationPath = Root + "/Animation/RM_Roll_front.fbx";
        private const string CharacterMaterialPath = Root + "/Materials/Qusap75K_Test_Neutral.mat";
        private const string GroundMaterialPath = Root + "/Materials/Qusap75K_Test_Ground.mat";
        private const string ControllerPath = Root + "/Controller/Qusap75K_RollTest.controller";
        private const string PrefabPath = Root + "/Qusap75K_AnimationTest.prefab";
        private const string ScenePath = Root + "/Scene/Qusap75K_RollTest.unity";
        private const string AvatarReportPath = Root + "/Validation/AvatarValidation.json";
        private const string FinalReportPath = Root + "/Validation/FinalValidation.json";
        private const string ScreenshotDirectory = Root + "/Validation/Screenshots";

        private static readonly string[] RequiredHumanBones =
        {
            "Hips", "Spine", "Neck", "Head",
            "LeftUpperArm", "RightUpperArm", "LeftLowerArm", "RightLowerArm",
            "LeftHand", "RightHand", "LeftUpperLeg", "RightUpperLeg",
            "LeftLowerLeg", "RightLowerLeg", "LeftFoot", "RightFoot"
        };

        [Serializable]
        private sealed class AvatarResult
        {
            public string assetPath;
            public bool isValid;
            public bool isHuman;
            public List<string> mappedHumanBones = new List<string>();
            public List<string> missingRequiredBones = new List<string>();
            public string optionalChestMapping;
            public string error;
        }

        [Serializable]
        private sealed class AvatarValidationReport
        {
            public string generatedAtUtc;
            public AvatarResult qusapAvatar;
            public AvatarResult animationAvatar;
            public List<string> animationClips = new List<string>();
            public bool passed;
        }

        [Serializable]
        private sealed class PoseSample
        {
            public string label;
            public float time;
            public string rootPosition;
            public string boundsCenter;
            public string boundsSize;
            public bool finiteBounds;
            public string screenshot;
        }

        [Serializable]
        private sealed class FinalValidationReport
        {
            public string generatedAtUtc;
            public string characterAsset;
            public string animationAsset;
            public string clipName;
            public float clipLengthSeconds;
            public float clipFrameRate;
            public int approximateFrameCount;
            public bool loopTime;
            public bool loopPose;
            public bool bakeRootRotation;
            public bool bakeRootPositionY;
            public bool bakeRootPositionXZ;
            public bool qusapAvatarValid;
            public bool animationAvatarValid;
            public bool animatorApplyRootMotion;
            public bool rootStayedFixed;
            public float maximumBoundsGrowthRatio;
            public bool grossStretchDetected;
            public string materialStatus;
            public string controllerAsset;
            public string prefabAsset;
            public string sceneAsset;
            public List<string> sceneRootObjects = new List<string>();
            public List<PoseSample> poseSamples = new List<PoseSample>();
            public List<string> notes = new List<string>();
            public bool passed;
        }

        public static void ConfigureAndValidateAvatars()
        {
            EnsureRequiredAsset(CharacterPath);
            EnsureRequiredAsset(AnimationPath);

            ConfigureModelImporter(CharacterPath, false);
            ConfigureModelImporter(AnimationPath, true);

            AvatarResult character = InspectAvatar(CharacterPath);
            AvatarResult animation = InspectAvatar(AnimationPath);
            List<AnimationClip> clips = LoadUsableClips(AnimationPath);

            var report = new AvatarValidationReport
            {
                generatedAtUtc = DateTime.UtcNow.ToString("O"),
                qusapAvatar = character,
                animationAvatar = animation,
                animationClips = clips.Select(clip => clip.name).ToList(),
                passed = AvatarPasses(character) && AvatarPasses(animation) && clips.Count > 0
            };

            WriteJsonAsset(AvatarReportPath, report);
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);

            if (!report.passed)
            {
                throw new InvalidOperationException(
                    "Humanoid validation failed. No prefab, controller, material, or scene was created. " +
                    "See " + AvatarReportPath);
            }

            Debug.Log("QUSAP_AVATAR_VALIDATION_PASSED " + JsonUtility.ToJson(report));
        }

        public static void BuildAndValidateIsolatedTest()
        {
            AvatarResult characterResult = InspectAvatar(CharacterPath);
            AvatarResult animationResult = InspectAvatar(AnimationPath);
            List<AnimationClip> clips = LoadUsableClips(AnimationPath);

            if (!AvatarPasses(characterResult) || !AvatarPasses(animationResult) || clips.Count == 0)
            {
                throw new InvalidOperationException(
                    "Build refused because one or both Humanoid Avatars are invalid, or the animation has no usable clip.");
            }

            AnimationClip clip = SelectRollClip(clips);
            ModelImporter animationImporter = AssetImporter.GetAtPath(AnimationPath) as ModelImporter;
            ModelImporterClipAnimation clipSettings = FindClipSettings(animationImporter, clip.name);
            if (clipSettings == null)
            {
                throw new InvalidOperationException("No importer clip settings were found for " + clip.name);
            }

            Material characterMaterial = CreateOrReplaceMaterial(
                CharacterMaterialPath,
                new Color(0.72f, 0.75f, 0.78f, 1f),
                0.15f);
            Material groundMaterial = CreateOrReplaceMaterial(
                GroundMaterialPath,
                new Color(0.12f, 0.16f, 0.20f, 1f),
                0f);

            AnimatorController controller = CreateController(clip);
            GameObject prefab = CreatePrefab(controller, characterMaterial);
            CreateScene(prefab, groundMaterial);
            FinalValidationReport report = ValidateAndCapture(
                characterResult,
                animationResult,
                clip,
                clipSettings);

            WriteJsonAsset(FinalReportPath, report);
            EditorSceneManager.SaveOpenScenes();
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);

            if (!report.passed)
            {
                throw new InvalidOperationException("Isolated test validation failed. See " + FinalReportPath);
            }

            Debug.Log("QUSAP_ISOLATED_TEST_PASSED " + JsonUtility.ToJson(report));
        }

        public static void RunPlayModeValidation()
        {
            Scene scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
            if (!scene.IsValid())
            {
                throw new InvalidOperationException("The isolated test scene could not be opened.");
            }

            GameObject character = scene.GetRootGameObjects()
                .FirstOrDefault(candidate => candidate.name == "Qusap75K_AnimationTest");
            if (character == null)
            {
                throw new InvalidOperationException("The isolated test scene has no Qusap75K_AnimationTest root.");
            }

            var probeObject = new GameObject("Qusap75K_PlayModeProbe_Transient");
            probeObject.AddComponent<Qusap.NewQusap75KAnimationTest.QusapRollPlayModeProbe>();
            Qusap75KPlayModeCompletion.MarkPending();
            EditorApplication.EnterPlaymode();
        }

        private static void ConfigureModelImporter(string path, bool importAnimation)
        {
            AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceSynchronousImport | ImportAssetOptions.ForceUpdate);
            ModelImporter importer = AssetImporter.GetAtPath(path) as ModelImporter;
            if (importer == null)
            {
                throw new InvalidOperationException("Expected a ModelImporter at " + path);
            }

            importer.animationType = ModelImporterAnimationType.Human;
            importer.avatarSetup = ModelImporterAvatarSetup.CreateFromThisModel;
            importer.importAnimation = importAnimation;
            importer.importCameras = false;
            importer.importLights = false;
            importer.isReadable = false;
            importer.SaveAndReimport();

            if (!importAnimation)
            {
                return;
            }

            ModelImporterClipAnimation[] clips = importer.clipAnimations;
            if (clips == null || clips.Length == 0)
            {
                clips = importer.defaultClipAnimations;
            }

            if (clips == null || clips.Length == 0)
            {
                throw new InvalidOperationException("The animation FBX contains no importable animation clips.");
            }

            foreach (ModelImporterClipAnimation clip in clips)
            {
                clip.loopTime = false;
                clip.loopPose = false;
                clip.lockRootRotation = true;
                clip.lockRootHeightY = true;
                clip.lockRootPositionXZ = true;
                clip.keepOriginalOrientation = true;
                clip.keepOriginalPositionY = true;
                clip.keepOriginalPositionXZ = true;
            }

            importer.clipAnimations = clips;
            importer.SaveAndReimport();
        }

        private static AvatarResult InspectAvatar(string path)
        {
            var result = new AvatarResult { assetPath = path };
            GameObject model = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            Animator animator = model != null ? model.GetComponentInChildren<Animator>(true) : null;
            Avatar avatar = animator != null ? animator.avatar : null;

            if (avatar == null)
            {
                result.error = "Unity did not create an Avatar sub-asset.";
                result.missingRequiredBones.AddRange(RequiredHumanBones);
                return result;
            }

            result.isValid = avatar.isValid;
            result.isHuman = avatar.isHuman;

            try
            {
                HumanDescription description = avatar.humanDescription;
                foreach (HumanBone humanBone in description.human)
                {
                    if (!string.IsNullOrEmpty(humanBone.humanName) && !string.IsNullOrEmpty(humanBone.boneName))
                    {
                        result.mappedHumanBones.Add(humanBone.humanName + " -> " + humanBone.boneName);
                    }
                }

                foreach (string required in RequiredHumanBones)
                {
                    if (!description.human.Any(h => h.humanName == required && !string.IsNullOrEmpty(h.boneName)))
                    {
                        result.missingRequiredBones.Add(required);
                    }
                }

                HumanBone chest = description.human.FirstOrDefault(h => h.humanName == "Chest");
                result.optionalChestMapping = string.IsNullOrEmpty(chest.boneName)
                    ? "Not mapped (optional)"
                    : "Chest -> " + chest.boneName;
            }
            catch (Exception exception)
            {
                result.error = exception.Message;
            }

            if (!result.isValid || !result.isHuman)
            {
                result.error = string.IsNullOrEmpty(result.error)
                    ? "Avatar is not a valid Humanoid Avatar."
                    : result.error;
            }

            return result;
        }

        private static bool AvatarPasses(AvatarResult result)
        {
            return result != null && result.isValid && result.isHuman &&
                   result.missingRequiredBones.Count == 0 && string.IsNullOrEmpty(result.error);
        }

        private static List<AnimationClip> LoadUsableClips(string path)
        {
            return AssetDatabase.LoadAllAssetsAtPath(path)
                .OfType<AnimationClip>()
                .Where(clip => !clip.name.StartsWith("__preview__", StringComparison.OrdinalIgnoreCase))
                .OrderBy(clip => clip.name, StringComparer.OrdinalIgnoreCase)
                .ToList();
        }

        private static AnimationClip SelectRollClip(List<AnimationClip> clips)
        {
            return clips.FirstOrDefault(clip => clip.name.IndexOf("roll", StringComparison.OrdinalIgnoreCase) >= 0)
                   ?? clips[0];
        }

        private static ModelImporterClipAnimation FindClipSettings(ModelImporter importer, string clipName)
        {
            if (importer == null)
            {
                return null;
            }

            ModelImporterClipAnimation[] clips = importer.clipAnimations;
            if (clips == null || clips.Length == 0)
            {
                clips = importer.defaultClipAnimations;
            }

            return clips.FirstOrDefault(setting => setting.name == clipName) ?? clips.FirstOrDefault();
        }

        private static Material CreateOrReplaceMaterial(string path, Color color, float smoothness)
        {
            AssetDatabase.DeleteAsset(path);
            Shader shader = Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard");
            if (shader == null)
            {
                throw new InvalidOperationException("No compatible Lit shader was found.");
            }

            var material = new Material(shader) { name = Path.GetFileNameWithoutExtension(path) };
            if (material.HasProperty("_BaseColor"))
            {
                material.SetColor("_BaseColor", color);
            }
            else if (material.HasProperty("_Color"))
            {
                material.SetColor("_Color", color);
            }

            if (material.HasProperty("_Smoothness"))
            {
                material.SetFloat("_Smoothness", smoothness);
            }

            AssetDatabase.CreateAsset(material, path);
            return material;
        }

        private static AnimatorController CreateController(AnimationClip clip)
        {
            AssetDatabase.DeleteAsset(ControllerPath);
            AnimatorController controller = AnimatorController.CreateAnimatorControllerAtPath(ControllerPath);
            AnimatorStateMachine stateMachine = controller.layers[0].stateMachine;
            AnimatorState state = stateMachine.AddState("RollFront");
            state.motion = clip;
            state.speed = 1f;
            stateMachine.defaultState = state;
            EditorUtility.SetDirty(controller);
            AssetDatabase.SaveAssets();
            return controller;
        }

        private static GameObject CreatePrefab(AnimatorController controller, Material characterMaterial)
        {
            GameObject modelAsset = AssetDatabase.LoadAssetAtPath<GameObject>(CharacterPath);
            Animator sourceAnimator = modelAsset != null ? modelAsset.GetComponentInChildren<Animator>(true) : null;
            if (modelAsset == null || sourceAnimator == null || sourceAnimator.avatar == null)
            {
                throw new InvalidOperationException("The Qusap model or its Avatar is unavailable.");
            }

            var root = new GameObject("Qusap75K_AnimationTest");
            root.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
            root.transform.localScale = Vector3.one;

            GameObject visual = PrefabUtility.InstantiatePrefab(modelAsset) as GameObject;
            if (visual == null)
            {
                UnityEngine.Object.DestroyImmediate(root);
                throw new InvalidOperationException("Could not instantiate the Qusap model asset.");
            }

            visual.name = "Visual";
            visual.transform.SetParent(root.transform, false);
            visual.transform.localPosition = Vector3.zero;
            visual.transform.localRotation = Quaternion.identity;
            visual.transform.localScale = Vector3.one;

            foreach (Animator nestedAnimator in visual.GetComponentsInChildren<Animator>(true))
            {
                UnityEngine.Object.DestroyImmediate(nestedAnimator);
            }

            foreach (Renderer renderer in visual.GetComponentsInChildren<Renderer>(true))
            {
                if (renderer is SkinnedMeshRenderer skinnedMeshRenderer)
                {
                    skinnedMeshRenderer.updateWhenOffscreen = true;
                }

                Material[] materials = renderer.sharedMaterials;
                for (int index = 0; index < materials.Length; index++)
                {
                    materials[index] = characterMaterial;
                }
                renderer.sharedMaterials = materials;
            }

            Animator animator = root.AddComponent<Animator>();
            animator.avatar = sourceAnimator.avatar;
            animator.runtimeAnimatorController = controller;
            animator.applyRootMotion = false;
            animator.cullingMode = AnimatorCullingMode.AlwaysAnimate;

            AssetDatabase.DeleteAsset(PrefabPath);
            GameObject prefab = PrefabUtility.SaveAsPrefabAsset(root, PrefabPath);
            UnityEngine.Object.DestroyImmediate(root);
            if (prefab == null)
            {
                throw new InvalidOperationException("Could not save the isolated test prefab.");
            }
            return prefab;
        }

        private static void CreateScene(GameObject prefab, Material groundMaterial)
        {
            Scene scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            GameObject instance = PrefabUtility.InstantiatePrefab(prefab, scene) as GameObject;
            if (instance == null)
            {
                throw new InvalidOperationException("Could not instantiate the test prefab in the new scene.");
            }

            instance.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
            instance.transform.localScale = Vector3.one;
            Bounds bounds = CalculateRendererBounds(instance);
            float height = Mathf.Max(1f, bounds.size.y);

            GameObject ground = GameObject.CreatePrimitive(PrimitiveType.Plane);
            ground.name = "VisualGround_NoCollider";
            Collider collider = ground.GetComponent<Collider>();
            if (collider != null)
            {
                UnityEngine.Object.DestroyImmediate(collider);
            }
            ground.transform.position = new Vector3(bounds.center.x, bounds.min.y, bounds.center.z);
            ground.transform.localScale = Vector3.one * Mathf.Max(0.25f, height * 0.22f);
            Renderer groundRenderer = ground.GetComponent<Renderer>();
            if (groundRenderer != null)
            {
                groundRenderer.sharedMaterial = groundMaterial;
            }

            var cameraObject = new GameObject("SideCamera", typeof(Camera));
            Camera camera = cameraObject.GetComponent<Camera>();
            camera.orthographic = true;
            camera.orthographicSize = height * 0.62f;
            camera.clearFlags = CameraClearFlags.SolidColor;
            camera.backgroundColor = new Color(0.035f, 0.045f, 0.06f, 1f);
            camera.nearClipPlane = 0.01f;
            camera.farClipPlane = height * 10f;
            Vector3 target = bounds.center + Vector3.up * height * 0.02f;
            cameraObject.transform.position = target + Vector3.right * height * 2.5f;
            cameraObject.transform.rotation = Quaternion.LookRotation(target - cameraObject.transform.position, Vector3.up);
            camera.tag = "MainCamera";

            var lightObject = new GameObject("DirectionalLight", typeof(Light));
            Light light = lightObject.GetComponent<Light>();
            light.type = LightType.Directional;
            light.intensity = 1.25f;
            lightObject.transform.rotation = Quaternion.Euler(35f, -45f, 0f);

            if (!EditorSceneManager.SaveScene(scene, ScenePath))
            {
                throw new InvalidOperationException("Could not save the isolated test scene.");
            }
        }

        private static FinalValidationReport ValidateAndCapture(
            AvatarResult characterResult,
            AvatarResult animationResult,
            AnimationClip clip,
            ModelImporterClipAnimation clipSettings)
        {
            Scene scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
            GameObject instance = scene.GetRootGameObjects().FirstOrDefault(go => go.name == "Qusap75K_AnimationTest");
            Camera camera = scene.GetRootGameObjects().Select(go => go.GetComponent<Camera>()).FirstOrDefault(c => c != null);
            if (instance == null || camera == null)
            {
                throw new InvalidOperationException("The isolated test scene is missing its character or camera.");
            }

            Animator animator = instance.GetComponent<Animator>();
            if (animator == null)
            {
                throw new InvalidOperationException("The isolated test prefab root has no Animator.");
            }

            Directory.CreateDirectory(ToAbsoluteAssetPath(ScreenshotDirectory));
            Vector3 rootStart = instance.transform.position;
            Bounds baseBounds = CalculateRendererBounds(instance);
            float baseMaximum = Mathf.Max(baseBounds.size.x, Mathf.Max(baseBounds.size.y, baseBounds.size.z));
            float maximumGrowth = 1f;
            bool finite = true;
            var samples = new List<PoseSample>();

            PlayableGraph graph = PlayableGraph.Create("Qusap75K_RollValidation");
            graph.SetTimeUpdateMode(DirectorUpdateMode.Manual);
            AnimationClipPlayable playable = AnimationClipPlayable.Create(graph, clip);
            AnimationPlayableOutput output = AnimationPlayableOutput.Create(graph, "RollFront", animator);
            output.SetSourcePlayable(playable);
            graph.Play();

            var sampleDefinitions = new[]
            {
                new { Label = "Start", Time = 0f },
                new { Label = "Quarter", Time = clip.length * 0.25f },
                new { Label = "Middle", Time = clip.length * 0.5f },
                new { Label = "ThreeQuarter", Time = clip.length * 0.75f },
                new { Label = "End", Time = Mathf.Max(0f, clip.length - (1f / Mathf.Max(1f, clip.frameRate))) }
            };

            foreach (var definition in sampleDefinitions)
            {
                playable.SetTime(definition.Time);
                graph.Evaluate(0f);
                Bounds poseBounds = CalculateRendererBounds(instance);
                bool poseFinite = IsFinite(poseBounds.center) && IsFinite(poseBounds.size);
                finite &= poseFinite;
                float poseMaximum = Mathf.Max(poseBounds.size.x, Mathf.Max(poseBounds.size.y, poseBounds.size.z));
                if (baseMaximum > 0.0001f)
                {
                    maximumGrowth = Mathf.Max(maximumGrowth, poseMaximum / baseMaximum);
                }

                string screenshotAssetPath = ScreenshotDirectory + "/RollFront_" + definition.Label + ".png";
                CaptureCamera(camera, screenshotAssetPath);
                samples.Add(new PoseSample
                {
                    label = definition.Label,
                    time = definition.Time,
                    rootPosition = FormatVector(instance.transform.position),
                    boundsCenter = FormatVector(poseBounds.center),
                    boundsSize = FormatVector(poseBounds.size),
                    finiteBounds = poseFinite,
                    screenshot = screenshotAssetPath
                });
            }

            graph.Destroy();
            bool rootStayedFixed = Vector3.Distance(rootStart, instance.transform.position) < 0.0001f;
            bool grossStretchDetected = !finite || maximumGrowth > 4f;
            AnimatorController controller = AssetDatabase.LoadAssetAtPath<AnimatorController>(ControllerPath);
            AnimatorStateMachine stateMachine = controller != null ? controller.layers[0].stateMachine : null;
            AnimatorState state = stateMachine != null ? stateMachine.defaultState : null;
            bool controllerValid = controller != null && controller.parameters.Length == 0 &&
                                   state != null && state.name == "RollFront" && state.motion == clip &&
                                   state.speed == 1f && stateMachine.anyStateTransitions.Length == 0 &&
                                   state.transitions.Length == 0;

            GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath);
            bool prefabClean = prefab != null && prefab.transform.localScale == Vector3.one &&
                               prefab.GetComponent<Rigidbody>() == null &&
                               prefab.GetComponentsInChildren<Collider>(true).Length == 0;

            var report = new FinalValidationReport
            {
                generatedAtUtc = DateTime.UtcNow.ToString("O"),
                characterAsset = CharacterPath,
                animationAsset = AnimationPath,
                clipName = clip.name,
                clipLengthSeconds = clip.length,
                clipFrameRate = clip.frameRate,
                approximateFrameCount = Mathf.RoundToInt(clip.length * clip.frameRate),
                loopTime = clipSettings.loopTime,
                loopPose = clipSettings.loopPose,
                bakeRootRotation = clipSettings.lockRootRotation,
                bakeRootPositionY = clipSettings.lockRootHeightY,
                bakeRootPositionXZ = clipSettings.lockRootPositionXZ,
                qusapAvatarValid = AvatarPasses(characterResult),
                animationAvatarValid = AvatarPasses(animationResult),
                animatorApplyRootMotion = animator.applyRootMotion,
                rootStayedFixed = rootStayedFixed,
                maximumBoundsGrowthRatio = maximumGrowth,
                grossStretchDetected = grossStretchDetected,
                materialStatus = "Neutral URP Lit material used for deformation visibility; imported vertex color is not evaluated by this material.",
                controllerAsset = ControllerPath,
                prefabAsset = PrefabPath,
                sceneAsset = ScenePath,
                sceneRootObjects = scene.GetRootGameObjects().Select(go => go.name).OrderBy(name => name).ToList(),
                poseSamples = samples,
                passed = AvatarPasses(characterResult) && AvatarPasses(animationResult) &&
                         !animator.applyRootMotion && rootStayedFixed && !grossStretchDetected &&
                         controllerValid && prefabClean && scene.rootCount == 4
            };

            report.notes.Add("The isolated prefab contains no Rigidbody, collider, gameplay, combat, input, or camera scripts.");
            report.notes.Add("Pose screenshots were rendered by deterministic Editor sampling; Play Mode was not invoked because no safe Unity integration was available.");
            report.notes.Add("Visual review of horns, claws, scales, armor, floor contact, and subtle deformation remains a human inspection step.");
            return report;
        }

        private static Bounds CalculateRendererBounds(GameObject root)
        {
            Renderer[] renderers = root.GetComponentsInChildren<Renderer>(true);
            if (renderers.Length == 0)
            {
                return new Bounds(root.transform.position, Vector3.one);
            }

            Bounds bounds = renderers[0].bounds;
            for (int index = 1; index < renderers.Length; index++)
            {
                bounds.Encapsulate(renderers[index].bounds);
            }
            return bounds;
        }

        private static void CaptureCamera(Camera camera, string assetPath)
        {
            const int width = 960;
            const int height = 960;
            var renderTexture = new RenderTexture(width, height, 24, RenderTextureFormat.ARGB32);
            var texture = new Texture2D(width, height, TextureFormat.RGB24, false);
            RenderTexture previousActive = RenderTexture.active;
            RenderTexture previousTarget = camera.targetTexture;
            try
            {
                camera.targetTexture = renderTexture;
                RenderTexture.active = renderTexture;
                camera.Render();
                texture.ReadPixels(new Rect(0, 0, width, height), 0, 0);
                texture.Apply();
                File.WriteAllBytes(ToAbsoluteAssetPath(assetPath), texture.EncodeToPNG());
            }
            finally
            {
                camera.targetTexture = previousTarget;
                RenderTexture.active = previousActive;
                UnityEngine.Object.DestroyImmediate(texture);
                renderTexture.Release();
                UnityEngine.Object.DestroyImmediate(renderTexture);
            }
        }

        private static bool IsFinite(Vector3 value)
        {
            return IsFinite(value.x) && IsFinite(value.y) && IsFinite(value.z);
        }

        private static bool IsFinite(float value)
        {
            return !float.IsNaN(value) && !float.IsInfinity(value);
        }

        private static string FormatVector(Vector3 value)
        {
            return string.Format(System.Globalization.CultureInfo.InvariantCulture,
                "({0:F5}, {1:F5}, {2:F5})", value.x, value.y, value.z);
        }

        private static void EnsureRequiredAsset(string path)
        {
            if (!File.Exists(ToAbsoluteAssetPath(path)))
            {
                throw new FileNotFoundException("Required test asset not found", path);
            }
        }

        private static void WriteJsonAsset(string assetPath, object data)
        {
            string absolutePath = ToAbsoluteAssetPath(assetPath);
            Directory.CreateDirectory(Path.GetDirectoryName(absolutePath) ?? throw new InvalidOperationException());
            File.WriteAllText(absolutePath, JsonUtility.ToJson(data, true));
        }

        private static string ToAbsoluteAssetPath(string assetPath)
        {
            if (!assetPath.StartsWith("Assets", StringComparison.Ordinal))
            {
                throw new ArgumentException("Expected an Assets-relative path", nameof(assetPath));
            }

            string relative = assetPath.Substring("Assets".Length).TrimStart('/', '\\');
            return Path.GetFullPath(Path.Combine(Application.dataPath, relative));
        }
    }
}
