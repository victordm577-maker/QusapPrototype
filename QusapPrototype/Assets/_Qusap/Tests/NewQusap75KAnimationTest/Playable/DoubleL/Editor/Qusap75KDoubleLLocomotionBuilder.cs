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
using Qusap.NewQusap75KAnimationTest.Playable.DoubleL.Validation;

namespace Qusap.NewQusap75KAnimationTest.Playable.DoubleL.Editor
{
    // Isolated builder: never invokes AssetDatabase.ImportPackage.
    [InitializeOnLoad]
    internal static class Qusap75KDoubleLLocomotionAutoRunner
    {
        private const string Root = "Assets/_Qusap/Tests/NewQusap75KAnimationTest/Playable/DoubleL";
        private const string TriggerPath = Root + "/Validation/BuildRequested.txt";
        private const string ResultPath = Root + "/Validation/BuildResult.txt";
        private const string ReportPath = Root + "/Validation/DoubleLLocomotionValidation.json";
        private const string RuntimeLogPath = Root + "/Validation/DoubleLLocomotionPlayModeConsole.log";
        private const string ScenePath = Root + "/Scene/CombatPlayground_Qusap75K_DoubleL_LocomotionTest.unity";
        private const string PendingKey = "Qusap.DoubleL.Locomotion.PlayModePending";
        private static double playStartedAt;

        static Qusap75KDoubleLLocomotionAutoRunner()
        {
            EditorApplication.delayCall += RunWhenReady;
            EditorApplication.playModeStateChanged += OnPlayModeStateChanged;
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
                Qusap75KDoubleLLocomotionBuilder.BuildAndValidate();
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
            if (!SessionState.GetBool(PendingKey, false))
            {
                return;
            }
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
            if (EditorApplication.isPlaying && EditorApplication.timeSinceStartup - playStartedAt > 45d)
            {
                Debug.LogError("DoubleL locomotion Play Mode validation exceeded 45 seconds.");
                EditorApplication.ExitPlaymode();
            }
        }

        private static void FinalizeValidation()
        {
            try
            {
                AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
                string logAbsolute = ToAbsolutePath(RuntimeLogPath);
                string[] lines = File.Exists(logAbsolute)
                    ? File.ReadAllLines(logAbsolute).Skip(1).Where(line => !string.IsNullOrWhiteSpace(line)).ToArray()
                    : Array.Empty<string>();
                string[] errors = lines.Where(line => line.StartsWith("ERROR|", StringComparison.Ordinal)
                    || line.StartsWith("ASSERT|", StringComparison.Ordinal)
                    || line.StartsWith("EXCEPTION|", StringComparison.Ordinal)).ToArray();
                string[] warnings = lines.Where(line => line.StartsWith("WARNING|", StringComparison.Ordinal)).ToArray();

                string reportAbsolute = ToAbsolutePath(ReportPath);
                DoubleLLocomotionValidationReport report = File.Exists(reportAbsolute)
                    ? JsonUtility.FromJson<DoubleLLocomotionValidationReport>(File.ReadAllText(reportAbsolute))
                    : null;
                if (report == null)
                {
                    throw new InvalidOperationException("DoubleL validation report is missing after Play Mode.");
                }
                report.sceneStartedInPlayMode = File.Exists(logAbsolute);
                report.consoleWarnings = warnings;
                report.consoleErrors = errors;
                report.consoleWarningCount = warnings.Length;
                report.consoleErrorCount = errors.Length;
                report.passed = report.passed && report.sceneStartedInPlayMode
                    && report.runtimeSequenceExecuted && report.runtimeSequencePassed && errors.Length == 0;
                report.generatedAtUtc = DateTime.UtcNow.ToString("O");
                File.WriteAllText(reportAbsolute, JsonUtility.ToJson(report, true));
                AssetDatabase.ImportAsset(ReportPath, ImportAssetOptions.ForceSynchronousImport);

                EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
                Selection.activeObject = AssetDatabase.LoadAssetAtPath<SceneAsset>(ScenePath);
                WriteResult(report.passed
                    ? $"PASSED; CLIPS={report.clips.Length}; ERRORS=0; WARNINGS={warnings.Length}"
                    : $"FAILED_PLAY_MODE; EXECUTED={report.runtimeSequenceExecuted}; ERRORS={errors.Length}; WARNINGS={warnings.Length}");
                Debug.Log(report.passed
                    ? "QUSAP75K_DOUBLEL_LOCOMOTION_VALIDATION_PASSED"
                    : "QUSAP75K_DOUBLEL_LOCOMOTION_VALIDATION_FAILED");
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
            return Path.GetFullPath(Path.Combine(Application.dataPath,
                assetPath.Substring("Assets".Length).TrimStart('/', '\\')));
        }
    }

