using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.Animations;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using Qusap.NewQusap75KAnimationTest.Playable.DoubleL.AutoLocomotion;
using Qusap.NewQusap75KAnimationTest.Playable.DoubleL.SwordIntegration.Validation;

namespace Qusap.NewQusap75KAnimationTest.Playable.DoubleL.SwordIntegration.Editor
{
    [InitializeOnLoad]
    internal static class QusapArmedRollIntegrationAutoRunner
    {
        private const string Root = "Assets/_Qusap/Tests/NewQusap75KAnimationTest/Playable/DoubleL/SwordIntegration";
        private const string ScenePath = Root + "/Scene/CombatPlayground_Qusap75K_DoubleL_SwordIntegrationTest.unity";
        private const string ReportPath = Root + "/Validation/ArmedRollValidation.json";
        private const string ResultPath = Root + "/Validation/ArmedRollBuildResult.txt";
        private const string BuildMarkerName = "QusapArmedRollIntegration.request";
        private const string PlayMarkerName = "QusapArmedRollValidation.request";
        private const string PendingKey = "Qusap.DoubleL.SwordIntegration.ArmedRollPending";
        private static double playStartedAt;

        static QusapArmedRollIntegrationAutoRunner()
        {
            EditorApplication.delayCall += RunWhenReady;
            EditorApplication.playModeStateChanged += OnPlayModeStateChanged;
        }

        private static string ProjectRoot => Directory.GetParent(Application.dataPath)?.FullName ?? Application.dataPath;
        private static string BuildMarkerPath => Path.Combine(ProjectRoot, "Library", BuildMarkerName);

        private static void RunWhenReady()
        {
            if (!File.Exists(BuildMarkerPath)) return;
            if (EditorApplication.isCompiling || EditorApplication.isUpdating)
            {
                EditorApplication.delayCall += RunWhenReady;
                return;
            }

            File.Delete(BuildMarkerPath);
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
                QusapArmedRollIntegrationBuilder.Build();
                File.WriteAllText(Path.Combine(ProjectRoot, "Library", PlayMarkerName),
                    "Run isolated armed-roll validation once.");
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
                EditorApplication.isPaused = false;
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
            if (EditorApplication.isPlaying && EditorApplication.timeSinceStartup - playStartedAt > 60d)
            {
                Debug.LogError("Armed-roll validation exceeded 60 seconds.");
                EditorApplication.ExitPlaymode();
            }
        }

