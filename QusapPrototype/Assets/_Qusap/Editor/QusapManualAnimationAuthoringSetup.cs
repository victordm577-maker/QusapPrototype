using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEditor.Animations;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

namespace Qusap.Editor
{
    public static class QusapManualAnimationAuthoringSetup
    {
        public const string CharacterModelPath =
            "Assets/_Qusap/Art/Characters/Models/Qusap_Luz_Modular_v1.fbx";
        public const string ModularVisualPrefabPath =
            "Assets/_Qusap/Prefabs/Characters/QusapLuzModularVisual.prefab";
        public const string BlueSwordPrefabPath =
            "Assets/_Qusap/Prefabs/Weapons/QusapSwordBlueVisual.prefab";
        public const string PurpleSwordPrefabPath =
            "Assets/_Qusap/Prefabs/Weapons/QusapSwordPurpleVisual.prefab";
        public const string WhiteSwordPrefabPath =
            "Assets/_Qusap/Prefabs/Weapons/QusapSwordWhiteVisual.prefab";
        public const string ClipPath =
            "Assets/_Qusap/Animation/Manual/Clips/Locomotion/Qusap_Idle_Manual_v1.anim";
        public const string ControllerPath =
            "Assets/_Qusap/Animation/Manual/Controllers/Qusap_ManualAuthoring.controller";
        public const string RigPrefabPath =
            "Assets/_Qusap/Prefabs/Characters/Animation/QusapManualAnimationRig.prefab";
        public const string ScenePath =
            "Assets/_Qusap/Scenes/Animation/Qusap_AnimationAuthoring.unity";

        private const string RootName = "QusapManualAnimationRig";
        private const string AnimationRootName = "AnimationRoot";
        private const string BodyPivotName = "BodyPivot";
        private const string BodyModelSpaceName = "BodyModelSpace";
        private const string LeftFootPivotName = "FootPivot_L";
        private const string LeftFootModelSpaceName = "FootLModelSpace";
        private const string RightFootPivotName = "FootPivot_R";
        private const string RightFootModelSpaceName = "FootRModelSpace";
        private const string WeaponSocketName = "WeaponSocket";
        private const float MatrixTolerance = 0.0001f;
        private static readonly Quaternion AuthoringFacingRotation = Quaternion.Euler(0f, 60f, 0f);

        [MenuItem("Qusap/Manual Animation/Rebuild Authoring Workspace")]
        public static void BuildAll()
        {
            EnsureFolders();

            GameObject existingRig = AssetDatabase.LoadAssetAtPath<GameObject>(RigPrefabPath);
            bool hasLegacyHierarchy = existingRig != null
                && existingRig.transform.Find(AnimationRootName + "/ModelSpace") != null;
            AppearanceSnapshot before = hasLegacyHierarchy
                && AssetDatabase.LoadAssetAtPath<SceneAsset>(ScenePath) != null
                ? CaptureSceneRigAppearance()
                : null;

            AnimationClip clip = BuildNeutralClip();
            AnimatorController controller = BuildController(clip);
            GameObject rigPrefab = BuildRigPrefab(controller, clip);
            BuildAuthoringScene(rigPrefab);

            if (before != null)
                CompareAppearance(before, CaptureSceneRigAppearance(), "before/after refactor");

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
            ValidateAll();
            Debug.Log("QUSAP_MANUAL_AUTHORING_BUILD_OK");
        }

        [MenuItem("Qusap/Manual Animation/Validate Authoring Workspace")]
        public static void ValidateAll()
        {
            GameObject rig = AssetDatabase.LoadAssetAtPath<GameObject>(RigPrefabPath);
            AnimationClip clip = AssetDatabase.LoadAssetAtPath<AnimationClip>(ClipPath);
            AnimatorController controller = AssetDatabase.LoadAssetAtPath<AnimatorController>(ControllerPath);
            SceneAsset scene = AssetDatabase.LoadAssetAtPath<SceneAsset>(ScenePath);
            if (rig == null || clip == null || controller == null || scene == null)
                throw new InvalidOperationException("One or more manual-authoring assets are missing.");

            ValidateRig(rig, controller);
            ValidateClip(rig.transform, clip);
            ValidateController(controller, clip);
            ValidateScene();
            ValidateBuildSettings();
            ValidateWeaponPrefabs();
            ValidateAppearanceAgainstSources(rig);
            Debug.Log("QUSAP_MANUAL_AUTHORING_VALIDATION_OK");
        }

