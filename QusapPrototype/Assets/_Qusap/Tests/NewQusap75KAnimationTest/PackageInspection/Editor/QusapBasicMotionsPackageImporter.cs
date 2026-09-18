using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using UnityEditor;
using UnityEditor.Animations;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Animations;
using UnityEngine.Playables;
using UnityEngine.SceneManagement;
using Debug = UnityEngine.Debug;
using Object = UnityEngine.Object;

namespace Qusap.NewQusap75KAnimationTest.PackageInspection.Editor
{
    [InitializeOnLoad]
    internal static class QusapBasicMotionsAutoRunner
    {
        private const string TestRoot = "Assets/_Qusap/Tests/NewQusap75KAnimationTest";
        private const string TriggerPath = TestRoot + "/PackageInspection/ImportBasicMotionsRequested.txt";
        private const string ResultPath = TestRoot + "/Playable/Validation/BasicMotionsBuildResult.txt";
        private const string RuntimeLogPath = TestRoot + "/Playable/Validation/BasicMotionsPlayModeConsole.log";
        private const string ScenePath = TestRoot + "/Playable/Scene/CombatPlayground_Qusap75K_PlayableTest.unity";
        private const string PackagePath =
            "C:/Users/victo/Downloads/Qusap_ArtSource/Qusap75K_Prueba/01_Originales/animation_pack_basic_motions.unitypackage";
        private const string ImportedSourceRoot = "Assets/PolyOne";
        private const string ImportedTargetRoot = TestRoot + "/Imported/BasicMotionsFree/PolyOne";
        private const string PendingPlayKey = "Qusap.BasicMotions.PlayModePending";

        private static HashSet<string> pathsBeforeImport;
        private static double playModeStartedAt;

        static QusapBasicMotionsAutoRunner()
        {
            EditorApplication.delayCall += RunWhenReady;
            EditorApplication.playModeStateChanged += OnPlayModeStateChanged;
            AssetDatabase.importPackageCompleted += OnPackageImported;
            AssetDatabase.importPackageCancelled += OnPackageCancelled;
            AssetDatabase.importPackageFailed += OnPackageFailed;
        }

        private static void RunWhenReady()
        {
            if (!File.Exists(ToAbsolutePath(TriggerPath)))
            {
                return;
            }

            if (EditorApplication.isCompiling || EditorApplication.isUpdating)
            {
                EditorApplication.delayCall += RunWhenReady;
                return;
            }

            if (EditorApplication.isPlayingOrWillChangePlaymode)
            {
                WriteResult("BLOCKED: Unity is in or entering Play Mode.");
                return;
            }

            Scene active = SceneManager.GetActiveScene();
            if (active.IsValid() && active.isDirty)
            {
                WriteResult("BLOCKED: The current scene has unsaved changes. Nothing was imported.");
                return;
            }

            if (!File.Exists(PackagePath))
            {
                WriteResult("BLOCKED: The inspected Unity package is missing.");
                return;
            }

            bool sourceExists = AssetDatabase.IsValidFolder(ImportedSourceRoot);
            bool targetExists = AssetDatabase.IsValidFolder(ImportedTargetRoot);
            if (sourceExists && !targetExists)
            {
                AssetDatabase.DeleteAsset(TriggerPath);
                WriteResult("IMPORTED_SOURCE_FOUND; ISOLATION_PENDING");
                try
                {
                    EnsureFolder(TestRoot + "/Imported/BasicMotionsFree");
                    string moveError = AssetDatabase.MoveAsset(ImportedSourceRoot, ImportedTargetRoot);
                    if (!string.IsNullOrEmpty(moveError))
                    {
                        throw new InvalidOperationException(
                            "AssetDatabase could not resume package isolation: " + moveError);
                    }

                    AssetDatabase.SaveAssets();
                    AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
                    if (AssetDatabase.IsValidFolder(ImportedSourceRoot)
                        || !AssetDatabase.IsValidFolder(ImportedTargetRoot))
                    {
                        throw new InvalidOperationException(
                            "The resumed package import was not fully isolated.");
                    }

                    QusapBasicMotionsBuilder.BuildAndValidate(
                        "animation_pack_basic_motions (resumed after verified import)",
                        Array.Empty<string>());
                    WriteResult("BUILD_PASSED; PLAY_MODE_PENDING");
                    SessionState.SetBool(PendingPlayKey, true);
                    Debug.Log("QUSAP75K_BASIC_MOTIONS_PLAYMODE_START");
                    EditorApplication.isPlaying = true;
                }
                catch (Exception exception)
                {
                    WriteResult("FAILED: " + exception);
                    Debug.LogException(exception);
                }
                return;
            }

            if (!sourceExists && targetExists)
            {
                AssetDatabase.DeleteAsset(TriggerPath);
                WriteResult("ISOLATED_PACKAGE_FOUND; BUILD_PENDING");
                try
                {
                    QusapBasicMotionsBuilder.BuildAndValidate(
                        "animation_pack_basic_motions (already isolated)", Array.Empty<string>());
                    WriteResult("BUILD_PASSED; PLAY_MODE_PENDING");
                    SessionState.SetBool(PendingPlayKey, true);
                    Debug.Log("QUSAP75K_BASIC_MOTIONS_PLAYMODE_START");
                    EditorApplication.isPlaying = true;
                }
                catch (Exception exception)
                {
                    WriteResult("FAILED: " + exception);
                    Debug.LogException(exception);
                }
                return;
            }

            if (sourceExists || targetExists)
            {
                WriteResult("BLOCKED: An import source or destination already exists; refusing to overwrite.");
                return;
            }

            WriteResult("PACKAGE_IMPORT_PENDING");
            AssetDatabase.DeleteAsset(TriggerPath);
            AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
            pathsBeforeImport = new HashSet<string>(AssetDatabase.GetAllAssetPaths(), StringComparer.Ordinal);

            try
            {
                AssetDatabase.ImportPackage(PackagePath, false);
            }
            catch (Exception exception)
            {
                WriteResult("FAILED_TO_START_IMPORT: " + exception);
                Debug.LogException(exception);
            }
        }