    public static class Qusap75KDoubleLLocomotionBuilder
    {
        private const string ExpectedPackageSha = "B0FFC0887F86F93B1C0F53102C8145B2AC44EE0012A0E7F6102991D5B0B1C681";
        private const string PackagePath = "C:/Users/victo/AppData/Roaming/Unity/Asset Store-5.x/DoubleL/Animation/RPGAnimations - One Hand Base.unitypackage";
        private const string TestRoot = "Assets/_Qusap/Tests/NewQusap75KAnimationTest";
        private const string ImportedRoot = TestRoot + "/Imported/DoubleL/OneHandBase";
        private const string OutputRoot = TestRoot + "/Playable/DoubleL";
        private const string SourcePrefabPath = TestRoot + "/Playable/Prefab/Qusap75K_PlayableTest.prefab";
        private const string SourceScenePath = TestRoot + "/Playable/Scene/CombatPlayground_Qusap75K_PlayableTest.unity";
        private const string TargetCharacterPath = TestRoot + "/Character/Qusap75K_Rigged_Test_v2.fbx";
        private const string InputActionsPath = "Assets/_Qusap/Settings/QusapControls.inputactions";
        private const string RollPath = TestRoot + "/Animation/RM_Roll_front.fbx";
        private const string AvatarPath = ImportedRoot + "/Model/T-Pose.fbx";
        private const string SwordPath = ImportedRoot + "/Model/SM_Wep_Sword_03.fbx";
        private const string ControllerPath = OutputRoot + "/Controller/Qusap75K_DoubleL_LocomotionTest.controller";
        private const string PrefabPath = OutputRoot + "/Prefab/Qusap75K_DoubleL_LocomotionTest.prefab";
        private const string ScenePath = OutputRoot + "/Scene/CombatPlayground_Qusap75K_DoubleL_LocomotionTest.unity";
        private const string ReportPath = OutputRoot + "/Validation/DoubleLLocomotionValidation.json";
        private const string RuntimeMarkerName = "QusapDoubleLLocomotionValidation.request";

        private sealed class ClipSpec
        {
            public string role;
            public string path;
            public string clipName;
            public string stateName;
            public string trigger;
            public bool loop;
            public bool oneShot;
            public AnimationClip clip;
        }

        private sealed class PrefabBuildResult
        {
            public string socketPath;
            public Vector3 swordPosition;
            public Vector3 swordEuler;
            public Vector3 swordScale;
        }

        private static readonly ClipSpec[] Specs =
        {
            Spec("Combat Idle B1", "Movement/Idle/Idle/1Hand_Base_Stand_Idle_B_1.fbx", "1Hand_Base_Stand_Idle_B_1", "CombatIdle_B1", "PlayIdleB1", true, false),
            Spec("Combat Idle B2", "Movement/Idle/Idle/1Hand_Base_Stand_Idle_B_2.fbx", "1Hand_Base_Stand_Idle_B_2", "CombatIdle_B2", "PlayIdleB2", true, false),
            Spec("Combat Idle B3", "Movement/Idle/Idle/1Hand_Base_Stand_Idle_B_3.fbx", "1Hand_Base_Stand_Idle_B_3", "CombatIdle_B3", "PlayIdleB3", true, false),
            Spec("Walk Forward", "Movement/Walk/Type B/Base/InPlace/1Hand_Base_Walk_B_F_InPlace.fbx", "1Hand_Base_Walk_B_F_InPlace", "WalkForward", "PlayWalkForward", true, false),
            Spec("Walk Backward", "Movement/Walk/Type B/Base/InPlace/1Hand_Base_Walk_B_B_InPlace.fbx", "1Hand_Base_Walk_B_B_InPlace", "WalkBackward", "PlayWalkBackward", true, false),
            Spec("Run Forward", "Movement/Run/Type B/Base/InPlace/1Hand_Base_Run_B_F_InPlace.fbx", "1Hand_Base_Run_B_F_InPlace", "RunForward", "PlayRunForward", true, false),
            Spec("Run Backward", "Movement/Run/Type B/Base/InPlace/1Hand_Base_Run_B_B_InPlace.fbx", "1Hand_Base_Run_B_B_InPlace", "RunBackward", "PlayRunBackward", true, false),
            Spec("Turn Left 90", "Movement/Idle/Turn_B/InPlace/1Hand_Base_Stand_Idle_Turn_B_L90_InPlace.fbx", "1Hand_Base_Stand_Idle_Turn_B_L90_InPlace", "TurnLeft90", "PlayTurnLeft90", false, true),
            Spec("Turn Right 90", "Movement/Idle/Turn_B/InPlace/1Hand_Base_Stand_Idle_Turn_B_R90_InPlace.fbx", "1Hand_Base_Stand_Idle_Turn_B_R90_InPlace", "TurnRight90", "PlayTurnRight90", false, true),
            new ClipSpec { role = "Roll comparison", path = RollPath, clipName = "RM_Roll_front", stateName = "RollFront", trigger = "PlayRoll", loop = false, oneShot = true }
        };