        private static void EnsureFolders()
        {
            string[] folders =
            {
                "Assets/_Qusap/Animation",
                "Assets/_Qusap/Animation/Manual",
                "Assets/_Qusap/Animation/Manual/Clips",
                "Assets/_Qusap/Animation/Manual/Clips/Locomotion",
                "Assets/_Qusap/Animation/Manual/Clips/Combat",
                "Assets/_Qusap/Animation/Manual/Controllers",
                "Assets/_Qusap/Animation/Manual/Documentation",
                "Assets/_Qusap/Prefabs/Characters/Animation",
                "Assets/_Qusap/Scenes/Animation"
            };

            foreach (string folder in folders)
                EnsureFolder(folder);
        }

        private static void EnsureFolder(string path)
        {
            string[] parts = path.Split('/');
            string current = parts[0];
            for (int index = 1; index < parts.Length; index++)
            {
                string next = current + "/" + parts[index];
                if (!AssetDatabase.IsValidFolder(next))
                    AssetDatabase.CreateFolder(current, parts[index]);
                current = next;
            }
        }

        private static AnimationClip BuildNeutralClip()
        {
            AnimationClip clip = AssetDatabase.LoadAssetAtPath<AnimationClip>(ClipPath);
            if (clip == null)
            {
                clip = new AnimationClip();
                AssetDatabase.CreateAsset(clip, ClipPath);
            }

            foreach (EditorCurveBinding binding in AnimationUtility.GetCurveBindings(clip))
                AnimationUtility.SetEditorCurve(clip, binding, null);
            foreach (EditorCurveBinding binding in AnimationUtility.GetObjectReferenceCurveBindings(clip))
                AnimationUtility.SetObjectReferenceCurve(clip, binding, null);

            clip.name = "Qusap_Idle_Manual_v1";
            clip.frameRate = 60f;
            clip.wrapMode = WrapMode.Loop;
            EditorUtility.SetDirty(clip);
            return clip;
        }

        private static AnimatorController BuildController(AnimationClip clip)
        {
            AnimatorController controller = AssetDatabase.LoadAssetAtPath<AnimatorController>(ControllerPath);
            if (controller == null)
                controller = AnimatorController.CreateAnimatorControllerAtPath(ControllerPath);

            AnimatorControllerLayer[] layers = controller.layers;
            layers[0].name = "Manual Authoring";
            controller.layers = layers;
            AnimatorStateMachine stateMachine = controller.layers[0].stateMachine;
            stateMachine.name = "Manual Authoring";
            AnimatorState state = stateMachine.states
                .Select(child => child.state)
                .FirstOrDefault(candidate => candidate.name == "Idle_Manual_v1");
            state ??= stateMachine.AddState("Idle_Manual_v1");
            state.motion = clip;
            state.writeDefaultValues = true;
            stateMachine.defaultState = state;
            EditorUtility.SetDirty(controller);
            return controller;
        }

