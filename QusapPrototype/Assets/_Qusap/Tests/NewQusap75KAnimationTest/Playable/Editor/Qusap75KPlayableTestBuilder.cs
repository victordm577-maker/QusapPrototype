using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using UnityEditor;
using UnityEditor.Animations;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Qusap.NewQusap75KAnimationTest.Playable.Editor
{
    [InitializeOnLoad]
    internal static class Qusap75KPlayableTestAutoRunner
    {
        private const string Root = "Assets/_Qusap/Tests/NewQusap75KAnimationTest/Playable";
        private const string TriggerPath = Root + "/Validation/BuildRequested.txt";
        private const string ResultPath = Root + "/Validation/BuildResult.txt";
        private const string RuntimeLogPath = Root + "/Validation/PlayModeConsole.log";
        private const string ScenePath = Root + "/Scene/CombatPlayground_Qusap75K_PlayableTest.unity";
        private const string PendingKey = "Qusap.Qusap75KPlayableTest.PlayModePending";

        private static double playModeStartedAt;

        static Qusap75KPlayableTestAutoRunner()
        {
            EditorApplication.delayCall += RunWhenReady;
            EditorApplication.playModeStateChanged += OnPlayModeStateChanged;
        }

        private static void RunWhenReady()
        {
            string trigger = ToAbsolutePath(TriggerPath);
            if (!File.Exists(trigger))
            {
                return;
            }

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

            Scene activeScene = SceneManager.GetActiveScene();
            if (activeScene.IsValid() && activeScene.isDirty)
            {
                WriteResult("BLOCKED: The currently open scene has unsaved changes; no project asset was modified.");
                return;
            }

            try
            {
                Qusap75KPlayableTestBuilder.BuildAndValidate();
                WriteResult("BUILD_PASSED; PLAY_MODE_PENDING");
                SessionState.SetBool(PendingKey, true);
                Debug.Log("QUSAP75K_PLAYABLE_PLAYMODE_START");
                EditorApplication.isPlaying = true;
            }
            catch (Exception exception)
            {
                WriteResult("FAILED: " + exception);
                Debug.LogException(exception);
            }
        }

        private static void OnPlayModeStateChanged(PlayModeStateChange state)
        {
            if (!SessionState.GetBool(PendingKey, false))
            {
                return;
            }

            if (state == PlayModeStateChange.EnteredPlayMode)
            {
                playModeStartedAt = EditorApplication.timeSinceStartup;
                EditorApplication.update -= StopPlayModeAfterObservation;
                EditorApplication.update += StopPlayModeAfterObservation;
                return;
            }

            if (state == PlayModeStateChange.EnteredEditMode)
            {
                EditorApplication.update -= StopPlayModeAfterObservation;
                SessionState.SetBool(PendingKey, false);
                EditorApplication.delayCall += FinalizePlayModeValidation;
            }
        }

        private static void StopPlayModeAfterObservation()
        {
            if (!EditorApplication.isPlaying || EditorApplication.timeSinceStartup - playModeStartedAt < 2.0d)
            {
                return;
            }

            EditorApplication.update -= StopPlayModeAfterObservation;
            EditorApplication.isPlaying = false;
        }

        private static void FinalizePlayModeValidation()
        {
            try
            {
                AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
                string runtimeLog = ToAbsolutePath(RuntimeLogPath);
                string[] lines = File.Exists(runtimeLog)
                    ? File.ReadAllLines(runtimeLog).Skip(1).Where(line => !string.IsNullOrWhiteSpace(line)).ToArray()
                    : Array.Empty<string>();
                string[] errors = lines.Where(line => line.StartsWith("ERROR|", StringComparison.Ordinal)
                    || line.StartsWith("ASSERT|", StringComparison.Ordinal)
                    || line.StartsWith("EXCEPTION|", StringComparison.Ordinal)).ToArray();
                string[] warnings = lines.Where(line => line.StartsWith("WARNING|", StringComparison.Ordinal)).ToArray();

                Qusap75KPlayableTestBuilder.CompletePlayModeValidation(
                    File.Exists(runtimeLog), warnings, errors);

                EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
                Selection.activeObject = AssetDatabase.LoadAssetAtPath<SceneAsset>(ScenePath);
                bool passed = File.Exists(runtimeLog) && errors.Length == 0;
                WriteResult(passed
                    ? $"PASSED; PLAY_MODE_STARTED; ERRORS=0; WARNINGS={warnings.Length}"
                    : $"FAILED_PLAY_MODE; LOG_PRESENT={File.Exists(runtimeLog)}; ERRORS={errors.Length}; WARNINGS={warnings.Length}");
                Debug.Log(passed
                    ? "QUSAP75K_PLAYABLE_PLAYMODE_PASSED"
                    : "QUSAP75K_PLAYABLE_PLAYMODE_FAILED");
            }
            catch (Exception exception)
            {
                WriteResult("FAILED_FINALIZATION: " + exception);
                Debug.LogException(exception);
            }
        }

        private static void WriteResult(string text)
        {
            File.WriteAllText(ToAbsolutePath(ResultPath), text);
            AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
        }

        private static string ToAbsolutePath(string assetPath)
        {
            string relative = assetPath.Substring("Assets".Length).TrimStart('/', '\\');
            return Path.GetFullPath(Path.Combine(Application.dataPath, relative));
        }
    }

    public static class Qusap75KPlayableTestBuilder
    {
        private const string Root = "Assets/_Qusap/Tests/NewQusap75KAnimationTest/Playable";
        private const string SourceScenePath = "Assets/_Qusap/Scenes/CombatPlayground.unity";
        private const string SourcePlayerPrefabPath = "Assets/_Qusap/Prefabs/QusapCombatPlayer.prefab";
        private const string SourceVisualPrefabPath =
            "Assets/_Qusap/Tests/NewQusap75KAnimationTest/Qusap75K_AnimationTest.prefab";
        private const string AnimationPath =
            "Assets/_Qusap/Tests/NewQusap75KAnimationTest/Animation/RM_Roll_front.fbx";
        private const string ControllerPath = Root + "/Controller/Qusap75K_PlayableTest.controller";
        private const string PrefabPath = Root + "/Prefab/Qusap75K_PlayableTest.prefab";
        private const string ScenePath = Root + "/Scene/CombatPlayground_Qusap75K_PlayableTest.unity";
        private const string ReportPath = Root + "/Validation/PlayableValidation.json";
        private const string InputAssetPath = "Assets/_Qusap/Settings/QusapControls.inputactions";

        [Serializable]
        public sealed class PlayableValidationReport
        {
            public string generatedAtUtc;
            public string sourceScene;
            public string copiedScene;
            public string sourcePlayerPrefab;
            public string testPlayerPrefab;
            public string modifiedPlayer;
            public string untouchedComparisonPlayer;
            public string[] preservedRootComponents;
            public string newVisualLocalPosition;
            public string newVisualLocalEulerAngles;
            public string newVisualLocalScale;
            public string keyboardRollBinding;
            public string xboxRollBinding;
            public string rejectedXboxBindingReason;
            public bool sourceSceneUnchanged;
            public bool sourcePrefabUnchanged;
            public bool inputActionsUnchanged;
            public bool rigidbodyMatchesSource;
            public bool collidersMatchSource;
            public bool gameplayComponentValuesMatchSource;
            public bool playerRootTransformMatchesSource;
            public bool oldRenderersRetainedAndDisabled;
            public bool animatorOnlyOnVisualHierarchy;
            public bool applyRootMotion;
            public bool restIsDefaultAndMotionless;
            public bool rollUsesExpectedClip;
            public bool rollLoopDisabled;
            public bool rollReturnsToRestAtExitTimeOne;
            public bool inputComponentOnlyOnTestPrefab;
            public bool playerTwoStillUsesOriginalPrefab;
            public bool sceneStartedInPlayMode;
            public int playModeWarningCount;
            public int playModeErrorCount;
            public string[] playModeWarnings;
            public string[] playModeErrors;
            public bool passed;
        }

        [MenuItem("Tools/Qusap/Tests/Build Qusap 75K Playable Test")]
        public static void BuildAndValidate()
        {
            string sourceSceneHash = ComputeHash(SourceScenePath);
            string sourcePrefabHash = ComputeHash(SourcePlayerPrefabPath);
            string inputAssetHash = ComputeHash(InputAssetPath);

            EnsureFolder(Root + "/Controller");
            EnsureFolder(Root + "/Prefab");
            EnsureFolder(Root + "/Scene");
            EnsureFolder(Root + "/Validation");

            AnimationClip clip = FindRollClip();
            AnimatorController controller = CreateController(clip);
            VisualTransformData visualTransform = CreatePlayablePrefab(controller);
            CreatePlayableScene();

            PlayableValidationReport report = Validate(
                controller,
                clip,
                visualTransform,
                sourceSceneHash,
                sourcePrefabHash,
                inputAssetHash);
            WriteReport(report);
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);

            if (!report.passed)
            {
                throw new InvalidOperationException("Playable validation failed. See " + ReportPath);
            }
        }

        public static void CompletePlayModeValidation(bool logPresent, string[] warnings, string[] errors)
        {
            string absolute = ToAbsolutePath(ReportPath);
            PlayableValidationReport report = File.Exists(absolute)
                ? JsonUtility.FromJson<PlayableValidationReport>(File.ReadAllText(absolute))
                : null;
            if (report == null)
            {
                throw new InvalidOperationException("The structural validation report is missing.");
            }

            report.sceneStartedInPlayMode = logPresent;
            report.playModeWarnings = warnings ?? Array.Empty<string>();
            report.playModeErrors = errors ?? Array.Empty<string>();
            report.playModeWarningCount = report.playModeWarnings.Length;
            report.playModeErrorCount = report.playModeErrors.Length;
            report.passed = report.passed && logPresent && report.playModeErrorCount == 0;
            WriteReport(report);
            AssetDatabase.SaveAssets();
        }

        private sealed class VisualTransformData
        {
            public Vector3 localPosition;
            public Vector3 localEulerAngles;
            public Vector3 localScale;
        }

        private static AnimationClip FindRollClip()
        {
            AnimationClip clip = AssetDatabase.LoadAllAssetsAtPath(AnimationPath)
                .OfType<AnimationClip>()
                .FirstOrDefault(candidate => candidate.name == "RM_Roll_front");
            if (clip == null)
            {
                throw new InvalidOperationException("RM_Roll_front was not found in the imported animation FBX.");
            }

            return clip;
        }

        private static AnimatorController CreateController(AnimationClip clip)
        {
            AssetDatabase.DeleteAsset(ControllerPath);
            AnimatorController controller = AnimatorController.CreateAnimatorControllerAtPath(ControllerPath);
            controller.AddParameter("PlayRoll", AnimatorControllerParameterType.Trigger);

            AnimatorStateMachine machine = controller.layers[0].stateMachine;
            AnimatorState rest = machine.AddState("Rest", new Vector3(250f, 100f, 0f));
            rest.motion = null;
            machine.defaultState = rest;

            AnimatorState roll = machine.AddState("RollFront", new Vector3(500f, 100f, 0f));
            roll.motion = clip;

            AnimatorStateTransition enter = machine.AddAnyStateTransition(roll);
            enter.hasExitTime = false;
            enter.hasFixedDuration = true;
            enter.duration = 0.03f;
            enter.canTransitionToSelf = false;
            enter.AddCondition(AnimatorConditionMode.If, 0f, "PlayRoll");

            AnimatorStateTransition exit = roll.AddTransition(rest);
            exit.hasExitTime = true;
            exit.exitTime = 1f;
            exit.hasFixedDuration = true;
            exit.duration = 0.03f;

            EditorUtility.SetDirty(controller);
            EditorUtility.SetDirty(machine);
            EditorUtility.SetDirty(rest);
            EditorUtility.SetDirty(roll);
            AssetDatabase.SaveAssets();
            return controller;
        }

        private static VisualTransformData CreatePlayablePrefab(AnimatorController controller)
        {
            AssetDatabase.DeleteAsset(PrefabPath);
            if (!AssetDatabase.CopyAsset(SourcePlayerPrefabPath, PrefabPath))
            {
                throw new InvalidOperationException("Unity could not copy the playable player prefab.");
            }

            GameObject root = PrefabUtility.LoadPrefabContents(PrefabPath);
            try
            {
                root.name = "Qusap75K_PlayableTest";
                Transform alignment = FindDeepChild(root.transform, "PlayerVisual_ModularAlignment");
                Transform facingPivot = FindDeepChild(root.transform, "ModularFacingPivot");
                if (alignment == null || facingPivot == null)
                {
                    throw new InvalidOperationException("The source player's visual alignment hierarchy is incomplete.");
                }

                Renderer[] retainedRenderers = root.GetComponentsInChildren<Renderer>(true);
                Renderer[] referenceRenderers = alignment.GetComponentsInChildren<Renderer>(true)
                    .Where(renderer => renderer.enabled && renderer.gameObject.activeInHierarchy)
                    .ToArray();
                if (referenceRenderers.Length == 0)
                {
                    throw new InvalidOperationException("The active original visual has no enabled renderer for alignment.");
                }

                Bounds referenceBounds = CalculateBounds(referenceRenderers);
                GameObject sourceVisual = AssetDatabase.LoadAssetAtPath<GameObject>(SourceVisualPrefabPath);
                GameObject visual = PrefabUtility.InstantiatePrefab(sourceVisual, root.scene) as GameObject;
                if (visual == null)
                {
                    throw new InvalidOperationException("Unity could not instantiate the Qusap 75K visual.");
                }

                visual.name = "Qusap75K_Visual";
                visual.transform.SetParent(facingPivot, false);
                visual.transform.localPosition = Vector3.zero;
                visual.transform.localRotation = Quaternion.identity;
                visual.transform.localScale = Vector3.one;

                Animator animator = visual.GetComponent<Animator>();
                if (animator == null)
                {
                    throw new InvalidOperationException("The isolated Qusap 75K visual has no Animator.");
                }

                animator.runtimeAnimatorController = controller;
                animator.applyRootMotion = false;
                animator.cullingMode = AnimatorCullingMode.AlwaysAnimate;
                foreach (SkinnedMeshRenderer skinned in visual.GetComponentsInChildren<SkinnedMeshRenderer>(true))
                {
                    skinned.updateWhenOffscreen = true;
                }

                Renderer[] newRenderers = visual.GetComponentsInChildren<Renderer>(true);
                Bounds newBounds = CalculateBounds(newRenderers);
                float uniformScale = referenceBounds.size.y / Mathf.Max(0.0001f, newBounds.size.y);
                visual.transform.localScale = Vector3.one * uniformScale;
                newBounds = CalculateBounds(newRenderers);

                Vector3 worldDelta = new Vector3(
                    referenceBounds.center.x - newBounds.center.x,
                    referenceBounds.min.y - newBounds.min.y,
                    referenceBounds.center.z - newBounds.center.z);
                visual.transform.position += worldDelta;

                foreach (Renderer renderer in retainedRenderers)
                {
                    renderer.enabled = false;
                }

                var testInput = root.AddComponent<Qusap.NewQusap75KAnimationTest.Playable.QusapPlayableAnimationTestInput>();
                testInput.Configure(animator);

                var transformData = new VisualTransformData
                {
                    localPosition = visual.transform.localPosition,
                    localEulerAngles = visual.transform.localEulerAngles,
                    localScale = visual.transform.localScale
                };

                PrefabUtility.SaveAsPrefabAsset(root, PrefabPath);
                return transformData;
            }
            finally
            {
                PrefabUtility.UnloadPrefabContents(root);
            }
        }

        private static void CreatePlayableScene()
        {
            AssetDatabase.DeleteAsset(ScenePath);
            if (!AssetDatabase.CopyAsset(SourceScenePath, ScenePath))
            {
                throw new InvalidOperationException("Unity could not copy CombatPlayground.");
            }

            Scene scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
            GameObject oldPlayer = scene.GetRootGameObjects().FirstOrDefault(root => root.name == "Player1_Qusap");
            if (oldPlayer == null)
            {
                throw new InvalidOperationException("Player1_Qusap was not found in the copied CombatPlayground scene.");
            }

            GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath);
            GameObject newPlayer = PrefabUtility.InstantiatePrefab(prefab, scene) as GameObject;
            if (newPlayer == null)
            {
                throw new InvalidOperationException("Unity could not instantiate the test player prefab in the copied scene.");
            }

            int siblingIndex = oldPlayer.transform.GetSiblingIndex();
            newPlayer.name = "Player1_Qusap75K_PlayableTest";
            newPlayer.transform.SetPositionAndRotation(oldPlayer.transform.position, oldPlayer.transform.rotation);
            newPlayer.transform.localScale = oldPlayer.transform.localScale;
            newPlayer.transform.SetSiblingIndex(siblingIndex);

            ReplaceExternalSceneReferences(scene, oldPlayer, newPlayer);
            UnityEngine.Object.DestroyImmediate(oldPlayer);
            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene, ScenePath);
            AssetDatabase.SaveAssets();
        }

        private static void ReplaceExternalSceneReferences(Scene scene, GameObject oldRoot, GameObject newRoot)
        {
            foreach (GameObject sceneRoot in scene.GetRootGameObjects())
            {
                foreach (Component component in sceneRoot.GetComponentsInChildren<Component>(true))
                {
                    if (component == null || component.transform.IsChildOf(oldRoot.transform)
                        || component.transform.IsChildOf(newRoot.transform))
                    {
                        continue;
                    }

                    var serialized = new SerializedObject(component);
                    SerializedProperty property = serialized.GetIterator();
                    bool changed = false;
                    while (property.Next(true))
                    {
                        if (property.propertyType != SerializedPropertyType.ObjectReference
                            || property.objectReferenceValue == null)
                        {
                            continue;
                        }

                        UnityEngine.Object replacement = MapHierarchyReference(
                            property.objectReferenceValue, oldRoot, newRoot);
                        if (replacement == null)
                        {
                            continue;
                        }

                        property.objectReferenceValue = replacement;
                        changed = true;
                    }

                    if (changed)
                    {
                        serialized.ApplyModifiedPropertiesWithoutUndo();
                        EditorUtility.SetDirty(component);
                    }
                }
            }
        }

        private static UnityEngine.Object MapHierarchyReference(
            UnityEngine.Object sourceReference,
            GameObject oldRoot,
            GameObject newRoot)
        {
            GameObject sourceObject = sourceReference as GameObject;
            Component sourceComponent = sourceReference as Component;
            Transform sourceTransform = sourceObject != null ? sourceObject.transform : sourceComponent?.transform;
            if (sourceTransform == null || !sourceTransform.IsChildOf(oldRoot.transform))
            {
                return null;
            }

            string path = AnimationUtility.CalculateTransformPath(sourceTransform, oldRoot.transform);
            Transform targetTransform = string.IsNullOrEmpty(path) ? newRoot.transform : newRoot.transform.Find(path);
            if (targetTransform == null)
            {
                throw new InvalidOperationException("Could not map scene reference at player path: " + path);
            }

            if (sourceObject != null)
            {
                return targetTransform.gameObject;
            }

            if (sourceComponent is Transform)
            {
                return targetTransform;
            }

            Type type = sourceComponent.GetType();
            Component[] sourceMatches = sourceTransform.GetComponents(type);
            Component[] targetMatches = targetTransform.GetComponents(type);
            int index = Array.IndexOf(sourceMatches, sourceComponent);
            if (index < 0 || index >= targetMatches.Length)
            {
                throw new InvalidOperationException("Could not map component reference: " + type.FullName);
            }

            return targetMatches[index];
        }

        private static PlayableValidationReport Validate(
            AnimatorController controller,
            AnimationClip clip,
            VisualTransformData visualTransform,
            string sourceSceneHash,
            string sourcePrefabHash,
            string inputAssetHash)
        {
            bool rigidbodyMatches;
            bool collidersMatch;
            bool componentValuesMatch;
            bool rootTransformMatches;
            bool oldRenderersDisabled;
            bool animatorPlacement;
            bool applyRootMotion;
            bool inputOnlyOnTest;
            string[] componentNames;

            GameObject sourceRoot = PrefabUtility.LoadPrefabContents(SourcePlayerPrefabPath);
            GameObject testRoot = PrefabUtility.LoadPrefabContents(PrefabPath);
            try
            {
                Type inputType = typeof(Qusap.NewQusap75KAnimationTest.Playable.QusapPlayableAnimationTestInput);
                Component[] sourceComponents = sourceRoot.GetComponents<Component>();
                Component[] testComponents = testRoot.GetComponents<Component>()
                    .Where(component => component != null && component.GetType() != inputType)
                    .ToArray();
                componentNames = sourceComponents.Where(component => component != null)
                    .Select(component => component.GetType().FullName).ToArray();

                bool componentTypesMatch = sourceComponents.Select(component => component?.GetType())
                    .SequenceEqual(testComponents.Select(component => component?.GetType()));
                componentValuesMatch = componentTypesMatch;
                if (componentValuesMatch)
                {
                    for (int i = 0; i < sourceComponents.Length; i++)
                    {
                        Type type = sourceComponents[i].GetType();
                        if (!typeof(MonoBehaviour).IsAssignableFrom(type))
                        {
                            continue;
                        }

                        if (!PrimitiveSerializedValuesMatch(sourceComponents[i], testComponents[i]))
                        {
                            componentValuesMatch = false;
                            break;
                        }
                    }
                }

                Rigidbody sourceBody = sourceRoot.GetComponent<Rigidbody>();
                Rigidbody testBody = testRoot.GetComponent<Rigidbody>();
                rigidbodyMatches = sourceBody != null && testBody != null
                    && Mathf.Approximately(sourceBody.mass, testBody.mass)
                    && Mathf.Approximately(sourceBody.linearDamping, testBody.linearDamping)
                    && Mathf.Approximately(sourceBody.angularDamping, testBody.angularDamping)
                    && sourceBody.useGravity == testBody.useGravity
                    && sourceBody.isKinematic == testBody.isKinematic
                    && sourceBody.interpolation == testBody.interpolation
                    && sourceBody.constraints == testBody.constraints
                    && sourceBody.collisionDetectionMode == testBody.collisionDetectionMode
                    && sourceBody.centerOfMass == testBody.centerOfMass;

                Collider[] sourceColliders = sourceRoot.GetComponentsInChildren<Collider>(true);
                Collider[] testColliders = testRoot.GetComponentsInChildren<Collider>(true);
                collidersMatch = sourceColliders.Length == testColliders.Length;
                if (collidersMatch)
                {
                    for (int i = 0; i < sourceColliders.Length; i++)
                    {
                        string sourcePath = AnimationUtility.CalculateTransformPath(
                            sourceColliders[i].transform, sourceRoot.transform);
                        Collider matching = testColliders.FirstOrDefault(candidate =>
                            candidate.GetType() == sourceColliders[i].GetType()
                            && AnimationUtility.CalculateTransformPath(candidate.transform, testRoot.transform) == sourcePath);
                        if (matching == null || !ColliderValuesMatch(sourceColliders[i], matching))
                        {
                            collidersMatch = false;
                            break;
                        }
                    }
                }

                rootTransformMatches = testRoot.transform.localPosition == sourceRoot.transform.localPosition
                    && testRoot.transform.localRotation == sourceRoot.transform.localRotation
                    && testRoot.transform.localScale == sourceRoot.transform.localScale;

                Transform visual = FindDeepChild(testRoot.transform, "Qusap75K_Visual");
                Renderer[] oldRenderers = testRoot.GetComponentsInChildren<Renderer>(true)
                    .Where(renderer => visual == null || !renderer.transform.IsChildOf(visual))
                    .ToArray();
                oldRenderersDisabled = oldRenderers.Length > 0 && oldRenderers.All(renderer => !renderer.enabled);

                Animator[] animators = testRoot.GetComponentsInChildren<Animator>(true);
                Animator playableAnimator = visual != null ? visual.GetComponent<Animator>() : null;
                animatorPlacement = testRoot.GetComponent<Animator>() == null
                    && playableAnimator != null
                    && animators.All(animator => animator.transform != testRoot.transform);
                applyRootMotion = playableAnimator != null && playableAnimator.applyRootMotion;

                var sourceInputs = sourceRoot.GetComponentsInChildren(
                    typeof(Qusap.NewQusap75KAnimationTest.Playable.QusapPlayableAnimationTestInput), true);
                var testInputs = testRoot.GetComponentsInChildren(
                    typeof(Qusap.NewQusap75KAnimationTest.Playable.QusapPlayableAnimationTestInput), true);
                inputOnlyOnTest = sourceInputs.Length == 0 && testInputs.Length == 1
                    && testInputs[0].gameObject == testRoot;
            }
            finally
            {
                PrefabUtility.UnloadPrefabContents(sourceRoot);
                PrefabUtility.UnloadPrefabContents(testRoot);
            }

            AnimatorStateMachine machine = controller.layers[0].stateMachine;
            AnimatorState rest = machine.states.Select(state => state.state)
                .FirstOrDefault(state => state.name == "Rest");
            AnimatorState roll = machine.states.Select(state => state.state)
                .FirstOrDefault(state => state.name == "RollFront");
            bool restValid = rest != null && rest.motion == null && machine.defaultState == rest;
            bool rollClipValid = roll != null && roll.motion == clip;
            bool rollLoopDisabled = !AnimationUtility.GetAnimationClipSettings(clip).loopTime;
            AnimatorStateTransition rollExit = roll?.transitions
                .FirstOrDefault(transition => transition.destinationState == rest);
            bool returnsToRest = rollExit != null && rollExit.hasExitTime
                && Mathf.Approximately(rollExit.exitTime, 1f)
                && rollExit.conditions.Length == 0
                && rollExit.duration >= 0f && rollExit.duration <= 0.05f;
            AnimatorStateTransition rollEntry = machine.anyStateTransitions
                .FirstOrDefault(transition => transition.destinationState == roll);
            bool entryValid = rollEntry != null && rollEntry.conditions.Length == 1
                && rollEntry.conditions[0].parameter == "PlayRoll";

            Scene scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
            GameObject playerOne = scene.GetRootGameObjects()
                .FirstOrDefault(root => root.name == "Player1_Qusap75K_PlayableTest");
            GameObject playerTwo = scene.GetRootGameObjects()
                .FirstOrDefault(root => root.name == "Player2_Qusap");
            string playerOneSource = GetPrefabAssetPath(playerOne);
            string playerTwoSource = GetPrefabAssetPath(playerTwo);
            bool playerOneValid = playerOneSource == PrefabPath;
            bool playerTwoValid = playerTwoSource == SourcePlayerPrefabPath;

            var report = new PlayableValidationReport
            {
                generatedAtUtc = DateTime.UtcNow.ToString("O"),
                sourceScene = SourceScenePath,
                copiedScene = ScenePath,
                sourcePlayerPrefab = SourcePlayerPrefabPath,
                testPlayerPrefab = PrefabPath,
                modifiedPlayer = "Player1_Qusap75K_PlayableTest (Player1Keyboard)",
                untouchedComparisonPlayer = "Player2_Qusap (Player2Gamepad)",
                preservedRootComponents = componentNames,
                newVisualLocalPosition = FormatVector(visualTransform.localPosition),
                newVisualLocalEulerAngles = FormatVector(visualTransform.localEulerAngles),
                newVisualLocalScale = FormatVector(visualTransform.localScale),
                keyboardRollBinding = "R",
                xboxRollBinding = "D-pad Up",
                rejectedXboxBindingReason = "D-pad Down is already bound to Gameplay/Drop for Player2.",
                sourceSceneUnchanged = ComputeHash(SourceScenePath) == sourceSceneHash,
                sourcePrefabUnchanged = ComputeHash(SourcePlayerPrefabPath) == sourcePrefabHash,
                inputActionsUnchanged = ComputeHash(InputAssetPath) == inputAssetHash,
                rigidbodyMatchesSource = rigidbodyMatches,
                collidersMatchSource = collidersMatch,
                gameplayComponentValuesMatchSource = componentValuesMatch,
                playerRootTransformMatchesSource = rootTransformMatches,
                oldRenderersRetainedAndDisabled = oldRenderersDisabled,
                animatorOnlyOnVisualHierarchy = animatorPlacement,
                applyRootMotion = applyRootMotion,
                restIsDefaultAndMotionless = restValid,
                rollUsesExpectedClip = rollClipValid && entryValid,
                rollLoopDisabled = rollLoopDisabled,
                rollReturnsToRestAtExitTimeOne = returnsToRest,
                inputComponentOnlyOnTestPrefab = inputOnlyOnTest,
                playerTwoStillUsesOriginalPrefab = playerTwoValid,
                sceneStartedInPlayMode = false,
                playModeWarnings = Array.Empty<string>(),
                playModeErrors = Array.Empty<string>()
            };

            report.passed = playerOneValid
                && report.playerTwoStillUsesOriginalPrefab
                && report.sourceSceneUnchanged
                && report.sourcePrefabUnchanged
                && report.inputActionsUnchanged
                && report.rigidbodyMatchesSource
                && report.collidersMatchSource
                && report.gameplayComponentValuesMatchSource
                && report.playerRootTransformMatchesSource
                && report.oldRenderersRetainedAndDisabled
                && report.animatorOnlyOnVisualHierarchy
                && !report.applyRootMotion
                && report.restIsDefaultAndMotionless
                && report.rollUsesExpectedClip
                && report.rollLoopDisabled
                && report.rollReturnsToRestAtExitTimeOne
                && report.inputComponentOnlyOnTestPrefab;
            return report;
        }

        private static bool PrimitiveSerializedValuesMatch(Component source, Component target)
        {
            if (source == null || target == null || source.GetType() != target.GetType())
            {
                return false;
            }

            Dictionary<string, string> sourceValues = CapturePrimitiveSerializedValues(source);
            Dictionary<string, string> targetValues = CapturePrimitiveSerializedValues(target);
            return sourceValues.Count == targetValues.Count
                && sourceValues.All(pair => targetValues.TryGetValue(pair.Key, out string value)
                    && string.Equals(pair.Value, value, StringComparison.Ordinal));
        }

        private static Dictionary<string, string> CapturePrimitiveSerializedValues(UnityEngine.Object value)
        {
            var result = new Dictionary<string, string>(StringComparer.Ordinal);
            var serialized = new SerializedObject(value);
            SerializedProperty property = serialized.GetIterator();
            while (property.NextVisible(true))
            {
                string serializedValue = GetComparableValue(property);
                if (serializedValue != null)
                {
                    result[property.propertyPath] = property.propertyType + ":" + serializedValue;
                }
            }

            return result;
        }

        private static bool ColliderValuesMatch(Collider source, Collider target)
        {
            if (source == null || target == null || source.GetType() != target.GetType()
                || source.enabled != target.enabled
                || source.isTrigger != target.isTrigger
                || source.contactOffset != target.contactOffset
                || source.sharedMaterial != target.sharedMaterial)
            {
                return false;
            }

            if (source is CapsuleCollider sourceCapsule && target is CapsuleCollider targetCapsule)
            {
                return sourceCapsule.center == targetCapsule.center
                    && Mathf.Approximately(sourceCapsule.radius, targetCapsule.radius)
                    && Mathf.Approximately(sourceCapsule.height, targetCapsule.height)
                    && sourceCapsule.direction == targetCapsule.direction;
            }

            if (source is BoxCollider sourceBox && target is BoxCollider targetBox)
            {
                return sourceBox.center == targetBox.center && sourceBox.size == targetBox.size;
            }

            if (source is SphereCollider sourceSphere && target is SphereCollider targetSphere)
            {
                return sourceSphere.center == targetSphere.center
                    && Mathf.Approximately(sourceSphere.radius, targetSphere.radius);
            }

            return PrimitiveSerializedValuesMatch(source, target);
        }

        private static string GetComparableValue(SerializedProperty property)
        {
            switch (property.propertyType)
            {
                case SerializedPropertyType.Integer:
                case SerializedPropertyType.LayerMask:
                case SerializedPropertyType.Character:
                    return property.longValue.ToString(CultureInfo.InvariantCulture);
                case SerializedPropertyType.Boolean:
                    return property.boolValue ? "1" : "0";
                case SerializedPropertyType.Float:
                    return property.doubleValue.ToString("R", CultureInfo.InvariantCulture);
                case SerializedPropertyType.String:
                    return property.stringValue ?? string.Empty;
                case SerializedPropertyType.Color:
                    return property.colorValue.ToString("R");
                case SerializedPropertyType.Enum:
                    return property.enumValueIndex.ToString(CultureInfo.InvariantCulture);
                case SerializedPropertyType.Vector2:
                    return property.vector2Value.ToString("R");
                case SerializedPropertyType.Vector3:
                    return property.vector3Value.ToString("R");
                case SerializedPropertyType.Vector4:
                    return property.vector4Value.ToString("R");
                case SerializedPropertyType.Rect:
                    return property.rectValue.ToString("R");
                case SerializedPropertyType.Bounds:
                    return property.boundsValue.ToString("R");
                case SerializedPropertyType.Quaternion:
                    return property.quaternionValue.ToString("R");
                case SerializedPropertyType.Vector2Int:
                    return property.vector2IntValue.ToString();
                case SerializedPropertyType.Vector3Int:
                    return property.vector3IntValue.ToString();
                case SerializedPropertyType.RectInt:
                    return property.rectIntValue.ToString();
                case SerializedPropertyType.BoundsInt:
                    return property.boundsIntValue.ToString();
                default:
                    return null;
            }
        }

        private static Bounds CalculateBounds(IEnumerable<Renderer> renderers)
        {
            Renderer[] array = renderers.Where(renderer => renderer != null).ToArray();
            if (array.Length == 0)
            {
                throw new InvalidOperationException("Cannot calculate visual bounds without renderers.");
            }

            Bounds bounds = array[0].bounds;
            foreach (Renderer renderer in array.Skip(1))
            {
                bounds.Encapsulate(renderer.bounds);
            }

            return bounds;
        }

        private static Transform FindDeepChild(Transform root, string name)
        {
            if (root.name == name)
            {
                return root;
            }

            for (int i = 0; i < root.childCount; i++)
            {
                Transform result = FindDeepChild(root.GetChild(i), name);
                if (result != null)
                {
                    return result;
                }
            }

            return null;
        }

        private static string GetPrefabAssetPath(GameObject instance)
        {
            if (instance == null)
            {
                return string.Empty;
            }

            UnityEngine.Object source = PrefabUtility.GetCorrespondingObjectFromSource(instance);
            return source != null ? AssetDatabase.GetAssetPath(source) : string.Empty;
        }

        private static string FormatVector(Vector3 value)
        {
            return string.Format(CultureInfo.InvariantCulture, "({0:F6}, {1:F6}, {2:F6})", value.x, value.y, value.z);
        }

        private static void EnsureFolder(string path)
        {
            string[] parts = path.Split('/');
            string current = parts[0];
            for (int i = 1; i < parts.Length; i++)
            {
                string next = current + "/" + parts[i];
                if (!AssetDatabase.IsValidFolder(next))
                {
                    AssetDatabase.CreateFolder(current, parts[i]);
                }

                current = next;
            }
        }

        private static void WriteReport(PlayableValidationReport report)
        {
            File.WriteAllText(ToAbsolutePath(ReportPath), JsonUtility.ToJson(report, true));
            AssetDatabase.ImportAsset(ReportPath, ImportAssetOptions.ForceSynchronousImport);
        }

        private static string ComputeHash(string assetPath)
        {
            using (SHA256 sha = SHA256.Create())
            using (FileStream stream = File.OpenRead(ToAbsolutePath(assetPath)))
            {
                byte[] hash = sha.ComputeHash(stream);
                var builder = new StringBuilder(hash.Length * 2);
                foreach (byte value in hash)
                {
                    builder.Append(value.ToString("x2", CultureInfo.InvariantCulture));
                }

                return builder.ToString();
            }
        }

        private static string ToAbsolutePath(string assetPath)
        {
            string relative = assetPath.Substring("Assets".Length).TrimStart('/', '\\');
            return Path.GetFullPath(Path.Combine(Application.dataPath, relative));
        }
    }
}
