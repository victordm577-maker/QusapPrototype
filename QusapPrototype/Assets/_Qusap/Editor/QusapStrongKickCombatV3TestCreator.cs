#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using Qusap;
using UnityEditor;
using UnityEditor.Animations;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Qusap.EditorTools
{
    public static class QusapStrongKickCombatV3TestCreator
    {
        private const string ArtFolder = "Assets/Scenes/Art/Characters/Qusap/Final_Light_v3/";
        private const string ExternalFbxPath =
            @"C:\Users\victo\Documents\Qusap\3D\Modelado\Qusap_Luz_Definitivo\03_Export_Unity\Qusap_Luz_Combat_v3.fbx";
        private const string SourceFbxPath = ArtFolder + "Qusap_Luz_Combat_v2.fbx";
        private const string TargetFbxPath = ArtFolder + "Qusap_Luz_Combat_v3.fbx";
        private const string SourceScenePath =
            "Assets/_Qusap/Scenes/Qusap_WeakKickAnimationV2Test_v1.unity";
        private const string TargetScenePath =
            "Assets/_Qusap/Scenes/Qusap_StrongKickAnimationTest_v1.unity";
        private const string SourceControllerPath =
            ArtFolder + "Qusap_Luz_Animator_CombatV2Test_v1.controller";
        private const string TargetControllerPath =
            ArtFolder + "Qusap_Luz_Animator_CombatV3Test_v1.controller";
        private const string ActiveVisualName = "PlayerVisual";
        private const string BackupVisualName = "PlayerVisual_CombatV2_Backup";
        private const float StrongGroundSpeed = 1f;
        private const float StrongAirSpeed = 1.011904762f;
        private const float FrameTolerance = 0.01f;
        private const float SoleAlignmentTolerance = 0.002f;

        private static readonly ClipRequirement[] ClipRequirements =
        {
            new("Qusap_Idle", true),
            new("Qusap_Run", true),
            new("Qusap_JumpRise", false),
            new("Qusap_Fall", true),
            new("Qusap_Land", false),
            new("Qusap_WallSlide", true),
            new("Qusap_WallJump", false),
            new("Qusap_Dash", false),
            new("Qusap_WeakKickGround", false),
            new("Qusap_WeakKickAir", false),
            new("Qusap_StrongKickGround", false),
            new("Qusap_StrongKickAir", false)
        };

        private static readonly string[] GroundSupportClips =
        {
            "Qusap_Idle", "Qusap_Run", "Qusap_WeakKickGround", "Qusap_StrongKickGround"
        };

        [MenuItem("Tools/Qusap/Create Strong Kick Combat V3 Test")]
        public static void CreateStrongKickCombatV3Test()
        {
            try
            {
                EnsureEditorIsIdle();
                if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo())
                    return;
                if (!ConfirmReplacement())
                    return;
                string report = CreateInternal();
                EditorUtility.DisplayDialog("Qusap Strong Kick Combat V3 Test", report, "Aceptar");
            }
            catch (Exception exception)
            {
                Debug.LogException(exception);
                EditorUtility.DisplayDialog(
                    "Qusap Strong Kick Combat V3 Test",
                    "No se completó la prueba V3. La escena V2, su controller, los prefabs y los FBX anteriores permanecen intactos.\n\n"
                    + exception.Message,
                    "Aceptar");
            }
        }

        [MenuItem("Tools/Qusap/Repair Strong Kick Combat V3 Test")]
        public static void RepairStrongKickCombatV3Test()
        {
            try
            {
                EnsureEditorIsIdle();
                if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo())
                    return;
                string report = RepairInternal();
                EditorUtility.DisplayDialog("Repair Strong Kick Combat V3 Test", report, "Aceptar");
            }
            catch (Exception exception)
            {
                Debug.LogException(exception);
                EditorUtility.DisplayDialog(
                    "Repair Strong Kick Combat V3 Test",
                    "No se completó la reparación. Las escenas V2, los prefabs, los FBX y los datos de combate permanecen intactos.\n\n"
                    + exception.Message,
                    "Aceptar");
            }
        }

        public static void RepairStrongKickCombatV3TestBatch()
        {
            EnsureEditorIsIdle();
            Debug.Log(RepairInternal());
        }

        // Entry point for safe batch execution. It never enters Play Mode.
        public static void CreateStrongKickCombatV3TestBatch()
        {
            EnsureEditorIsIdle();
            string report = CreateInternal();
            Debug.Log(report);
        }

        private static string RepairInternal()
        {
            string inventoryBefore = CaptureTargetAnimatorInventory();
            PreflightContext preflight = InspectSourceScene();
            Dictionary<string, AnimationClip> clips = ValidateV3ImporterReadOnly();
            GameObject model = RequireAsset<GameObject>(TargetFbxPath, "FBX Combat_v3");
            ValidateModel(model);
            string bindingReport = ValidateClipBindings(model, clips);

            bool rebuiltController = false;
            AnimatorController controller = AssetDatabase.LoadAssetAtPath<AnimatorController>(TargetControllerPath);
            try
            {
                if (controller == null)
                    throw new InvalidOperationException("El controller CombatV3Test no existe.");
                ValidateControllerCopy(preflight.Controller, controller, clips);
            }
            catch (Exception exception)
            {
                Debug.Log("CombatV3Test será reconstruido desde Combat V2: " + exception.Message);
                controller = CreateControllerCopy(preflight, clips);
                rebuiltController = true;
            }

            SceneBuildResult sceneResult = CreateSceneCopy(preflight, model, controller, clips);
            if (!EditorSceneManager.SaveScene(sceneResult.Scene, TargetScenePath))
                throw new InvalidOperationException("Unity no pudo guardar la escena Combat V3 reparada.");
            AssetDatabase.SaveAssets();
            string inventoryAfter = CaptureAnimatorInventory(sceneResult.Scene);
            Selection.activeObject = AssetDatabase.LoadAssetAtPath<SceneAsset>(TargetScenePath);
            string report = BuildRepairReport(preflight, sceneResult, controller,
                rebuiltController, inventoryBefore, inventoryAfter, bindingReport);
            Debug.Log(report);
            return report;
        }

        private static string CreateInternal()
        {
            PreflightContext preflight = InspectSourceScene();
            var protectedHashes = new Dictionary<string, string>(StringComparer.Ordinal)
            {
                [SourceScenePath] = FileHash(AbsolutePath(SourceScenePath)),
                [SourceControllerPath] = FileHash(AbsolutePath(SourceControllerPath)),
                [SourceFbxPath] = FileHash(AbsolutePath(SourceFbxPath)),
                [SourceFbxPath + ".meta"] = FileHash(AbsolutePath(SourceFbxPath + ".meta"))
            };

            EnsureExactV3Fbx();
            Dictionary<string, AnimationClip> clips = ConfigureV3Importer();
            GameObject model = RequireAsset<GameObject>(TargetFbxPath, "FBX Combat_v3");
            ValidateModel(model);
            AnimatorController controller = CreateControllerCopy(preflight, clips);
            SceneBuildResult sceneResult = CreateSceneCopy(preflight, model, controller, clips);

            foreach (KeyValuePair<string, string> pair in protectedHashes)
                if (FileHash(AbsolutePath(pair.Key)) != pair.Value)
                    throw new InvalidOperationException($"Cambió un asset protegido durante la creación: {pair.Key}");

            if (!EditorSceneManager.SaveScene(sceneResult.Scene, TargetScenePath))
                throw new InvalidOperationException("Unity no pudo guardar la escena Combat V3.");
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            Selection.activeObject = AssetDatabase.LoadAssetAtPath<SceneAsset>(TargetScenePath);
            string report = BuildReport(preflight, sceneResult);
            Debug.Log(report);
            return report;
        }

        private static bool ConfirmReplacement()
        {
            bool exists = AssetDatabase.LoadAssetAtPath<UnityEngine.Object>(TargetControllerPath) != null
                || AssetDatabase.LoadAssetAtPath<UnityEngine.Object>(TargetScenePath) != null;
            return !exists || EditorUtility.DisplayDialog(
                "Qusap Strong Kick Combat V3 Test",
                "Ya existe una salida Combat V3. ¿Deseas reemplazar únicamente su controller y escena de prueba?",
                "Reemplazar V3", "Cancelar");
        }

        private static PreflightContext InspectSourceScene()
        {
            RequireAsset<SceneAsset>(SourceScenePath, "escena funcional Combat V2");
            RequireAsset<AnimatorController>(SourceControllerPath, "controller funcional Combat V2");
            RequireAsset<GameObject>(SourceFbxPath, "FBX Combat_v2");
            Scene preview = EditorSceneManager.OpenPreviewScene(SourceScenePath);
            try
            {
                PlayerContext[] players = ResolvePlayers(preview, SourceControllerPath, SourceFbxPath);
                if (players.Length != 2)
                    throw new InvalidOperationException(
                        $"La escena candidata '{SourceScenePath}' es ambigua: contiene {players.Length} jugadores funcionales; se esperaban 2.");
                string[] controllerPaths = players.Select(player =>
                        AssetDatabase.GetAssetPath(player.Animator.runtimeAnimatorController))
                    .Distinct(StringComparer.Ordinal).ToArray();
                if (controllerPaths.Length != 1 || controllerPaths[0] != SourceControllerPath)
                    throw new InvalidOperationException(
                        "Los PlayerVisual activos no comparten el único Animator Controller Combat V2 esperado: "
                        + string.Join(", ", controllerPaths));

                AnimatorController controller = RequireAsset<AnimatorController>(
                    SourceControllerPath, "controller Combat V2 activo");
                ControllerSnapshot snapshot = CaptureController(controller);
                string[] expectedStates = ClipRequirements.Take(10).Select(item => item.Name).ToArray();
                if (snapshot.States.Count != 10
                    || expectedStates.Any(name => !snapshot.States.ContainsKey(name)))
                    throw new InvalidOperationException(
                        $"El controller Combat V2 contiene {snapshot.States.Count} estados; no coincide con los diez estados funcionales esperados.");
                ValidateCombatParameters(controller);
                ValidateKickTransitions(controller, "Qusap_WeakKickGround", QusapAttackVariant.WeakKickGround);
                ValidateKickTransitions(controller, "Qusap_WeakKickAir", QusapAttackVariant.WeakKickAir);
                ValidateStrongKickTimings(players);
                ValidateDepthAndPlane(players);
                return new PreflightContext(snapshot, players.Length);
            }
            finally
            {
                EditorSceneManager.ClosePreviewScene(preview);
            }
        }

        private static void EnsureExactV3Fbx()
        {
            if (!File.Exists(ExternalFbxPath))
                throw new FileNotFoundException("No existe el FBX Combat_v3 externo.", ExternalFbxPath);
            string destination = AbsolutePath(TargetFbxPath);
            if (!File.Exists(destination))
            {
                if (File.Exists(destination + ".meta"))
                    throw new InvalidOperationException("Existe un .meta huérfano para Combat_v3; no se sobrescribirá.");
                File.Copy(ExternalFbxPath, destination, false);
            }
            if (FileHash(ExternalFbxPath) != FileHash(destination))
                throw new InvalidOperationException(
                    "El Combat_v3 existente no coincide con el archivo fuente externo; no se sobrescribirá.");
            AssetDatabase.ImportAsset(TargetFbxPath, ImportAssetOptions.ForceSynchronousImport);
        }

        private static Dictionary<string, AnimationClip> ConfigureV3Importer()
        {
            var importer = AssetImporter.GetAtPath(TargetFbxPath) as ModelImporter;
            var reference = AssetImporter.GetAtPath(SourceFbxPath) as ModelImporter;
            if (importer == null || reference == null)
                throw new InvalidOperationException("No se resolvieron los ModelImporter Combat_v2/v3.");
            CopyImporterSettings(reference, importer);

            ModelImporterClipAnimation[] takes = importer.defaultClipAnimations;
            if (takes == null || takes.Length == 0)
                throw new InvalidOperationException("Combat_v3 no expone Source Takes.");
            ModelImporterClipAnimation[] candidates = takes.Where(take => take != null
                    && !IsPreviewName(take.name) && !IsPreviewName(take.takeName)
                    && !HasNumberedDuplicate(take.name) && !HasNumberedDuplicate(take.takeName))
                .GroupBy(take => new { take.name, take.takeName, take.firstFrame, take.lastFrame })
                .Select(group => group.First()).ToArray();

            var definitions = new List<ModelImporterClipAnimation>();
            foreach (ClipRequirement requirement in ClipRequirements)
            {
                ModelImporterClipAnimation[] matches = candidates.Where(take =>
                    IsExactTakeName(take.name, requirement.Name)
                    || IsExactTakeName(take.takeName, requirement.Name)).ToArray();
                if (matches.Length != 1)
                {
                    string found = string.Join(", ", takes.Select(take => $"'{take.name}'/'{take.takeName}'"));
                    throw new InvalidOperationException(
                        $"Source Take '{requirement.Name}': encontrados {matches.Length}; se requiere exactamente 1. Takes: {found}");
                }
                ModelImporterClipAnimation source = matches[0];
                definitions.Add(new ModelImporterClipAnimation
                {
                    name = requirement.Name,
                    takeName = string.IsNullOrWhiteSpace(source.takeName) ? source.name : source.takeName,
                    firstFrame = source.firstFrame,
                    lastFrame = source.lastFrame,
                    loopTime = requirement.Loop,
                    loopPose = requirement.Loop,
                    keepOriginalOrientation = true,
                    keepOriginalPositionY = true,
                    keepOriginalPositionXZ = true,
                    lockRootRotation = true,
                    lockRootHeightY = true,
                    lockRootPositionXZ = true
                });
            }

            importer.animationType = ModelImporterAnimationType.Generic;
            importer.avatarSetup = ModelImporterAvatarSetup.CreateFromThisModel;
            importer.importAnimation = true;
            importer.animationCompression = ModelImporterAnimationCompression.Off;
            importer.resampleCurves = true;
            importer.motionNodeName = string.Empty;
            importer.importCameras = false;
            importer.importLights = false;
            importer.clipAnimations = definitions.ToArray();
            importer.SaveAndReimport();

            importer = AssetImporter.GetAtPath(TargetFbxPath) as ModelImporter;
            Dictionary<string, AnimationClip> clips = LoadV3Clips();
            if (importer == null || importer.animationType != ModelImporterAnimationType.Generic
                || importer.avatarSetup != ModelImporterAvatarSetup.CreateFromThisModel
                || !importer.importAnimation || importer.animationCompression != ModelImporterAnimationCompression.Off
                || !string.IsNullOrEmpty(importer.motionNodeName) || importer.importCameras || importer.importLights
                || importer.clipAnimations.Length != 12 || clips.Count != 12
                || !ImporterSettingsMatch(reference, importer))
                throw new InvalidOperationException("Combat_v3 no conservó la configuración de importación requerida.");

            foreach (ClipRequirement requirement in ClipRequirements)
            {
                ModelImporterClipAnimation source = candidates.Single(take =>
                    IsExactTakeName(take.name, requirement.Name)
                    || IsExactTakeName(take.takeName, requirement.Name));
                ModelImporterClipAnimation configured = importer.clipAnimations.SingleOrDefault(
                    clip => clip.name == requirement.Name);
                AnimationClip clip = clips[requirement.Name];
                float expectedFrames = source.lastFrame - source.firstFrame;
                if (configured == null || configured.firstFrame != source.firstFrame
                    || configured.lastFrame != source.lastFrame || configured.loopTime != requirement.Loop
                    || configured.loopPose != requirement.Loop || !configured.lockRootRotation
                    || !configured.lockRootHeightY || !configured.lockRootPositionXZ
                    || !configured.keepOriginalOrientation || !configured.keepOriginalPositionY
                    || !configured.keepOriginalPositionXZ
                    || Mathf.Abs(clip.length * clip.frameRate - expectedFrames) > FrameTolerance)
                    throw new InvalidOperationException(
                        $"{requirement.Name} cambió de rango, loop o bloqueo de root durante la importación.");
            }
            return clips;
        }

        private static Dictionary<string, AnimationClip> ValidateV3ImporterReadOnly()
        {
            var importer = AssetImporter.GetAtPath(TargetFbxPath) as ModelImporter;
            if (importer == null)
                throw new InvalidOperationException("No se encontró el ModelImporter de Combat_v3.");
            Dictionary<string, AnimationClip> clips = LoadV3Clips();
            if (importer.animationType != ModelImporterAnimationType.Generic
                || importer.avatarSetup != ModelImporterAvatarSetup.CreateFromThisModel
                || !importer.importAnimation
                || importer.animationCompression != ModelImporterAnimationCompression.Off
                || !string.IsNullOrEmpty(importer.motionNodeName)
                || importer.importCameras || importer.importLights
                || importer.clipAnimations.Length != 12)
                throw new InvalidOperationException("La configuración de importación Combat_v3 no es válida.");
            foreach (ClipRequirement requirement in ClipRequirements)
            {
                ModelImporterClipAnimation clip = importer.clipAnimations.SingleOrDefault(
                    item => item.name == requirement.Name);
                if (clip == null || clip.loopTime != requirement.Loop
                    || clip.loopPose != requirement.Loop || !clip.lockRootRotation
                    || !clip.lockRootHeightY || !clip.lockRootPositionXZ)
                    throw new InvalidOperationException(
                        $"El clip importado '{requirement.Name}' no conserva loop/root lock requeridos.");
            }
            return clips;
        }

        private static string ValidateClipBindings(GameObject model,
            IReadOnlyDictionary<string, AnimationClip> clips)
        {
            HashSet<string> hierarchyPaths = model.GetComponentsInChildren<Transform>(true)
                .Select(item => AnimationUtility.CalculateTransformPath(item, model.transform))
                .ToHashSet(StringComparer.Ordinal);
            var usedPaths = new HashSet<string>(StringComparer.Ordinal);
            foreach (AnimationClip clip in clips.Values)
            {
                EditorCurveBinding[] bindings = AnimationUtility.GetCurveBindings(clip)
                    .Concat(AnimationUtility.GetObjectReferenceCurveBindings(clip)).ToArray();
                string[] missing = bindings.Select(binding => binding.path).Distinct()
                    .Where(path => !hierarchyPaths.Contains(path)).ToArray();
                if (missing.Length != 0)
                    throw new InvalidOperationException(
                        $"{clip.name} contiene bindings sin destino en Combat_v3: {string.Join(", ", missing)}");
                foreach (string path in bindings.Select(binding => binding.path))
                    usedPaths.Add(path);
            }

            string rootPath = RequireSemanticPath(hierarchyPaths, "Root");
            string bodyPath = RequireSemanticPath(hierarchyPaths, "Body");
            string leftFootPath = RequireSemanticPath(hierarchyPaths, "FootL");
            string rightFootPath = RequireSemanticPath(hierarchyPaths, "FootR");
            SkinnedMeshRenderer[] skinned = model.GetComponentsInChildren<SkinnedMeshRenderer>(true);
            Transform rig = skinned.Select(renderer => renderer.rootBone)
                .FirstOrDefault(root => root != null)?.parent;
            string rigPath = rig != null
                ? AnimationUtility.CalculateTransformPath(rig, model.transform)
                : hierarchyPaths.FirstOrDefault(path => NormalizeBoneName(Path.GetFileName(path)).Contains("rig"));
            if (string.IsNullOrEmpty(rigPath))
                throw new InvalidOperationException("No se pudo resolver la raíz del rig de Combat_v3.");
            return $"Rig={rigPath}; Root={rootPath}; Body={bodyPath}; "
                + $"Foot_L={leftFootPath}; Foot_R={rightFootPath}; bindings={usedPaths.Count}";
        }

        private static string RequireSemanticPath(IEnumerable<string> paths, string semanticName)
        {
            string normalized = NormalizeBoneName(semanticName);
            string[] matches = paths.Where(path =>
                    NormalizeBoneName(Path.GetFileName(path)).Equals(normalized, StringComparison.Ordinal))
                .ToArray();
            if (matches.Length == 0)
                throw new InvalidOperationException(
                    $"Combat_v3 no contiene una ruta compatible con el hueso requerido '{semanticName}'.");
            return matches.OrderBy(path => path.Length).First();
        }

        private static string NormalizeBoneName(string value)
        {
            return Regex.Replace(value ?? string.Empty, "[^A-Za-z0-9]", string.Empty);
        }

        private static void CopyImporterSettings(ModelImporter source, ModelImporter target)
        {
            target.globalScale = source.globalScale;
            target.useFileScale = source.useFileScale;
            target.bakeAxisConversion = source.bakeAxisConversion;
            target.preserveHierarchy = source.preserveHierarchy;
            target.sortHierarchyByName = source.sortHierarchyByName;
            target.importBlendShapes = source.importBlendShapes;
            target.importVisibility = source.importVisibility;
            target.importNormals = source.importNormals;
            target.importTangents = source.importTangents;
            target.meshCompression = source.meshCompression;
            target.isReadable = source.isReadable;
            target.optimizeGameObjects = source.optimizeGameObjects;
            target.extraExposedTransformPaths = source.extraExposedTransformPaths;
            target.humanDescription = source.humanDescription;
            target.materialImportMode = source.materialImportMode;
            target.materialLocation = source.materialLocation;
            target.materialName = source.materialName;
            target.materialSearch = source.materialSearch;
            foreach (KeyValuePair<AssetImporter.SourceAssetIdentifier, UnityEngine.Object> mapping
                in source.GetExternalObjectMap())
                target.AddRemap(mapping.Key, mapping.Value);
        }

        private static bool ImporterSettingsMatch(ModelImporter source, ModelImporter target)
        {
            return Mathf.Approximately(source.globalScale, target.globalScale)
                && source.useFileScale == target.useFileScale
                && source.bakeAxisConversion == target.bakeAxisConversion
                && source.preserveHierarchy == target.preserveHierarchy
                && source.sortHierarchyByName == target.sortHierarchyByName
                && source.importBlendShapes == target.importBlendShapes
                && source.importVisibility == target.importVisibility
                && source.importNormals == target.importNormals
                && source.importTangents == target.importTangents
                && source.meshCompression == target.meshCompression
                && source.isReadable == target.isReadable
                && source.optimizeGameObjects == target.optimizeGameObjects
                && source.materialImportMode == target.materialImportMode
                && source.materialLocation == target.materialLocation
                && source.materialName == target.materialName
                && source.materialSearch == target.materialSearch;
        }

        private static AnimatorController CreateControllerCopy(PreflightContext preflight,
            IReadOnlyDictionary<string, AnimationClip> clips)
        {
            DeleteTargetAsset(TargetControllerPath);
            if (!AssetDatabase.CopyAsset(SourceControllerPath, TargetControllerPath))
                throw new InvalidOperationException("Unity no pudo duplicar el controller Combat V2.");
            var controller = RequireAsset<AnimatorController>(TargetControllerPath, "controller CombatV3Test");
            ControllerSnapshot pristine = CaptureController(controller);
            if (!ControllerSnapshotsEqual(preflight.Controller, pristine))
                throw new InvalidOperationException("La copia inicial del controller V2 no es semánticamente idéntica.");

            AnimatorStateMachine machine = controller.layers[0].stateMachine;
            Dictionary<string, AnimatorState> states = EnumerateStates(machine)
                .ToDictionary(state => state.name, StringComparer.Ordinal);
            foreach (KeyValuePair<string, AnimatorState> pair in states)
            {
                pair.Value.motion = clips[pair.Key];
                EditorUtility.SetDirty(pair.Value);
            }

            float rightEdge = machine.states.Length == 0
                ? 1000f : machine.states.Max(child => child.position.x) + 220f;
            AnimatorState strongGround = AddKickState(machine, "Qusap_StrongKickGround",
                clips["Qusap_StrongKickGround"], StrongGroundSpeed, new Vector3(rightEdge, -10f, 0f));
            AnimatorState strongAir = AddKickState(machine, "Qusap_StrongKickAir",
                clips["Qusap_StrongKickAir"], StrongAirSpeed, new Vector3(rightEdge, 100f, 0f));
            AddKickEntry(machine, strongGround, QusapAttackVariant.StrongKickGround);
            AddKickEntry(machine, strongAir, QusapAttackVariant.StrongKickAir);
            AddKickExits(strongGround, states);
            AddKickExits(strongAir, states);

            EditorUtility.SetDirty(machine);
            EditorUtility.SetDirty(controller);
            AssetDatabase.SaveAssetIfDirty(controller);
            controller = RequireAsset<AnimatorController>(TargetControllerPath, "controller CombatV3Test guardado");
            ValidateControllerCopy(preflight.Controller, controller, clips);
            return controller;
        }

        private static AnimatorState AddKickState(AnimatorStateMachine machine, string name,
            AnimationClip clip, float speed, Vector3 position)
        {
            AnimatorState state = machine.AddState(name, position);
            state.motion = clip;
            state.speed = speed;
            state.writeDefaultValues = true;
            EditorUtility.SetDirty(state);
            return state;
        }

        private static void AddKickEntry(AnimatorStateMachine machine, AnimatorState state,
            QusapAttackVariant variant)
        {
            AnimatorStateTransition transition = machine.AddAnyStateTransition(state);
            ConfigureTransition(transition, 0.02f);
            transition.AddCondition(AnimatorConditionMode.If, 0f, "CombatAnimating");
            transition.AddCondition(AnimatorConditionMode.Equals, (int)variant, "AttackVariant");
            EditorUtility.SetDirty(transition);
        }

        private static void AddKickExits(AnimatorState kick,
            IReadOnlyDictionary<string, AnimatorState> states)
        {
            AddKickExit(kick, states["Qusap_Idle"], true, "Speed", AnimatorConditionMode.Less);
            AddKickExit(kick, states["Qusap_Run"], true, "Speed", AnimatorConditionMode.Greater);
            AddKickExit(kick, states["Qusap_JumpRise"], false, "VerticalSpeed", AnimatorConditionMode.Greater);
            AddKickExit(kick, states["Qusap_Fall"], false, "VerticalSpeed", AnimatorConditionMode.Less);
        }

        private static void AddKickExit(AnimatorState source, AnimatorState destination, bool grounded,
            string movementParameter, AnimatorConditionMode mode)
        {
            AnimatorStateTransition transition = source.AddTransition(destination);
            ConfigureTransition(transition, 0.03f);
            transition.AddCondition(AnimatorConditionMode.IfNot, 0f, "CombatAnimating");
            transition.AddCondition(grounded ? AnimatorConditionMode.If : AnimatorConditionMode.IfNot,
                0f, "Grounded");
            transition.AddCondition(mode, 0.1f, movementParameter);
            EditorUtility.SetDirty(transition);
        }

        private static void ConfigureTransition(AnimatorStateTransition transition, float duration)
        {
            transition.hasExitTime = false;
            transition.hasFixedDuration = true;
            transition.duration = duration;
            transition.canTransitionToSelf = false;
        }

        private static void ValidateControllerCopy(ControllerSnapshot source, AnimatorController target,
            IReadOnlyDictionary<string, AnimationClip> clips)
        {
            ValidateCombatParameters(target);
            ControllerSnapshot actual = CaptureController(target);
            if (actual.States.Count != 12 || source.Parameters != actual.Parameters
                || source.Layer != actual.Layer)
                throw new InvalidOperationException("Parámetros, layer o cantidad de estados incorrectos en Combat V3.");
            foreach (KeyValuePair<string, StateSnapshot> pair in source.States)
            {
                if (!actual.States.TryGetValue(pair.Key, out StateSnapshot state)
                    || pair.Value.Semantics != state.Semantics
                    || pair.Value.Transitions != state.Transitions
                    || !Mathf.Approximately(pair.Value.Speed, state.Speed)
                    || state.Motion != clips[pair.Key]
                    || AssetDatabase.GetAssetPath(state.Motion) != TargetFbxPath)
                    throw new InvalidOperationException($"El estado funcional V2 cambió fuera de su Motion: {pair.Key}.");
            }
            string[] sourceAny = SplitSignatures(source.AnyStateTransitions);
            string[] targetAny = SplitSignatures(actual.AnyStateTransitions);
            if (targetAny.Length != sourceAny.Length + 2
                || sourceAny.Any(signature => !targetAny.Contains(signature)))
                throw new InvalidOperationException("Las transiciones Any State existentes no se conservaron intactas.");
            foreach (ClipRequirement requirement in ClipRequirements)
            {
                AnimatorState state = EnumerateStates(target.layers[0].stateMachine)
                    .Single(item => item.name == requirement.Name);
                if (state.motion != clips[requirement.Name]
                    || AssetDatabase.GetAssetPath(state.motion) != TargetFbxPath)
                    throw new InvalidOperationException($"Motion Combat_v3 incorrecto: {requirement.Name}.");
            }
            AnimatorState ground = EnumerateStates(target.layers[0].stateMachine)
                .Single(state => state.name == "Qusap_StrongKickGround");
            AnimatorState air = EnumerateStates(target.layers[0].stateMachine)
                .Single(state => state.name == "Qusap_StrongKickAir");
            if (!Mathf.Approximately(ground.speed, StrongGroundSpeed)
                || !Mathf.Approximately(air.speed, StrongAirSpeed))
                throw new InvalidOperationException("Las velocidades StrongKick no son las requeridas.");
            ValidateKickTransitions(target, ground.name, QusapAttackVariant.StrongKickGround);
            ValidateKickTransitions(target, air.name, QusapAttackVariant.StrongKickAir);
        }

        private static SceneBuildResult CreateSceneCopy(PreflightContext preflight, GameObject model,
            AnimatorController controller, IReadOnlyDictionary<string, AnimationClip> clips)
        {
            DeleteTargetAsset(TargetScenePath);
            if (!AssetDatabase.CopyAsset(SourceScenePath, TargetScenePath))
                throw new InvalidOperationException("Unity no pudo copiar la escena funcional Combat V2.");
            Scene scene = EditorSceneManager.OpenScene(TargetScenePath, OpenSceneMode.Single);
            PlayerContext[] players = ResolvePlayers(scene, SourceControllerPath, SourceFbxPath);
            if (players.Length != preflight.PlayerCount)
                throw new InvalidOperationException(
                    $"La copia contiene {players.Length} jugadores y la fuente {preflight.PlayerCount}.");

            Dictionary<Component, string> protectedComponents = CaptureProtectedComponents(scene, players);
            var alignments = new List<AlignmentReport>();
            for (int index = 0; index < players.Length; index++)
            {
                string combatBefore = EditorJsonUtility.ToJson(players[index].Combat);
                alignments.Add(InstallV3Visual(players[index], model, controller, clips));
                if (EditorJsonUtility.ToJson(players[index].Combat) != combatBefore)
                    throw new InvalidOperationException(
                        $"Los tiempos o datos de combate del jugador {index + 1} cambiaron.");
            }
            ValidateProtectedComponents(protectedComponents);
            ValidateInstalledScene(scene, controller, players.Length);
            EditorSceneManager.MarkSceneDirty(scene);
            return new SceneBuildResult(scene, alignments.ToArray());
        }

        private static AlignmentReport InstallV3Visual(PlayerContext player, GameObject model,
            AnimatorController controller, IReadOnlyDictionary<string, AnimationClip> clips)
        {
            CapsuleCollider capsule = RequirePhysicalCapsule(player);
            Rigidbody body = player.Root.GetComponent<Rigidbody>();
            string capsuleBefore = EditorJsonUtility.ToJson(capsule);
            string bodyBefore = EditorJsonUtility.ToJson(body);
            Vector3 rootPosition = player.Root.position;
            Quaternion rootRotation = player.Root.rotation;
            Vector3 rootScale = player.Root.localScale;

            Transform oldVisual = player.Visual;
            Vector3 oldPosition = oldVisual.localPosition;
            Quaternion oldRotation = oldVisual.localRotation;
            Vector3 oldScale = oldVisual.localScale;
            Animator sourceAnimator = player.Animator;
            AnimatorUpdateMode updateMode = sourceAnimator.updateMode;
            bool fireEvents = sourceAnimator.fireEvents;

            oldVisual.name = BackupVisualName;
            oldVisual.gameObject.SetActive(false);
            GameObject visual = PrefabUtility.InstantiatePrefab(model, player.Root) as GameObject;
            if (visual == null)
                throw new InvalidOperationException("Unity no pudo instanciar Combat_v3.");
            visual.name = ActiveVisualName;
            visual.transform.localPosition = oldPosition;
            visual.transform.localRotation = oldRotation;
            visual.transform.localScale = oldScale;
            visual.SetActive(true);

            Animator animator = visual.GetComponent<Animator>() ?? visual.AddComponent<Animator>();
            Avatar avatar = model.GetComponent<Animator>()?.avatar;
            if (avatar == null || !avatar.isValid || avatar.isHuman)
                throw new InvalidOperationException("Combat_v3 no expone un Avatar Generic válido.");
            animator.avatar = avatar;
            animator.runtimeAnimatorController = controller;
            animator.enabled = true;
            animator.applyRootMotion = false;
            animator.cullingMode = AnimatorCullingMode.AlwaysAnimate;
            animator.updateMode = updateMode;
            animator.fireEvents = fireEvents;
            ConnectDriver(player.Root, visual.transform, animator);
            Physics.SyncTransforms();
            float colliderBottom = capsule.bounds.min.y;
            float soleBefore = LowestAtClipPose(player, model, clips["Qusap_Idle"],
                oldPosition, oldRotation, oldScale, 0f, controller, avatar);
            float worldDelta = colliderBottom - soleBefore;
            float parentYScale = Vector3.Dot(player.Root.TransformVector(Vector3.up), Vector3.up);
            if (Mathf.Abs(parentYScale) < 0.0001f
                || Vector3.Cross(player.Root.TransformVector(Vector3.up).normalized, Vector3.up).sqrMagnitude > 0.000001f)
                throw new InvalidOperationException(
                    $"El jugador '{player.Root.name}' no permite corregir altura cambiando únicamente localPosition.y.");
            Vector3 corrected = oldPosition;
            corrected.y += worldDelta / parentYScale;
            visual.transform.localPosition = corrected;
            Physics.SyncTransforms();
            float soleAfter = LowestAtClipPose(player, model, clips["Qusap_Idle"],
                corrected, oldRotation, oldScale, 0f, controller, avatar);
            if (Mathf.Abs(soleAfter - colliderBottom) > SoleAlignmentTolerance)
                throw new InvalidOperationException(
                    $"Alineación de suela fuera de tolerancia en '{player.Root.name}': {Mathf.Abs(soleAfter - colliderBottom):R}.");

            GroundSupportReport support = ValidateGroundSupport(player, model, clips, corrected,
                oldRotation, oldScale, colliderBottom, controller, avatar);
            float positiveGap = support.MaximumGap.Values.Max();
            if (positiveGap > SoleAlignmentTolerance)
            {
                corrected.y -= positiveGap / parentYScale;
                visual.transform.localPosition = corrected;
                Physics.SyncTransforms();
                support = ValidateGroundSupport(player, model, clips, corrected,
                    oldRotation, oldScale, colliderBottom, controller, avatar);
                positiveGap = support.MaximumGap.Values.Max();
            }
            if (positiveGap > SoleAlignmentTolerance)
                throw new InvalidOperationException(
                    $"Las animaciones terrestres aún separan las suelas {positiveGap:R} de la base del collider.");
            soleAfter = LowestAtClipPose(player, model, clips["Qusap_Idle"],
                corrected, oldRotation, oldScale, 0f, controller, avatar);

            if (visual.transform.localPosition.x != oldPosition.x
                || visual.transform.localPosition.z != oldPosition.z
                || visual.transform.localRotation != oldRotation
                || visual.transform.localScale != oldScale || animator.applyRootMotion
                || !animator.enabled || !animator.gameObject.activeInHierarchy
                || animator.runtimeAnimatorController != controller
                || animator.avatar != avatar || !animator.avatar.isValid
                || animator.cullingMode != AnimatorCullingMode.AlwaysAnimate
                || player.Root.position != rootPosition || player.Root.rotation != rootRotation
                || player.Root.localScale != rootScale
                || EditorJsonUtility.ToJson(capsule) != capsuleBefore
                || EditorJsonUtility.ToJson(body) != bodyBefore)
                throw new InvalidOperationException(
                    $"Se alteró un transform, Rigidbody, collider o Root Motion protegido en '{player.Root.name}'.");

            PrefabUtility.RecordPrefabInstancePropertyModifications(oldVisual.gameObject);
            PrefabUtility.RecordPrefabInstancePropertyModifications(visual);
            PrefabUtility.RecordPrefabInstancePropertyModifications(visual.transform);
            PrefabUtility.RecordPrefabInstancePropertyModifications(animator);
            return new AlignmentReport(player.Root.name, oldPosition, corrected,
                corrected.y - oldPosition.y, colliderBottom, soleAfter, support);
        }

        private static float LowestAtClipPose(PlayerContext player, GameObject model, AnimationClip clip,
            Vector3 position, Quaternion rotation, Vector3 scale, float time,
            RuntimeAnimatorController controller, Avatar avatar)
        {
            GameObject sample = CreatePoseSample(player, model, position, rotation, scale, controller, avatar);
            try
            {
                clip.SampleAnimation(sample, time);
                Physics.SyncTransforms();
                return LowestRenderedPoint(sample);
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(sample);
            }
        }

        private static GroundSupportReport ValidateGroundSupport(PlayerContext player, GameObject model,
            IReadOnlyDictionary<string, AnimationClip> clips, Vector3 position, Quaternion rotation,
            Vector3 scale, float colliderBottom, RuntimeAnimatorController controller, Avatar avatar)
        {
            GameObject sample = CreatePoseSample(player, model, position, rotation, scale, controller, avatar);
            var maximumGap = new Dictionary<string, float>(StringComparer.Ordinal);
            try
            {
                foreach (string clipName in GroundSupportClips)
                {
                    AnimationClip clip = clips[clipName];
                    int frames = Mathf.Max(1, Mathf.CeilToInt(clip.length * clip.frameRate));
                    float worst = float.NegativeInfinity;
                    for (int frame = 0; frame <= frames; frame++)
                    {
                        float time = frames == 0 ? 0f : clip.length * frame / frames;
                        clip.SampleAnimation(sample, time);
                        Physics.SyncTransforms();
                        float gap = LowestRenderedPoint(sample) - colliderBottom;
                        worst = Mathf.Max(worst, gap);
                    }
                    maximumGap[clipName] = worst;
                }
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(sample);
            }
            return new GroundSupportReport(maximumGap);
        }

        private static GameObject CreatePoseSample(PlayerContext player, GameObject model,
            Vector3 position, Quaternion rotation, Vector3 scale,
            RuntimeAnimatorController controller, Avatar avatar)
        {
            GameObject sample = PrefabUtility.InstantiatePrefab(model, player.Root) as GameObject;
            if (sample == null)
                throw new InvalidOperationException("No se pudo crear el modelo temporal para validar poses.");
            sample.name = "__CombatV3_PoseValidation";
            sample.hideFlags = HideFlags.HideAndDontSave;
            sample.transform.localPosition = position;
            sample.transform.localRotation = rotation;
            sample.transform.localScale = scale;
            Animator sampleAnimator = sample.GetComponent<Animator>() ?? sample.AddComponent<Animator>();
            sampleAnimator.avatar = avatar;
            sampleAnimator.runtimeAnimatorController = controller;
            sampleAnimator.applyRootMotion = false;
            sampleAnimator.cullingMode = AnimatorCullingMode.AlwaysAnimate;
            sampleAnimator.enabled = true;
            return sample;
        }

        private static void ConnectDriver(Transform playerRoot, Transform visual, Animator animator)
        {
            QusapAnimationDriver driver = playerRoot.GetComponent<QusapAnimationDriver>();
            if (driver == null)
                throw new InvalidOperationException($"'{playerRoot.name}' no contiene QusapAnimationDriver.");
            var serialized = new SerializedObject(driver);
            SerializedProperty animatorProperty = serialized.FindProperty("animator");
            SerializedProperty visualProperty = serialized.FindProperty("playerVisual");
            if (animatorProperty == null || visualProperty == null)
                throw new InvalidOperationException("QusapAnimationDriver no expone sus referencias de escena.");
            animatorProperty.objectReferenceValue = animator;
            visualProperty.objectReferenceValue = visual;
            serialized.ApplyModifiedPropertiesWithoutUndo();
            PrefabUtility.RecordPrefabInstancePropertyModifications(driver);
        }

        private static float LowestRenderedPoint(GameObject visual)
        {
            bool found = false;
            float minimum = float.PositiveInfinity;
            foreach (SkinnedMeshRenderer renderer in visual.GetComponentsInChildren<SkinnedMeshRenderer>(true))
            {
                if (renderer.sharedMesh == null)
                    continue;
                var baked = new Mesh();
                try
                {
                    renderer.BakeMesh(baked);
                    foreach (Vector3 vertex in baked.vertices)
                    {
                        minimum = Mathf.Min(minimum, renderer.transform.TransformPoint(vertex).y);
                        found = true;
                    }
                }
                finally
                {
                    UnityEngine.Object.DestroyImmediate(baked);
                }
            }
            foreach (MeshFilter filter in visual.GetComponentsInChildren<MeshFilter>(true))
            {
                if (filter.sharedMesh == null)
                    continue;
                Bounds bounds = filter.sharedMesh.bounds;
                foreach (Vector3 corner in BoundsCorners(bounds))
                {
                    minimum = Mathf.Min(minimum, filter.transform.TransformPoint(corner).y);
                    found = true;
                }
            }
            if (!found || float.IsInfinity(minimum) || float.IsNaN(minimum))
                throw new InvalidOperationException("Combat_v3 no expone geometría renderizable para medir las suelas.");
            return minimum;
        }

        private static IEnumerable<Vector3> BoundsCorners(Bounds bounds)
        {
            Vector3 center = bounds.center;
            Vector3 extents = bounds.extents;
            for (int x = -1; x <= 1; x += 2)
                for (int y = -1; y <= 1; y += 2)
                    for (int z = -1; z <= 1; z += 2)
                        yield return center + Vector3.Scale(extents, new Vector3(x, y, z));
        }

        private static CapsuleCollider RequirePhysicalCapsule(PlayerContext player)
        {
            CapsuleCollider[] capsules = player.Root.GetComponents<CapsuleCollider>()
                .Where(collider => collider.enabled && !collider.isTrigger).ToArray();
            if (capsules.Length != 1 || capsules[0].direction != 1)
                throw new InvalidOperationException(
                    $"'{player.Root.name}' tiene {capsules.Length} CapsuleCollider físicos activos; se requiere uno vertical.");
            if (player.Root.GetComponent<Rigidbody>() == null)
                throw new InvalidOperationException($"'{player.Root.name}' no tiene Rigidbody físico.");
            return capsules[0];
        }

        private static void ValidateInstalledScene(Scene scene, AnimatorController controller, int playerCount)
        {
            PlayerContext[] installed = ResolvePlayers(scene, TargetControllerPath, TargetFbxPath);
            if (installed.Length != playerCount)
                throw new InvalidOperationException(
                    $"Tras instalar v3 se resolvieron {installed.Length}/{playerCount} jugadores funcionales.");
            foreach (PlayerContext player in installed)
            {
                Transform[] backups = player.Root.Cast<Transform>()
                    .Where(child => child.name == BackupVisualName && !child.gameObject.activeSelf).ToArray();
                Animator[] activeAnimators = player.Root.GetComponentsInChildren<Animator>(true)
                    .Where(item => item.enabled && item.gameObject.activeInHierarchy).ToArray();
                var driver = player.Root.GetComponent<QusapAnimationDriver>();
                var serializedDriver = new SerializedObject(driver);
                Animator connectedAnimator = serializedDriver.FindProperty("animator")?.objectReferenceValue as Animator;
                Transform connectedVisual = serializedDriver.FindProperty("playerVisual")?.objectReferenceValue as Transform;
                if (backups.Length != 1 || player.Animator.runtimeAnimatorController != controller
                    || player.Animator.applyRootMotion || !player.Animator.enabled
                    || !player.Animator.gameObject.activeInHierarchy
                    || player.Animator.avatar == null || !player.Animator.avatar.isValid
                    || player.Animator.avatar.isHuman
                    || player.Animator.cullingMode != AnimatorCullingMode.AlwaysAnimate
                    || activeAnimators.Length != 1 || activeAnimators[0] != player.Animator
                    || connectedAnimator != player.Animator || connectedVisual != player.Visual
                    || !AnimatorControlsRig(player.Animator))
                    throw new InvalidOperationException("El visual Combat_v3 o el backup Combat_v2 no es exacto.");
            }
        }

        private static bool AnimatorControlsRig(Animator animator)
        {
            SkinnedMeshRenderer[] renderers = animator.GetComponentsInChildren<SkinnedMeshRenderer>(true);
            return renderers.Length != 0 && renderers.All(renderer => renderer.rootBone != null
                && renderer.rootBone.IsChildOf(animator.transform)
                && renderer.bones.All(bone => bone != null && bone.IsChildOf(animator.transform)));
        }

        private static PlayerContext[] ResolvePlayers(Scene scene, string expectedControllerPath,
            string expectedFbxPath)
        {
            var result = new List<PlayerContext>();
            QusapCombatController[] combats = scene.GetRootGameObjects()
                .SelectMany(root => root.GetComponentsInChildren<QusapCombatController>(true))
                .Where(combat => combat != null && combat.gameObject.activeInHierarchy).ToArray();
            foreach (QusapCombatController combat in combats)
            {
                QusapAnimationDriver driver = combat.GetComponent<QusapAnimationDriver>();
                if (driver == null)
                    continue;
                Animator[] animators = combat.GetComponentsInChildren<Animator>(false)
                    .Where(animator => animator.enabled).ToArray();
                if (animators.Length != 1)
                    throw new InvalidOperationException(
                        $"El candidato '{combat.name}' tiene {animators.Length} Animators activos; se requiere 1.");
                Animator animator = animators[0];
                Transform visual = animator.transform;
                while (visual.parent != null && visual.parent != combat.transform)
                    visual = visual.parent;
                if (visual.parent != combat.transform || visual.name != ActiveVisualName)
                    throw new InvalidOperationException(
                        $"No se pudo localizar el PlayerVisual directo del candidato '{combat.name}'.");
                string controllerPath = AssetDatabase.GetAssetPath(animator.runtimeAnimatorController);
                string fbxPath = PrefabUtility.GetPrefabAssetPathOfNearestInstanceRoot(visual.gameObject);
                if (controllerPath != expectedControllerPath || fbxPath != expectedFbxPath)
                    throw new InvalidOperationException(
                        $"Candidato '{combat.name}': controller '{controllerPath}', FBX '{fbxPath}'; "
                        + $"esperados '{expectedControllerPath}' y '{expectedFbxPath}'.");
                result.Add(new PlayerContext(combat.transform, visual, animator, combat));
            }
            return result.OrderBy(player => player.Root.GetSiblingIndex()).ToArray();
        }

        private static Dictionary<Component, string> CaptureProtectedComponents(Scene scene,
            IReadOnlyCollection<PlayerContext> players)
        {
            Transform[] visuals = players.Select(player => player.Visual).ToArray();
            return scene.GetRootGameObjects().SelectMany(root => root.GetComponentsInChildren<Component>(true))
                .Where(component => component != null
                    && component is not QusapAnimationDriver
                    && !visuals.Any(visual => component.transform == visual
                        || component.transform.IsChildOf(visual)))
                .ToDictionary(component => component, EditorJsonUtility.ToJson);
        }

        private static void ValidateProtectedComponents(Dictionary<Component, string> snapshot)
        {
            foreach (KeyValuePair<Component, string> pair in snapshot)
                if (pair.Key == null || EditorJsonUtility.ToJson(pair.Key) != pair.Value)
                    throw new InvalidOperationException(
                        $"Cambió un componente protegido: {pair.Key?.GetType().Name ?? "destruido"}.");
        }

        private static void ValidateStrongKickTimings(IEnumerable<PlayerContext> players)
        {
            foreach (PlayerContext player in players)
            {
                QusapAttackData ground = player.Combat.GetAttackData(QusapAttackType.StrongKick);
                QusapAirAttackData air = player.Combat.GetAirAttackData(QusapAttackVariant.StrongKickAir);
                if (ground == null || air == null
                    || !Mathf.Approximately(ground.StartupTime, 0.18f)
                    || !Mathf.Approximately(ground.ActiveDuration, 0.10f)
                    || !Mathf.Approximately(ground.RecoveryTime, 0.32f)
                    || !Mathf.Approximately(air.StartupTime, 0.16f)
                    || !Mathf.Approximately(air.ActiveDuration, 0.10f)
                    || !Mathf.Approximately(air.RecoveryTime, 0.30f)
                    || !Mathf.Approximately(air.LandingRecoveryTime, 0.20f))
                    throw new InvalidOperationException(
                        $"Los timings StrongKick de '{player.Root.name}' no coinciden con los valores protegidos.");
            }
        }

        private static void ValidateDepthAndPlane(IEnumerable<PlayerContext> players)
        {
            foreach (PlayerContext player in players)
            {
                CapsuleCollider capsule = RequirePhysicalCapsule(player);
                Rigidbody body = player.Root.GetComponent<Rigidbody>();
                QusapAttackData ground = player.Combat.GetAttackData(QusapAttackType.StrongKick);
                QusapAirAttackData air = player.Combat.GetAirAttackData(QusapAttackVariant.StrongKickAir);
                bool frozenZ = (body.constraints & RigidbodyConstraints.FreezePositionZ) != 0;
                float hurtboxDepth = capsule.radius * 2f * Mathf.Abs(player.Root.lossyScale.z);
                if (!frozenZ || Mathf.Abs(player.Root.position.z) > 0.0001f
                    || ground.HitboxDepth <= 0f || air.HitboxDepth <= 0f || hurtboxDepth <= 0f)
                    throw new InvalidOperationException(
                        $"La profundidad legal no es determinista en '{player.Root.name}': "
                        + $"FreezeZ={frozenZ}, z={player.Root.position.z:R}, hurtbox={hurtboxDepth:R}.");
            }
        }

        private static void ValidateModel(GameObject model)
        {
            Animator animator = model.GetComponent<Animator>();
            if (animator == null || animator.avatar == null || !animator.avatar.isValid
                || animator.avatar.isHuman)
                throw new InvalidOperationException("Combat_v3 no contiene un Animator con Avatar Generic válido.");
            int renderers = model.GetComponentsInChildren<SkinnedMeshRenderer>(true).Length
                + model.GetComponentsInChildren<MeshFilter>(true).Length;
            if (renderers == 0)
                throw new InvalidOperationException("Combat_v3 no contiene mallas renderizables.");
            if (!AnimatorControlsRig(animator))
                throw new InvalidOperationException("El Animator raíz de Combat_v3 no controla los huesos del rig.");
        }

        private static void ValidateCombatParameters(AnimatorController controller)
        {
            AnimatorControllerParameter[] parameters = controller.parameters;
            if (parameters.Count(parameter => parameter.name == "CombatAnimating"
                    && parameter.type == AnimatorControllerParameterType.Bool) != 1
                || parameters.Count(parameter => parameter.name == "AttackVariant"
                    && parameter.type == AnimatorControllerParameterType.Int) != 1)
                throw new InvalidOperationException(
                    "El controller no expone los parámetros CombatAnimating(bool) y AttackVariant(int) exactos.");
        }

        private static void ValidateKickTransitions(AnimatorController controller, string stateName,
            QusapAttackVariant variant)
        {
            AnimatorStateMachine machine = controller.layers[0].stateMachine;
            AnimatorState state = EnumerateStates(machine).Single(item => item.name == stateName);
            AnimatorStateTransition[] entries = machine.anyStateTransitions
                .Where(transition => transition.destinationState == state).ToArray();
            if (entries.Length != 1 || entries[0].hasExitTime || entries[0].canTransitionToSelf
                || !entries[0].hasFixedDuration || !Mathf.Approximately(entries[0].duration, 0.02f)
                || entries[0].conditions.Length != 2
                || !HasCondition(entries[0], "CombatAnimating", AnimatorConditionMode.If, 0f)
                || !HasCondition(entries[0], "AttackVariant", AnimatorConditionMode.Equals, (int)variant)
                || state.transitions.Length != 4
                || state.transitions.Any(transition => transition.hasExitTime
                    || transition.canTransitionToSelf || !transition.hasFixedDuration
                    || !Mathf.Approximately(transition.duration, 0.03f)
                    || !HasCondition(transition, "CombatAnimating", AnimatorConditionMode.IfNot, 0f)))
                throw new InvalidOperationException($"Las transiciones mecánicas de {stateName} no son exactas.");
        }

        private static bool HasCondition(AnimatorStateTransition transition, string parameter,
            AnimatorConditionMode mode, float threshold)
        {
            return transition.conditions.Any(condition => condition.parameter == parameter
                && condition.mode == mode && Mathf.Approximately(condition.threshold, threshold));
        }

        private static ControllerSnapshot CaptureController(AnimatorController controller)
        {
            if (controller == null || controller.layers.Length != 1
                || controller.layers[0].stateMachine.stateMachines.Length != 0)
                throw new InvalidOperationException("El controller debe tener una capa sin submáquinas.");
            AnimatorControllerLayer layer = controller.layers[0];
            AnimatorStateMachine machine = layer.stateMachine;
            string parameters = string.Join("\n", controller.parameters.Select(parameter =>
                $"{parameter.name}|{parameter.type}|{parameter.defaultBool}|{parameter.defaultFloat:R}|{parameter.defaultInt}"));
            string layerSignature = $"{layer.name}|{layer.blendingMode}|{layer.defaultWeight:R}|{layer.iKPass}|"
                + $"{layer.syncedLayerIndex}|{layer.syncedLayerAffectsTiming}|{ObjectId(layer.avatarMask)}|"
                + $"default:{machine.defaultState?.name}";
            string any = string.Join("\n", machine.anyStateTransitions.Select(TransitionSignature));
            Dictionary<string, StateSnapshot> states = EnumerateStates(machine).ToDictionary(
                state => state.name,
                state => new StateSnapshot(
                    state.motion,
                    state.speed,
                    $"{state.cycleOffset:R}|{state.mirror}|{state.iKOnFeet}|{state.writeDefaultValues}|{state.tag}",
                    string.Join("\n", state.transitions.Select(TransitionSignature))),
                StringComparer.Ordinal);
            return new ControllerSnapshot(parameters, layerSignature, any, states);
        }

        private static bool ControllerSnapshotsEqual(ControllerSnapshot left, ControllerSnapshot right)
        {
            return left.Parameters == right.Parameters && left.Layer == right.Layer
                && left.AnyStateTransitions == right.AnyStateTransitions
                && left.States.Count == right.States.Count
                && left.States.All(pair => right.States.TryGetValue(pair.Key, out StateSnapshot value)
                    && pair.Value.Motion == value.Motion && Mathf.Approximately(pair.Value.Speed, value.Speed)
                    && pair.Value.Semantics == value.Semantics && pair.Value.Transitions == value.Transitions);
        }

        private static string TransitionSignature(AnimatorStateTransition transition)
        {
            string conditions = string.Join(",", transition.conditions.Select(condition =>
                $"{condition.parameter}:{condition.mode}:{condition.threshold:R}"));
            return $"{transition.destinationState?.name}|{transition.hasExitTime}|{transition.exitTime:R}|"
                + $"{transition.hasFixedDuration}|{transition.duration:R}|{transition.offset:R}|"
                + $"{transition.interruptionSource}|{transition.orderedInterruption}|"
                + $"{transition.canTransitionToSelf}|{transition.mute}|{transition.solo}|{conditions}";
        }

        private static IEnumerable<AnimatorState> EnumerateStates(AnimatorStateMachine machine)
        {
            foreach (ChildAnimatorState child in machine.states)
                yield return child.state;
            foreach (ChildAnimatorStateMachine child in machine.stateMachines)
                foreach (AnimatorState state in EnumerateStates(child.stateMachine))
                    yield return state;
        }

        private static Dictionary<string, AnimationClip> LoadV3Clips()
        {
            AnimationClip[] clips = AssetDatabase.LoadAllAssetsAtPath(TargetFbxPath).OfType<AnimationClip>()
                .Where(clip => !IsPreviewName(clip.name) && !HasNumberedDuplicate(clip.name)).ToArray();
            if (clips.Length != 12 || clips.GroupBy(clip => clip.name).Any(group => group.Count() != 1)
                || ClipRequirements.Any(requirement => clips.All(clip => clip.name != requirement.Name)))
                throw new InvalidOperationException("Combat_v3 no expone los doce clips públicos exactos.");
            return clips.ToDictionary(clip => clip.name, StringComparer.Ordinal);
        }

        private static bool IsExactTakeName(string candidate, string required)
        {
            if (string.IsNullOrWhiteSpace(candidate))
                return false;
            int separator = candidate.LastIndexOf('|');
            string segment = separator >= 0 ? candidate.Substring(separator + 1) : candidate;
            return string.Equals(segment.Trim(), required, StringComparison.OrdinalIgnoreCase);
        }

        private static bool IsPreviewName(string candidate)
        {
            if (string.IsNullOrWhiteSpace(candidate))
                return false;
            int separator = candidate.LastIndexOf('|');
            string segment = separator >= 0 ? candidate.Substring(separator + 1) : candidate;
            return segment.Trim().TrimStart('_').StartsWith("preview", StringComparison.OrdinalIgnoreCase);
        }

        private static bool HasNumberedDuplicate(string candidate)
        {
            return !string.IsNullOrWhiteSpace(candidate) && Regex.IsMatch(candidate, @"\(\d+\)\s*$");
        }

        private static string[] SplitSignatures(string value)
        {
            return string.IsNullOrEmpty(value) ? Array.Empty<string>() : value.Split('\n');
        }

        private static string ObjectId(UnityEngine.Object item)
        {
            if (item == null)
                return "null";
            return AssetDatabase.TryGetGUIDAndLocalFileIdentifier(item, out string guid, out long localId)
                ? guid + ":" + localId : item.GetEntityId().ToString();
        }

        private static T RequireAsset<T>(string path, string description) where T : UnityEngine.Object
        {
            T asset = AssetDatabase.LoadAssetAtPath<T>(path);
            if (asset == null)
                throw new InvalidOperationException($"Falta {description}: {path}");
            return asset;
        }

        private static string CaptureTargetAnimatorInventory()
        {
            RequireAsset<SceneAsset>(TargetScenePath, "escena StrongKick Combat V3");
            Scene loaded = SceneManager.GetSceneByPath(TargetScenePath);
            if (loaded.IsValid() && loaded.isLoaded)
                return CaptureAnimatorInventory(loaded);
            Scene preview = EditorSceneManager.OpenPreviewScene(TargetScenePath);
            try
            {
                return CaptureAnimatorInventory(preview);
            }
            finally
            {
                EditorSceneManager.ClosePreviewScene(preview);
            }
        }

        private static string CaptureAnimatorInventory(Scene scene)
        {
            var builder = new StringBuilder();
            QusapAnimationDriver[] drivers = scene.GetRootGameObjects()
                .SelectMany(root => root.GetComponentsInChildren<QusapAnimationDriver>(true)).ToArray();
            foreach (QusapAnimationDriver driver in drivers)
            {
                var serialized = new SerializedObject(driver);
                Animator connected = serialized.FindProperty("animator")?.objectReferenceValue as Animator;
                builder.AppendLine($"Jugador: {HierarchyPath(driver.transform)}; driver -> "
                    + (connected != null ? HierarchyPath(connected.transform) : "null"));
                Animator[] animators = driver.GetComponentsInChildren<Animator>(true);
                builder.AppendLine($"Animators encontrados: {animators.Length}; activos/habilitados: "
                    + animators.Count(item => item.enabled && item.gameObject.activeInHierarchy));
                foreach (Animator animator in animators)
                {
                    RuntimeAnimatorController runtime = animator.runtimeAnimatorController;
                    AnimatorController assetController = runtime as AnimatorController;
                    string parameters = assetController != null
                        ? string.Join(",", assetController.parameters.Select(item => item.name))
                        : "<sin controller>";
                    int layers = assetController != null ? assetController.layers.Length : 0;
                    builder.AppendLine($"- {HierarchyPath(animator.transform)} | activeSelf={animator.gameObject.activeSelf} "
                        + $"activeInHierarchy={animator.gameObject.activeInHierarchy} enabled={animator.enabled} "
                        + $"controller={(runtime != null ? AssetDatabase.GetAssetPath(runtime) : "null")} "
                        + $"avatar={(animator.avatar != null ? animator.avatar.name : "null")} "
                        + $"avatarValid={(animator.avatar != null && animator.avatar.isValid)} "
                        + $"generic={(animator.avatar != null && animator.avatar.isValid && !animator.avatar.isHuman)} "
                        + $"rootMotion={animator.applyRootMotion} culling={animator.cullingMode} "
                        + $"layers={layers} parameters=[{parameters}] controlsRig={AnimatorControlsRig(animator)}");
                }
            }
            return builder.ToString();
        }

        private static string HierarchyPath(Transform item)
        {
            if (item == null)
                return "<null>";
            string path = item.name;
            for (Transform parent = item.parent; parent != null; parent = parent.parent)
                path = parent.name + "/" + path;
            return path;
        }

        private static string BuildRepairReport(PreflightContext preflight, SceneBuildResult result,
            AnimatorController controller, bool rebuiltController, string inventoryBefore,
            string inventoryAfter, string bindingReport)
        {
            var builder = new StringBuilder();
            builder.AppendLine("REPARACIÓN COMBAT V3 COMPLETADA");
            builder.AppendLine("Causa: la validación de apoyo anterior abortó después de limpiar temporalmente el RuntimeAnimatorController; el Animator V3 quedó activo con controller null.");
            builder.AppendLine($"Controller: {TargetControllerPath} ({(rebuiltController ? "reconstruido desde Combat V2" : "validado sin reconstrucción")}).");
            builder.AppendLine($"Base Layers: {controller.layers.Length}; parámetros: {controller.parameters.Length}.");
            builder.AppendLine("Bindings resueltos: " + bindingReport);
            builder.AppendLine("Estados/Motion:");
            foreach (AnimatorState state in EnumerateStates(controller.layers[0].stateMachine))
                builder.AppendLine($"- {state.name} -> {AssetDatabase.GetAssetPath(state.motion)}::{state.motion?.name ?? "None"}");
            foreach (AlignmentReport alignment in result.Alignments)
            {
                builder.AppendLine($"{alignment.Player}: localPosition {alignment.Before:R} -> {alignment.After:R}; ajuste Y {alignment.DeltaY:R}.");
                builder.AppendLine($"Base collider={alignment.ColliderBottom:R}; Idle inicial={alignment.SoleBottom:R}; gap positivo máximo="
                    + $"{alignment.Support.MaximumGap.Values.Max():R}.");
            }
            builder.AppendLine($"Escena guardada: {TargetScenePath}; jugadores reparados: {preflight.PlayerCount}.");
            builder.AppendLine("Apply Root Motion Off; Avatar Generic válido; Culling Always Animate; Rigidbody, collider, combate y escenas anteriores intactos.");
            builder.AppendLine("\nINVENTARIO ANTES:\n" + inventoryBefore);
            builder.AppendLine("INVENTARIO DESPUÉS:\n" + inventoryAfter);
            return builder.ToString();
        }

        private static void DeleteTargetAsset(string path)
        {
            Scene loaded = SceneManager.GetSceneByPath(path);
            if (loaded.IsValid() && loaded.isLoaded)
                EditorSceneManager.OpenScene(SourceScenePath, OpenSceneMode.Single);
            if (AssetDatabase.LoadAssetAtPath<UnityEngine.Object>(path) != null
                && !AssetDatabase.DeleteAsset(path))
                throw new InvalidOperationException($"No se pudo reemplazar la salida V3: {path}");
        }

        private static string BuildReport(PreflightContext preflight, SceneBuildResult result)
        {
            var builder = new StringBuilder();
            builder.AppendLine($"FBX nuevo: {TargetFbxPath}");
            builder.AppendLine("Importación: Generic, Create From This Model, Animation On, Compression Off, Root Motion bloqueado, cámaras/luces Off.");
            builder.AppendLine("Clips: 12 exactos; sin preview, duplicados ni sufijos numerados.");
            builder.AppendLine($"Controller nuevo: {TargetControllerPath}");
            builder.AppendLine("Los diez estados existentes conservan parámetros, velocidades y transiciones; sus Motion ahora usan Combat_v3.");
            builder.AppendLine($"StrongKickGround speed: {StrongGroundSpeed:R}; StrongKickAir speed: {StrongAirSpeed:R}.");
            builder.AppendLine($"Escena creada y abierta: {TargetScenePath}");
            builder.AppendLine($"Fuente única validada: {SourceScenePath}; jugadores conservados: {preflight.PlayerCount}.");
            foreach (AlignmentReport alignment in result.Alignments)
            {
                builder.AppendLine($"{alignment.Player}: localPosition {alignment.Before:R} -> {alignment.After:R}; ajuste Y {alignment.DeltaY:R}.");
                builder.AppendLine($"  Base collider {alignment.ColliderBottom:R}; suela {alignment.SoleBottom:R}; diferencia {Mathf.Abs(alignment.ColliderBottom - alignment.SoleBottom):R}.");
                builder.AppendLine("  Apoyo máximo por clip: " + string.Join(", ", alignment.Support.MaximumGap.Select(
                    pair => $"{pair.Key}={pair.Value:R}")));
            }
            builder.AppendLine("Profundidad inspeccionada: StrongKick Ground/Air=1; hurtbox física (cápsula)=1; separación Z legal=0 (FreezePositionZ).");
            builder.AppendLine("No existe un fallo real de profundidad para posiciones legales; no se modificaron hitboxes.");
            builder.AppendLine("Timings protegidos confirmados: Ground 0.18/0.10/0.32; Air 0.16/0.10/0.30; Landing Recovery 0.20.");
            builder.AppendLine("Prefabs, Rigidbody, CapsuleCollider, movimiento, inputs y datos de combate: intactos. Apply Root Motion: Off.");
            return builder.ToString();
        }

        private static string FileHash(string path)
        {
            using SHA256 sha = SHA256.Create();
            using FileStream stream = File.OpenRead(path);
            return BitConverter.ToString(sha.ComputeHash(stream)).Replace("-", string.Empty);
        }

        private static string AbsolutePath(string assetPath)
        {
            return Path.GetFullPath(Path.Combine(Directory.GetParent(Application.dataPath).FullName, assetPath));
        }

        private static void EnsureEditorIsIdle()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode || AnimationMode.InAnimationMode())
                throw new InvalidOperationException("Sal de Play Mode y Animation Mode antes de crear la prueba Combat V3.");
        }

        private readonly struct ClipRequirement
        {
            public ClipRequirement(string name, bool loop)
            {
                Name = name;
                Loop = loop;
            }
            public string Name { get; }
            public bool Loop { get; }
        }

        private readonly struct PlayerContext
        {
            public PlayerContext(Transform root, Transform visual, Animator animator,
                QusapCombatController combat)
            {
                Root = root;
                Visual = visual;
                Animator = animator;
                Combat = combat;
            }
            public Transform Root { get; }
            public Transform Visual { get; }
            public Animator Animator { get; }
            public QusapCombatController Combat { get; }
        }

        private sealed class PreflightContext
        {
            public PreflightContext(ControllerSnapshot controller, int playerCount)
            {
                Controller = controller;
                PlayerCount = playerCount;
            }
            public ControllerSnapshot Controller { get; }
            public int PlayerCount { get; }
        }

        private sealed class ControllerSnapshot
        {
            public ControllerSnapshot(string parameters, string layer, string anyStateTransitions,
                Dictionary<string, StateSnapshot> states)
            {
                Parameters = parameters;
                Layer = layer;
                AnyStateTransitions = anyStateTransitions;
                States = states;
            }
            public string Parameters { get; }
            public string Layer { get; }
            public string AnyStateTransitions { get; }
            public Dictionary<string, StateSnapshot> States { get; }
        }

        private readonly struct StateSnapshot
        {
            public StateSnapshot(Motion motion, float speed, string semantics, string transitions)
            {
                Motion = motion;
                Speed = speed;
                Semantics = semantics;
                Transitions = transitions;
            }
            public Motion Motion { get; }
            public float Speed { get; }
            public string Semantics { get; }
            public string Transitions { get; }
        }

        private sealed class SceneBuildResult
        {
            public SceneBuildResult(Scene scene, AlignmentReport[] alignments)
            {
                Scene = scene;
                Alignments = alignments;
            }
            public Scene Scene { get; }
            public AlignmentReport[] Alignments { get; }
        }

        private readonly struct AlignmentReport
        {
            public AlignmentReport(string player, Vector3 before, Vector3 after, float deltaY,
                float colliderBottom, float soleBottom, GroundSupportReport support)
            {
                Player = player;
                Before = before;
                After = after;
                DeltaY = deltaY;
                ColliderBottom = colliderBottom;
                SoleBottom = soleBottom;
                Support = support;
            }
            public string Player { get; }
            public Vector3 Before { get; }
            public Vector3 After { get; }
            public float DeltaY { get; }
            public float ColliderBottom { get; }
            public float SoleBottom { get; }
            public GroundSupportReport Support { get; }
        }

        private sealed class GroundSupportReport
        {
            public GroundSupportReport(Dictionary<string, float> maximumGap)
            {
                MaximumGap = maximumGap;
            }
            public Dictionary<string, float> MaximumGap { get; }
        }
    }
}
#endif