        private static GameObject BuildRigPrefab(AnimatorController controller, AnimationClip clip)
        {
            GameObject sourceVisual = LoadRequired<GameObject>(ModularVisualPrefabPath);
            GameObject blueSword = LoadRequired<GameObject>(BlueSwordPrefabPath);

            var root = new GameObject(RootName);
            try
            {
                Animator animator = root.AddComponent<Animator>();
                animator.runtimeAnimatorController = controller;
                animator.applyRootMotion = false;
                animator.updateMode = AnimatorUpdateMode.Normal;
                animator.cullingMode = AnimatorCullingMode.AlwaysAnimate;

                Transform animationRoot = CreateTransform(AnimationRootName, root.transform);
                GameObject visualInstance = (GameObject)PrefabUtility.InstantiatePrefab(sourceVisual);
                visualInstance.transform.SetPositionAndRotation(Vector3.zero, AuthoringFacingRotation);
                visualInstance.transform.localScale = Vector3.one;
                PrefabUtility.UnpackPrefabInstance(
                    visualInstance,
                    PrefabUnpackMode.Completely,
                    InteractionMode.AutomatedAction);

                Transform importedModelSpace = FindUnique(visualInstance.transform, "QusapVisualRoot");
                Transform importedBodyPivot = FindUnique(importedModelSpace, BodyPivotName);
                Transform importedLeftFootPivot = FindUnique(importedModelSpace, LeftFootPivotName);
                Transform importedRightFootPivot = FindUnique(importedModelSpace, RightFootPivotName);
                Transform bodyModel = FindUnique(importedBodyPivot, "Body");
                Transform leftFootModel = FindUnique(importedLeftFootPivot, "FloatingFoot_L");
                Transform rightFootModel = FindUnique(importedRightFootPivot, "FloatingFoot_R");

                Transform bodyPivot = BuildUnityAxisControl(
                    animationRoot,
                    BodyPivotName,
                    BodyModelSpaceName,
                    importedBodyPivot,
                    bodyModel);
                Transform leftFootPivot = BuildUnityAxisControl(
                    animationRoot,
                    LeftFootPivotName,
                    LeftFootModelSpaceName,
                    importedLeftFootPivot,
                    leftFootModel);
                Transform rightFootPivot = BuildUnityAxisControl(
                    animationRoot,
                    RightFootPivotName,
                    RightFootModelSpaceName,
                    importedRightFootPivot,
                    rightFootModel);
                Object.DestroyImmediate(visualInstance);

                Transform weaponSocket = CreateTransform(WeaponSocketName, animationRoot);
                weaponSocket.localPosition = AuthoringFacingRotation
                    * new Vector3(1.15f, 1.25f, -0.35f);

                GameObject swordInstance = (GameObject)PrefabUtility.InstantiatePrefab(
                    blueSword, weaponSocket);
                swordInstance.name = blueSword.name;
                swordInstance.transform.SetLocalPositionAndRotation(
                    Vector3.zero,
                    AuthoringFacingRotation * Quaternion.Euler(0f, 0f, -12f));
                swordInstance.transform.localScale = Vector3.one * 0.70f;

                SetNeutralCurves(root.transform, clip, bodyPivot);
                SetNeutralCurves(root.transform, clip, leftFootPivot);
                SetNeutralCurves(root.transform, clip, rightFootPivot);
                SetNeutralCurves(root.transform, clip, weaponSocket);

                GameObject saved = PrefabUtility.SaveAsPrefabAsset(root, RigPrefabPath);
                if (saved == null)
                    throw new InvalidOperationException("Could not save the manual animation rig prefab.");
                return saved;
            }
            finally
            {
                Object.DestroyImmediate(root);
            }
        }

        private static Transform BuildUnityAxisControl(
            Transform animationRoot,
            string controlName,
            string correctionName,
            Transform importedPivot,
            Transform rigidModel)
        {
            Vector3 pivotPosition = animationRoot.InverseTransformPoint(importedPivot.position);
            Quaternion pivotWorldRotation = importedPivot.rotation;
            Vector3 pivotWorldScale = importedPivot.lossyScale;

            Transform control = CreateTransform(controlName, animationRoot);
            control.localPosition = pivotPosition;

            Transform correction = CreateTransform(correctionName, control);
            correction.localRotation = Quaternion.Inverse(control.rotation) * pivotWorldRotation;
            correction.localScale = pivotWorldScale;

            rigidModel.SetParent(correction, false);
            return control;
        }

        private static void SetNeutralCurves(Transform animatorRoot, AnimationClip clip, Transform control)
        {
            string path = AnimationUtility.CalculateTransformPath(control, animatorRoot);
            Vector3 position = control.localPosition;
            Quaternion rotation = control.localRotation;
            SetCurve(clip, path, "m_LocalPosition.x", position.x);
            SetCurve(clip, path, "m_LocalPosition.y", position.y);
            SetCurve(clip, path, "m_LocalPosition.z", position.z);
            SetCurve(clip, path, "m_LocalRotation.x", rotation.x);
            SetCurve(clip, path, "m_LocalRotation.y", rotation.y);
            SetCurve(clip, path, "m_LocalRotation.z", rotation.z);
            SetCurve(clip, path, "m_LocalRotation.w", rotation.w);
            clip.EnsureQuaternionContinuity();
            EditorUtility.SetDirty(clip);
        }

        private static void SetCurve(AnimationClip clip, string path, string propertyName, float value)
        {
            var curve = new AnimationCurve(
                new Keyframe(0f, value),
                new Keyframe(1f, value));
            AnimationUtility.SetEditorCurve(
                clip,
                EditorCurveBinding.FloatCurve(path, typeof(Transform), propertyName),
                curve);
        }

