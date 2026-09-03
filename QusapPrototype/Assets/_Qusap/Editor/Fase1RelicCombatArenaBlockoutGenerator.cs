#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Qusap.EditorTools
{
    public static class Fase1RelicCombatArenaBlockoutGenerator
    {
        private const string BaseScenePath = "Assets/_Qusap/Scenes/CombatPlayground.unity";
        private const string TargetScenePath = "Assets/_Qusap/Scenes/Fase1_RelicCombatArena_Blockout.unity";
        private const string TargetPrefabPath = "Assets/_Qusap/Prefabs/Levels/Fase1_RelicCombatArena_Blockout.prefab";
        private const string PlayerPrefabPath = "Assets/_Qusap/Prefabs/QusapCombatPlayer.prefab";
        private const string OneWayPrefabPath = "Assets/_Qusap/Prefabs/QusapOneWayPlatform.prefab";
        private const string MaterialFolder = "Assets/_Qusap/Materials/RelicCombatArenaBlockout";
        private const int ExpectedSolidColliderCount = 18;
        private const int ExpectedOneWayCount = 41;
        private const int ExpectedPlayerSlotCount = 8;
        private const float ArenaWidth = 40f;
        private const float ArenaHeight = 30f;
        private const float StandardRise = 1.65f;
        private const float ShortcutRise = 1.9f;

        private static readonly Vector3 DebugPlayerStart = new(-16.05f, 1.05f, 0f);

        private readonly struct Block
        {
            public Block(string name, float x, float y, float width, float height)
            {
                Name = name;
                Position = new Vector3(x, y, 0f);
                Scale = new Vector3(width, height, 2f);
            }

            public string Name { get; }
            public Vector3 Position { get; }
            public Vector3 Scale { get; }
        }

        private readonly struct Platform
        {
            public Platform(string name, float x, float y, float width = 4f)
            {
                Name = name;
                Position = new Vector3(x, y, 0f);
                Width = width;
            }

            public string Name { get; }
            public Vector3 Position { get; }
            public float Width { get; }
        }

        private static readonly Block[] OuterBounds =
        {
            new("OuterFloor_40x1", 0f, -0.5f, 40f, 1f),
            new("OuterWall_Left", -20.5f, 15f, 1f, 30f),
            new("OuterWall_Right", 20.5f, 15f, 1f, 30f),
            new("OuterCeiling_40x1", 0f, 30.5f, 40f, 1f)
        };

        private static readonly Block[] SolidGeometry =
        {
            new("RecoveryDeck_LowerLeft", -15.75f, 2.4f, 6.5f, 0.5f),
            new("RecoveryDeck_LowerRight", 15.75f, 2.4f, 6.5f, 0.5f),
            new("RecoveryShelf_MiddleLeft", -16.25f, 10.75f, 5.5f, 0.5f),
            new("RecoveryShelf_MiddleRight", 16.25f, 10.75f, 5.5f, 0.5f),
            new("RecoveryShelf_UpperLeft", -15.5f, 18.9f, 5f, 0.5f),
            new("RecoveryShelf_UpperRight", 15.5f, 18.9f, 5f, 0.5f)
        };

        private static readonly Block[] WallJumpBlocks =
        {
            new("WallJump_LowerLeft_Outer", -18.25f, 6.45f, 0.5f, 7.6f),
            new("WallJump_LowerLeft_Inner", -14.25f, 6.45f, 0.5f, 7.6f),
            new("WallJump_LowerRight_Inner", 14.25f, 6.45f, 0.5f, 7.6f),
            new("WallJump_LowerRight_Outer", 18.25f, 6.45f, 0.5f, 7.6f),
            new("WallJump_UpperLeft_Outer", -18f, 15.1f, 0.5f, 7.6f),
            new("WallJump_UpperLeft_Inner", -13.5f, 15.1f, 0.5f, 7.6f),
            new("WallJump_UpperRight_Inner", 13.5f, 15.1f, 0.5f, 7.6f),
            new("WallJump_UpperRight_Outer", 18f, 15.1f, 0.5f, 7.6f)
        };

        private static readonly Platform[] OneWayPlatforms =
        {
            // Shared lower fan. Every mandatory surface rise is 1.65 units.
            new("Shared_LowerEntry_Left", -10f, 1.4f, 5f),
            new("Shared_LowerEntry_Center", 0f, 1.4f, 6f),
            new("Shared_LowerEntry_Right", 10f, 1.4f, 5f),

            new("Left_01_LowerRecovery", -14f, 3.05f),
            new("Center_01_DirectRise", 0f, 3.05f, 6f),
            new("Right_01_LowerRecovery", 14f, 3.05f),

            new("Left_02_InwardStep", -10.5f, 4.7f),
            new("Center_02_ExposeRight", 3f, 4.7f, 5f),
            new("Right_02_InwardStep", 10.5f, 4.7f),

            new("Left_03_WallJumpExit", -15f, 6.35f),
            new("Center_03_ExposeLeft", -3f, 6.35f, 5f),
            new("Right_03_WallJumpExit", 15f, 6.35f),

            new("Left_04_LowerCross", -10f, 8f, 5f),
            new("Center_04_LowerBrawl", 0f, 8f, 12f),
            new("Right_04_LowerCross", 10f, 8f, 5f),

            new("Left_05_OuterReturn", -14f, 9.65f),
            new("Center_05_DirectRise", 3f, 9.65f, 6f),
            new("Right_05_OuterReturn", 14f, 9.65f),

            new("Left_06_CenterSwitch", -9f, 11.3f, 5f),
            new("Center_06_CrossTraffic", -3f, 11.3f, 6f),
            new("Right_06_CenterSwitch", 9.5f, 11.3f, 5f),

            new("Shared_MiddleReconnect", 0f, 12.95f, 12f),

            new("Left_07_UpperWallEntry", -12.5f, 14.6f),
            new("Center_07_ExposedRise", 0f, 14.6f, 6f),
            new("Right_07_UpperWallEntry", 12.5f, 14.6f),

            new("Left_08_InwardStep", -8.5f, 16.25f, 5f),
            new("Center_08_ExposeRight", 3f, 16.25f, 5f),
            new("Right_08_InwardStep", 8.5f, 16.25f, 5f),

            new("Left_09_RecoveryLoop", -13f, 17.9f),
            new("Center_09_ExposeLeft", -3f, 17.9f, 5f),
            new("Right_09_RecoveryLoop", 13f, 17.9f),

            new("Shared_UpperReconnect", 0f, 19.55f, 14f),

            new("Left_10_LateralReentry", -10.5f, 21.2f, 5f),
            new("Center_10_UpperBrawl", 0f, 21.2f, 7f),
            new("Right_10_LateralReentry", 10.5f, 21.2f, 5f),

            new("Left_11_RelicApproach", -7f, 22.85f, 5f),
            new("Center_11_RelicApproach", 0f, 22.85f, 5f),
            new("Right_11_RelicApproach", 7f, 22.85f, 5f),

            new("RelicCombatPlatform_ThreeWayAccess", 0f, 24.5f, 14f),

            // Optional 76%-of-jump shortcuts into the upper wall-jump loops.
            new("Shortcut_Left_DashWallEntry", -16.5f, 13.2f, 3f),
            new("Shortcut_Right_DashWallEntry", 16.5f, 13.2f, 3f)
        };

        [MenuItem("Tools/Qusap/Build Fase 1 Relic Combat Arena")]
        private static void BuildFromMenu()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode)
            {
                EditorUtility.DisplayDialog(
                    "Fase 1 Relic Combat Arena",
                    "La arena no puede generarse mientras Unity esta en Play Mode.",
                    "Aceptar");
                return;
            }

            if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo())
            {
                return;
            }

            bool targetExists = GeneratedTargetsExist();
            if (targetExists && !EditorUtility.DisplayDialog(
                    "Regenerate Fase 1 Relic Combat Arena",
                    "Se reemplazaran solamente la escena, el prefab y los materiales propios de la arena de reliquia.",
                    "Regenerar",
                    "Cancelar"))
            {
                return;
            }

            BuildSceneAndPrefab(targetExists);
            EditorUtility.DisplayDialog(
                "Fase 1 Relic Combat Arena",
                "La arena vertical fue generada, validada y guardada. La escena quedo abierta para inspeccion y Play Mode.",
                "Aceptar");
        }

        // Entry point used only to materialize and validate this new asset in batch mode.
        public static void BuildForAutomation()
        {
            BuildSceneAndPrefab(GeneratedTargetsExist());
        }

        private static void BuildSceneAndPrefab(bool replaceExisting)
        {
            ValidateRequiredAssets();
            EnsureGeneratedFolders();
            CloseLoadedTargetScene();

            if (replaceExisting)
            {
                DeleteGeneratedAssetIfPresent(TargetScenePath);
                DeleteGeneratedAssetIfPresent(TargetPrefabPath);
            }

            Material solidMaterial = CreateOrUpdateMaterial("RelicArena_Solid.mat", new Color(0.29f, 0.34f, 0.39f));
            Material oneWayMaterial = CreateOrUpdateMaterial("RelicArena_OneWay.mat", new Color(0.39f, 0.63f, 0.76f));
            Material portalMaterial = CreateOrUpdateMaterial("RelicArena_PortalArrival.mat", new Color(0.15f, 0.92f, 1f));
            Material relicMaterial = CreateOrUpdateMaterial("RelicArena_RelicSocket.mat", new Color(1f, 0.73f, 0.12f));
            Material hazardMaterial = CreateOrUpdateMaterial("RelicArena_FutureHazard.mat", new Color(1f, 0.24f, 0.12f));

            if (!AssetDatabase.CopyAsset(BaseScenePath, TargetScenePath))
            {
                throw new InvalidOperationException($"Could not copy the stable combat scene from {BaseScenePath}.");
            }

            AssetDatabase.ImportAsset(TargetScenePath, ImportAssetOptions.ForceSynchronousImport);
            Scene targetScene = EditorSceneManager.OpenScene(TargetScenePath, OpenSceneMode.Single);
            SceneManager.SetActiveScene(targetScene);

            RemoveLegacyGeometry(targetScene);
            GameObject editableRoot = CreateArenaHierarchy(
                solidMaterial,
                oneWayMaterial,
                portalMaterial,
                relicMaterial,
                hazardMaterial);

            GameObject savedPrefab = PrefabUtility.SaveAsPrefabAsset(editableRoot, TargetPrefabPath);
            if (savedPrefab == null)
            {
                throw new InvalidOperationException($"Could not save reusable arena prefab at {TargetPrefabPath}.");
            }

            UnityEngine.Object.DestroyImmediate(editableRoot);
            GameObject arenaInstance = (GameObject)PrefabUtility.InstantiatePrefab(savedPrefab, targetScene);
            arenaInstance.name = "Fase1_RelicCombatArena";
            arenaInstance.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);

            ConfigureSinglePlayerScene(targetScene);
            ValidateGeneratedContent(targetScene, arenaInstance);
            EditorSceneManager.MarkSceneDirty(targetScene);
            if (!EditorSceneManager.SaveScene(targetScene, TargetScenePath))
            {
                throw new InvalidOperationException($"Could not save generated scene at {TargetScenePath}.");
            }

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            Selection.activeGameObject = arenaInstance;
            Debug.Log(
                $"Built Fase 1 Relic Combat Arena: {ArenaWidth}x{ArenaHeight}, " +
                $"{ExpectedSolidColliderCount} solid blocks, {ExpectedOneWayCount} one-way platforms, " +
                $"{ExpectedPlayerSlotCount} player slots.");
        }

        private static GameObject CreateArenaHierarchy(
            Material solidMaterial,
            Material oneWayMaterial,
            Material portalMaterial,
            Material relicMaterial,
            Material hazardMaterial)
        {
            GameObject root = new("Fase1_RelicCombatArena");
            Transform geometry = CreateGroup("Geometry", root.transform);
            Transform outerBounds = CreateGroup("OuterBounds", geometry);
            Transform solidGeometry = CreateGroup("SolidGeometry", geometry);
            Transform oneWayPlatforms = CreateGroup("OneWayPlatforms", geometry);
            Transform wallJumpSections = CreateGroup("WallJumpSections", geometry);
            Transform portalArrivals = CreateGroup("PortalArrivals", root.transform);
            Transform routes = CreateGroup("Routes", root.transform);
            Transform leftRoute = CreateGroup("LeftRoute", routes);
            Transform centerRoute = CreateGroup("CenterRoute", routes);
            Transform rightRoute = CreateGroup("RightRoute", routes);
            Transform relicArea = CreateGroup("RelicArea", root.transform);
            Transform futureHazards = CreateGroup("FutureHazards", root.transform);
            Transform fallRecovery = CreateGroup("FallRecovery", root.transform);
            Transform debug = CreateGroup("Debug", root.transform);

            CreateBlocks(OuterBounds, outerBounds, solidMaterial);
            CreateBlocks(SolidGeometry, solidGeometry, solidMaterial);
            CreateBlocks(WallJumpBlocks, wallJumpSections, solidMaterial);

            GameObject oneWayPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(OneWayPrefabPath);
            foreach (Platform platform in OneWayPlatforms)
            {
                CreateOneWayPlatform(oneWayPrefab, platform, oneWayPlatforms, oneWayMaterial);
            }

            CreatePortalArrival("PortalArrival_01", -15f, portalArrivals, portalMaterial);
            CreatePortalArrival("PortalArrival_02", -5f, portalArrivals, portalMaterial);
            CreatePortalArrival("PortalArrival_03", 5f, portalArrivals, portalMaterial);
            CreatePortalArrival("PortalArrival_04", 15f, portalArrivals, portalMaterial);

            CreateRouteWaypoints(leftRoute, new[]
            {
                new Vector3(-14f, 3.05f, 0f), new Vector3(-10f, 8f, 0f),
                new Vector3(-12.5f, 14.6f, 0f), new Vector3(-10.5f, 21.2f, 0f),
                new Vector3(-7f, 22.85f, 0f)
            });
            CreateRouteWaypoints(centerRoute, new[]
            {
                new Vector3(0f, 3.05f, 0f), new Vector3(0f, 8f, 0f),
                new Vector3(0f, 12.95f, 0f), new Vector3(0f, 19.55f, 0f),
                new Vector3(0f, 22.85f, 0f)
            });
            CreateRouteWaypoints(rightRoute, new[]
            {
                new Vector3(14f, 3.05f, 0f), new Vector3(10f, 8f, 0f),
                new Vector3(12.5f, 14.6f, 0f), new Vector3(10.5f, 21.2f, 0f),
                new Vector3(7f, 22.85f, 0f)
            });

            CreateMarker(
                "RelicPedestalSocket",
                relicArea,
                new Vector3(0f, 25.05f, 0f),
                relicMaterial,
                PrimitiveType.Cylinder,
                new Vector3(0.8f, 0.18f, 0.8f));
            CreateMarker(
                "RelicProtectionSocket",
                relicArea,
                new Vector3(0f, 26.4f, 0f),
                relicMaterial,
                PrimitiveType.Sphere,
                Vector3.one * 0.9f);

            CreateMarker("HazardSocket_LeftUpper_Reserved", futureHazards, new Vector3(-10.5f, 21.45f, 0f), hazardMaterial, PrimitiveType.Cylinder, new Vector3(0.3f, 0.45f, 0.3f));
            CreateMarker("HazardSocket_RightUpper_Reserved", futureHazards, new Vector3(10.5f, 21.45f, 0f), hazardMaterial, PrimitiveType.Cylinder, new Vector3(0.3f, 0.45f, 0.3f));
            CreateMarker("HazardSocket_CenterExposure_Reserved", futureHazards, new Vector3(0f, 13.25f, 0f), hazardMaterial, PrimitiveType.Cylinder, new Vector3(0.3f, 0.45f, 0.3f));

            CreateGroup("RecoveryRoute_Left_Lower", fallRecovery).localPosition = new Vector3(-15.75f, 2.65f, 0f);
            CreateGroup("RecoveryRoute_Right_Lower", fallRecovery).localPosition = new Vector3(15.75f, 2.65f, 0f);
            CreateGroup("RecoveryRoute_Left_Upper", fallRecovery).localPosition = new Vector3(-15.5f, 19.15f, 0f);
            CreateGroup("RecoveryRoute_Right_Upper", fallRecovery).localPosition = new Vector3(15.5f, 19.15f, 0f);
            CreateMarker("FallResetZone_Reserved", fallRecovery, new Vector3(0f, -2.5f, 0f), hazardMaterial, PrimitiveType.Cube, new Vector3(10f, 0.1f, 0.1f));

            CreateMarker("ArenaExtent_BottomLeft", debug, new Vector3(-20f, 0f, 0f), portalMaterial, PrimitiveType.Cube, Vector3.one * 0.15f);
            CreateMarker("ArenaExtent_TopRight", debug, new Vector3(20f, 30f, 0f), portalMaterial, PrimitiveType.Cube, Vector3.one * 0.15f);
            CreateGroup("MeasuredSourceLootMap_64x42", debug);
            CreateGroup("StandardRise_1_65_Is_66PercentOfJump", debug);
            CreateGroup("ShortcutRise_1_90_Is_76PercentOfJump", debug);

            return root;
        }

        private static void CreatePortalArrival(string name, float x, Transform parent, Material material)
        {
            Transform arrival = CreateGroup(name, parent);
            arrival.localPosition = new Vector3(x, 1.05f, 0f);
            CreateVisualCube("PortalVisual_Backdrop_NoGameplay", arrival, new Vector3(0f, 0.45f, 0f), new Vector3(2.15f, 2.25f, 0.12f), material);
            CreateVisualCube("PortalVisual_LeftPost_NoGameplay", arrival, new Vector3(-1.2f, 0.2f, 0f), new Vector3(0.18f, 2.5f, 0.22f), material);
            CreateVisualCube("PortalVisual_RightPost_NoGameplay", arrival, new Vector3(1.2f, 0.2f, 0f), new Vector3(0.18f, 2.5f, 0.22f), material);
            CreateVisualCube("PortalVisual_Top_NoGameplay", arrival, new Vector3(0f, 1.5f, 0f), new Vector3(2.55f, 0.18f, 0.22f), material);

            string suffix = name.Substring(name.Length - 2);
            CreateMarker($"PlayerSlot_{suffix}A", arrival, new Vector3(-1.05f, 0f, 0f), material, PrimitiveType.Cylinder, new Vector3(0.42f, 0.05f, 0.42f));
            CreateMarker($"PlayerSlot_{suffix}B", arrival, new Vector3(1.05f, 0f, 0f), material, PrimitiveType.Cylinder, new Vector3(0.42f, 0.05f, 0.42f));
        }

        private static void CreateRouteWaypoints(Transform route, IReadOnlyList<Vector3> positions)
        {
            for (int index = 0; index < positions.Count; index++)
            {
                Transform waypoint = CreateGroup($"RouteWaypoint_{index + 1:00}", route);
                waypoint.localPosition = positions[index];
            }
        }

        private static void ConfigureSinglePlayerScene(Scene scene)
        {
            GameObject legacyArenaObject = RequireGameObject(scene, "CombatArena");
            QusapCombatArenaController arena = legacyArenaObject.GetComponent<QusapCombatArenaController>();
            if (arena == null || arena.PlayerOne == null || arena.PlayerTwo == null)
            {
                throw new InvalidOperationException("The stable combat scene is missing its configured two-player arena controller.");
            }

            GameObject sceneDebug = new("SceneDebug");
            SceneManager.MoveGameObjectToScene(sceneDebug, scene);

            QusapInputReader player = arena.PlayerOne;
            player.name = "Player1_Qusap_RouteTest";
            player.SetLocalPlayerSlot(QusapLocalPlayerSlot.Player1Keyboard);
            player.transform.SetParent(sceneDebug.transform, false);
            player.transform.SetPositionAndRotation(DebugPlayerStart, Quaternion.identity);
            UnityEngine.Object.DestroyImmediate(arena.PlayerTwo.gameObject);

            GameObject cameraObject = RequireGameObject(scene, "Main Camera");
            QusapSharedCombatCamera sharedCamera = cameraObject.GetComponent<QusapSharedCombatCamera>();
            if (sharedCamera == null)
            {
                throw new InvalidOperationException("The stable combat scene is missing its shared combat camera.");
            }

            UnityEngine.Object.DestroyImmediate(sharedCamera);
            QusapCameraFollow follow = cameraObject.AddComponent<QusapCameraFollow>();
            SerializedObject serializedFollow = new(follow);
            serializedFollow.FindProperty("target").objectReferenceValue = player.transform;
            serializedFollow.FindProperty("offset").vector3Value = new Vector3(0f, 1.5f, -20f);
            serializedFollow.FindProperty("horizontalSmoothTime").floatValue = 0.12f;
            serializedFollow.FindProperty("verticalSmoothTime").floatValue = 0.12f;
            serializedFollow.ApplyModifiedPropertiesWithoutUndo();
            cameraObject.transform.position = DebugPlayerStart + new Vector3(0f, 1.5f, -20f);

            UnityEngine.Object.DestroyImmediate(arena);
            if (legacyArenaObject.transform.childCount == 0)
            {
                UnityEngine.Object.DestroyImmediate(legacyArenaObject);
            }
            else
            {
                legacyArenaObject.name = "SceneRuntime";
            }
        }

        private static void ValidateRequiredAssets()
        {
            if (AssetDatabase.LoadAssetAtPath<SceneAsset>(BaseScenePath) == null)
            {
                throw new InvalidOperationException($"Stable base scene is missing at {BaseScenePath}.");
            }

            GameObject playerPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(PlayerPrefabPath);
            if (playerPrefab == null || playerPrefab.GetComponent<CapsuleCollider>() == null ||
                playerPrefab.GetComponent<Rigidbody>() == null || playerPrefab.GetComponent<QusapInputReader>() == null)
            {
                throw new InvalidOperationException($"Playable combat player prefab is incomplete at {PlayerPrefabPath}.");
            }

            CapsuleCollider capsule = playerPrefab.GetComponent<CapsuleCollider>();
            QusapHorizontalMotor horizontal = playerPrefab.GetComponent<QusapHorizontalMotor>();
            QusapVerticalMotor vertical = playerPrefab.GetComponent<QusapVerticalMotor>();
            QusapDashMotor dash = playerPrefab.GetComponent<QusapDashMotor>();
            SerializedObject playerSerialized = new(horizontal);
            SerializedObject verticalSerialized = new(vertical);
            SerializedObject dashSerialized = new(dash);
            float moveSpeed = playerSerialized.FindProperty("maxSpeed").floatValue;
            float jumpHeight = verticalSerialized.FindProperty("jumpHeight").floatValue;
            float wallJumpSpeed = verticalSerialized.FindProperty("wallJumpVerticalSpeed").floatValue;
            float dashDistance = dashSerialized.FindProperty("dashDistance").floatValue;
            if (!Mathf.Approximately(capsule.radius, 0.5f) || !Mathf.Approximately(capsule.height, 2f) ||
                !Mathf.Approximately(moveSpeed, 8f) || !Mathf.Approximately(jumpHeight, 2.5f) ||
                !Mathf.Approximately(wallJumpSpeed, 9f) || !Mathf.Approximately(dashDistance, 2f))
            {
                throw new InvalidOperationException("The combat player movement scale changed; remeasure the relic arena before building it.");
            }

            ValidateOneWayPlatform(AssetDatabase.LoadAssetAtPath<GameObject>(OneWayPrefabPath), false, null);
            if (StandardRise / jumpHeight < 0.65f || StandardRise / jumpHeight > 0.7f ||
                ShortcutRise / jumpHeight < 0.75f || ShortcutRise / jumpHeight > 0.8f)
            {
                throw new InvalidOperationException("Arena rise ratios no longer match the measured jump-height targets.");
            }
        }

        private static void ValidateGeneratedContent(Scene scene, GameObject arenaRoot)
        {
            string[] requiredPaths =
            {
                "Geometry/OuterBounds", "Geometry/SolidGeometry", "Geometry/OneWayPlatforms",
                "Geometry/WallJumpSections", "PortalArrivals/PortalArrival_01",
                "PortalArrivals/PortalArrival_02", "PortalArrivals/PortalArrival_03",
                "PortalArrivals/PortalArrival_04", "Routes/LeftRoute", "Routes/CenterRoute",
                "Routes/RightRoute", "RelicArea/RelicPedestalSocket",
                "RelicArea/RelicProtectionSocket", "FutureHazards", "FallRecovery",
                "FallRecovery/FallResetZone_Reserved", "Debug"
            };
            foreach (string requiredPath in requiredPaths)
            {
                if (arenaRoot.transform.Find(requiredPath) == null)
                {
                    throw new InvalidOperationException($"Generated relic arena is missing {requiredPath}.");
                }
            }

            ValidateSolidGeometry(arenaRoot);

            QusapOneWayPlatform[] oneWays = arenaRoot.GetComponentsInChildren<QusapOneWayPlatform>(true);
            if (oneWays.Length != ExpectedOneWayCount)
            {
                throw new InvalidOperationException($"Expected {ExpectedOneWayCount} one-way platforms; found {oneWays.Length}.");
            }
            Material oneWayMaterial = AssetDatabase.LoadAssetAtPath<Material>($"{MaterialFolder}/RelicArena_OneWay.mat");
            foreach (QusapOneWayPlatform oneWay in oneWays)
            {
                ValidateOneWayPlatform(PrefabUtility.GetNearestPrefabInstanceRoot(oneWay.gameObject), true, oneWayMaterial);
            }

            if (arenaRoot.GetComponentInChildren<Rigidbody>(true) != null ||
                arenaRoot.GetComponentInChildren<QusapInputReader>(true) != null ||
                arenaRoot.GetComponentInChildren<Camera>(true) != null)
            {
                throw new InvalidOperationException("The reusable relic arena prefab must contain only static geometry and markers.");
            }

            ValidateNoNegativeScaleOrOffPlane(arenaRoot.transform);
            ValidateVisualOnlyGroup(arenaRoot.transform.Find("PortalArrivals"));
            ValidateVisualOnlyGroup(arenaRoot.transform.Find("RelicArea"));
            ValidateVisualOnlyGroup(arenaRoot.transform.Find("FutureHazards"));
            ValidateVisualOnlyGroup(arenaRoot.transform.Find("FallRecovery"));
            ValidateVisualOnlyGroup(arenaRoot.transform.Find("Debug"));

            Transform[] allTransforms = arenaRoot.GetComponentsInChildren<Transform>(true);
            List<Transform> playerSlots = new();
            foreach (Transform candidate in allTransforms)
            {
                if (candidate.name.StartsWith("PlayerSlot_", StringComparison.Ordinal))
                {
                    playerSlots.Add(candidate);
                }
            }
            if (playerSlots.Count != ExpectedPlayerSlotCount)
            {
                throw new InvalidOperationException($"Expected exactly {ExpectedPlayerSlotCount} PlayerSlot markers; found {playerSlots.Count}.");
            }
            foreach (Transform slot in playerSlots)
            {
                if (!Mathf.Approximately(slot.position.y, 1.05f) || !Mathf.Approximately(slot.position.z, 0f))
                {
                    throw new InvalidOperationException($"Player slot {slot.name} is not on the shared safe arrival height and Z plane.");
                }
            }

            float[] arrivalX = { -15f, -5f, 5f, 15f };
            for (int index = 0; index < arrivalX.Length; index++)
            {
                Transform arrival = arenaRoot.transform.Find($"PortalArrivals/PortalArrival_{index + 1:00}");
                if (arrival == null || !Mathf.Approximately(arrival.position.x, arrivalX[index]) ||
                    !Mathf.Approximately(arrival.position.y, 1.05f))
                {
                    throw new InvalidOperationException("Portal arrival positions drifted from the measured symmetric layout.");
                }
            }

            Transform pedestal = arenaRoot.transform.Find("RelicArea/RelicPedestalSocket");
            if (!Mathf.Approximately(pedestal.position.x, 0f) || !Mathf.Approximately(pedestal.position.y, 25.05f))
            {
                throw new InvalidOperationException("RelicPedestalSocket drifted from the upper center combat platform.");
            }

            ValidateThreeWayRelicAccess(arenaRoot);
            ValidateSceneDebugPlayerAndCamera(scene);
            ValidateNoMissingScripts(scene);
        }

        private static void ValidateSolidGeometry(GameObject arenaRoot)
        {
            string[] groupPaths = { "Geometry/OuterBounds", "Geometry/SolidGeometry", "Geometry/WallJumpSections" };
            int colliderCount = 0;
            foreach (string groupPath in groupPaths)
            {
                Transform group = arenaRoot.transform.Find(groupPath);
                foreach (BoxCollider collider in group.GetComponentsInChildren<BoxCollider>(true))
                {
                    colliderCount++;
                    MeshRenderer renderer = collider.GetComponent<MeshRenderer>();
                    Vector3 scale = collider.transform.lossyScale;
                    if (!collider.enabled || collider.isTrigger || collider.GetComponent<Rigidbody>() != null ||
                        renderer == null || scale.x <= 0f || scale.y <= 0f || scale.z <= 0f ||
                        !Approximately(collider.bounds.size, renderer.bounds.size))
                    {
                        throw new InvalidOperationException($"Solid geometry {collider.name} has an invalid collider, scale, or visual fit.");
                    }
                }
            }
            if (colliderCount != ExpectedSolidColliderCount)
            {
                throw new InvalidOperationException($"Expected {ExpectedSolidColliderCount} solid colliders; found {colliderCount}.");
            }
        }

        private static void ValidateThreeWayRelicAccess(GameObject arenaRoot)
        {
            Transform platforms = arenaRoot.transform.Find("Geometry/OneWayPlatforms");
            string[] approachNames =
            {
                "Left_11_RelicApproach", "Center_11_RelicApproach", "Right_11_RelicApproach"
            };
            foreach (string approachName in approachNames)
            {
                Transform approach = platforms.Find(approachName);
                if (approach == null || !Mathf.Approximately(24.5f - approach.localPosition.y, StandardRise))
                {
                    throw new InvalidOperationException($"Relic approach {approachName} is missing or outside the safe mandatory jump rise.");
                }
            }
            if (platforms.Find("Shared_MiddleReconnect") == null || platforms.Find("Shared_UpperReconnect") == null)
            {
                throw new InvalidOperationException("The three routes must cross and reconnect at both middle and upper tiers.");
            }
        }

        private static void ValidateSceneDebugPlayerAndCamera(Scene scene)
        {
            QusapInputReader activePlayer = null;
            int scenePlayerCount = 0;
            foreach (QusapInputReader player in UnityEngine.Object.FindObjectsByType<QusapInputReader>(FindObjectsInactive.Include))
            {
                if (player.gameObject.scene != scene)
                {
                    continue;
                }
                scenePlayerCount++;
                activePlayer = player;
            }
            if (scenePlayerCount != 1 || activePlayer == null || !activePlayer.gameObject.activeInHierarchy ||
                activePlayer.LocalPlayerSlot != QusapLocalPlayerSlot.Player1Keyboard)
            {
                throw new InvalidOperationException($"Generated scene must contain exactly one active keyboard test player; found {scenePlayerCount}.");
            }

            CapsuleCollider capsule = activePlayer.GetComponent<CapsuleCollider>();
            Rigidbody body = activePlayer.GetComponent<Rigidbody>();
            if (capsule == null || body == null || !Mathf.Approximately(capsule.radius, 0.5f) ||
                !Mathf.Approximately(capsule.height, 2f) || (body.constraints & RigidbodyConstraints.FreezePositionZ) == 0)
            {
                throw new InvalidOperationException("The route-test player no longer preserves the measured capsule or Z constraint.");
            }

            GameObject cameraObject = RequireGameObject(scene, "Main Camera");
            QusapCameraFollow follow = cameraObject.GetComponent<QusapCameraFollow>();
            if (follow == null || cameraObject.GetComponent<QusapSharedCombatCamera>() != null)
            {
                throw new InvalidOperationException("Main Camera must use only the existing single-target follow component.");
            }
            SerializedObject serializedFollow = new(follow);
            if (serializedFollow.FindProperty("target").objectReferenceValue != activePlayer.transform)
            {
                throw new InvalidOperationException("Main Camera must target the only active test player.");
            }
        }

        private static void ValidateNoMissingScripts(Scene scene)
        {
            int missingScriptCount = 0;
            foreach (GameObject sceneRoot in scene.GetRootGameObjects())
            {
                foreach (Transform candidate in sceneRoot.GetComponentsInChildren<Transform>(true))
                {
                    missingScriptCount += GameObjectUtility.GetMonoBehavioursWithMissingScriptCount(candidate.gameObject);
                }
            }
            if (missingScriptCount != 0)
            {
                throw new InvalidOperationException($"Generated scene contains {missingScriptCount} missing script references.");
            }
        }

        private static void ValidateNoNegativeScaleOrOffPlane(Transform root)
        {
            foreach (Transform candidate in root.GetComponentsInChildren<Transform>(true))
            {
                Vector3 scale = candidate.lossyScale;
                if (scale.x < 0f || scale.y < 0f || scale.z < 0f || !Mathf.Approximately(candidate.position.z, 0f))
                {
                    throw new InvalidOperationException($"{candidate.name} has a negative scale or is outside gameplay Z = 0.");
                }
            }
        }

        private static void ValidateVisualOnlyGroup(Transform group)
        {
            if (group == null || group.GetComponentsInChildren<Collider>(true).Length != 0 ||
                group.GetComponentsInChildren<Rigidbody>(true).Length != 0 ||
                group.GetComponentsInChildren<MonoBehaviour>(true).Length != 0)
            {
                throw new InvalidOperationException($"{group?.name ?? "Marker group"} must remain visual-only.");
            }
        }

        private static void ValidateOneWayPlatform(GameObject root, bool requirePrefabInstance, Material expectedMaterial)
        {
            QusapOneWayPlatform oneWay = root != null ? root.GetComponentInChildren<QusapOneWayPlatform>(true) : null;
            BoxCollider solid = root != null ? root.GetComponent<BoxCollider>() : null;
            BoxCollider trigger = oneWay != null ? oneWay.GetComponent<BoxCollider>() : null;
            MeshRenderer renderer = root != null ? root.GetComponent<MeshRenderer>() : null;
            string nearestAssetPath = root != null ? PrefabUtility.GetPrefabAssetPathOfNearestInstanceRoot(root) : string.Empty;
            bool materialIsValid = expectedMaterial == null || (renderer != null && renderer.sharedMaterial == expectedMaterial);
            bool valid = root != null && oneWay != null && oneWay.enabled && solid != null && solid.enabled &&
                !solid.isTrigger && trigger != null && trigger.enabled && trigger.isTrigger &&
                oneWay.SolidCollider == solid && root.transform.localScale.x > 0f &&
                root.transform.localScale.y > 0f && root.transform.localScale.z > 0f &&
                root.transform.localRotation == Quaternion.identity && materialIsValid &&
                (!requirePrefabInstance || nearestAssetPath == OneWayPrefabPath);
            if (!valid)
            {
                throw new InvalidOperationException("A one-way surface is incomplete, visually incorrect, or no longer linked to QusapOneWayPlatform.prefab.");
            }
        }

        private static void CreateBlocks(IEnumerable<Block> blocks, Transform parent, Material material)
        {
            foreach (Block block in blocks)
            {
                GameObject cube = GameObject.CreatePrimitive(PrimitiveType.Cube);
                cube.name = block.Name;
                cube.transform.SetParent(parent, false);
                cube.transform.localPosition = block.Position;
                cube.transform.localRotation = Quaternion.identity;
                cube.transform.localScale = block.Scale;
                cube.GetComponent<MeshRenderer>().sharedMaterial = material;
                BoxCollider collider = cube.GetComponent<BoxCollider>();
                collider.enabled = true;
                collider.isTrigger = false;
                collider.center = Vector3.zero;
                collider.size = Vector3.one;
            }
        }

        private static void CreateOneWayPlatform(GameObject prefab, Platform platform, Transform parent, Material material)
        {
            GameObject instance = (GameObject)PrefabUtility.InstantiatePrefab(prefab, parent);
            instance.name = platform.Name;
            instance.transform.localPosition = platform.Position;
            instance.transform.localRotation = Quaternion.identity;
            Vector3 sourceScale = prefab.transform.localScale;
            instance.transform.localScale = new Vector3(platform.Width, sourceScale.y, sourceScale.z);
            MeshRenderer renderer = instance.GetComponent<MeshRenderer>();
            renderer.sharedMaterial = material;
            PrefabUtility.RecordPrefabInstancePropertyModifications(instance.transform);
            PrefabUtility.RecordPrefabInstancePropertyModifications(renderer);
            ValidateOneWayPlatform(instance, true, material);
        }

        private static Transform CreateMarker(
            string name,
            Transform parent,
            Vector3 position,
            Material material,
            PrimitiveType primitiveType,
            Vector3 scale)
        {
            Transform marker = CreateGroup(name, parent);
            marker.localPosition = position;
            GameObject visual = GameObject.CreatePrimitive(primitiveType);
            visual.name = "EditorMarker_NoGameplay";
            visual.transform.SetParent(marker, false);
            visual.transform.localPosition = Vector3.zero;
            visual.transform.localRotation = Quaternion.identity;
            visual.transform.localScale = scale;
            Collider collider = visual.GetComponent<Collider>();
            if (collider != null)
            {
                UnityEngine.Object.DestroyImmediate(collider);
            }
            visual.GetComponent<MeshRenderer>().sharedMaterial = material;
            return marker;
        }

        private static void CreateVisualCube(string name, Transform parent, Vector3 localPosition, Vector3 scale, Material material)
        {
            GameObject visual = GameObject.CreatePrimitive(PrimitiveType.Cube);
            visual.name = name;
            visual.transform.SetParent(parent, false);
            visual.transform.localPosition = localPosition;
            visual.transform.localRotation = Quaternion.identity;
            visual.transform.localScale = scale;
            UnityEngine.Object.DestroyImmediate(visual.GetComponent<BoxCollider>());
            visual.GetComponent<MeshRenderer>().sharedMaterial = material;
        }

        private static Transform CreateGroup(string name, Transform parent)
        {
            GameObject group = new(name);
            group.transform.SetParent(parent, false);
            return group.transform;
        }

        private static Material CreateOrUpdateMaterial(string fileName, Color color)
        {
            string path = $"{MaterialFolder}/{fileName}";
            Material material = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (material == null)
            {
                Shader shader = Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard");
                if (shader == null)
                {
                    throw new InvalidOperationException("No compatible Lit shader is available for relic arena materials.");
                }
                material = new Material(shader);
                AssetDatabase.CreateAsset(material, path);
            }
            material.color = color;
            if (material.HasProperty("_BaseColor"))
            {
                material.SetColor("_BaseColor", color);
            }
            EditorUtility.SetDirty(material);
            return material;
        }

        private static void EnsureGeneratedFolders()
        {
            EnsureFolder("Assets/_Qusap/Prefabs", "Levels");
            EnsureFolder("Assets/_Qusap/Materials", "RelicCombatArenaBlockout");
        }

        private static void EnsureFolder(string parent, string child)
        {
            if (!AssetDatabase.IsValidFolder($"{parent}/{child}"))
            {
                AssetDatabase.CreateFolder(parent, child);
            }
        }

        private static bool GeneratedTargetsExist()
        {
            return AssetDatabase.LoadMainAssetAtPath(TargetScenePath) != null ||
                AssetDatabase.LoadMainAssetAtPath(TargetPrefabPath) != null;
        }

        private static void RemoveLegacyGeometry(Scene scene)
        {
            Transform geometry = RequireGameObject(scene, "CombatArena").transform.Find("Geometry");
            if (geometry != null)
            {
                UnityEngine.Object.DestroyImmediate(geometry.gameObject);
            }
        }

        private static void CloseLoadedTargetScene()
        {
            Scene loaded = SceneManager.GetSceneByPath(TargetScenePath);
            if (loaded.IsValid() && loaded.isLoaded)
            {
                EditorSceneManager.CloseScene(loaded, true);
            }
        }

        private static void DeleteGeneratedAssetIfPresent(string path)
        {
            if (AssetDatabase.LoadMainAssetAtPath(path) != null && !AssetDatabase.DeleteAsset(path))
            {
                throw new InvalidOperationException($"Could not replace generated asset at {path}.");
            }
        }

        private static bool Approximately(Vector3 first, Vector3 second)
        {
            return Mathf.Approximately(first.x, second.x) &&
                Mathf.Approximately(first.y, second.y) &&
                Mathf.Approximately(first.z, second.z);
        }

        private static GameObject RequireGameObject(Scene scene, string objectName)
        {
            foreach (GameObject sceneRoot in scene.GetRootGameObjects())
            {
                foreach (Transform candidate in sceneRoot.GetComponentsInChildren<Transform>(true))
                {
                    if (candidate.name == objectName)
                    {
                        return candidate.gameObject;
                    }
                }
            }
            throw new InvalidOperationException($"Required object {objectName} is missing from copied scene {scene.path}.");
        }
    }
}
#endif
