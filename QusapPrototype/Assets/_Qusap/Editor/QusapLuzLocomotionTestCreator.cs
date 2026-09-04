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
    public static class QusapLuzLocomotionTestCreator
    {
        private const string MenuPath = "Tools/Qusap/Create Qusap Luz Locomotion Test";
        private const string ActiveVisualName = "PlayerVisual";
        private const string BackupVisualName = "PlayerVisual_Previous_Backup";
        private const string FbxFileName = "Qusap_Luz_Locomotion_v2.fbx";
        private const string ControllerFileName = "Qusap_Luz_Animator_v1.controller";
        private const string TestSceneSuffix = "_QusapLuzTest_v1";
        private const string IdleClipName = "Qusap_Idle";
        private const string RunClipName = "Qusap_Run";
        private const string DialogTitle = "Qusap Luz Locomotion Test";

        [MenuItem(MenuPath)]
        private static void CreateTestInstallation()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode)
            {
                ShowError("La instalación de prueba no puede crearse mientras Unity está entrando o se encuentra en Play Mode.");
                return;
            }

            Scene sourceScene = SceneManager.GetActiveScene();
            if (!TryValidateSourceScene(
                    sourceScene,
                    out GameObject player,
                    out QusapAnimationDriver animationDriver,
                    out Transform previousVisual,
                    out Collider playerCollider,
                    out string validationError))
            {
                ShowError(validationError + "\n\nNo se modificó ningún asset ni objeto de escena.");
                return;
            }

            if (!TryRepairFbxAndLoadClips(
                    out string fbxPath,
                    out GameObject fbxAsset,
                    out AnimationClip idleClip,
                    out AnimationClip runClip,
                    out string fbxError))
            {
                ShowError(fbxError + "\n\nNo se modificó ningún asset ni objeto de escena.");
                return;
            }

            string testScenePath = BuildTestScenePath(sourceScene.path);
            string controllerPath = CombineAssetPath(Path.GetDirectoryName(fbxPath), ControllerFileName);

            UnityEngine.Object existingControllerAsset = AssetDatabase.LoadMainAssetAtPath(controllerPath);
            if (existingControllerAsset != null && !(existingControllerAsset is AnimatorController))
            {
                ShowError(
                    $"Ya existe un asset que no es Animator Controller en la ruta requerida:\n{controllerPath}\n\n" +
                    "No se modificó ningún asset ni objeto de escena.");
                return;
            }

            if (!CanWriteDestination(testScenePath, "la escena de prueba")
                || !CanWriteDestination(controllerPath, "el Animator Controller"))
            {
                return;
            }

            if (AssetDatabase.LoadAssetAtPath<SceneAsset>(testScenePath) != null
                && !EditorUtility.DisplayDialog(
                    DialogTitle,
                    $"Ya existe la escena de prueba:\n{testScenePath}\n\n¿Deseas reemplazar su contenido con una copia nueva de la escena activa?",
                    "Reemplazar copia",
                    "Cancelar"))
            {
                return;
            }

            bool controllerAlreadyExisted = AssetDatabase.LoadAssetAtPath<AnimatorController>(controllerPath) != null;

            try
            {
                // saveAsCopy=false performs a Save As: the open scene becomes the new test scene,
                // while the source scene asset on disk remains untouched.
                if (!EditorSceneManager.SaveScene(sourceScene, testScenePath, false))
                {
                    ShowError($"Unity no pudo guardar la copia de la escena en:\n{testScenePath}\n\nNo se modificó la escena original.");
                    return;
                }

                Scene testScene = SceneManager.GetActiveScene();
                AnimatorController controller = RebuildController(controllerPath, idleClip, runClip);

                Undo.SetCurrentGroupName("Create Qusap Luz Locomotion Test");
                int undoGroup = Undo.GetCurrentGroup();

                Undo.RecordObject(previousVisual.gameObject, "Back up previous PlayerVisual");
                previousVisual.name = BackupVisualName;
                previousVisual.gameObject.SetActive(false);
                PrefabUtility.RecordPrefabInstancePropertyModifications(previousVisual.gameObject);

                GameObject newVisual = PrefabUtility.InstantiatePrefab(fbxAsset, player.transform) as GameObject;
                if (newVisual == null)
                {
                    throw new InvalidOperationException($"Unity no pudo instanciar el modelo '{fbxPath}'.");
                }

                Undo.RegisterCreatedObjectUndo(newVisual, "Create Qusap Luz PlayerVisual");
                newVisual.name = ActiveVisualName;

                float rightFacingYaw = ReadRightFacingYaw(animationDriver);
                Transform newVisualTransform = newVisual.transform;
                newVisualTransform.localPosition = Vector3.zero;
                newVisualTransform.localRotation = Quaternion.Euler(0f, rightFacingYaw, 0f);
                newVisualTransform.localScale = Vector3.one;

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

                bool driverFindsAnimator = DriverWillFindAnimator(
                    animationDriver,
                    newVisualTransform,
                    animator,
                    previousVisual);

                if (!driverFindsAnimator)
                {
                    throw new InvalidOperationException(
                        "La verificación estructural de QusapAnimationDriver falló: el nuevo hijo activo PlayerVisual no se resolvería como su Animator.");
                }

                string rendererDimensions = DescribeRendererDimensions(newVisual);
                string colliderDimensions = DescribeColliderDimensions(playerCollider);

                EditorSceneManager.MarkSceneDirty(testScene);
                if (!EditorSceneManager.SaveScene(testScene))
                {
                    throw new InvalidOperationException($"Unity no pudo guardar la escena de prueba '{testScenePath}'.");
                }

                Undo.CollapseUndoOperations(undoGroup);
                AssetDatabase.SaveAssets();
                Selection.activeGameObject = newVisual;

                string createdFiles = controllerAlreadyExisted
                    ? $"- {testScenePath} (creada o reemplazada)\n- Ningún controller nuevo; se reconstruyó de forma segura {controllerPath} conservando su asset."
                    : $"- {testScenePath}\n- {controllerPath}";

                string report =
                    "Instalación de prueba creada correctamente.\n\n" +
                    $"Importador FBX reparado y reimportado:\n{fbxPath}\n\n" +
                    $"Archivos creados:\n{createdFiles}\n\n" +
                    $"Escena de prueba:\n{testScenePath}\n\n" +
                    $"Animator Controller:\n{controllerPath}\n\n" +
                    $"Dimensiones del nuevo modelo (Renderer bounds, AABB mundial):\n{rendererDimensions}\n\n" +
                    $"Dimensiones del collider físico (bounds, AABB mundial):\n{colliderDimensions}\n\n" +
                    $"QusapAnimationDriver encontró el Animator: {(driverFindsAnimator ? "Sí" : "No")}\n" +
                    $"Orientación inicial: yaw local {rightFacingYaw:0.###}° (rightFacingYaw existente).\n\n" +
                    "La escena original y el prefab funcional no fueron modificados.";

                Debug.Log(report, newVisual);
                EditorUtility.DisplayDialog(DialogTitle, report, "Aceptar");
            }
            catch (Exception exception)
            {
                Debug.LogException(exception);
                ShowError(
                    "La creación de la instalación de prueba no pudo completarse. " +
                    "La escena original permanece intacta; revisa la copia de prueba y la consola para conocer el detalle.\n\n" +
                    exception.Message);
            }
        }

        private static bool TryValidateSourceScene(
            Scene scene,
            out GameObject player,
            out QusapAnimationDriver animationDriver,
            out Transform previousVisual,
            out Collider playerCollider,
            out string error)
        {
            player = null;
            animationDriver = null;
            previousVisual = null;
            playerCollider = null;
            error = null;

            if (!scene.IsValid() || !scene.isLoaded)
            {
                error = "No hay una escena activa válida y cargada.";
                return false;
            }

            if (string.IsNullOrEmpty(scene.path) || !scene.path.StartsWith("Assets/", StringComparison.Ordinal))
            {
                error = "La escena activa debe estar guardada dentro de Assets antes de crear la copia de prueba.";
                return false;
            }

            if (Path.GetFileNameWithoutExtension(scene.path).EndsWith(TestSceneSuffix, StringComparison.Ordinal))
            {
                error = $"La escena activa ya tiene el sufijo de prueba '{TestSceneSuffix}'. Abre la escena funcional original y ejecuta la herramienta desde allí.";
                return false;
            }

            if (!TryResolveFunctionalPlayer(scene, out player, out animationDriver, out error))
            {
                return false;
            }

            if (!player.activeInHierarchy)
            {
                error = $"La raíz seleccionada '{player.name}' está desactivada y no puede considerarse el jugador funcional.";
                return false;
            }

            if (player.GetComponent<Rigidbody>() == null)
            {
                error = $"La raíz seleccionada '{player.name}' no tiene el Rigidbody funcional esperado.";
                return false;
            }

            playerCollider = player.GetComponent<Collider>();
            if (playerCollider == null || !playerCollider.enabled)
            {
                error = $"La raíz seleccionada '{player.name}' no tiene un Collider 3D habilitado.";
                return false;
            }

            if (animationDriver == null || !animationDriver.enabled)
            {
                error = $"La raíz seleccionada '{player.name}' no tiene un QusapAnimationDriver habilitado.";
                return false;
            }

            if (player.GetComponent<QusapGroundSensor>() == null || player.GetComponent<QusapInputReader>() == null)
            {
                error = $"La raíz seleccionada '{player.name}' no contiene los proveedores de Grounded e input requeridos por QusapAnimationDriver.";
                return false;
            }

            previousVisual = player.transform.Find(ActiveVisualName);
            if (previousVisual == null || !previousVisual.gameObject.activeInHierarchy)
            {
                error = $"La raíz seleccionada '{player.name}' no tiene un hijo directo activo llamado exactamente '{ActiveVisualName}'.";
                return false;
            }

            if (previousVisual.GetComponent<Animator>() == null)
            {
                error = $"El hijo activo '{ActiveVisualName}' no tiene el Animator que utiliza QusapAnimationDriver.";
                return false;
            }

            Transform existingBackup = player.transform.Find(BackupVisualName);
            if (existingBackup != null && existingBackup != previousVisual)
            {
                error = $"La raíz seleccionada '{player.name}' ya contiene un hijo llamado '{BackupVisualName}'. Para evitar sobrescribir o duplicar respaldos, no se realizó ningún cambio.";
                return false;
            }

            return true;
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
                Transform current = selectedObject.transform;
                while (current != null)
                {
                    QusapAnimationDriver candidateDriver = current.GetComponent<QusapAnimationDriver>();
                    Rigidbody candidateRigidbody = current.GetComponent<Rigidbody>();
                    if (candidateDriver != null
                        && candidateRigidbody != null
                        && candidateDriver.isActiveAndEnabled
                        && current.gameObject.activeInHierarchy)
                    {
                        player = current.gameObject;
                        animationDriver = candidateDriver;
                        return true;
                    }

                    current = current.parent;
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
                    "No se encontró ningún QusapAnimationDriver activo en la escena. " +
                    "Selecciona un hijo de un jugador funcional en la Hierarchy o abre una escena que contenga uno.";
                return false;
            }

            if (activeDrivers.Length > 1)
            {
                error =
                    $"Se encontraron {activeDrivers.Length} jugadores con QusapAnimationDriver activo en la escena. " +
                    "Selecciona en la Hierarchy el jugador que quieres convertir, o cualquiera de sus hijos, y ejecuta nuevamente la herramienta.";
                return false;
            }

            animationDriver = activeDrivers[0];
            player = animationDriver.gameObject;
            return true;
        }

        private static bool TryRepairFbxAndLoadClips(
            out string fbxPath,
            out GameObject fbxAsset,
            out AnimationClip idleClip,
            out AnimationClip runClip,
            out string error)
        {
            fbxPath = null;
            fbxAsset = null;
            idleClip = null;
            runClip = null;
            error = null;

            string expectedBaseName = Path.GetFileNameWithoutExtension(FbxFileName);
            string[] matchingPaths = AssetDatabase.FindAssets(expectedBaseName + " t:Model")
                .Select(AssetDatabase.GUIDToAssetPath)
                .Where(path =>
                    string.Equals(Path.GetFileName(path), FbxFileName, StringComparison.Ordinal)
                    && path.StartsWith("Assets/", StringComparison.Ordinal))
                .Distinct(StringComparer.Ordinal)
                .ToArray();

            if (matchingPaths.Length == 0)
            {
                error = $"AssetDatabase no encontró el FBX exacto '{FbxFileName}' dentro de Assets.";
                return false;
            }

            if (matchingPaths.Length > 1)
            {
                error =
                    $"AssetDatabase encontró más de un FBX llamado '{FbxFileName}'. No es seguro elegir uno automáticamente:\n" +
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
            bool idleTakeIsValid = TryFindSourceTake(
                sourceTakes,
                IdleClipName,
                120f,
                out ModelImporterClipAnimation idleTake,
                out string idleTakeError);
            bool runTakeIsValid = TryFindSourceTake(
                sourceTakes,
                RunClipName,
                24f,
                out ModelImporterClipAnimation runTake,
                out string runTakeError);
            if (!idleTakeIsValid || !runTakeIsValid)
            {
                error =
                    $"No se pudo reparar la importación de animaciones de '{fbxPath}'.\n\n" +
                    $"Idle: {idleTakeError ?? "Source Take válido."}\n" +
                    $"Run: {runTakeError ?? "Source Take válido."}\n\n" +
                    "Source Takes detectados:\n" + DescribeClipDefinitions(sourceTakes);
                return false;
            }

            ModelImporterClipAnimation idleDefinition = CreateClipDefinition(IdleClipName, idleTake);
            ModelImporterClipAnimation runDefinition = CreateClipDefinition(RunClipName, runTake);

            try
            {
                importer.animationType = ModelImporterAnimationType.Generic;
                importer.avatarSetup = ModelImporterAvatarSetup.CreateFromThisModel;
                importer.importAnimation = true;
                importer.animationCompression = ModelImporterAnimationCompression.Off;
                importer.clipAnimations = new[] { idleDefinition, runDefinition };
                importer.SaveAndReimport();
            }
            catch (Exception exception)
            {
                error =
                    $"Unity no pudo guardar y reimportar de forma síncrona el FBX '{fbxPath}'.\n\n" +
                    exception.Message;
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

            AnimationClip[] nonPreviewClips = allClips
                .Where(clip => !IsPreviewName(clip.name))
                .ToArray();
            AnimationClip[] idleMatches = nonPreviewClips.Where(clip => clip.name == IdleClipName).ToArray();
            AnimationClip[] runMatches = nonPreviewClips.Where(clip => clip.name == RunClipName).ToArray();
            AnimationClip[] numberedDuplicates = nonPreviewClips
                .Where(clip => IsNumberedDuplicate(clip.name, IdleClipName) || IsNumberedDuplicate(clip.name, RunClipName))
                .ToArray();
            ModelImporterClipAnimation[] importedDefinitions = importer.clipAnimations;
            bool importerDefinitionsAreValid = importedDefinitions != null
                && importedDefinitions.Length == 2
                && importedDefinitions.Count(definition => definition.name == IdleClipName) == 1
                && importedDefinitions.Count(definition => definition.name == RunClipName) == 1;

            if (fbxAsset == null
                || !importerDefinitionsAreValid
                || nonPreviewClips.Length != 2
                || idleMatches.Length != 1
                || runMatches.Length != 1
                || numberedDuplicates.Length > 0)
            {
                error =
                    $"La reimportación de '{fbxPath}' no produjo exactamente los dos clips públicos requeridos, " +
                    "o Unity generó nombres duplicados. La escena no será modificada.\n\n" +
                    "ModelImporter.clipAnimations:\n" + DescribeClipDefinitions(importedDefinitions) + "\n\n" +
                    "Sub-assets AnimationClip encontrados:\n" + DescribeAnimationSubAssets(allClips);
                return false;
            }

            idleClip = idleMatches[0];
            runClip = runMatches[0];
            return true;
        }

        private static bool TryFindSourceTake(
            ModelImporterClipAnimation[] sourceTakes,
            string requiredClipName,
            float expectedFrameDuration,
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
                    && (ContainsTakeName(candidate.name, requiredClipName)
                        || ContainsTakeName(candidate.takeName, requiredClipName)))
                .ToArray();

            if (matches.Length == 0)
            {
                error = $"No se encontró un Source Take real que contenga '{requiredClipName}'.";
                return false;
            }

            if (matches.Length > 1)
            {
                error =
                    $"Se encontraron {matches.Length} Source Takes posibles para '{requiredClipName}':\n" +
                    DescribeClipDefinitions(matches);
                return false;
            }

            sourceTake = matches[0];
            float actualDuration = sourceTake.lastFrame - sourceTake.firstFrame;
            if (sourceTake.lastFrame < sourceTake.firstFrame
                || !Mathf.Approximately(actualDuration, expectedFrameDuration))
            {
                error =
                    $"El Source Take '{GetRealTakeName(sourceTake)}' usa frames " +
                    $"{sourceTake.firstFrame:0.###}–{sourceTake.lastFrame:0.###} " +
                    $"(duración {actualDuration:0.###}), pero se esperaban {expectedFrameDuration:0.###} frames.";
                sourceTake = null;
                return false;
            }

            if (string.IsNullOrWhiteSpace(GetRealTakeName(sourceTake)))
            {
                error = $"El Source Take de '{requiredClipName}' no tiene un takeName utilizable.";
                sourceTake = null;
                return false;
            }

            return true;
        }

        private static ModelImporterClipAnimation CreateClipDefinition(
            string publicClipName,
            ModelImporterClipAnimation sourceTake)
        {
            return new ModelImporterClipAnimation
            {
                name = publicClipName,
                takeName = GetRealTakeName(sourceTake),
                firstFrame = sourceTake.firstFrame,
                lastFrame = sourceTake.lastFrame,
                loopTime = true,
                loopPose = true,
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

        private static bool ContainsTakeName(string candidateName, string requiredClipName)
        {
            return !string.IsNullOrWhiteSpace(candidateName)
                && candidateName.IndexOf(requiredClipName, StringComparison.OrdinalIgnoreCase) >= 0;
        }

        private static bool IsPreviewName(string candidateName)
        {
            if (string.IsNullOrWhiteSpace(candidateName))
            {
                return false;
            }

            int takeSeparator = candidateName.LastIndexOf('|');
            string takeSegment = takeSeparator >= 0
                ? candidateName.Substring(takeSeparator + 1)
                : candidateName;
            string normalizedName = takeSegment.TrimStart('_');
            return normalizedName.StartsWith("preview", StringComparison.OrdinalIgnoreCase);
        }

        private static bool IsNumberedDuplicate(string candidateName, string requiredClipName)
        {
            if (string.IsNullOrEmpty(candidateName)
                || !candidateName.StartsWith(requiredClipName + " (", StringComparison.Ordinal))
            {
                return false;
            }

            int numberStart = requiredClipName.Length + 2;
            int numberLength = candidateName.Length - numberStart - 1;
            return candidateName.EndsWith(")", StringComparison.Ordinal)
                && numberLength > 0
                && int.TryParse(candidateName.Substring(numberStart, numberLength), out _);
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
                          $"keepOriginalOrientation={definition.keepOriginalOrientation}, " +
                          $"keepOriginalPositionY={definition.keepOriginalPositionY}, " +
                          $"keepOriginalPositionXZ={definition.keepOriginalPositionXZ}, " +
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
                clips
                    .OrderBy(clip => clip.name, StringComparer.Ordinal)
                    .Select(clip => $"- '{clip.name}' (hideFlags: {clip.hideFlags})"));
        }

        private static AnimatorController RebuildController(
            string controllerPath,
            AnimationClip idleClip,
            AnimationClip runClip)
        {
            UnityEngine.Object existingMainAsset = AssetDatabase.LoadMainAssetAtPath(controllerPath);
            if (existingMainAsset != null && !(existingMainAsset is AnimatorController))
            {
                throw new InvalidOperationException(
                    $"Ya existe un asset que no es Animator Controller en la ruta requerida: '{controllerPath}'.");
            }

            AnimatorController controller = existingMainAsset as AnimatorController;
            if (controller == null)
            {
                controller = AnimatorController.CreateAnimatorControllerAtPath(controllerPath);
                if (controller == null)
                {
                    throw new InvalidOperationException($"No se pudo crear el Animator Controller '{controllerPath}'.");
                }
            }

            controller.parameters = Array.Empty<AnimatorControllerParameter>();
            controller.layers = Array.Empty<AnimatorControllerLayer>();
            EditorUtility.SetDirty(controller);
            AssetDatabase.SaveAssets();

            // Remove old controller sub-assets only after detaching them from the main asset.
            // This preserves the controller's path and GUID while avoiding orphan states/layers.
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
            AnimatorState idleState = stateMachine.AddState(IdleClipName, new Vector3(250f, 100f, 0f));
            AnimatorState runState = stateMachine.AddState(RunClipName, new Vector3(500f, 100f, 0f));
            idleState.motion = idleClip;
            runState.motion = runClip;
            stateMachine.defaultState = idleState;

            AnimatorStateTransition toRun = idleState.AddTransition(runState);
            toRun.hasExitTime = false;
            toRun.hasFixedDuration = true;
            toRun.duration = 0.08f;
            toRun.AddCondition(AnimatorConditionMode.Greater, 0.1f, "Speed");

            AnimatorStateTransition toIdle = runState.AddTransition(idleState);
            toIdle.hasExitTime = false;
            toIdle.hasFixedDuration = true;
            toIdle.duration = 0.08f;
            toIdle.AddCondition(AnimatorConditionMode.Less, 0.1f, "Speed");

            EditorUtility.SetDirty(controller);
            EditorUtility.SetDirty(stateMachine);
            EditorUtility.SetDirty(idleState);
            EditorUtility.SetDirty(runState);
            EditorUtility.SetDirty(toRun);
            EditorUtility.SetDirty(toIdle);
            AssetDatabase.SaveAssets();

            ValidateRebuiltController(controller, idleClip, runClip);
            return controller;
        }

        private static void ValidateRebuiltController(
            AnimatorController controller,
            AnimationClip idleClip,
            AnimationClip runClip)
        {
            Dictionary<string, AnimatorControllerParameter> parameters = controller.parameters
                .ToDictionary(parameter => parameter.name, StringComparer.Ordinal);

            bool parametersAreValid =
                parameters.Count == 3
                && parameters.TryGetValue("Speed", out AnimatorControllerParameter speed)
                && speed.type == AnimatorControllerParameterType.Float
                && parameters.TryGetValue("VerticalSpeed", out AnimatorControllerParameter verticalSpeed)
                && verticalSpeed.type == AnimatorControllerParameterType.Float
                && parameters.TryGetValue("Grounded", out AnimatorControllerParameter grounded)
                && grounded.type == AnimatorControllerParameterType.Bool
                && grounded.defaultBool;

            if (!parametersAreValid || controller.layers.Length != 1)
            {
                throw new InvalidOperationException("El Animator Controller reconstruido no superó la validación de parámetros o capas.");
            }

            AnimatorStateMachine stateMachine = controller.layers[0].stateMachine;
            ChildAnimatorState[] states = stateMachine.states;
            AnimatorState idleState = states.Select(child => child.state).SingleOrDefault(state => state.name == IdleClipName);
            AnimatorState runState = states.Select(child => child.state).SingleOrDefault(state => state.name == RunClipName);

            if (states.Length != 2
                || idleState == null
                || runState == null
                || idleState.motion != idleClip
                || runState.motion != runClip
                || stateMachine.defaultState != idleState
                || !HasExpectedTransition(idleState, runState, AnimatorConditionMode.Greater)
                || !HasExpectedTransition(runState, idleState, AnimatorConditionMode.Less))
            {
                throw new InvalidOperationException("El Animator Controller reconstruido no superó la validación de estados o transiciones.");
            }
        }

        private static bool HasExpectedTransition(
            AnimatorState source,
            AnimatorState destination,
            AnimatorConditionMode conditionMode)
        {
            AnimatorStateTransition[] matches = source.transitions
                .Where(transition => transition.destinationState == destination)
                .ToArray();

            if (matches.Length != 1)
            {
                return false;
            }

            AnimatorStateTransition transition = matches[0];
            AnimatorCondition[] conditions = transition.conditions;
            return !transition.hasExitTime
                && transition.hasFixedDuration
                && Mathf.Approximately(transition.duration, 0.08f)
                && conditions.Length == 1
                && conditions[0].mode == conditionMode
                && conditions[0].parameter == "Speed"
                && Mathf.Approximately(conditions[0].threshold, 0.1f);
        }

        private static float ReadRightFacingYaw(QusapAnimationDriver animationDriver)
        {
            SerializedObject serializedDriver = new SerializedObject(animationDriver);
            SerializedProperty property = serializedDriver.FindProperty("rightFacingYaw");
            if (property == null)
            {
                throw new InvalidOperationException("No se pudo leer rightFacingYaw de QusapAnimationDriver.");
            }

            return property.floatValue;
        }

        private static bool DriverWillFindAnimator(
            QusapAnimationDriver animationDriver,
            Transform newVisual,
            Animator configuredAnimator,
            Transform backupVisual)
        {
            Transform resolvedVisual = animationDriver.transform.Find(ActiveVisualName);
            return animationDriver.enabled
                && resolvedVisual == newVisual
                && resolvedVisual.gameObject.activeInHierarchy
                && resolvedVisual.GetComponent<Animator>() == configuredAnimator
                && backupVisual.name == BackupVisualName
                && !backupVisual.gameObject.activeInHierarchy;
        }

        private static string DescribeRendererDimensions(GameObject modelRoot)
        {
            Renderer[] renderers = modelRoot.GetComponentsInChildren<Renderer>(true);
            if (renderers.Length == 0)
            {
                return "No se encontraron componentes Renderer.";
            }

            Bounds combinedBounds = renderers[0].bounds;
            for (int index = 1; index < renderers.Length; index++)
            {
                combinedBounds.Encapsulate(renderers[index].bounds);
            }

            return $"{FormatVector(combinedBounds.size)} unidades (centro {FormatVector(combinedBounds.center)}, {renderers.Length} Renderer(s))";
        }

        private static string DescribeColliderDimensions(Collider playerCollider)
        {
            Bounds bounds = playerCollider.bounds;
            return $"{FormatVector(bounds.size)} unidades (centro {FormatVector(bounds.center)}, tipo {playerCollider.GetType().Name})";
        }

        private static string FormatVector(Vector3 value)
        {
            return $"X {value.x:0.###}, Y {value.y:0.###}, Z {value.z:0.###}";
        }

        private static string BuildTestScenePath(string sourceScenePath)
        {
            string directory = Path.GetDirectoryName(sourceScenePath);
            string sceneName = Path.GetFileNameWithoutExtension(sourceScenePath);
            return CombineAssetPath(directory, sceneName + TestSceneSuffix + ".unity");
        }

        private static string CombineAssetPath(string directory, string fileName)
        {
            return (directory + "/" + fileName).Replace('\\', '/');
        }

        private static bool CanWriteDestination(string assetPath, string description)
        {
            UnityEngine.Object existingAsset = AssetDatabase.LoadMainAssetAtPath(assetPath);
            if (existingAsset == null || AssetDatabase.IsOpenForEdit(assetPath, StatusQueryOptions.UseCachedIfPossible))
            {
                return true;
            }

            ShowError($"No se puede escribir {description} porque el asset no está disponible para edición:\n{assetPath}");
            return false;
        }

        private static void ShowError(string message)
        {
            Debug.LogError(message);
            EditorUtility.DisplayDialog(DialogTitle, message, "Aceptar");
        }
    }
}
#endif
