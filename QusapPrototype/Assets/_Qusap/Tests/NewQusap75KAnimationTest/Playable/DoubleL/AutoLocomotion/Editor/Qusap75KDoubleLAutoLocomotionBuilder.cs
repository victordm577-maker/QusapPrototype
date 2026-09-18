using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using UnityEditor;
using UnityEditor.Animations;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;
using Qusap.NewQusap75KAnimationTest.Playable.DoubleL.AutoLocomotion.Validation;

namespace Qusap.NewQusap75KAnimationTest.Playable.DoubleL.AutoLocomotion.Editor
{
    [InitializeOnLoad]
    internal static class Qusap75KDoubleLAutoLocomotionAutoRunner
    {
        private const string Root = "Assets/_Qusap/Tests/NewQusap75KAnimationTest/Playable/DoubleL/AutoLocomotion";
        private const string TriggerPath = Root + "/Validation/BuildRequested.txt";
        private const string ResultPath = Root + "/Validation/BuildResult.txt";
        private const string ReportPath = Root + "/Validation/AutoLocomotionValidation.json";
        private const string ScenePath = Root + "/Scene/CombatPlayground_Qusap75K_DoubleL_AutoLocomotionTest.unity";
        private const string PendingKey = "Qusap.DoubleL.AutoLocomotion.PlayModePending";
        private static double playStartedAt;

        static Qusap75KDoubleLAutoLocomotionAutoRunner()
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
                WriteResult("BLOCKED: The currently open scene has unsaved changes.");
                return;
            }