        private static void BuildAuthoringScene(GameObject rigPrefab)
        {
            Scene previousActive = SceneManager.GetActiveScene();
            Scene scene = GetLoadedScene(ScenePath);
            bool closeWhenFinished = !scene.IsValid();
            if (closeWhenFinished)
            {
                scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Additive);
                scene.name = "Qusap_AnimationAuthoring";
            }
            else
            {
                foreach (GameObject sceneRoot in scene.GetRootGameObjects())
                    Object.DestroyImmediate(sceneRoot);
            }

            SceneManager.SetActiveScene(scene);

            try
            {
                GameObject rig = (GameObject)PrefabUtility.InstantiatePrefab(rigPrefab, scene);
                rig.name = RootName;
                rig.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
                rig.transform.localScale = Vector3.one;

                GameObject ground = GameObject.CreatePrimitive(PrimitiveType.Cube);
                ground.name = "GroundReference";
                SceneManager.MoveGameObjectToScene(ground, scene);
                ground.transform.SetPositionAndRotation(new Vector3(0f, -0.05f, 0f), Quaternion.identity);
                ground.transform.localScale = new Vector3(6f, 0.1f, 2f);
                Collider groundCollider = ground.GetComponent<Collider>();
                if (groundCollider != null)
                    Object.DestroyImmediate(groundCollider);

                var lightObject = new GameObject("Directional Light");
                SceneManager.MoveGameObjectToScene(lightObject, scene);
                lightObject.transform.rotation = Quaternion.Euler(38f, -32f, 0f);
                Light light = lightObject.AddComponent<Light>();
                light.type = LightType.Directional;
                light.intensity = 1.25f;

                var cameraObject = new GameObject("Main Camera");
                SceneManager.MoveGameObjectToScene(cameraObject, scene);
                cameraObject.tag = "MainCamera";
                cameraObject.transform.SetPositionAndRotation(
                    new Vector3(0f, 1.15f, -8f), Quaternion.identity);
                Camera camera = cameraObject.AddComponent<Camera>();
                camera.orthographic = true;
                camera.orthographicSize = 2.2f;
                camera.clearFlags = CameraClearFlags.SolidColor;
                camera.backgroundColor = new Color(0.055f, 0.07f, 0.095f, 1f);
                camera.nearClipPlane = 0.1f;
                camera.farClipPlane = 50f;

                bool saved = closeWhenFinished
                    ? EditorSceneManager.SaveScene(scene, ScenePath)
                    : EditorSceneManager.SaveScene(scene);
                if (!saved)
                    throw new InvalidOperationException("Could not save the animation authoring scene.");
            }
            finally
            {
                if (closeWhenFinished)
                    EditorSceneManager.CloseScene(scene, true);
                if (previousActive.IsValid() && previousActive.isLoaded && previousActive != scene)
                    SceneManager.SetActiveScene(previousActive);
            }
        }

