#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Qusap;
using UnityEditor;
using UnityEditor.Animations;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Qusap.EditorTools
{
    public static class QusapLuzAirLocomotionTestCreator
    {
        private const string MenuPath = "Tools/Qusap/Create Qusap Luz Air Locomotion Test";
        private const string ActiveVisualName = "PlayerVisual";

        private static readonly InstallationConfig AirConfiguration = new(
            "Qusap_Luz_Locomotion_v4.fbx",
            "Qusap_Luz_Animator_v2.controller",
            "PlayerVisual_LocomotionV2_Backup",
            "Qusap Luz Air Locomotion Test",
            null,
            "_QusapLuzAirTest_v1",
            null,
            false);

        private static readonly ClipRequirement[] ClipRequirements =
        {
            new("Qusap_Idle", true),
            new("Qusap_Run", true),
            new("Qusap_JumpRise", false),
            new("Qusap_Fall", true),
            new("Qusap_Land", false)
        };

        [MenuItem(MenuPath)]
        private static void CreateTestInstallation()
        {
            CreateTestInstallation(AirConfiguration);
        }

        internal static void CreateTestInstallation(InstallationConfig configuration)
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode)
            {
                ShowError(
                    configuration,
                    "La prueba no puede crearse mientras Unity está entrando o se encuentra en Play Mode.");
                return;
            }

            Scene sourceScene = SceneManager.GetActiveScene();
            if (!TryValidateSourceScene(
                    sourceScene,
                    configuration,
                    out GameObject player,
                    out QusapAnimationDriver animationDriver,
                    out Transform previousVisual,
                    out string validationError))
            {
                ShowError(configuration, validationError + "\n\nNo se modificó ningún objeto de escena.");
                return;
            }

            if (!TryConfigureFbxAndLoadClips(
                    configuration,
                    out string fbxPath,
                    out GameObject fbxAsset,
                    out Dictionary<string, AnimationClip> clips,
                    out string fbxError))
            {
                ShowError(configuration, fbxError + "\n\nLa escena no fue modificada.");
                return;
            }

            string testScenePath = BuildTestScenePath(sourceScene.path, configuration);
            string controllerPath = CombineAssetPath(
                Path.GetDirectoryName(fbxPath),
                configuration.ControllerFileName);

            UnityEngine.Object existingController = AssetDatabase.LoadMainAssetAtPath(controllerPath);
            if (existingController != null && !(existingController is AnimatorController))
            {
                ShowError(
                    configuration,
                    $"Ya existe un asset que no es Animator Controller en la ruta requerida:\n{controllerPath}\n\n" +
                    "La escena no fue modificada.");
                return;
            }

            if (!CanWriteDestination(configuration, testScenePath, "la escena de prueba")
                || !CanWriteDestination(configuration, controllerPath, "el Animator Controller"))
            {
                return;
            }

            if (AssetDatabase.LoadAssetAtPath<SceneAsset>(testScenePath) != null
                && !EditorUtility.DisplayDialog(
                    configuration.DialogTitle,
                    $"Ya existe la copia de prueba:\n{testScenePath}\n\n¿Deseas reemplazar su contenido desde la escena activa?",
                    "Reemplazar copia",
                    "Cancelar"))
            {
                return;
            }

            bool controllerAlreadyExisted = existingController is AnimatorController;
            Vector3 visualPosition = previousVisual.localPosition;
            Quaternion visualRotation = previousVisual.localRotation;
            Vector3 visualScale = previousVisual.localScale;

            try
            {
                // saveAsCopy=false changes the open scene to the new path. The original scene
                // asset on disk remains untouched, and every hierarchy edit below targets only the copy.
                if (!EditorSceneManager.SaveScene(sourceScene, testScenePath, false))
                {
                    ShowError(
                        configuration,
                        $"Unity no pudo guardar la copia de la escena en:\n{testScenePath}\n\n" +
                        "La escena original no fue modificada.");
                    return;
                }

                Scene testScene = SceneManager.GetActiveScene();
                AnimatorController controller = RebuildController(controllerPath, clips);

                Undo.SetCurrentGroupName(configuration.DialogTitle);
                int undoGroup = Undo.GetCurrentGroup();

                Undo.RecordObject(previousVisual.gameObject, "Back up previous PlayerVisual");
                previousVisual.name = configuration.BackupVisualName;
                previousVisual.gameObject.SetActive(false);
                PrefabUtility.RecordPrefabInstancePropertyModifications(previousVisual.gameObject);

                GameObject newVisual = PrefabUtility.InstantiatePrefab(fbxAsset, player.transform) as GameObject;
                if (newVisual == null)
                {
                    throw new InvalidOperationException($"Unity no pudo instanciar el modelo '{fbxPath}'.");
                }

                Undo.RegisterCreatedObjectUndo(newVisual, "Create Qusap Luz PlayerVisual");
                newVisual.name = ActiveVisualName;
                newVisual.SetActive(true);

                Transform newVisualTransform = newVisual.transform;
                newVisualTransform.localPosition = configuration.PreserveVisualTransform
                    ? visualPosition
                    : Vector3.zero;
                newVisualTransform.localRotation = visualRotation;
                newVisualTransform.localScale = configuration.PreserveVisualTransform
                    ? visualScale
                    : Vector3.one;

                Animator animator = newVisual.GetComponent<Animator>();
                if (animator == null)
                {
                    animator = Undo.AddComponent<Animator>(newVisual);
                }

                Undo.RecordObject(animator, "Configure Qusap Luz Animator");
                animator.runtimeAnimatorController = controller;
                animator.applyRootMotion = false;
                animator.updateMode = AnimatorUpdateMode.Normal;
                animator.cullingMode = AnimatorCullingMode.AlwaysAnimate;

                PrefabUtility.RecordPrefabInstancePropertyModifications(newVisual);
                PrefabUtility.RecordPrefabInstancePropertyModifications(newVisualTransform);
                PrefabUtility.RecordPrefabInstancePropertyModifications(animator);

                ValidateSceneInstallation(
                    animationDriver,
                    previousVisual,
                    newVisualTransform,
                    animator,
                    controller,
                    configuration,
                    visualPosition,
                    visualRotation,
                    visualScale);

                EditorSceneManager.MarkSceneDirty(testScene);
                if (!EditorSceneManager.SaveScene(testScene))
                {
                    throw new InvalidOperationException($"Unity no pudo guardar la escena de prueba '{testScenePath}'.");
                }

                Undo.CollapseUndoOperations(undoGroup);
                AssetDatabase.SaveAssets();
                Selection.activeGameObject = newVisual;

                string controllerResult = controllerAlreadyExisted
                    ? $"actualizado conservando su asset: {controllerPath}"
                    : $"creado: {controllerPath}";
                string report =
                    "Prueba de locomoción creada correctamente.\n\n" +
                    $"FBX configurado y reimportado:\n{fbxPath}\n\n" +
                    $"Animator Controller {controllerResult}\n\n" +
                    $"Copia de escena creada o actualizada:\n{testScenePath}\n\n" +
                    "Se validaron los cinco clips públicos, los tres parámetros, los cinco estados, " +
                    "todas las transiciones, el respaldo anterior desactivado y el nuevo PlayerVisual sin Root Motion.\n\n" +
                    "La escena original y el prefab funcional no fueron modificados.";

                Debug.Log(report, newVisual);
                EditorUtility.DisplayDialog(configuration.DialogTitle, report, "Aceptar");
            }
            catch (Exception exception)
            {
                Debug.LogException(exception);
                ShowError(
                    configuration,
                    "La creación de la prueba no pudo completarse. La escena original permanece intacta; " +
                    "revisa la copia de prueba y la consola para conocer el detalle.\n\n" +
                    exception.Message);
            }
        }

        private static bool TryValidateSourceScene(
            Scene scene,
            InstallationConfig configuration,
            out GameObject player,
            out QusapAnimationDriver animationDriver,
            out Transform previousVisual,
            out string error)
        {
            player = null;
            animationDriver = null;
            previousVisual = null;
            error = null;

            if (!scene.IsValid() || !scene.isLoaded)
            {
                error = "No hay una escena activa válida y cargada.";
                return false;
            }

            if (string.IsNullOrEmpty(scene.path)
                || !scene.path.StartsWith("Assets/", StringComparison.Ordinal))
            {
                error = "La escena activa debe estar guardada dentro de Assets antes de crear la copia de prueba.";
                return false;
            }

            if (string.Equals(
                    Path.GetFileName(scene.path),
                    configuration.TestSceneFileName,
                    StringComparison.Ordinal)
                || (!string.IsNullOrEmpty(configuration.TestSceneSuffix)
                    && Path.GetFileNameWithoutExtension(scene.path)
                        .EndsWith(configuration.TestSceneSuffix, StringComparison.Ordinal)))
            {
                error =
                    "La escena activa ya es la copia de prueba de esta herramienta. " +
                    "Abre la escena original y ejecuta la herramienta desde allí.";
                return false;
            }

            if (!TryResolveFunctionalPlayer(scene, out player, out animationDriver, out error))
            {
                return false;
            }

            if (!player.activeInHierarchy || player.GetComponent<Rigidbody>() == null)
            {
                error = $"El jugador resuelto '{player.name}' debe estar activo y conservar su Rigidbody funcional.";
                return false;
            }

            if (!animationDriver.isActiveAndEnabled)
            {
                error = $"El QusapAnimationDriver de '{player.name}' no está activo y habilitado.";
                return false;
            }

            previousVisual = player.transform.Find(ActiveVisualName);
            if (previousVisual == null || !previousVisual.gameObject.activeInHierarchy)
            {
                error =
                    $"El jugador '{player.name}' no tiene un hijo directo activo llamado exactamente " +
                    $"'{ActiveVisualName}'.";
                return false;
            }

            if (previousVisual.GetComponent<Animator>() == null)
            {
                error = $"El hijo activo '{ActiveVisualName}' no tiene el Animator usado por QusapAnimationDriver.";
                return false;
            }

            if (!string.IsNullOrEmpty(configuration.ExpectedPreviousFbxFileName)
                && !VisualComesFromExpectedFbx(
                    previousVisual.gameObject,
                    configuration.ExpectedPreviousFbxFileName))
            {
                error =
                    $"El PlayerVisual activo no es una instancia de '{configuration.ExpectedPreviousFbxFileName}'. " +
                    "Para no respaldar ni reemplazar el visual equivocado, no se realizó ningún cambio de escena.";
                return false;
            }

            Transform existingBackup = player.transform.Find(configuration.BackupVisualName);
            if (existingBackup != null && existingBackup != previousVisual)
            {
                error =
                    $"El jugador '{player.name}' ya contiene un hijo llamado '{configuration.BackupVisualName}'. " +
                    "Para no sobrescribir el respaldo, no se realizó ningún cambio de escena.";
                return false;
            }

            return true;
        }

        private static bool VisualComesFromExpectedFbx(
            GameObject visual,
            string expectedFbxFileName)
        {
            GameObject source = PrefabUtility.GetCorrespondingObjectFromOriginalSource(visual);
            if (source == null)
            {
                source = PrefabUtility.GetCorrespondingObjectFromSource(visual);
            }

            string sourcePath = source != null ? AssetDatabase.GetAssetPath(source) : null;
            return !string.IsNullOrEmpty(sourcePath)
                && string.Equals(
                    Path.GetFileName(sourcePath),
                    expectedFbxFileName,
                    StringComparison.Ordinal);
        }

        private static bool TryResolveFunctionalPlayer(
            Scene scene,
            out GameObject player,
            out QusapAnimationDriver animationDriver,
            out string error)
        {
            player = null;
            animationDriver = null;
            error = null;

            GameObject selectedObject = Selection.activeGameObject;
            if (selectedObject != null && selectedObject.scene == scene)
            {
                for (Transform current = selectedObject.transform; current != null; current = current.parent)
                {
                    QusapAnimationDriver candidateDriver = current.GetComponent<QusapAnimationDriver>();
                    Rigidbody candidateBody = current.GetComponent<Rigidbody>();
                    if (candidateDriver != null
                        && candidateBody != null
                        && candidateDriver.isActiveAndEnabled
                        && current.gameObject.activeInHierarchy)
                    {
                        player = current.gameObject;
                        animationDriver = candidateDriver;
                        return true;
                    }
                }
            }

            QusapAnimationDriver[] activeDrivers = scene.GetRootGameObjects()
                .SelectMany(root => root.GetComponentsInChildren<QusapAnimationDriver>(false))
                .Where(driver =>
                    driver != null
                    && driver.gameObject.scene == scene
                    && driver.isActiveAndEnabled)
                .ToArray();

            if (activeDrivers.Length == 0)
            {
                error =
                    "No se encontró ningún QusapAnimationDriver activo. Selecciona un jugador funcional " +
                    "o cualquiera de sus hijos.";
                return false;
            }

            if (activeDrivers.Length > 1)
            {
                error =
                    $"Se encontraron {activeDrivers.Length} QusapAnimationDriver activos. Selecciona el jugador " +
                    "que quieres probar, o cualquiera de sus hijos, para resolverlo sin depender de su nombre.";
                return false;
            }

            animationDriver = activeDrivers[0];
            player = animationDriver.gameObject;
            return true;
        }

        private static bool TryConfigureFbxAndLoadClips(
            InstallationConfig configuration,
            out string fbxPath,
            out GameObject fbxAsset,
            out Dictionary<string, AnimationClip> clips,
            out string error)
        {
            fbxPath = null;
            fbxAsset = null;
            clips = null;
            error = null;

            string expectedBaseName = Path.GetFileNameWithoutExtension(configuration.FbxFileName);
            string[] matchingPaths = AssetDatabase.FindAssets(expectedBaseName + " t:Model")
                .Select(AssetDatabase.GUIDToAssetPath)
                .Where(path =>
                    path.StartsWith("Assets/", StringComparison.Ordinal)
                    && string.Equals(
                        Path.GetFileName(path),
                        configuration.FbxFileName,
                        StringComparison.Ordinal))
                .Distinct(StringComparer.Ordinal)
                .ToArray();

            if (matchingPaths.Length != 1)
            {
                error = matchingPaths.Length == 0
                    ? $"AssetDatabase no encontró el FBX exacto '{configuration.FbxFileName}' dentro de Assets."
                    : $"AssetDatabase encontró más de un FBX llamado '{configuration.FbxFileName}':\n" +
                      string.Join("\n", matchingPaths);
                return false;
            }

            fbxPath = matchingPaths[0];
            ModelImporter importer = AssetImporter.GetAtPath(fbxPath) as ModelImporter;
            if (importer == null)
            {
                error = $"El asset encontrado no utiliza ModelImporter: {fbxPath}";
                return false;
            }

            ModelImporterClipAnimation[] sourceTakes = importer.defaultClipAnimations;
            var definitions = new List<ModelImporterClipAnimation>(ClipRequirements.Length);

            foreach (ClipRequirement requirement in ClipRequirements)
            {
                if (!TryFindSourceTake(
                        sourceTakes,
                        requirement.Name,
                        out ModelImporterClipAnimation sourceTake,
                        out string takeError))
                {
                    error =
                        $"No se pudieron resolver los cinco Source Takes de '{fbxPath}'.\n\n" +
                        takeError + "\n\nSource Takes detectados:\n" + DescribeClipDefinitions(sourceTakes);
                    return false;
                }

                definitions.Add(CreateClipDefinition(requirement, sourceTake));
            }

            try
            {
                importer.animationType = ModelImporterAnimationType.Generic;
                importer.avatarSetup = ModelImporterAvatarSetup.CreateFromThisModel;
                importer.importAnimation = true;
                importer.animationCompression = ModelImporterAnimationCompression.Off;
                importer.clipAnimations = definitions.ToArray();
                importer.SaveAndReimport();
            }
            catch (Exception exception)
            {
                error = $"Unity no pudo guardar y reimportar '{fbxPath}'.\n\n{exception.Message}";
                return false;
            }

            importer = AssetImporter.GetAtPath(fbxPath) as ModelImporter;
            if (importer == null)
            {
                error = $"El ModelImporter de '{fbxPath}' no pudo recargarse después de SaveAndReimport.";
                return false;
            }

            fbxAsset = AssetDatabase.LoadAssetAtPath<GameObject>(fbxPath);
            AnimationClip[] allClips = AssetDatabase.LoadAllAssetsAtPath(fbxPath)
                .OfType<AnimationClip>()
                .ToArray();
            AnimationClip[] publicClips = allClips
                .Where(clip => !IsPreviewName(clip.name))
                .ToArray();
            ModelImporterClipAnimation[] importedDefinitions = importer.clipAnimations;

            bool importerIsValid =
                importer.animationType == ModelImporterAnimationType.Generic
                && importer.avatarSetup == ModelImporterAvatarSetup.CreateFromThisModel
                && importer.importAnimation
                && importer.animationCompression == ModelImporterAnimationCompression.Off
                && importedDefinitions != null
                && importedDefinitions.Length == ClipRequirements.Length
                && ClipRequirements.All(requirement =>
                    importedDefinitions.Count(definition =>
                        definition.name == requirement.Name
                        && definition.loopTime == requirement.Loop
                        && definition.loopPose == requirement.Loop
                        && definition.keepOriginalOrientation
                        && definition.keepOriginalPositionY
                        && definition.keepOriginalPositionXZ
                        && definition.lockRootRotation
                        && definition.lockRootHeightY
                        && definition.lockRootPositionXZ) == 1);

            bool publicClipsAreValid = publicClips.Length == ClipRequirements.Length
                && ClipRequirements.All(requirement =>
                    publicClips.Count(clip => clip.name == requirement.Name) == 1)
                && !publicClips.Any(clip => IsNumberedDuplicate(clip.name));

            if (fbxAsset == null || !importerIsValid || !publicClipsAreValid)
            {
                error =
                    $"La reimportación de '{fbxPath}' no produjo exactamente los cinco clips públicos requeridos " +
                    "o generó duplicados numerados. La escena no será modificada.\n\n" +
                    "ModelImporter.clipAnimations:\n" + DescribeClipDefinitions(importedDefinitions) + "\n\n" +
                    "Sub-assets AnimationClip:\n" + DescribeAnimationSubAssets(allClips);
                return false;
            }

            clips = publicClips.ToDictionary(clip => clip.name, StringComparer.Ordinal);
            return true;
        }

        private static bool TryFindSourceTake(
            ModelImporterClipAnimation[] sourceTakes,
            string requiredName,
            out ModelImporterClipAnimation sourceTake,
            out string error)
        {
            sourceTake = null;
            error = null;

            ModelImporterClipAnimation[] matches = (sourceTakes ?? Array.Empty<ModelImporterClipAnimation>())
                .Where(candidate =>
                    candidate != null
                    && !IsPreviewName(candidate.name)
                    && !IsPreviewName(candidate.takeName)
                    && (IsRequiredTakeName(candidate.name, requiredName)
                        || IsRequiredTakeName(candidate.takeName, requiredName)))
                .ToArray();

            if (matches.Length != 1)
            {
                error = matches.Length == 0
                    ? $"No se encontró un Source Take real llamado '{requiredName}'."
                    : $"Se encontraron {matches.Length} Source Takes posibles para '{requiredName}':\n" +
                      DescribeClipDefinitions(matches);
                return false;
            }

            sourceTake = matches[0];
            if (sourceTake.lastFrame < sourceTake.firstFrame
                || string.IsNullOrWhiteSpace(GetRealTakeName(sourceTake)))
            {
                error = $"El Source Take '{requiredName}' tiene un nombre o rango de frames inválido.";
                sourceTake = null;
                return false;
            }

            return true;
        }

        private static ModelImporterClipAnimation CreateClipDefinition(
            ClipRequirement requirement,
            ModelImporterClipAnimation sourceTake)
        {
            return new ModelImporterClipAnimation
            {
                name = requirement.Name,
                takeName = GetRealTakeName(sourceTake),
                firstFrame = sourceTake.firstFrame,
                lastFrame = sourceTake.lastFrame,
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

        private static string GetRealTakeName(ModelImporterClipAnimation sourceTake)
        {
            return !string.IsNullOrWhiteSpace(sourceTake.takeName)
                ? sourceTake.takeName
                : sourceTake.name;
        }

        private static bool IsRequiredTakeName(string candidateName, string requiredName)
        {
            if (string.IsNullOrWhiteSpace(candidateName))
            {
                return false;
            }

            int separator = candidateName.LastIndexOf('|');
            string takeSegment = separator >= 0
                ? candidateName.Substring(separator + 1)
                : candidateName;
            return string.Equals(takeSegment.Trim(), requiredName, StringComparison.OrdinalIgnoreCase);
        }

        private static bool IsPreviewName(string candidateName)
        {
            if (string.IsNullOrWhiteSpace(candidateName))
            {
                return false;
            }

            int separator = candidateName.LastIndexOf('|');
            string takeSegment = separator >= 0
                ? candidateName.Substring(separator + 1)
                : candidateName;
            return takeSegment.Trim().TrimStart('_')
                .StartsWith("preview", StringComparison.OrdinalIgnoreCase);
        }

        private static bool IsNumberedDuplicate(string candidateName)
        {
            foreach (ClipRequirement requirement in ClipRequirements)
            {
                if (!candidateName.StartsWith(requirement.Name, StringComparison.Ordinal)
                    || candidateName.Length == requirement.Name.Length)
                {
                    continue;
                }

                string suffix = candidateName.Substring(requirement.Name.Length).Trim();
                if (suffix.StartsWith("(", StringComparison.Ordinal)
                    && suffix.EndsWith(")", StringComparison.Ordinal))
                {
                    suffix = suffix.Substring(1, suffix.Length - 2);
                }

                if (int.TryParse(suffix, out _))
                {
                    return true;
                }
            }

            return false;
        }

        private static AnimatorController RebuildController(
            string controllerPath,
            IReadOnlyDictionary<string, AnimationClip> clips)
        {
            UnityEngine.Object existingMainAsset = AssetDatabase.LoadMainAssetAtPath(controllerPath);
            if (existingMainAsset != null && !(existingMainAsset is AnimatorController))
            {
                throw new InvalidOperationException(
                    $"Ya existe un asset que no es Animator Controller en '{controllerPath}'.");
            }

            AnimatorController controller = existingMainAsset as AnimatorController;
            if (controller == null)
            {
                controller = AnimatorController.CreateAnimatorControllerAtPath(controllerPath);
                if (controller == null)
                {
                    throw new InvalidOperationException($"No se pudo crear '{controllerPath}'.");
                }
            }

            controller.parameters = Array.Empty<AnimatorControllerParameter>();
            controller.layers = Array.Empty<AnimatorControllerLayer>();
            EditorUtility.SetDirty(controller);
            AssetDatabase.SaveAssets();

            UnityEngine.Object[] oldSubAssets = AssetDatabase.LoadAllAssetsAtPath(controllerPath)
                .Where(asset => asset != null && asset != controller)
                .ToArray();
            foreach (UnityEngine.Object oldSubAsset in oldSubAssets)
            {
                UnityEngine.Object.DestroyImmediate(oldSubAsset, true);
            }

            controller.AddParameter(new AnimatorControllerParameter
            {
                name = "Speed",
                type = AnimatorControllerParameterType.Float,
                defaultFloat = 0f
            });
            controller.AddParameter(new AnimatorControllerParameter
            {
                name = "VerticalSpeed",
                type = AnimatorControllerParameterType.Float,
                defaultFloat = 0f
            });
            controller.AddParameter(new AnimatorControllerParameter
            {
                name = "Grounded",
                type = AnimatorControllerParameterType.Bool,
                defaultBool = true
            });

            controller.AddLayer("Base Layer");
            AnimatorControllerLayer[] layers = controller.layers;
            layers[0].defaultWeight = 1f;
            controller.layers = layers;

            AnimatorStateMachine stateMachine = controller.layers[0].stateMachine;
            AnimatorState idle = AddState(stateMachine, "Qusap_Idle", clips, new Vector3(230f, 80f, 0f));
            AnimatorState run = AddState(stateMachine, "Qusap_Run", clips, new Vector3(500f, 80f, 0f));
            AnimatorState jumpRise = AddState(stateMachine, "Qusap_JumpRise", clips, new Vector3(230f, 230f, 0f));
            AnimatorState fall = AddState(stateMachine, "Qusap_Fall", clips, new Vector3(500f, 230f, 0f));
            AnimatorState land = AddState(stateMachine, "Qusap_Land", clips, new Vector3(365f, 380f, 0f));
            stateMachine.defaultState = idle;

            AnimatorStateTransition idleToRun = idle.AddTransition(run);
            ConfigureTransition(idleToRun, false, 0f, 0.08f);
            idleToRun.AddCondition(AnimatorConditionMode.Greater, 0.1f, "Speed");
            idleToRun.AddCondition(AnimatorConditionMode.If, 0f, "Grounded");

            AnimatorStateTransition runToIdle = run.AddTransition(idle);
            ConfigureTransition(runToIdle, false, 0f, 0.08f);
            runToIdle.AddCondition(AnimatorConditionMode.Less, 0.1f, "Speed");
            runToIdle.AddCondition(AnimatorConditionMode.If, 0f, "Grounded");

            AnimatorStateTransition anyToJumpRise = stateMachine.AddAnyStateTransition(jumpRise);
            ConfigureTransition(anyToJumpRise, false, 0f, 0.05f);
            anyToJumpRise.canTransitionToSelf = false;
            anyToJumpRise.AddCondition(AnimatorConditionMode.IfNot, 0f, "Grounded");
            anyToJumpRise.AddCondition(AnimatorConditionMode.Greater, 0.1f, "VerticalSpeed");

            AnimatorStateTransition anyToFall = stateMachine.AddAnyStateTransition(fall);
            ConfigureTransition(anyToFall, false, 0f, 0.05f);
            anyToFall.canTransitionToSelf = false;
            anyToFall.AddCondition(AnimatorConditionMode.IfNot, 0f, "Grounded");
            anyToFall.AddCondition(AnimatorConditionMode.Less, 0f, "VerticalSpeed");

            AnimatorStateTransition jumpRiseToLand = jumpRise.AddTransition(land);
            ConfigureTransition(jumpRiseToLand, false, 0f, 0.03f);
            jumpRiseToLand.AddCondition(AnimatorConditionMode.If, 0f, "Grounded");

            AnimatorStateTransition fallToLand = fall.AddTransition(land);
            ConfigureTransition(fallToLand, false, 0f, 0.03f);
            fallToLand.AddCondition(AnimatorConditionMode.If, 0f, "Grounded");

            AnimatorStateTransition landToIdle = land.AddTransition(idle);
            ConfigureTransition(landToIdle, true, 0.75f, 0.05f);
            landToIdle.AddCondition(AnimatorConditionMode.Less, 0.1f, "Speed");
            landToIdle.AddCondition(AnimatorConditionMode.If, 0f, "Grounded");

            AnimatorStateTransition landToRun = land.AddTransition(run);
            ConfigureTransition(landToRun, true, 0.75f, 0.05f);
            landToRun.AddCondition(AnimatorConditionMode.Greater, 0.1f, "Speed");
            landToRun.AddCondition(AnimatorConditionMode.If, 0f, "Grounded");

            EditorUtility.SetDirty(controller);
            EditorUtility.SetDirty(stateMachine);
            foreach (ChildAnimatorState childState in stateMachine.states)
            {
                EditorUtility.SetDirty(childState.state);
            }

            AnimatorStateTransition[] configuredTransitions =
            {
                idleToRun,
                runToIdle,
                anyToJumpRise,
                anyToFall,
                jumpRiseToLand,
                fallToLand,
                landToIdle,
                landToRun
            };
            foreach (AnimatorStateTransition transition in configuredTransitions)
            {
                EditorUtility.SetDirty(transition);
            }

            AssetDatabase.SaveAssets();
            ValidateController(controller, clips);
            return controller;
        }

        private static AnimatorState AddState(
            AnimatorStateMachine stateMachine,
            string stateName,
            IReadOnlyDictionary<string, AnimationClip> clips,
            Vector3 position)
        {
            AnimatorState state = stateMachine.AddState(stateName, position);
            state.motion = clips[stateName];
            return state;
        }

        private static void ConfigureTransition(
            AnimatorStateTransition transition,
            bool hasExitTime,
            float exitTime,
            float duration)
        {
            transition.hasExitTime = hasExitTime;
            transition.exitTime = exitTime;
            transition.hasFixedDuration = true;
            transition.duration = duration;
        }

        private static void ValidateController(
            AnimatorController controller,
            IReadOnlyDictionary<string, AnimationClip> clips)
        {
            Dictionary<string, AnimatorControllerParameter> parameters = controller.parameters
                .ToDictionary(parameter => parameter.name, StringComparer.Ordinal);
            bool parametersAreValid = parameters.Count == 3
                && HasParameter(parameters, "Speed", AnimatorControllerParameterType.Float)
                && HasParameter(parameters, "VerticalSpeed", AnimatorControllerParameterType.Float)
                && HasParameter(parameters, "Grounded", AnimatorControllerParameterType.Bool)
                && parameters["Grounded"].defaultBool;

            if (!parametersAreValid || controller.layers.Length != 1)
            {
                throw new InvalidOperationException(
                    "El Animator Controller no superó la validación de parámetros o capas.");
            }

            AnimatorStateMachine stateMachine = controller.layers[0].stateMachine;
            Dictionary<string, AnimatorState> states = stateMachine.states
                .Select(child => child.state)
                .ToDictionary(state => state.name, StringComparer.Ordinal);

            if (states.Count != ClipRequirements.Length
                || ClipRequirements.Any(requirement =>
                    !states.TryGetValue(requirement.Name, out AnimatorState state)
                    || state.motion != clips[requirement.Name])
                || stateMachine.defaultState != states["Qusap_Idle"])
            {
                throw new InvalidOperationException(
                    "El Animator Controller no superó la validación de estados, clips o estado predeterminado.");
            }

            AssertStateTransition(
                states["Qusap_Idle"], states["Qusap_Run"], false, 0f, 0.08f,
                Condition("Speed", AnimatorConditionMode.Greater, 0.1f),
                Condition("Grounded", AnimatorConditionMode.If, 0f));
            AssertStateTransition(
                states["Qusap_Run"], states["Qusap_Idle"], false, 0f, 0.08f,
                Condition("Speed", AnimatorConditionMode.Less, 0.1f),
                Condition("Grounded", AnimatorConditionMode.If, 0f));
            AssertAnyStateTransition(
                stateMachine, states["Qusap_JumpRise"], 0.05f,
                Condition("Grounded", AnimatorConditionMode.IfNot, 0f),
                Condition("VerticalSpeed", AnimatorConditionMode.Greater, 0.1f));
            AssertAnyStateTransition(
                stateMachine, states["Qusap_Fall"], 0.05f,
                Condition("Grounded", AnimatorConditionMode.IfNot, 0f),
                Condition("VerticalSpeed", AnimatorConditionMode.Less, 0f));
            AssertStateTransition(
                states["Qusap_JumpRise"], states["Qusap_Land"], false, 0f, 0.03f,
                Condition("Grounded", AnimatorConditionMode.If, 0f));
            AssertStateTransition(
                states["Qusap_Fall"], states["Qusap_Land"], false, 0f, 0.03f,
                Condition("Grounded", AnimatorConditionMode.If, 0f));
            AssertStateTransition(
                states["Qusap_Land"], states["Qusap_Idle"], true, 0.75f, 0.05f,
                Condition("Speed", AnimatorConditionMode.Less, 0.1f),
                Condition("Grounded", AnimatorConditionMode.If, 0f));
            AssertStateTransition(
                states["Qusap_Land"], states["Qusap_Run"], true, 0.75f, 0.05f,
                Condition("Speed", AnimatorConditionMode.Greater, 0.1f),
                Condition("Grounded", AnimatorConditionMode.If, 0f));

            if (states["Qusap_Idle"].transitions.Length != 1
                || states["Qusap_Run"].transitions.Length != 1
                || states["Qusap_JumpRise"].transitions.Length != 1
                || states["Qusap_Fall"].transitions.Length != 1
                || states["Qusap_Land"].transitions.Length != 2
                || stateMachine.anyStateTransitions.Length != 2)
            {
                throw new InvalidOperationException(
                    "El Animator Controller contiene transiciones adicionales no solicitadas.");
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

        private static ExpectedCondition Condition(
            string parameter,
            AnimatorConditionMode mode,
            float threshold)
        {
            return new ExpectedCondition(parameter, mode, threshold);
        }

        private static void AssertStateTransition(
            AnimatorState source,
            AnimatorState destination,
            bool hasExitTime,
            float exitTime,
            float duration,
            params ExpectedCondition[] expectedConditions)
        {
            AnimatorStateTransition[] matches = source.transitions
                .Where(transition => transition.destinationState == destination)
                .ToArray();
            if (matches.Length != 1
                || !TransitionMatches(
                    matches[0], hasExitTime, exitTime, duration, expectedConditions))
            {
                throw new InvalidOperationException(
                    $"La transición {source.name} → {destination.name} no coincide con la configuración requerida.");
            }
        }

        private static void AssertAnyStateTransition(
            AnimatorStateMachine stateMachine,
            AnimatorState destination,
            float duration,
            params ExpectedCondition[] expectedConditions)
        {
            AnimatorStateTransition[] matches = stateMachine.anyStateTransitions
                .Where(transition => transition.destinationState == destination)
                .ToArray();
            if (matches.Length != 1
                || matches[0].canTransitionToSelf
                || !TransitionMatches(matches[0], false, 0f, duration, expectedConditions))
            {
                throw new InvalidOperationException(
                    $"La transición Any State → {destination.name} no coincide con la configuración requerida.");
            }
        }

        private static bool TransitionMatches(
            AnimatorStateTransition transition,
            bool hasExitTime,
            float exitTime,
            float duration,
            IReadOnlyCollection<ExpectedCondition> expectedConditions)
        {
            if (transition.hasExitTime != hasExitTime
                || (hasExitTime && !Mathf.Approximately(transition.exitTime, exitTime))
                || !transition.hasFixedDuration
                || !Mathf.Approximately(transition.duration, duration)
                || transition.conditions.Length != expectedConditions.Count)
            {
                return false;
            }

            return expectedConditions.All(expected => transition.conditions.Any(actual =>
                actual.parameter == expected.Parameter
                && actual.mode == expected.Mode
                && Mathf.Approximately(actual.threshold, expected.Threshold)));
        }

        private static void ValidateSceneInstallation(
            QusapAnimationDriver animationDriver,
            Transform backupVisual,
            Transform newVisual,
            Animator animator,
            AnimatorController controller,
            InstallationConfig configuration,
            Vector3 previousPosition,
            Quaternion previousRotation,
            Vector3 previousScale)
        {
            Transform resolvedVisual = animationDriver.transform.Find(ActiveVisualName);
            Vector3 expectedPosition = configuration.PreserveVisualTransform
                ? previousPosition
                : Vector3.zero;
            Vector3 expectedScale = configuration.PreserveVisualTransform
                ? previousScale
                : Vector3.one;
            if (resolvedVisual != newVisual
                || !resolvedVisual.gameObject.activeInHierarchy
                || resolvedVisual.GetComponent<Animator>() != animator
                || animator.runtimeAnimatorController != controller
                || animator.applyRootMotion
                || animator.cullingMode != AnimatorCullingMode.AlwaysAnimate
                || newVisual.localPosition != expectedPosition
                || newVisual.localRotation != previousRotation
                || newVisual.localScale != expectedScale
                || backupVisual.name != configuration.BackupVisualName
                || backupVisual.gameObject.activeSelf)
            {
                throw new InvalidOperationException(
                    "La instalación visual o la conexión con QusapAnimationDriver no superó la validación final.");
            }

            string[] requiredParameters = { "Speed", "VerticalSpeed", "Grounded" };
            HashSet<string> controllerParameters = new(
                controller.parameters.Select(parameter => parameter.name),
                StringComparer.Ordinal);
            if (!requiredParameters.All(controllerParameters.Contains))
            {
                throw new InvalidOperationException(
                    "QusapAnimationDriver no dispone de los tres parámetros requeridos en el Animator Controller.");
            }
        }

        private static string DescribeClipDefinitions(ModelImporterClipAnimation[] definitions)
        {
            if (definitions == null || definitions.Length == 0)
            {
                return "- (ninguna definición)";
            }

            return string.Join(
                "\n",
                definitions.Select(definition =>
                    definition == null
                        ? "- (definición nula)"
                        : $"- name='{definition.name}', takeName='{definition.takeName}', " +
                          $"frames={definition.firstFrame:0.###}–{definition.lastFrame:0.###}, " +
                          $"loopTime={definition.loopTime}, loopPose={definition.loopPose}, " +
                          $"lockRootRotation={definition.lockRootRotation}, " +
                          $"lockRootHeightY={definition.lockRootHeightY}, " +
                          $"lockRootPositionXZ={definition.lockRootPositionXZ}"));
        }

        private static string DescribeAnimationSubAssets(AnimationClip[] clips)
        {
            if (clips == null || clips.Length == 0)
            {
                return "- (ningún AnimationClip)";
            }

            return string.Join(
                "\n",
                clips.OrderBy(clip => clip.name, StringComparer.Ordinal)
                    .Select(clip => $"- '{clip.name}' (hideFlags: {clip.hideFlags})"));
        }

        private static string BuildTestScenePath(
            string sourceScenePath,
            InstallationConfig configuration)
        {
            string directory = Path.GetDirectoryName(sourceScenePath);
            if (!string.IsNullOrEmpty(configuration.TestSceneFileName))
            {
                return CombineAssetPath(directory, configuration.TestSceneFileName);
            }

            string sceneName = Path.GetFileNameWithoutExtension(sourceScenePath);
            return CombineAssetPath(
                directory,
                sceneName + configuration.TestSceneSuffix + ".unity");
        }

        private static string CombineAssetPath(string directory, string fileName)
        {
            return (directory + "/" + fileName).Replace('\\', '/');
        }

        private static bool CanWriteDestination(
            InstallationConfig configuration,
            string assetPath,
            string description)
        {
            UnityEngine.Object existingAsset = AssetDatabase.LoadMainAssetAtPath(assetPath);
            if (existingAsset == null
                || AssetDatabase.IsOpenForEdit(assetPath, StatusQueryOptions.UseCachedIfPossible))
            {
                return true;
            }

            ShowError(
                configuration,
                $"No se puede escribir {description} porque no está disponible para edición:\n{assetPath}");
            return false;
        }

        private static void ShowError(InstallationConfig configuration, string message)
        {
            Debug.LogError(message);
            EditorUtility.DisplayDialog(configuration.DialogTitle, message, "Aceptar");
        }

        internal sealed class InstallationConfig
        {
            public InstallationConfig(
                string fbxFileName,
                string controllerFileName,
                string backupVisualName,
                string dialogTitle,
                string testSceneFileName,
                string testSceneSuffix,
                string expectedPreviousFbxFileName,
                bool preserveVisualTransform)
            {
                FbxFileName = fbxFileName;
                ControllerFileName = controllerFileName;
                BackupVisualName = backupVisualName;
                DialogTitle = dialogTitle;
                TestSceneFileName = testSceneFileName;
                TestSceneSuffix = testSceneSuffix;
                ExpectedPreviousFbxFileName = expectedPreviousFbxFileName;
                PreserveVisualTransform = preserveVisualTransform;
            }

            public string FbxFileName { get; }
            public string ControllerFileName { get; }
            public string BackupVisualName { get; }
            public string DialogTitle { get; }
            public string TestSceneFileName { get; }
            public string TestSceneSuffix { get; }
            public string ExpectedPreviousFbxFileName { get; }
            public bool PreserveVisualTransform { get; }
        }

        private sealed class ClipRequirement
        {
            public ClipRequirement(string name, bool loop)
            {
                Name = name;
                Loop = loop;
            }

            public string Name { get; }
            public bool Loop { get; }
        }

        private readonly struct ExpectedCondition
        {
            public ExpectedCondition(string parameter, AnimatorConditionMode mode, float threshold)
            {
                Parameter = parameter;
                Mode = mode;
                Threshold = threshold;
            }

            public string Parameter { get; }
            public AnimatorConditionMode Mode { get; }
            public float Threshold { get; }
        }
    }
}
#endif
