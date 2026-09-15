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
        private const string ModelSpaceName = "ModelSpace";
        private const string BodyPivotName = "BodyPivot";
        private const string LeftFootPivotName = "FootPivot_L";
        private const string RightFootPivotName = "FootPivot_R";
        private const string WeaponSocketName = "WeaponSocket";

        [MenuItem("Qusap/Manual Animation/Create Authoring Workspace")]
        public static void BuildAll()
        {
            EnsureTargetsDoNotExist();
            EnsureFolders();

            AnimationClip clip = BuildNeutralClip();
            AnimatorController controller = BuildController(clip);
            GameObject rigPrefab = BuildRigPrefab(controller, clip);
            BuildAuthoringScene(rigPrefab);

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
            Debug.Log("QUSAP_MANUAL_AUTHORING_VALIDATION_OK");
        }

        private static void EnsureTargetsDoNotExist()
        {
            string[] targets = { ClipPath, ControllerPath, RigPrefabPath, ScenePath };
            foreach (string target in targets)
            {
                if (AssetDatabase.LoadMainAssetAtPath(target) != null)
                    throw new InvalidOperationException($"Refusing to overwrite existing asset '{target}'.");
            }
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
            var clip = new AnimationClip
            {
                name = "Qusap_Idle_Manual_v1",
                frameRate = 60f,
                wrapMode = WrapMode.Loop
            };
            AssetDatabase.CreateAsset(clip, ClipPath);
            return clip;
        }

        private static AnimatorController BuildController(AnimationClip clip)
        {
            AnimatorController controller = AnimatorController.CreateAnimatorControllerAtPath(ControllerPath);
            AnimatorStateMachine stateMachine = controller.layers[0].stateMachine;
            AnimatorState state = stateMachine.AddState("Idle_Manual_v1");
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
                PrefabUtility.UnpackPrefabInstance(
                    visualInstance,
                    PrefabUnpackMode.Completely,
                    InteractionMode.AutomatedAction);

                Transform importedModelSpace = FindUnique(visualInstance.transform, "QusapVisualRoot");
                importedModelSpace.SetParent(animationRoot, true);
                importedModelSpace.name = ModelSpaceName;
                Object.DestroyImmediate(visualInstance);

                Transform bodyPivot = FindUnique(importedModelSpace, BodyPivotName);
                Transform leftFootPivot = FindUnique(importedModelSpace, LeftFootPivotName);
                Transform rightFootPivot = FindUnique(importedModelSpace, RightFootPivotName);
                Transform weaponSocket = CreateTransform(WeaponSocketName, animationRoot);
                weaponSocket.localPosition = new Vector3(1.15f, 1.25f, -0.35f);
                weaponSocket.localRotation = Quaternion.Euler(0f, 0f, -12f);

                GameObject swordInstance = (GameObject)PrefabUtility.InstantiatePrefab(
                    blueSword, weaponSocket);
                swordInstance.name = blueSword.name;
                swordInstance.transform.SetLocalPositionAndRotation(Vector3.zero, Quaternion.identity);
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
            Scene scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Additive);
            scene.name = "Qusap_AnimationAuthoring";
            SceneManager.SetActiveScene(scene);

            try
            {
                GameObject rig = (GameObject)PrefabUtility.InstantiatePrefab(rigPrefab, scene);
                rig.name = RootName;
                rig.transform.SetPositionAndRotation(Vector3.zero, Quaternion.Euler(0f, 60f, 0f));
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

                if (!EditorSceneManager.SaveScene(scene, ScenePath))
                    throw new InvalidOperationException("Could not save the animation authoring scene.");
            }
            finally
            {
                EditorSceneManager.CloseScene(scene, true);
                if (previousActive.IsValid() && previousActive.isLoaded)
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
            Transform modelSpace = RequirePath(rig.transform, AnimationRootName + "/" + ModelSpaceName);
            Transform body = RequirePath(rig.transform, AnimationRootName + "/" + ModelSpaceName + "/" + BodyPivotName);
            Transform left = RequirePath(rig.transform, AnimationRootName + "/" + ModelSpaceName + "/" + LeftFootPivotName);
            Transform right = RequirePath(rig.transform, AnimationRootName + "/" + ModelSpaceName + "/" + RightFootPivotName);
            Transform socket = RequirePath(rig.transform, AnimationRootName + "/" + WeaponSocketName);

            foreach (Transform control in new[] { animationRoot, body, left, right, socket })
            {
                if (!Approximately(control.localScale, Vector3.one))
                    throw new InvalidOperationException($"Animated control '{control.name}' must have local scale 1.");
            }

            MeshFilter[] characterMeshes = modelSpace.GetComponentsInChildren<MeshFilter>(true);
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
        }

        private static void ValidateClip(Transform rigRoot, AnimationClip clip)
        {
            if (!Mathf.Approximately(clip.frameRate, 60f))
                throw new InvalidOperationException("The neutral clip must use 60 FPS.");

            var expectedPaths = new HashSet<string>
            {
                AnimationRootName + "/" + ModelSpaceName + "/" + BodyPivotName,
                AnimationRootName + "/" + ModelSpaceName + "/" + LeftFootPivotName,
                AnimationRootName + "/" + ModelSpaceName + "/" + RightFootPivotName,
                AnimationRootName + "/" + WeaponSocketName
            };
            EditorCurveBinding[] bindings = AnimationUtility.GetCurveBindings(clip);
            if (bindings.Length != 28)
                throw new InvalidOperationException($"Expected 28 neutral transform curves; found {bindings.Length}.");
            if (bindings.Any(binding => !expectedPaths.Contains(binding.path)
                    || binding.path == AnimationRootName
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
            Scene scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Additive);
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
                EditorSceneManager.CloseScene(scene, true);
                if (previousActive.IsValid() && previousActive.isLoaded)
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
    }
}
