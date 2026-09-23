using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;
using Qusap.NewQusap75KAnimationTest.Playable.DoubleL.SwordIntegration.Validation;

namespace Qusap.NewQusap75KAnimationTest.Playable.DoubleL.SwordIntegration.Editor
{
    [InitializeOnLoad]
    internal static class QusapBasicSwordIntegrationAutoRunner
    {
        private const string Root = "Assets/_Qusap/Tests/NewQusap75KAnimationTest/Playable/DoubleL/SwordIntegration";
        private const string TriggerPath = Root + "/Validation/BuildRequested.txt";
        private const string ResultPath = Root + "/Validation/BuildResult.txt";
        private const string ReportPath = Root + "/Validation/LargeGripSwordValidation.json";
        private const string ScenePath = Root + "/Scene/CombatPlayground_Qusap75K_DoubleL_SwordIntegrationTest.unity";
        private const string PendingKey = "Qusap.DoubleL.SwordIntegration.PlayModePending";
        private static double playStartedAt;

        static QusapBasicSwordIntegrationAutoRunner()
        {
            EditorApplication.delayCall += RunWhenReady;
            EditorApplication.playModeStateChanged += OnPlayModeStateChanged;
        }

        private static void RunWhenReady()
        {
            if (!File.Exists(ToAbsolutePath(TriggerPath))) return;
            if (EditorApplication.isCompiling || EditorApplication.isUpdating)
            {
                EditorApplication.delayCall += RunWhenReady;
                return;
            }
            AssetDatabase.DeleteAsset(TriggerPath);
            if (EditorApplication.isPlayingOrWillChangePlaymode)
            {
                WriteResult("BLOCKED: Unity is in or entering Play Mode.");
                return;
            }
            Scene current = SceneManager.GetActiveScene();
            if (current.IsValid() && current.isDirty)
            {
                WriteResult("BLOCKED: The current scene has unsaved changes.");
                return;
            }
            try
            {
                QusapBasicSwordIntegrationBuilder.BuildAndValidate();
                WriteResult("BUILD_PASSED; PLAY_MODE_PENDING");
                SessionState.SetBool(PendingKey, true);
                EditorApplication.EnterPlaymode();
            }
            catch (Exception exception)
            {
                WriteResult("FAILED: " + exception);
                Debug.LogException(exception);
            }
        }

