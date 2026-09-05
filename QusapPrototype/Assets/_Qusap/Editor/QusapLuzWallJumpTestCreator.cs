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
    // Nothing here runs on load. PrepareAssets imports/controller only; scene creation is manual.
    public static class QusapLuzWallJumpTestCreator
    {
        private const string Folder = "Assets/Scenes/Art/Characters/Qusap/Final_Light_v3/";
        private const string FbxAssetPath = Folder + "Qusap_Luz_Locomotion_v11.fbx";
        private const string SourceFbxAssetPath = Folder + "Qusap_Luz_Locomotion_v10.fbx";
        private const string ExternalFbxPath = @"C:\Users\victo\Documents\Qusap\3D\Modelado\Qusap_Luz_Definitivo\03_Export_Unity\Qusap_Luz_Locomotion_v11.fbx";
        private const string TargetControllerPath = Folder + "Qusap_Luz_Animator_WallJumpTest_v1.controller";
        private const string TargetScenePath = "Assets/_Qusap/Scenes/Qusap_Luz_Locomotion_WallJumpTest_v1.unity";
        private const string ActiveVisualName = "PlayerVisual";
        private const string BackupVisualName = "PlayerVisual_WallSlideV10_Backup";
        private const string DialogTitle = "Qusap Luz Wall Jump Test";
        private const float FrameTolerance = 0.01f;
        private const float SoleHeightTolerance = 0.01f;
        private static readonly ClipRequirement[] ClipRequirements =
        {
            new("Qusap_Idle", 120f, true), new("Qusap_Run", 36f, true),
            new("Qusap_JumpRise", 18f, false), new("Qusap_Fall", 24f, true),
            new("Qusap_Land", 18f, false), new("Qusap_WallSlide", 48f, true),
            new("Qusap_WallJump", 18f, false)
        };

        // Explicit batch entry point. Does not create, save or open the test scene.
        public static void PrepareAssets()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode || AnimationMode.InAnimationMode())
                throw new InvalidOperationException("Sal de Play Mode y Animation Mode.");
            if (!TryFindExactFbx(out string path, out ModelImporter importer, out string error))
                throw new InvalidOperationException(error);
            var clips = ConfigureImporterAndLoadClips(path, importer);
            string sourceScene = FindLatestWallSlideScene();
            Scene preview = EditorSceneManager.OpenPreviewScene(sourceScene);
            try
            {
                SceneContext context = ResolveSceneContext(preview);
                var sourceController = context.Animator.runtimeAnimatorController as AnimatorController;
                var sourceStates = ValidateStates(sourceController, false);
                ValidateDriverParameters(sourceController);
                if (sourceStates.Values.Any(state => AssetDatabase.GetAssetPath(state.motion) != SourceFbxAssetPath))
                    throw new InvalidOperationException("Los seis Motion de origen deben usar v10.");
                string signature = BuildControllerSignature(sourceController);
                var controller = AssetDatabase.LoadAssetAtPath<AnimatorController>(TargetControllerPath);
                if (controller == null)
                {
                    if (File.Exists(AbsoluteAssetPath(TargetControllerPath))
                        || File.Exists(AbsoluteAssetPath(TargetControllerPath) + ".meta"))
                        throw new InvalidOperationException("El destino controller ya existe y no se reemplazara.");
                    if (!AssetDatabase.CopyAsset(AssetDatabase.GetAssetPath(sourceController), TargetControllerPath))
                        throw new InvalidOperationException("No se pudo copiar el controller v10.");
                    controller = AssetDatabase.LoadAssetAtPath<AnimatorController>(TargetControllerPath);
                    if (BuildControllerSignature(controller) != signature)
                        throw new InvalidOperationException("La copia no conserva la estructura original.");
                    var states = ValidateStates(controller, false);
                    foreach (var pair in states)
                    {
                        pair.Value.motion = clips[pair.Key];
                        EditorUtility.SetDirty(pair.Value);
                    }
                    AddWallJump(controller, states, clips["Qusap_WallJump"]);
                    EditorUtility.SetDirty(controller);
                    AssetDatabase.SaveAssetIfDirty(controller);
                }
                ValidateWallJumpController(controller, clips);
                if (BuildControllerSignature(sourceController) != signature)
                    throw new InvalidOperationException("Cambio el controller original.");
                Debug.Log("WallJump v11: importacion y controller verificados. La escena de prueba NO se ha creado.");
            }
            finally { EditorSceneManager.ClosePreviewScene(preview); }
        }

        // Explicit validation on disposable preview objects; never calls the scene creation menu.
        public static void ValidatePreparedAssets()
        {
            var controller = AssetDatabase.LoadAssetAtPath<AnimatorController>(TargetControllerPath);
            var clips = AssetDatabase.LoadAllAssetsAtPath(FbxAssetPath).OfType<AnimationClip>()
                .Where(c => !IsPreviewName(c.name)).ToDictionary(c => c.name);
            ValidateWallJumpController(controller, clips);
            Scene preview = EditorSceneManager.OpenPreviewScene(FindLatestWallSlideScene());
            try
            {
                SceneContext context = ResolveSceneContext(preview);
                var oldStates = ValidateStates((AnimatorController)context.Animator.runtimeAnimatorController, false);
                Vector2 oldSoles = MeasureSoleHeight(context.VisualRoot, (AnimationClip)oldStates["Qusap_Idle"].motion);
                var visual = (GameObject)PrefabUtility.InstantiatePrefab(
                    AssetDatabase.LoadAssetAtPath<GameObject>(FbxAssetPath), context.PlayerRoot);
                visual.transform.localPosition = context.VisualRoot.localPosition;
                visual.transform.localRotation = context.VisualRoot.localRotation;
                visual.transform.localScale = context.VisualRoot.localScale;
                ValidateInPlaceClips(visual.transform, clips);
                Vector2 soles = MeasureSoleHeight(visual.transform, clips["Qusap_Idle"]);
                Require(MaxSoleDifference(oldSoles, soles) <= SoleHeightTolerance, "Altura de suelas v10/v11");
                context.VisualRoot.name = BackupVisualName;
                context.VisualRoot.gameObject.SetActive(false);
                visual.name = ActiveVisualName;
                var animator = visual.GetComponent<Animator>();
                animator.runtimeAnimatorController = controller;
                animator.applyRootMotion = false;
                animator.cullingMode = AnimatorCullingMode.AlwaysAnimate;
                animator.Rebind();

                var driver = context.Driver;
                var motor = driver.GetComponent<QusapVerticalMotor>();
                var horizontal = driver.GetComponent<QusapHorizontalMotor>();
                var ground = driver.GetComponent<QusapGroundSensor>();
                var wall = driver.GetComponent<QusapWallSensor>();
                var input = driver.GetComponent<QusapInputReader>();
                var dash = driver.GetComponent<QusapDashMotor>();
                var body = driver.GetComponent<Rigidbody>();
                InvokePrivate(horizontal, "Awake");
                InvokePrivate(motor, "Awake");
                InvokePrivate(driver, "Awake");
                InvokePrivate(driver, "OnEnable");
                var contacts = (Dictionary<Collider, int>)GetPrivate(wall, "wallContacts");
                Collider wallCollider = driver.GetComponent<Collider>();

                // Exercise the actual motor branch: both wall sides, neutral/toward/away inputs.
                foreach (int side in new[] { -1, 1 })
                foreach (float intent in new[] { -1f, 0f, 1f })
                {
                    SetPrivate(ground, "<IsGrounded>k__BackingField", false);
                    SetPrivate(wall, "<WallSide>k__BackingField", side);
                    contacts.Clear();
                    contacts.Add(wallCollider, side);
                    SetPrivate(motor, "lastWallJumpSide", 0);
                    SetPrivate(motor, "isRising", false);
                    SetPrivate(motor, "coyoteTimeRemaining", 0f);
                    SetPrivate(motor, "jumpBufferRemaining", 0f);
                    SetPrivate(input, "horizontalValue", intent);
                    SetPrivate(input, "jumpPressed", true);
                    body.linearVelocity = new Vector3(0f, -1f, 0f);
                    uint before = motor.WallJumpSequence;
                    InvokePrivate(motor, "FixedUpdate");
                    Require(motor.WallJumpSequence == before + 1, "Senal de salto real");
                    float expectedSpeed = (float)GetPrivate(motor, intent == 0f
                        ? "wallJumpNeutralHorizontalSpeed" : intent * side < 0f
                            ? "wallJumpHorizontalSpeed" : "wallJumpTowardWallHorizontalSpeed");
                    Require(Mathf.Approximately(body.linearVelocity.x, -side * expectedSpeed)
                        && Mathf.Approximately(body.linearVelocity.y, (float)GetPrivate(motor, "wallJumpVerticalSpeed")),
                        "Velocidad original del salto");
                    animator.Play("Qusap_WallSlide", 0, 0f);
                    animator.Update(0f);
                    Quaternion rootRotation = driver.transform.localRotation;
                    Vector3 physicalVelocity = body.linearVelocity;
                    float yaw = visual.transform.localEulerAngles.y;
                    InvokePrivate(driver, "Update");
                    Require(animator.GetBool("WallJumping"), "Bool tras salto real");
                    Require(Mathf.Abs(Mathf.DeltaAngle(yaw, visual.transform.localEulerAngles.y)) < 0.001f,
                        "Orientacion retenida al iniciar");
                    SetPrivate(driver, "wallJumpStartedAt", Time.time - 0.11f);
                    InvokePrivate(driver, "Update");
                    Require(Mathf.Approximately((float)GetPrivate(driver, "targetFacingYaw"),
                        body.linearVelocity.x > 0f ? 150f : 210f), "Giro liberado hacia velocidad real");
                    Require(driver.transform.localRotation == rootRotation && body.linearVelocity == physicalVelocity,
                        "Driver exclusivamente visual");
                    animator.Update(0.01f);
                    animator.Update(0.04f);
                    Require(animator.GetCurrentAnimatorStateInfo(0).IsName("Qusap_WallJump"), "Entrada desde WallSlide");
                    for (int frame = 0; frame < 24; frame++)
                    {
                        InvokePrivate(driver, "Update");
                        animator.Update(1f / 60f);
                    }
                    Require(animator.GetCurrentAnimatorStateInfo(0).IsName("Qusap_JumpRise"), "Salida a JumpRise al 80%");
                    SetPrivate(driver, "enteredWallJump", true);
                    InvokePrivate(driver, "Update");
                    Require(!animator.GetBool("WallJumping"), "Bool liberado al terminar");
                }

                // No false positives: ordinary jump, leaving a wall, or dash.
                foreach (string scenario in new[] { "normal", "leave", "dash" })
                {
                    uint before = motor.WallJumpSequence;
                    SetPrivate(motor, "isRising", false);
                    SetPrivate(motor, "lastWallJumpSide", 0);
                    SetPrivate(motor, "jumpBufferRemaining", 0f);
                    SetPrivate(motor, "coyoteTimeRemaining", 0f);
                    SetPrivate(ground, "<IsGrounded>k__BackingField", scenario == "normal");
                    SetPrivate(input, "jumpPressed", scenario != "leave");
                    SetPrivate(dash, "<IsDashing>k__BackingField", scenario == "dash");
                    contacts.Clear();
                    body.linearVelocity = Vector3.zero;
                    InvokePrivate(motor, "FixedUpdate");
                    InvokePrivate(driver, "Update");
                    Require(motor.WallJumpSequence == before && !animator.GetBool("WallJumping"), scenario);
                }
                SetPrivate(dash, "<IsDashing>k__BackingField", false);
                animator.Play("Qusap_WallJump", 0, 0.2f);
                animator.Update(0f);
                SetPrivate(driver, "wallJumping", true);
                SetPrivate(ground, "<IsGrounded>k__BackingField", true);
                InvokePrivate(driver, "Update");
                Require(!animator.GetBool("WallJumping"), "Suelo cancela Bool");
                animator.Update(0.01f);
                animator.Update(0.04f);
                Require(animator.GetCurrentAnimatorStateInfo(0).IsName("Qusap_Land"), "Suelo interrumpe a Land");
                string report = $"PASS Unity {Application.unityVersion}\nSole delta: {MaxSoleDifference(oldSoles, soles):R}\n"
                    + "Seven in-place clips; real wall jumps on both sides with three inputs; unchanged jump velocities; "
                    + "visual hold/release; WallSlide -> WallJump -> JumpRise; Grounded -> Land; "
                    + "no normal-jump/leave/dash false positives. No scene created or saved.";
                File.WriteAllText(AbsoluteAssetPath("Library/WallJump-validation.txt"), report);
                Debug.Log(report);
            }
            finally { EditorSceneManager.ClosePreviewScene(preview); }
        }

        private static readonly System.Reflection.BindingFlags PrivateInstance =
            System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic;
        private static object GetPrivate(object target, string field) => target.GetType().GetField(field, PrivateInstance).GetValue(target);
        private static void SetPrivate(object target, string field, object value) => target.GetType().GetField(field, PrivateInstance).SetValue(target, value);
        private static void InvokePrivate(object target, string method) => target.GetType().GetMethod(method, PrivateInstance).Invoke(target, null);
        private static void Require(bool condition, string message)
        {
            if (!condition) throw new InvalidOperationException("WallJump validation failed: " + message);
        }

        [MenuItem("Tools/Qusap/Create Qusap Luz Wall Jump Test")]
        private static void CreateWallJumpTest()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode || EditorApplication.isCompiling
                || EditorApplication.isUpdating || AnimationMode.InAnimationMode())
            {
                ShowError("Sal de Play Mode/Animation Mode y espera que termine la compilacion.");
                return;
            }
            // Never discard unsaved work, including a previously created test.
            for (int i = 0; i < SceneManager.sceneCount; i++)
                if (SceneManager.GetSceneAt(i).isDirty)
                {
                    ShowError("Guarda o descarta manualmente los cambios de las escenas abiertas.");
                    return;
                }
            if (File.Exists(AbsoluteAssetPath(TargetScenePath))
                || File.Exists(AbsoluteAssetPath(TargetScenePath) + ".meta"))
            {
                EditorUtility.DisplayDialog(DialogTitle, "La escena de prueba ya existe; no se sobrescribe:\n" + TargetScenePath, "Aceptar");
                return;
            }
            try
            {
                PrepareAssets();
                string sourcePath = FindLatestWallSlideScene();
                if (!AssetDatabase.CopyAsset(sourcePath, TargetScenePath))
                    throw new InvalidOperationException("No se pudo copiar la escena WallSlide.");
                Scene scene = EditorSceneManager.OpenScene(TargetScenePath, OpenSceneMode.Single);
                SceneContext context = ResolveSceneContext(scene);
                var protectedComponents = CaptureProtectedComponents(scene, context.VisualRoot);
                var controller = AssetDatabase.LoadAssetAtPath<AnimatorController>(TargetControllerPath);
                var states = ValidateStates(controller, true);
                var clips = states.ToDictionary(pair => pair.Key, pair => (AnimationClip)pair.Value.motion);
                var previousController = (AnimatorController)context.Animator.runtimeAnimatorController;
                var previousStates = ValidateStates(previousController, false);
                Vector2 previousSoles = MeasureSoleHeight(context.VisualRoot, (AnimationClip)previousStates["Qusap_Idle"].motion);
                Vector3 position = context.VisualRoot.localPosition;
                Quaternion rotation = context.VisualRoot.localRotation;
                Vector3 scale = context.VisualRoot.localScale;
                if (scale.x <= 0f || scale.y <= 0f || scale.z <= 0f || context.PlayerRoot.Find(BackupVisualName) != null)
                    throw new InvalidOperationException("Escala no positiva o respaldo v10 ya existente.");
                // The scene copy already duplicates the entire original visual. Retain that exact instance.
                context.VisualRoot.name = BackupVisualName;
                context.VisualRoot.gameObject.SetActive(false);
                PrefabUtility.RecordPrefabInstancePropertyModifications(context.VisualRoot.gameObject);
                var model = AssetDatabase.LoadAssetAtPath<GameObject>(FbxAssetPath);
                var visual = (GameObject)PrefabUtility.InstantiatePrefab(model, context.PlayerRoot);
                visual.name = ActiveVisualName;
                visual.SetActive(true);
                visual.transform.localPosition = position;
                visual.transform.localRotation = rotation;
                visual.transform.localScale = scale;
                var animator = visual.GetComponent<Animator>() ?? visual.AddComponent<Animator>();
                animator.enabled = true;
                animator.runtimeAnimatorController = controller;
                animator.applyRootMotion = false;
                animator.cullingMode = AnimatorCullingMode.AlwaysAnimate;
                PrefabUtility.RecordPrefabInstancePropertyModifications(visual);
                PrefabUtility.RecordPrefabInstancePropertyModifications(visual.transform);
                PrefabUtility.RecordPrefabInstancePropertyModifications(animator);
                ValidateInPlaceClips(visual.transform, clips);
                Vector2 soles = MeasureSoleHeight(visual.transform, clips["Qusap_Idle"]);
                ValidateSceneResult(context, visual.transform, animator, controller, states,
                    position, rotation, scale, previousSoles, soles);
                ValidateProtectedComponents(protectedComponents);
                EditorSceneManager.MarkSceneDirty(scene);
                if (!EditorSceneManager.SaveScene(scene))
                    throw new InvalidOperationException("No se pudo guardar la copia de prueba.");
                Selection.activeGameObject = visual;
                string report = $"Escena creada: {TargetScenePath}\nController creado: {TargetControllerPath}\n"
                    + "Siete clips encontrados; WallJump: 18 frames, 0.30 s a 60 FPS.\n"
                    + "Senal: QusapVerticalMotor.WallJumpSequence, publicada tras ejecutar el Wall Jump real.\n"
                    + $"Visual anterior respaldado y desactivado: {BackupVisualName}\n"
                    + $"Diferencia maxima de altura de suelas: {MaxSoleDifference(previousSoles, soles):0.######}\n"
                    + "Transform local conservado. Root Motion desactivado.\n"
                    + "Jugabilidad sin modificar: componentes y valores serializados protegidos verificados.\n"
                    + "Orientacion visual retenida 0.10 s; giro 150/210 con la velocidad existente.\n"
                    + "Comprueba manualmente ambas paredes, salto normal, soltar pared, dash y aterrizaje.";
                Debug.Log(report, visual);
                EditorUtility.DisplayDialog(DialogTitle, report, "Aceptar");
            }
            catch (Exception exception)
            {
                Debug.LogException(exception);
                ShowError("No se completo la prueba. Los originales se conservan; revisa la copia parcial.\n" + exception.Message);
            }
        }

        private static string FindLatestWallSlideScene()
        {
            var paths = AssetDatabase.FindAssets("WallSlideTest t:Scene", new[] { "Assets" })
                .Select(AssetDatabase.GUIDToAssetPath)
                .Where(path => System.Text.RegularExpressions.Regex.IsMatch(Path.GetFileName(path),
                    @"^Qusap_Luz_Locomotion_WallSlideTest_v\d+\.unity$"))
                .OrderByDescending(path => File.GetLastWriteTimeUtc(AbsoluteAssetPath(path))).ToArray();
            foreach (string path in paths)
            {
                Scene preview = EditorSceneManager.OpenPreviewScene(path);
                try
                {
                    var context = ResolveSceneContext(preview);
                    var controller = context.Animator.runtimeAnimatorController as AnimatorController;
                    ValidateStates(controller, false);
                    ValidateDriverParameters(controller);
                    if (controller.parameters.Count(p => p.name == "WallSliding"
                        && p.type == AnimatorControllerParameterType.Bool) != 1) continue;
                    return path;
                }
                catch (InvalidOperationException) { /* Try the next saved functional WallSlide scene. */ }
                finally { EditorSceneManager.ClosePreviewScene(preview); }
            }
            throw new InvalidOperationException("No se encontro una escena WallSlide funcional con visual v10.");
        }

        private static void AddWallJump(AnimatorController controller,
            IReadOnlyDictionary<string, AnimatorState> states, AnimationClip clip)
        {
            if (controller.parameters.Any(p => p.name == "WallJumping"))
                throw new InvalidOperationException("WallJumping ya existe.");
            controller.AddParameter("WallJumping", AnimatorControllerParameterType.Bool);
            var machine = controller.layers[0].stateMachine;
            var jump = machine.AddState("Qusap_WallJump", new Vector3(1040f, 230f, 0f));
            jump.motion = clip;
            jump.writeDefaultValues = states["Qusap_JumpRise"].writeDefaultValues;
            foreach (var transition in machine.anyStateTransitions.Concat(states.Values.SelectMany(s => s.transitions)))
            {
                if (transition.destinationState == states["Qusap_JumpRise"]
                    || transition.destinationState == states["Qusap_Fall"]
                    || transition.destinationState == states["Qusap_WallSlide"])
                {
                    transition.AddCondition(AnimatorConditionMode.IfNot, 0f, "WallJumping");
                    EditorUtility.SetDirty(transition);
                }
            }
            // A real wall jump can occur before WallSlide has been evaluated (or with neutral input).
            // Explicit entries from existing states cover this without an Any State/self entry.
            foreach (var state in states.Values)
            {
                var enter = state.AddTransition(jump);
                ConfigureTransition(enter, 0.03f);
                enter.AddCondition(AnimatorConditionMode.If, 0f, "WallJumping");
                enter.AddCondition(AnimatorConditionMode.IfNot, 0f, "Grounded");
                enter.interruptionSource = TransitionInterruptionSource.Destination;
                enter.orderedInterruption = false;
                state.transitions = new[] { enter }.Concat(state.transitions.Where(t => t != enter)).ToArray();
                EditorUtility.SetDirty(state);
                EditorUtility.SetDirty(enter);
            }
            var land = jump.AddTransition(states["Qusap_Land"]);
            ConfigureTransition(land, 0.03f);
            land.AddCondition(AnimatorConditionMode.If, 0f, "Grounded");
            var rise = jump.AddTransition(states["Qusap_JumpRise"]);
            ConfigureTransition(rise, 0.05f);
            rise.hasExitTime = true;
            rise.exitTime = 0.80f;
            rise.AddCondition(AnimatorConditionMode.IfNot, 0f, "Grounded");
            // Only WallJump's Grounded exit may interrupt this blend. Destination entries
            // still see WallJumping=true until the driver observes the completed blend.
            rise.interruptionSource = TransitionInterruptionSource.Source;
            rise.orderedInterruption = false;
            foreach (UnityEngine.Object item in new UnityEngine.Object[] { machine, jump, land, rise })
                EditorUtility.SetDirty(item);
        }

        private static void ValidateWallJumpController(AnimatorController controller,
            IReadOnlyDictionary<string, AnimationClip> clips)
        {
            var states = ValidateStates(controller, true);
            ValidateDriverParameters(controller);
            if (controller.parameters.Length != 5 || controller.parameters.Count(p => p.name == "WallJumping"
                && p.type == AnimatorControllerParameterType.Bool && !p.defaultBool) != 1
                || states.Any(pair => pair.Value.motion != clips[pair.Key]))
                throw new InvalidOperationException("Parametros o siete Motion v11 invalidos.");
            var machine = controller.layers[0].stateMachine;
            var jump = states["Qusap_WallJump"];
            if (machine.anyStateTransitions.Any(t => t.destinationState == jump)
                || states.Values.Any(s => s.transitions.Any(t => t.destinationState == s))
                || jump.transitions.Length != 2)
                throw new InvalidOperationException("Entrada Any State, auto-transicion o salidas duplicadas.");
            foreach (var state in states.Values.Where(s => s != jump))
            {
                var entries = state.transitions.Where(t => t.destinationState == jump).ToArray();
                if (entries.Length != 1 || entries[0].hasExitTime || !entries[0].hasFixedDuration
                    || !Mathf.Approximately(entries[0].duration, 0.03f)
                    || !entries[0].conditions.Any(c => c.parameter == "WallJumping" && c.mode == AnimatorConditionMode.If))
                    throw new InvalidOperationException("Entrada WallJump invalida.");
            }
            var rise = jump.transitions.Single(t => t.destinationState == states["Qusap_JumpRise"]);
            var land = jump.transitions.Single(t => t.destinationState == states["Qusap_Land"]);
            if (!rise.hasExitTime || !Mathf.Approximately(rise.exitTime, 0.8f)
                || rise.interruptionSource != TransitionInterruptionSource.Source
                || !Mathf.Approximately(rise.duration, 0.05f) || land.hasExitTime
                || !land.conditions.Any(c => c.parameter == "Grounded" && c.mode == AnimatorConditionMode.If))
                throw new InvalidOperationException("Salida WallJump invalida.");
            foreach (var transition in machine.anyStateTransitions.Concat(states.Values.Where(s => s != jump).SelectMany(s => s.transitions)))
                if ((transition.destinationState == states["Qusap_JumpRise"]
                    || transition.destinationState == states["Qusap_Fall"]
                    || transition.destinationState == states["Qusap_WallSlide"])
                    && !transition.conditions.Any(c => c.parameter == "WallJumping" && c.mode == AnimatorConditionMode.IfNot))
                    throw new InvalidOperationException("Una transicion general puede interrumpir WallJump.");
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
                        throw new InvalidOperationException("Existe un .meta huerfano para v11. No se reemplazara.");
                    // Stage outside Assets: never leave a truncated FBX after an interruption.
                    string staging = AbsoluteAssetPath("Library/QusapWallJumpFbx-" + Guid.NewGuid() + ".tmp");
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

        private static Dictionary<string, AnimationClip> ConfigureImporterAndLoadClips(
            string fbxPath,
            ModelImporter importer)
        {
            var reference = AssetImporter.GetAtPath(SourceFbxAssetPath) as ModelImporter;
            if (reference == null) throw new InvalidOperationException("Falta el ModelImporter v10.");
            importer.globalScale = reference.globalScale;
            importer.useFileScale = reference.useFileScale;
            importer.bakeAxisConversion = reference.bakeAxisConversion;
            importer.preserveHierarchy = reference.preserveHierarchy;
            importer.sortHierarchyByName = reference.sortHierarchyByName;
            importer.importBlendShapes = reference.importBlendShapes;
            importer.importVisibility = reference.importVisibility;
            importer.importNormals = reference.importNormals;
            importer.importTangents = reference.importTangents;
            importer.meshCompression = reference.meshCompression;
            importer.isReadable = reference.isReadable;
            importer.optimizeGameObjects = reference.optimizeGameObjects;
            importer.extraExposedTransformPaths = reference.extraExposedTransformPaths;
            importer.humanDescription = reference.humanDescription;
            importer.materialImportMode = reference.materialImportMode;
            importer.materialLocation = reference.materialLocation;
            importer.materialName = reference.materialName;
            importer.materialSearch = reference.materialSearch;
            foreach (var mapping in reference.GetExternalObjectMap())
                importer.AddRemap(mapping.Key, mapping.Value);
            ModelImporterClipAnimation[] sourceTakes = importer.defaultClipAnimations;
            if (sourceTakes == null || sourceTakes.Length == 0)
            {
                throw new InvalidOperationException($"'{fbxPath}' no contiene Source Takes.");
            }
            foreach (var take in sourceTakes.Where(take => take != null))
                Debug.Log($"v11 Source Take: {take.name} | {take.takeName} | {take.firstFrame:R}..{take.lastFrame:R}");

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
                        $"Se esperaba un Source Take Ãºnico '{requirement.Name}', pero se encontraron {matches.Length}.");
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
                throw new InvalidOperationException("El ModelImporter v11 no conservÃ³ la configuraciÃ³n requerida.");
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
                    "La reimportaciÃ³n no produjo exactamente los siete clips pÃºblicos requeridos sin duplicados.");
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

            AnimationClip wallJump = result["Qusap_WallJump"];
            if (Mathf.Abs(wallJump.frameRate - 60f) > FrameTolerance
                || Mathf.Abs(wallJump.length - 0.30f) > 0.0001f)
                throw new InvalidOperationException("WallJump debe durar 0.30 segundos a 60 FPS (18 frames efectivos).");
            foreach (var clip in result.Values)
                Debug.Log($"v11 clip validado: {clip.name}: {clip.length:R}s, {clip.frameRate:R} FPS, {clip.length * clip.frameRate:R} frames");
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
                && System.Text.RegularExpressions.Regex.IsMatch(candidate, @"\(\d+\)\s*$");
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
                    $"La escena WallSlideTest debe contener un Ãºnico QusapAnimationDriver activo; se encontraron {drivers.Length}.");
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
                    $"Se esperaba un Ãºnico Animator activo debajo del jugador; se encontraron {animators.Length}.");
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
                throw new InvalidOperationException("No se pudo resolver la raÃ­z visual activa del jugador.");
            }

            if (visualRoot.GetComponent<Animator>() != animator)
            {
                throw new InvalidOperationException(
                    "QusapAnimationDriver requiere que el Animator estÃ© en la raÃ­z visual directa.");
            }

            if (visualRoot.name != ActiveVisualName
                || playerRoot.Cast<Transform>().Count(child => child.name == ActiveVisualName) != 1
                || PrefabUtility.GetPrefabAssetPathOfNearestInstanceRoot(visualRoot.gameObject) != SourceFbxAssetPath)
                throw new InvalidOperationException("PlayerVisual debe ser la instancia activa unica del FBX v10.");
            var verticalMotor = playerRoot.GetComponent<QusapVerticalMotor>();
            var horizontalMotor = playerRoot.GetComponent<QusapHorizontalMotor>();
            var wallSensor = playerRoot.GetComponent<QusapWallSensor>();
            if (verticalMotor == null || !verticalMotor.isActiveAndEnabled
                || horizontalMotor == null || !horizontalMotor.isActiveAndEnabled
                || wallSensor == null || !wallSensor.isActiveAndEnabled)
                throw new InvalidOperationException(
                    "Falta el proveedor real de Wall Slide: se requieren QusapVerticalMotor, " +
                    "QusapHorizontalMotor y QusapWallSensor activos en el jugador. No se aÃ±adirÃ¡n sensores.");
            return new SceneContext(driver, playerRoot, visualRoot, animator);
        }

        private static Dictionary<string, AnimatorState> ValidateStates(
            AnimatorController controller, bool includeWallJump)
        {
            if (controller == null)
            {
                throw new InvalidOperationException("El Animator Controller es nulo.");
            }

            AnimatorState[] states = controller.layers
                .SelectMany(layer => EnumerateStates(layer.stateMachine))
                .ToArray();
            int expectedCount = includeWallJump ? 7 : 6;
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

        private static void ConfigureTransition(AnimatorStateTransition transition, float duration)
        {
            transition.hasExitTime = false;
            transition.hasFixedDuration = true;
            transition.duration = duration;
            transition.canTransitionToSelf = false;
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
                    "El controller no conserva Speed, VerticalSpeed y Grounded con la configuraciÃ³n requerida.");
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
                        "Qusap_Idle intentÃ³ alterar la transformaciÃ³n raÃ­z GroundAligned durante el muestreo.");
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
                    $"Se esperaba un renderer Ãºnico para {footName}; se encontraron {matches.Length}.");
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
                        $"El renderer '{renderer.name}' no produjo vÃ©rtices al hornear su pose.");
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
            Vector2 v11SoleHeight)
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
                    "La sustituciÃ³n visual v11 no superÃ³ la validaciÃ³n estructural o de transformaciÃ³n.");
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
                throw new InvalidOperationException("El driver debe encontrar exclusivamente el Animator v11; todas las mallas y Motion deben provenir de v11.");

            if (MaxSoleDifference(previousSoleHeight, v11SoleHeight) > SoleHeightTolerance)
            {
                throw new InvalidOperationException(
                    $"Las suelas v11 cambiaron {MaxSoleDifference(previousSoleHeight, v11SoleHeight):0.######} unidades respecto a GroundAligned. " +
                    "No se recalculÃ³ ni alterÃ³ localPosition.y.");
            }

            if (targetStates.Count != ClipRequirements.Length
                || targetStates.Values.Any(state => state.motion == null))
            {
                throw new InvalidOperationException(
                    "El controller nuevo no conserva exactamente siete estados con Motion.");
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
                    "No se puede verificar el FBX v11 en su ruta exacta contra el archivo externo.");
            }

            using (SHA256 sha = SHA256.Create())
            using (FileStream external = File.OpenRead(ExternalFbxPath))
            using (FileStream existing = File.OpenRead(AbsoluteAssetPath(assetPath)))
            {
                if (!sha.ComputeHash(external).SequenceEqual(sha.ComputeHash(existing)))
                {
                    throw new InvalidOperationException(
                        "El FBX v11 existente no coincide con el externo. No se reemplazarÃ¡ ni se crearÃ¡ un duplicado.");
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