        private static void FinalizeValidation()
        {
            try
            {
                AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
                ArmedRollValidationReport report = File.Exists(ToAbsolutePath(ReportPath))
                    ? JsonUtility.FromJson<ArmedRollValidationReport>(File.ReadAllText(ToAbsolutePath(ReportPath)))
                    : null;
                EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
                Selection.activeObject = AssetDatabase.LoadAssetAtPath<SceneAsset>(ScenePath);
                WriteResult(report != null && report.automatedChecksPassed
                    ? "AUTOMATED_CHECKS_PASSED; VISUAL_REVIEW_REQUIRED"
                    : "FAILED_PLAY_MODE; inspect ArmedRollValidation.json");
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

    public static class QusapArmedRollIntegrationBuilder
    {
        private const string TestRoot = "Assets/_Qusap/Tests/NewQusap75KAnimationTest";
        private const string Root = TestRoot + "/Playable/DoubleL/SwordIntegration";
        private const string RollPath = TestRoot + "/Imported/DoubleL/OneHandBase/Roll/Base/InPlace/1Hand_Base_Roll_F_InPlace.fbx";
        private const string ExpectedRollGuid = "1289a380d63c625488042925bae18207";
        private const string ExpectedAvatarGuid = "af0adcb624545c24e8c836635d07b769";
        private const string TposePath = TestRoot + "/Imported/DoubleL/OneHandBase/Model/T-Pose.fbx";
        private const string SourceControllerPath = TestRoot + "/Playable/DoubleL/AutoLocomotion/Controller/Qusap75K_DoubleL_AutoLocomotionTest.controller";
        private const string ControllerPath = Root + "/Controller/Qusap75K_DoubleL_ArmedRoll.controller";
        private const string PrefabPath = Root + "/Prefab/Qusap75K_DoubleL_SwordIntegrationTest.prefab";
        private const string ScenePath = Root + "/Scene/CombatPlayground_Qusap75K_DoubleL_SwordIntegrationTest.unity";

        public static void Build()
        {
            AssetDatabase.ImportAsset(RollPath,
                ImportAssetOptions.ForceSynchronousImport | ImportAssetOptions.ForceUpdate);
            ValidateImportedRoll();
            AnimatorController controller = CreateIsolatedController();
            AssignControllerToPrefab(controller);
            InstallSceneValidator();
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
        }

        private static void ValidateImportedRoll()
        {
            if (AssetDatabase.AssetPathToGUID(RollPath) != ExpectedRollGuid)
                throw new InvalidOperationException("The armed-roll GUID does not match the package GUID.");
            if (AssetDatabase.AssetPathToGUID(TposePath) != ExpectedAvatarGuid)
                throw new InvalidOperationException("The imported DoubleL T-Pose GUID does not match.");

            ModelImporter importer = AssetImporter.GetAtPath(RollPath) as ModelImporter;
            if (importer == null) throw new InvalidOperationException("The armed roll has no ModelImporter.");
            if (importer.animationType != ModelImporterAnimationType.Human)
                throw new InvalidOperationException("The armed roll is not Humanoid.");
            if (importer.avatarSetup != ModelImporterAvatarSetup.CopyFromOther)
                throw new InvalidOperationException("The armed roll is not configured to copy another Avatar.");
            if (importer.sourceAvatar == null || AssetDatabase.AssetPathToGUID(AssetDatabase.GetAssetPath(importer.sourceAvatar)) != ExpectedAvatarGuid)
                throw new InvalidOperationException("The armed roll is not using the expected DoubleL T-Pose Avatar.");

            AnimationClip clip = LoadRollClip();
            if (clip.isLooping) throw new InvalidOperationException("The armed roll unexpectedly loops.");
            if (Mathf.Abs(clip.frameRate - 30f) > 0.01f)
                throw new InvalidOperationException($"Unexpected armed-roll frame rate: {clip.frameRate}.");
            if (Mathf.Abs(clip.length - 55f / 30f) > 0.05f)
                throw new InvalidOperationException($"Unexpected armed-roll duration: {clip.length}.");
        }

        private static AnimationClip LoadRollClip()
        {
            AnimationClip clip = AssetDatabase.LoadAllAssetsAtPath(RollPath)
                .OfType<AnimationClip>()
                .FirstOrDefault(item => !item.name.StartsWith("__preview__", StringComparison.Ordinal));
            if (clip == null || clip.name != "1Hand_Base_Roll_F_InPlace")
                throw new InvalidOperationException("The expected 1Hand_Base_Roll_F_InPlace clip was not found.");
            return clip;
        }

        private static AnimatorController CreateIsolatedController()
        {
            AssetDatabase.DeleteAsset(ControllerPath);
            if (!AssetDatabase.CopyAsset(SourceControllerPath, ControllerPath))
                throw new InvalidOperationException("Could not create the isolated SwordIntegration controller.");

            AnimatorController controller = AssetDatabase.LoadAssetAtPath<AnimatorController>(ControllerPath);
            if (controller == null) throw new InvalidOperationException("Could not load the isolated controller.");
            AnimatorStateMachine machine = controller.layers[0].stateMachine;
            AnimatorState roll = machine.states.Select(item => item.state)
                .FirstOrDefault(item => item.name == "RollFront");
            AnimatorState idle = machine.states.Select(item => item.state)
                .FirstOrDefault(item => item.name == "CombatIdle_B1");
            AnimatorState run = machine.states.Select(item => item.state)
                .FirstOrDefault(item => item.name == "RunForward");
            if (roll == null || idle == null || run == null)
                throw new InvalidOperationException("The source locomotion controller is missing required states.");

            roll.motion = LoadRollClip();
            roll.speed = 1.5f;
            foreach (AnimatorStateTransition transition in roll.transitions)
            {
                transition.hasExitTime = true;
                transition.exitTime = 1f;
                transition.hasFixedDuration = true;
                transition.duration = 0.05f;
                transition.interruptionSource = TransitionInterruptionSource.None;
                transition.orderedInterruption = true;
            }
            foreach (AnimatorStateTransition transition in machine.anyStateTransitions)
            {
                if (transition.destinationState != roll) continue;
                transition.hasExitTime = false;
                transition.duration = 0.05f;
                transition.canTransitionToSelf = false;
                transition.interruptionSource = TransitionInterruptionSource.None;
            }

            EditorUtility.SetDirty(controller);
            EditorUtility.SetDirty(roll);
            AssetDatabase.SaveAssets();
            return controller;
        }

        private static void AssignControllerToPrefab(AnimatorController controller)
        {
            GameObject root = PrefabUtility.LoadPrefabContents(PrefabPath);
            try
            {
                Transform alignment = root.GetComponentsInChildren<Transform>(true)
                    .FirstOrDefault(item => item.name == "LargeGripAlignment");
                if (alignment == null) throw new InvalidOperationException("LargeGripAlignment was not found.");
                Vector3 position = alignment.localPosition;
                Quaternion rotation = alignment.localRotation;
                Vector3 scale = alignment.localScale;

                QusapDoubleLAutoLocomotionDriver driver = root.GetComponent<QusapDoubleLAutoLocomotionDriver>();
                if (driver == null || driver.Animator == null)
                    throw new InvalidOperationException("The SwordIntegration driver or Animator was not found.");
                driver.Animator.runtimeAnimatorController = controller;
                driver.Animator.applyRootMotion = false;

                if (Vector3.Distance(position, alignment.localPosition) > 0.000001f
                    || Quaternion.Angle(rotation, alignment.localRotation) > 0.0001f
                    || Vector3.Distance(scale, alignment.localScale) > 0.000001f)
                    throw new InvalidOperationException("LargeGripAlignment changed unexpectedly.");

                PrefabUtility.SaveAsPrefabAsset(root, PrefabPath);
            }
            finally
            {
                PrefabUtility.UnloadPrefabContents(root);
            }
        }

        private static void InstallSceneValidator()
        {
            Scene scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
            foreach (QusapArmedRollValidation old in UnityEngine.Object.FindObjectsByType<QusapArmedRollValidation>(
                         FindObjectsInactive.Include))
                UnityEngine.Object.DestroyImmediate(old.gameObject);
            new GameObject("Qusap75K_Armed_Roll_Validation").AddComponent<QusapArmedRollValidation>();
            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene, ScenePath);
            Selection.activeObject = AssetDatabase.LoadAssetAtPath<SceneAsset>(ScenePath);
        }
    }
}