        [MenuItem("Tools/Qusap/Tests/Build DoubleL Armed Locomotion Test")]
        public static void BuildAndValidate()
        {
            string packageHash = ComputeFileHash(PackagePath);
            if (!string.Equals(packageHash, ExpectedPackageSha, StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException("The cached One Hand Base package SHA-256 does not match the approved package.");
            }

            string[] protectedPaths = { SourcePrefabPath, SourceScenePath, TargetCharacterPath, InputActionsPath, RollPath };
            Dictionary<string, string> originalHashes = protectedPaths.ToDictionary(path => path, ComputeAssetAndMetaHash);

            EnsureFolder(OutputRoot + "/Controller");
            EnsureFolder(OutputRoot + "/Prefab");
            EnsureFolder(OutputRoot + "/Scene");
            EnsureFolder(OutputRoot + "/Validation");

            Avatar sourceAvatar = ConfigureSourceAvatar();
            LoadAndConfigureClips(sourceAvatar);
            AnimatorController controller = CreateController();
            PrefabBuildResult prefabResult = CreatePrefab(controller);
            CreateScene();

            DoubleLLocomotionValidationReport report = Validate(packageHash, sourceAvatar,
                controller, prefabResult, originalHashes);
            WriteReport(report);
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
            if (!report.passed)
            {
                throw new InvalidOperationException("DoubleL structural validation failed. See " + ReportPath);
            }

            string marker = Path.Combine(Directory.GetParent(Application.dataPath)?.FullName
                ?? Application.dataPath, "Library", RuntimeMarkerName);
            File.WriteAllText(marker, "Run the isolated DoubleL armed locomotion validation once.");
        }

        private static ClipSpec Spec(string role, string suffix, string clipName,
            string stateName, string trigger, bool loop, bool oneShot)
        {
            return new ClipSpec
            {
                role = role,
                path = ImportedRoot + "/FBX_Animations/One Hand Base/" + suffix,
                clipName = clipName,
                stateName = stateName,
                trigger = trigger,
                loop = loop,
                oneShot = oneShot
            };
        }

        private static Avatar ConfigureSourceAvatar()
        {
            AssetDatabase.ImportAsset(AvatarPath, ImportAssetOptions.ForceSynchronousImport | ImportAssetOptions.ForceUpdate);
            ModelImporter importer = AssetImporter.GetAtPath(AvatarPath) as ModelImporter;
            if (importer == null)
            {
                throw new InvalidOperationException("T-Pose.fbx has no ModelImporter.");
            }
            importer.animationType = ModelImporterAnimationType.Human;
            importer.avatarSetup = ModelImporterAvatarSetup.CreateFromThisModel;
            importer.importAnimation = false;
            importer.importCameras = false;
            importer.importLights = false;
            importer.SaveAndReimport();

            Avatar avatar = AssetDatabase.LoadAllAssetsAtPath(AvatarPath)
                .OfType<Avatar>().FirstOrDefault(candidate => candidate.isValid && candidate.isHuman);
            if (avatar == null)
            {
                throw new InvalidOperationException("DoubleL T-Pose did not produce a valid Humanoid Avatar.");
            }
            return avatar;
        }

        private static void LoadAndConfigureClips(Avatar sourceAvatar)
        {
            foreach (ClipSpec spec in Specs)
            {
                if (spec.path == RollPath)
                {
                    spec.clip = LoadClip(spec.path, spec.clipName);
                    continue;
                }

                AssetDatabase.ImportAsset(spec.path,
                    ImportAssetOptions.ForceSynchronousImport | ImportAssetOptions.ForceUpdate);
                ModelImporter importer = AssetImporter.GetAtPath(spec.path) as ModelImporter;
                if (importer == null)
                {
                    throw new InvalidOperationException("Expected ModelImporter at " + spec.path);
                }
                importer.animationType = ModelImporterAnimationType.Human;
                importer.avatarSetup = ModelImporterAvatarSetup.CopyFromOther;
                importer.sourceAvatar = sourceAvatar;
                importer.importAnimation = true;
                importer.importCameras = false;
                importer.importLights = false;

                ModelImporterClipAnimation[] settings = importer.clipAnimations;
                if (settings == null || settings.Length == 0)
                {
                    settings = importer.defaultClipAnimations;
                }
                if (settings == null || settings.Length == 0)
                {
                    throw new InvalidOperationException("No animation clip settings found in " + spec.path);
                }
                foreach (ModelImporterClipAnimation setting in settings)
                {
                    setting.loopTime = spec.loop;
                    setting.loopPose = spec.loop;
                    setting.lockRootRotation = true;
                    setting.lockRootHeightY = true;
                    setting.lockRootPositionXZ = true;
                    setting.keepOriginalOrientation = true;
                    setting.keepOriginalPositionY = true;
                    setting.keepOriginalPositionXZ = true;
                }
                importer.clipAnimations = settings;
                importer.SaveAndReimport();
                spec.clip = LoadClip(spec.path, spec.clipName);
                if (!spec.clip.isHumanMotion)
                {
                    throw new InvalidOperationException(spec.clipName + " did not import as Humanoid motion.");
                }
            }
        }

        private static AnimationClip LoadClip(string path, string exactName)
        {
            AnimationClip clip = AssetDatabase.LoadAllAssetsAtPath(path).OfType<AnimationClip>()
                .FirstOrDefault(candidate => candidate.name == exactName);
            if (clip == null)
            {
                throw new InvalidOperationException("Clip not found: " + exactName + " in " + path);
            }
            return clip;
        }

        private static AnimatorController CreateController()
        {
            AssetDatabase.DeleteAsset(ControllerPath);
            AnimatorController controller = AnimatorController.CreateAnimatorControllerAtPath(ControllerPath);
            controller.AddParameter(new AnimatorControllerParameter
            {
                name = "SelectedIdle",
                type = AnimatorControllerParameterType.Int,
                defaultInt = 1
            });
            foreach (ClipSpec spec in Specs)
            {
                controller.AddParameter(spec.trigger, AnimatorControllerParameterType.Trigger);
            }

            AnimatorStateMachine machine = controller.layers[0].stateMachine;
            var states = new Dictionary<string, AnimatorState>(StringComparer.Ordinal);
            for (int index = 0; index < Specs.Length; index++)
            {
                ClipSpec spec = Specs[index];
                AnimatorState state = machine.AddState(spec.stateName,
                    new Vector3(280f + (index % 3) * 230f, 60f + (index / 3) * 90f));
                state.motion = spec.clip;
                states.Add(spec.stateName, state);
                AddTriggerTransition(machine, state, spec.trigger);
            }
            machine.defaultState = states["CombatIdle_B1"];

            foreach (ClipSpec oneShot in Specs.Where(spec => spec.oneShot))
            {
                AddIdleExit(states[oneShot.stateName], states["CombatIdle_B1"], 1);
                AddIdleExit(states[oneShot.stateName], states["CombatIdle_B2"], 2);
                AddIdleExit(states[oneShot.stateName], states["CombatIdle_B3"], 3);
            }

            EditorUtility.SetDirty(controller);
            EditorUtility.SetDirty(machine);
            foreach (AnimatorState state in states.Values)
            {
                EditorUtility.SetDirty(state);
            }
            AssetDatabase.SaveAssets();
            return controller;
        }

        private static void AddTriggerTransition(AnimatorStateMachine machine, AnimatorState target, string trigger)
        {
            AnimatorStateTransition transition = machine.AddAnyStateTransition(target);
            transition.hasExitTime = false;
            transition.hasFixedDuration = true;
            transition.duration = 0.05f;
            transition.canTransitionToSelf = false;
            transition.AddCondition(AnimatorConditionMode.If, 0f, trigger);
        }

        private static void AddIdleExit(AnimatorState source, AnimatorState target, int selectedIdle)
        {
            AnimatorStateTransition transition = source.AddTransition(target);
            transition.hasExitTime = true;
            transition.exitTime = 1f;
            transition.hasFixedDuration = true;
            transition.duration = 0.05f;
            transition.AddCondition(AnimatorConditionMode.Equals, selectedIdle, "SelectedIdle");
        }

        private static PrefabBuildResult CreatePrefab(AnimatorController controller)
        {
            AssetDatabase.DeleteAsset(PrefabPath);
            if (!AssetDatabase.CopyAsset(SourcePrefabPath, PrefabPath))
            {
                throw new InvalidOperationException("Could not copy the latest playable Qusap75K prefab.");
            }

            GameObject root = PrefabUtility.LoadPrefabContents(PrefabPath);
            try
            {
                root.name = "Qusap75K_DoubleL_LocomotionTest";
                Transform visual = FindDeepChild(root.transform, "Qusap75K_Visual");
                Animator animator = visual != null ? visual.GetComponent<Animator>() : null;
                if (animator == null || animator.avatar == null || !animator.avatar.isHuman || !animator.avatar.isValid)
                {
                    throw new InvalidOperationException("The copied Qusap75K visual lacks its valid Humanoid Avatar.");
                }
                animator.runtimeAnimatorController = controller;
                animator.applyRootMotion = false;
                animator.cullingMode = AnimatorCullingMode.AlwaysAnimate;

                foreach (MonoBehaviour behaviour in root.GetComponents<MonoBehaviour>())
                {
                    if (behaviour != null && behaviour.GetType().FullName ==
                        "Qusap.NewQusap75KAnimationTest.Playable.QusapPlayableAnimationTestInput")
                    {
                        Object.DestroyImmediate(behaviour);
                    }
                }
                var input = root.GetComponent<QusapDoubleLLocomotionInput>()
                    ?? root.AddComponent<QusapDoubleLLocomotionInput>();
                input.Configure(animator);

                Transform rightHand = animator.GetBoneTransform(HumanBodyBones.RightHand);
                if (rightHand == null)
                {
                    throw new InvalidOperationException("Qusap75K Humanoid Avatar does not resolve RightHand.");
                }
                Transform previousSocket = rightHand.Find("DoubleL_WeaponSocket");
                if (previousSocket != null)
                {
                    Object.DestroyImmediate(previousSocket.gameObject);
                }
                var socketObject = new GameObject("DoubleL_WeaponSocket");
                socketObject.transform.SetParent(rightHand, false);

                GameObject swordAsset = AssetDatabase.LoadAssetAtPath<GameObject>(SwordPath);
                GameObject sword = swordAsset != null
                    ? PrefabUtility.InstantiatePrefab(swordAsset, socketObject.transform) as GameObject
                    : null;
                if (sword == null)
                {
                    throw new InvalidOperationException("Could not instantiate SM_Wep_Sword_03.");
                }
                sword.name = "SM_Wep_Sword_03_Placeholder";
                sword.transform.localPosition = new Vector3(0.0369f, -0.0906f, 0.0401f);
                sword.transform.localRotation = Quaternion.Euler(-90f, 0f, 0f);
                sword.transform.localScale = Vector3.one;

                Bounds characterBounds = CalculateBounds(visual.gameObject,
                    renderer => !renderer.transform.IsChildOf(sword.transform));
                Bounds swordBounds = CalculateBounds(sword, renderer => true);
                float swordLongestDimension = Mathf.Max(swordBounds.size.x,
                    Mathf.Max(swordBounds.size.y, swordBounds.size.z));
                float desiredSwordLength = characterBounds.size.y * 0.58f;
                float swordScale = desiredSwordLength / Mathf.Max(0.0001f, swordLongestDimension);
                sword.transform.localScale = Vector3.one * Mathf.Clamp(swordScale, 0.001f, 10f);

                swordBounds = CalculateBounds(sword, renderer => true);
                Vector3 endpointA = swordBounds.center;
                Vector3 endpointB = swordBounds.center;
                if (swordBounds.size.x >= swordBounds.size.y && swordBounds.size.x >= swordBounds.size.z)
                {
                    endpointA.x = swordBounds.min.x;
                    endpointB.x = swordBounds.max.x;
                }
                else if (swordBounds.size.y >= swordBounds.size.z)
                {
                    endpointA.y = swordBounds.min.y;
                    endpointB.y = swordBounds.max.y;
                }
                else
                {
                    endpointA.z = swordBounds.min.z;
                    endpointB.z = swordBounds.max.z;
                }
                Vector3 gripPoint = Vector3.SqrMagnitude(endpointA - rightHand.position)
                    <= Vector3.SqrMagnitude(endpointB - rightHand.position) ? endpointA : endpointB;
                sword.transform.position += rightHand.position - gripPoint;

                var result = new PrefabBuildResult
                {
                    socketPath = GetHierarchyPath(socketObject.transform, root.transform),
                    swordPosition = sword.transform.localPosition,
                    swordEuler = sword.transform.localEulerAngles,
                    swordScale = sword.transform.localScale
                };
                PrefabUtility.SaveAsPrefabAsset(root, PrefabPath);
                return result;
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
            {
                throw new InvalidOperationException("Could not copy the latest playable test scene.");
            }
            Scene scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
            GameObject oldPlayer = scene.GetRootGameObjects().FirstOrDefault(root =>
                GetPrefabAssetPath(root) == SourcePrefabPath || root.name.StartsWith("Player1_Qusap75K", StringComparison.Ordinal));
            if (oldPlayer == null)
            {
                throw new InvalidOperationException("Player 1 was not found in the copied playable scene.");
            }

            GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath);
            GameObject newPlayer = PrefabUtility.InstantiatePrefab(prefab, scene) as GameObject;
            if (newPlayer == null)
            {
                throw new InvalidOperationException("Could not instantiate the DoubleL player prefab.");
            }
            newPlayer.name = "Player1_Qusap75K_DoubleL_LocomotionTest";
            newPlayer.transform.SetPositionAndRotation(oldPlayer.transform.position, oldPlayer.transform.rotation);
            newPlayer.transform.localScale = oldPlayer.transform.localScale;
            newPlayer.transform.SetSiblingIndex(oldPlayer.transform.GetSiblingIndex());
            ReplaceExternalSceneReferences(scene, oldPlayer, newPlayer);
            Object.DestroyImmediate(oldPlayer);

            foreach (GameObject root in scene.GetRootGameObjects().Where(item =>
                item.name.StartsWith("Qusap75K_BasicMotions_Instructions", StringComparison.Ordinal)
                || item.name.StartsWith("Qusap75K_DoubleL_Locomotion_Instructions", StringComparison.Ordinal)).ToArray())
            {
                Object.DestroyImmediate(root);
            }
            var validationObject = new GameObject("Qusap75K_DoubleL_Locomotion_Instructions");
            validationObject.AddComponent<QusapDoubleLLocomotionValidation>();

            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene, ScenePath);
            AssetDatabase.SaveAssets();
        }

