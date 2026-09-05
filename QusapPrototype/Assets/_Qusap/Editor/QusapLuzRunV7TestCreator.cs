#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using Qusap;
using UnityEditor;
using UnityEditor.Animations;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Qusap.EditorTools
{
    public static class QusapLuzRunV7TestCreator
    {
        private const string MenuPath = "Tools/Qusap/Create Qusap Luz Run v7 Test";
        private const string FbxFileName = "Qusap_Luz_Locomotion_v7.fbx";
        private const string FbxAssetPath =
            "Assets/Scenes/Art/Characters/Qusap/Final_Light_v3/" + FbxFileName;
        private const string ExternalFbxPath =
            @"C:\Users\victo\Documents\Qusap\3D\Modelado\Qusap_Luz_Definitivo\03_Export_Unity\Qusap_Luz_Locomotion_v7.fbx";
        private const string GroundAlignedSceneToken = "Qusap_Luz_Locomotion_GroundAlignedTest";
        private const string TargetSceneFileName = "Qusap_Luz_Locomotion_RunV7Test_v1.unity";
        private const string TargetControllerFileName = "Qusap_Luz_Animator_RunV7Test_v1.controller";
        private const string ActiveVisualName = "PlayerVisual";
        private const string BackupVisualName = "PlayerVisual_LocomotionV5_GroundAligned_Backup";
        private const string DialogTitle = "Qusap Luz Run v7 Test";
        private const float FrameTolerance = 0.01f;
        private const float RunDurationSeconds = 0.70f;
        private const float DurationTolerance = 0.001f;
        private const float SoleHeightTolerance = 0.01f;

        private static readonly ClipRequirement[] ClipRequirements =
        {
            new("Qusap_Idle", 120f, true),
            new("Qusap_Run", 42f, true),
            new("Qusap_JumpRise", 18f, false),
            new("Qusap_Fall", 24f, true),
            new("Qusap_Land", 18f, false)
        };

        [MenuItem(MenuPath)]
        private static void CreateRunV7Test()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode)
            {
                ShowError("La prueba no puede crearse mientras Unity está entrando o se encuentra en Play Mode.");
                return;
            }

            if (AnimationMode.InAnimationMode())
            {
                ShowError("Sal de Animation Mode antes de crear la prueba Run v7.");
                return;
            }

            if (!TryFindExactFbx(out string fbxPath, out ModelImporter importer, out string fbxError))
            {
                ShowError(fbxError);
                return;
            }

            if (!TryFindLatestGroundAlignedScene(out string sourceScenePath, out string sceneError))
            {
                ShowError(sceneError);
                return;
            }

            string sceneDirectory = Path.GetDirectoryName(sourceScenePath);
            string targetScenePath = CombineAssetPath(sceneDirectory, TargetSceneFileName);
            string targetControllerPath = CombineAssetPath(
                Path.GetDirectoryName(fbxPath),
                TargetControllerFileName);

            if (!ValidateTestDestination(targetScenePath, TargetSceneFileName, typeof(SceneAsset))
                || !ValidateTestDestination(
                    targetControllerPath, TargetControllerFileName, typeof(AnimatorController)))
            {
                return;
            }

            try
            {
                ValidateExistingFbx(fbxPath);
                Dictionary<string, AnimationClip> v7Clips =
                    ConfigureImporterAndLoadClips(fbxPath, importer);
                GameObject v7Model = AssetDatabase.LoadAssetAtPath<GameObject>(fbxPath);
                if (v7Model == null)
                {
                    throw new InvalidOperationException($"No se pudo cargar el modelo v7 desde '{fbxPath}'.");
                }

                Scene testScene = RebuildTestScene(sourceScenePath, targetScenePath);
                SceneManager.SetActiveScene(testScene);

                SceneContext context = ResolveSceneContext(testScene);
                AnimatorController sourceController =
                    context.Animator.runtimeAnimatorController as AnimatorController;
                if (sourceController == null)
                {
                    throw new InvalidOperationException(
                        "El visual GroundAligned no utiliza directamente un AnimatorController copiable.");
                }

                Dictionary<string, AnimatorState> sourceStates = ValidateFiveStates(sourceController);
                ValidateDriverParameters(sourceController);
                string sourceControllerPath = AssetDatabase.GetAssetPath(sourceController);
                string sourceSignature = BuildControllerSignature(sourceController);
                AnimationClip previousIdle = sourceStates["Qusap_Idle"].motion as AnimationClip;
                if (previousIdle == null)
                {
                    throw new InvalidOperationException(
                        "El estado Qusap_Idle del controller GroundAligned no contiene un AnimationClip.");
                }

                float previousSoleHeight = MeasureSoleHeight(
                    context.VisualRoot,
                    previousIdle);

                RebuildTestAsset(sourceControllerPath, targetControllerPath, TargetControllerFileName);
                AnimatorController targetController =
                    AssetDatabase.LoadAssetAtPath<AnimatorController>(targetControllerPath);
                if (targetController == null)
                {
                    throw new InvalidOperationException(
                        $"No se pudo cargar el controller copiado '{targetControllerPath}'.");
                }

                if (BuildControllerSignature(targetController) != sourceSignature)
                {
                    throw new InvalidOperationException(
                        "La copia inicial del Animator Controller no conserva exactamente la estructura GroundAligned.");
                }

                Dictionary<string, AnimatorState> targetStates = ValidateFiveStates(targetController);
                foreach (ClipRequirement requirement in ClipRequirements)
                {
                    AnimatorState state = targetStates[requirement.Name];
                    state.motion = v7Clips[requirement.Name];
                    EditorUtility.SetDirty(state);
                }

                EditorUtility.SetDirty(targetController);
                AssetDatabase.SaveAssetIfDirty(targetController);

                targetController = AssetDatabase.LoadAssetAtPath<AnimatorController>(targetControllerPath);
                targetStates = ValidateFiveStates(targetController);
                ValidateDriverParameters(targetController);
                if (BuildControllerSignature(targetController) != sourceSignature
                    || ClipRequirements.Any(requirement =>
                        targetStates[requirement.Name].motion != v7Clips[requirement.Name]))
                {
                    throw new InvalidOperationException(
                        "El controller nuevo modificó algo distinto de los cinco Motion o no guardó los clips v7.");
                }

                Vector3 preservedPosition = context.VisualRoot.localPosition;
                Quaternion preservedRotation = context.VisualRoot.localRotation;
                Vector3 preservedScale = context.VisualRoot.localScale;
                if (context.PlayerRoot.Find(BackupVisualName) != null)
                {
                    throw new InvalidOperationException(
                        $"Ya existe un respaldo llamado '{BackupVisualName}' en la copia de escena.");
                }

                Undo.SetCurrentGroupName("Create Qusap Luz Run v7 Test");
                int undoGroup = Undo.GetCurrentGroup();
                Undo.RecordObject(context.VisualRoot.gameObject, "Back up GroundAligned visual");
                context.VisualRoot.name = BackupVisualName;
                context.VisualRoot.gameObject.SetActive(false);
                PrefabUtility.RecordPrefabInstancePropertyModifications(context.VisualRoot.gameObject);

                GameObject newVisual =
                    PrefabUtility.InstantiatePrefab(v7Model, context.PlayerRoot) as GameObject;
                if (newVisual == null)
                {
                    throw new InvalidOperationException("Unity no pudo instanciar Qusap Luz Locomotion v7.");
                }

                Undo.RegisterCreatedObjectUndo(newVisual, "Create Qusap Luz v7 PlayerVisual");
                newVisual.name = ActiveVisualName;
                newVisual.SetActive(true);
                Transform newVisualTransform = newVisual.transform;
                newVisualTransform.localPosition = preservedPosition;
                newVisualTransform.localRotation = preservedRotation;
                newVisualTransform.localScale = preservedScale;

                Animator newAnimator = newVisual.GetComponent<Animator>();
                if (newAnimator == null)
                {
                    newAnimator = Undo.AddComponent<Animator>(newVisual);
                }

                Undo.RecordObject(newAnimator, "Configure Run v7 Animator");
                newAnimator.runtimeAnimatorController = targetController;
                newAnimator.applyRootMotion = false;
                newAnimator.cullingMode = AnimatorCullingMode.AlwaysAnimate;
                PrefabUtility.RecordPrefabInstancePropertyModifications(newVisual);
                PrefabUtility.RecordPrefabInstancePropertyModifications(newVisualTransform);
                PrefabUtility.RecordPrefabInstancePropertyModifications(newAnimator);

                float v7SoleHeight = MeasureSoleHeight(
                    newVisualTransform,
                    v7Clips["Qusap_Idle"]);
                ValidateSceneResult(
                    context,
                    newVisualTransform,
                    newAnimator,
                    targetController,
                    targetStates,
                    preservedPosition,
                    preservedRotation,
                    preservedScale,
                    previousSoleHeight,
                    v7SoleHeight);

                EditorSceneManager.MarkSceneDirty(testScene);
                if (!EditorSceneManager.SaveScene(testScene))
                {
                    throw new InvalidOperationException(
                        $"Unity no pudo guardar la escena Run v7 '{targetScenePath}'.");
                }

                Undo.CollapseUndoOperations(undoGroup);
                AssetDatabase.SaveAssetIfDirty(targetController);
                Selection.activeGameObject = newVisual;

                string report =
                    $"Escena creada: {targetScenePath}\n" +
                    $"Controller creado: {targetControllerPath}\n" +
                    $"localPosition preservada: {FormatVector(preservedPosition)}\n" +
                    $"Diferencia de altura de suelas: {(v7SoleHeight - previousSoleHeight):0.######}";
                Debug.Log(report, newVisual);
                EditorUtility.DisplayDialog(DialogTitle, report, "Aceptar");
            }
            catch (Exception exception)
            {
                Debug.LogException(exception);
                ShowError(
                    "No se pudo completar la prueba Run v7. Los assets originales permanecen intactos; " +
                    "revisa la copia parcial y la consola.\n\n" + exception.Message);
            }
        }

        private static bool TryFindExactFbx(
            out string fbxPath,
            out ModelImporter importer,
            out string error)
        {
            string expectedBaseName = Path.GetFileNameWithoutExtension(FbxFileName);
            string[] matches = AssetDatabase.FindAssets(expectedBaseName + " t:Model")
                .Select(AssetDatabase.GUIDToAssetPath)
                .Where(path =>
                    path.StartsWith("Assets/", StringComparison.Ordinal)
                    && string.Equals(Path.GetFileName(path), FbxFileName, StringComparison.Ordinal))
                .Distinct(StringComparer.Ordinal)
                .ToArray();

            fbxPath = matches.Length == 1 ? matches[0] : null;
            importer = fbxPath != null ? AssetImporter.GetAtPath(fbxPath) as ModelImporter : null;
            error = null;

            if (matches.Length != 1)
            {
                error = matches.Length == 0
                    ? $"AssetDatabase no encontró '{FbxFileName}' dentro de Assets."
                    : $"AssetDatabase encontró más de un '{FbxFileName}':\n" + string.Join("\n", matches);
                return false;
            }

            if (importer == null)
            {
                error = $"El asset '{fbxPath}' no utiliza ModelImporter.";
                return false;
            }

            return true;
        }

        private static bool TryFindLatestGroundAlignedScene(
            out string scenePath,
            out string error)
        {
            string projectRoot = Directory.GetParent(Application.dataPath).FullName;
            string[] candidates = AssetDatabase.FindAssets("t:Scene")
                .Select(AssetDatabase.GUIDToAssetPath)
                .Where(path =>
                    Path.GetFileNameWithoutExtension(path)
                        .IndexOf(GroundAlignedSceneToken, StringComparison.Ordinal) >= 0)
                .OrderByDescending(path =>
                    File.GetLastWriteTimeUtc(Path.Combine(projectRoot, path)))
                .ThenByDescending(path => path, StringComparer.Ordinal)
                .ToArray();

            scenePath = candidates.FirstOrDefault();
            error = scenePath == null
                ? $"No se encontró ninguna escena cuyo nombre contenga '{GroundAlignedSceneToken}'."
                : null;
            return scenePath != null;
        }

        private static Dictionary<string, AnimationClip> ConfigureImporterAndLoadClips(
            string fbxPath,
            ModelImporter importer)
        {
            ModelImporterClipAnimation[] sourceTakes = importer.defaultClipAnimations;
            if (sourceTakes == null || sourceTakes.Length == 0)
            {
                throw new InvalidOperationException($"'{fbxPath}' no contiene Source Takes.");
            }

            ModelImporterClipAnimation[] nonPreviewTakes = sourceTakes
                .Where(take =>
                    take != null
                    && !IsPreviewName(take.name)
                    && !IsPreviewName(take.takeName))
                .ToArray();
            if (nonPreviewTakes.Any(take => HasNumberedDuplicateName(take.name)
                || HasNumberedDuplicateName(take.takeName)))
            {
                throw new InvalidOperationException(
                    "El FBX v7 contiene un Source Take no preview con nombre duplicado o sufijo '(1)'.");
            }

            var definitions = new List<ModelImporterClipAnimation>(ClipRequirements.Length);
            foreach (ClipRequirement requirement in ClipRequirements)
            {
                ModelImporterClipAnimation[] matches = nonPreviewTakes
                    .Where(take => IsExactTakeName(take.name, requirement.Name)
                        || IsExactTakeName(take.takeName, requirement.Name))
                    .ToArray();
                if (matches.Length != 1)
                {
                    throw new InvalidOperationException(
                        $"Se esperaba un Source Take único '{requirement.Name}', pero se encontraron {matches.Length}.");
                }

                ModelImporterClipAnimation source = matches[0];
                float effectiveFrames = source.lastFrame - source.firstFrame;
                if (Mathf.Abs(effectiveFrames - requirement.ExpectedFrames) > FrameTolerance)
                {
                    throw new InvalidOperationException(
                        $"{requirement.Name} contiene {effectiveFrames:0.###} frames efectivos; " +
                        $"se esperaban {requirement.ExpectedFrames:0.###}.");
                }

                definitions.Add(CreateClipDefinition(requirement, source));
            }

            importer.animationType = ModelImporterAnimationType.Generic;
            importer.avatarSetup = ModelImporterAvatarSetup.CreateFromThisModel;
            importer.importAnimation = true;
            importer.animationCompression = ModelImporterAnimationCompression.Off;
            importer.resampleCurves = true;
            importer.importCameras = false;
            importer.importLights = false;
            importer.clipAnimations = definitions.ToArray();
            importer.SaveAndReimport();

            importer = AssetImporter.GetAtPath(fbxPath) as ModelImporter;
            if (importer == null
                || importer.animationType != ModelImporterAnimationType.Generic
                || importer.avatarSetup != ModelImporterAvatarSetup.CreateFromThisModel
                || !importer.importAnimation
                || importer.animationCompression != ModelImporterAnimationCompression.Off
                || !importer.resampleCurves
                || importer.importCameras
                || importer.importLights)
            {
                throw new InvalidOperationException("El ModelImporter v7 no conservó la configuración requerida.");
            }

            ModelImporterClipAnimation[] importedDefinitions = importer.clipAnimations;
            AnimationClip[] allClips = AssetDatabase.LoadAllAssetsAtPath(fbxPath)
                .OfType<AnimationClip>()
                .ToArray();
            AnimationClip[] publicClips = allClips
                .Where(clip => !IsPreviewName(clip.name))
                .ToArray();

            bool definitionsValid = importedDefinitions != null
                && importedDefinitions.Length == ClipRequirements.Length
                && ClipRequirements.All(requirement => importedDefinitions.Count(definition =>
                    definition.name == requirement.Name
                    && definition.loopTime == requirement.Loop
                    && definition.loopPose == requirement.Loop
                    && definition.lockRootRotation
                    && definition.lockRootHeightY
                    && definition.lockRootPositionXZ
                    && definition.keepOriginalOrientation
                    && definition.keepOriginalPositionY
                    && definition.keepOriginalPositionXZ) == 1);
            bool clipsValid = publicClips.Length == ClipRequirements.Length
                && !publicClips.Any(clip => HasNumberedDuplicateName(clip.name))
                && ClipRequirements.All(requirement =>
                    publicClips.Count(clip => clip.name == requirement.Name) == 1);
            if (!definitionsValid || !clipsValid)
            {
                throw new InvalidOperationException(
                    "La reimportación no produjo exactamente los cinco clips públicos requeridos sin duplicados.");
            }

            var result = publicClips.ToDictionary(clip => clip.name, StringComparer.Ordinal);
            foreach (ClipRequirement requirement in ClipRequirements)
            {
                AnimationClip clip = result[requirement.Name];
                float effectiveFrames = clip.length * clip.frameRate;
                if (Mathf.Abs(effectiveFrames - requirement.ExpectedFrames) > FrameTolerance)
                {
                    throw new InvalidOperationException(
                        $"El clip importado {requirement.Name} contiene {effectiveFrames:0.###} frames efectivos; " +
                        $"se esperaban {requirement.ExpectedFrames:0.###}.");
                }
            }

            AnimationClip runClip = result["Qusap_Run"];
            if (Mathf.Abs(runClip.length - RunDurationSeconds) > DurationTolerance)
            {
                throw new InvalidOperationException(
                    $"Qusap_Run dura {runClip.length:0.######} segundos; se esperaban {RunDurationSeconds:0.00}.");
            }

            return result;
        }

        private static ModelImporterClipAnimation CreateClipDefinition(
            ClipRequirement requirement,
            ModelImporterClipAnimation source)
        {
            return new ModelImporterClipAnimation
            {
                name = requirement.Name,
                takeName = !string.IsNullOrWhiteSpace(source.takeName) ? source.takeName : source.name,
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
            };
        }

        private static bool IsExactTakeName(string candidate, string required)
        {
            if (string.IsNullOrWhiteSpace(candidate))
            {
                return false;
            }

            int separator = candidate.LastIndexOf('|');
            string segment = separator >= 0 ? candidate.Substring(separator + 1) : candidate;
            return string.Equals(segment.Trim(), required, StringComparison.OrdinalIgnoreCase);
        }

        private static bool IsPreviewName(string candidate)
        {
            if (string.IsNullOrWhiteSpace(candidate))
            {
                return false;
            }

            int separator = candidate.LastIndexOf('|');
            string segment = separator >= 0 ? candidate.Substring(separator + 1) : candidate;
            return segment.Trim().TrimStart('_')
                .StartsWith("preview", StringComparison.OrdinalIgnoreCase);
        }

        private static bool HasNumberedDuplicateName(string candidate)
        {
            return !string.IsNullOrWhiteSpace(candidate)
                && candidate.IndexOf("(1)", StringComparison.Ordinal) >= 0;
        }

        private static SceneContext ResolveSceneContext(Scene scene)
        {
            QusapAnimationDriver[] drivers = scene.GetRootGameObjects()
                .SelectMany(root => root.GetComponentsInChildren<QusapAnimationDriver>(false))
                .Where(driver => driver != null && driver.isActiveAndEnabled)
                .ToArray();
            if (drivers.Length != 1)
            {
                throw new InvalidOperationException(
                    $"La escena GroundAligned debe contener un único QusapAnimationDriver activo; se encontraron {drivers.Length}.");
            }

            QusapAnimationDriver driver = drivers[0];
            Transform playerRoot = driver.transform;
            if (playerRoot.GetComponent<Rigidbody>() == null)
            {
                throw new InvalidOperationException("El objeto de QusapAnimationDriver no conserva su Rigidbody.");
            }

            Animator[] animators = playerRoot.GetComponentsInChildren<Animator>(false)
                .Where(animator => animator.enabled && animator.gameObject.activeInHierarchy)
                .ToArray();
            if (animators.Length != 1)
            {
                throw new InvalidOperationException(
                    $"Se esperaba un único Animator activo debajo del jugador; se encontraron {animators.Length}.");
            }

            Animator animator = animators[0];
            Transform visualRoot = animator.transform;
            while (visualRoot != null && visualRoot.parent != playerRoot)
            {
                visualRoot = visualRoot.parent;
            }

            if (visualRoot == null
                || visualRoot.parent != playerRoot
                || !visualRoot.gameObject.activeInHierarchy)
            {
                throw new InvalidOperationException("No se pudo resolver la raíz visual activa del jugador.");
            }

            if (visualRoot.GetComponent<Animator>() != animator)
            {
                throw new InvalidOperationException(
                    "QusapAnimationDriver requiere que el Animator esté en la raíz visual directa.");
            }

            return new SceneContext(driver, playerRoot, visualRoot, animator);
        }

        private static Dictionary<string, AnimatorState> ValidateFiveStates(
            AnimatorController controller)
        {
            if (controller == null)
            {
                throw new InvalidOperationException("El Animator Controller es nulo.");
            }

            AnimatorState[] states = controller.layers
                .SelectMany(layer => EnumerateStates(layer.stateMachine))
                .ToArray();
            if (states.Length != ClipRequirements.Length
                || ClipRequirements.Any(requirement =>
                    states.Count(state => state.name == requirement.Name) != 1))
            {
                throw new InvalidOperationException(
                    "El Animator Controller debe contener exactamente los cinco estados de locomoción sin duplicados.");
            }

            return states.ToDictionary(state => state.name, StringComparer.Ordinal);
        }

        private static IEnumerable<AnimatorState> EnumerateStates(AnimatorStateMachine stateMachine)
        {
            foreach (ChildAnimatorState child in stateMachine.states)
            {
                yield return child.state;
            }

            foreach (ChildAnimatorStateMachine childMachine in stateMachine.stateMachines)
            {
                foreach (AnimatorState state in EnumerateStates(childMachine.stateMachine))
                {
                    yield return state;
                }
            }
        }

        private static void ValidateDriverParameters(AnimatorController controller)
        {
            Dictionary<string, AnimatorControllerParameter> parameters = controller.parameters
                .ToDictionary(parameter => parameter.name, StringComparer.Ordinal);
            if (parameters.Count != 3
                || !HasParameter(parameters, "Speed", AnimatorControllerParameterType.Float)
                || !HasParameter(parameters, "VerticalSpeed", AnimatorControllerParameterType.Float)
                || !HasParameter(parameters, "Grounded", AnimatorControllerParameterType.Bool)
                || !parameters["Grounded"].defaultBool)
            {
                throw new InvalidOperationException(
                    "El controller no conserva Speed, VerticalSpeed y Grounded con la configuración requerida.");
            }
        }

        private static bool HasParameter(
            IReadOnlyDictionary<string, AnimatorControllerParameter> parameters,
            string name,
            AnimatorControllerParameterType type)
        {
            return parameters.TryGetValue(name, out AnimatorControllerParameter parameter)
                && parameter.type == type;
        }

        private static string BuildControllerSignature(AnimatorController controller)
        {
            var builder = new StringBuilder();
            foreach (AnimatorControllerParameter parameter in controller.parameters)
            {
                builder.Append("P|").Append(parameter.name).Append('|').Append((int)parameter.type)
                    .Append('|').Append(FloatText(parameter.defaultFloat))
                    .Append('|').Append(parameter.defaultInt)
                    .Append('|').Append(parameter.defaultBool).AppendLine();
            }

            for (int layerIndex = 0; layerIndex < controller.layers.Length; layerIndex++)
            {
                AnimatorControllerLayer layer = controller.layers[layerIndex];
                builder.Append("L|").Append(layerIndex).Append('|').Append(layer.name)
                    .Append('|').Append(FloatText(layer.defaultWeight))
                    .Append('|').Append((int)layer.blendingMode)
                    .Append('|').Append(layer.iKPass)
                    .Append('|').Append(layer.syncedLayerIndex).AppendLine();
                AppendStateMachineSignature(builder, layer.stateMachine, layer.name);
            }

            return builder.ToString();
        }

        private static void AppendStateMachineSignature(
            StringBuilder builder,
            AnimatorStateMachine machine,
            string path)
        {
            builder.Append("SM|").Append(path).Append("|D|")
                .Append(machine.defaultState != null ? machine.defaultState.name : "null")
                .AppendLine();

            foreach (ChildAnimatorState child in machine.states)
            {
                AnimatorState state = child.state;
                string statePath = path + "/" + state.name;
                builder.Append("S|").Append(statePath)
                    .Append('|').Append(FloatText(state.speed))
                    .Append('|').Append(FloatText(state.cycleOffset))
                    .Append('|').Append(state.mirror)
                    .Append('|').Append(state.iKOnFeet)
                    .Append('|').Append(state.writeDefaultValues)
                    .Append('|').Append(state.tag).AppendLine();
                foreach (AnimatorStateTransition transition in state.transitions)
                {
                    AppendTransitionSignature(builder, "T|" + statePath, transition);
                }
            }

            foreach (AnimatorStateTransition transition in machine.anyStateTransitions)
            {
                AppendTransitionSignature(builder, "A|" + path, transition);
            }

            foreach (ChildAnimatorStateMachine child in machine.stateMachines)
            {
                AppendStateMachineSignature(
                    builder,
                    child.stateMachine,
                    path + "/" + child.stateMachine.name);
            }
        }

        private static void AppendTransitionSignature(
            StringBuilder builder,
            string prefix,
            AnimatorStateTransition transition)
        {
            builder.Append(prefix)
                .Append("|DST|").Append(transition.destinationState != null
                    ? transition.destinationState.name
                    : transition.destinationStateMachine != null
                        ? transition.destinationStateMachine.name
                        : transition.isExit ? "EXIT" : "null")
                .Append('|').Append(transition.hasExitTime)
                .Append('|').Append(FloatText(transition.exitTime))
                .Append('|').Append(FloatText(transition.duration))
                .Append('|').Append(FloatText(transition.offset))
                .Append('|').Append(transition.hasFixedDuration)
                .Append('|').Append(transition.canTransitionToSelf)
                .Append('|').Append((int)transition.interruptionSource)
                .Append('|').Append(transition.orderedInterruption)
                .Append('|').Append(transition.mute)
                .Append('|').Append(transition.solo).AppendLine();
            foreach (AnimatorCondition condition in transition.conditions)
            {
                builder.Append("C|").Append(condition.parameter)
                    .Append('|').Append((int)condition.mode)
                    .Append('|').Append(FloatText(condition.threshold)).AppendLine();
            }
        }

        private static string FloatText(float value)
        {
            return value.ToString("R", CultureInfo.InvariantCulture);
        }

        private static float MeasureSoleHeight(Transform visualRoot, AnimationClip idleClip)
        {
            SkinnedMeshRenderer[] activeRenderers = visualRoot
                .GetComponentsInChildren<SkinnedMeshRenderer>(false)
                .Where(renderer => renderer.enabled && renderer.gameObject.activeInHierarchy)
                .ToArray();
            SkinnedMeshRenderer left = FindFootRenderer(activeRenderers, "FloatingFoot_L");
            SkinnedMeshRenderer right = FindFootRenderer(activeRenderers, "FloatingFoot_R");

            Vector3 expectedPosition = visualRoot.localPosition;
            Quaternion expectedRotation = visualRoot.localRotation;
            Vector3 expectedScale = visualRoot.localScale;
            bool sampling = false;
            AnimationMode.StartAnimationMode();
            try
            {
                AnimationMode.BeginSampling();
                sampling = true;
                AnimationMode.SampleAnimationClip(visualRoot.gameObject, idleClip, 0f);
                AnimationMode.EndSampling();
                sampling = false;

                if (visualRoot.localPosition != expectedPosition
                    || visualRoot.localRotation != expectedRotation
                    || visualRoot.localScale != expectedScale)
                {
                    throw new InvalidOperationException(
                        "Qusap_Idle intentó alterar la transformación raíz GroundAligned durante el muestreo.");
                }

                return Mathf.Min(BakeLowestWorldY(left), BakeLowestWorldY(right));
            }
            finally
            {
                if (sampling)
                {
                    AnimationMode.EndSampling();
                }

                if (AnimationMode.InAnimationMode())
                {
                    AnimationMode.StopAnimationMode();
                }
            }
        }

        private static SkinnedMeshRenderer FindFootRenderer(
            IEnumerable<SkinnedMeshRenderer> renderers,
            string footName)
        {
            SkinnedMeshRenderer[] matches = renderers.Where(renderer =>
                IsFootName(renderer.name, footName)
                || (renderer.sharedMesh != null && IsFootName(renderer.sharedMesh.name, footName)))
                .ToArray();
            if (matches.Length != 1)
            {
                throw new InvalidOperationException(
                    $"Se esperaba un renderer único para {footName}; se encontraron {matches.Length}.");
            }

            return matches[0];
        }

        private static bool IsFootName(string candidate, string footName)
        {
            return string.Equals(candidate, footName, StringComparison.Ordinal)
                || string.Equals(candidate, footName + "_Mesh", StringComparison.Ordinal);
        }

        private static float BakeLowestWorldY(SkinnedMeshRenderer renderer)
        {
            Mesh mesh = new Mesh { hideFlags = HideFlags.HideAndDontSave };
            try
            {
                renderer.BakeMesh(mesh, false);
                var vertices = new List<Vector3>(mesh.vertexCount);
                mesh.GetVertices(vertices);
                if (vertices.Count == 0)
                {
                    throw new InvalidOperationException(
                        $"El renderer '{renderer.name}' no produjo vértices al hornear su pose.");
                }

                float lowestY = float.PositiveInfinity;
                foreach (Vector3 vertex in vertices)
                {
                    lowestY = Mathf.Min(lowestY, renderer.transform.TransformPoint(vertex).y);
                }

                return lowestY;
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(mesh);
            }
        }

        private static void ValidateSceneResult(
            SceneContext previousContext,
            Transform newVisual,
            Animator newAnimator,
            AnimatorController targetController,
            IReadOnlyDictionary<string, AnimatorState> targetStates,
            Vector3 preservedPosition,
            Quaternion preservedRotation,
            Vector3 preservedScale,
            float previousSoleHeight,
            float v7SoleHeight)
        {
            Transform resolvedVisual = previousContext.Driver.transform.Find(ActiveVisualName);
            if (resolvedVisual != newVisual
                || resolvedVisual.GetComponent<Animator>() != newAnimator
                || !resolvedVisual.gameObject.activeInHierarchy
                || newAnimator.runtimeAnimatorController != targetController
                || newAnimator.applyRootMotion
                || newAnimator.cullingMode != AnimatorCullingMode.AlwaysAnimate
                || newVisual.localPosition != preservedPosition
                || newVisual.localRotation != preservedRotation
                || newVisual.localScale != preservedScale
                || previousContext.VisualRoot.name != BackupVisualName
                || previousContext.VisualRoot.gameObject.activeSelf)
            {
                throw new InvalidOperationException(
                    "La sustitución visual v7 no superó la validación estructural o de transformación.");
            }

            if (Mathf.Abs(v7SoleHeight - previousSoleHeight) > SoleHeightTolerance)
            {
                throw new InvalidOperationException(
                    $"Las suelas v7 cambiaron {v7SoleHeight - previousSoleHeight:0.######} unidades respecto a GroundAligned. " +
                    "No se recalculó ni alteró localPosition.y.");
            }

            if (targetStates.Count != ClipRequirements.Length
                || targetStates.Values.Any(state => state.motion == null))
            {
                throw new InvalidOperationException(
                    "El controller nuevo no conserva exactamente cinco estados con Motion.");
            }
        }

        private static void ValidateExistingFbx(string assetPath)
        {
            if (assetPath != FbxAssetPath || !File.Exists(ExternalFbxPath))
            {
                throw new InvalidOperationException(
                    "No se puede verificar el FBX v7 en su ruta exacta contra el archivo externo.");
            }

            using (SHA256 sha = SHA256.Create())
            using (FileStream external = File.OpenRead(ExternalFbxPath))
            using (FileStream existing = File.OpenRead(AbsoluteAssetPath(assetPath)))
            {
                if (!sha.ComputeHash(external).SequenceEqual(sha.ComputeHash(existing)))
                {
                    throw new InvalidOperationException(
                        "El FBX v7 existente no coincide con el externo. No se reemplazará ni se creará un duplicado.");
                }
            }
        }

        private static bool ValidateTestDestination(string path, string exactName, Type expectedType)
        {
            UnityEngine.Object asset = AssetDatabase.LoadMainAssetAtPath(path);
            if (!path.StartsWith("Assets/", StringComparison.Ordinal)
                || Path.GetFileName(path) != exactName
                || (asset != null && !expectedType.IsInstanceOfType(asset))
                || (File.Exists(AbsoluteAssetPath(path)) && asset == null))
            {
                ShowError($"El destino no es un recurso de prueba reconocido y no será modificado:\n{path}");
                return false;
            }

            // These two exact reserved outputs belong to this tool. Never use fuzzy matches,
            // DeleteAsset, or a numbered destination when recovering an interrupted run.
            return true;
        }

        private static void RebuildTestAsset(string source, string destination, string exactName)
        {
            string sourceFullPath = AbsoluteAssetPath(source);
            string destinationFullPath = AbsoluteAssetPath(destination);
            if (Path.GetFileName(destination) != exactName
                || string.Equals(sourceFullPath, destinationFullPath, StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException("La reconstrucción no puede sobrescribir un asset original.");
            }

            if (File.Exists(destinationFullPath))
            {
                // Overwrite only the serialized content of the dedicated test asset.
                // Keep its existing .meta (and GUID) byte-for-byte; never copy a source .meta.
                File.Copy(sourceFullPath, destinationFullPath, true);
            }
            else if (!AssetDatabase.CopyAsset(source, destination))
            {
                throw new InvalidOperationException($"Unity no pudo crear la copia de prueba '{destination}'.");
            }

            AssetDatabase.ImportAsset(destination,
                ImportAssetOptions.ForceUpdate | ImportAssetOptions.ForceSynchronousImport);
        }

        private static Scene RebuildTestScene(string source, string destination)
        {
            for (int index = 0; index < SceneManager.sceneCount; index++)
            {
                Scene openScene = SceneManager.GetSceneAt(index);
                if (openScene.path != destination && openScene.isDirty)
                {
                    throw new InvalidOperationException(
                        $"La escena '{openScene.name}' tiene cambios sin guardar. " +
                        "Guárdalos o descártalos manualmente antes de reconstruir la prueba.");
                }
            }

            Scene loadedTest = SceneManager.GetSceneByPath(destination);
            Scene temporaryScene = default;
            try
            {
                if (loadedTest.IsValid() && loadedTest.isLoaded)
                {
                    if (SceneManager.sceneCount == 1)
                    {
                        temporaryScene = EditorSceneManager.NewScene(
                            NewSceneSetup.EmptyScene, NewSceneMode.Additive);
                    }

                    // Only the reserved partial test can have unsaved content discarded.
                    // The dirty-scene check above protects all original scenes.
                    if (!EditorSceneManager.CloseScene(loadedTest, true))
                    {
                        throw new InvalidOperationException("No se pudo cerrar la copia RunV7 parcial.");
                    }
                }

                RebuildTestAsset(source, destination, TargetSceneFileName);
                // Open only the test to avoid running two copies of the physical player.
                return EditorSceneManager.OpenScene(destination, OpenSceneMode.Single);
            }
            finally
            {
                if (temporaryScene.IsValid() && temporaryScene.isLoaded && SceneManager.sceneCount > 1)
                {
                    EditorSceneManager.CloseScene(temporaryScene, true);
                }
            }
        }

        private static string AbsoluteAssetPath(string path)
        {
            return Path.GetFullPath(Path.Combine(Directory.GetParent(Application.dataPath).FullName, path));
        }

        private static string CombineAssetPath(string directory, string fileName)
        {
            return (directory + "/" + fileName).Replace('\\', '/');
        }

        private static string FormatVector(Vector3 value)
        {
            return $"({value.x:0.######}, {value.y:0.######}, {value.z:0.######})";
        }

        private static void ShowError(string message)
        {
            Debug.LogError(message);
            EditorUtility.DisplayDialog(DialogTitle, message, "Aceptar");
        }

        private sealed class ClipRequirement
        {
            public ClipRequirement(string name, float expectedFrames, bool loop)
            {
                Name = name;
                ExpectedFrames = expectedFrames;
                Loop = loop;
            }

            public string Name { get; }
            public float ExpectedFrames { get; }
            public bool Loop { get; }
        }

        private sealed class SceneContext
        {
            public SceneContext(
                QusapAnimationDriver driver,
                Transform playerRoot,
                Transform visualRoot,
                Animator animator)
            {
                Driver = driver;
                PlayerRoot = playerRoot;
                VisualRoot = visualRoot;
                Animator = animator;
            }

            public QusapAnimationDriver Driver { get; }
            public Transform PlayerRoot { get; }
            public Transform VisualRoot { get; }
            public Animator Animator { get; }
        }
    }
}
#endif
