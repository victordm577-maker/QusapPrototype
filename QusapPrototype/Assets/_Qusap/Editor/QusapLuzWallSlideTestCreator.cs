#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using Qusap;
using UnityEditor;
using UnityEditor.Animations;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Qusap.EditorTools
{
    public static class QusapLuzWallSlideTestCreator
    {
        private const string MenuPath = "Tools/Qusap/Create Qusap Luz Wall Slide Test";
        private const string FbxFileName = "Qusap_Luz_Locomotion_v10.fbx";
        private const string FbxAssetPath =
            "Assets/Scenes/Art/Characters/Qusap/Final_Light_v3/" + FbxFileName;
        private const string ExternalFbxPath =
            @"C:\Users\victo\Documents\Qusap\3D\Modelado\Qusap_Luz_Definitivo\03_Export_Unity\Qusap_Luz_Locomotion_v10.fbx";
        private const string SourceSceneFileName = "Qusap_Luz_Locomotion_RunV9Test_v1.unity";
        private const string SourceFbxAssetPath =
            "Assets/Scenes/Art/Characters/Qusap/Final_Light_v3/Qusap_Luz_Locomotion_v9.fbx";
        private const string TargetScenePath = "Assets/_Qusap/Scenes/" + TargetSceneFileName;
        private const string TargetControllerPath =
            "Assets/Scenes/Art/Characters/Qusap/Final_Light_v3/" + TargetControllerFileName;
        private const string TargetSceneFileName = "Qusap_Luz_Locomotion_WallSlideTest_v1.unity";
        private const string TargetControllerFileName = "Qusap_Luz_Animator_WallSlideTest_v1.controller";
        private const string ActiveVisualName = "PlayerVisual";
        private const string BackupVisualName = "PlayerVisual_RunV9_Backup";
        private const string DialogTitle = "Qusap Luz Wall Slide Test";
        private const string CompletionPath = "Library/QusapWallSlideTest.complete";
        private const float FrameTolerance = 0.01f;
        private const float SoleHeightTolerance = 0.01f;

        private static readonly ClipRequirement[] ClipRequirements =
        {
            new("Qusap_Idle", 120f, true),
            new("Qusap_Run", 36f, true),
            new("Qusap_JumpRise", 18f, false),
            new("Qusap_Fall", 24f, true),
            new("Qusap_Land", 18f, false),
            new("Qusap_WallSlide", 48f, true)
        };

        [MenuItem(MenuPath)]
        private static void CreateWallSlideTest()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode || EditorApplication.isCompiling || EditorApplication.isUpdating)
            {
                ShowError("La prueba no puede crearse mientras Unity está entrando o se encuentra en Play Mode.");
                return;
            }

            if (AnimationMode.InAnimationMode())
            {
                ShowError("Sal de Animation Mode antes de crear la prueba Wall Slide.");
                return;
            }

            for (int index = 0; index < SceneManager.sceneCount; index++)
            {
                Scene openScene = SceneManager.GetSceneAt(index);
                if (openScene.isDirty && openScene.path != TargetScenePath)
                {
                    ShowError($"Guarda o descarta manualmente los cambios de '{openScene.name}' antes de crear v10.");
                    return;
                }
            }

            if (!TryFindExactFbx(out string fbxPath, out ModelImporter importer, out string fbxError))
            {
                ShowError(fbxError);
                return;
            }

            if (!TryFindRunV9Scene(out string sourceScenePath, out string sceneError))
            {
                ShowError(sceneError);
                return;
            }

            string targetScenePath = TargetScenePath;
            string targetControllerPath = TargetControllerPath;

            try
            {
                if (!ValidateTestDestination(targetScenePath, TargetSceneFileName, typeof(SceneAsset))
                    || !ValidateTestDestination(
                        targetControllerPath, TargetControllerFileName, typeof(AnimatorController)))
                    return;
                if (File.Exists(AbsoluteAssetPath(CompletionPath))
                    && AssetDatabase.LoadAssetAtPath<SceneAsset>(targetScenePath) != null
                    && AssetDatabase.LoadAssetAtPath<AnimatorController>(targetControllerPath) != null)
                {
                    Selection.activeObject = AssetDatabase.LoadAssetAtPath<SceneAsset>(targetScenePath);
                    EditorGUIUtility.PingObject(Selection.activeObject);
                    EditorUtility.DisplayDialog(DialogTitle,
                        "La prueba v10 ya fue creada. Abre la escena seleccionada en Project.\n" + targetScenePath,
                        "Aceptar");
                    return;
                }
                if (File.Exists(AbsoluteAssetPath(CompletionPath)))
                    File.Delete(AbsoluteAssetPath(CompletionPath));
                ValidateExistingFbx(fbxPath);
                Dictionary<string, AnimationClip> v10Clips =
                    ConfigureImporterAndLoadClips(fbxPath, importer);
                GameObject v10Model = AssetDatabase.LoadAssetAtPath<GameObject>(fbxPath);
                if (v10Model == null)
                {
                    throw new InvalidOperationException($"No se pudo cargar el modelo v10 desde '{fbxPath}'.");
                }

                Scene testScene = RebuildTestScene(sourceScenePath, targetScenePath);
                SceneManager.SetActiveScene(testScene);

                SceneContext context = ResolveSceneContext(testScene);
                Dictionary<Component, string> protectedComponents = CaptureProtectedComponents(testScene, context.VisualRoot);
                AnimatorController sourceController =
                    context.Animator.runtimeAnimatorController as AnimatorController;
                if (sourceController == null)
                {
                    throw new InvalidOperationException(
                        "El visual RunV9Test no utiliza directamente un AnimatorController copiable.");
                }

                Dictionary<string, AnimatorState> sourceStates = ValidateStates(sourceController, false);
                ValidateDriverParameters(sourceController);
                string sourceControllerPath = AssetDatabase.GetAssetPath(sourceController);
                if (sourceControllerPath == TargetControllerPath)
                    throw new InvalidOperationException("El controller origen no puede ser el destino v10.");
                if (sourceStates.Values.Any(state => AssetDatabase.GetAssetPath(state.motion) != SourceFbxAssetPath))
                    throw new InvalidOperationException("Los cinco Motion del controller origen deben usar el FBX v9.");
                string sourceSignature = BuildControllerSignature(sourceController);
                AnimationClip previousIdle = sourceStates["Qusap_Idle"].motion as AnimationClip;
                if (previousIdle == null)
                {
                    throw new InvalidOperationException(
                        "El estado Qusap_Idle del controller RunV9Test no contiene un AnimationClip.");
                }

                Vector2 previousSoleHeight = MeasureSoleHeight(
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
                        "La copia inicial del Animator Controller no conserva exactamente la estructura RunV9Test.");
                }

                Dictionary<string, AnimatorState> targetStates = ValidateStates(targetController, false);
                foreach (ClipRequirement requirement in ClipRequirements.Take(5))
                {
                    AnimatorState state = targetStates[requirement.Name];
                    state.motion = v10Clips[requirement.Name];
                    EditorUtility.SetDirty(state);
                }

                AddWallSlide(targetController, targetStates, v10Clips["Qusap_WallSlide"]);
                ValidateWallSlideController(targetController, v10Clips);
                string expectedSignature = BuildControllerSignature(targetController);

                EditorUtility.SetDirty(targetController);
                AssetDatabase.SaveAssetIfDirty(targetController);

                targetController = AssetDatabase.LoadAssetAtPath<AnimatorController>(targetControllerPath);
                targetStates = ValidateStates(targetController, true);
                ValidateDriverParameters(targetController);
                ValidateWallSlideController(targetController, v10Clips);
                if (BuildControllerSignature(targetController) != expectedSignature
                    || BuildControllerSignature(sourceController) != sourceSignature
                    || ClipRequirements.Any(requirement =>
                        targetStates[requirement.Name].motion != v10Clips[requirement.Name]))
                {
                    throw new InvalidOperationException(
                        "El controller no guardó la extensión WallSlide o cambió el controller original.");
                }

                Vector3 preservedPosition = context.VisualRoot.localPosition;
                Quaternion preservedRotation = context.VisualRoot.localRotation;
                Vector3 preservedScale = context.VisualRoot.localScale;
                if (context.PlayerRoot.Find(BackupVisualName) != null)
                {
                    throw new InvalidOperationException(
                        $"Ya existe un respaldo llamado '{BackupVisualName}' en la copia de escena.");
                }

                Undo.SetCurrentGroupName("Create Qusap Luz Wall Slide Test");
                int undoGroup = Undo.GetCurrentGroup();
                Undo.RecordObject(context.VisualRoot.gameObject, "Back up Run v9 visual");
                context.VisualRoot.name = BackupVisualName;
                context.VisualRoot.gameObject.SetActive(false);
                PrefabUtility.RecordPrefabInstancePropertyModifications(context.VisualRoot.gameObject);

                GameObject newVisual =
                    PrefabUtility.InstantiatePrefab(v10Model, context.PlayerRoot) as GameObject;
                if (newVisual == null)
                {
                    throw new InvalidOperationException("Unity no pudo instanciar Qusap Luz Locomotion v10.");
                }

                Undo.RegisterCreatedObjectUndo(newVisual, "Create Qusap Luz v10 PlayerVisual");
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

                Undo.RecordObject(newAnimator, "Configure Wall Slide Animator");
                newAnimator.enabled = true;
                newAnimator.runtimeAnimatorController = targetController;
                newAnimator.applyRootMotion = false;
                newAnimator.cullingMode = AnimatorCullingMode.AlwaysAnimate;
                PrefabUtility.RecordPrefabInstancePropertyModifications(newVisual);
                PrefabUtility.RecordPrefabInstancePropertyModifications(newVisualTransform);
                PrefabUtility.RecordPrefabInstancePropertyModifications(newAnimator);

                ValidateInPlaceClips(newVisualTransform, v10Clips);
                Vector2 v10SoleHeight = MeasureSoleHeight(
                    newVisualTransform,
                    v10Clips["Qusap_Idle"]);
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
                    v10SoleHeight);

                ValidateProtectedComponents(protectedComponents);
                EditorSceneManager.MarkSceneDirty(testScene);
                if (!EditorSceneManager.SaveScene(testScene))
                {
                    throw new InvalidOperationException(
                        $"Unity no pudo guardar la escena Wall Slide '{targetScenePath}'.");
                }

                Undo.CollapseUndoOperations(undoGroup);
                AssetDatabase.SaveAssetIfDirty(targetController);
                Selection.activeGameObject = newVisual;
                // Only incomplete runs may discard the reserved partial scene on retry.
                File.WriteAllText(AbsoluteAssetPath(CompletionPath), targetScenePath);

                string report =
                    $"Escena creada: {targetScenePath}\n" +
                    $"Controller creado: {targetControllerPath}\n" +
                    $"localPosition preservada: {FormatVector(preservedPosition)}\n" +
                    $"Diferencia maxima de altura de suelas: {MaxSoleDifference(previousSoleHeight, v10SoleHeight):0.######}\n" +
                    "6 estados con Motion v10; WallSliding conectado a QusapVerticalMotor; sin Root Motion.\n" +
                    "Speed, VerticalSpeed y Grounded conservados. Fall/JumpRise bloqueados durante WallSlide.\n" +
                    "Validación en editor completada. En Play Mode comprueba ambas paredes, soltar -> Fall, " +
                    "Wall Jump -> JumpRise, piso -> Land y la orientación de las suelas.";
                Debug.Log(report, newVisual);
                EditorUtility.DisplayDialog(DialogTitle, report, "Aceptar");
            }
            catch (Exception exception)
            {
                Debug.LogException(exception);
                ShowError(
                    "No se pudo completar la prueba Wall Slide. Los assets originales permanecen intactos; " +
                    "revisa la copia parcial y la consola.\n\n" + exception.Message);
            }
        }

        private static bool TryFindExactFbx(
            out string fbxPath,
            out ModelImporter importer,
            out string error)
        {
            fbxPath = FbxAssetPath;
            importer = null;
            error = null;
            try
            {
                string destination = AbsoluteAssetPath(FbxAssetPath);
                if (!File.Exists(destination))
                {
                    if (File.Exists(destination + ".meta"))
                        throw new InvalidOperationException("Existe un .meta huerfano para v10. No se reemplazara.");
                    // Stage outside Assets: never leave a truncated FBX after an interruption.
                    string staging = AbsoluteAssetPath("Library/QusapWallSlideFbx-" + Guid.NewGuid() + ".tmp");
                    try
                    {
                        File.Copy(ExternalFbxPath, staging, false);
                        File.Move(staging, destination);
                    }
                    finally
                    {
                        if (File.Exists(staging)) File.Delete(staging);
                    }
                }
                ValidateExistingFbx(fbxPath);
                AssetDatabase.ImportAsset(fbxPath, ImportAssetOptions.ForceSynchronousImport);
                importer = AssetImporter.GetAtPath(fbxPath) as ModelImporter;
                if (importer == null)
                    throw new InvalidOperationException($"El asset '{fbxPath}' no utiliza ModelImporter.");
                return true;
            }
            catch (Exception exception)
            {
                error = exception.Message;
                return false;
            }
        }

        private static bool TryFindRunV9Scene(
            out string scenePath,
            out string error)
        {
            string[] candidates = AssetDatabase.FindAssets("RunV9Test t:Scene")
                .Select(AssetDatabase.GUIDToAssetPath)
                .Where(path => path.StartsWith("Assets/", StringComparison.Ordinal)
                    && Path.GetFileName(path) == SourceSceneFileName)
                .ToArray();
            scenePath = candidates.Length == 1 ? candidates[0] : null;
            error = scenePath == null
                ? $"Se requiere una unica escena '{SourceSceneFileName}'; encontradas: {candidates.Length}."
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
                .Where(take => take != null
                    && !IsPreviewName(take.name) && !IsPreviewName(take.takeName)
                    && !HasNumberedDuplicateName(take.name)
                    && !HasNumberedDuplicateName(take.takeName))
                .GroupBy(take => new { take.name, take.takeName, take.firstFrame, take.lastFrame })
                .Select(group => group.First())
                .ToArray();

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
            importer.motionNodeName = string.Empty;
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
                || !string.IsNullOrEmpty(importer.motionNodeName)
                || importer.importCameras
                || importer.importLights)
            {
                throw new InvalidOperationException("El ModelImporter v10 no conservó la configuración requerida.");
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
                    && Mathf.Abs(definition.lastFrame - definition.firstFrame - requirement.ExpectedFrames) <= FrameTolerance
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
                    "La reimportación no produjo exactamente los seis clips públicos requeridos sin duplicados.");
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
                    $"La escena RunV9Test debe contener un único QusapAnimationDriver activo; se encontraron {drivers.Length}.");
            }

            QusapAnimationDriver driver = drivers[0];
            Transform playerRoot = driver.transform;
            if (playerRoot.GetComponent<Rigidbody>() == null
                || playerRoot.GetComponent<QusapGroundSensor>() == null
                || playerRoot.GetComponent<QusapInputReader>() == null)
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

            if (visualRoot.name != ActiveVisualName
                || playerRoot.Cast<Transform>().Count(child => child.name == ActiveVisualName) != 1
                || PrefabUtility.GetPrefabAssetPathOfNearestInstanceRoot(visualRoot.gameObject) != SourceFbxAssetPath)
                throw new InvalidOperationException("PlayerVisual debe ser la instancia activa unica del FBX v9.");
            var verticalMotor = playerRoot.GetComponent<QusapVerticalMotor>();
            var horizontalMotor = playerRoot.GetComponent<QusapHorizontalMotor>();
            var wallSensor = playerRoot.GetComponent<QusapWallSensor>();
            if (verticalMotor == null || !verticalMotor.isActiveAndEnabled
                || horizontalMotor == null || !horizontalMotor.isActiveAndEnabled
                || wallSensor == null || !wallSensor.isActiveAndEnabled)
                throw new InvalidOperationException(
                    "Falta el proveedor real de Wall Slide: se requieren QusapVerticalMotor, " +
                    "QusapHorizontalMotor y QusapWallSensor activos en el jugador. No se añadirán sensores.");
            return new SceneContext(driver, playerRoot, visualRoot, animator);
        }

        private static Dictionary<string, AnimatorState> ValidateStates(
            AnimatorController controller, bool includeWallSlide)
        {
            if (controller == null)
            {
                throw new InvalidOperationException("El Animator Controller es nulo.");
            }

            AnimatorState[] states = controller.layers
                .SelectMany(layer => EnumerateStates(layer.stateMachine))
                .ToArray();
            int expectedCount = includeWallSlide ? 6 : 5;
            if (controller.layers.Length != 1 || controller.layers[0].stateMachine.stateMachines.Length != 0
                || states.Length != expectedCount
                || ClipRequirements.Take(expectedCount).Any(requirement =>
                    states.Count(state => state.name == requirement.Name) != 1))
            {
                throw new InvalidOperationException(
                    $"El Animator Controller debe contener exactamente {expectedCount} estados en una capa sin duplicados.");
            }

            return states.ToDictionary(state => state.name, StringComparer.Ordinal);
        }

        private static void AddWallSlide(
            AnimatorController controller,
            IReadOnlyDictionary<string, AnimatorState> states,
            AnimationClip wallSlideClip)
        {
            // Always called on a fresh copy of RunV9, including recovery after partial failure.
            if (controller.parameters.Any(parameter => parameter.name == "WallSliding"))
                throw new InvalidOperationException("El controller origen ya contiene WallSliding.");

            AnimatorStateMachine machine = controller.layers[0].stateMachine;
            controller.AddParameter("WallSliding", AnimatorControllerParameterType.Bool);
            AnimatorState wallSlide = machine.AddState("Qusap_WallSlide", new Vector3(770f, 230f, 0f));
            wallSlide.motion = wallSlideClip;
            wallSlide.writeDefaultValues = states["Qusap_Fall"].writeDefaultValues;

            foreach (string airborneState in new[] { "Qusap_Fall", "Qusap_JumpRise" })
            {
                AnimatorStateTransition[] matches = machine.anyStateTransitions
                    .Where(transition => transition.destinationState == states[airborneState]).ToArray();
                if (matches.Length != 1)
                    throw new InvalidOperationException($"Se requiere una única transición Any State -> {airborneState}.");
                matches[0].AddCondition(AnimatorConditionMode.IfNot, 0f, "WallSliding");
                EditorUtility.SetDirty(matches[0]);
            }

            AnimatorStateTransition enter = machine.AddAnyStateTransition(wallSlide);
            ConfigureTransition(enter, 0.04f);
            enter.AddCondition(AnimatorConditionMode.If, 0f, "WallSliding");
            machine.anyStateTransitions = new[] { enter }
                .Concat(machine.anyStateTransitions.Where(transition => transition != enter)).ToArray();

            AnimatorStateTransition fall = wallSlide.AddTransition(states["Qusap_Fall"]);
            ConfigureTransition(fall, 0.04f);
            fall.AddCondition(AnimatorConditionMode.IfNot, 0f, "WallSliding");
            fall.AddCondition(AnimatorConditionMode.IfNot, 0f, "Grounded");
            fall.AddCondition(AnimatorConditionMode.Less, 0f, "VerticalSpeed");

            AnimatorStateTransition rise = wallSlide.AddTransition(states["Qusap_JumpRise"]);
            ConfigureTransition(rise, 0.04f);
            rise.AddCondition(AnimatorConditionMode.IfNot, 0f, "WallSliding");
            rise.AddCondition(AnimatorConditionMode.IfNot, 0f, "Grounded");
            rise.AddCondition(AnimatorConditionMode.Greater, 0.1f, "VerticalSpeed");

            AnimatorStateTransition land = wallSlide.AddTransition(states["Qusap_Land"]);
            ConfigureTransition(land, 0.03f);
            land.AddCondition(AnimatorConditionMode.If, 0f, "Grounded");

            foreach (UnityEngine.Object asset in new UnityEngine.Object[] { machine, wallSlide, enter, fall, rise, land })
                EditorUtility.SetDirty(asset);
        }

        private static void ConfigureTransition(AnimatorStateTransition transition, float duration)
        {
            transition.hasExitTime = false;
            transition.hasFixedDuration = true;
            transition.duration = duration;
            transition.canTransitionToSelf = false;
        }

        private static void ValidateWallSlideController(
            AnimatorController controller, IReadOnlyDictionary<string, AnimationClip> clips)
        {
            Dictionary<string, AnimatorState> states = ValidateStates(controller, true);
            AnimatorControllerParameter[] parameters = controller.parameters
                .Where(parameter => parameter.name == "WallSliding").ToArray();
            if (parameters.Length != 1 || parameters[0].type != AnimatorControllerParameterType.Bool
                || parameters[0].defaultBool
                || ClipRequirements.Any(requirement => states[requirement.Name].motion != clips[requirement.Name]))
                throw new InvalidOperationException("WallSliding debe ser Bool, false por defecto, y los seis Motion deben usar v10.");

            AnimatorState wallSlide = states["Qusap_WallSlide"];
            AnimatorStateMachine machine = controller.layers[0].stateMachine;
            AnimatorStateTransition[] entries = machine.anyStateTransitions
                .Where(transition => transition.destinationState == wallSlide).ToArray();
            if (entries.Length != 1 || machine.anyStateTransitions[0] != entries[0]
                || wallSlide.transitions.Length != 3)
                throw new InvalidOperationException("WallSlide debe tener una entrada prioritaria y tres salidas únicas.");

            ValidateTransition(entries[0], 0.04f, Condition("WallSliding", AnimatorConditionMode.If));
            foreach (string airborneState in new[] { "Qusap_Fall", "Qusap_JumpRise" })
            {
                AnimatorStateTransition[] matches = machine.anyStateTransitions
                    .Where(transition => transition.destinationState == states[airborneState]).ToArray();
                if (matches.Length != 1 || matches[0].conditions.Count(condition =>
                    condition.parameter == "WallSliding" && condition.mode == AnimatorConditionMode.IfNot) != 1)
                    throw new InvalidOperationException($"Any State -> {airborneState} puede interrumpir WallSlide.");
            }

            ValidateTransition(FindExit(wallSlide, states["Qusap_Fall"]), 0.04f,
                Condition("WallSliding", AnimatorConditionMode.IfNot),
                Condition("Grounded", AnimatorConditionMode.IfNot),
                Condition("VerticalSpeed", AnimatorConditionMode.Less));
            ValidateTransition(FindExit(wallSlide, states["Qusap_JumpRise"]), 0.04f,
                Condition("WallSliding", AnimatorConditionMode.IfNot),
                Condition("Grounded", AnimatorConditionMode.IfNot),
                Condition("VerticalSpeed", AnimatorConditionMode.Greater, 0.1f));
            ValidateTransition(FindExit(wallSlide, states["Qusap_Land"]), 0.03f,
                Condition("Grounded", AnimatorConditionMode.If));
        }

        private static AnimatorStateTransition FindExit(AnimatorState source, AnimatorState destination)
        {
            AnimatorStateTransition[] matches = source.transitions
                .Where(transition => transition.destinationState == destination).ToArray();
            if (matches.Length != 1)
                throw new InvalidOperationException($"Se requiere una única salida WallSlide -> {destination.name}.");
            return matches[0];
        }

        private static AnimatorCondition Condition(string parameter, AnimatorConditionMode mode, float threshold = 0f)
        {
            return new AnimatorCondition { parameter = parameter, mode = mode, threshold = threshold };
        }

        private static void ValidateTransition(
            AnimatorStateTransition transition, float duration, params AnimatorCondition[] conditions)
        {
            if (transition.hasExitTime || !transition.hasFixedDuration || transition.canTransitionToSelf
                || !Mathf.Approximately(transition.duration, duration) || transition.mute || transition.solo
                || transition.conditions.Length != conditions.Length
                || conditions.Any(expected => transition.conditions.Count(actual =>
                    actual.parameter == expected.parameter && actual.mode == expected.mode
                    && Mathf.Approximately(actual.threshold, expected.threshold)) != 1))
                throw new InvalidOperationException("Una transición WallSlide no conserva las condiciones o duración requeridas.");
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
            if (!HasParameter(parameters, "Speed", AnimatorControllerParameterType.Float)
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
            // Compare every serialized property, including Entry / Any State, behaviours,
            // layers and transition fields. Ignore only the new asset name and five Motion.
            string controllerPath = AssetDatabase.GetAssetPath(controller);
            var records = new SortedDictionary<long, string>();
            foreach (UnityEngine.Object asset in AssetDatabase.LoadAllAssetsAtPath(controllerPath))
            {
                if (!AssetDatabase.TryGetGUIDAndLocalFileIdentifier(asset, out string _, out long localId))
                    throw new InvalidOperationException("No se pudo identificar un subasset del controller.");
                var builder = new System.Text.StringBuilder(asset.GetType().FullName);
                using (var serialized = new SerializedObject(asset))
                {
                    SerializedProperty property = serialized.GetIterator();
                    bool enterChildren = true;
                    while (property.Next(enterChildren))
                    {
                        enterChildren = true;
                        if ((asset == controller && property.propertyPath == "m_Name")
                            || (asset is AnimatorState && property.propertyPath == "m_Motion"))
                        {
                            enterChildren = false;
                            continue;
                        }
                        builder.Append('|').Append(property.propertyPath).Append(':').Append(property.type);
                        if (property.propertyType == SerializedPropertyType.ObjectReference)
                        {
                            enterChildren = false;
                            UnityEngine.Object reference = property.objectReferenceValue;
                            if (reference == null)
                            {
                                builder.Append("=null");
                                continue;
                            }
                            if (!AssetDatabase.TryGetGUIDAndLocalFileIdentifier(
                                reference, out string guid, out long referenceId))
                                throw new InvalidOperationException("El controller contiene una referencia no persistente.");
                            string owner = AssetDatabase.GetAssetPath(reference) == controllerPath ? "SELF" : guid;
                            builder.Append('=').Append(owner).Append(':').Append(referenceId);
                        }
                        else if (property.propertyType == SerializedPropertyType.ManagedReference)
                        {
                            builder.Append('=').Append(property.managedReferenceFullTypename);
                        }
                        else if (property.propertyType != SerializedPropertyType.Generic)
                        {
                            // Hash values only at leaves: reference identities were normalized above.
                            if (!property.hasChildren)
                                builder.Append('=').Append(property.contentHash);
                        }
                    }
                }
                records.Add(localId, builder.ToString());
            }
            return string.Join("\n", records.Select(entry => entry.Key + "|" + entry.Value));
        }

        private static void ValidateInPlaceClips(
            Transform visual, IReadOnlyDictionary<string, AnimationClip> clips)
        {
            Vector3 position = visual.localPosition;
            Quaternion rotation = visual.localRotation;
            Vector3 scale = visual.localScale;
            AnimationMode.StartAnimationMode();
            try
            {
                foreach (ClipRequirement requirement in ClipRequirements)
                {
                    AnimationClip clip = clips[requirement.Name];
                    for (int frame = 0; frame <= (int)requirement.ExpectedFrames; frame++)
                    {
                        AnimationMode.BeginSampling();
                        try
                        {
                            AnimationMode.SampleAnimationClip(visual.gameObject, clip,
                                Mathf.Min(frame / clip.frameRate, clip.length));
                        }
                        finally
                        {
                            AnimationMode.EndSampling();
                        }
                        if (visual.localPosition != position || visual.localRotation != rotation
                            || visual.localScale != scale)
                            throw new InvalidOperationException(
                                $"{clip.name}, frame {frame}: el clip altera la raiz del visual GroundAligned.");
                    }
                }
            }
            finally
            {
                AnimationMode.StopAnimationMode();
            }
        }

        private static Vector2 MeasureSoleHeight(Transform visualRoot, AnimationClip idleClip)
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

                return new Vector2(BakeLowestWorldY(left), BakeLowestWorldY(right));
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

        private static float MaxSoleDifference(Vector2 previous, Vector2 current)
        {
            return Mathf.Max(Mathf.Abs(current.x - previous.x), Mathf.Abs(current.y - previous.y));
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
            Vector2 previousSoleHeight,
            Vector2 v10SoleHeight)
        {
            Transform resolvedVisual = previousContext.Driver.transform.Find(ActiveVisualName);
            if (resolvedVisual != newVisual
                || resolvedVisual.GetComponent<Animator>() != newAnimator
                || !resolvedVisual.gameObject.activeInHierarchy
                || newAnimator.runtimeAnimatorController != targetController
                || !newAnimator.isActiveAndEnabled
                || newAnimator.applyRootMotion
                || newAnimator.cullingMode != AnimatorCullingMode.AlwaysAnimate
                || !newVisual.localPosition.Equals(preservedPosition)
                || !newVisual.localRotation.Equals(preservedRotation)
                || !newVisual.localScale.Equals(preservedScale)
                || previousContext.VisualRoot.name != BackupVisualName
                || previousContext.VisualRoot.gameObject.activeSelf)
            {
                throw new InvalidOperationException(
                    "La sustitución visual v10 no superó la validación estructural o de transformación.");
            }

            Animator[] activeAnimators = previousContext.PlayerRoot.GetComponentsInChildren<Animator>(false);
            if (activeAnimators.Length != 1 || activeAnimators[0] != newAnimator
                || previousContext.PlayerRoot.Cast<Transform>().Count(child => child.name == ActiveVisualName) != 1
                || PrefabUtility.GetPrefabAssetPathOfNearestInstanceRoot(newVisual.gameObject) != FbxAssetPath
                || newVisual.GetComponentsInChildren<SkinnedMeshRenderer>(true)
                    .Any(renderer => AssetDatabase.GetAssetPath(renderer.sharedMesh) != FbxAssetPath)
                || newVisual.GetComponentsInChildren<MeshFilter>(true)
                    .Any(filter => AssetDatabase.GetAssetPath(filter.sharedMesh) != FbxAssetPath)
                || targetStates.Values.Any(state => AssetDatabase.GetAssetPath(state.motion) != FbxAssetPath))
                throw new InvalidOperationException("El driver debe encontrar exclusivamente el Animator v10; todas las mallas y Motion deben provenir de v10.");

            if (MaxSoleDifference(previousSoleHeight, v10SoleHeight) > SoleHeightTolerance)
            {
                throw new InvalidOperationException(
                    $"Las suelas v10 cambiaron {MaxSoleDifference(previousSoleHeight, v10SoleHeight):0.######} unidades respecto a GroundAligned. " +
                    "No se recalculó ni alteró localPosition.y.");
            }

            if (targetStates.Count != ClipRequirements.Length
                || targetStates.Values.Any(state => state.motion == null))
            {
                throw new InvalidOperationException(
                    "El controller nuevo no conserva exactamente seis estados con Motion.");
            }
        }

        private static Dictionary<Component, string> CaptureProtectedComponents(Scene scene, Transform visual)
        {
            return scene.GetRootGameObjects()
                .SelectMany(root => root.GetComponentsInChildren<Component>(true))
                .Where(component => component != null && !component.transform.IsChildOf(visual)
                    && component != visual.parent)
                .ToDictionary(component => component, component => EditorJsonUtility.ToJson(component));
        }

        private static void ValidateProtectedComponents(Dictionary<Component, string> snapshot)
        {
            foreach (KeyValuePair<Component, string> entry in snapshot)
                if (entry.Key == null || EditorJsonUtility.ToJson(entry.Key) != entry.Value)
                    throw new InvalidOperationException("Un componente ajeno al visual cambio. La escena no se guardara.");
        }

        private static void ValidateExistingFbx(string assetPath)
        {
            if (assetPath != FbxAssetPath || !File.Exists(ExternalFbxPath))
            {
                throw new InvalidOperationException(
                    "No se puede verificar el FBX v10 en su ruta exacta contra el archivo externo.");
            }

            using (SHA256 sha = SHA256.Create())
            using (FileStream external = File.OpenRead(ExternalFbxPath))
            using (FileStream existing = File.OpenRead(AbsoluteAssetPath(assetPath)))
            {
                if (!sha.ComputeHash(external).SequenceEqual(sha.ComputeHash(existing)))
                {
                    throw new InvalidOperationException(
                        "El FBX v10 existente no coincide con el externo. No se reemplazará ni se creará un duplicado.");
                }
            }
        }

        private static bool ValidateTestDestination(string path, string exactName, Type expectedType)
        {
            UnityEngine.Object asset = AssetDatabase.LoadMainAssetAtPath(path);
            if (!path.StartsWith("Assets/", StringComparison.Ordinal)
                || Path.GetFileName(path) != exactName
                || (asset != null && !expectedType.IsInstanceOfType(asset)))
            {
                ShowError($"El destino no es un recurso de prueba reconocido y no será modificado:\n{path}");
                return false;
            }

            if (path != TargetScenePath && path != TargetControllerPath)
            {
                ShowError("La ruta no pertenece a los dos destinos v10 reservados.");
                return false;
            }
            string reservation = AbsoluteAssetPath("Library/QusapWallSlide-" + exactName + ".owner");
            if (!File.Exists(reservation))
            {
                if (File.Exists(AbsoluteAssetPath(path)) || File.Exists(AbsoluteAssetPath(path) + ".meta"))
                {
                    ShowError($"Ya existe un destino sin registro de esta herramienta. No se reemplazara: {path}");
                    return false;
                }
                File.WriteAllText(reservation, path);
            }
            else if (File.ReadAllText(reservation) != path)
            {
                ShowError("El registro de recuperacion v10 no coincide con el destino.");
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
            if ((destination != TargetScenePath && destination != TargetControllerPath)
                || Path.GetFileName(destination) != exactName
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
            else if (File.Exists(destinationFullPath + ".meta"))
            {
                File.Copy(sourceFullPath, destinationFullPath, false);
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
                        throw new InvalidOperationException("No se pudo cerrar la copia WallSlide parcial.");
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