        private static DoubleLLocomotionValidationReport Validate(string packageHash, Avatar sourceAvatar,
            AnimatorController controller, PrefabBuildResult prefabResult,
            Dictionary<string, string> originalHashes)
        {
            GameObject targetModel = AssetDatabase.LoadAssetAtPath<GameObject>(TargetCharacterPath);
            Animator targetAnimator = targetModel != null ? targetModel.GetComponentInChildren<Animator>(true) : null;
            bool targetAvatarValid = targetAnimator != null && targetAnimator.avatar != null
                && targetAnimator.avatar.isHuman && targetAnimator.avatar.isValid;

            string[] importedPackageAssets = AssetDatabase.GetAllAssetPaths()
                .Where(path => path.StartsWith(ImportedRoot + "/", StringComparison.Ordinal)
                    && path.EndsWith(".fbx", StringComparison.OrdinalIgnoreCase))
                .OrderBy(path => path, StringComparer.Ordinal).ToArray();
            bool exactAssets = importedPackageAssets.Length == 11
                && importedPackageAssets.Count(path => path.Contains("/FBX_Animations/")) == 9
                && importedPackageAssets.Contains(AvatarPath)
                && importedPackageAssets.Contains(SwordPath);

            DoubleLClipValidationRecord[] clipRecords = Specs.Select(spec => CreateClipRecord(spec, sourceAvatar)).ToArray();
            bool originalFilesUnchanged = originalHashes.All(pair => ComputeAssetAndMetaHash(pair.Key) == pair.Value);
            ValidatePrefabPreservation(out bool gameplay, out bool rigidbody, out bool colliders,
                out bool applyRootMotion, out bool swordAttached);

            Scene scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
            GameObject playerTwo = scene.GetRootGameObjects().FirstOrDefault(root => root.name == "Player2_Qusap");
            bool playerTwoUntouched = GetPrefabAssetPath(playerTwo) == "Assets/_Qusap/Prefabs/QusapCombatPlayer.prefab";

            AnimatorStateMachine machine = controller.layers[0].stateMachine;
            Dictionary<string, AnimatorState> states = machine.states.ToDictionary(child => child.state.name,
                child => child.state, StringComparer.Ordinal);
            bool allStates = Specs.All(spec => states.ContainsKey(spec.stateName));
            bool turnsReturn = Specs.Where(spec => spec.role.StartsWith("Turn", StringComparison.Ordinal))
                .All(spec => HasThreeIdleExits(states[spec.stateName]));
            bool rollReturns = states.ContainsKey("RollFront") && HasThreeIdleExits(states["RollFront"]);

            string[] created = AssetDatabase.GetAllAssetPaths()
                .Where(path => path.StartsWith(OutputRoot + "/", StringComparison.Ordinal)
                    || path.StartsWith(ImportedRoot + "/", StringComparison.Ordinal))
                .OrderBy(path => path, StringComparer.Ordinal).ToArray();

            var report = new DoubleLLocomotionValidationReport
            {
                generatedAtUtc = DateTime.UtcNow.ToString("O"),
                packagePath = PackagePath,
                packageSha256 = packageHash,
                importedRoot = ImportedRoot,
                targetCharacter = TargetCharacterPath,
                sourceAvatar = AvatarPath,
                swordModel = SwordPath,
                controllerAsset = ControllerPath,
                prefabAsset = PrefabPath,
                sceneAsset = ScenePath,
                sourcePlayablePrefab = SourcePrefabPath,
                sourcePlayableScene = SourceScenePath,
                importedPackageAssets = importedPackageAssets,
                controls = new[]
                {
                    "1 Combat Idle B1", "2 Combat Idle B2", "3 Combat Idle B3",
                    "4 Walk Forward", "5 Walk Backward", "6 Run Forward", "7 Run Backward",
                    "8 Turn Left 90", "9 Turn Right 90", "R previous Roll"
                },
                packageHashMatches = string.Equals(packageHash, ExpectedPackageSha, StringComparison.OrdinalIgnoreCase),
                exactlySelectedAssetsImported = exactAssets,
                sourceAvatarValid = sourceAvatar != null && sourceAvatar.isHuman && sourceAvatar.isValid,
                targetAvatarValid = targetAvatarValid,
                allClipsHumanoid = Specs.All(spec => spec.clip != null && spec.clip.isHumanMotion),
                animatorApplyRootMotion = applyRootMotion,
                originalFilesUnchanged = originalFilesUnchanged,
                gameplayComponentsPreserved = gameplay,
                rigidbodyPreserved = rigidbody,
                collidersPreserved = colliders,
                inputActionsUnchanged = originalHashes[InputActionsPath] == ComputeAssetAndMetaHash(InputActionsPath),
                playerTwoUntouched = playerTwoUntouched,
                movementAndJumpSystemsPreserved = gameplay && rigidbody && colliders,
                swordAttachedToRightHand = swordAttached,
                weaponSocketPath = prefabResult.socketPath,
                swordLocalPosition = Format(prefabResult.swordPosition),
                swordLocalEulerAngles = Format(prefabResult.swordEuler),
                swordLocalScale = Format(prefabResult.swordScale),
                controllerHasAllStates = allStates,
                turnsReturnToSelectedIdle = turnsReturn,
                rollReturnsToSelectedIdle = rollReturns,
                runtimeSequenceExecuted = false,
                runtimeSequencePassed = false,
                sceneStartedInPlayMode = false,
                consoleWarnings = Array.Empty<string>(),
                consoleErrors = Array.Empty<string>(),
                clips = clipRecords,
                createdAssets = created
            };
            report.passed = report.packageHashMatches && report.exactlySelectedAssetsImported
                && report.sourceAvatarValid && report.targetAvatarValid && report.allClipsHumanoid
                && !report.animatorApplyRootMotion && report.originalFilesUnchanged
                && report.gameplayComponentsPreserved && report.rigidbodyPreserved
                && report.collidersPreserved && report.inputActionsUnchanged
                && report.playerTwoUntouched && report.movementAndJumpSystemsPreserved
                && report.swordAttachedToRightHand && report.controllerHasAllStates
                && report.turnsReturnToSelectedIdle && report.rollReturnsToSelectedIdle
                && report.clips.All(record => record.passed);
            return report;
        }

