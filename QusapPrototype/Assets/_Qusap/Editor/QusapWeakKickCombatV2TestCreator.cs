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
    public static class QusapWeakKickCombatV2TestCreator
    {
        private const string ArtFolder = "Assets/Scenes/Art/Characters/Qusap/Final_Light_v3/";
        private const string ExternalFbxPath =
            @"C:\Users\victo\Documents\Qusap\3D\Modelado\Qusap_Luz_Definitivo\03_Export_Unity\Qusap_Luz_Combat_v2.fbx";
        private const string V1FbxPath = ArtFolder + "Qusap_Luz_Combat_v1.fbx";
        private const string V2FbxPath = ArtFolder + "Qusap_Luz_Combat_v2.fbx";
        private const string SourceScenePath = "Assets/_Qusap/Scenes/Qusap_WeakKickAnimationTest_v1.unity";
        private const string TargetScenePath = "Assets/_Qusap/Scenes/Qusap_WeakKickAnimationV2Test_v1.unity";
        private const string TargetControllerPath =
            ArtFolder + "Qusap_Luz_Animator_CombatV2Test_v1.controller";
        private const string ActiveVisualName = "PlayerVisual";
        private const string BackupVisualName = "PlayerVisual_CombatV1_Backup";
        private const float GroundSpeed = 0.989583333f;
        private const float AirSpeed = 1.025641026f;
        private const float FrameTolerance = 0.01f;

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
            new("Qusap_WeakKickAir", false)
        };

        [MenuItem("Tools/Qusap/Create Weak Kick Combat V2 Test")]
        private static void CreateWeakKickCombatV2Test()
        {
            try
            {
                EnsureEditorIsIdle();
                if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo())
                    return;
                if (!ConfirmReplacement())
                    return;

                PreflightContext preflight = InspectSourceScene();
                string sourceSceneHash = FileHash(AbsolutePath(SourceScenePath));
                string sourceControllerHash = FileHash(AbsolutePath(preflight.ControllerPath));
                string v1FbxHash = FileHash(AbsolutePath(V1FbxPath));

                EnsureExactV2Fbx();
                Dictionary<string, AnimationClip> clips = ConfigureV2Importer();
                GameObject model = RequireAsset<GameObject>(V2FbxPath, "FBX Combat v2");
                ValidateModel(model);
                AnimatorController controller = CreateControllerCopy(preflight, clips);
                Scene scene = CreateSceneCopy(preflight, model, controller);

                if (FileHash(AbsolutePath(SourceScenePath)) != sourceSceneHash
                    || FileHash(AbsolutePath(preflight.ControllerPath)) != sourceControllerHash
                    || FileHash(AbsolutePath(V1FbxPath)) != v1FbxHash)
                    throw new InvalidOperationException("Una referencia Combat_v1 cambió durante la creación.");
                if (!EditorSceneManager.SaveScene(scene, TargetScenePath))
                    throw new InvalidOperationException("Unity no pudo guardar la escena V2.");

                Selection.activeObject = AssetDatabase.LoadAssetAtPath<SceneAsset>(TargetScenePath);
                EditorUtility.DisplayDialog("Qusap Weak Kick Combat V2 Test", BuildReport(preflight), "Aceptar");
            }
            catch (Exception exception)
            {
                Debug.LogException(exception);
                EditorUtility.DisplayDialog(
                    "Qusap Weak Kick Combat V2 Test",
                    "No se completó la prueba V2. Ninguna escena, prefab o asset Combat_v1 fue reemplazado.\n\n"
                    + exception.Message,
                    "Aceptar");
            }
        }

        private static bool ConfirmReplacement()
        {
            bool exists = AssetDatabase.LoadAssetAtPath<UnityEngine.Object>(TargetControllerPath) != null
                || AssetDatabase.LoadAssetAtPath<UnityEngine.Object>(TargetScenePath) != null;
            if (!exists)
                return true;
            return EditorUtility.DisplayDialog(
                "Qusap Weak Kick Combat V2 Test",
                "Ya existe una salida V2. ¿Deseas reemplazar únicamente el controller y la escena V2?",
                "Reemplazar V2", "Cancelar");
        }

        private static PreflightContext InspectSourceScene()
        {
            RequireAsset<SceneAsset>(SourceScenePath, "escena funcional Combat_v1");
            Scene preview = EditorSceneManager.OpenPreviewScene(SourceScenePath);
            try
            {
                PlayerContext[] players = ResolvePlayers(preview, null, V1FbxPath);
                if (players.Length != 2)
                    throw new InvalidOperationException(
                        $"La escena Combat_v1 es ambigua: se encontraron {players.Length} jugadores funcionales; se requieren 2.");
                string[] controllerPaths = players.Select(player =>
                        AssetDatabase.GetAssetPath(player.Animator.runtimeAnimatorController))
                    .Distinct(StringComparer.Ordinal).ToArray();
                if (controllerPaths.Length != 1 || string.IsNullOrEmpty(controllerPaths[0]))
                    throw new InvalidOperationException(
                        "Los PlayerVisual activos no comparten un único Animator Controller Combat_v1.");
                var controller = RequireAsset<AnimatorController>(controllerPaths[0], "controller Combat_v1 activo");
                ControllerSnapshot snapshot = CaptureController(controller);
                if (!snapshot.States.ContainsKey("Qusap_WeakKickGround")
                    || !snapshot.States.ContainsKey("Qusap_WeakKickAir")
                    || snapshot.States.Count != 10)
                    throw new InvalidOperationException(
                        $"El controller Combat_v1 contiene {snapshot.States.Count} estados; faltan las dos rutas WeakKick exactas.");
                ValidateWeakTransitions(controller);
                return new PreflightContext(controllerPaths[0], snapshot, players.Length);
            }
            finally { EditorSceneManager.ClosePreviewScene(preview); }
        }

        private static void EnsureExactV2Fbx()
        {
            if (!File.Exists(ExternalFbxPath))
                throw new FileNotFoundException("No existe Qusap_Luz_Combat_v2.fbx externo.", ExternalFbxPath);
            string destination = AbsolutePath(V2FbxPath);
            if (!File.Exists(destination))
            {
                if (File.Exists(destination + ".meta"))
                    throw new InvalidOperationException("Existe un .meta huérfano para Combat_v2.");
                File.Copy(ExternalFbxPath, destination, false);
            }
            if (FileHash(ExternalFbxPath) != FileHash(destination))
                throw new InvalidOperationException(
                    "El FBX Combat_v2 del proyecto no coincide con el origen; no se sobrescribirá.");
            AssetDatabase.ImportAsset(V2FbxPath, ImportAssetOptions.ForceSynchronousImport);
        }

        private static Dictionary<string, AnimationClip> ConfigureV2Importer()
        {
            var importer = AssetImporter.GetAtPath(V2FbxPath) as ModelImporter;
            var reference = AssetImporter.GetAtPath(V1FbxPath) as ModelImporter;
            if (importer == null || reference == null)
                throw new InvalidOperationException("No se resolvieron los ModelImporter Combat_v1/v2.");
            CopyImporterSettings(reference, importer);

            ModelImporterClipAnimation[] takes = importer.defaultClipAnimations;
            if (takes == null || takes.Length == 0)
                throw new InvalidOperationException("Combat_v2 no expone Source Takes.");
            foreach (ModelImporterClipAnimation take in takes.Where(item => item != null))
                Debug.Log($"Combat_v2 Source Take: {take.name} | {take.takeName} | "
                    + $"{take.firstFrame:R}..{take.lastFrame:R}");

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
                    throw new InvalidOperationException(
                        $"Source Take '{requirement.Name}': encontrados {matches.Length}; se requiere exactamente 1.");
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

            importer = AssetImporter.GetAtPath(V2FbxPath) as ModelImporter;
            Dictionary<string, AnimationClip> clips = LoadV2Clips();
            if (importer == null || importer.animationType != ModelImporterAnimationType.Generic
                || importer.avatarSetup != ModelImporterAvatarSetup.CreateFromThisModel
                || !importer.importAnimation || importer.animationCompression != ModelImporterAnimationCompression.Off
                || importer.motionNodeName != string.Empty || importer.importCameras || importer.importLights
                || importer.clipAnimations.Length != 10 || clips.Count != 10
                || !ImporterSettingsMatch(reference, importer))
                throw new InvalidOperationException("Combat_v2 no conservó la configuración de importación requerida.");
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
                        $"{requirement.Name} cambió de rango/loop/root lock durante la importación.");
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

        private static AnimatorController CreateControllerCopy(PreflightContext preflight,
            IReadOnlyDictionary<string, AnimationClip> clips)
        {
            if (AssetDatabase.LoadAssetAtPath<UnityEngine.Object>(TargetControllerPath) != null)
                AssetDatabase.DeleteAsset(TargetControllerPath);
            if (!AssetDatabase.CopyAsset(preflight.ControllerPath, TargetControllerPath))
                throw new InvalidOperationException("Unity no pudo copiar el controller Combat_v1.");
            var controller = RequireAsset<AnimatorController>(TargetControllerPath, "controller CombatV2Test");
            ControllerSnapshot pristineCopy = CaptureController(controller);
            if (!ControllerSnapshotsEqual(preflight.Controller, pristineCopy))
                throw new InvalidOperationException("La copia inicial del controller no es semánticamente idéntica.");

            Dictionary<string, AnimatorState> states = EnumerateStates(controller.layers[0].stateMachine)
                .ToDictionary(state => state.name, StringComparer.Ordinal);
            states["Qusap_WeakKickGround"].motion = clips["Qusap_WeakKickGround"];
            states["Qusap_WeakKickGround"].speed = GroundSpeed;
            states["Qusap_WeakKickAir"].motion = clips["Qusap_WeakKickAir"];
            states["Qusap_WeakKickAir"].speed = AirSpeed;
            EditorUtility.SetDirty(states["Qusap_WeakKickGround"]);
            EditorUtility.SetDirty(states["Qusap_WeakKickAir"]);
            EditorUtility.SetDirty(controller);
            AssetDatabase.SaveAssetIfDirty(controller);
            controller = RequireAsset<AnimatorController>(TargetControllerPath, "controller CombatV2Test guardado");
            ValidateControllerCopy(preflight.Controller, controller, clips);
            return controller;
        }

        private static void ValidateControllerCopy(ControllerSnapshot source, AnimatorController target,
            IReadOnlyDictionary<string, AnimationClip> clips)
        {
            ControllerSnapshot actual = CaptureController(target);
            if (source.Parameters != actual.Parameters || source.Layer != actual.Layer
                || source.AnyStateTransitions != actual.AnyStateTransitions
                || source.States.Count != actual.States.Count)
                throw new InvalidOperationException("Parámetros, layer o transiciones Any State cambiaron en la copia.");
            foreach (KeyValuePair<string, StateSnapshot> pair in source.States)
            {
                StateSnapshot targetState = actual.States[pair.Key];
                bool weak = pair.Key == "Qusap_WeakKickGround" || pair.Key == "Qusap_WeakKickAir";
                if (pair.Value.Semantics != targetState.Semantics
                    || pair.Value.Transitions != targetState.Transitions
                    || (!weak && (pair.Value.Motion != targetState.Motion
                        || !Mathf.Approximately(pair.Value.Speed, targetState.Speed))))
                    throw new InvalidOperationException($"El estado no-objetivo o sus transiciones cambiaron: {pair.Key}.");
            }
            AnimatorState[] states = EnumerateStates(target.layers[0].stateMachine).ToArray();
            AnimatorState ground = states.Single(state => state.name == "Qusap_WeakKickGround");
            AnimatorState air = states.Single(state => state.name == "Qusap_WeakKickAir");
            if (ground.motion != clips["Qusap_WeakKickGround"] || air.motion != clips["Qusap_WeakKickAir"]
                || AssetDatabase.GetAssetPath(ground.motion) != V2FbxPath
                || AssetDatabase.GetAssetPath(air.motion) != V2FbxPath
                || !Mathf.Approximately(ground.speed, GroundSpeed)
                || !Mathf.Approximately(air.speed, AirSpeed)
                || ground.motion == source.States[ground.name].Motion
                || air.motion == source.States[air.name].Motion)
                throw new InvalidOperationException("Los dos estados WeakKick no apuntan exclusivamente a Combat_v2.");
        }

        private static Scene CreateSceneCopy(PreflightContext preflight, GameObject model,
            AnimatorController controller)
        {
            if (AssetDatabase.LoadAssetAtPath<UnityEngine.Object>(TargetScenePath) != null)
            {
                Scene loadedTarget = SceneManager.GetSceneByPath(TargetScenePath);
                if (loadedTarget.IsValid() && loadedTarget.isLoaded)
                    EditorSceneManager.OpenScene(SourceScenePath, OpenSceneMode.Single);
                AssetDatabase.DeleteAsset(TargetScenePath);
            }
            if (!AssetDatabase.CopyAsset(SourceScenePath, TargetScenePath))
                throw new InvalidOperationException("Unity no pudo copiar la escena Combat_v1.");
            Scene scene = EditorSceneManager.OpenScene(TargetScenePath, OpenSceneMode.Single);
            PlayerContext[] players = ResolvePlayers(scene, preflight.ControllerPath, V1FbxPath);
            if (players.Length != preflight.PlayerCount)
                throw new InvalidOperationException(
                    $"La copia contiene {players.Length} jugadores y la fuente {preflight.PlayerCount}.");
            Dictionary<Component, string> protectedComponents = CaptureProtectedComponents(scene, players);
            string[] combatBefore = players.Select(player => EditorJsonUtility.ToJson(player.Combat)).ToArray();

            for (int index = 0; index < players.Length; index++)
            {
                InstallV2Visual(players[index], model, controller);
                if (EditorJsonUtility.ToJson(players[index].Combat) != combatBefore[index])
                    throw new InvalidOperationException(
                        $"Los datos de combate/hitbox del jugador {index + 1} cambiaron.");
            }
            ValidateProtectedComponents(protectedComponents);
            ValidateInstalledScene(scene, controller, players.Length);
            EditorSceneManager.MarkSceneDirty(scene);
            return scene;
        }

        private static void InstallV2Visual(PlayerContext player, GameObject model,
            AnimatorController controller)
        {
            Transform oldVisual = player.Visual;
            Vector3 position = oldVisual.localPosition;
            Quaternion rotation = oldVisual.localRotation;
            Vector3 scale = oldVisual.localScale;
            Animator sourceAnimator = player.Animator;
            bool animatorEnabled = sourceAnimator.enabled;
            AnimatorCullingMode culling = sourceAnimator.cullingMode;
            AnimatorUpdateMode updateMode = sourceAnimator.updateMode;
            bool fireEvents = sourceAnimator.fireEvents;

            oldVisual.name = BackupVisualName;
            oldVisual.gameObject.SetActive(false);
            GameObject visual = PrefabUtility.InstantiatePrefab(model, player.Root) as GameObject;
            if (visual == null)
                throw new InvalidOperationException("Unity no pudo instanciar Combat_v2.");
            visual.name = ActiveVisualName;
            visual.transform.localPosition = position;
            visual.transform.localRotation = rotation;
            visual.transform.localScale = scale;
            visual.SetActive(true);
            Animator animator = visual.GetComponent<Animator>() ?? visual.AddComponent<Animator>();
            animator.enabled = animatorEnabled;
            animator.runtimeAnimatorController = controller;
            animator.applyRootMotion = false;
            animator.cullingMode = culling;
            animator.updateMode = updateMode;
            animator.fireEvents = fireEvents;

            if (visual.transform.localPosition != position || visual.transform.localRotation != rotation
                || visual.transform.localScale != scale || animator.applyRootMotion)
                throw new InvalidOperationException("Combat_v2 no conservó el transform visual o Root Motion Off.");
            PrefabUtility.RecordPrefabInstancePropertyModifications(oldVisual.gameObject);
            PrefabUtility.RecordPrefabInstancePropertyModifications(visual);
            PrefabUtility.RecordPrefabInstancePropertyModifications(visual.transform);
            PrefabUtility.RecordPrefabInstancePropertyModifications(animator);
        }

        private static void ValidateInstalledScene(Scene scene, AnimatorController controller, int playerCount)
        {
            PlayerContext[] installed = ResolvePlayers(scene, TargetControllerPath, V2FbxPath);
            if (installed.Length != playerCount)
                throw new InvalidOperationException(
                    $"Tras instalar v2 se resolvieron {installed.Length}/{playerCount} jugadores funcionales.");
            foreach (PlayerContext player in installed)
            {
                Transform[] backups = player.Root.Cast<Transform>()
                    .Where(child => child.name == BackupVisualName && !child.gameObject.activeSelf).ToArray();
                if (backups.Length != 1 || player.Animator.runtimeAnimatorController != controller
                    || player.Animator.applyRootMotion)
                    throw new InvalidOperationException("El visual activo o el backup Combat_v1 no es exacto.");
            }
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
                        $"El jugador '{combat.name}' tiene {animators.Length} Animators activos; se requiere 1.");
                Animator animator = animators[0];
                Transform visual = animator.transform;
                while (visual.parent != null && visual.parent != combat.transform)
                    visual = visual.parent;
                if (visual.parent != combat.transform || visual.name != ActiveVisualName)
                    throw new InvalidOperationException(
                        $"No se pudo localizar el PlayerVisual directo de '{combat.name}'.");
                string controllerPath = AssetDatabase.GetAssetPath(animator.runtimeAnimatorController);
                string fbxPath = PrefabUtility.GetPrefabAssetPathOfNearestInstanceRoot(visual.gameObject);
                if (expectedControllerPath != null && controllerPath != expectedControllerPath)
                    throw new InvalidOperationException(
                        $"{combat.name}: controller encontrado '{controllerPath}', esperado '{expectedControllerPath}'.");
                if (fbxPath != expectedFbxPath)
                    throw new InvalidOperationException(
                        $"{combat.name}: PlayerVisual encontrado '{fbxPath}', esperado '{expectedFbxPath}'.");
                result.Add(new PlayerContext(combat.transform, visual, animator, combat, driver));
            }
            return result.OrderBy(player => player.Root.GetSiblingIndex()).ToArray();
        }

        private static Dictionary<Component, string> CaptureProtectedComponents(Scene scene,
            IReadOnlyCollection<PlayerContext> players)
        {
            Transform[] visuals = players.Select(player => player.Visual).ToArray();
            return scene.GetRootGameObjects().SelectMany(root => root.GetComponentsInChildren<Component>(true))
                .Where(component => component != null
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

        private static void ValidateWeakTransitions(AnimatorController controller)
        {
            AnimatorStateMachine machine = controller.layers[0].stateMachine;
            Dictionary<string, AnimatorState> states = EnumerateStates(machine)
                .ToDictionary(state => state.name, StringComparer.Ordinal);
            foreach ((string name, QusapAttackVariant variant) in new[]
            {
                ("Qusap_WeakKickGround", QusapAttackVariant.WeakKickGround),
                ("Qusap_WeakKickAir", QusapAttackVariant.WeakKickAir)
            })
            {
                AnimatorState state = states[name];
                AnimatorStateTransition[] entries = machine.anyStateTransitions
                    .Where(transition => transition.destinationState == state).ToArray();
                if (entries.Length != 1 || state.transitions.Length != 4
                    || !entries[0].conditions.Any(condition => condition.parameter == "CombatAnimating"
                        && condition.mode == AnimatorConditionMode.If)
                    || !entries[0].conditions.Any(condition => condition.parameter == "AttackVariant"
                        && condition.mode == AnimatorConditionMode.Equals
                        && Mathf.Approximately(condition.threshold, (int)variant)))
                    throw new InvalidOperationException($"Las transiciones reales de {name} no son válidas.");
            }
        }

        private static IEnumerable<AnimatorState> EnumerateStates(AnimatorStateMachine machine)
        {
            foreach (ChildAnimatorState child in machine.states)
                yield return child.state;
            foreach (ChildAnimatorStateMachine child in machine.stateMachines)
                foreach (AnimatorState state in EnumerateStates(child.stateMachine))
                    yield return state;
        }

        private static Dictionary<string, AnimationClip> LoadV2Clips()
        {
            AnimationClip[] clips = AssetDatabase.LoadAllAssetsAtPath(V2FbxPath).OfType<AnimationClip>()
                .Where(clip => !IsPreviewName(clip.name) && !HasNumberedDuplicate(clip.name)).ToArray();
            if (clips.Length != 10 || clips.GroupBy(clip => clip.name).Any(group => group.Count() != 1)
                || ClipRequirements.Any(requirement => clips.All(clip => clip.name != requirement.Name)))
                throw new InvalidOperationException("Combat_v2 no expone los diez clips públicos exactos.");
            return clips.ToDictionary(clip => clip.name, StringComparer.Ordinal);
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
                    $"Combat_v2: {skinned.Length + staticMeshes.Length} mallas, "
                    + $"{armatures} armaduras, {bones.Length} huesos; se esperaban 3/1/4.");
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

        private static string ObjectId(UnityEngine.Object item)
        {
            if (item == null) return "null";
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

        private static string BuildReport(PreflightContext preflight)
        {
            return $"FBX nuevo: {V2FbxPath}\n"
                + "Importación: Generic/Create From This Model, Animation On, Compression Off, "
                + "Root bloqueado, cámaras/luces Off.\n"
                + "Contenido: 10 clips exactos, 3 mallas, 1 armadura, 4 huesos.\n\n"
                + $"Controller nuevo: {TargetControllerPath}\n"
                + "Solo cambiaron los Motion WeakKickGround/WeakKickAir.\n"
                + $"Animator Speed: Ground {GroundSpeed:R}; Air {AirSpeed:R}.\n\n"
                + $"Escena creada y abierta: {TargetScenePath}\n"
                + $"Fuente: {SourceScenePath}; controller localizado: {preflight.ControllerPath}.\n"
                + $"Jugadores conservados: {preflight.PlayerCount}. Combat_v1 queda como backup desactivado.\n"
                + "Jugabilidad, hitboxes corregidas, daño, knockback, hitstun, cámara y controles: intactos.";
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
                throw new InvalidOperationException("Sal de Play Mode y Animation Mode antes de crear la prueba V2.");
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
                QusapCombatController combat, QusapAnimationDriver driver)
            {
                Root = root;
                Visual = visual;
                Animator = animator;
                Combat = combat;
                Driver = driver;
            }
            public Transform Root { get; }
            public Transform Visual { get; }
            public Animator Animator { get; }
            public QusapCombatController Combat { get; }
            public QusapAnimationDriver Driver { get; }
        }

        private sealed class PreflightContext
        {
            public PreflightContext(string controllerPath, ControllerSnapshot controller, int playerCount)
            {
                ControllerPath = controllerPath;
                Controller = controller;
                PlayerCount = playerCount;
            }
            public string ControllerPath { get; }
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
    }
}
#endif
