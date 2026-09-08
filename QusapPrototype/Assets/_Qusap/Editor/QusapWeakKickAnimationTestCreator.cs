#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text.RegularExpressions;
using Qusap;
using UnityEditor;
using UnityEditor.Animations;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Qusap.EditorTools
{
    public static class QusapWeakKickAnimationTestCreator
    {
        private const string ArtFolder = "Assets/Scenes/Art/Characters/Qusap/Final_Light_v3/";
        private const string FbxPath = ArtFolder + "Qusap_Luz_Combat_v1.fbx";
        private const string ReferenceFbxPath = ArtFolder + "Qusap_Luz_Locomotion_v12.fbx";
        private const string SourceControllerPath = ArtFolder + "Qusap_Luz_Animator_DashTest_v1.controller";
        private const string TargetControllerPath = ArtFolder + "Qusap_Luz_Animator_WeakKickTest_v1.controller";
        private const string SourceScenePath = "Assets/_Qusap/Scenes/Qusap_AirCombatMechanicsTest_v1.unity";
        private const string TargetScenePath = "Assets/_Qusap/Scenes/Qusap_WeakKickAnimationTest_v1.unity";
        private const string ExternalFbxPath =
            @"C:\Users\victo\Documents\Qusap\3D\Modelado\Qusap_Luz_Definitivo\03_Export_Unity\Qusap_Luz_Combat_v1.fbx";
        private const string ActiveVisualName = "PlayerVisual";
        private const string BackupVisualName = "PlayerVisual_LocomotionV12_Backup";
        private const string PreviousVisualBackupName = "PlayerVisual_PreCombat_Backup";
        private const float FrameTolerance = 0.01f;
        private const float SoleTolerance = 0.01f;
        private const float GroundKickSpeed = (19f / 60f) / 0.30f;
        private const float AirKickSpeed = (16f / 60f) / 0.26f;
        private const float GroundClawReach = 0.77f;
        private const float AirClawReach = 0.67f;
        private static readonly Vector2 GroundKickSize = new(0.72f, 0.60f);
        private static readonly Vector2 GroundKickOffset = new(0.56f, -0.35f);
        private static readonly Vector2 AirKickSize = new(0.64f, 0.55f);
        private static readonly Vector2 AirKickOffset = new(0.50f, 0f);

        private static readonly ClipRequirement[] ClipRequirements =
        {
            new("Qusap_Idle", 120f, true),
            new("Qusap_Run", 36f, true),
            new("Qusap_JumpRise", 18f, false),
            new("Qusap_Fall", 24f, true),
            new("Qusap_Land", 18f, false),
            new("Qusap_WallSlide", 48f, true),
            new("Qusap_WallJump", 18f, false),
            new("Qusap_Dash", 10f, false),
            new("Qusap_WeakKickGround", 19f, false),
            new("Qusap_WeakKickAir", 16f, false)
        };

        [MenuItem("Tools/Qusap/Create Weak Kick Animation Test")]
        private static void CreateWeakKickAnimationTest()
        {
            try
            {
                EnsureEditorIsIdle();
                if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo())
                    return;
                if (!ConfirmReplacement())
                    return;

                RequireAsset<SceneAsset>(SourceScenePath, "escena aérea base");
                RequireAsset<AnimatorController>(SourceControllerPath, "controller Dash base");
                RequireAsset<GameObject>(ReferenceFbxPath, "FBX v12 de referencia");
                string sourceSceneHash = FileHash(AbsolutePath(SourceScenePath));
                string sourceControllerHash = FileHash(AbsolutePath(SourceControllerPath));
                string referenceFbxHash = FileHash(AbsolutePath(ReferenceFbxPath));

                EnsureExactFbxCopy();
                Dictionary<string, AnimationClip> clips = ConfigureImporter();
                GameObject model = RequireAsset<GameObject>(FbxPath, "FBX Combat v1");
                ValidateModel(model);
                AnimatorController controller = CreateController(clips);
                Scene scene = CreateScene(model, controller, clips);

                if (FileHash(AbsolutePath(SourceScenePath)) != sourceSceneHash
                    || FileHash(AbsolutePath(SourceControllerPath)) != sourceControllerHash
                    || FileHash(AbsolutePath(ReferenceFbxPath)) != referenceFbxHash)
                    throw new InvalidOperationException("Un asset original cambió durante la creación; revisa la consola.");

                EditorSceneManager.SaveScene(scene, TargetScenePath);
                Selection.activeObject = AssetDatabase.LoadAssetAtPath<SceneAsset>(TargetScenePath);
                EditorUtility.DisplayDialog("Qusap Weak Kick Animation Test", BuildReport(), "Aceptar");
            }
            catch (Exception exception)
            {
                Debug.LogException(exception);
                EditorUtility.DisplayDialog(
                    "Qusap Weak Kick Animation Test",
                    "No se completó la copia. Los assets originales no se reemplazaron.\n\n" + exception.Message,
                    "Aceptar");
            }
        }

        private static bool ConfirmReplacement()
        {
            bool exists = AssetDatabase.LoadAssetAtPath<UnityEngine.Object>(TargetScenePath) != null
                || AssetDatabase.LoadAssetAtPath<UnityEngine.Object>(TargetControllerPath) != null;
            if (!exists)
                return true;
            return EditorUtility.DisplayDialog(
                "Qusap Weak Kick Animation Test",
                "Ya existe una copia de esta prueba. ¿Deseas reemplazar únicamente la escena y el controller WeakKick?",
                "Reemplazar copia", "Cancelar");
        }

        private static void EnsureExactFbxCopy()
        {
            if (!File.Exists(ExternalFbxPath))
                throw new FileNotFoundException("No existe el FBX Combat v1 externo.", ExternalFbxPath);
            string destination = AbsolutePath(FbxPath);
            if (!File.Exists(destination))
            {
                if (File.Exists(destination + ".meta"))
                    throw new InvalidOperationException("Existe un .meta huérfano para Combat v1.");
                File.Copy(ExternalFbxPath, destination, false);
            }
            if (FileHash(ExternalFbxPath) != FileHash(destination))
                throw new InvalidOperationException("El FBX de destino no es una copia binaria exacta del origen.");
            AssetDatabase.ImportAsset(FbxPath, ImportAssetOptions.ForceSynchronousImport);
        }

        private static Dictionary<string, AnimationClip> ConfigureImporter()
        {
            var importer = AssetImporter.GetAtPath(FbxPath) as ModelImporter;
            var reference = AssetImporter.GetAtPath(ReferenceFbxPath) as ModelImporter;
            if (importer == null || reference == null)
                throw new InvalidOperationException("No se pudieron resolver los ModelImporter requeridos.");

            CopyImporterSettings(reference, importer);
            ModelImporterClipAnimation[] sourceTakes = importer.defaultClipAnimations;
            if (sourceTakes == null || sourceTakes.Length == 0)
                throw new InvalidOperationException("Combat v1 no contiene Source Takes reales.");
            foreach (ModelImporterClipAnimation take in sourceTakes.Where(item => item != null))
                Debug.Log($"Combat v1 Source Take: {take.name} | {take.takeName} | {take.firstFrame:R}..{take.lastFrame:R}");

            ModelImporterClipAnimation[] candidates = sourceTakes.Where(take => take != null
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
                    throw new InvalidOperationException(
                        $"Se esperaba un Source Take único '{requirement.Name}'; encontrados: {matches.Length}.");
                ModelImporterClipAnimation source = matches[0];
                float frames = source.lastFrame - source.firstFrame;
                if (Mathf.Abs(frames - requirement.Frames) > FrameTolerance)
                    throw new InvalidOperationException(
                        $"{requirement.Name}: {source.firstFrame:R}..{source.lastFrame:R} ({frames:R} frames), " +
                        $"esperados {requirement.Frames:R}.");
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

            importer = AssetImporter.GetAtPath(FbxPath) as ModelImporter;
            Dictionary<string, AnimationClip> clips = LoadClips();
            if (importer == null || importer.animationType != ModelImporterAnimationType.Generic
                || importer.avatarSetup != ModelImporterAvatarSetup.CreateFromThisModel
                || !importer.importAnimation || importer.animationCompression != ModelImporterAnimationCompression.Off
                || importer.motionNodeName != string.Empty || clips.Count != 10 || importer.clipAnimations.Length != 10
                || !ImporterSettingsMatch(reference, importer))
                throw new InvalidOperationException("La reimportación no produjo los diez clips Generic requeridos.");
            foreach (ClipRequirement requirement in ClipRequirements)
            {
                ModelImporterClipAnimation definition = importer.clipAnimations.SingleOrDefault(
                    item => item.name == requirement.Name);
                AnimationClip clip = clips[requirement.Name];
                if (definition == null || definition.loopTime != requirement.Loop
                    || definition.loopPose != requirement.Loop || !definition.lockRootRotation
                    || !definition.lockRootHeightY || !definition.lockRootPositionXZ
                    || !definition.keepOriginalOrientation || !definition.keepOriginalPositionY
                    || !definition.keepOriginalPositionXZ
                    || Mathf.Abs(clip.length * clip.frameRate - requirement.Frames) > FrameTolerance)
                    throw new InvalidOperationException($"El clip {requirement.Name} no conserva su rango/loop/root lock.");
            }
            return clips;
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

        private static AnimatorController CreateController(IReadOnlyDictionary<string, AnimationClip> clips)
        {
            if (AssetDatabase.LoadAssetAtPath<UnityEngine.Object>(TargetControllerPath) != null)
                AssetDatabase.DeleteAsset(TargetControllerPath);
            if (!AssetDatabase.CopyAsset(SourceControllerPath, TargetControllerPath))
                throw new InvalidOperationException("Unity no pudo copiar el controller Dash base.");
            var controller = RequireAsset<AnimatorController>(TargetControllerPath, "controller WeakKick");
            AnimatorStateMachine machine = controller.layers[0].stateMachine;
            Dictionary<string, AnimatorState> states = machine.states.Select(item => item.state)
                .ToDictionary(state => state.name, StringComparer.Ordinal);
            foreach (string name in ClipRequirements.Take(8).Select(item => item.Name))
            {
                if (!states.TryGetValue(name, out AnimatorState state))
                    throw new InvalidOperationException($"El controller Dash no contiene {name}.");
                state.motion = clips[name];
                EditorUtility.SetDirty(state);
            }
            if (controller.parameters.Any(parameter => parameter.name == "CombatAnimating"
                || parameter.name == "AttackVariant"))
                throw new InvalidOperationException("El controller Dash base ya contiene parámetros de combate inesperados.");
            controller.AddParameter("CombatAnimating", AnimatorControllerParameterType.Bool);
            controller.AddParameter("AttackVariant", AnimatorControllerParameterType.Int);

            foreach (AnimatorStateTransition transition in machine.anyStateTransitions
                .Concat(states.Values.SelectMany(state => state.transitions)))
            {
                transition.AddCondition(AnimatorConditionMode.IfNot, 0f, "CombatAnimating");
                EditorUtility.SetDirty(transition);
            }

            AnimatorState ground = AddKickState(machine, "Qusap_WeakKickGround",
                clips["Qusap_WeakKickGround"], GroundKickSpeed, new Vector3(1260f, -10f, 0f));
            AnimatorState air = AddKickState(machine, "Qusap_WeakKickAir",
                clips["Qusap_WeakKickAir"], AirKickSpeed, new Vector3(1260f, 100f, 0f));
            AddKickEntry(machine, ground, QusapAttackVariant.WeakKickGround);
            AddKickEntry(machine, air, QusapAttackVariant.WeakKickAir);
            AddKickExits(ground, states);
            AddKickExits(air, states);
            EditorUtility.SetDirty(machine);
            EditorUtility.SetDirty(controller);
            AssetDatabase.SaveAssetIfDirty(controller);
            ValidateController(controller, clips);
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

        private static void AddKickExits(AnimatorState kick, IReadOnlyDictionary<string, AnimatorState> states)
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

        private static void ValidateController(AnimatorController controller,
            IReadOnlyDictionary<string, AnimationClip> clips)
        {
            AnimatorStateMachine machine = controller.layers[0].stateMachine;
            AnimatorState[] allStates = machine.states.Select(item => item.state).ToArray();
            if (controller.layers.Length != 1 || machine.stateMachines.Length != 0 || allStates.Length != 10
                || ClipRequirements.Any(requirement => allStates.Count(state => state.name == requirement.Name) != 1))
                throw new InvalidOperationException("El controller no contiene los diez estados exactos.");
            Dictionary<string, AnimatorControllerParameter> parameters = controller.parameters
                .ToDictionary(parameter => parameter.name, StringComparer.Ordinal);
            if (parameters.Count != 8
                || parameters["CombatAnimating"].type != AnimatorControllerParameterType.Bool
                || parameters["AttackVariant"].type != AnimatorControllerParameterType.Int)
                throw new InvalidOperationException("Los parámetros del controller WeakKick no son exactos.");

            foreach (ClipRequirement requirement in ClipRequirements)
                if (allStates.Single(state => state.name == requirement.Name).motion != clips[requirement.Name])
                    throw new InvalidOperationException($"Motion incorrecto en {requirement.Name}.");
            AnimatorState ground = allStates.Single(state => state.name == "Qusap_WeakKickGround");
            AnimatorState air = allStates.Single(state => state.name == "Qusap_WeakKickAir");
            if (!Mathf.Approximately(ground.speed, GroundKickSpeed)
                || !Mathf.Approximately(air.speed, AirKickSpeed))
                throw new InvalidOperationException("Las velocidades WeakKick no coinciden con los timings mecánicos.");
            ValidateKickTransitions(machine, ground, QusapAttackVariant.WeakKickGround);
            ValidateKickTransitions(machine, air, QusapAttackVariant.WeakKickAir);

            foreach (AnimatorStateTransition transition in machine.anyStateTransitions
                .Concat(allStates.Where(state => state != ground && state != air)
                    .SelectMany(state => state.transitions)))
            {
                if (transition.destinationState != ground && transition.destinationState != air
                    && !HasCondition(transition, "CombatAnimating", AnimatorConditionMode.IfNot, 0f))
                    throw new InvalidOperationException("Una transición general podría interrumpir WeakKick.");
            }
        }

        private static void ValidateKickTransitions(AnimatorStateMachine machine, AnimatorState kick,
            QusapAttackVariant variant)
        {
            AnimatorStateTransition[] entries = machine.anyStateTransitions
                .Where(transition => transition.destinationState == kick).ToArray();
            if (entries.Length != 1 || entries[0].hasExitTime || entries[0].canTransitionToSelf
                || !entries[0].hasFixedDuration || !Mathf.Approximately(entries[0].duration, 0.02f)
                || entries[0].conditions.Length != 2
                || !HasCondition(entries[0], "CombatAnimating", AnimatorConditionMode.If, 0f)
                || !HasCondition(entries[0], "AttackVariant", AnimatorConditionMode.Equals, (int)variant))
                throw new InvalidOperationException($"Any State -> {kick.name} no es exacta.");
            if (kick.transitions.Length != 4 || kick.transitions.Any(transition => transition.hasExitTime
                || transition.canTransitionToSelf || !transition.hasFixedDuration
                || !Mathf.Approximately(transition.duration, 0.03f)
                || !HasCondition(transition, "CombatAnimating", AnimatorConditionMode.IfNot, 0f)))
                throw new InvalidOperationException($"Las salidas de {kick.name} no son exactas.");
        }

        private static Scene CreateScene(GameObject model, AnimatorController controller,
            IReadOnlyDictionary<string, AnimationClip> clips)
        {
            if (AssetDatabase.LoadAssetAtPath<UnityEngine.Object>(TargetScenePath) != null)
            {
                Scene loaded = SceneManager.GetSceneByPath(TargetScenePath);
                if (loaded.IsValid() && loaded.isLoaded)
                    EditorSceneManager.OpenScene(SourceScenePath, OpenSceneMode.Single);
                AssetDatabase.DeleteAsset(TargetScenePath);
            }
            if (!AssetDatabase.CopyAsset(SourceScenePath, TargetScenePath))
                throw new InvalidOperationException("Unity no pudo copiar la escena aérea base.");
            Scene scene = EditorSceneManager.OpenScene(TargetScenePath, OpenSceneMode.Single);
            QusapCombatController[] players = FindPlayers(scene);
            if (players.Length != 2)
                throw new InvalidOperationException($"Se esperaban dos jugadores; encontrados: {players.Length}.");
            Dictionary<Component, string> protectedComponents = CaptureProtectedComponents(scene, players);

            foreach (QusapCombatController player in players)
            {
                InstallVisual(player, model, controller, clips);
                TuneTestOnlyCombatData(player);
            }
            PlacePlayers(players);
            ValidateProtectedComponents(protectedComponents);
            GameObject hudObject = new("WeakKickAnimationDebugHUD");
            SceneManager.MoveGameObjectToScene(hudObject, scene);
            hudObject.AddComponent<QusapWeakKickAnimationDebugHud>().Configure(players);
            EditorSceneManager.MarkSceneDirty(scene);
            return scene;
        }

        private static void InstallVisual(QusapCombatController player, GameObject model,
            AnimatorController controller, IReadOnlyDictionary<string, AnimationClip> clips)
        {
            Transform oldVisual = player.transform.Find(ActiveVisualName);
            if (oldVisual == null || !oldVisual.gameObject.activeInHierarchy)
                throw new InvalidOperationException($"{player.name} no contiene PlayerVisual activo.");
            Vector3 position = oldVisual.localPosition;
            Quaternion rotation = oldVisual.localRotation;
            Vector3 scale = oldVisual.localScale;
            AnimationClip oldIdle = AssetDatabase.LoadAllAssetsAtPath(ReferenceFbxPath)
                .OfType<AnimationClip>().Single(clip => clip.name == "Qusap_Idle");
            Transform v12Backup;
            if (PrefabUtility.GetPrefabAssetPathOfNearestInstanceRoot(oldVisual.gameObject) == ReferenceFbxPath)
            {
                v12Backup = oldVisual;
                v12Backup.name = BackupVisualName;
            }
            else
            {
                oldVisual.name = PreviousVisualBackupName;
                oldVisual.gameObject.SetActive(false);
                GameObject referenceModel = RequireAsset<GameObject>(ReferenceFbxPath, "FBX v12 de referencia");
                GameObject referenceVisual = PrefabUtility.InstantiatePrefab(referenceModel, player.transform)
                    as GameObject;
                if (referenceVisual == null)
                    throw new InvalidOperationException("Unity no pudo instanciar el respaldo v12.");
                referenceVisual.name = BackupVisualName;
                referenceVisual.transform.localPosition = position;
                referenceVisual.transform.localRotation = rotation;
                referenceVisual.transform.localScale = scale;
                Animator referenceAnimator = referenceVisual.GetComponent<Animator>()
                    ?? referenceVisual.AddComponent<Animator>();
                referenceAnimator.runtimeAnimatorController = RequireAsset<AnimatorController>(
                    SourceControllerPath, "controller Dash base");
                referenceAnimator.applyRootMotion = false;
                PrefabUtility.RecordPrefabInstancePropertyModifications(referenceVisual);
                PrefabUtility.RecordPrefabInstancePropertyModifications(referenceVisual.transform);
                PrefabUtility.RecordPrefabInstancePropertyModifications(referenceAnimator);
                v12Backup = referenceVisual.transform;
            }
            v12Backup.gameObject.SetActive(true);
            Vector2 oldSoles = MeasureSoles(v12Backup, oldIdle);
            v12Backup.gameObject.SetActive(false);

            GameObject visual = PrefabUtility.InstantiatePrefab(model, player.transform) as GameObject;
            if (visual == null)
                throw new InvalidOperationException("Unity no pudo instanciar Combat v1.");
            visual.name = ActiveVisualName;
            visual.transform.localPosition = position;
            visual.transform.localRotation = rotation;
            visual.transform.localScale = scale;
            Animator animator = visual.GetComponent<Animator>() ?? visual.AddComponent<Animator>();
            animator.runtimeAnimatorController = controller;
            animator.applyRootMotion = false;
            animator.cullingMode = AnimatorCullingMode.AlwaysAnimate;
            ValidateInPlaceRoot(visual.transform, clips);
            Vector2 newSoles = MeasureSoles(visual.transform, clips["Qusap_Idle"]);
            if (Mathf.Max(Mathf.Abs(oldSoles.x - newSoles.x), Mathf.Abs(oldSoles.y - newSoles.y)) > SoleTolerance
                || visual.transform.localPosition != position || visual.transform.localRotation != rotation
                || visual.transform.localScale != scale || animator.applyRootMotion)
                throw new InvalidOperationException($"{player.name}: Combat v1 no conservó transform/suelas/root motion.");
            PrefabUtility.RecordPrefabInstancePropertyModifications(oldVisual.gameObject);
            PrefabUtility.RecordPrefabInstancePropertyModifications(v12Backup.gameObject);
            PrefabUtility.RecordPrefabInstancePropertyModifications(v12Backup);
            PrefabUtility.RecordPrefabInstancePropertyModifications(visual);
            PrefabUtility.RecordPrefabInstancePropertyModifications(visual.transform);
            PrefabUtility.RecordPrefabInstancePropertyModifications(animator);
        }

        private static void TuneTestOnlyCombatData(QusapCombatController player)
        {
            string strongBefore = EditorJsonUtility.ToJson(player.GetAttackData(QusapAttackType.StrongKick));
            string headbuttBefore = EditorJsonUtility.ToJson(player.GetAttackData(QusapAttackType.Headbutt));
            SerializedObject serialized = new(player);
            serialized.Update();
            SerializedProperty ground = serialized.FindProperty("weakKick");
            SerializedProperty air = serialized.FindProperty("weakKickAir");
            SerializedProperty dive = serialized.FindProperty("diveHeadbuttAir");
            string groundInvariant = CaptureAttackInvariant(ground, "hitboxSize", "hitboxOffset");
            string airInvariant = CaptureAttackInvariant(air, "hitboxSize", "hitboxOffset");
            string diveInvariant = CaptureAttackInvariant(dive, "hitstunDuration");
            RequirePreservedVertical(ground, new Vector2(1f, 0.6f), new Vector2(0.75f, -0.35f));
            RequirePreservedVertical(air, new Vector2(1f, 0.55f), new Vector2(0.75f, 0f));
            ground.FindPropertyRelative("hitboxSize").vector2Value = GroundKickSize;
            ground.FindPropertyRelative("hitboxOffset").vector2Value = GroundKickOffset;
            air.FindPropertyRelative("hitboxSize").vector2Value = AirKickSize;
            air.FindPropertyRelative("hitboxOffset").vector2Value = AirKickOffset;
            dive.FindPropertyRelative("hitstunDuration").floatValue = 0.35f;
            serialized.ApplyModifiedPropertiesWithoutUndo();
            serialized.Update();
            PrefabUtility.RecordPrefabInstancePropertyModifications(player);
            EditorUtility.SetDirty(player);
            if (EditorJsonUtility.ToJson(player.GetAttackData(QusapAttackType.StrongKick)) != strongBefore
                || EditorJsonUtility.ToJson(player.GetAttackData(QusapAttackType.Headbutt)) != headbuttBefore
                || CaptureAttackInvariant(serialized.FindProperty("weakKick"), "hitboxSize", "hitboxOffset") != groundInvariant
                || CaptureAttackInvariant(serialized.FindProperty("weakKickAir"), "hitboxSize", "hitboxOffset") != airInvariant
                || CaptureAttackInvariant(serialized.FindProperty("diveHeadbuttAir"), "hitstunDuration") != diveInvariant)
                throw new InvalidOperationException("StrongKick o Headbutt terrestre cambió en la copia.");
        }

        private static string CaptureAttackInvariant(SerializedProperty attack, params string[] excludedNames)
        {
            if (attack == null)
                throw new InvalidOperationException("Falta una definición de ataque serializada.");
            var records = new List<string>();
            SerializedProperty iterator = attack.Copy();
            SerializedProperty end = iterator.GetEndProperty();
            bool enterChildren = true;
            while (iterator.NextVisible(enterChildren) && !SerializedProperty.EqualContents(iterator, end))
            {
                enterChildren = false;
                if (excludedNames.Contains(iterator.name))
                    continue;
                string value = iterator.propertyType switch
                {
                    SerializedPropertyType.Boolean => iterator.boolValue.ToString(),
                    SerializedPropertyType.Enum => iterator.enumValueIndex.ToString(),
                    SerializedPropertyType.Float => iterator.floatValue.ToString("R"),
                    SerializedPropertyType.Integer => iterator.longValue.ToString(),
                    SerializedPropertyType.Vector2 => iterator.vector2Value.ToString("R"),
                    _ => iterator.propertyType.ToString()
                };
                records.Add(iterator.propertyPath + "=" + value);
            }
            return string.Join("|", records);
        }

        private static void RequirePreservedVertical(SerializedProperty attack, Vector2 size, Vector2 offset)
        {
            if (attack == null || attack.FindPropertyRelative("hitboxSize").vector2Value != size
                || attack.FindPropertyRelative("hitboxOffset").vector2Value != offset)
                throw new InvalidOperationException("Los datos WeakKick de la escena base no coinciden con el baseline.");
        }

        private static void PlacePlayers(QusapCombatController[] players)
        {
            players[0].transform.position = new Vector3(-0.75f, players[0].transform.position.y,
                players[0].transform.position.z);
            players[1].transform.position = new Vector3(0.75f, players[1].transform.position.y,
                players[1].transform.position.z);
            foreach (QusapCombatController player in players)
                PrefabUtility.RecordPrefabInstancePropertyModifications(player.transform);
        }

        private static QusapCombatController[] FindPlayers(Scene scene)
        {
            return scene.GetRootGameObjects()
                .SelectMany(root => root.GetComponentsInChildren<QusapCombatController>(true))
                .Where(player => player.GetComponent<QusapAnimationDriver>() != null)
                .Distinct().OrderBy(player => player.transform.position.x).ToArray();
        }

        private static Dictionary<Component, string> CaptureProtectedComponents(Scene scene,
            IReadOnlyCollection<QusapCombatController> players)
        {
            Transform[] visuals = players.Select(player => player.transform.Find(ActiveVisualName)).ToArray();
            return scene.GetRootGameObjects().SelectMany(root => root.GetComponentsInChildren<Component>(true))
                .Where(component => component != null && component is not Transform
                    && component is not QusapCombatController
                    && !visuals.Any(visual => visual != null && component.transform.IsChildOf(visual)))
                .ToDictionary(component => component, EditorJsonUtility.ToJson);
        }

        private static void ValidateProtectedComponents(Dictionary<Component, string> snapshot)
        {
            foreach (KeyValuePair<Component, string> pair in snapshot)
                if (pair.Key == null || EditorJsonUtility.ToJson(pair.Key) != pair.Value)
                    throw new InvalidOperationException(
                        $"El componente protegido {pair.Key?.GetType().Name ?? "destruido"} cambió en la copia.");
        }

        private static void ValidateModel(GameObject model)
        {
            SkinnedMeshRenderer[] skinned = model.GetComponentsInChildren<SkinnedMeshRenderer>(true);
            MeshFilter[] staticMeshes = model.GetComponentsInChildren<MeshFilter>(true);
            Transform[] bones = skinned.SelectMany(renderer => renderer.bones)
                .Where(bone => bone != null).Distinct().ToArray();
            int armatures = bones.Select(bone =>
            {
                Transform root = bone;
                while (root.parent != null && root.parent != model.transform)
                    root = root.parent;
                return root;
            }).Distinct().Count();
            if (skinned.Length + staticMeshes.Length != 3 || armatures != 1 || bones.Length != 4)
                throw new InvalidOperationException(
                    $"Estructura Combat v1 inesperada: {skinned.Length + staticMeshes.Length} mallas, " +
                    $"{armatures} armaduras, {bones.Length} huesos.");
        }

        private static void ValidateInPlaceRoot(Transform visual,
            IReadOnlyDictionary<string, AnimationClip> clips)
        {
            Vector3 position = visual.localPosition;
            Quaternion rotation = visual.localRotation;
            Vector3 scale = visual.localScale;
            AnimationMode.StartAnimationMode();
            try
            {
                foreach (AnimationClip clip in clips.Values)
                {
                    foreach (float time in new[] { 0f, clip.length * 0.5f, clip.length })
                    {
                        AnimationMode.BeginSampling();
                        try { AnimationMode.SampleAnimationClip(visual.gameObject, clip, time); }
                        finally { AnimationMode.EndSampling(); }
                        if (visual.localPosition != position || visual.localRotation != rotation
                            || visual.localScale != scale)
                            throw new InvalidOperationException($"{clip.name} altera la raíz visual.");
                    }
                }
            }
            finally { AnimationMode.StopAnimationMode(); }
        }

        private static Vector2 MeasureSoles(Transform visual, AnimationClip idle)
        {
            Vector3 position = visual.localPosition;
            Quaternion rotation = visual.localRotation;
            Vector3 scale = visual.localScale;
            AnimationMode.StartAnimationMode();
            try
            {
                AnimationMode.BeginSampling();
                try { AnimationMode.SampleAnimationClip(visual.gameObject, idle, 0f); }
                finally { AnimationMode.EndSampling(); }
                SkinnedMeshRenderer[] renderers = visual.GetComponentsInChildren<SkinnedMeshRenderer>(false);
                float left = LowestY(FindFoot(renderers, "FloatingFoot_L"));
                float right = LowestY(FindFoot(renderers, "FloatingFoot_R"));
                if (visual.localPosition != position || visual.localRotation != rotation || visual.localScale != scale)
                    throw new InvalidOperationException("Idle altera la raíz visual.");
                return new Vector2(left, right);
            }
            finally { AnimationMode.StopAnimationMode(); }
        }

        private static SkinnedMeshRenderer FindFoot(IEnumerable<SkinnedMeshRenderer> renderers, string name)
        {
            SkinnedMeshRenderer[] matches = renderers.Where(renderer => renderer.name == name
                || renderer.name == name + "_Mesh"
                || (renderer.sharedMesh != null && (renderer.sharedMesh.name == name
                    || renderer.sharedMesh.name == name + "_Mesh"))).ToArray();
            if (matches.Length != 1)
                throw new InvalidOperationException($"No se encontró el pie único {name}.");
            return matches[0];
        }

        private static float LowestY(SkinnedMeshRenderer renderer)
        {
            Mesh mesh = new() { hideFlags = HideFlags.HideAndDontSave };
            try
            {
                renderer.BakeMesh(mesh, false);
                var vertices = new List<Vector3>(mesh.vertexCount);
                mesh.GetVertices(vertices);
                return vertices.Min(vertex => renderer.transform.TransformPoint(vertex).y);
            }
            finally { UnityEngine.Object.DestroyImmediate(mesh); }
        }

        private static Dictionary<string, AnimationClip> LoadClips()
        {
            AnimationClip[] clips = AssetDatabase.LoadAllAssetsAtPath(FbxPath).OfType<AnimationClip>()
                .Where(clip => !IsPreviewName(clip.name) && !HasNumberedDuplicate(clip.name)).ToArray();
            if (clips.Length != 10 || clips.GroupBy(clip => clip.name).Any(group => group.Count() != 1)
                || ClipRequirements.Any(requirement => clips.All(clip => clip.name != requirement.Name)))
                throw new InvalidOperationException("Combat v1 no expone diez clips públicos exactos.");
            return clips.ToDictionary(clip => clip.name, StringComparer.Ordinal);
        }

        private static void ConfigureTransition(AnimatorStateTransition transition, float duration)
        {
            transition.hasExitTime = false;
            transition.hasFixedDuration = true;
            transition.duration = duration;
            transition.canTransitionToSelf = false;
        }

        private static bool HasCondition(AnimatorStateTransition transition, string parameter,
            AnimatorConditionMode mode, float threshold)
        {
            return transition.conditions.Count(condition => condition.parameter == parameter
                && condition.mode == mode && Mathf.Approximately(condition.threshold, threshold)) == 1;
        }

        private static bool IsExactTakeName(string candidate, string required)
        {
            if (string.IsNullOrWhiteSpace(candidate)) return false;
            int separator = candidate.LastIndexOf('|');
            string segment = separator >= 0 ? candidate.Substring(separator + 1) : candidate;
            return string.Equals(segment.Trim(), required, StringComparison.OrdinalIgnoreCase);
        }

        private static bool IsPreviewName(string candidate)
        {
            if (string.IsNullOrWhiteSpace(candidate)) return false;
            int separator = candidate.LastIndexOf('|');
            string segment = separator >= 0 ? candidate.Substring(separator + 1) : candidate;
            return segment.Trim().TrimStart('_').StartsWith("preview", StringComparison.OrdinalIgnoreCase);
        }

        private static bool HasNumberedDuplicate(string candidate)
        {
            return !string.IsNullOrWhiteSpace(candidate) && Regex.IsMatch(candidate, @"\(\d+\)\s*$");
        }

        private static T RequireAsset<T>(string path, string description) where T : UnityEngine.Object
        {
            T asset = AssetDatabase.LoadAssetAtPath<T>(path);
            if (asset == null)
                throw new InvalidOperationException($"Falta {description}: {path}");
            return asset;
        }

        private static string BuildReport()
        {
            float oldReach = 0.75f + 1f * 0.5f;
            float groundReach = GroundKickOffset.x + GroundKickSize.x * 0.5f;
            float airReach = AirKickOffset.x + AirKickSize.x * 0.5f;
            return $"Escena creada y abierta: {TargetScenePath}\n" +
                $"Controller: {TargetControllerPath}\n" +
                "FBX: copia exacta; Generic/Create From This Model; compresión Off; Root Motion bloqueado.\n" +
                "Clips: 10 exactos. Modelo: 3 mallas, 1 armadura, 4 huesos.\n\n" +
                $"WeakKickGround speed: {GroundKickSpeed:0.######} " +
                "(19/60 s ajustados a 0.30 s mecánicos; se descartó 0.989583).\n" +
                $"WeakKickAir speed: {AirKickSpeed:0.######} (16/60 s ajustados a 0.26 s mecánicos).\n\n" +
                $"Ground box: reach {oldReach:0.00} -> {groundReach:0.00}; size (1.00, 0.60) -> " +
                $"{GroundKickSize}; offset (0.75, -0.35) -> {GroundKickOffset}; margen garra {groundReach - GroundClawReach:0.00}.\n" +
                $"Air box: reach {oldReach:0.00} -> {airReach:0.00}; size (1.00, 0.55) -> " +
                $"{AirKickSize}; offset (0.75, 0.00) -> {AirKickOffset}; margen garra {airReach - AirClawReach:0.00}.\n" +
                "Dive target hitstun: 0.35 s. Daño, knockback, fases, gravedad y Headbutt terrestre: intactos.\n" +
                "Visual v12 conservado/instalado como backup desactivado; Combat v1 activo; Apply Root Motion desactivado.\n" +
                "La base aérea real usa aún el visual prototipo: también se conserva desactivado como PreCombat_Backup.";
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
                throw new InvalidOperationException("Sal de Play Mode y Animation Mode antes de crear la prueba.");
        }

        private readonly struct ClipRequirement
        {
            public ClipRequirement(string name, float frames, bool loop)
            {
                Name = name;
                Frames = frames;
                Loop = loop;
            }
            public string Name { get; }
            public float Frames { get; }
            public bool Loop { get; }
        }
    }
}
#endif