        private static DoubleLClipValidationRecord CreateClipRecord(ClipSpec spec, Avatar sourceAvatar)
        {
            var settings = AnimationUtility.GetAnimationClipSettings(spec.clip);
            bool isRoll = spec.path == RollPath;
            bool baked = settings.loopBlendOrientation && settings.loopBlendPositionY && settings.loopBlendPositionXZ;
            return new DoubleLClipValidationRecord
            {
                role = spec.role,
                sourceAsset = spec.path,
                exactClipName = spec.clip.name,
                durationSeconds = spec.clip.length,
                framesPerSecond = spec.clip.frameRate,
                loopExpected = spec.loop,
                loopActual = settings.loopTime,
                isHumanMotion = spec.clip.isHumanMotion,
                sourceAvatarValid = isRoll || (sourceAvatar != null && sourceAvatar.isValid && sourceAvatar.isHuman),
                bakeRootRotation = settings.loopBlendOrientation,
                bakeRootPositionY = settings.loopBlendPositionY,
                bakeRootPositionXZ = settings.loopBlendPositionXZ,
                passed = settings.loopTime == spec.loop && spec.clip.isHumanMotion && baked,
                notes = isRoll ? "Previously validated roll retained for comparison."
                    : "DoubleL Unity Humanoid clip using the package T-Pose Avatar; all root channels baked."
            };
        }

