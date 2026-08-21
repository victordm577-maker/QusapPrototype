#if UNITY_EDITOR
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Qusap.EditorTools
{
    [InitializeOnLoad]
    public static class CombatPlaygroundGenerator
    {
        private const string MovementPlaygroundPath = "Assets/_Qusap/Scenes/MovementPlayground.unity";
        private const string CombatPlayerPath = "Assets/_Qusap/Prefabs/QusapCombatPlayer.prefab";
        private const string OneWayPlatformPrefabPath = "Assets/_Qusap/Prefabs/QusapOneWayPlatform.prefab";
        private const string CombatScenePath = "Assets/_Qusap/Scenes/CombatPlayground.unity";
        private const string ArenaMaterialFolder = "Assets/_Qusap/Materials";
        private const string ArenaMaterialPath = ArenaMaterialFolder + "/CombatArenaGeometry.mat";

        static CombatPlaygroundGenerator()
        {
            EditorApplication.delayCall += GenerateMissingAssets;
        }

        [MenuItem("Tools/Qusap/Generate Combat Playground")]
        private static void GenerateFromMenu()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode)
            {
                EditorUtility.DisplayDialog(
                    "Combat Playground",
                    "La arena no puede generarse mientras Unity está en Play Mode.",
                    "Aceptar");
                return;
            }

            bool assetsExist = AssetDatabase.LoadAssetAtPath<GameObject>(CombatPlayerPath) != null
                || AssetDatabase.LoadAssetAtPath<SceneAsset>(CombatScenePath) != null;
            if (assetsExist && !EditorUtility.DisplayDialog(
                    "Regenerate Combat Playground",
                    "Se reemplazarán únicamente QusapCombatPlayer.prefab y CombatPlayground.unity.",
                    "Regenerar",
                    "Cancelar"))
            {
                return;
            }

            GenerateCombatPlaygroundForAutomation();
            EditorUtility.DisplayDialog(
                "Combat Playground",
                "La variante de jugador y la arena 1v1 quedaron generadas.",
                "Aceptar");
        }

        public static void GenerateCombatPlaygroundForAutomation()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode)
            {
                throw new System.InvalidOperationException(
                    "CombatPlayground cannot be generated while Unity is entering or running Play Mode.");
            }

            GenerateAllAssets();
            if (!IsCombatPlayerPrefabComplete() || !IsOneWayPlatformPrefabComplete())
            {
                throw new System.InvalidOperationException(
                    "CombatPlayground generation completed without a valid combat player or one-way platform prefab.");
            }
        }

        private static void GenerateMissingAssets()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode
                || EditorApplication.isCompiling
                || EditorApplication.isUpdating)
            {
                EditorApplication.delayCall += GenerateMissingAssets;
                return;
            }

            bool missingPlayer = AssetDatabase.LoadAssetAtPath<GameObject>(CombatPlayerPath) == null;
            bool missingPlatform = AssetDatabase.LoadAssetAtPath<GameObject>(OneWayPlatformPrefabPath) == null;
            bool missingScene = AssetDatabase.LoadAssetAtPath<SceneAsset>(CombatScenePath) == null;
            if (missingPlayer && missingPlatform && missingScene)
            {
                GenerateAllAssets();
            }
            else if (!missingScene
                && (!IsCombatPlayerPrefabComplete() || !IsOneWayPlatformPrefabComplete()))
            {
                Debug.LogWarning(
                    "CombatPlaygroundGenerator detected an incomplete generated combat asset and will repair the generated prefabs and scene.");
                GenerateAllAssets();
            }
            else if (missingPlayer || missingPlatform || missingScene)
            {
                Debug.LogWarning(
                    "CombatPlaygroundGenerator found only one generated asset. Use Tools/Qusap/Generate Combat Playground to confirm regeneration without overwriting silently.");
            }
        }

        private static void GenerateAllAssets()
        {
            try
            {
                EnsureArenaMaterial();
                AssetDatabase.SaveAssets();
                AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
                CreateOneWayPlatformPrefab();
                AssetDatabase.SaveAssets();
                AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
                if (!IsCombatPlayerPrefabComplete())
                {
                    CreateCombatPlayerPrefab();
                    AssetDatabase.SaveAssets();
                    AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
                }

                CreateCombatScene();
            }
            finally
            {
                AssetDatabase.SaveAssets();
                AssetDatabase.Refresh();
            }
        }

        private static void CreateOneWayPlatformPrefab()
        {
            Scene sourceScene = GetLoadedScene(MovementPlaygroundPath);
            bool closeSourceScene = !sourceScene.IsValid();
            sourceScene = closeSourceScene
                ? EditorSceneManager.OpenScene(MovementPlaygroundPath, OpenSceneMode.Additive)
                : sourceScene;
            Scene previewScene = EditorSceneManager.NewPreviewScene();

            try
            {
                QusapOneWayPlatform sourcePlatform = FindFunctionalOneWayPlatform(sourceScene);
                if (sourcePlatform == null || sourcePlatform.SolidCollider == null)
                {
                    Debug.LogError(
                        "CombatPlaygroundGenerator could not find a complete functional one-way platform in MovementPlayground.");
                    return;
                }

                GameObject platform = Object.Instantiate(sourcePlatform.SolidCollider.gameObject);
                SceneManager.MoveGameObjectToScene(platform, previewScene);
                MaterializePrefabHierarchy(platform);
                platform.name = "QusapOneWayPlatform";
                platform.transform.position = Vector3.zero;
                platform.transform.rotation = Quaternion.identity;

                PrefabUtility.SaveAsPrefabAsset(platform, OneWayPlatformPrefabPath, out bool saveSucceeded);
                Object.DestroyImmediate(platform);

                if (!saveSucceeded)
                {
                    Debug.LogError("CombatPlaygroundGenerator failed to save QusapOneWayPlatform.prefab.");
                }
            }
            finally
            {
                EditorSceneManager.ClosePreviewScene(previewScene);
                if (closeSourceScene && sourceScene.IsValid() && sourceScene.isLoaded)
                {
                    EditorSceneManager.CloseScene(sourceScene, true);
                }
            }
        }

        private static void CreateCombatPlayerPrefab()
        {
            Scene sourceScene = GetLoadedScene(MovementPlaygroundPath);
            bool closeSourceScene = !sourceScene.IsValid();
            sourceScene = closeSourceScene
                ? EditorSceneManager.OpenScene(MovementPlaygroundPath, OpenSceneMode.Additive)
                : sourceScene;
            Scene previewScene = EditorSceneManager.NewPreviewScene();
            try
            {
                QusapAnimationDriver sourceDriver = FindFunctionalPlayerDriver(sourceScene);
                if (sourceDriver == null)
                {
                    Debug.LogError(
                        "CombatPlaygroundGenerator could not find the enabled functional Qusap with an active PlayerVisual in MovementPlayground.");
                    return;
                }

                GameObject player = Object.Instantiate(sourceDriver.gameObject);
                SceneManager.MoveGameObjectToScene(player, previewScene);

                MaterializePrefabHierarchy(player);

                player.name = "QusapCombatPlayer";
                player.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
                player.transform.localScale = Vector3.one;

                Transform visual = player.transform.Find("PlayerVisual");
                if (visual == null || !visual.gameObject.activeSelf)
                {
                    Debug.LogError(
                        "CombatPlaygroundGenerator cloned the functional Qusap but its active PlayerVisual was not materialized.");
                    Object.DestroyImmediate(player);
                    return;
                }

                Animator animator = visual.GetComponent<Animator>();
                if (animator == null || animator.runtimeAnimatorController == null)
                {
                    Debug.LogError(
                        "CombatPlaygroundGenerator found PlayerVisual without its functional Animator or Animator Controller.");
                    Object.DestroyImmediate(player);
                    return;
                }

                QusapAnimationDriver animationDriver = KeepSingleComponent<QusapAnimationDriver>(player);
                if (animationDriver == null)
                {
                    animationDriver = player.AddComponent<QusapAnimationDriver>();
                    EditorUtility.CopySerialized(sourceDriver, animationDriver);
                }

                QusapHitReactionVisual hitReaction = KeepSingleComponent<QusapHitReactionVisual>(player);
                hitReaction ??= player.AddComponent<QusapHitReactionVisual>();
                hitReaction.Configure(visual, visual.GetComponentsInChildren<Renderer>(true));

                PrefabUtility.SaveAsPrefabAsset(player, CombatPlayerPath, out bool saveSucceeded);
                Object.DestroyImmediate(player);

                if (!saveSucceeded)
                {
                    Debug.LogError("CombatPlaygroundGenerator failed to save QusapCombatPlayer.prefab.");
                }
            }
            finally
            {
                EditorSceneManager.ClosePreviewScene(previewScene);
                if (closeSourceScene && sourceScene.IsValid() && sourceScene.isLoaded)
                {
                    EditorSceneManager.CloseScene(sourceScene, true);
                }
            }
        }

        private static Scene GetLoadedScene(string scenePath)
        {
            for (int index = 0; index < SceneManager.sceneCount; index++)
            {
                Scene candidate = SceneManager.GetSceneAt(index);
                if (candidate.path == scenePath)
                {
                    return candidate;
                }
            }

            return default;
        }

        private static QusapAnimationDriver FindFunctionalPlayerDriver(Scene sourceScene)
        {
            foreach (GameObject root in sourceScene.GetRootGameObjects())
            {
                foreach (QusapAnimationDriver driver in root.GetComponentsInChildren<QusapAnimationDriver>(true))
                {
                    Transform visual = driver.transform.Find("PlayerVisual");
                    Animator animator = visual != null ? visual.GetComponent<Animator>() : null;
                    if (driver.enabled
                        && driver.gameObject.activeInHierarchy
                        && visual != null
                        && visual.gameObject.activeInHierarchy
                        && animator != null
                        && animator.runtimeAnimatorController != null)
                    {
                        return driver;
                    }
                }
            }

            return null;
        }

        private static QusapOneWayPlatform FindFunctionalOneWayPlatform(Scene sourceScene)
        {
            QusapOneWayPlatform firstValidPlatform = null;

            foreach (GameObject root in sourceScene.GetRootGameObjects())
            {
                foreach (QusapOneWayPlatform platform in root.GetComponentsInChildren<QusapOneWayPlatform>(true))
                {
                    BoxCollider detectionTrigger = platform.GetComponent<BoxCollider>();
                    BoxCollider solidCollider = platform.SolidCollider as BoxCollider;
                    bool isComplete = platform.enabled
                        && detectionTrigger != null
                        && detectionTrigger.enabled
                        && detectionTrigger.isTrigger
                        && solidCollider != null
                        && solidCollider.enabled
                        && !solidCollider.isTrigger
                        && platform.transform.IsChildOf(solidCollider.transform);

                    if (!isComplete)
                    {
                        continue;
                    }

                    if (solidCollider.gameObject.name == "OneWay_01")
                    {
                        return platform;
                    }

                    firstValidPlatform ??= platform;
                }
            }

            return firstValidPlatform;
        }

        private static T KeepSingleComponent<T>(GameObject target) where T : Component
        {
            T[] components = target.GetComponents<T>();
            for (int index = 1; index < components.Length; index++)
            {
                Object.DestroyImmediate(components[index]);
            }

            return components.Length > 0 ? components[0] : null;
        }

        private static void MaterializePrefabHierarchy(GameObject root)
        {
            bool unpackedInstance;
            do
            {
                unpackedInstance = false;
                foreach (Transform candidate in root.GetComponentsInChildren<Transform>(true))
                {
                    if (!PrefabUtility.IsAnyPrefabInstanceRoot(candidate.gameObject))
                    {
                        continue;
                    }

                    PrefabUtility.UnpackPrefabInstance(
                        candidate.gameObject,
                        PrefabUnpackMode.Completely,
                        InteractionMode.AutomatedAction);
                    unpackedInstance = true;
                    break;
                }
            }
            while (unpackedInstance);
        }

        private static bool IsCombatPlayerPrefabComplete()
        {
            GameObject player = AssetDatabase.LoadAssetAtPath<GameObject>(CombatPlayerPath);
            if (player == null)
            {
                return false;
            }

            Transform visual = player.transform.Find("PlayerVisual");
            Animator animator = visual != null ? visual.GetComponent<Animator>() : null;
            QusapHitReactionVisual hitReaction = player.GetComponent<QusapHitReactionVisual>();
            if (visual == null
                || !visual.gameObject.activeSelf
                || PrefabUtility.IsPartOfPrefabInstance(visual.gameObject)
                || animator == null
                || animator.runtimeAnimatorController == null
                || player.GetComponents<QusapAnimationDriver>().Length != 1
                || player.GetComponents<QusapHitReactionVisual>().Length != 1)
            {
                return false;
            }

            SerializedObject serializedReaction = new(hitReaction);
            return serializedReaction.FindProperty("playerVisual").objectReferenceValue == visual;
        }

        private static bool IsOneWayPlatformPrefabComplete()
        {
            GameObject platformRoot = AssetDatabase.LoadAssetAtPath<GameObject>(OneWayPlatformPrefabPath);
            if (platformRoot == null)
            {
                return false;
            }

            QusapOneWayPlatform platform = platformRoot.GetComponentInChildren<QusapOneWayPlatform>(true);
            BoxCollider solidCollider = platformRoot.GetComponent<BoxCollider>();
            BoxCollider detectionTrigger = platform != null ? platform.GetComponent<BoxCollider>() : null;
            return platform != null
                && platformRoot.transform.childCount == 1
                && solidCollider != null
                && solidCollider.enabled
                && !solidCollider.isTrigger
                && detectionTrigger != null
                && detectionTrigger.enabled
                && detectionTrigger.isTrigger
                && platform.SolidCollider == solidCollider;
        }

        private static void CreateCombatScene()
        {
            GameObject playerPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(CombatPlayerPath);
            GameObject oneWayPlatformPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(OneWayPlatformPrefabPath);
            Material arenaMaterial = AssetDatabase.LoadAssetAtPath<Material>(ArenaMaterialPath);
            if (playerPrefab == null || oneWayPlatformPrefab == null)
            {
                Debug.LogError(
                    "CombatPlaygroundGenerator could not create the scene because a generated player or one-way platform prefab is missing.");
                return;
            }

            SceneAsset existingSceneAsset = AssetDatabase.LoadAssetAtPath<SceneAsset>(CombatScenePath);
            if (existingSceneAsset != null)
            {
                UpdateExistingCombatScene(oneWayPlatformPrefab);
                return;
            }

            Scene previousActiveScene = SceneManager.GetActiveScene();
            Scene combatScene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Additive);
            combatScene.name = "CombatPlayground";
            SceneManager.SetActiveScene(combatScene);

            try
            {
                GameObject arenaRoot = new("CombatArena");
                SceneManager.MoveGameObjectToScene(arenaRoot, combatScene);

                GameObject geometryRoot = new("Geometry");
                geometryRoot.transform.SetParent(arenaRoot.transform, false);
                CreateSolidCube("CentralFloor", geometryRoot.transform, new Vector3(0f, -0.5f, 0f), new Vector3(24f, 1f, 4f), arenaMaterial);
                CreateSolidCube("LeftBoundary", geometryRoot.transform, new Vector3(-12.5f, 3.5f, 0f), new Vector3(1f, 8f, 4f), arenaMaterial);
                CreateSolidCube("RightBoundary", geometryRoot.transform, new Vector3(12.5f, 3.5f, 0f), new Vector3(1f, 8f, 4f), arenaMaterial);
                CreateOneWayPlatformInstance(oneWayPlatformPrefab, "LeftPlatform", geometryRoot.transform, new Vector3(-5f, 2.6f, 0f));
                CreateOneWayPlatformInstance(oneWayPlatformPrefab, "CenterPlatform", geometryRoot.transform, new Vector3(0f, 4.4f, 0f));
                CreateOneWayPlatformInstance(oneWayPlatformPrefab, "RightPlatform", geometryRoot.transform, new Vector3(5f, 2.6f, 0f));

                GameObject playerOneObject = (GameObject)PrefabUtility.InstantiatePrefab(playerPrefab, combatScene);
                GameObject playerTwoObject = (GameObject)PrefabUtility.InstantiatePrefab(playerPrefab, combatScene);
                playerOneObject.name = "Player1_Qusap";
                playerTwoObject.name = "Player2_Qusap";

                Vector3 playerOnePosition = new(-5f, 1.05f, 0f);
                Vector3 playerTwoPosition = new(5f, 1.05f, 0f);
                playerOneObject.transform.position = playerOnePosition;
                playerTwoObject.transform.position = playerTwoPosition;
                Transform playerOneVisual = playerOneObject.transform.Find("PlayerVisual");
                Transform playerTwoVisual = playerTwoObject.transform.Find("PlayerVisual");
                if (playerOneVisual == null || playerTwoVisual == null)
                {
                    Debug.LogError(
                        "CombatPlaygroundGenerator refused to save players without a direct PlayerVisual child.");
                    return;
                }

                playerOneVisual.localRotation = Quaternion.Euler(0f, 150f, 0f);
                playerTwoVisual.localRotation = Quaternion.Euler(0f, 210f, 0f);

                QusapInputReader playerOneInput = playerOneObject.GetComponent<QusapInputReader>();
                QusapInputReader playerTwoInput = playerTwoObject.GetComponent<QusapInputReader>();
                playerOneInput.SetLocalPlayerSlot(QusapLocalPlayerSlot.Player1Keyboard);
                playerTwoInput.SetLocalPlayerSlot(QusapLocalPlayerSlot.Player2Gamepad);
                SetInitialFacing(playerOneObject.GetComponent<QusapCombatController>(), 1);
                SetInitialFacing(playerTwoObject.GetComponent<QusapCombatController>(), -1);

                QusapCombatArenaController arenaController = arenaRoot.AddComponent<QusapCombatArenaController>();
                arenaController.Configure(playerOneInput, playerTwoInput, playerOnePosition, playerTwoPosition, true);

                GameObject cameraObject = new("Main Camera");
                SceneManager.MoveGameObjectToScene(cameraObject, combatScene);
                cameraObject.tag = "MainCamera";
                cameraObject.transform.position = new Vector3(0f, 2.5f, -20f);
                Camera targetCamera = cameraObject.AddComponent<Camera>();
                targetCamera.orthographic = true;
                targetCamera.orthographicSize = 7f;
                targetCamera.clearFlags = CameraClearFlags.SolidColor;
                targetCamera.backgroundColor = new Color(0.08f, 0.1f, 0.14f, 1f);
                cameraObject.AddComponent<AudioListener>();
                QusapSharedCombatCamera cameraController = cameraObject.AddComponent<QusapSharedCombatCamera>();
                cameraController.Configure(playerOneObject.transform, playerTwoObject.transform);

                GameObject lightObject = new("Directional Light");
                SceneManager.MoveGameObjectToScene(lightObject, combatScene);
                lightObject.transform.rotation = Quaternion.Euler(38f, -32f, 0f);
                Light light = lightObject.AddComponent<Light>();
                light.type = LightType.Directional;
                light.intensity = 1.25f;

                EditorSceneManager.SaveScene(combatScene, CombatScenePath);
            }
            finally
            {
                EditorSceneManager.CloseScene(combatScene, true);
                if (previousActiveScene.IsValid() && previousActiveScene.isLoaded)
                {
                    SceneManager.SetActiveScene(previousActiveScene);
                }
            }
        }

        private static void UpdateExistingCombatScene(GameObject oneWayPlatformPrefab)
        {
            Scene combatScene = GetLoadedScene(CombatScenePath);
            bool closeCombatScene = !combatScene.IsValid();
            combatScene = closeCombatScene
                ? EditorSceneManager.OpenScene(CombatScenePath, OpenSceneMode.Additive)
                : combatScene;

            try
            {
                Transform geometryRoot = FindTransformInScene(combatScene, "Geometry");
                if (geometryRoot == null)
                {
                    Debug.LogError(
                        "CombatPlaygroundGenerator could not update the existing scene because its Geometry root is missing.");
                    return;
                }

                ReplaceOneWayPlatforms(oneWayPlatformPrefab, geometryRoot);
                EditorSceneManager.MarkSceneDirty(combatScene);
                EditorSceneManager.SaveScene(combatScene);
            }
            finally
            {
                if (closeCombatScene && combatScene.IsValid() && combatScene.isLoaded)
                {
                    EditorSceneManager.CloseScene(combatScene, true);
                }
            }
        }

        private static void ReplaceOneWayPlatforms(GameObject oneWayPlatformPrefab, Transform geometryRoot)
        {
            string[] platformNames = { "LeftPlatform", "CenterPlatform", "RightPlatform" };
            foreach (string platformName in platformNames)
            {
                Transform existingPlatform = geometryRoot.Find(platformName);
                if (existingPlatform != null)
                {
                    Object.DestroyImmediate(existingPlatform.gameObject);
                }
            }

            CreateOneWayPlatformInstance(oneWayPlatformPrefab, "LeftPlatform", geometryRoot, new Vector3(-5f, 2.6f, 0f));
            CreateOneWayPlatformInstance(oneWayPlatformPrefab, "CenterPlatform", geometryRoot, new Vector3(0f, 4.4f, 0f));
            CreateOneWayPlatformInstance(oneWayPlatformPrefab, "RightPlatform", geometryRoot, new Vector3(5f, 2.6f, 0f));
        }

        private static Transform FindTransformInScene(Scene scene, string objectName)
        {
            foreach (GameObject root in scene.GetRootGameObjects())
            {
                foreach (Transform candidate in root.GetComponentsInChildren<Transform>(true))
                {
                    if (candidate.name == objectName)
                    {
                        return candidate;
                    }
                }
            }

            return null;
        }

        private static GameObject CreateSolidCube(
            string name,
            Transform parent,
            Vector3 position,
            Vector3 scale,
            Material material)
        {
            GameObject cube = GameObject.CreatePrimitive(PrimitiveType.Cube);
            cube.name = name;
            cube.transform.SetParent(parent, false);
            cube.transform.position = position;
            cube.transform.localScale = scale;
            if (material != null)
            {
                cube.GetComponent<MeshRenderer>().sharedMaterial = material;
            }

            return cube;
        }

        private static void CreateOneWayPlatformInstance(
            GameObject platformPrefab,
            string name,
            Transform parent,
            Vector3 position)
        {
            GameObject platform = (GameObject)PrefabUtility.InstantiatePrefab(platformPrefab, parent);
            platform.name = name;
            platform.transform.position = position;
            platform.transform.rotation = Quaternion.identity;
        }

        private static void SetInitialFacing(QusapCombatController controller, int direction)
        {
            SerializedObject serializedController = new(controller);
            serializedController.FindProperty("initialFacingDirection").intValue = direction < 0 ? -1 : 1;
            serializedController.ApplyModifiedPropertiesWithoutUndo();
        }

        private static void EnsureArenaMaterial()
        {
            if (AssetDatabase.LoadAssetAtPath<Material>(ArenaMaterialPath) != null)
            {
                return;
            }

            if (!AssetDatabase.IsValidFolder(ArenaMaterialFolder))
            {
                AssetDatabase.CreateFolder("Assets/_Qusap", "Materials");
            }

            Shader shader = Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard");
            if (shader == null)
            {
                Debug.LogError("CombatPlaygroundGenerator could not find a compatible Lit shader for arena geometry.");
                return;
            }

            Material material = new(shader)
            {
                name = "CombatArenaGeometry",
                color = new Color(0.22f, 0.3f, 0.38f, 1f)
            };
            AssetDatabase.CreateAsset(material, ArenaMaterialPath);
        }
    }
}
#endif