        public static void RunFromCommandLine()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode)
                throw new InvalidOperationException("Unity is already in or entering Play Mode.");
            QusapBasicSwordIntegrationBuilder.BuildAndValidate();
            WriteResult("BUILD_PASSED; PLAY_MODE_PENDING");
            SessionState.SetBool(PendingKey, true);
            EditorApplication.EnterPlaymode();
        }

        public static void OpenFinalScene()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode) EditorApplication.ExitPlaymode();
            EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
            Selection.activeObject = AssetDatabase.LoadAssetAtPath<SceneAsset>(ScenePath);
        }

        private static void OnPlayModeStateChanged(PlayModeStateChange state)
        {
            if (!SessionState.GetBool(PendingKey, false)) return;
            if (state == PlayModeStateChange.EnteredPlayMode)
            {
                playStartedAt = EditorApplication.timeSinceStartup;
                EditorApplication.isPaused = false;
                EditorApplication.QueuePlayerLoopUpdate();
                EditorApplication.update -= Timeout;
                EditorApplication.update += Timeout;
            }
            else if (state == PlayModeStateChange.EnteredEditMode)
            {
                EditorApplication.update -= Timeout;
                SessionState.SetBool(PendingKey, false);
                EditorApplication.delayCall += FinalizeValidation;
            }
        }

        private static void Timeout()
        {
            if (EditorApplication.isPlaying && !EditorApplication.isPaused)
                EditorApplication.QueuePlayerLoopUpdate();
            if (EditorApplication.isPlaying && EditorApplication.timeSinceStartup - playStartedAt > 45d)
            {
                Debug.LogError("SwordIntegration Play Mode validation exceeded 45 seconds.");
                EditorApplication.ExitPlaymode();
            }
        }

        private static void FinalizeValidation()
        {
            try
            {
                AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
                LargeGripSwordValidationReport report = File.Exists(ToAbsolutePath(ReportPath))
                    ? JsonUtility.FromJson<LargeGripSwordValidationReport>(File.ReadAllText(ToAbsolutePath(ReportPath)))
                    : null;
                EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
                Selection.activeObject = AssetDatabase.LoadAssetAtPath<SceneAsset>(ScenePath);
                WriteResult(report != null && report.passed
                    ? $"PASSED; VERTICES={report.vertexCount}; TRIANGLES={report.triangleCount}; ERRORS={report.consoleErrors.Length}"
                    : "FAILED_PLAY_MODE; inspect LargeGripSwordValidation.json");
                Debug.Log(report != null && report.passed
                    ? "QUSAP_LARGE_GRIP_SWORD_VALIDATION_PASSED"
                    : "QUSAP_LARGE_GRIP_SWORD_VALIDATION_FAILED");
                if (Application.isBatchMode)
                {
                    int exitCode = report != null && report.passed ? 0 : 2;
                    EditorApplication.delayCall += () => EditorApplication.Exit(exitCode);
                }
            }
            catch (Exception exception)
            {
                WriteResult("FAILED_FINALIZATION: " + exception);
                Debug.LogException(exception);
            }
        }

        private static void WriteResult(string value)
        {
            File.WriteAllText(ToAbsolutePath(ResultPath), value);
            AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
        }

        private static string ToAbsolutePath(string assetPath) => Path.GetFullPath(Path.Combine(
            Application.dataPath, assetPath.Substring("Assets".Length).TrimStart('/', '\\')));
    }

    public static class QusapBasicSwordIntegrationBuilder
    {
        private const string TestRoot = "Assets/_Qusap/Tests/NewQusap75KAnimationTest";
        private const string Root = TestRoot + "/Playable/DoubleL/SwordIntegration";
        private const string ImportedRoot = TestRoot + "/Imported/QusapBasicSwordLargeGrip25K";
        private const string SourceFbx = "C:/Users/victo/Documents/Qusap_ArtSource/Qusap75K_Prueba/05_Armas/EspadaBasicaLargeGrip25K/03_Export_Unity/Qusap_BasicSword_LargeGrip_25K.fbx";
        private const string ModelPath = ImportedRoot + "/Model/Qusap_BasicSword_LargeGrip_25K.fbx";
        private const string ShaderPath = ImportedRoot + "/Shader/QusapBasicSwordLargeGrip25K_VertexColor_URP.shader";
        private const string MaterialPath = ImportedRoot + "/Materials/Qusap_BasicSword_LargeGrip_25K_VertexColor.mat";
        private const string VisualPrefabPath = ImportedRoot + "/Prefab/Qusap_BasicSword_LargeGrip_25K_Visual.prefab";
        private const string SourcePrefabPath = TestRoot + "/Playable/DoubleL/AutoLocomotion/Prefab/Qusap75K_DoubleL_AutoLocomotionTest.prefab";
        private const string SourceScenePath = TestRoot + "/Playable/DoubleL/AutoLocomotion/Scene/CombatPlayground_Qusap75K_DoubleL_AutoLocomotionTest.unity";
        private const string SourceControllerPath = TestRoot + "/Playable/DoubleL/AutoLocomotion/Controller/Qusap75K_DoubleL_AutoLocomotionTest.controller";
        private const string InputActionsPath = "Assets/_Qusap/Settings/QusapControls.inputactions";
        private const string PrefabPath = Root + "/Prefab/Qusap75K_DoubleL_SwordIntegrationTest.prefab";
        private const string ScenePath = Root + "/Scene/CombatPlayground_Qusap75K_DoubleL_SwordIntegrationTest.unity";
        private const string MarkerName = "QusapLargeGripIntegrationValidation.request";

        [MenuItem("Tools/Qusap/Tests/Build Basic Sword 25K Integration Test")]
        public static void BuildAndValidate()
        {
            string sourceHash = HashFile(SourceFbx);
            if (sourceHash != HashFile(ToAbsolutePath(ModelPath)))
                throw new InvalidOperationException("Imported FBX is not a byte-identical copy of the approved source.");
            string[] protectedPaths = { SourcePrefabPath, SourceScenePath, SourceControllerPath, InputActionsPath };
            Dictionary<string, string> before = protectedPaths.ToDictionary(path => path, HashAssetAndMeta);

            EnsureFolder(Root + "/Prefab");
            EnsureFolder(Root + "/Scene");
            EnsureFolder(Root + "/Validation");
            EnsureFolder(Root + "/Materials");
            EnsureFolder(Root + "/Shader");
            ConfigureModelImporter();
            Material material = CreateMaterial();
            GameObject visualPrefab = CreateVisualPrefab(material);
            TransformRecord transform = CreatePlayerPrefab(visualPrefab);
            CreateScene();
            SwordIntegrationValidationReport report = ValidateStructure(sourceHash, transform, before);
            AssetDatabase.SaveAssets();
            if (!report.sourceAutoLocomotionUnchanged || !report.movementPhysicsInputCombatPreserved
                || report.meshCount != 1 || report.vertexCount < 12000 || report.vertexCount > 13000
                || report.triangleCount < 24500 || report.triangleCount > 25100
                || !report.hasVertexColor || report.hasAvatarBonesOrAnimations)
                throw new InvalidOperationException("SwordIntegration structural validation failed.");

            string marker = Path.Combine(Directory.GetParent(Application.dataPath)?.FullName
                ?? Application.dataPath, "Library", MarkerName);
            File.WriteAllText(marker, "Run LargeGrip SwordIntegration validation once.");
        }

        private sealed class TransformRecord
        {
            public Vector3 alignmentPosition;
            public Vector3 alignmentEuler;
            public Vector3 alignmentScale;
            public Vector3 swordPosition;
            public Vector3 swordEuler;
            public Vector3 swordScale;
            public bool provisionalAlwaysActive;
        }

        private static void ConfigureModelImporter()
        {
            AssetDatabase.ImportAsset(ModelPath, ImportAssetOptions.ForceSynchronousImport | ImportAssetOptions.ForceUpdate);
            ModelImporter importer = AssetImporter.GetAtPath(ModelPath) as ModelImporter;
            if (importer == null) throw new InvalidOperationException("LargeGrip sword FBX has no ModelImporter.");
            importer.animationType = ModelImporterAnimationType.None;
            importer.importAnimation = false;
            importer.importBlendShapes = false;
            importer.importCameras = false;
            importer.importLights = false;
            importer.isReadable = true;
            importer.materialImportMode = ModelImporterMaterialImportMode.None;
            importer.SaveAndReimport();
        }

        private static Material CreateMaterial()
        {
            Shader shader = AssetDatabase.LoadAssetAtPath<Shader>(ShaderPath);
            if (shader == null || !shader.isSupported)
                throw new InvalidOperationException("Isolated URP Vertex Color shader is missing or unsupported.");
            AssetDatabase.DeleteAsset(MaterialPath);
            var material = new Material(shader) { name = "Qusap_BasicSword_LargeGrip_25K_VertexColor" };
            material.SetColor("_BaseColor", new Color(0.8f, 0.8f, 0.8f, 1f));
            material.SetFloat("_AlbedoContrast", 1.18f);
            material.SetFloat("_Metallic", 0f);
            material.SetFloat("_Smoothness", 0.22f);
            AssetDatabase.CreateAsset(material, MaterialPath);
            return material;
        }

        private static GameObject CreateVisualPrefab(Material material)
        {
            AssetDatabase.DeleteAsset(VisualPrefabPath);
            GameObject model = AssetDatabase.LoadAssetAtPath<GameObject>(ModelPath);
            if (model == null) throw new InvalidOperationException("Imported sword model could not be loaded.");
            GameObject root = new GameObject("Qusap_BasicSword_LargeGrip_25K_Visual");
            try
            {
                GameObject instance = PrefabUtility.InstantiatePrefab(model, root.transform) as GameObject;
                if (instance == null) throw new InvalidOperationException("Could not instantiate imported sword model.");
                instance.name = "Qusap_BasicSword_LargeGrip_25K_Model";
                instance.transform.localPosition = Vector3.zero;
                instance.transform.localRotation = Quaternion.identity;
                instance.transform.localScale = Vector3.one;
                Renderer[] renderers = instance.GetComponentsInChildren<Renderer>(true);
                if (renderers.Length != 1) throw new InvalidOperationException("Expected exactly one sword Renderer.");
                renderers[0].sharedMaterial = material;
                renderers[0].enabled = true;
                foreach (Collider collider in instance.GetComponentsInChildren<Collider>(true)) Object.DestroyImmediate(collider);
                foreach (Rigidbody body in instance.GetComponentsInChildren<Rigidbody>(true)) Object.DestroyImmediate(body);
                return PrefabUtility.SaveAsPrefabAsset(root, VisualPrefabPath);
            }
            finally
            {
                Object.DestroyImmediate(root);
            }
        }

        private static TransformRecord CreatePlayerPrefab(GameObject visualPrefab)
        {
            AssetDatabase.DeleteAsset(PrefabPath);
            if (!AssetDatabase.CopyAsset(SourcePrefabPath, PrefabPath))
                throw new InvalidOperationException("Could not copy AutoLocomotion prefab.");
            GameObject root = PrefabUtility.LoadPrefabContents(PrefabPath);
            try
            {
                root.name = "Qusap75K_DoubleL_SwordIntegrationTest";
                Transform socket = FindDeepChild(root.transform, "DoubleL_WeaponSocket");
                if (socket == null) throw new InvalidOperationException("DoubleL_WeaponSocket was not found.");
                Transform provisional = socket.Cast<Transform>().FirstOrDefault(child =>
                    child.name == "SM_Wep_Sword_03_Placeholder");
                if (provisional == null) throw new InvalidOperationException("Provisional DoubleL sword was not found.");
                bool provisionalAlwaysActive = provisional.gameObject.activeSelf
                    && provisional.GetComponentsInChildren<Renderer>(true).All(item => item.enabled);
                Object.DestroyImmediate(provisional.gameObject);

                var alignmentObject = new GameObject("LargeGripAlignment");
                Transform alignment = alignmentObject.transform;
                alignment.SetParent(socket, false);
                alignment.localPosition = Vector3.zero;
                // Identity inspection placed the blade about 55 degrees below screen-horizontal.
                // Rotate in the socket's camera-facing plane to settle near 15 degrees down.
                alignment.localRotation = Quaternion.SlerpUnclamped(
                    Quaternion.identity,
                    Quaternion.Euler(346.0443f, 357.6083f, 321.0569f),
                    0.94f);
                alignment.localScale = Vector3.one * 0.65f;

                GameObject sword = PrefabUtility.InstantiatePrefab(visualPrefab, alignment) as GameObject;
                if (sword == null) throw new InvalidOperationException("Could not attach sword visual prefab.");
                sword.name = "Qusap_BasicSword_LargeGrip_25K_Visual";
                sword.transform.localPosition = Vector3.zero;
                sword.transform.localRotation = Quaternion.identity;
                sword.transform.localScale = Vector3.one;

                Animator animator = FindDeepChild(root.transform, "Qusap75K_Visual")?.GetComponent<Animator>();
                if (animator == null) throw new InvalidOperationException("Qusap75K Animator was not found.");
                animator.applyRootMotion = false;
                var record = new TransformRecord
                {
                    alignmentPosition = alignment.localPosition,
                    alignmentEuler = alignment.localEulerAngles,
                    alignmentScale = alignment.localScale,
                    swordPosition = sword.transform.localPosition,
                    swordEuler = sword.transform.localEulerAngles,
                    swordScale = sword.transform.localScale,
                    provisionalAlwaysActive = provisionalAlwaysActive
                };
                PrefabUtility.SaveAsPrefabAsset(root, PrefabPath);
                return record;
            }
            finally { PrefabUtility.UnloadPrefabContents(root); }
        }

        private static void CreateScene()
        {
            AssetDatabase.DeleteAsset(ScenePath);
            if (!AssetDatabase.CopyAsset(SourceScenePath, ScenePath))
                throw new InvalidOperationException("Could not copy AutoLocomotion scene.");
            Scene scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
            GameObject oldPlayer = scene.GetRootGameObjects().FirstOrDefault(root =>
                GetPrefabAssetPath(root) == SourcePrefabPath
                || root.name == "Player1_Qusap75K_DoubleL_AutoLocomotionTest");
            if (oldPlayer == null) throw new InvalidOperationException("Player 1 was not found.");
            GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath);
            GameObject newPlayer = PrefabUtility.InstantiatePrefab(prefab, scene) as GameObject;
            if (newPlayer == null) throw new InvalidOperationException("Could not instantiate SwordIntegration player.");
            newPlayer.name = "Player1_Qusap75K_DoubleL_SwordIntegrationTest";
            newPlayer.transform.SetPositionAndRotation(oldPlayer.transform.position, oldPlayer.transform.rotation);
            newPlayer.transform.localScale = oldPlayer.transform.localScale;
            newPlayer.transform.SetSiblingIndex(oldPlayer.transform.GetSiblingIndex());
            ReplaceExternalSceneReferences(scene, oldPlayer, newPlayer);
            Object.DestroyImmediate(oldPlayer);
            foreach (GameObject item in scene.GetRootGameObjects().Where(root =>
                root.GetComponent<Qusap.NewQusap75KAnimationTest.Playable.DoubleL.AutoLocomotion.Validation.QusapDoubleLAutoLocomotionValidation>() != null
                || root.name.Contains("AutoLocomotion_Validation")).ToArray()) Object.DestroyImmediate(item);
            new GameObject("Qusap75K_LargeGrip_Sword_Integration_Validation")
                .AddComponent<QusapLargeGripIntegrationValidation>();
            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene, ScenePath);
            AssetDatabase.SaveAssets();
        }

        private static SwordIntegrationValidationReport ValidateStructure(string sourceHash,
            TransformRecord transform, Dictionary<string, string> protectedHashes)
        {
            GameObject model = AssetDatabase.LoadAssetAtPath<GameObject>(ModelPath);
            MeshFilter[] filters = model.GetComponentsInChildren<MeshFilter>(true);
            Mesh[] meshes = filters.Select(item => item.sharedMesh).Where(item => item != null).Distinct().ToArray();
            int vertices = meshes.Sum(item => item.vertexCount);
            int triangles = meshes.Sum(item => item.triangles.Length / 3);
            Color32[] colors = meshes.SelectMany(item => item.colors32).ToArray();
            int distinctColors = colors.Distinct().Count();
            bool noRig = model.GetComponentInChildren<Animator>(true) == null
                && model.GetComponentsInChildren<SkinnedMeshRenderer>(true).Length == 0
                && !AssetDatabase.LoadAllAssetsAtPath(ModelPath).OfType<AnimationClip>()
                    .Any(clip => !clip.name.StartsWith("__preview__", StringComparison.Ordinal));
            bool sourceUnchanged = protectedHashes.All(pair => HashAssetAndMeta(pair.Key) == pair.Value);

            GameObject source = PrefabUtility.LoadPrefabContents(SourcePrefabPath);
            GameObject target = PrefabUtility.LoadPrefabContents(PrefabPath);
            bool preserved;
            try
            {
                string[] types =
                {
                    "Qusap.QusapInputReader", "Qusap.QusapHorizontalMotor", "Qusap.QusapGroundSensor",
                    "Qusap.QusapVerticalMotor", "Qusap.QusapWallSensor", "Qusap.QusapDashMotor",
                    "Qusap.QusapCombatController", "UnityEngine.Rigidbody", "UnityEngine.CapsuleCollider",
                    "Qusap.NewQusap75KAnimationTest.Playable.DoubleL.AutoLocomotion.QusapDoubleLAutoLocomotionDriver"
                };
                preserved = types.All(typeName => ComponentsMatch(source, target, typeName));
            }
            finally
            {
                PrefabUtility.UnloadPrefabContents(source);
                PrefabUtility.UnloadPrefabContents(target);
            }

            Scene scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
            GameObject playerTwo = scene.GetRootGameObjects().FirstOrDefault(item => item.name == "Player2_Qusap");
            bool playerTwoUntouched = GetPrefabAssetPath(playerTwo) == "Assets/_Qusap/Prefabs/QusapCombatPlayer.prefab";
            Shader shader = AssetDatabase.LoadAssetAtPath<Shader>(ShaderPath);
            Material material = AssetDatabase.LoadAssetAtPath<Material>(MaterialPath);
            return new SwordIntegrationValidationReport
            {
                generatedAtUtc = DateTime.UtcNow.ToString("O"),
                sourceFbxSha256 = sourceHash,
                importedFbx = ModelPath,
                visualPrefab = VisualPrefabPath,
                playerPrefab = PrefabPath,
                scene = ScenePath,
                material = MaterialPath,
                shader = shader != null ? shader.name : ShaderPath,
                meshCount = meshes.Length,
                vertexCount = vertices,
                triangleCount = triangles,
                hasVertexColor = colors.Length == vertices && distinctColors > 1,
                distinctVertexColors = distinctColors,
                hasAvatarBonesOrAnimations = !noRig,
                alignmentLocalPosition = Format(transform.alignmentPosition),
                alignmentLocalEulerAngles = Format(transform.alignmentEuler),
                alignmentLocalScale = Format(transform.alignmentScale),
                swordLocalPosition = Format(transform.swordPosition),
                swordLocalEulerAngles = Format(transform.swordEuler),
                swordLocalScale = Format(transform.swordScale),
                materialBaseColor = material != null ? Format(material.GetColor("_BaseColor")) : string.Empty,
                materialMetallic = material != null ? material.GetFloat("_Metallic") : -1f,
                materialSmoothness = material != null ? material.GetFloat("_Smoothness") : -1f,
                materialUsesEmission = material != null && material.IsKeywordEnabled("_EMISSION"),
                sourceAutoLocomotionUnchanged = sourceUnchanged,
                movementPhysicsInputCombatPreserved = sourceUnchanged && preserved,
                playerTwoUntouched = playerTwoUntouched,
                provisionalSwordWasAlwaysActive = transform.provisionalAlwaysActive,
                provisionalSwordExplanation = "The provisional sword and its Renderer were always active; its tiny scale and the idle hand pose placed it behind the body from the lateral camera.",
                rootMotionDisabled = true,
                clippingNotes = "Pending visual Play Mode review.",
                consoleWarnings = Array.Empty<string>(),
                consoleErrors = Array.Empty<string>(),
                passed = false
            };
        }

        private static bool ComponentsMatch(GameObject source, GameObject target, string fullName)
        {
            Component a = source.GetComponents<Component>().FirstOrDefault(item => item != null && item.GetType().FullName == fullName);
            Component b = target.GetComponents<Component>().FirstOrDefault(item => item != null && item.GetType().FullName == fullName);
            return a != null && b != null && EditorJsonUtility.ToJson(a) == EditorJsonUtility.ToJson(b);
        }

        private static void ReplaceExternalSceneReferences(Scene scene, GameObject oldPlayer, GameObject newPlayer)
        {
            foreach (GameObject root in scene.GetRootGameObjects())
            foreach (Component component in root.GetComponentsInChildren<Component>(true))
            {
                if (component == null || component.transform.IsChildOf(oldPlayer.transform)) continue;
                SerializedObject serialized = new SerializedObject(component);
                SerializedProperty property = serialized.GetIterator();
                bool enterChildren = true;
                bool changed = false;
                while (property.NextVisible(enterChildren))
                {
                    enterChildren = false;
                    if (property.propertyType == SerializedPropertyType.ObjectReference
                        && property.objectReferenceValue == oldPlayer)
                    {
                        property.objectReferenceValue = newPlayer;
                        changed = true;
                    }
                }
                if (changed) serialized.ApplyModifiedPropertiesWithoutUndo();
            }
        }

        private static Transform FindDeepChild(Transform root, string name)
        {
            if (root.name == name) return root;
            foreach (Transform child in root)
            {
                Transform found = FindDeepChild(child, name);
                if (found != null) return found;
            }
            return null;
        }

        private static string GetPrefabAssetPath(GameObject instance)
        {
            if (instance == null) return string.Empty;
            Object source = PrefabUtility.GetCorrespondingObjectFromSource(instance);
            return source != null ? AssetDatabase.GetAssetPath(source) : string.Empty;
        }

        private static void EnsureFolder(string path)
        {
            string current = "Assets";
            foreach (string segment in path.Substring("Assets/".Length).Split('/'))
            {
                string next = current + "/" + segment;
                if (!AssetDatabase.IsValidFolder(next)) AssetDatabase.CreateFolder(current, segment);
                current = next;
            }
        }

        private static string Format(Vector3 value) => $"({value.x:F6}, {value.y:F6}, {value.z:F6})";
        private static string Format(Color value) => $"({value.r:F3}, {value.g:F3}, {value.b:F3}, {value.a:F3})";
        private static string HashFile(string path)
        {
            using SHA256 sha = SHA256.Create();
            return BitConverter.ToString(sha.ComputeHash(File.ReadAllBytes(path))).Replace("-", string.Empty);
        }
        private static string HashAssetAndMeta(string assetPath)
        {
            string absolute = ToAbsolutePath(assetPath);
            using SHA256 sha = SHA256.Create();
            byte[] bytes = File.ReadAllBytes(absolute).Concat(File.ReadAllBytes(absolute + ".meta")).ToArray();
            return BitConverter.ToString(sha.ComputeHash(bytes)).Replace("-", string.Empty);
        }
        private static string ToAbsolutePath(string assetPath) => Path.GetFullPath(Path.Combine(
            Application.dataPath, assetPath.Substring("Assets".Length).TrimStart('/', '\\')));
    }
}