        private static void ValidateRig(GameObject rig, RuntimeAnimatorController controller)
        {
            if (rig.name != RootName)
                throw new InvalidOperationException("The authoring rig root name is not stable.");
            Animator animator = rig.GetComponent<Animator>();
            if (animator == null || animator.applyRootMotion || animator.runtimeAnimatorController != controller)
                throw new InvalidOperationException("The authoring Animator configuration is invalid.");
            if (rig.GetComponentsInChildren<MonoBehaviour>(true).Length != 0
                || rig.GetComponentsInChildren<Rigidbody>(true).Length != 0
                || rig.GetComponentsInChildren<Collider>(true).Length != 0
                || rig.GetComponentsInChildren<SkinnedMeshRenderer>(true).Length != 0)
            {
                throw new InvalidOperationException("The authoring rig contains a script, physics component, or skinned mesh.");
            }

            Transform animationRoot = RequirePath(rig.transform, AnimationRootName);
            Transform body = RequirePath(rig.transform, AnimationRootName + "/" + BodyPivotName);
            Transform bodyModelSpace = RequirePath(
                rig.transform,
                AnimationRootName + "/" + BodyPivotName + "/" + BodyModelSpaceName);
            Transform left = RequirePath(rig.transform, AnimationRootName + "/" + LeftFootPivotName);
            Transform leftModelSpace = RequirePath(
                rig.transform,
                AnimationRootName + "/" + LeftFootPivotName + "/" + LeftFootModelSpaceName);
            Transform right = RequirePath(rig.transform, AnimationRootName + "/" + RightFootPivotName);
            Transform rightModelSpace = RequirePath(
                rig.transform,
                AnimationRootName + "/" + RightFootPivotName + "/" + RightFootModelSpaceName);
            Transform socket = RequirePath(rig.transform, AnimationRootName + "/" + WeaponSocketName);

            foreach (Transform control in new[] { animationRoot, body, left, right, socket })
            {
                if (!Approximately(control.localScale, Vector3.one))
                    throw new InvalidOperationException($"Animated control '{control.name}' must have local scale 1.");
                if (!Approximately(control.localRotation, Quaternion.identity))
                    throw new InvalidOperationException($"Animated control '{control.name}' must use Unity identity axes.");
                ValidateAncestors(control, rig.transform);
            }

            if (animationRoot.parent != rig.transform
                || body.parent != animationRoot
                || left.parent != animationRoot
                || right.parent != animationRoot
                || socket.parent != animationRoot
                || bodyModelSpace.parent != body
                || leftModelSpace.parent != left
                || rightModelSpace.parent != right)
            {
                throw new InvalidOperationException("The authoring controls do not use the required flat Unity-axis hierarchy.");
            }

            MeshFilter[] characterMeshes = new[] { bodyModelSpace, leftModelSpace, rightModelSpace }
                .SelectMany(node => node.GetComponentsInChildren<MeshFilter>(true))
                .ToArray();
            if (characterMeshes.Length != 3
                || characterMeshes.Any(mesh => AssetDatabase.GetAssetPath(mesh.sharedMesh) != CharacterModelPath))
            {
                throw new InvalidOperationException("The rig must reference the three rigid meshes from the current modular FBX.");
            }

            if (socket.childCount != 1
                || PrefabUtility.GetPrefabAssetPathOfNearestInstanceRoot(socket.GetChild(0).gameObject) != BlueSwordPrefabPath)
            {
                throw new InvalidOperationException("WeaponSocket must contain exactly the blue sword prefab.");
            }

            ValidateUnityAxisTranslation(rig, body, Vector3.up, "BodyPivot Y");
            ValidateUnityAxisTranslation(rig, left, Vector3.right, "FootPivot_L X");
        }

        private static void ValidateClip(Transform rigRoot, AnimationClip clip)
        {
            if (!Mathf.Approximately(clip.frameRate, 60f))
                throw new InvalidOperationException("The neutral clip must use 60 FPS.");

            var expectedPaths = new HashSet<string>
            {
                AnimationRootName + "/" + BodyPivotName,
                AnimationRootName + "/" + LeftFootPivotName,
                AnimationRootName + "/" + RightFootPivotName,
                AnimationRootName + "/" + WeaponSocketName
            };
            EditorCurveBinding[] bindings = AnimationUtility.GetCurveBindings(clip);
            if (bindings.Length != 28)
                throw new InvalidOperationException($"Expected 28 neutral transform curves; found {bindings.Length}.");
            if (bindings.Any(binding => !expectedPaths.Contains(binding.path)
                    || binding.path == AnimationRootName
                    || binding.path.IndexOf("ModelSpace", StringComparison.Ordinal) >= 0
                    || binding.propertyName.IndexOf("Scale", StringComparison.OrdinalIgnoreCase) >= 0
                    || (!binding.propertyName.StartsWith("m_LocalPosition", StringComparison.Ordinal)
                        && !binding.propertyName.StartsWith("m_LocalRotation", StringComparison.Ordinal))))
            {
                throw new InvalidOperationException("The clip contains a forbidden or unstable binding.");
            }

            foreach (EditorCurveBinding binding in bindings)
            {
                if (rigRoot.Find(binding.path) == null)
                    throw new InvalidOperationException($"Clip path '{binding.path}' does not resolve in the rig.");
                AnimationCurve curve = AnimationUtility.GetEditorCurve(clip, binding);
                if (curve == null || curve.length != 2
                    || !Mathf.Approximately(curve.keys[0].time, 0f)
                    || !Mathf.Approximately(curve.keys[1].time, 1f)
                    || !Mathf.Approximately(curve.keys[0].value, curve.keys[1].value))
                {
                    throw new InvalidOperationException($"Binding '{binding.path}/{binding.propertyName}' is not a neutral endpoint pair.");
                }
            }
        }