            try
            {
                Qusap75KDoubleLAutoLocomotionBuilder.BuildAndValidate();
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

        private static void OnPlayModeStateChanged(PlayModeStateChange state)
        {
            if (!SessionState.GetBool(PendingKey, false)) return;
            if (state == PlayModeStateChange.EnteredPlayMode)
            {
                playStartedAt = EditorApplication.timeSinceStartup;
                EditorApplication.update -= PlayModeTimeout;
                EditorApplication.update += PlayModeTimeout;
            }
            else if (state == PlayModeStateChange.EnteredEditMode)
            {
                EditorApplication.update -= PlayModeTimeout;
                SessionState.SetBool(PendingKey, false);
                EditorApplication.delayCall += FinalizeValidation;
            }
        }

        private static void PlayModeTimeout()
        {
            if (EditorApplication.isPlaying && EditorApplication.timeSinceStartup - playStartedAt > 40d)
            {
                Debug.LogError("AutoLocomotion Play Mode validation exceeded 40 seconds.");
                EditorApplication.ExitPlaymode();
            }
        }

        private static void FinalizeValidation()
        {
            try
            {
                AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
                AutoLocomotionValidationReport report = File.Exists(ToAbsolutePath(ReportPath))
                    ? JsonUtility.FromJson<AutoLocomotionValidationReport>(File.ReadAllText(ToAbsolutePath(ReportPath)))
                    : null;
                EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
                Selection.activeObject = AssetDatabase.LoadAssetAtPath<SceneAsset>(ScenePath);
                WriteResult(report != null && report.passed
                    ? $"PASSED; ERRORS={report.consoleErrorCount}; WARNINGS={report.consoleWarningCount}"
                    : "FAILED_PLAY_MODE; inspect AutoLocomotionValidation.json");
                Debug.Log(report != null && report.passed
                    ? "QUSAP75K_DOUBLEL_AUTO_LOCOMOTION_VALIDATION_PASSED"
                    : "QUSAP75K_DOUBLEL_AUTO_LOCOMOTION_VALIDATION_FAILED");
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

    public static class Qusap75KDoubleLAutoLocomotionBuilder
    {
        private const string Root = "Assets/_Qusap/Tests/NewQusap75KAnimationTest/Playable/DoubleL/AutoLocomotion";
        private const string SourceRoot = "Assets/_Qusap/Tests/NewQusap75KAnimationTest/Playable/DoubleL";
        private const string SourceControllerPath = SourceRoot + "/Controller/Qusap75K_DoubleL_LocomotionTest.controller";
        private const string SourcePrefabPath = SourceRoot + "/Prefab/Qusap75K_DoubleL_LocomotionTest.prefab";
        private const string SourceScenePath = SourceRoot + "/Scene/CombatPlayground_Qusap75K_DoubleL_LocomotionTest.unity";
        private const string InputActionsPath = "Assets/_Qusap/Settings/QusapControls.inputactions";
        private const string ControllerPath = Root + "/Controller/Qusap75K_DoubleL_AutoLocomotionTest.controller";
        private const string PrefabPath = Root + "/Prefab/Qusap75K_DoubleL_AutoLocomotionTest.prefab";
        private const string ScenePath = Root + "/Scene/CombatPlayground_Qusap75K_DoubleL_AutoLocomotionTest.unity";
        private const string ReportPath = Root + "/Validation/AutoLocomotionValidation.json";
        private const string DriverPath = Root + "/Scripts/QusapDoubleLAutoLocomotionDriver.cs";
        private const string MarkerName = "QusapDoubleLAutoLocomotionValidation.request";

        [MenuItem("Tools/Qusap/Tests/Build DoubleL Auto Locomotion Test")]
        public static void BuildAndValidate()
        {
            string[] protectedPaths = { SourceControllerPath, SourcePrefabPath, SourceScenePath, InputActionsPath };
            Dictionary<string, string> before = protectedPaths.ToDictionary(path => path, HashAssetAndMeta);

            EnsureFolder(Root + "/Controller");
            EnsureFolder(Root + "/Prefab");
            EnsureFolder(Root + "/Scene");
            EnsureFolder(Root + "/Validation");

            AnimatorController controller = CreateController();
            CreatePrefab(controller);
            CreateScene();

            AutoLocomotionValidationReport report = ValidateStructure(controller, before);
            File.WriteAllText(ToAbsolutePath(ReportPath), JsonUtility.ToJson(report, true));
            AssetDatabase.ImportAsset(ReportPath, ImportAssetOptions.ForceSynchronousImport);
            AssetDatabase.SaveAssets();
            if (!report.structuralValidationPassed)
            {
                throw new InvalidOperationException("AutoLocomotion structural validation failed.");
            }

            string marker = Path.Combine(Directory.GetParent(Application.dataPath)?.FullName
                ?? Application.dataPath, "Library", MarkerName);
            File.WriteAllText(marker, "Run isolated AutoLocomotion validation once.");
        }

        private static AnimatorController CreateController()
        {
            AnimatorController source = AssetDatabase.LoadAssetAtPath<AnimatorController>(SourceControllerPath);
            if (source == null) throw new InvalidOperationException("Manual DoubleL controller is missing.");
            Dictionary<string, Motion> motions = source.layers[0].stateMachine.states
                .ToDictionary(child => child.state.name, child => child.state.motion, StringComparer.Ordinal);
            foreach (string required in new[] { "CombatIdle_B1", "RunForward", "RollFront" })
            {
                if (!motions.TryGetValue(required, out Motion motion) || motion == null)
                    throw new InvalidOperationException("Required source state/clip is missing: " + required);
            }

            AssetDatabase.DeleteAsset(ControllerPath);
            AnimatorController controller = AnimatorController.CreateAnimatorControllerAtPath(ControllerPath);
            controller.AddParameter("IsMoving", AnimatorControllerParameterType.Bool);
            controller.AddParameter("RunPlaybackSpeed", AnimatorControllerParameterType.Float);
            controller.AddParameter("PlayRoll", AnimatorControllerParameterType.Trigger);
            AnimatorStateMachine machine = controller.layers[0].stateMachine;
            AnimatorState idle = machine.AddState("CombatIdle_B1", new Vector3(260f, 80f, 0f));
            AnimatorState run = machine.AddState("RunForward", new Vector3(520f, 80f, 0f));
            AnimatorState roll = machine.AddState("RollFront", new Vector3(390f, 230f, 0f));
            idle.motion = motions["CombatIdle_B1"];
            run.motion = motions["RunForward"];
            run.speedParameterActive = true;
            run.speedParameter = "RunPlaybackSpeed";
            roll.motion = motions["RollFront"];
            machine.defaultState = idle;

            AddConditionTransition(idle, run, AnimatorConditionMode.If, "IsMoving", false);
            AddConditionTransition(run, idle, AnimatorConditionMode.IfNot, "IsMoving", false);
            AnimatorStateTransition rollEntry = machine.AddAnyStateTransition(roll);
            ConfigureTransition(rollEntry, false);
            rollEntry.canTransitionToSelf = false;
            rollEntry.AddCondition(AnimatorConditionMode.If, 0f, "PlayRoll");
            AddConditionTransition(roll, run, AnimatorConditionMode.If, "IsMoving", true);
            AddConditionTransition(roll, idle, AnimatorConditionMode.IfNot, "IsMoving", true);

            EditorUtility.SetDirty(controller);
            EditorUtility.SetDirty(machine);
            foreach (AnimatorState state in new[] { idle, run, roll }) EditorUtility.SetDirty(state);
            AssetDatabase.SaveAssets();
            return controller;
        }

        private static void AddConditionTransition(AnimatorState source, AnimatorState destination,
            AnimatorConditionMode mode, string parameter, bool exitTime)
        {
            AnimatorStateTransition transition = source.AddTransition(destination);
            ConfigureTransition(transition, exitTime);
            transition.AddCondition(mode, 0f, parameter);
        }

        private static void ConfigureTransition(AnimatorStateTransition transition, bool exitTime)
        {
            transition.hasExitTime = exitTime;
            transition.exitTime = exitTime ? 0.92f : 0f;
            transition.hasFixedDuration = true;
            transition.duration = 0.08f;
            transition.interruptionSource = TransitionInterruptionSource.None;
            transition.canTransitionToSelf = false;
        }

        private static void CreatePrefab(AnimatorController controller)
        {
            AssetDatabase.DeleteAsset(PrefabPath);
            if (!AssetDatabase.CopyAsset(SourcePrefabPath, PrefabPath))
                throw new InvalidOperationException("Could not copy the manual DoubleL prefab.");

            GameObject root = PrefabUtility.LoadPrefabContents(PrefabPath);
            try
            {
                root.name = "Qusap75K_DoubleL_AutoLocomotionTest";
                foreach (MonoBehaviour behaviour in root.GetComponents<MonoBehaviour>())
                {
                    if (behaviour != null && behaviour.GetType().FullName ==
                        "Qusap.NewQusap75KAnimationTest.Playable.DoubleL.QusapDoubleLLocomotionInput")
                    {
                        Object.DestroyImmediate(behaviour);
                    }
                }
                Transform visual = FindDeepChild(root.transform, "Qusap75K_Visual");
                Animator animator = visual != null ? visual.GetComponent<Animator>() : null;
                Rigidbody body = root.GetComponent<Rigidbody>();
                if (animator == null || body == null) throw new InvalidOperationException("Copied prefab lacks Animator or Rigidbody.");
                animator.runtimeAnimatorController = controller;
                animator.applyRootMotion = false;
                animator.cullingMode = AnimatorCullingMode.AlwaysAnimate;
                QusapDoubleLAutoLocomotionDriver driver = root.GetComponent<QusapDoubleLAutoLocomotionDriver>()
                    ?? root.AddComponent<QusapDoubleLAutoLocomotionDriver>();
                driver.Configure(body, animator);
                PrefabUtility.SaveAsPrefabAsset(root, PrefabPath);
            }
            finally
            {
                PrefabUtility.UnloadPrefabContents(root);
            }
        }

        private static void CreateScene()
        {
            AssetDatabase.DeleteAsset(ScenePath);
            if (!AssetDatabase.CopyAsset(SourceScenePath, ScenePath))
                throw new InvalidOperationException("Could not copy the manual DoubleL scene.");
            Scene scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
            GameObject oldPlayer = scene.GetRootGameObjects().FirstOrDefault(root =>
                GetPrefabAssetPath(root) == SourcePrefabPath
                || root.name.StartsWith("Player1_Qusap75K_DoubleL", StringComparison.Ordinal));
            if (oldPlayer == null) throw new InvalidOperationException("Player 1 was not found in copied scene.");
            GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath);
            GameObject newPlayer = PrefabUtility.InstantiatePrefab(prefab, scene) as GameObject;
            if (newPlayer == null) throw new InvalidOperationException("Could not instantiate AutoLocomotion prefab.");
            newPlayer.name = "Player1_Qusap75K_DoubleL_AutoLocomotionTest";
            newPlayer.transform.SetPositionAndRotation(oldPlayer.transform.position, oldPlayer.transform.rotation);
            newPlayer.transform.localScale = oldPlayer.transform.localScale;
            newPlayer.transform.SetSiblingIndex(oldPlayer.transform.GetSiblingIndex());
            ReplaceExternalSceneReferences(scene, oldPlayer, newPlayer);
            Object.DestroyImmediate(oldPlayer);

            foreach (GameObject item in scene.GetRootGameObjects().Where(root =>
                root.GetComponent<Qusap.NewQusap75KAnimationTest.Playable.DoubleL.Validation.QusapDoubleLLocomotionValidation>() != null
                || root.name.Contains("DoubleL_Locomotion_Instructions")).ToArray())
            {
                Object.DestroyImmediate(item);
            }
            new GameObject("Qusap75K_DoubleL_AutoLocomotion_Validation")
                .AddComponent<QusapDoubleLAutoLocomotionValidation>();
            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene, ScenePath);
            AssetDatabase.SaveAssets();
        }

        private static AutoLocomotionValidationReport ValidateStructure(AnimatorController controller,
            Dictionary<string, string> protectedHashes)
        {
            bool protectedUnchanged = protectedHashes.All(pair => HashAssetAndMeta(pair.Key) == pair.Value);
            GameObject source = PrefabUtility.LoadPrefabContents(SourcePrefabPath);
            GameObject target = PrefabUtility.LoadPrefabContents(PrefabPath);
            bool systemsPreserved;
            bool rootMotionOff;
            try
            {
                string[] preservedTypes =
                {
                    "Qusap.QusapInputReader", "Qusap.QusapHorizontalMotor", "Qusap.QusapGroundSensor",
                    "Qusap.QusapVerticalMotor", "Qusap.QusapWallSensor", "Qusap.QusapDashMotor",
                    "UnityEngine.Rigidbody", "UnityEngine.CapsuleCollider"
                };
                systemsPreserved = preservedTypes.All(typeName => ComponentsMatch(source, target, typeName));
                Transform visual = FindDeepChild(target.transform, "Qusap75K_Visual");
                Animator animator = visual != null ? visual.GetComponent<Animator>() : null;
                rootMotionOff = animator != null && !animator.applyRootMotion
                    && animator.runtimeAnimatorController == controller;
            }
            finally
            {
                PrefabUtility.UnloadPrefabContents(source);
                PrefabUtility.UnloadPrefabContents(target);
            }

            Scene scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
            GameObject playerTwo = scene.GetRootGameObjects().FirstOrDefault(item => item.name == "Player2_Qusap");
            bool playerTwoUntouched = GetPrefabAssetPath(playerTwo) == "Assets/_Qusap/Prefabs/QusapCombatPlayer.prefab";
            Dictionary<string, AnimatorState> states = controller.layers[0].stateMachine.states
                .ToDictionary(child => child.state.name, child => child.state, StringComparer.Ordinal);
            bool stateSetup = states.Count == 3 && states.ContainsKey("CombatIdle_B1")
                && states.ContainsKey("RunForward") && states.ContainsKey("RollFront")
                && states["RunForward"].speedParameterActive
                && states["RunForward"].speedParameter == "RunPlaybackSpeed"
                && states["RollFront"].transitions.Length == 2
                && controller.layers[0].stateMachine.anyStateTransitions.Length == 1;

            var report = new AutoLocomotionValidationReport
            {
                generatedAtUtc = DateTime.UtcNow.ToString("O"),
                scene = ScenePath,
                prefab = PrefabPath,
                controller = ControllerPath,
                driver = DriverPath,
                structuralValidationPassed = protectedUnchanged && systemsPreserved && rootMotionOff
                    && playerTwoUntouched && stateSetup,
                rootMotionDisabled = rootMotionOff,
                movementPhysicsAndInputsPreserved = protectedUnchanged && systemsPreserved,
                playerTwoUntouched = playerTwoUntouched,
                enterRunThreshold = 0.10f,
                exitRunThreshold = 0.05f,
                runPlaybackSpeed = 1.0f,
                consoleWarnings = Array.Empty<string>(),
                consoleErrors = Array.Empty<string>(),
                notes = "Isolated copy of the validated manual DoubleL test. No source asset was modified."
            };
            report.passed = false;
            return report;
        }

        private static bool ComponentsMatch(GameObject source, GameObject target, string fullName)
        {
            Component a = source.GetComponents<Component>().FirstOrDefault(item => item != null
                && item.GetType().FullName == fullName);
            Component b = target.GetComponents<Component>().FirstOrDefault(item => item != null
                && item.GetType().FullName == fullName);
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

        private static string GetPrefabAssetPath(GameObject instance)
        {
            if (instance == null) return string.Empty;
            Object source = PrefabUtility.GetCorrespondingObjectFromSource(instance);
            return source != null ? AssetDatabase.GetAssetPath(source) : string.Empty;
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

        private static string HashAssetAndMeta(string assetPath)
        {
            string absolute = ToAbsolutePath(assetPath);
            using SHA256 sha = SHA256.Create();
            byte[] asset = File.ReadAllBytes(absolute);
            byte[] meta = File.Exists(absolute + ".meta") ? File.ReadAllBytes(absolute + ".meta") : Array.Empty<byte>();
            return BitConverter.ToString(sha.ComputeHash(asset.Concat(meta).ToArray())).Replace("-", string.Empty);
        }

        private static string ToAbsolutePath(string assetPath) => Path.GetFullPath(Path.Combine(
            Application.dataPath, assetPath.Substring("Assets".Length).TrimStart('/', '\\')));
    }
}