        private static void ValidatePrefabPreservation(out bool gameplay, out bool rigidbody,
            out bool colliders, out bool applyRootMotion, out bool swordAttached)
        {
            GameObject source = PrefabUtility.LoadPrefabContents(SourcePrefabPath);
            GameObject target = PrefabUtility.LoadPrefabContents(PrefabPath);
            try
            {
                Rigidbody a = source.GetComponent<Rigidbody>();
                Rigidbody b = target.GetComponent<Rigidbody>();
                rigidbody = a != null && b != null && Mathf.Approximately(a.mass, b.mass)
                    && Mathf.Approximately(a.linearDamping, b.linearDamping)
                    && Mathf.Approximately(a.angularDamping, b.angularDamping)
                    && a.useGravity == b.useGravity && a.isKinematic == b.isKinematic
                    && a.interpolation == b.interpolation && a.constraints == b.constraints
                    && a.collisionDetectionMode == b.collisionDetectionMode;

                CapsuleCollider ca = source.GetComponent<CapsuleCollider>();
                CapsuleCollider cb = target.GetComponent<CapsuleCollider>();
                colliders = ca != null && cb != null && ca.center == cb.center
                    && Mathf.Approximately(ca.radius, cb.radius) && Mathf.Approximately(ca.height, cb.height)
                    && ca.direction == cb.direction && ca.isTrigger == cb.isTrigger
                    && ca.sharedMaterial == cb.sharedMaterial;

                string oldInput = "Qusap.NewQusap75KAnimationTest.Playable.QusapPlayableAnimationTestInput";
                string newInput = typeof(QusapDoubleLLocomotionInput).FullName;
                string[] sourceTypes = source.GetComponents<Component>().Where(component => component != null)
                    .Select(component => component.GetType().FullName)
                    .Where(name => name != oldInput).ToArray();
                string[] targetTypes = target.GetComponents<Component>().Where(component => component != null)
                    .Select(component => component.GetType().FullName)
                    .Where(name => name != newInput).ToArray();
                gameplay = sourceTypes.SequenceEqual(targetTypes);

                Transform visual = FindDeepChild(target.transform, "Qusap75K_Visual");
                Animator animator = visual != null ? visual.GetComponent<Animator>() : null;
                applyRootMotion = animator == null || animator.applyRootMotion;
                Transform hand = animator != null ? animator.GetBoneTransform(HumanBodyBones.RightHand) : null;
                Transform socket = hand != null ? hand.Find("DoubleL_WeaponSocket") : null;
                Transform sword = socket != null ? socket.Find("SM_Wep_Sword_03_Placeholder") : null;
                swordAttached = hand != null && socket != null && sword != null && sword.IsChildOf(hand);
            }
            finally
            {
                PrefabUtility.UnloadPrefabContents(source);
                PrefabUtility.UnloadPrefabContents(target);
            }
        }

