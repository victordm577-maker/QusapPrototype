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
    public static class QusapLuzDashTestCreator
    {
        private const string Folder = "Assets/Scenes/Art/Characters/Qusap/Final_Light_v3/";
        private const string FbxAssetPath = Folder + "Qusap_Luz_Locomotion_v12.fbx";
        private const string SourceFbxAssetPath = Folder + "Qusap_Luz_Locomotion_v11.fbx";
        private const string ExternalFbxPath =
            @"C:\Users\victo\Documents\Qusap\3D\Modelado\Qusap_Luz_Definitivo\03_Export_Unity\Qusap_Luz_Locomotion_v12.fbx";
        private const string TargetControllerPath = Folder + "Qusap_Luz_Animator_DashTest_v1.controller";
        private const string TargetScenePath = "Assets/_Qusap/Scenes/Qusap_Luz_Locomotion_DashTest_v1.unity";
        private const string ActiveVisualName = "PlayerVisual";
        private const string BackupVisualName = "PlayerVisual_WallJumpV11_Backup";
        private const string DialogTitle = "Qusap Luz Dash Test";
        private const float FrameTolerance = 0.01f;
        private const float SoleHeightTolerance = 0.01f;

        private static readonly ClipRequirement[] ClipRequirements =
        {
            new("Qusap_Idle", 120f, true),
            new("Qusap_Run", 36f, true),
            new("Qusap_JumpRise", 18f, false),
            new("Qusap_Fall", 24f, true),
            new("Qusap_Land", 18f, false),
            new("Qusap_WallSlide", 48f, true),
            new("Qusap_WallJump", 18f, false),
            // The FBX authoring range 1..11 is reported by Unity's Source Take as zero-based 0..10.
            new("Qusap_Dash", 10f, false, 0f, 10f)
        };

        // Explicit batch entry point. It never creates, saves or opens the Dash test scene.
        public static void PrepareAssets()
        {
            EnsureEditorIsIdle();
            string fbxPath = EnsureExactFbx();
            var importer = AssetImporter.GetAtPath(fbxPath) as ModelImporter;
            if (importer == null)
                throw new InvalidOperationException("El FBX v12 no utiliza ModelImporter.");
            Dictionary<string, AnimationClip> clips = ConfigureImporterAndLoadClips(importer);
            ValidateModelStructure(AssetDatabase.LoadAssetAtPath<GameObject>(fbxPath));

            string sourceScenePath = FindLatestWallJumpScene();
            Scene preview = EditorSceneManager.OpenPreviewScene(sourceScenePath);
            try
            {
                SceneContext context = ResolveSceneContext(preview, SourceFbxAssetPath, 7);
                var sourceController = context.Animator.runtimeAnimatorController as AnimatorController;
                ValidateRequiredParameters(sourceController, false);
                Dictionary<string, AnimatorState> sourceStates = ValidateStates(sourceController, 7);
                if (sourceStates.Values.Any(state => AssetDatabase.GetAssetPath(state.motion) != SourceFbxAssetPath))
                    throw new InvalidOperationException("Los siete Motion del controller WallJump deben usar v11.");

                string sourcePath = AssetDatabase.GetAssetPath(sourceController);
                string sourceHash = FileHash(AbsoluteAssetPath(sourcePath));
                string sourceSemantics = BuildBaseControllerSemantics(sourceController, null);
                var targetController = AssetDatabase.LoadAssetAtPath<AnimatorController>(TargetControllerPath);
                if (targetController == null)
                {
                    if (File.Exists(AbsoluteAssetPath(TargetControllerPath))
                        || File.Exists(AbsoluteAssetPath(TargetControllerPath) + ".meta"))
                        throw new InvalidOperationException(
                            "El destino controller existe pero no es un AnimatorController válido; no se sobrescribirá.");
                    if (!AssetDatabase.CopyAsset(sourcePath, TargetControllerPath))
                        throw new InvalidOperationException("Unity no pudo crear el controller Dash.");
                    targetController = AssetDatabase.LoadAssetAtPath<AnimatorController>(TargetControllerPath);
                    if (targetController == null
                        || BuildBaseControllerSemantics(targetController, null) != sourceSemantics)
                        throw new InvalidOperationException("La copia inicial no conserva el controller WallJump.");
                    Dictionary<string, AnimatorState> targetStates = ValidateStates(targetController, 7);
                    foreach (KeyValuePair<string, AnimatorState> pair in targetStates)
                    {
                        pair.Value.motion = clips[pair.Key];
                        EditorUtility.SetDirty(pair.Value);
                    }
                    AddDash(targetController, targetStates, clips["Qusap_Dash"]);
                    EditorUtility.SetDirty(targetController);
                    AssetDatabase.SaveAssetIfDirty(targetController);
                    targetController = AssetDatabase.LoadAssetAtPath<AnimatorController>(TargetControllerPath);
                }
                ValidateDashController(targetController, clips, sourceSemantics);

                if (FileHash(AbsoluteAssetPath(sourcePath)) != sourceHash)
                    throw new InvalidOperationException("El controller WallJump original cambió.");
                Debug.Log("Dash v12: FBX, ocho clips y controller verificados. La escena Dash NO se creó.");
            }
            finally
            {
                EditorSceneManager.ClosePreviewScene(preview);
            }
        }

        // Uses only disposable preview objects. It does not invoke the scene-creation menu.
        public static void ValidatePreparedAssets()
        {
            EnsureEditorIsIdle();
            var controller = AssetDatabase.LoadAssetAtPath<AnimatorController>(TargetControllerPath);
            Dictionary<string, AnimationClip> clips = LoadPublicClips();
            ValidateDashController(controller, clips, null);
            ValidateModelStructure(AssetDatabase.LoadAssetAtPath<GameObject>(FbxAssetPath));

            string scenePath = FindLatestWallJumpScene();
            string sceneHash = FileHash(AbsoluteAssetPath(scenePath));
            string dashScriptHash = FileHash(AbsoluteAssetPath("Assets/_Qusap/Scripts/Movement/QusapDashMotor.cs"));
            string combatScriptHash = FileHash(AbsoluteAssetPath("Assets/_Qusap/Scripts/Combat/QusapCombatController.cs"));
            Scene preview = EditorSceneManager.OpenPreviewScene(scenePath);
            try
            {
                SceneContext context = ResolveSceneContext(preview, SourceFbxAssetPath, 7);
                var oldStates = ValidateStates((AnimatorController)context.Animator.runtimeAnimatorController, 7);
                Vector2 oldSoles = MeasureSoleHeight(
                    context.VisualRoot, (AnimationClip)oldStates["Qusap_Idle"].motion);
                GameObject visual = InstantiateVisual(
                    AssetDatabase.LoadAssetAtPath<GameObject>(FbxAssetPath), context.PlayerRoot,
                    context.VisualRoot.localPosition, context.VisualRoot.localRotation, context.VisualRoot.localScale);
                ValidateInPlaceClips(visual.transform, clips);
                Vector2 newSoles = MeasureSoleHeight(visual.transform, clips["Qusap_Idle"]);
                Require(MaxSoleDifference(oldSoles, newSoles) <= SoleHeightTolerance, "altura de suelas v11/v12");

                context.VisualRoot.name = BackupVisualName;
                context.VisualRoot.gameObject.SetActive(false);
                visual.name = ActiveVisualName;
                Animator animator = ConfigureAnimator(visual, controller);
                QusapAnimationDriver driver = context.Driver;
                QusapDashMotor dash = driver.GetComponent<QusapDashMotor>();
                Rigidbody body = driver.GetComponent<Rigidbody>();
                QusapGroundSensor ground = driver.GetComponent<QusapGroundSensor>();
                string dashJson = EditorJsonUtility.ToJson(dash);
                Quaternion physicalRotation = driver.transform.localRotation;

                InvokePrivate(driver, "Awake");
                InvokePrivate(driver, "OnEnable");
                foreach (float direction in new[] { -1f, 1f })
                {
                    animator.Play("Qusap_Idle", 0, 0f);
                    animator.Update(0f);
                    body.linearVelocity = new Vector3(direction * 12.5f, 0f, 0f);
                    SetPrivate(dash, "<IsDashing>k__BackingField", true);
                    InvokePrivate(driver, "Update");
                    Require(animator.GetBool("Dashing"), "Dashing=true desde IsDashing");
                    float expectedYaw = direction > 0f ? 150f : 210f;
                    Require(Mathf.Approximately((float)GetPrivate(driver, "targetFacingYaw"), expectedYaw),
                        "orientación inicial del Dash");
                    body.linearVelocity = new Vector3(-direction * 12.5f, 0f, 0f);
                    InvokePrivate(driver, "Update");
                    Require(Mathf.Approximately((float)GetPrivate(driver, "targetFacingYaw"), expectedYaw),
                        "orientación bloqueada durante el Dash");
                    animator.Update(0.01f);
                    animator.Update(0.03f);
                    Require(animator.GetCurrentAnimatorStateInfo(0).IsName("Qusap_Dash"), "Any State a Dash");
                    animator.Update(0.22f);
                    Require(animator.GetCurrentAnimatorStateInfo(0).IsName("Qusap_Dash"),
                        "último frame conservado mientras Dashing=true");
                    SetPrivate(dash, "<IsDashing>k__BackingField", false);
                    InvokePrivate(driver, "Update");
                    Require(!animator.GetBool("Dashing"), "Dashing=false inmediato");
                }

                ValidateDashExits(animator, ground);
                Require(driver.transform.localRotation == physicalRotation, "raíz física sin rotación");
                Require(EditorJsonUtility.ToJson(dash) == dashJson, "valores serializados del Dash intactos");
                Require(FileHash(AbsoluteAssetPath(scenePath)) == sceneHash, "escena WallJump intacta");
                Require(FileHash(AbsoluteAssetPath("Assets/_Qusap/Scripts/Movement/QusapDashMotor.cs")) == dashScriptHash,
                    "script del Dash intacto");
                Require(FileHash(AbsoluteAssetPath("Assets/_Qusap/Scripts/Combat/QusapCombatController.cs")) == combatScriptHash,
                    "bloqueo de ataques intacto");
                string report = $"PASS Unity {Application.unityVersion}\n"
                    + $"Sole delta: {MaxSoleDifference(oldSoles, newSoles):R}\n"
                    + "3 meshes, 1 armature, 4 bones; 8 clips; Dashing mirrors IsDashing; "
                    + "orientation locked for both directions; Dash holds while true; 4 exits verified; "
                    + "Dash serialization, combat script and WallJump scene unchanged. No Dash scene created or saved.";
                File.WriteAllText(AbsoluteAssetPath("Library/Dash-v12-validation.txt"), report);
                Debug.Log(report);
            }
            finally
            {
                EditorSceneManager.ClosePreviewScene(preview);
            }
        }

        [MenuItem("Tools/Qusap/Create Qusap Luz Dash Test")]
        private static void CreateDashTest()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode || EditorApplication.isCompiling
                || EditorApplication.isUpdating || AnimationMode.InAnimationMode())
            {
                ShowError("Sal de Play Mode/Animation Mode y espera a que termine la compilación.");
                return;
            }
            for (int index = 0; index < SceneManager.sceneCount; index++)
            {
                Scene openScene = SceneManager.GetSceneAt(index);
                if (openScene.isDirty)
                {
                    ShowError($"Guarda o descarta manualmente los cambios de '{openScene.name}'.");
                    return;
                }
            }
            if (File.Exists(AbsoluteAssetPath(TargetScenePath))
                || File.Exists(AbsoluteAssetPath(TargetScenePath) + ".meta"))
            {
                ShowError("La escena Dash ya existe y no se sobrescribirá:\n" + TargetScenePath);
                return;
            }

            try
            {
                PrepareAssets();
                string sourceScenePath = FindLatestWallJumpScene();
                string sourceSceneHash = FileHash(AbsoluteAssetPath(sourceScenePath));
                if (!AssetDatabase.CopyAsset(sourceScenePath, TargetScenePath))
                    throw new InvalidOperationException("Unity no pudo copiar la escena WallJump.");
                Scene scene = EditorSceneManager.OpenScene(TargetScenePath, OpenSceneMode.Single);
                SceneContext context = ResolveSceneContext(scene, SourceFbxAssetPath, 7);
                Dictionary<Component, string> protectedComponents = CaptureProtectedComponents(scene, context.VisualRoot);
                string dashJson = EditorJsonUtility.ToJson(context.PlayerRoot.GetComponent<QusapDashMotor>());
                var oldController = (AnimatorController)context.Animator.runtimeAnimatorController;
                string sourceControllerPath = AssetDatabase.GetAssetPath(oldController);
                string sourceControllerHash = FileHash(AbsoluteAssetPath(sourceControllerPath));
                Dictionary<string, AnimatorState> oldStates = ValidateStates(oldController, 7);
                Vector2 oldSoles = MeasureSoleHeight(
                    context.VisualRoot, (AnimationClip)oldStates["Qusap_Idle"].motion);
                Vector3 position = context.VisualRoot.localPosition;
                Quaternion rotation = context.VisualRoot.localRotation;
                Vector3 scale = context.VisualRoot.localScale;
                if (scale.x <= 0f || scale.y <= 0f || scale.z <= 0f
                    || context.PlayerRoot.Find(BackupVisualName) != null)
                    throw new InvalidOperationException("Escala no positiva o respaldo v11 ya existente.");

                context.VisualRoot.name = BackupVisualName;
                context.VisualRoot.gameObject.SetActive(false);
                PrefabUtility.RecordPrefabInstancePropertyModifications(context.VisualRoot.gameObject);
                GameObject visual = InstantiateVisual(
                    AssetDatabase.LoadAssetAtPath<GameObject>(FbxAssetPath), context.PlayerRoot,
                    position, rotation, scale);
                Animator animator = ConfigureAnimator(
                    visual, AssetDatabase.LoadAssetAtPath<AnimatorController>(TargetControllerPath));
                Dictionary<string, AnimationClip> clips = LoadPublicClips();
                ValidateInPlaceClips(visual.transform, clips);
                Vector2 newSoles = MeasureSoleHeight(visual.transform, clips["Qusap_Idle"]);
                ValidateInstalledVisual(context, visual.transform, animator, position, rotation, scale,
                    oldSoles, newSoles);
                ValidateProtectedComponents(protectedComponents);
                if (EditorJsonUtility.ToJson(context.PlayerRoot.GetComponent<QusapDashMotor>()) != dashJson)
                    throw new InvalidOperationException("La configuración mecánica del Dash cambió.");
                if (FileHash(AbsoluteAssetPath(sourceScenePath)) != sourceSceneHash
                    || FileHash(AbsoluteAssetPath(sourceControllerPath)) != sourceControllerHash)
                    throw new InvalidOperationException("La escena WallJump original cambió.");

                EditorSceneManager.MarkSceneDirty(scene);
                if (!EditorSceneManager.SaveScene(scene))
                    throw new InvalidOperationException("Unity no pudo guardar la copia Dash.");
                Selection.activeGameObject = visual;
                string report = $"Escena creada: {TargetScenePath}\n"
                    + $"Controller creado: {TargetControllerPath}\n"
                    + "Ocho clips encontrados; Dash: autoría 1-11 / Source Take Unity 0-10, "
                    + "10 efectivos, 0.166667 s a 60 FPS.\n"
                    + "Proveedor real: QusapDashMotor.IsDashing.\n"
                    + $"Visual anterior respaldado: {BackupVisualName}\n"
                    + $"Posición local preservada: {FormatVector(position)}\n"
                    + $"Diferencia máxima de suelas: {MaxSoleDifference(oldSoles, newSoles):0.#########}\n"
                    + "Apply Root Motion desactivado. Configuración mecánica del Dash intacta.";
                Debug.Log(report, visual);
                EditorUtility.DisplayDialog(DialogTitle, report, "Aceptar");
            }
            catch (Exception exception)
            {
                Debug.LogException(exception);
                ShowError("No se completó la prueba Dash. Los originales permanecen intactos; "
                    + "revisa la copia parcial y la consola.\n\n" + exception.Message);
            }
        }

        private static string EnsureExactFbx()
        {
            if (!File.Exists(ExternalFbxPath))
                throw new FileNotFoundException("No existe el FBX v12 externo.", ExternalFbxPath);
            string destination = AbsoluteAssetPath(FbxAssetPath);
            if (!File.Exists(destination))
            {
                if (File.Exists(destination + ".meta"))
                    throw new InvalidOperationException("Existe un .meta huérfano para v12.");
                string staging = AbsoluteAssetPath("Library/QusapDashV12-" + Guid.NewGuid() + ".tmp");
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
            if (FileHash(ExternalFbxPath) != FileHash(destination))
                throw new InvalidOperationException("El FBX v12 de destino no coincide con el origen; no se reemplazará.");
            AssetDatabase.ImportAsset(FbxAssetPath, ImportAssetOptions.ForceSynchronousImport);
            return FbxAssetPath;
        }

        private static Dictionary<string, AnimationClip> ConfigureImporterAndLoadClips(ModelImporter importer)
        {
            var reference = AssetImporter.GetAtPath(SourceFbxAssetPath) as ModelImporter;
            if (reference == null)
                throw new InvalidOperationException("Falta el ModelImporter v11 de referencia.");
            CopyImporterSettings(reference, importer);
            ModelImporterClipAnimation[] sourceTakes = importer.defaultClipAnimations;
            if (sourceTakes == null || sourceTakes.Length == 0)
                throw new InvalidOperationException("El FBX v12 no contiene Source Takes.");
            foreach (ModelImporterClipAnimation take in sourceTakes.Where(take => take != null))
                Debug.Log($"v12 Source Take: {take.name} | {take.takeName} | {take.firstFrame:R}..{take.lastFrame:R}");

            ModelImporterClipAnimation[] candidates = sourceTakes
                .Where(take => take != null && !IsPreviewName(take.name) && !IsPreviewName(take.takeName)
                    && !HasNumberedDuplicateName(take.name) && !HasNumberedDuplicateName(take.takeName))
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
                if (Mathf.Abs(source.lastFrame - source.firstFrame - requirement.ExpectedFrames) > FrameTolerance
                    || (requirement.ExpectedFirstFrame.HasValue
                        && (Mathf.Abs(source.firstFrame - requirement.ExpectedFirstFrame.Value) > FrameTolerance
                            || Mathf.Abs(source.lastFrame - requirement.ExpectedLastFrame.Value) > FrameTolerance)))
                    throw new InvalidOperationException(
                        $"{requirement.Name}: rango real {source.firstFrame:R}..{source.lastFrame:R}, "
                        + $"esperado {requirement.ExpectedFirstFrame ?? source.firstFrame:R}.."
                        + $"{requirement.ExpectedLastFrame ?? source.lastFrame:R}.");
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

            importer = AssetImporter.GetAtPath(FbxAssetPath) as ModelImporter;
            if (importer == null || importer.animationType != ModelImporterAnimationType.Generic
                || importer.avatarSetup != ModelImporterAvatarSetup.CreateFromThisModel
                || !importer.importAnimation || importer.animationCompression != ModelImporterAnimationCompression.Off
                || !ImporterSettingsMatch(reference, importer))
                throw new InvalidOperationException("El ModelImporter v12 no conservó la configuración de v11.");
            Dictionary<string, AnimationClip> clips = LoadPublicClips();
            ModelImporterClipAnimation[] imported = importer.clipAnimations;
            if (clips.Count != 8 || imported.Length != 8
                || ClipRequirements.Any(requirement => clips.Count(pair => pair.Key == requirement.Name) != 1
                    || imported.Count(clip => clip.name == requirement.Name
                        && Mathf.Abs(clip.lastFrame - clip.firstFrame - requirement.ExpectedFrames) <= FrameTolerance
                        && clip.loopTime == requirement.Loop && clip.loopPose == requirement.Loop
                        && clip.lockRootRotation && clip.lockRootHeightY && clip.lockRootPositionXZ
                        && clip.keepOriginalOrientation && clip.keepOriginalPositionY
                        && clip.keepOriginalPositionXZ) != 1))
                throw new InvalidOperationException("La reimportación no produjo los ocho clips exactos con Root Motion bloqueado.");
            foreach (ClipRequirement requirement in ClipRequirements)
            {
                AnimationClip clip = clips[requirement.Name];
                float frames = clip.length * clip.frameRate;
                if (Mathf.Abs(frames - requirement.ExpectedFrames) > FrameTolerance)
                    throw new InvalidOperationException($"{clip.name}: {frames:R} frames efectivos.");
                Debug.Log($"v12 clip validado: {clip.name}: {clip.length:R}s, {clip.frameRate:R} FPS, {frames:R} frames");
            }
            AnimationClip dash = clips["Qusap_Dash"];
            if (Mathf.Abs(dash.frameRate - 60f) > FrameTolerance
                || Mathf.Abs(dash.length - 10f / 60f) > 0.0001f)
                throw new InvalidOperationException("Qusap_Dash debe durar 10/60 segundos a 60 FPS.");
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

        private static ModelImporterClipAnimation CreateClipDefinition(
            ClipRequirement requirement, ModelImporterClipAnimation source)
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

        private static void AddDash(AnimatorController controller,
            IReadOnlyDictionary<string, AnimatorState> states, AnimationClip clip)
        {
            if (controller.parameters.Any(parameter => parameter.name == "Dashing"))
                throw new InvalidOperationException("El controller WallJump ya contiene Dashing.");
            AnimatorStateMachine machine = controller.layers[0].stateMachine;
            controller.AddParameter("Dashing", AnimatorControllerParameterType.Bool);
            foreach (AnimatorStateTransition transition in machine.anyStateTransitions
                .Concat(states.Values.SelectMany(state => state.transitions)))
            {
                if (transition.destinationState != null && states.Values.Contains(transition.destinationState))
                {
                    transition.AddCondition(AnimatorConditionMode.IfNot, 0f, "Dashing");
                    EditorUtility.SetDirty(transition);
                }
            }

            AnimatorState dash = machine.AddState("Qusap_Dash", new Vector3(1040f, 80f, 0f));
            dash.motion = clip;
            dash.writeDefaultValues = states["Qusap_Run"].writeDefaultValues;
            AnimatorStateTransition enter = machine.AddAnyStateTransition(dash);
            ConfigureTransition(enter, 0.02f);
            enter.AddCondition(AnimatorConditionMode.If, 0f, "Dashing");
            machine.anyStateTransitions = new[] { enter }
                .Concat(machine.anyStateTransitions.Where(transition => transition != enter)).ToArray();
            AddDashExit(dash, states["Qusap_Run"],
                Condition("Dashing", AnimatorConditionMode.IfNot),
                Condition("Grounded", AnimatorConditionMode.If),
                Condition("Speed", AnimatorConditionMode.Greater, 0.1f));
            AddDashExit(dash, states["Qusap_Idle"],
                Condition("Dashing", AnimatorConditionMode.IfNot),
                Condition("Grounded", AnimatorConditionMode.If),
                Condition("Speed", AnimatorConditionMode.Less, 0.1f));
            AddDashExit(dash, states["Qusap_JumpRise"],
                Condition("Dashing", AnimatorConditionMode.IfNot),
                Condition("Grounded", AnimatorConditionMode.IfNot),
                Condition("VerticalSpeed", AnimatorConditionMode.Greater, 0.1f));
            AddDashExit(dash, states["Qusap_Fall"],
                Condition("Dashing", AnimatorConditionMode.IfNot),
                Condition("Grounded", AnimatorConditionMode.IfNot),
                Condition("VerticalSpeed", AnimatorConditionMode.Less, 0.1f));
            foreach (UnityEngine.Object item in new UnityEngine.Object[] { machine, dash, enter })
                EditorUtility.SetDirty(item);
        }

        private static void AddDashExit(AnimatorState source, AnimatorState destination,
            params AnimatorCondition[] conditions)
        {
            AnimatorStateTransition transition = source.AddTransition(destination);
            ConfigureTransition(transition, 0.03f);
            foreach (AnimatorCondition condition in conditions)
                transition.AddCondition(condition.mode, condition.threshold, condition.parameter);
            EditorUtility.SetDirty(transition);
        }

        private static void ValidateDashController(AnimatorController controller,
            IReadOnlyDictionary<string, AnimationClip> clips, string sourceSemantics)
        {
            Dictionary<string, AnimatorState> states = ValidateStates(controller, 8);
            ValidateRequiredParameters(controller, true);
            if (states.Any(pair => pair.Value.motion != clips[pair.Key]))
                throw new InvalidOperationException("Los ocho estados no utilizan los ocho Motion v12.");
            AnimatorStateMachine machine = controller.layers[0].stateMachine;
            AnimatorState dash = states["Qusap_Dash"];
            AnimatorStateTransition[] entries = machine.anyStateTransitions
                .Where(transition => transition.destinationState == dash).ToArray();
            if (entries.Length != 1 || machine.anyStateTransitions[0] != entries[0]
                || entries[0].hasExitTime || !entries[0].hasFixedDuration
                || !Mathf.Approximately(entries[0].duration, 0.02f) || entries[0].canTransitionToSelf
                || !HasConditions(entries[0], Condition("Dashing", AnimatorConditionMode.If)))
                throw new InvalidOperationException("Any State -> Dash no conserva su configuración exacta.");
            if (states.Values.Any(state => state.transitions.Any(transition => transition.destinationState == state))
                || dash.transitions.Length != 4)
                throw new InvalidOperationException("Hay una auto-transición o salidas Dash duplicadas.");
            ValidateDashExit(dash, states["Qusap_Run"], "Speed", AnimatorConditionMode.Greater);
            ValidateDashExit(dash, states["Qusap_Idle"], "Speed", AnimatorConditionMode.Less);
            ValidateDashExit(dash, states["Qusap_JumpRise"], "VerticalSpeed", AnimatorConditionMode.Greater);
            ValidateDashExit(dash, states["Qusap_Fall"], "VerticalSpeed", AnimatorConditionMode.Less);
            foreach (AnimatorStateTransition transition in machine.anyStateTransitions
                .Concat(states.Values.Where(state => state != dash).SelectMany(state => state.transitions)))
            {
                if (transition.destinationState != null && transition.destinationState != dash
                    && states.Values.Contains(transition.destinationState)
                    && !transition.conditions.Any(condition => condition.parameter == "Dashing"
                        && condition.mode == AnimatorConditionMode.IfNot))
                    throw new InvalidOperationException(
                        $"Una transición general a {transition.destinationState.name} puede interrumpir Dash.");
            }
            if (sourceSemantics != null
                && BuildBaseControllerSemantics(controller, dash) != sourceSemantics)
                throw new InvalidOperationException("El controller Dash no conserva la estructura funcional WallJump.");
        }

        private static void ValidateDashExit(AnimatorState dash, AnimatorState destination,
            string motionParameter, AnimatorConditionMode motionMode)
        {
            AnimatorStateTransition[] matches = dash.transitions
                .Where(transition => transition.destinationState == destination).ToArray();
            AnimatorCondition grounded = destination.name == "Qusap_Run" || destination.name == "Qusap_Idle"
                ? Condition("Grounded", AnimatorConditionMode.If)
                : Condition("Grounded", AnimatorConditionMode.IfNot);
            if (matches.Length != 1 || matches[0].hasExitTime || !matches[0].hasFixedDuration
                || !Mathf.Approximately(matches[0].duration, 0.03f) || matches[0].canTransitionToSelf
                || !HasConditions(matches[0], Condition("Dashing", AnimatorConditionMode.IfNot),
                    grounded, Condition(motionParameter, motionMode, 0.1f)))
                throw new InvalidOperationException($"Dash -> {destination.name} no es exacta.");
        }

        private static string FindLatestWallJumpScene()
        {
            string[] paths = AssetDatabase.FindAssets("WallJumpTest t:Scene", new[] { "Assets" })
                .Select(AssetDatabase.GUIDToAssetPath)
                .Where(path => Regex.IsMatch(Path.GetFileName(path),
                    @"^Qusap_Luz_Locomotion_WallJumpTest_v\d+\.unity$"))
                .OrderByDescending(path => File.GetLastWriteTimeUtc(AbsoluteAssetPath(path))).ToArray();
            foreach (string path in paths)
            {
                Scene preview = EditorSceneManager.OpenPreviewScene(path);
                try
                {
                    SceneContext context = ResolveSceneContext(preview, SourceFbxAssetPath, 7);
                    var controller = context.Animator.runtimeAnimatorController as AnimatorController;
                    ValidateRequiredParameters(controller, false);
                    ValidateStates(controller, 7);
                    return path;
                }
                catch (InvalidOperationException) { }
                finally { EditorSceneManager.ClosePreviewScene(preview); }
            }
            throw new InvalidOperationException("No se encontró una escena WallJump funcional con visual v11.");
        }

        private static SceneContext ResolveSceneContext(Scene scene, string expectedFbx, int expectedStates)
        {
            QusapAnimationDriver[] drivers = scene.GetRootGameObjects()
                .SelectMany(root => root.GetComponentsInChildren<QusapAnimationDriver>(false))
                .Where(driver => driver != null && driver.isActiveAndEnabled).ToArray();
            if (drivers.Length != 1)
                throw new InvalidOperationException($"Se esperaba un QusapAnimationDriver activo; encontrados: {drivers.Length}.");
            QusapAnimationDriver driver = drivers[0];
            Transform root = driver.transform;
            if (root.GetComponent<Rigidbody>() == null || root.GetComponent<QusapGroundSensor>() == null
                || root.GetComponent<QusapInputReader>() == null || root.GetComponent<QusapVerticalMotor>() == null
                || root.GetComponent<QusapWallSensor>() == null || root.GetComponent<QusapDashMotor>() == null)
                throw new InvalidOperationException("La raíz física no conserva sus componentes funcionales.");
            Animator[] animators = root.GetComponentsInChildren<Animator>(false)
                .Where(animator => animator.isActiveAndEnabled).ToArray();
            if (animators.Length != 1)
                throw new InvalidOperationException($"Se esperaba un Animator activo; encontrados: {animators.Length}.");
            Animator animator = animators[0];
            Transform visual = animator.transform;
            while (visual != null && visual.parent != root) visual = visual.parent;
            if (visual == null || visual.name != ActiveVisualName || visual.GetComponent<Animator>() != animator
                || root.Cast<Transform>().Count(child => child.name == ActiveVisualName) != 1
                || PrefabUtility.GetPrefabAssetPathOfNearestInstanceRoot(visual.gameObject) != expectedFbx)
                throw new InvalidOperationException("PlayerVisual activo no es la instancia esperada.");
            ValidateStates(animator.runtimeAnimatorController as AnimatorController, expectedStates);
            return new SceneContext(driver, root, visual, animator);
        }

        private static Dictionary<string, AnimatorState> ValidateStates(AnimatorController controller, int expectedCount)
        {
            if (controller == null || controller.layers.Length != 1
                || controller.layers[0].stateMachine.stateMachines.Length != 0)
                throw new InvalidOperationException("El Animator Controller debe tener una capa sin submáquinas.");
            AnimatorState[] states = EnumerateStates(controller.layers[0].stateMachine).ToArray();
            if (states.Length != expectedCount || ClipRequirements.Take(expectedCount)
                .Any(requirement => states.Count(state => state.name == requirement.Name) != 1))
                throw new InvalidOperationException($"Se esperaban {expectedCount} estados exactos sin duplicados.");
            return states.ToDictionary(state => state.name, StringComparer.Ordinal);
        }

        private static void ValidateRequiredParameters(AnimatorController controller, bool includeDashing)
        {
            Dictionary<string, AnimatorControllerParameter> parameters = controller.parameters
                .ToDictionary(parameter => parameter.name, StringComparer.Ordinal);
            int expectedCount = includeDashing ? 6 : 5;
            if (parameters.Count != expectedCount
                || !HasParameter(parameters, "Speed", AnimatorControllerParameterType.Float)
                || !HasParameter(parameters, "VerticalSpeed", AnimatorControllerParameterType.Float)
                || !HasParameter(parameters, "Grounded", AnimatorControllerParameterType.Bool)
                || !HasParameter(parameters, "WallSliding", AnimatorControllerParameterType.Bool)
                || !HasParameter(parameters, "WallJumping", AnimatorControllerParameterType.Bool)
                || (includeDashing && (!HasParameter(parameters, "Dashing", AnimatorControllerParameterType.Bool)
                    || parameters["Dashing"].defaultBool)))
                throw new InvalidOperationException("Los parámetros del controller no son exactos.");
        }

        private static string BuildBaseControllerSemantics(AnimatorController controller, AnimatorState dash)
        {
            AnimatorStateMachine machine = controller.layers[0].stateMachine;
            var records = new List<string>();
            foreach (AnimatorStateTransition transition in machine.anyStateTransitions
                .Where(transition => transition.destinationState != dash))
                records.Add(TransitionRecord("ANY", transition));
            foreach (AnimatorState state in EnumerateStates(machine).Where(state => state != dash))
            {
                records.Add($"STATE|{state.name}|{state.speed:R}|{state.cycleOffset:R}|{state.mirror}|"
                    + $"{state.iKOnFeet}|{state.writeDefaultValues}|{state.tag}");
                foreach (AnimatorStateTransition transition in state.transitions
                    .Where(transition => transition.destinationState != dash))
                    records.Add(TransitionRecord(state.name, transition));
            }
            return string.Join("\n", records.OrderBy(record => record, StringComparer.Ordinal));
        }

        private static string TransitionRecord(string source, AnimatorStateTransition transition)
        {
            string conditions = string.Join(",", transition.conditions
                .Where(condition => condition.parameter != "Dashing")
                .Select(condition => $"{condition.parameter}:{condition.mode}:{condition.threshold:R}")
                .OrderBy(value => value, StringComparer.Ordinal));
            return $"TRANS|{source}|{transition.destinationState?.name}|{transition.hasExitTime}|"
                + $"{transition.exitTime:R}|{transition.hasFixedDuration}|{transition.duration:R}|"
                + $"{transition.offset:R}|{transition.interruptionSource}|{transition.orderedInterruption}|"
                + $"{transition.canTransitionToSelf}|{transition.mute}|{transition.solo}|{conditions}";
        }

        private static void ValidateModelStructure(GameObject model)
        {
            if (model == null) throw new InvalidOperationException("No se pudo cargar el modelo v12.");
            SkinnedMeshRenderer[] skinned = model.GetComponentsInChildren<SkinnedMeshRenderer>(true);
            MeshFilter[] staticMeshes = model.GetComponentsInChildren<MeshFilter>(true);
            Transform[] distinctBones = skinned.SelectMany(renderer => renderer.bones)
                .Where(bone => bone != null).Distinct().ToArray();
            int bones = distinctBones.Length;
            int armatures = distinctBones.Select(bone =>
            {
                Transform root = bone;
                while (root.parent != null && root.parent != model.transform) root = root.parent;
                return root;
            }).Distinct().Count();
            if (skinned.Length + staticMeshes.Length != 3 || armatures != 1 || bones != 4)
                throw new InvalidOperationException(
                    $"Estructura v12 inesperada: {skinned.Length + staticMeshes.Length} mallas, "
                    + $"{armatures} armaduras, {bones} huesos.");
            Debug.Log("v12 estructura validada: 3 mallas, 1 armadura, 4 huesos.");
        }

        private static void ValidateInPlaceClips(Transform visual, IReadOnlyDictionary<string, AnimationClip> clips)
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
                    for (int frame = 0; frame <= Mathf.CeilToInt(requirement.ExpectedFrames); frame++)
                    {
                        AnimationMode.BeginSampling();
                        try
                        {
                            AnimationMode.SampleAnimationClip(visual.gameObject, clip,
                                Mathf.Min(frame / clip.frameRate, clip.length));
                        }
                        finally { AnimationMode.EndSampling(); }
                        if (visual.localPosition != position || visual.localRotation != rotation
                            || visual.localScale != scale)
                            throw new InvalidOperationException($"{clip.name}, frame {frame}: altera la raíz visual.");
                    }
                }
            }
            finally { AnimationMode.StopAnimationMode(); }
        }

        private static Vector2 MeasureSoleHeight(Transform visual, AnimationClip idle)
        {
            SkinnedMeshRenderer[] renderers = visual.GetComponentsInChildren<SkinnedMeshRenderer>(false)
                .Where(renderer => renderer.enabled).ToArray();
            SkinnedMeshRenderer left = FindFootRenderer(renderers, "FloatingFoot_L");
            SkinnedMeshRenderer right = FindFootRenderer(renderers, "FloatingFoot_R");
            Vector3 position = visual.localPosition;
            Quaternion rotation = visual.localRotation;
            Vector3 scale = visual.localScale;
            AnimationMode.StartAnimationMode();
            try
            {
                AnimationMode.BeginSampling();
                try { AnimationMode.SampleAnimationClip(visual.gameObject, idle, 0f); }
                finally { AnimationMode.EndSampling(); }
                if (visual.localPosition != position || visual.localRotation != rotation || visual.localScale != scale)
                    throw new InvalidOperationException("Idle altera la transformación raíz.");
                return new Vector2(BakeLowestWorldY(left), BakeLowestWorldY(right));
            }
            finally { AnimationMode.StopAnimationMode(); }
        }

        private static SkinnedMeshRenderer FindFootRenderer(
            IEnumerable<SkinnedMeshRenderer> renderers, string footName)
        {
            SkinnedMeshRenderer[] matches = renderers.Where(renderer =>
                renderer.name == footName || renderer.name == footName + "_Mesh"
                || (renderer.sharedMesh != null && (renderer.sharedMesh.name == footName
                    || renderer.sharedMesh.name == footName + "_Mesh"))).ToArray();
            if (matches.Length != 1)
                throw new InvalidOperationException($"Se esperaba un renderer único para {footName}.");
            return matches[0];
        }

        private static float BakeLowestWorldY(SkinnedMeshRenderer renderer)
        {
            Mesh mesh = new Mesh { hideFlags = HideFlags.HideAndDontSave };
            try
            {
                renderer.BakeMesh(mesh, false);
                var vertices = new List<Vector3>(mesh.vertexCount);
                mesh.GetVertices(vertices);
                if (vertices.Count == 0) throw new InvalidOperationException("El pie no produjo vértices.");
                return vertices.Min(vertex => renderer.transform.TransformPoint(vertex).y);
            }
            finally { UnityEngine.Object.DestroyImmediate(mesh); }
        }

        private static GameObject InstantiateVisual(GameObject model, Transform parent,
            Vector3 position, Quaternion rotation, Vector3 scale)
        {
            GameObject visual = PrefabUtility.InstantiatePrefab(model, parent) as GameObject;
            if (visual == null) throw new InvalidOperationException("Unity no pudo instanciar v12.");
            visual.name = ActiveVisualName;
            visual.SetActive(true);
            visual.transform.localPosition = position;
            visual.transform.localRotation = rotation;
            visual.transform.localScale = scale;
            return visual;
        }

        private static Animator ConfigureAnimator(GameObject visual, AnimatorController controller)
        {
            Animator animator = visual.GetComponent<Animator>() ?? visual.AddComponent<Animator>();
            animator.enabled = true;
            animator.runtimeAnimatorController = controller;
            animator.applyRootMotion = false;
            animator.cullingMode = AnimatorCullingMode.AlwaysAnimate;
            PrefabUtility.RecordPrefabInstancePropertyModifications(visual);
            PrefabUtility.RecordPrefabInstancePropertyModifications(visual.transform);
            PrefabUtility.RecordPrefabInstancePropertyModifications(animator);
            return animator;
        }

        private static void ValidateInstalledVisual(SceneContext context, Transform visual, Animator animator,
            Vector3 position, Quaternion rotation, Vector3 scale, Vector2 oldSoles, Vector2 newSoles)
        {
            Transform resolved = context.Driver.transform.Find(ActiveVisualName);
            if (resolved != visual || resolved.GetComponent<Animator>() != animator
                || !resolved.gameObject.activeInHierarchy || animator.applyRootMotion
                || animator.runtimeAnimatorController != AssetDatabase.LoadAssetAtPath<AnimatorController>(TargetControllerPath)
                || visual.localPosition != position || visual.localRotation != rotation || visual.localScale != scale
                || context.VisualRoot.name != BackupVisualName || context.VisualRoot.gameObject.activeSelf
                || PrefabUtility.GetPrefabAssetPathOfNearestInstanceRoot(visual.gameObject) != FbxAssetPath
                || MaxSoleDifference(oldSoles, newSoles) > SoleHeightTolerance)
                throw new InvalidOperationException("El visual v12 instalado no conserva estructura, transformación o suelas.");
            Animator[] activeAnimators = context.PlayerRoot.GetComponentsInChildren<Animator>(false);
            if (activeAnimators.Length != 1 || activeAnimators[0] != animator)
                throw new InvalidOperationException("El driver no resolvería exclusivamente el Animator v12.");
        }

        private static void ValidateDashExits(Animator animator, QusapGroundSensor ground)
        {
            var cases = new[]
            {
                new ExitCase("Qusap_Run", true, 1f, 0f),
                new ExitCase("Qusap_Idle", true, 0f, 0f),
                new ExitCase("Qusap_JumpRise", false, 0f, 1f),
                new ExitCase("Qusap_Fall", false, 0f, -1f)
            };
            foreach (ExitCase item in cases)
            {
                animator.Play("Qusap_Dash", 0, 0.5f);
                animator.Update(0f);
                animator.SetBool("Dashing", false);
                animator.SetBool("Grounded", item.Grounded);
                animator.SetBool("WallSliding", false);
                animator.SetBool("WallJumping", false);
                animator.SetFloat("Speed", item.Speed);
                animator.SetFloat("VerticalSpeed", item.VerticalSpeed);
                SetPrivate(ground, "<IsGrounded>k__BackingField", item.Grounded);
                animator.Update(0.01f);
                animator.Update(0.04f);
                bool reachedDestination = animator.GetCurrentAnimatorStateInfo(0).IsName(item.State)
                    || (animator.IsInTransition(0)
                        && animator.GetNextAnimatorStateInfo(0).IsName(item.State));
                Require(reachedDestination, "salida Dash -> " + item.State);
            }
        }

        private static Dictionary<Component, string> CaptureProtectedComponents(Scene scene, Transform visual)
        {
            return scene.GetRootGameObjects().SelectMany(root => root.GetComponentsInChildren<Component>(true))
                .Where(component => component != null && !component.transform.IsChildOf(visual)
                    && component != visual.parent)
                .ToDictionary(component => component, EditorJsonUtility.ToJson);
        }

        private static void ValidateProtectedComponents(Dictionary<Component, string> snapshot)
        {
            foreach (KeyValuePair<Component, string> pair in snapshot)
                if (pair.Key == null || EditorJsonUtility.ToJson(pair.Key) != pair.Value)
                    throw new InvalidOperationException("Un componente ajeno al visual cambió; la escena no se guardará.");
        }

        private static Dictionary<string, AnimationClip> LoadPublicClips()
        {
            AnimationClip[] clips = AssetDatabase.LoadAllAssetsAtPath(FbxAssetPath).OfType<AnimationClip>()
                .Where(clip => !IsPreviewName(clip.name)).ToArray();
            if (clips.Length != 8 || clips.Any(clip => HasNumberedDuplicateName(clip.name))
                || clips.GroupBy(clip => clip.name).Any(group => group.Count() != 1))
                throw new InvalidOperationException("El FBX v12 no expone ocho clips públicos únicos.");
            return clips.ToDictionary(clip => clip.name, StringComparer.Ordinal);
        }

        private static IEnumerable<AnimatorState> EnumerateStates(AnimatorStateMachine machine)
        {
            foreach (ChildAnimatorState child in machine.states) yield return child.state;
            foreach (ChildAnimatorStateMachine child in machine.stateMachines)
                foreach (AnimatorState state in EnumerateStates(child.stateMachine)) yield return state;
        }

        private static void ConfigureTransition(AnimatorStateTransition transition, float duration)
        {
            transition.hasExitTime = false;
            transition.hasFixedDuration = true;
            transition.duration = duration;
            transition.canTransitionToSelf = false;
        }

        private static AnimatorCondition Condition(string parameter, AnimatorConditionMode mode, float threshold = 0f)
        {
            return new AnimatorCondition { parameter = parameter, mode = mode, threshold = threshold };
        }

        private static bool HasConditions(AnimatorStateTransition transition, params AnimatorCondition[] expected)
        {
            return transition.conditions.Length == expected.Length && expected.All(item =>
                transition.conditions.Count(actual => actual.parameter == item.parameter
                    && actual.mode == item.mode && Mathf.Approximately(actual.threshold, item.threshold)) == 1);
        }

        private static bool HasParameter(IReadOnlyDictionary<string, AnimatorControllerParameter> parameters,
            string name, AnimatorControllerParameterType type)
        {
            return parameters.TryGetValue(name, out AnimatorControllerParameter parameter)
                && parameter.type == type;
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

        private static bool HasNumberedDuplicateName(string candidate)
        {
            return !string.IsNullOrWhiteSpace(candidate) && Regex.IsMatch(candidate, @"\(\d+\)\s*$");
        }

        private static float MaxSoleDifference(Vector2 oldSoles, Vector2 newSoles)
        {
            return Mathf.Max(Mathf.Abs(oldSoles.x - newSoles.x), Mathf.Abs(oldSoles.y - newSoles.y));
        }

        private static string FileHash(string path)
        {
            using SHA256 sha = SHA256.Create();
            using FileStream stream = File.OpenRead(path);
            return BitConverter.ToString(sha.ComputeHash(stream)).Replace("-", string.Empty);
        }

        private static string AbsoluteAssetPath(string path)
        {
            return Path.GetFullPath(Path.Combine(Directory.GetParent(Application.dataPath).FullName, path));
        }

        private static string FormatVector(Vector3 value)
        {
            return $"({value.x:0.######}, {value.y:0.######}, {value.z:0.######})";
        }

        private static void EnsureEditorIsIdle()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode || AnimationMode.InAnimationMode())
                throw new InvalidOperationException("Sal de Play Mode y Animation Mode.");
        }

        private static void ShowError(string message)
        {
            Debug.LogError(message);
            EditorUtility.DisplayDialog(DialogTitle, message, "Aceptar");
        }

        private static readonly System.Reflection.BindingFlags PrivateInstance =
            System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic;
        private static object GetPrivate(object target, string field) =>
            target.GetType().GetField(field, PrivateInstance).GetValue(target);
        private static void SetPrivate(object target, string field, object value) =>
            target.GetType().GetField(field, PrivateInstance).SetValue(target, value);
        private static void InvokePrivate(object target, string method) =>
            target.GetType().GetMethod(method, PrivateInstance).Invoke(target, null);
        private static void Require(bool condition, string message)
        {
            if (!condition) throw new InvalidOperationException("Dash v12 validation failed: " + message);
        }

        private sealed class ClipRequirement
        {
            public ClipRequirement(string name, float expectedFrames, bool loop,
                float? expectedFirstFrame = null, float? expectedLastFrame = null)
            {
                Name = name;
                ExpectedFrames = expectedFrames;
                Loop = loop;
                ExpectedFirstFrame = expectedFirstFrame;
                ExpectedLastFrame = expectedLastFrame;
            }
            public string Name { get; }
            public float ExpectedFrames { get; }
            public bool Loop { get; }
            public float? ExpectedFirstFrame { get; }
            public float? ExpectedLastFrame { get; }
        }

        private sealed class SceneContext
        {
            public SceneContext(QusapAnimationDriver driver, Transform playerRoot,
                Transform visualRoot, Animator animator)
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

        private readonly struct ExitCase
        {
            public ExitCase(string state, bool grounded, float speed, float verticalSpeed)
            {
                State = state;
                Grounded = grounded;
                Speed = speed;
                VerticalSpeed = verticalSpeed;
            }
            public string State { get; }
            public bool Grounded { get; }
            public float Speed { get; }
            public float VerticalSpeed { get; }
        }
    }
}
#endif