        private static void ValidateController(AnimatorController controller, AnimationClip clip)
        {
            if (controller.layers.Length != 1
                || controller.layers[0].stateMachine.defaultState == null
                || controller.layers[0].stateMachine.defaultState.motion != clip)
            {
                throw new InvalidOperationException("The manual authoring controller does not default to the neutral clip.");
            }
        }

        private static void ValidateScene()
        {
            Scene previousActive = SceneManager.GetActiveScene();
            Scene scene = GetLoadedScene(ScenePath);
            bool closeWhenFinished = !scene.IsValid();
            if (closeWhenFinished)
                scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Additive);
            try
            {
                string[] expectedRoots =
                {
                    RootName,
                    "GroundReference",
                    "Directional Light",
                    "Main Camera"
                };
                string[] actualRoots = scene.GetRootGameObjects().Select(root => root.name).OrderBy(name => name).ToArray();
                if (!actualRoots.SequenceEqual(expectedRoots.OrderBy(name => name)))
                    throw new InvalidOperationException("The authoring scene contains unexpected root objects.");
                GameObject sceneRig = scene.GetRootGameObjects().Single(root => root.name == RootName);
                if (!Approximately(sceneRig.transform.localScale, Vector3.one)
                    || !Approximately(sceneRig.transform.localRotation, Quaternion.identity))
                {
                    throw new InvalidOperationException(
                        "The authoring scene rig root must preserve standard Unity axes and scale 1.");
                }
                if (scene.GetRootGameObjects().SelectMany(root => root.GetComponentsInChildren<MonoBehaviour>(true)).Any())
                    throw new InvalidOperationException("The authoring scene contains runtime scripts.");
                if (scene.GetRootGameObjects().SelectMany(root => root.GetComponentsInChildren<Rigidbody>(true)).Any()
                    || scene.GetRootGameObjects().SelectMany(root => root.GetComponentsInChildren<Collider>(true)).Any())
                {
                    throw new InvalidOperationException("The authoring scene contains physics components.");
                }
                if (scene.GetRootGameObjects().SelectMany(root => root.GetComponentsInChildren<Camera>(true)).Count() != 1
                    || scene.GetRootGameObjects().SelectMany(root => root.GetComponentsInChildren<Light>(true)).Count() != 1)
                {
                    throw new InvalidOperationException("The authoring scene needs exactly one camera and one light.");
                }
            }
            finally
            {
                if (closeWhenFinished)
                    EditorSceneManager.CloseScene(scene, true);
                if (previousActive.IsValid() && previousActive.isLoaded && previousActive != scene)
                    SceneManager.SetActiveScene(previousActive);
            }
        }

        private static void ValidateBuildSettings()
        {
            if (EditorBuildSettings.scenes.Any(scene => scene.path == ScenePath))
                throw new InvalidOperationException("The authoring scene must remain outside Build Settings.");
        }

        private static void ValidateWeaponPrefabs()
        {
            string[] paths = { BlueSwordPrefabPath, PurpleSwordPrefabPath, WhiteSwordPrefabPath };
            foreach (string path in paths)
            {
                GameObject weapon = LoadRequired<GameObject>(path);
                if (weapon.GetComponentsInChildren<MonoBehaviour>(true).Length != 0
                    || weapon.GetComponentsInChildren<Rigidbody>(true).Length != 0
                    || weapon.GetComponentsInChildren<Collider>(true).Length != 0
                    || weapon.GetComponentsInChildren<SkinnedMeshRenderer>(true).Length != 0)
                {
                    throw new InvalidOperationException($"Weapon visual '{path}' is not a rigid visual-only prefab.");
                }
            }
        }

        private static void ValidateAncestors(Transform control, Transform rigRoot)
        {
            for (Transform current = control.parent; current != null; current = current.parent)
            {
                if (!Approximately(current.localScale, Vector3.one)
                    || !Approximately(current.localRotation, Quaternion.identity))
                {
                    throw new InvalidOperationException(
                        $"Ancestor '{current.name}' of control '{control.name}' must have identity rotation and scale 1.");
                }

                if (current == rigRoot)
                    return;
            }

            throw new InvalidOperationException($"Control '{control.name}' is not under the rig root.");
        }

        private static void ValidateUnityAxisTranslation(
            GameObject rigPrefab,
            Transform prefabControl,
            Vector3 localAxis,
            string label)
        {
            GameObject instance = (GameObject)PrefabUtility.InstantiatePrefab(rigPrefab);
            try
            {
                string path = AnimationUtility.CalculateTransformPath(prefabControl, rigPrefab.transform);
                Transform control = RequirePath(instance.transform, path);
                Vector3 before = control.position;
                const float distance = 0.25f;
                control.localPosition += localAxis * distance;
                Vector3 actualDelta = control.position - before;
                Vector3 expectedDelta = localAxis * distance;
                if (!Approximately(actualDelta, expectedDelta, MatrixTolerance))
                {
                    throw new InvalidOperationException(
                        $"{label} does not map exactly to the corresponding Unity world axis. "
                        + $"Expected {expectedDelta}; got {actualDelta}.");
                }
            }
            finally
            {
                Object.DestroyImmediate(instance);
            }
        }

        private static void ValidateAppearanceAgainstSources(GameObject rig)
        {
            var referenceRoot = new GameObject("LegacyAuthoringPoseReference");
            try
            {
                referenceRoot.transform.SetPositionAndRotation(Vector3.zero, AuthoringFacingRotation);
                referenceRoot.transform.localScale = Vector3.one;

                GameObject sourceVisual = (GameObject)PrefabUtility.InstantiatePrefab(
                    LoadRequired<GameObject>(ModularVisualPrefabPath),
                    referenceRoot.transform);
                sourceVisual.transform.SetLocalPositionAndRotation(Vector3.zero, Quaternion.identity);
                sourceVisual.transform.localScale = Vector3.one;

                Transform socket = CreateTransform(WeaponSocketName, referenceRoot.transform);
                socket.localPosition = new Vector3(1.15f, 1.25f, -0.35f);
                socket.localRotation = Quaternion.Euler(0f, 0f, -12f);
                GameObject sword = (GameObject)PrefabUtility.InstantiatePrefab(
                    LoadRequired<GameObject>(BlueSwordPrefabPath),
                    socket);
                sword.transform.SetLocalPositionAndRotation(Vector3.zero, Quaternion.identity);
                sword.transform.localScale = Vector3.one * 0.70f;

                AppearanceSnapshot expected = CaptureAppearance(referenceRoot);
                AppearanceSnapshot actual = CapturePrefabAppearance(rig);
                CompareAppearance(expected, actual, "legacy source/current authoring pose");
            }
            finally
            {
                Object.DestroyImmediate(referenceRoot);
            }
        }

        private static AppearanceSnapshot CaptureSceneRigAppearance()
        {
            Scene previousActive = SceneManager.GetActiveScene();
            Scene scene = GetLoadedScene(ScenePath);
            bool closeWhenFinished = !scene.IsValid();
            if (closeWhenFinished)
                scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Additive);
            try
            {
                GameObject rig = scene.GetRootGameObjects().Single(root => root.name == RootName);
                return CaptureAppearance(rig);
            }
            finally
            {
                if (closeWhenFinished)
                    EditorSceneManager.CloseScene(scene, true);
                if (previousActive.IsValid() && previousActive.isLoaded && previousActive != scene)
                    SceneManager.SetActiveScene(previousActive);
            }
        }

        private static Scene GetLoadedScene(string path)
        {
            for (int index = 0; index < SceneManager.sceneCount; index++)
            {
                Scene candidate = SceneManager.GetSceneAt(index);
                if (candidate.path == path)
                    return candidate;
            }

            return default;
        }

        private static AppearanceSnapshot CapturePrefabAppearance(GameObject prefab)
        {
            GameObject instance = (GameObject)PrefabUtility.InstantiatePrefab(prefab);
            try
            {
                instance.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
                instance.transform.localScale = Vector3.one;
                return CaptureAppearance(instance);
            }
            finally
            {
                Object.DestroyImmediate(instance);
            }
        }

        private static AppearanceSnapshot CaptureAppearance(GameObject root)
        {
            var matrices = new Dictionary<string, Matrix4x4>(StringComparer.Ordinal);
            var materials = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (MeshFilter meshFilter in root.GetComponentsInChildren<MeshFilter>(true))
            {
                string key = AssetIdentifier(meshFilter.sharedMesh) + ":" + meshFilter.name;
                if (!matrices.TryAdd(key, meshFilter.transform.localToWorldMatrix))
                    throw new InvalidOperationException($"Duplicate visual mesh key '{key}'.");

                MeshRenderer renderer = meshFilter.GetComponent<MeshRenderer>();
                if (renderer == null)
                    throw new InvalidOperationException($"Rigid mesh '{meshFilter.name}' has no MeshRenderer.");
                materials.Add(
                    key,
                    string.Join(",", renderer.sharedMaterials.Select(AssetIdentifier)));
            }

            return new AppearanceSnapshot(matrices, materials);
        }

        private static void CompareAppearance(
            AppearanceSnapshot expected,
            AppearanceSnapshot actual,
            string context)
        {
            if (expected.Matrices.Count != actual.Matrices.Count)
            {
                throw new InvalidOperationException(
                    $"Visual mesh count changed during {context}: "
                    + $"expected {expected.Matrices.Count}, got {actual.Matrices.Count}.");
            }

            foreach (KeyValuePair<string, Matrix4x4> item in expected.Matrices)
            {
                if (!actual.Matrices.TryGetValue(item.Key, out Matrix4x4 actualMatrix))
                    throw new InvalidOperationException($"Visual mesh '{item.Key}' is missing after {context}.");
                if (!Approximately(item.Value, actualMatrix, MatrixTolerance))
                    throw new InvalidOperationException($"World matrix for '{item.Key}' changed during {context}.");
                if (!actual.Materials.TryGetValue(item.Key, out string actualMaterials)
                    || expected.Materials[item.Key] != actualMaterials)
                {
                    throw new InvalidOperationException($"Materials for '{item.Key}' changed during {context}.");
                }
            }
        }

        private static string AssetIdentifier(Object asset)
        {
            if (asset == null)
                return "null";
            return AssetDatabase.TryGetGUIDAndLocalFileIdentifier(asset, out string guid, out long localId)
                ? guid + ":" + localId
                : asset.name;
        }

        private static Transform CreateTransform(string name, Transform parent)
        {
            var gameObject = new GameObject(name);
            gameObject.transform.SetParent(parent, false);
            gameObject.transform.SetLocalPositionAndRotation(Vector3.zero, Quaternion.identity);
            gameObject.transform.localScale = Vector3.one;
            return gameObject.transform;
        }

        private static Transform FindUnique(Transform root, string name)
        {
            Transform[] matches = root.GetComponentsInChildren<Transform>(true)
                .Where(candidate => candidate.name == name)
                .ToArray();
            if (matches.Length != 1)
                throw new InvalidOperationException($"Expected one transform named '{name}', found {matches.Length}.");
            return matches[0];
        }

        private static Transform RequirePath(Transform root, string path)
        {
            Transform result = root.Find(path);
            return result != null
                ? result
                : throw new InvalidOperationException($"Required authoring path '{path}' is missing.");
        }

        private static T LoadRequired<T>(string path) where T : Object
        {
            T asset = AssetDatabase.LoadAssetAtPath<T>(path);
            return asset != null
                ? asset
                : throw new InvalidOperationException($"Required asset '{path}' is missing.");
        }

        private static bool Approximately(Vector3 left, Vector3 right)
        {
            return Mathf.Approximately(left.x, right.x)
                && Mathf.Approximately(left.y, right.y)
                && Mathf.Approximately(left.z, right.z);
        }

        private static bool Approximately(Vector3 left, Vector3 right, float tolerance)
        {
            return Mathf.Abs(left.x - right.x) <= tolerance
                && Mathf.Abs(left.y - right.y) <= tolerance
                && Mathf.Abs(left.z - right.z) <= tolerance;
        }

        private static bool Approximately(Quaternion left, Quaternion right)
        {
            return Mathf.Abs(Quaternion.Dot(left, right)) >= 0.999999f;
        }

        private static bool Approximately(Matrix4x4 left, Matrix4x4 right, float tolerance)
        {
            for (int index = 0; index < 16; index++)
            {
                if (Mathf.Abs(left[index] - right[index]) > tolerance)
                    return false;
            }

            return true;
        }

        private sealed class AppearanceSnapshot
        {
            public AppearanceSnapshot(
                Dictionary<string, Matrix4x4> matrices,
                Dictionary<string, string> materials)
            {
                Matrices = matrices;
                Materials = materials;
            }

            public Dictionary<string, Matrix4x4> Matrices { get; }
            public Dictionary<string, string> Materials { get; }
        }
    }
}