        private static bool HasThreeIdleExits(AnimatorState state)
        {
            return state.transitions.Count(transition => transition.hasExitTime
                && Mathf.Approximately(transition.exitTime, 1f)
                && transition.destinationState != null
                && transition.destinationState.name.StartsWith("CombatIdle_B", StringComparison.Ordinal)) == 3;
        }

        private static void ReplaceExternalSceneReferences(Scene scene, GameObject oldPlayer, GameObject newPlayer)
        {
            foreach (GameObject root in scene.GetRootGameObjects())
            {
                foreach (Component component in root.GetComponentsInChildren<Component>(true))
                {
                    if (component == null || component.gameObject == oldPlayer
                        || component.transform.IsChildOf(oldPlayer.transform))
                    {
                        continue;
                    }
                    var serialized = new SerializedObject(component);
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
                    if (changed)
                    {
                        serialized.ApplyModifiedPropertiesWithoutUndo();
                    }
                }
            }
        }

        private static string GetPrefabAssetPath(GameObject instance)
        {
            if (instance == null)
            {
                return string.Empty;
            }
            Object source = PrefabUtility.GetCorrespondingObjectFromSource(instance);
            return source != null ? AssetDatabase.GetAssetPath(source) : string.Empty;
        }

        private static Transform FindDeepChild(Transform root, string name)
        {
            if (root.name == name)
            {
                return root;
            }
            foreach (Transform child in root)
            {
                Transform found = FindDeepChild(child, name);
                if (found != null)
                {
                    return found;
                }
            }
            return null;
        }