        private static void OnPackageImported(string packageName)
        {
            if (pathsBeforeImport == null)
            {
                return;
            }

            try
            {
                string[] createdPaths = AssetDatabase.GetAllAssetPaths()
                    .Where(path => !pathsBeforeImport.Contains(path))
                    .OrderBy(path => path, StringComparer.Ordinal)
                    .ToArray();
                pathsBeforeImport = null;

                if (createdPaths.Length == 0 || createdPaths.Any(path =>
                        !path.Equals(ImportedSourceRoot, StringComparison.Ordinal)
                        && !path.StartsWith(ImportedSourceRoot + "/", StringComparison.Ordinal)))
                {
                    throw new InvalidOperationException(
                        "The package created an unexpected path outside Assets/PolyOne; isolation stopped.");
                }

                EnsureFolder(TestRoot + "/Imported/BasicMotionsFree");
                string moveError = AssetDatabase.MoveAsset(ImportedSourceRoot, ImportedTargetRoot);
                if (!string.IsNullOrEmpty(moveError))
                {
                    throw new InvalidOperationException("AssetDatabase could not isolate the package: " + moveError);
                }

                AssetDatabase.SaveAssets();
                AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
                if (AssetDatabase.IsValidFolder(ImportedSourceRoot)
                    || !AssetDatabase.IsValidFolder(ImportedTargetRoot))
                {
                    throw new InvalidOperationException("The imported package was not fully isolated.");
                }

                QusapBasicMotionsBuilder.BuildAndValidate(packageName, createdPaths);
                WriteResult("BUILD_PASSED; PLAY_MODE_PENDING");
                SessionState.SetBool(PendingPlayKey, true);
                Debug.Log("QUSAP75K_BASIC_MOTIONS_PLAYMODE_START");
                EditorApplication.isPlaying = true;
            }
            catch (Exception exception)
            {
                WriteResult("FAILED: " + exception);
                Debug.LogException(exception);
            }
        }

        private static void OnPackageCancelled(string packageName)
        {
            if (pathsBeforeImport == null)
            {
                return;
            }

            pathsBeforeImport = null;
            WriteResult("BLOCKED: Unity package import was cancelled: " + packageName);
        }

        private static void OnPackageFailed(string packageName, string errorMessage)
        {
            if (pathsBeforeImport == null)
            {
                return;
            }

            pathsBeforeImport = null;
            WriteResult("FAILED_PACKAGE_IMPORT: " + packageName + " | " + errorMessage);
        }

        private static void OnPlayModeStateChanged(PlayModeStateChange state)
        {
            if (!SessionState.GetBool(PendingPlayKey, false))
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
                SessionState.SetBool(PendingPlayKey, false);
                EditorApplication.delayCall += FinalizePlayModeValidation;
            }
        }