        private static string GetHierarchyPath(Transform value, Transform root)
        {
            var names = new List<string>();
            Transform current = value;
            while (current != null)
            {
                names.Add(current.name);
                if (current == root)
                {
                    break;
                }
                current = current.parent;
            }
            names.Reverse();
            return string.Join("/", names);
        }

        private static Bounds CalculateBounds(GameObject root, Func<Renderer, bool> predicate)
        {
            Renderer[] renderers = root.GetComponentsInChildren<Renderer>(true)
                .Where(renderer => renderer.enabled && predicate(renderer)).ToArray();
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

        private static string ComputeAssetAndMetaHash(string assetPath)
        {
            string absolute = ToAbsolutePath(assetPath);
            string first = File.Exists(absolute) ? ComputeFileHash(absolute) : "MISSING";
            string meta = absolute + ".meta";
            string second = File.Exists(meta) ? ComputeFileHash(meta) : "MISSING_META";
            return first + ":" + second;
        }

        private static string ComputeFileHash(string path)
        {
            using (SHA256 sha = SHA256.Create())
            using (FileStream stream = File.OpenRead(path))
            {
                return string.Concat(sha.ComputeHash(stream).Select(value => value.ToString("X2")));
            }
        }

        private static string Format(Vector3 value)
        {
            return $"({value.x:F6}, {value.y:F6}, {value.z:F6})";
        }

        private static void WriteReport(DoubleLLocomotionValidationReport report)
        {
            File.WriteAllText(ToAbsolutePath(ReportPath), JsonUtility.ToJson(report, true));
            AssetDatabase.ImportAsset(ReportPath, ImportAssetOptions.ForceSynchronousImport);
        }

        private static string ToAbsolutePath(string assetPath)
        {
            return Path.GetFullPath(Path.Combine(Application.dataPath,
                assetPath.Substring("Assets".Length).TrimStart('/', '\\')));
        }
    }
}