        private static void StopPlayModeAfterObservation()
        {
            if (!EditorApplication.isPlaying || EditorApplication.timeSinceStartup - playModeStartedAt < 30d)
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
                string absoluteLog = ToAbsolutePath(RuntimeLogPath);
                string[] lines = File.Exists(absoluteLog)
                    ? File.ReadAllLines(absoluteLog).Skip(1).Where(line => !string.IsNullOrWhiteSpace(line)).ToArray()
                    : Array.Empty<string>();
                string[] errors = lines.Where(IsErrorLine).ToArray();
                string[] warnings = lines.Where(line => line.StartsWith("WARNING|", StringComparison.Ordinal)).ToArray();

                bool runtimePassed = QusapBasicMotionsBuilder.CompletePlayModeValidation(
                    File.Exists(absoluteLog), warnings, errors);
                EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
                Selection.activeObject = AssetDatabase.LoadAssetAtPath<SceneAsset>(ScenePath);

                bool passed = File.Exists(absoluteLog) && errors.Length == 0 && runtimePassed;
                WriteResult(passed
                    ? $"PASSED; PLAY_MODE_STARTED; ERRORS=0; WARNINGS={warnings.Length}"
                    : $"FAILED_PLAY_MODE; LOG_PRESENT={File.Exists(absoluteLog)}; ERRORS={errors.Length}; WARNINGS={warnings.Length}");
                Debug.Log(passed
                    ? "QUSAP75K_BASIC_MOTIONS_PLAYMODE_PASSED"
                    : "QUSAP75K_BASIC_MOTIONS_PLAYMODE_FAILED");
            }
            catch (Exception exception)
            {
                WriteResult("FAILED_FINALIZATION: " + exception);
                Debug.LogException(exception);
            }
        }

        private static bool IsErrorLine(string line)
        {
            return line.StartsWith("ERROR|", StringComparison.Ordinal)
                || line.StartsWith("ASSERT|", StringComparison.Ordinal)
                || line.StartsWith("EXCEPTION|", StringComparison.Ordinal);
        }

        private static void WriteResult(string value)
        {
            File.WriteAllText(ToAbsolutePath(ResultPath), value);
            AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
        }

        private static void EnsureFolder(string path)
        {
            string[] parts = path.Split('/');
            string current = parts[0];
            for (int index = 1; index < parts.Length; index++)
            {
                string next = current + "/" + parts[index];
                if (!AssetDatabase.IsValidFolder(next))
                {
                    AssetDatabase.CreateFolder(current, parts[index]);
                }

                current = next;
            }
        }

        private static string ToAbsolutePath(string assetPath)
        {
            string relative = assetPath.Substring("Assets".Length).TrimStart('/', '\\');
            return Path.GetFullPath(Path.Combine(Application.dataPath, relative));
        }
    }

    public static class QusapBasicMotionsBuilder
    {
        private const string TestRoot = "Assets/_Qusap/Tests/NewQusap75KAnimationTest";
        private const string ImportedRoot = TestRoot + "/Imported/BasicMotionsFree/PolyOne";
        private const string AnimationRoot = ImportedRoot + "/Basic Motions/Animation";
        private const string SourcePlayerPrefabPath = "Assets/_Qusap/Prefabs/QusapCombatPlayer.prefab";
        private const string SourceScenePath = "Assets/_Qusap/Scenes/CombatPlayground.unity";
        private const string InputActionsPath = "Assets/_Qusap/Settings/QusapControls.inputactions";
        private const string SourceVisualPrefabPath = TestRoot + "/Qusap75K_AnimationTest.prefab";
        private const string BaseControllerPath = TestRoot + "/Playable/Controller/Qusap75K_PlayableTest.controller";
        private const string ControllerPath = TestRoot + "/Playable/Controller/Qusap75K_BasicMotionsPlayableTest.controller";
        private const string PrefabPath = TestRoot + "/Playable/Prefab/Qusap75K_PlayableTest.prefab";
        private const string ScenePath = TestRoot + "/Playable/Scene/CombatPlayground_Qusap75K_PlayableTest.unity";
        private const string SelectionReportPath = TestRoot + "/Playable/Validation/BasicMotionsClipSelection.json";
        private const string ValidationReportPath = TestRoot + "/Playable/Validation/BasicMotionsValidation.json";
        private const string RuntimeValidationReportPath = TestRoot + "/Playable/Validation/BasicMotionsRuntimeValidation.json";
        private const string ScreenshotRoot = TestRoot + "/Playable/Validation/BasicMotionsScreenshots";
        private const string RuntimeMarkerName = "QusapBasicMotionsRuntimeValidation.request";

        [Serializable]
        private sealed class RootMotionMetrics
        {
            public bool hasRootTranslationCurves;
            public float rawEndDisplacementX;
            public float rawEndDisplacementY;
            public float rawEndDisplacementZ;
            public float rawEndDisplacementXZ;
            public float rawRangeX;
            public float rawRangeY;
            public float rawRangeZ;
        }

        [Serializable]
        private sealed class ClipSelectionRecord
        {
            public string sourceFile;
            public string exactClipName;
            public float durationSeconds;
            public float framesPerSecond;
            public bool loopTime;
            public bool isHumanMotion;
            public bool sourceAvatarValid;
            public bool targetAvatarValid;
            public bool hasRootTranslationCurves;
            public float rawRootEndDisplacementXZ;
            public bool bakeRootRotation;
            public bool bakeRootPositionY;
            public bool bakeRootPositionXZ;
            public bool selected;
            public string selectedRole;
            public string reason;
        }

        [Serializable]
        private sealed class ClipSelectionReport
        {
            public string generatedAtUtc;
            public string importedRoot;
            public bool sourceModelAvatarValid;
            public bool targetQusapAvatarValid;
            public ClipSelectionRecord[] clips;
            public bool passed;
        }

        [Serializable]
        public sealed class ClipValidationRecord
        {
            public string role;
            public string sourceFile;
            public string exactClipName;
            public float durationSeconds;
            public float framesPerSecond;
            public bool loopExpected;
            public bool loopActual;
            public bool isHumanMotion;
            public bool avatarValid;
            public bool applyRootMotion;
            public float rawRootEndDisplacementXZ;
            public float sampledVisualRootDisplacement;
            public float maximumBoundsGrowthRatio;
            public bool finitePose;
            public bool grossStretchDetected;
            public string screenshot;
            public bool passed;
            public string notes;
        }

        [Serializable]
        public sealed class BasicMotionsValidationReport
        {
            public string generatedAtUtc;
            public string packageName;
            public string importedRoot;
            public string controllerAsset;
            public string prefabAsset;
            public string sceneAsset;
            public string[] controls;
            public bool originalsUnchanged;
            public bool inputActionsUnchanged;
            public bool gameplayComponentsPreserved;
            public bool rigidbodyPreserved;
            public bool collidersPreserved;
            public bool playerTwoUntouched;
            public bool rollPreservedAndReturnsToIdle;
            public bool jumpReturnsToIdle;
            public bool dyingHoldsFinalPose;
            public bool animatorApplyRootMotion;
            public bool onScreenInstructionsPresent;
            public bool runtimeAnimationSequencePassed;
            public string runtimeValidationReport;
            public ClipValidationRecord[] clips;
            public bool sceneStartedInPlayMode;
            public int consoleWarningCount;
            public int consoleErrorCount;
            public string[] consoleWarnings;
            public string[] consoleErrors;
            public string[] importedFiles;
            public string[] createdOrModifiedAssets;
            public string[] gitStatusShort;
            public bool passed;
        }

        private sealed class SelectedClips
        {
            public AnimationClip idle;
            public AnimationClip walk;
            public AnimationClip run;
            public AnimationClip jumping;
            public AnimationClip dying;

            public IEnumerable<(string role, AnimationClip clip, bool loop)> All()
            {
                yield return ("Idle", idle, true);
                yield return ("Walk", walk, true);
                yield return ("Run", run, true);
                yield return ("Jumping", jumping, false);
                yield return ("Dying", dying, false);
            }
        }

        private sealed class VisualSampleResult
        {
            public float rootDisplacement;
            public float boundsGrowthRatio;
            public bool finitePose;
            public bool grossStretch;
            public string screenshot;
        }

        public static void BuildAndValidate(string packageName, string[] originalCreatedPaths)
        {
            string sourcePrefabHash = ComputeHash(SourcePlayerPrefabPath);
            string sourceSceneHash = ComputeHash(SourceScenePath);
            string inputHash = ComputeHash(InputActionsPath);

            bool sourceAvatarValid = InspectSourceModelAvatar();
            bool targetAvatarValid = InspectTargetAvatar();
            SelectedClips selected = SelectAndConfigureClips(sourceAvatarValid, targetAvatarValid);
            AnimatorController controller = CreateController(selected);
            UpdatePlayablePrefab(controller);
            AddInstructionsToScene();
            ClipValidationRecord[] clipResults = ValidateVisuals(selected, targetAvatarValid);

            BasicMotionsValidationReport report = ValidateIntegration(
                packageName,
                selected,
                controller,
                clipResults,
                sourcePrefabHash,
                sourceSceneHash,
                inputHash);
            WriteJson(ValidationReportPath, report);
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);

            if (!report.passed)
            {
                throw new InvalidOperationException("Basic Motions validation failed. See " + ValidationReportPath);
            }

            string marker = Path.Combine(
                Directory.GetParent(Application.dataPath)?.FullName ?? Application.dataPath,
                "Library", RuntimeMarkerName);
            File.WriteAllText(marker, "Run the isolated Basic Motions Play Mode validation once.");
        }

        public static bool CompletePlayModeValidation(bool logPresent, string[] warnings, string[] errors)
        {
            string absolute = ToAbsolutePath(ValidationReportPath);
            BasicMotionsValidationReport report = File.Exists(absolute)
                ? JsonUtility.FromJson<BasicMotionsValidationReport>(File.ReadAllText(absolute))
                : null;
            if (report == null)
            {
                throw new InvalidOperationException("Basic Motions validation report is missing.");
            }

            report.sceneStartedInPlayMode = logPresent;
            report.consoleWarnings = warnings ?? Array.Empty<string>();
            report.consoleErrors = errors ?? Array.Empty<string>();
            report.consoleWarningCount = report.consoleWarnings.Length;
            report.consoleErrorCount = report.consoleErrors.Length;
            string runtimeAbsolute = ToAbsolutePath(RuntimeValidationReportPath);
            Qusap.NewQusap75KAnimationTest.Playable.Validation.BasicMotionsRuntimeReport runtimeReport =
                File.Exists(runtimeAbsolute)
                    ? JsonUtility.FromJson<Qusap.NewQusap75KAnimationTest.Playable.Validation.BasicMotionsRuntimeReport>(
                        File.ReadAllText(runtimeAbsolute))
                    : null;
            report.runtimeAnimationSequencePassed = runtimeReport != null && runtimeReport.passed;
            report.runtimeValidationReport = RuntimeValidationReportPath;
            report.gitStatusShort = GetGitStatus();
            report.passed = report.passed && logPresent && report.consoleErrorCount == 0
                && report.runtimeAnimationSequencePassed;
            WriteJson(ValidationReportPath, report);
            AssetDatabase.SaveAssets();
            return report.passed;
        }

        private static SelectedClips SelectAndConfigureClips(bool sourceAvatarValid, bool targetAvatarValid)
        {
            AnimationClip[] clips = AssetDatabase.FindAssets("t:AnimationClip", new[] { AnimationRoot })
                .Select(AssetDatabase.GUIDToAssetPath)
                .Distinct(StringComparer.Ordinal)
                .Select(path => AssetDatabase.LoadAssetAtPath<AnimationClip>(path))
                .Where(clip => clip != null)
                .OrderBy(clip => clip.name, StringComparer.Ordinal)
                .ToArray();

            var selected = new SelectedClips
            {
                idle = clips.SingleOrDefault(clip => clip.name == "Idle"),
                walk = clips.SingleOrDefault(clip => clip.name == "Walk"),
                run = clips.SingleOrDefault(clip => clip.name == "Run"),
                jumping = clips.SingleOrDefault(clip => clip.name == "Jumping Up"),
                dying = clips.SingleOrDefault(clip => clip.name == "Dying")
            };

            if (selected.All().Any(item => item.clip == null))
            {
                throw new InvalidOperationException("One or more required neutral In-Place clips are missing.");
            }

            foreach ((string role, AnimationClip clip, bool loop) in selected.All())
            {
                ConfigureClip(clip, loop);
                if (!clip.isHumanMotion)
                {
                    throw new InvalidOperationException(role + " is not a Humanoid animation clip.");
                }
            }
            AssetDatabase.SaveAssets();

            var records = new List<ClipSelectionRecord>();
            foreach (AnimationClip clip in clips)
            {
                string role = selected.All().Where(item => item.clip == clip).Select(item => item.role).FirstOrDefault();
                bool isSelected = !string.IsNullOrEmpty(role);
                RootMotionMetrics metrics = MeasureRootCurves(clip);
                var settings = AnimationUtility.GetAnimationClipSettings(clip);
                records.Add(new ClipSelectionRecord
                {
                    sourceFile = AssetDatabase.GetAssetPath(clip),
                    exactClipName = clip.name,
                    durationSeconds = clip.length,
                    framesPerSecond = clip.frameRate,
                    loopTime = settings.loopTime,
                    isHumanMotion = clip.isHumanMotion,
                    sourceAvatarValid = sourceAvatarValid,
                    targetAvatarValid = targetAvatarValid,
                    hasRootTranslationCurves = metrics.hasRootTranslationCurves,
                    rawRootEndDisplacementXZ = metrics.rawEndDisplacementXZ,
                    bakeRootRotation = settings.loopBlendOrientation,
                    bakeRootPositionY = settings.loopBlendPositionY,
                    bakeRootPositionXZ = settings.loopBlendPositionXZ,
                    selected = isSelected,
                    selectedRole = role ?? string.Empty,
                    reason = GetSelectionReason(clip.name, role)
                });
            }

            var report = new ClipSelectionReport
            {
                generatedAtUtc = DateTime.UtcNow.ToString("O"),
                importedRoot = ImportedRoot,
                sourceModelAvatarValid = sourceAvatarValid,
                targetQusapAvatarValid = targetAvatarValid,
                clips = records.ToArray(),
                passed = sourceAvatarValid && targetAvatarValid
                    && selected.All().All(item => item.clip.isHumanMotion)
            };
            WriteJson(SelectionReportPath, report);

            if (!report.passed)
            {
                throw new InvalidOperationException("Humanoid Avatar compatibility validation failed.");
            }

            return selected;
        }

        private static string GetSelectionReason(string clipName, string selectedRole)
        {
            if (!string.IsNullOrEmpty(selectedRole))
            {
                return selectedRole == "Jumping"
                    ? "Selected as the neutral one-shot jumping-up motion; Jumping Down is a longer descent motion."
                    : "Selected as the exact neutral base motion for " + selectedRole + ".";
            }

            if (clipName.IndexOf("Run", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                return "Discarded directional, fast, stair, backward, or turning Run variant in favor of neutral Run.";
            }

            if (clipName.IndexOf("Walk", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                return "Discarded emotional or turning Walk variant in favor of neutral Walk.";
            }

            if (clipName == "Jumping Down")
            {
                return "Discarded because it is a 2.4-second descent motion rather than the neutral jump-up test.";
            }

            return "Discarded because it does not correspond to Idle, Walk, Run, Jumping, or Dying.";
        }

        private static void ConfigureClip(AnimationClip clip, bool loop)
        {
            var settings = AnimationUtility.GetAnimationClipSettings(clip);
            settings.loopTime = loop;
            settings.loopBlend = loop;
            settings.loopBlendOrientation = true;
            settings.loopBlendPositionY = true;
            settings.loopBlendPositionXZ = true;
            settings.keepOriginalOrientation = true;
            settings.keepOriginalPositionY = true;
            settings.keepOriginalPositionXZ = true;
            AnimationUtility.SetAnimationClipSettings(clip, settings);
            clip.wrapMode = loop ? WrapMode.Loop : WrapMode.ClampForever;
            EditorUtility.SetDirty(clip);
        }

        private static AnimatorController CreateController(SelectedClips selected)
        {
            AssetDatabase.DeleteAsset(ControllerPath);
            if (!AssetDatabase.CopyAsset(BaseControllerPath, ControllerPath))
            {
                throw new InvalidOperationException("Could not copy the existing playable Animator Controller.");
            }

            AnimatorController controller = AssetDatabase.LoadAssetAtPath<AnimatorController>(ControllerPath);
            AnimatorStateMachine machine = controller.layers[0].stateMachine;
            AnimatorState idle = machine.states.Select(child => child.state)
                .FirstOrDefault(state => state.name == "Rest");
            AnimatorState roll = machine.states.Select(child => child.state)
                .FirstOrDefault(state => state.name == "RollFront");
            if (idle == null || roll == null)
            {
                throw new InvalidOperationException("The existing playable controller lacks Rest or RollFront.");
            }

            idle.name = "Idle";
            idle.motion = selected.idle;
            machine.defaultState = idle;
            AnimatorState walk = machine.AddState("Walk", new Vector3(480f, 20f));
            AnimatorState run = machine.AddState("Run", new Vector3(480f, 90f));
            AnimatorState jumping = machine.AddState("Jumping", new Vector3(480f, 160f));
            AnimatorState dying = machine.AddState("Dying", new Vector3(480f, 230f));
            walk.motion = selected.walk;
            run.motion = selected.run;
            jumping.motion = selected.jumping;
            dying.motion = selected.dying;

            AddTrigger(controller, "PlayIdle");
            AddTrigger(controller, "PlayWalk");
            AddTrigger(controller, "PlayRun");
            AddTrigger(controller, "PlayJumping");
            AddTrigger(controller, "PlayDying");
            AddTriggerTransition(machine, idle, "PlayIdle");
            AddTriggerTransition(machine, walk, "PlayWalk");
            AddTriggerTransition(machine, run, "PlayRun");
            AddTriggerTransition(machine, jumping, "PlayJumping");
            AddTriggerTransition(machine, dying, "PlayDying");

            AnimatorStateTransition jumpExit = jumping.AddTransition(idle);
            ConfigureExit(jumpExit);
            AnimatorStateTransition rollExit = roll.transitions
                .FirstOrDefault(transition => transition.destinationState == idle);
            if (rollExit == null)
            {
                rollExit = roll.AddTransition(idle);
            }
            ConfigureExit(rollExit);

            foreach (Object item in new Object[] { controller, machine, idle, walk, run, jumping, dying, roll })
            {
                EditorUtility.SetDirty(item);
            }
            AssetDatabase.SaveAssets();
            return controller;
        }

        private static void AddTrigger(AnimatorController controller, string name)
        {
            if (controller.parameters.All(parameter => parameter.name != name))
            {
                controller.AddParameter(name, AnimatorControllerParameterType.Trigger);
            }
        }

        private static void AddTriggerTransition(AnimatorStateMachine machine, AnimatorState state, string trigger)
        {
            AnimatorStateTransition transition = machine.AddAnyStateTransition(state);
            transition.hasExitTime = false;
            transition.hasFixedDuration = true;
            transition.duration = 0.03f;
            transition.canTransitionToSelf = true;
            transition.AddCondition(AnimatorConditionMode.If, 0f, trigger);
        }

        private static void ConfigureExit(AnimatorStateTransition transition)
        {
            transition.hasExitTime = true;
            transition.exitTime = 1f;
            transition.hasFixedDuration = true;
            transition.duration = 0.03f;
            foreach (AnimatorCondition condition in transition.conditions)
            {
                transition.RemoveCondition(condition);
            }
        }

        private static void UpdatePlayablePrefab(AnimatorController controller)
        {
            GameObject root = PrefabUtility.LoadPrefabContents(PrefabPath);
            try
            {
                Transform visual = FindDeepChild(root.transform, "Qusap75K_Visual");
                Animator animator = visual != null ? visual.GetComponent<Animator>() : null;
                if (animator == null)
                {
                    throw new InvalidOperationException("The playable Qusap75K visual Animator is missing.");
                }

                animator.runtimeAnimatorController = controller;
                animator.applyRootMotion = false;
                animator.cullingMode = AnimatorCullingMode.AlwaysAnimate;
                PrefabUtility.SaveAsPrefabAsset(root, PrefabPath);
            }
            finally
            {
                PrefabUtility.UnloadPrefabContents(root);
            }
        }

        private static void AddInstructionsToScene()
        {
            Scene scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
            GameObject instructions = scene.GetRootGameObjects()
                .FirstOrDefault(root => root.name == "Qusap75K_BasicMotions_Instructions");
            if (instructions == null)
            {
                instructions = new GameObject("Qusap75K_BasicMotions_Instructions");
                SceneManager.MoveGameObjectToScene(instructions, scene);
            }

            if (instructions.GetComponent<Qusap.NewQusap75KAnimationTest.Playable.QusapBasicMotionsTestInstructions>() == null)
            {
                instructions.AddComponent<Qusap.NewQusap75KAnimationTest.Playable.QusapBasicMotionsTestInstructions>();
            }

            if (instructions.GetComponent<Qusap.NewQusap75KAnimationTest.Playable.Validation.QusapBasicMotionsPlayModeProbe>() == null)
            {
                instructions.AddComponent<Qusap.NewQusap75KAnimationTest.Playable.Validation.QusapBasicMotionsPlayModeProbe>();
            }

            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene, ScenePath);
        }

        private static ClipValidationRecord[] ValidateVisuals(SelectedClips selected, bool avatarValid)
        {
            EnsureFolder(ScreenshotRoot);
            var results = new List<ClipValidationRecord>();
            foreach ((string role, AnimationClip clip, bool loop) in selected.All())
            {
                VisualSampleResult sample = SampleVisual(role, clip);
                RootMotionMetrics root = MeasureRootCurves(clip);
                bool actualLoop = AnimationUtility.GetAnimationClipSettings(clip).loopTime;
                bool passed = clip.isHumanMotion && avatarValid && actualLoop == loop
                    && sample.rootDisplacement <= 0.001f && sample.finitePose && !sample.grossStretch;
                results.Add(new ClipValidationRecord
                {
                    role = role,
                    sourceFile = AssetDatabase.GetAssetPath(clip),
                    exactClipName = clip.name,
                    durationSeconds = clip.length,
                    framesPerSecond = clip.frameRate,
                    loopExpected = loop,
                    loopActual = actualLoop,
                    isHumanMotion = clip.isHumanMotion,
                    avatarValid = avatarValid,
                    applyRootMotion = false,
                    rawRootEndDisplacementXZ = root.rawEndDisplacementXZ,
                    sampledVisualRootDisplacement = sample.rootDisplacement,
                    maximumBoundsGrowthRatio = sample.boundsGrowthRatio,
                    finitePose = sample.finitePose,
                    grossStretchDetected = sample.grossStretch,
                    screenshot = sample.screenshot,
                    passed = passed,
                    notes = passed
                        ? "Deterministic Humanoid sampling remained finite with no gross bounds growth; subtle surface quality remains a visual-review item."
                        : "Automatic Humanoid sampling detected an invalid pose, root displacement, or gross stretch."
                });
            }

            AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
            return results.ToArray();
        }

        private static VisualSampleResult SampleVisual(string role, AnimationClip clip)
        {
            Scene preview = EditorSceneManager.NewPreviewScene();
            GameObject visual = null;
            Color previousAmbient = RenderSettings.ambientLight;
            try
            {
                GameObject visualAsset = AssetDatabase.LoadAssetAtPath<GameObject>(SourceVisualPrefabPath);
                visual = PrefabUtility.InstantiatePrefab(visualAsset, preview) as GameObject;
                if (visual == null)
                {
                    throw new InvalidOperationException("Could not instantiate the validated Qusap75K visual.");
                }

                visual.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
                visual.transform.localScale = Vector3.one;
                Animator animator = visual.GetComponent<Animator>();
                if (animator == null || animator.avatar == null || !animator.avatar.isValid
                    || !animator.avatar.isHuman)
                {
                    throw new InvalidOperationException(
                        "The Qusap75K validation visual has no valid Humanoid Animator avatar.");
                }

                animator.applyRootMotion = false;
                animator.cullingMode = AnimatorCullingMode.AlwaysAnimate;
                animator.runtimeAnimatorController = null;
                animator.Rebind();
                Vector3 initialRoot = visual.transform.position;
                float maximumRootDisplacement = 0f;
                float maximumGrowth = 1f;
                bool finite = true;
                bool grossStretch = false;
                Bounds baseline = default;

                PlayableGraph graph = PlayableGraph.Create("Qusap75KBasicMotionsValidation");
                try
                {
                    graph.SetTimeUpdateMode(DirectorUpdateMode.Manual);
                    AnimationPlayableOutput output = AnimationPlayableOutput.Create(
                        graph, "HumanoidOutput", animator);
                    AnimationClipPlayable playable = AnimationClipPlayable.Create(graph, clip);
                    playable.SetApplyFootIK(false);
                    playable.SetSpeed(0d);
                    output.SetSourcePlayable(playable);
                    graph.Play();

                    float[] fractions = { 0f, 0.25f, 0.5f, 0.75f, 0.98f };
                    for (int index = 0; index < fractions.Length; index++)
                    {
                        playable.SetTime(clip.length * fractions[index]);
                        graph.Evaluate(0f);
                        Bounds bounds = CalculateRendererBounds(visual);
                        if (index == 0)
                        {
                            baseline = bounds;
                        }

                        float growth = MaxComponent(bounds.size) /
                            Mathf.Max(0.0001f, MaxComponent(baseline.size));
                        maximumGrowth = Mathf.Max(maximumGrowth, growth);
                        maximumRootDisplacement = Mathf.Max(maximumRootDisplacement,
                            Vector3.Distance(initialRoot, visual.transform.position));
                        finite &= IsFinite(bounds.center) && IsFinite(bounds.size)
                            && visual.GetComponentsInChildren<Transform>(true).All(transform =>
                                IsFinite(transform.position) && IsFinite(transform.localScale));
                        grossStretch |= growth > 3f;
                    }

                    float captureFraction = role == "Dying" ? 0.98f : 0.5f;
                    playable.SetTime(clip.length * captureFraction);
                    graph.Evaluate(0f);
                    string screenshot = ScreenshotRoot + "/" + role + ".png";
                    CaptureVisual(preview, visual, screenshot);

                    return new VisualSampleResult
                    {
                        rootDisplacement = maximumRootDisplacement,
                        boundsGrowthRatio = maximumGrowth,
                        finitePose = finite,
                        grossStretch = grossStretch,
                        screenshot = screenshot
                    };
                }
                finally
                {
                    if (graph.IsValid())
                    {
                        graph.Destroy();
                    }
                }
            }
            finally
            {
                RenderSettings.ambientLight = previousAmbient;
                EditorSceneManager.ClosePreviewScene(preview);
            }
        }

        private static void CaptureVisual(Scene scene, GameObject visual, string assetPath)
        {
            Bounds bounds = CalculateRendererBounds(visual);
            var lightObject = new GameObject("ValidationLight");
            SceneManager.MoveGameObjectToScene(lightObject, scene);
            Light light = lightObject.AddComponent<Light>();
            light.type = LightType.Directional;
            light.intensity = 2.4f;
            lightObject.transform.rotation = Quaternion.Euler(35f, -45f, 0f);

            var cameraObject = new GameObject("ValidationCamera");
            SceneManager.MoveGameObjectToScene(cameraObject, scene);
            Camera camera = cameraObject.AddComponent<Camera>();
            camera.clearFlags = CameraClearFlags.SolidColor;
            camera.backgroundColor = new Color(0.06f, 0.075f, 0.1f, 1f);
            camera.orthographic = true;
            camera.aspect = 1f;
            camera.orthographicSize = Mathf.Max(bounds.extents.y, bounds.extents.z) * 1.35f;
            camera.transform.position = bounds.center + Vector3.right * Mathf.Max(3f, bounds.extents.magnitude * 4f);
            camera.transform.rotation = Quaternion.LookRotation(bounds.center - camera.transform.position, Vector3.up);
            RenderSettings.ambientLight = new Color(0.7f, 0.7f, 0.7f, 1f);

            const int size = 720;
            var renderTexture = new RenderTexture(size, size, 24, RenderTextureFormat.ARGB32);
            var texture = new Texture2D(size, size, TextureFormat.RGB24, false);
            RenderTexture previousActive = RenderTexture.active;
            try
            {
                camera.targetTexture = renderTexture;
                RenderTexture.active = renderTexture;
                camera.Render();
                texture.ReadPixels(new Rect(0, 0, size, size), 0, 0);
                texture.Apply();
                File.WriteAllBytes(ToAbsolutePath(assetPath), texture.EncodeToPNG());
            }
            finally
            {
                camera.targetTexture = null;
                RenderTexture.active = previousActive;
                Object.DestroyImmediate(texture);
                renderTexture.Release();
                Object.DestroyImmediate(renderTexture);
            }
        }

        private static BasicMotionsValidationReport ValidateIntegration(
            string packageName,
            SelectedClips selected,
            AnimatorController controller,
            ClipValidationRecord[] clips,
            string sourcePrefabHash,
            string sourceSceneHash,
            string inputHash)
        {
            bool rigidbodyPreserved;
            bool collidersPreserved;
            bool gameplayPreserved;
            bool applyRootMotion;
            ValidatePrefabPreservation(out rigidbodyPreserved, out collidersPreserved,
                out gameplayPreserved, out applyRootMotion);

            AnimatorStateMachine machine = controller.layers[0].stateMachine;
            Dictionary<string, AnimatorState> states = machine.states
                .Select(child => child.state).ToDictionary(state => state.name, StringComparer.Ordinal);
            bool rollValid = states.TryGetValue("RollFront", out AnimatorState roll)
                && roll.transitions.Any(transition => transition.destinationState == states["Idle"]
                    && transition.hasExitTime && Mathf.Approximately(transition.exitTime, 1f));
            bool jumpValid = states.TryGetValue("Jumping", out AnimatorState jump)
                && jump.transitions.Any(transition => transition.destinationState == states["Idle"]
                    && transition.hasExitTime && Mathf.Approximately(transition.exitTime, 1f));
            bool dyingValid = states.TryGetValue("Dying", out AnimatorState dying)
                && dying.transitions.Length == 0;

            Scene scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
            GameObject playerTwo = scene.GetRootGameObjects().FirstOrDefault(root => root.name == "Player2_Qusap");
            bool playerTwoUntouched = GetPrefabAssetPath(playerTwo) == SourcePlayerPrefabPath;
            bool instructions = scene.GetRootGameObjects().Any(root =>
                root.name == "Qusap75K_BasicMotions_Instructions"
                && root.GetComponent<Qusap.NewQusap75KAnimationTest.Playable.QusapBasicMotionsTestInstructions>() != null);

            string[] importedFiles = AssetDatabase.GetAllAssetPaths()
                .Where(path => path.StartsWith(ImportedRoot + "/", StringComparison.Ordinal)
                    && File.Exists(ToAbsolutePath(path)))
                .OrderBy(path => path, StringComparer.Ordinal)
                .ToArray();
            string[] modified = importedFiles.Concat(new[]
                {
                    ControllerPath,
                    PrefabPath,
                    ScenePath,
                    SelectionReportPath,
                    ValidationReportPath,
                    TestRoot + "/Playable/Scripts/QusapPlayableAnimationTestInput.cs",
                    TestRoot + "/Playable/Scripts/QusapBasicMotionsTestInstructions.cs",
                    TestRoot + "/Playable/Validation/QusapBasicMotionsPlayModeLogCapture.cs",
                    TestRoot + "/Playable/Validation/QusapBasicMotionsPlayModeProbe.cs",
                    RuntimeValidationReportPath
                })
                .Concat(clips.Select(clip => clip.screenshot))
                .Distinct(StringComparer.Ordinal)
                .OrderBy(path => path, StringComparer.Ordinal)
                .ToArray();

            var report = new BasicMotionsValidationReport
            {
                generatedAtUtc = DateTime.UtcNow.ToString("O"),
                packageName = packageName,
                importedRoot = ImportedRoot,
                controllerAsset = ControllerPath,
                prefabAsset = PrefabPath,
                sceneAsset = ScenePath,
                controls = new[] { "1 Idle", "2 Walk", "3 Run", "4 Jump", "5 Dying", "R Roll" },
                originalsUnchanged = ComputeHash(SourcePlayerPrefabPath) == sourcePrefabHash
                    && ComputeHash(SourceScenePath) == sourceSceneHash,
                inputActionsUnchanged = ComputeHash(InputActionsPath) == inputHash,
                gameplayComponentsPreserved = gameplayPreserved,
                rigidbodyPreserved = rigidbodyPreserved,
                collidersPreserved = collidersPreserved,
                playerTwoUntouched = playerTwoUntouched,
                rollPreservedAndReturnsToIdle = rollValid,
                jumpReturnsToIdle = jumpValid,
                dyingHoldsFinalPose = dyingValid,
                animatorApplyRootMotion = applyRootMotion,
                onScreenInstructionsPresent = instructions,
                runtimeAnimationSequencePassed = false,
                runtimeValidationReport = RuntimeValidationReportPath,
                clips = clips,
                sceneStartedInPlayMode = false,
                consoleWarnings = Array.Empty<string>(),
                consoleErrors = Array.Empty<string>(),
                importedFiles = importedFiles,
                createdOrModifiedAssets = modified,
                gitStatusShort = GetGitStatus()
            };
            report.passed = report.originalsUnchanged && report.inputActionsUnchanged
                && report.gameplayComponentsPreserved && report.rigidbodyPreserved
                && report.collidersPreserved && report.playerTwoUntouched
                && report.rollPreservedAndReturnsToIdle && report.jumpReturnsToIdle
                && report.dyingHoldsFinalPose && !report.animatorApplyRootMotion
                && report.onScreenInstructionsPresent && report.clips.All(clip => clip.passed);
            return report;
        }

        private static void ValidatePrefabPreservation(
            out bool rigidbodyPreserved,
            out bool collidersPreserved,
            out bool gameplayPreserved,
            out bool applyRootMotion)
        {
            GameObject source = PrefabUtility.LoadPrefabContents(SourcePlayerPrefabPath);
            GameObject target = PrefabUtility.LoadPrefabContents(PrefabPath);
            try
            {
                Rigidbody a = source.GetComponent<Rigidbody>();
                Rigidbody b = target.GetComponent<Rigidbody>();
                rigidbodyPreserved = a != null && b != null
                    && Mathf.Approximately(a.mass, b.mass)
                    && Mathf.Approximately(a.linearDamping, b.linearDamping)
                    && Mathf.Approximately(a.angularDamping, b.angularDamping)
                    && a.useGravity == b.useGravity && a.isKinematic == b.isKinematic
                    && a.interpolation == b.interpolation && a.constraints == b.constraints
                    && a.collisionDetectionMode == b.collisionDetectionMode;

                CapsuleCollider ca = source.GetComponent<CapsuleCollider>();
                CapsuleCollider cb = target.GetComponent<CapsuleCollider>();
                collidersPreserved = ca != null && cb != null
                    && ca.center == cb.center && Mathf.Approximately(ca.radius, cb.radius)
                    && Mathf.Approximately(ca.height, cb.height) && ca.direction == cb.direction
                    && ca.isTrigger == cb.isTrigger && ca.sharedMaterial == cb.sharedMaterial;

                Type inputType = typeof(Qusap.NewQusap75KAnimationTest.Playable.QusapPlayableAnimationTestInput);
                Type[] sourceTypes = source.GetComponents<Component>().Select(component => component.GetType()).ToArray();
                Type[] targetTypes = target.GetComponents<Component>()
                    .Where(component => component.GetType() != inputType)
                    .Select(component => component.GetType()).ToArray();
                gameplayPreserved = sourceTypes.SequenceEqual(targetTypes);

                Transform visual = FindDeepChild(target.transform, "Qusap75K_Visual");
                Animator animator = visual != null ? visual.GetComponent<Animator>() : null;
                applyRootMotion = animator == null || animator.applyRootMotion;
            }
            finally
            {
                PrefabUtility.UnloadPrefabContents(source);
                PrefabUtility.UnloadPrefabContents(target);
            }
        }

        private static bool InspectSourceModelAvatar()
        {
            string path = ImportedRoot + "/Basic Motions/Model/SM_DemoCharacter.fbx";
            GameObject model = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            Animator animator = model != null ? model.GetComponentInChildren<Animator>(true) : null;
            return animator != null && animator.avatar != null && animator.avatar.isValid && animator.avatar.isHuman;
        }

        private static bool InspectTargetAvatar()
        {
            GameObject visual = AssetDatabase.LoadAssetAtPath<GameObject>(SourceVisualPrefabPath);
            Animator animator = visual != null ? visual.GetComponent<Animator>() : null;
            return animator != null && animator.avatar != null && animator.avatar.isValid && animator.avatar.isHuman;
        }

        private static RootMotionMetrics MeasureRootCurves(AnimationClip clip)
        {
            var metrics = new RootMotionMetrics();
            float[] first = new float[3];
            float[] last = new float[3];
            float[] min = { float.PositiveInfinity, float.PositiveInfinity, float.PositiveInfinity };
            float[] max = { float.NegativeInfinity, float.NegativeInfinity, float.NegativeInfinity };
            bool[] found = new bool[3];

            foreach (EditorCurveBinding binding in AnimationUtility.GetCurveBindings(clip))
            {
                int axis = binding.propertyName == "RootT.x" ? 0
                    : binding.propertyName == "RootT.y" ? 1
                    : binding.propertyName == "RootT.z" ? 2 : -1;
                if (axis < 0)
                {
                    continue;
                }

                AnimationCurve curve = AnimationUtility.GetEditorCurve(clip, binding);
                if (curve == null || curve.length == 0)
                {
                    continue;
                }

                found[axis] = true;
                first[axis] = curve.Evaluate(0f);
                last[axis] = curve.Evaluate(clip.length);
                foreach (Keyframe key in curve.keys)
                {
                    min[axis] = Mathf.Min(min[axis], key.value);
                    max[axis] = Mathf.Max(max[axis], key.value);
                }
            }

            metrics.hasRootTranslationCurves = found.Any(value => value);
            metrics.rawEndDisplacementX = found[0] ? last[0] - first[0] : 0f;
            metrics.rawEndDisplacementY = found[1] ? last[1] - first[1] : 0f;
            metrics.rawEndDisplacementZ = found[2] ? last[2] - first[2] : 0f;
            metrics.rawEndDisplacementXZ = new Vector2(
                metrics.rawEndDisplacementX, metrics.rawEndDisplacementZ).magnitude;
            metrics.rawRangeX = found[0] ? max[0] - min[0] : 0f;
            metrics.rawRangeY = found[1] ? max[1] - min[1] : 0f;
            metrics.rawRangeZ = found[2] ? max[2] - min[2] : 0f;
            return metrics;
        }

        private static Bounds CalculateRendererBounds(GameObject root)
        {
            Renderer[] renderers = root.GetComponentsInChildren<Renderer>(true);
            if (renderers.Length == 0)
            {
                return new Bounds(root.transform.position, Vector3.one);
            }

            Bounds bounds = renderers[0].bounds;
            foreach (Renderer renderer in renderers.Skip(1))
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

            for (int index = 0; index < root.childCount; index++)
            {
                Transform result = FindDeepChild(root.GetChild(index), name);
                if (result != null)
                {
                    return result;
                }
            }
            return null;
        }

        private static string GetPrefabAssetPath(GameObject instance)
        {
            Object source = instance != null ? PrefabUtility.GetCorrespondingObjectFromSource(instance) : null;
            return source != null ? AssetDatabase.GetAssetPath(source) : string.Empty;
        }

        private static float MaxComponent(Vector3 value)
        {
            return Mathf.Max(value.x, Mathf.Max(value.y, value.z));
        }

        private static bool IsFinite(Vector3 value)
        {
            return IsFinite(value.x) && IsFinite(value.y) && IsFinite(value.z);
        }

        private static bool IsFinite(float value)
        {
            return !float.IsNaN(value) && !float.IsInfinity(value);
        }

        private static void EnsureFolder(string path)
        {
            string[] parts = path.Split('/');
            string current = parts[0];
            for (int index = 1; index < parts.Length; index++)
            {
                string next = current + "/" + parts[index];
                if (!AssetDatabase.IsValidFolder(next))
                {
                    AssetDatabase.CreateFolder(current, parts[index]);
                }
                current = next;
            }
        }

        private static void WriteJson(string path, object value)
        {
            File.WriteAllText(ToAbsolutePath(path), JsonUtility.ToJson(value, true));
            AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceSynchronousImport);
        }

        private static string ComputeHash(string path)
        {
            using (SHA256 sha = SHA256.Create())
            using (FileStream stream = File.OpenRead(ToAbsolutePath(path)))
            {
                return string.Concat(sha.ComputeHash(stream)
                    .Select(value => value.ToString("x2", CultureInfo.InvariantCulture)));
            }
        }

        private static string[] GetGitStatus()
        {
            try
            {
                var startInfo = new ProcessStartInfo("git", "status --short")
                {
                    WorkingDirectory = Directory.GetParent(Application.dataPath)?.FullName ?? Application.dataPath,
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    CreateNoWindow = true
                };
                using (Process process = Process.Start(startInfo))
                {
                    string output = process.StandardOutput.ReadToEnd();
                    process.WaitForExit(5000);
                    return output.Split(new[] { "\r\n", "\n" }, StringSplitOptions.RemoveEmptyEntries);
                }
            }
            catch (Exception exception)
            {
                return new[] { "git status unavailable: " + exception.Message };
            }
        }

        private static string ToAbsolutePath(string assetPath)
        {
            string relative = assetPath.Substring("Assets".Length).TrimStart('/', '\\');
            return Path.GetFullPath(Path.Combine(Application.dataPath, relative));
        }
    }
}
