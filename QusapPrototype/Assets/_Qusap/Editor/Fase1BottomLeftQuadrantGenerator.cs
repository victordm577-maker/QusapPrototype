#if UNITY_EDITOR
using System;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Qusap.EditorTools
{
    public static class Fase1BottomLeftQuadrantGenerator
    {
        private const string BaseScenePath = "Assets/_Qusap/Scenes/CombatPlayground.unity";
        private const string TargetScenePath = "Assets/_Qusap/Scenes/Fase1_Quadrant_BottomLeft_Blockout.unity";
        private const string TargetPrefabPath = "Assets/_Qusap/Prefabs/Levels/Fase1_Quadrant_BottomLeft_Blockout.prefab";
        private const string PlayerPrefabPath = "Assets/_Qusap/Prefabs/QusapCombatPlayer.prefab";
        private const string OneWayPrefabPath = "Assets/_Qusap/Prefabs/QusapOneWayPlatform.prefab";
        private const string GeometryMaterialPath = "Assets/_Qusap/Materials/CombatArenaGeometry.mat";
        private const string MarkerMaterialFolder = "Assets/_Qusap/Materials/BlockoutMarkers";
        private const int ExpectedOneWayCount = 42;
        private const int ExpectedSolidColliderCount = 45;

        private static readonly Vector3 PlayerStart = new(4f, 1.05f, 0f);
        private static readonly Vector3 ReservedPlayerStart = new(6.25f, 1.05f, 0f);

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
            public Platform(string name, float x, float y, float width = 3f)
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
            new("OuterFloor_64x1", 32f, -0.5f, 64f, 1f),
            new("OuterWall_Left", -0.5f, 21f, 1f, 42f),
            new("OuterCeiling_Main", 27f, 42.5f, 54f, 1f),
            new("OuterCeiling_ExitCap", 61.5f, 42.5f, 5f, 1f),
            new("OuterWall_Right_Lower", 64.5f, 16f, 1f, 32f),
            new("OuterWall_Right_Upper", 64.5f, 40f, 1f, 4f)
        };

        private static readonly Block[] SolidBlocks =
        {
            new("StartRoom_Roof", 5.5f, 5f, 11f, 0.5f),
            new("StartRoom_RightBaffle", 11f, 4f, 0.5f, 2f),
            new("StartRoom_ExitFloor", 11.75f, 0.25f, 3.5f, 0.5f),
            new("LowerRouteGate_Start", 16.5f, 1.625f, 1f, 3.25f),
            new("LowerCombatChamber_Floor", 23f, 4.75f, 7f, 0.5f),
            new("LowerCombatChamber_RoofLeft", 22f, 9.55f, 8f, 0.5f),
            new("MiddleCombatRoom_Floor", 42f, 12.75f, 12f, 0.5f),
            new("MiddleCombatRoom_LeftShelf", 33f, 16f, 7f, 0.5f),
            new("MiddleCombatRoom_RightShelf", 47f, 19.25f, 8f, 0.5f),
            new("UpperCombatRoom_Floor", 37f, 25.65f, 11f, 0.5f),
            new("UpperCombatRoom_RoofLeft", 35f, 30.45f, 9f, 0.5f),
            new("LegendaryChestRoom_LeftBaffle", 46f, 6f, 0.5f, 5f),
            new("LegendaryChestRoom_RoofLeft", 49.5f, 8.5f, 7f, 0.5f),
            new("LegendaryChestRoom_RoofRight", 60.5f, 8.5f, 7f, 0.5f),
            new("LegendaryChestRoom_RewardPlinth", 59f, 0.35f, 5f, 0.7f),
            new("LegendaryChestRoom_UpperReconnect", 57.5f, 11.7f, 7f, 0.5f),
            new("LegendaryChestAccessGate", 43.75f, 1.625f, 1f, 3.25f),
            new("LegendaryGemRoom_FloorLeft", 2.5f, 30.4f, 5f, 0.5f),
            new("LegendaryGemRoom_FloorRight", 12f, 30.4f, 6f, 0.5f),
            new("LegendaryGemRoom_RightLower", 15f, 30.9f, 0.5f, 1f),
            new("LegendaryGemRoom_RightUpper", 15f, 38.5f, 0.5f, 7f),
            new("LegendaryGemRoom_RewardPlinth", 5f, 30.85f, 4f, 0.9f),
            new("LegendaryGemRoom_ReturnBridge", 19f, 31.9f, 8f, 0.5f),
            new("LegendaryGemRoom_ReturnShelf", 26f, 31.8f, 6f, 0.5f),
            new("UpperRoute_LongShelf", 47f, 29f, 8f, 0.5f),
            new("CenterExit_Approach", 56.5f, 32.2f, 9f, 0.5f),
            new("CenterExit_Threshold", 62f, 33.8f, 4f, 0.5f),
            new("CorridorBaffle_Lower", 27f, 14f, 0.5f, 4f),
            new("CorridorBaffle_MiddleLeft", 18f, 18f, 0.5f, 6f),
            new("CorridorBaffle_MiddleRight", 57f, 19f, 0.5f, 5f),
            new("CorridorBaffle_Upper", 42f, 33.5f, 0.5f, 6f)
        };

        private static readonly Block[] WallJumpBlocks =
        {
            new("WallJump_LowerFork_LeftWall", 25.5f, 13.5f, 0.5f, 5.5f),
            new("WallJump_LowerFork_RightWall", 28.5f, 13.5f, 0.5f, 5.5f),
            new("WallJump_LegendaryChest_LeftWall", 53f, 6f, 0.5f, 5f),
            new("WallJump_LegendaryChest_RightWall", 57f, 6f, 0.5f, 5f),
            new("WallJump_LeftAscent_InnerWallA", 3f, 23.5f, 0.5f, 7f),
            new("WallJump_LeftAscent_InnerWallB", 8f, 27f, 0.5f, 5f),
            new("WallJump_UpperFork_LeftWall", 30f, 24f, 0.5f, 5f),
            new("WallJump_UpperFork_RightWall", 34f, 24f, 0.5f, 5f)
        };

        private static readonly Platform[] OneWayPlatforms =
        {
            new("Main_01_StartExit", 14f, 1.5f, 3.5f),
            new("Main_02_LowerRise", 18f, 3.1f),
            new("Main_03_ChamberExit", 28f, 6.3f),
            new("Main_04_LowerFork", 33f, 7.9f, 3.5f),
            new("Main_05_CrossBack", 29f, 9.5f),
            new("Main_06_MiddleEntry", 35f, 11.1f, 3.5f),
            new("Main_07_MiddleRoom", 40f, 14.4f, 4f),
            new("Main_08_MiddleCross", 45f, 16f),
            new("Main_09_MiddleReturn", 40f, 17.6f),
            new("Main_10_UpperRoomEntry", 46f, 20.9f, 3.5f),
            new("Main_11_RightRise", 53f, 22.5f),
            new("Main_12_UpperCross", 48f, 24.1f),
            new("Main_13_UpperRoom", 44.5f, 25.7f, 4f),
            new("Main_14_TowardExit", 48f, 27.3f, 3.5f),
            new("Main_15_ExitRise", 54f, 28.9f),
            new("Main_16_ExitCross", 50f, 30.5f),
            new("Main_17_ExitDoorStep", 62f, 35.4f, 3.5f),
            new("ChestRoute_01_DropCatch", 30f, 3.1f),
            new("ChestRoute_02_LowerRun", 35f, 1.5f, 3.5f),
            new("ChestRoute_03_RoomApproach", 41f, 1.5f, 4f),
            new("ChestRoute_04_InsideLow", 49f, 1.8f),
            new("ChestRoute_05_InsideMid", 51f, 3.4f),
            new("ChestRoute_06_InsideHigh", 55f, 5f),
            new("ChestRoute_07_RoofGap", 55f, 6.6f),
            new("ChestRoute_08_Reconnect", 53f, 10.1f, 3.5f),
            new("GemRoute_01_West", 24f, 11.1f),
            new("GemRoute_02_West", 19f, 12.7f),
            new("GemRoute_03_West", 14f, 14.3f),
            new("GemRoute_04_Zig", 9f, 15.9f),
            new("GemRoute_05_Zag", 13f, 17.5f),
            new("GemRoute_06_Zig", 8f, 19.1f),
            new("GemRoute_07_Zag", 12f, 20.7f),
            new("GemRoute_08_WallEntry", 6f, 22.4f),
            new("GemRoute_09_WallRest", 10f, 23.9f),
            new("GemRoute_10_UpperZig", 5f, 25.6f),
            new("GemRoute_11_UpperZag", 10f, 27.1f),
            new("GemRoute_12_RoomEntry", 7f, 28.8f),
            new("GemReturn_01_Door", 17f, 31.9f),
            new("GemReturn_02_Descent", 23f, 30.3f),
            new("GemReturn_03_Rejoin", 31f, 28.7f, 3.5f),
            new("Shortcut_Dash_Lower", 23f, 7.9f),
            new("ChestRoute_09_MainRejoin", 51f, 13.3f)
        };

        [MenuItem("Tools/Qusap/Build Fase 1 Bottom Left Quadrant")]
        private static void BuildFromMenu()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode)
            {
                EditorUtility.DisplayDialog("Fase 1 Bottom Left Quadrant", "El cuadrante no puede generarse mientras Unity esta en Play Mode.", "Aceptar");
                return;
            }

            if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo())
            {
                return;
            }

            bool targetExists = GeneratedTargetsExist();
            if (targetExists && !EditorUtility.DisplayDialog(
                    "Regenerate Fase 1 Bottom Left Quadrant",
                    "Se reemplazaran solamente la escena y el prefab del cuadrante inferior izquierdo. Los blockouts anteriores no se modificaran.",
                    "Regenerar",
                    "Cancelar"))
            {
                return;
            }

            BuildSceneAndPrefab(targetExists);
            EditorUtility.DisplayDialog("Fase 1 Bottom Left Quadrant", "El cuadrante fue generado, validado y guardado. La escena quedo abierta para inspeccion y Play Mode.", "Aceptar");
        }

        private static void BuildSceneAndPrefab(bool replaceConfirmed)
        {
            ValidateRequiredAssets();
            if (GeneratedTargetsExist() && !replaceConfirmed)
            {
                throw new InvalidOperationException("Generated targets already exist and replacement was not confirmed.");
            }

            EnsureGeneratedFolders();
            Material geometryMaterial = AssetDatabase.LoadAssetAtPath<Material>(GeometryMaterialPath);
            GameObject oneWayPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(OneWayPrefabPath);
            Material commonMaterial = CreateOrUpdateMaterial("CommonMarker.mat", new Color(0.25f, 0.95f, 0.35f));
            Material rareMaterial = CreateOrUpdateMaterial("RareMarker.mat", new Color(0.15f, 0.7f, 1f));
            Material legendaryMaterial = CreateOrUpdateMaterial("LegendaryMarker.mat", new Color(0.9f, 0.25f, 1f));
            Material hazardMaterial = CreateOrUpdateMaterial("HazardMarker.mat", new Color(1f, 0.35f, 0.08f));
            Material connectionMaterial = CreateOrUpdateMaterial("ConnectionMarker.mat", new Color(0.1f, 0.95f, 1f));

            CloseLoadedTargetScene();
            if (replaceConfirmed)
            {
                DeleteGeneratedAssetIfPresent(TargetScenePath);
                DeleteGeneratedAssetIfPresent(TargetPrefabPath);
            }

            if (!AssetDatabase.CopyAsset(BaseScenePath, TargetScenePath))
            {
                throw new InvalidOperationException($"Could not copy the stable combat scene from {BaseScenePath}.");
            }

            AssetDatabase.ImportAsset(TargetScenePath, ImportAssetOptions.ForceSynchronousImport);
            Scene targetScene = EditorSceneManager.OpenScene(TargetScenePath, OpenSceneMode.Single);
            SceneManager.SetActiveScene(targetScene);
            RemoveLegacyGeometry(targetScene);

            GameObject editableRoot = CreateQuadrantHierarchy(
                geometryMaterial, oneWayPrefab, commonMaterial, rareMaterial,
                legendaryMaterial, hazardMaterial, connectionMaterial);
            GameObject savedPrefab = PrefabUtility.SaveAsPrefabAsset(editableRoot, TargetPrefabPath);
            if (savedPrefab == null)
            {
                throw new InvalidOperationException($"Could not save reusable quadrant prefab at {TargetPrefabPath}.");
            }

            UnityEngine.Object.DestroyImmediate(editableRoot);
            GameObject quadrantInstance = (GameObject)PrefabUtility.InstantiatePrefab(savedPrefab, targetScene);
            quadrantInstance.name = "Fase1_Quadrant_BottomLeft";
            quadrantInstance.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);

            ConfigureSinglePlayerScene(targetScene);
            ValidateGeneratedContent(targetScene, quadrantInstance);
            EditorSceneManager.MarkSceneDirty(targetScene);
            if (!EditorSceneManager.SaveScene(targetScene, TargetScenePath))
            {
                throw new InvalidOperationException($"Could not save generated scene at {TargetScenePath}.");
            }

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            Selection.activeGameObject = quadrantInstance;
        }

        private static GameObject CreateQuadrantHierarchy(
            Material geometryMaterial, GameObject oneWayPrefab, Material commonMaterial,
            Material rareMaterial, Material legendaryMaterial, Material hazardMaterial,
            Material connectionMaterial)
        {
            GameObject root = new("Fase1_Quadrant_BottomLeft");
            Transform geometry = CreateGroup("Geometry", root.transform);
            Transform outerBounds = CreateGroup("OuterBounds", geometry);
            Transform solidGeometry = CreateGroup("SolidGeometry", geometry);
            Transform oneWayPlatforms = CreateGroup("OneWayPlatforms", geometry);
            Transform wallJumpSections = CreateGroup("WallJumpSections", geometry);
            Transform connections = CreateGroup("Connections", root.transform);
            Transform startEntrance = CreateGroup("StartEntrance_BottomLeft", connections);
            Transform centerEntrance = CreateGroup("CenterEntrance_TopRight", connections);
            Transform futureLoot = CreateGroup("FutureLoot", root.transform);
            Transform common = CreateGroup("Common", futureLoot);
            Transform rare = CreateGroup("Rare", futureLoot);
            Transform legendary = CreateGroup("Legendary", futureLoot);
            Transform futureHazards = CreateGroup("FutureHazards", root.transform);
            Transform respawnArea = CreateGroup("RespawnArea", root.transform);
            Transform debug = CreateGroup("Debug", root.transform);

            CreateBlocks(OuterBounds, outerBounds, geometryMaterial);
            CreateBlocks(SolidBlocks, solidGeometry, geometryMaterial);
            CreateBlocks(WallJumpBlocks, wallJumpSections, geometryMaterial);
            foreach (Platform platform in OneWayPlatforms)
            {
                CreateOneWayPlatform(oneWayPrefab, platform, oneWayPlatforms);
            }

            CreateMarker("Entrance_BottomLeft", startEntrance, new Vector3(1.5f, 1.5f, 0f), connectionMaterial, PrimitiveType.Cube, new Vector3(0.35f, 3f, 0.35f));
            CreateMarker("CenterObjectiveEntrance_TopRight", centerEntrance, new Vector3(63.5f, 36f, 0f), connectionMaterial, PrimitiveType.Cube, new Vector3(0.35f, 4f, 0.35f));
            CreateLootMarkers(common, rare, legendary, commonMaterial, rareMaterial, legendaryMaterial);
            CreateHazardMarkers(futureHazards, hazardMaterial);
            CreateMarker("PlayerStart_BottomLeft", respawnArea, PlayerStart, connectionMaterial, PrimitiveType.Cylinder, new Vector3(0.45f, 0.06f, 0.45f));
            Transform reservedStart = CreateMarker("PlayerStart_BottomLeft_Reserved", respawnArea, ReservedPlayerStart, connectionMaterial, PrimitiveType.Cylinder, new Vector3(0.45f, 0.06f, 0.45f));
            reservedStart.gameObject.SetActive(false);
            CreateMarker("QuadrantExtent_BottomLeft_0_0", debug, Vector3.zero, connectionMaterial, PrimitiveType.Cube, Vector3.one * 0.2f);
            CreateMarker("QuadrantExtent_TopRight_64_42", debug, new Vector3(64f, 42f, 0f), connectionMaterial, PrimitiveType.Cube, Vector3.one * 0.2f);
            CreateGroup("CopyNote_NoNegativeParentScale", debug);
            CreateGroup("CopyNote_TopQuadrantsNeedGravityAwareRouteAdjustments", debug);
            return root;
        }

        private static void CreateLootMarkers(Transform common, Transform rare, Transform legendary, Material commonMaterial, Material rareMaterial, Material legendaryMaterial)
        {
            CreateMarker("CommonGemSocket_01_LowerChamber", common, new Vector3(23f, 5.65f, 0f), commonMaterial, PrimitiveType.Sphere, Vector3.one * 0.45f);
            CreateMarker("CommonChestSocket_01_WestRoute", common, new Vector3(14f, 15.15f, 0f), commonMaterial, PrimitiveType.Cube, new Vector3(0.8f, 0.55f, 0.55f));
            CreateMarker("CommonGemSocket_02_MiddleRoom", common, new Vector3(42f, 13.65f, 0f), commonMaterial, PrimitiveType.Sphere, Vector3.one * 0.45f);
            CreateMarker("CommonChestSocket_02_UpperRoute", common, new Vector3(47f, 30f, 0f), commonMaterial, PrimitiveType.Cube, new Vector3(0.8f, 0.55f, 0.55f));
            CreateMarker("RareGemSocket_01_LeftAscent", rare, new Vector3(10f, 24.8f, 0f), rareMaterial, PrimitiveType.Sphere, Vector3.one * 0.6f);
            CreateMarker("RareChestSocket_01_MiddleFork", rare, new Vector3(33f, 16.9f, 0f), rareMaterial, PrimitiveType.Cube, new Vector3(1f, 0.7f, 0.7f));
            CreateMarker("RareGemSocket_02_UpperCombatRoom", rare, new Vector3(37f, 26.55f, 0f), rareMaterial, PrimitiveType.Sphere, Vector3.one * 0.6f);
            CreateMarker("RareChestSocket_02_RightReconnect", rare, new Vector3(57.5f, 12.6f, 0f), rareMaterial, PrimitiveType.Cube, new Vector3(1f, 0.7f, 0.7f));
            CreateMarker("LegendaryGemSocket_TopLeft", legendary, new Vector3(5f, 32.2f, 0f), legendaryMaterial, PrimitiveType.Sphere, Vector3.one * 0.9f);
            CreateMarker("LegendaryChestSocket_BottomRight", legendary, new Vector3(59f, 1.6f, 0f), legendaryMaterial, PrimitiveType.Cube, new Vector3(1.5f, 1f, 0.9f));
        }

        private static void CreateHazardMarkers(Transform parent, Material material)
        {
            CreateMarker("HazardSocket_Spikes_TopLeftApproach", parent, new Vector3(7f, 30.55f, 0f), material, PrimitiveType.Cylinder, new Vector3(0.35f, 0.8f, 0.35f));
            CreateMarker("HazardSocket_Energy_TopLeftReturn", parent, new Vector3(19f, 32.1f, 0f), material, PrimitiveType.Cylinder, new Vector3(0.35f, 0.8f, 0.35f));
            CreateMarker("HazardSocket_Spikes_BottomRightRoom", parent, new Vector3(54.5f, 0.1f, 0f), material, PrimitiveType.Cylinder, new Vector3(0.35f, 0.8f, 0.35f));
            CreateMarker("HazardSocket_Energy_BottomRightRoof", parent, new Vector3(55f, 8.65f, 0f), material, PrimitiveType.Cylinder, new Vector3(0.35f, 0.8f, 0.35f));
            CreateMarker("HazardSocket_Energy_DashShortcutLower", parent, new Vector3(26f, 7.9f, 0f), material, PrimitiveType.Cylinder, new Vector3(0.35f, 0.8f, 0.35f));
            CreateMarker("HazardSocket_Spikes_DashShortcutUpper", parent, new Vector3(42f, 22.5f, 0f), material, PrimitiveType.Cylinder, new Vector3(0.35f, 0.8f, 0.35f));
            CreateMarker("FallResetZone_Reserved_LowerMiddle", parent, new Vector3(38f, -1.25f, 0f), material, PrimitiveType.Cylinder, new Vector3(0.6f, 1.2f, 0.6f));
        }

        private static void ConfigureSinglePlayerScene(Scene scene)
        {
            QusapCombatArenaController arena = RequireGameObject(scene, "CombatArena").GetComponent<QusapCombatArenaController>();
            if (arena == null || arena.PlayerOne == null || arena.PlayerTwo == null)
            {
                throw new InvalidOperationException("The stable combat scene is missing its configured two-player arena controller.");
            }

            QusapInputReader player = arena.PlayerOne;
            player.name = "Player1_Qusap";
            player.SetLocalPlayerSlot(QusapLocalPlayerSlot.Player1Keyboard);
            player.transform.SetPositionAndRotation(PlayerStart, Quaternion.identity);
            UnityEngine.Object.DestroyImmediate(arena.PlayerTwo.gameObject);

            GameObject cameraObject = RequireGameObject(scene, "Main Camera");
            QusapSharedCombatCamera sharedCamera = cameraObject.GetComponent<QusapSharedCombatCamera>();
            if (sharedCamera == null)
            {
                throw new InvalidOperationException("The stable combat scene is missing its shared camera.");
            }

            UnityEngine.Object.DestroyImmediate(sharedCamera);
            QusapCameraFollow follow = cameraObject.AddComponent<QusapCameraFollow>();
            SerializedObject serializedFollow = new(follow);
            serializedFollow.FindProperty("target").objectReferenceValue = player.transform;
            serializedFollow.FindProperty("offset").vector3Value = new Vector3(0f, 1.5f, -20f);
            serializedFollow.FindProperty("horizontalSmoothTime").floatValue = 0.12f;
            serializedFollow.FindProperty("verticalSmoothTime").floatValue = 0.12f;
            serializedFollow.ApplyModifiedPropertiesWithoutUndo();
            cameraObject.transform.position = PlayerStart + new Vector3(0f, 1.5f, -20f);

            UnityEngine.Object.DestroyImmediate(arena);
        }

        private static void ValidateRequiredAssets()
        {
            if (AssetDatabase.LoadAssetAtPath<SceneAsset>(BaseScenePath) == null)
            {
                throw new InvalidOperationException($"Stable base scene is missing at {BaseScenePath}.");
            }

            GameObject playerPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(PlayerPrefabPath);
            if (playerPrefab == null || playerPrefab.GetComponent<CapsuleCollider>() == null || playerPrefab.GetComponent<Rigidbody>() == null || playerPrefab.GetComponent<QusapInputReader>() == null)
            {
                throw new InvalidOperationException($"Playable combat player prefab is incomplete at {PlayerPrefabPath}.");
            }

            ValidateOneWayPlatform(AssetDatabase.LoadAssetAtPath<GameObject>(OneWayPrefabPath), false);
            if (AssetDatabase.LoadAssetAtPath<Material>(GeometryMaterialPath) == null)
            {
                throw new InvalidOperationException($"Shared blockout material is missing at {GeometryMaterialPath}.");
            }
        }

        private static void ValidateGeneratedContent(Scene scene, GameObject quadrantRoot)
        {
            string[] requiredPaths =
            {
                "Geometry/OuterBounds", "Geometry/SolidGeometry", "Geometry/OneWayPlatforms",
                "Geometry/WallJumpSections", "Connections/StartEntrance_BottomLeft",
                "Connections/CenterEntrance_TopRight", "FutureLoot/Common", "FutureLoot/Rare",
                "FutureLoot/Legendary", "FutureHazards", "RespawnArea", "Debug"
            };
            foreach (string requiredPath in requiredPaths)
            {
                if (quadrantRoot.transform.Find(requiredPath) == null)
                {
                    throw new InvalidOperationException($"Generated quadrant is missing {requiredPath}.");
                }
            }

            QusapOneWayPlatform[] oneWays = quadrantRoot.GetComponentsInChildren<QusapOneWayPlatform>(true);
            if (oneWays.Length != ExpectedOneWayCount)
            {
                throw new InvalidOperationException($"Expected {ExpectedOneWayCount} one-way platforms; found {oneWays.Length}.");
            }
            foreach (QusapOneWayPlatform oneWay in oneWays)
            {
                ValidateOneWayPlatform(PrefabUtility.GetNearestPrefabInstanceRoot(oneWay.gameObject), true);
            }

            ValidateSolidGeometry(quadrantRoot);
            ValidateNavigationGeometry(quadrantRoot);

            if (quadrantRoot.GetComponentInChildren<Rigidbody>(true) != null)
            {
                throw new InvalidOperationException("Static quadrant geometry must not contain a Rigidbody.");
            }
            if (quadrantRoot.GetComponentInChildren<QusapInputReader>(true) != null || quadrantRoot.GetComponentInChildren<Camera>(true) != null)
            {
                throw new InvalidOperationException("The reusable quadrant prefab must not contain players or cameras.");
            }

            ValidateVisualOnlyGroup(quadrantRoot.transform.Find("FutureLoot"));
            ValidateVisualOnlyGroup(quadrantRoot.transform.Find("FutureHazards"));
            ValidateVisualOnlyGroup(quadrantRoot.transform.Find("Connections"));
            ValidateVisualOnlyGroup(quadrantRoot.transform.Find("RespawnArea"));

            int scenePlayerCount = 0;
            QusapInputReader activePlayer = null;
            foreach (QusapInputReader player in UnityEngine.Object.FindObjectsByType<QusapInputReader>(FindObjectsInactive.Include))
            {
                if (player.gameObject.scene != scene)
                {
                    continue;
                }

                scenePlayerCount++;
                activePlayer = player;
                CapsuleCollider capsule = player.GetComponent<CapsuleCollider>();
                Rigidbody body = player.GetComponent<Rigidbody>();
                if (!player.gameObject.activeInHierarchy || capsule == null || !Mathf.Approximately(capsule.radius, 0.5f) || !Mathf.Approximately(capsule.height, 2f) || body == null || (body.constraints & RigidbodyConstraints.FreezePositionZ) == 0)
                {
                    throw new InvalidOperationException("A player no longer preserves its 1x2 capsule or Z-axis constraint.");
                }
            }
            if (scenePlayerCount != 1 || activePlayer == null || activePlayer.LocalPlayerSlot != QusapLocalPlayerSlot.Player1Keyboard)
            {
                throw new InvalidOperationException($"Generated scene must contain exactly one keyboard Player1; found {scenePlayerCount} players.");
            }

            QusapCameraFollow follow = RequireGameObject(scene, "Main Camera").GetComponent<QusapCameraFollow>();
            if (follow == null || RequireGameObject(scene, "Main Camera").GetComponent<QusapSharedCombatCamera>() != null)
            {
                throw new InvalidOperationException("Main Camera must use the existing single-target follow component only.");
            }
            SerializedObject serializedFollow = new(follow);
            if (serializedFollow.FindProperty("target").objectReferenceValue != activePlayer.transform)
            {
                throw new InvalidOperationException("Main Camera must target the only active player.");
            }

            Transform respawnArea = quadrantRoot.transform.Find("RespawnArea");
            int activeStartCount = 0;
            foreach (Transform child in respawnArea)
            {
                if (child.gameObject.activeSelf && child.name == "PlayerStart_BottomLeft")
                {
                    activeStartCount++;
                }
            }
            Transform reservedStart = respawnArea.Find("PlayerStart_BottomLeft_Reserved");
            if (activeStartCount != 1 || respawnArea.childCount != 2 || reservedStart == null || reservedStart.gameObject.activeSelf)
            {
                throw new InvalidOperationException("The quadrant must contain one active PlayerStart_BottomLeft and one inactive reserved marker.");
            }

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

        private static void ValidateVisualOnlyGroup(Transform group)
        {
            if (group == null || group.GetComponentsInChildren<Collider>(true).Length != 0 || group.GetComponentsInChildren<Rigidbody>(true).Length != 0 || group.GetComponentsInChildren<MonoBehaviour>(true).Length != 0)
            {
                throw new InvalidOperationException($"{group?.name ?? "Marker group"} must remain visual-only and contain no gameplay components.");
            }
        }

        private static void ValidateSolidGeometry(GameObject quadrantRoot)
        {
            string[] groups = { "Geometry/OuterBounds", "Geometry/SolidGeometry", "Geometry/WallJumpSections" };
            int playerLayer = AssetDatabase.LoadAssetAtPath<GameObject>(PlayerPrefabPath).layer;
            int colliderCount = 0;
            foreach (string groupPath in groups)
            {
                Transform group = quadrantRoot.transform.Find(groupPath);
                foreach (BoxCollider collider in group.GetComponentsInChildren<BoxCollider>(true))
                {
                    colliderCount++;
                    Vector3 scale = collider.transform.lossyScale;
                    MeshRenderer renderer = collider.GetComponent<MeshRenderer>();
                    if (!collider.enabled || collider.isTrigger || collider.GetComponent<Rigidbody>() != null ||
                        scale.x <= 0f || scale.y <= 0f || scale.z <= 0f || renderer == null ||
                        Physics.GetIgnoreLayerCollision(collider.gameObject.layer, playerLayer) ||
                        !Approximately(collider.bounds.size, renderer.bounds.size))
                    {
                        throw new InvalidOperationException($"Solid geometry '{collider.name}' has an invalid collider, scale, layer, or visual fit.");
                    }
                }
            }

            if (colliderCount != ExpectedSolidColliderCount)
            {
                throw new InvalidOperationException($"Expected {ExpectedSolidColliderCount} solid colliders; found {colliderCount}.");
            }
        }

        private static bool Approximately(Vector3 a, Vector3 b)
        {
            return Mathf.Approximately(a.x, b.x) && Mathf.Approximately(a.y, b.y) && Mathf.Approximately(a.z, b.z);
        }

        private static void ValidateNavigationGeometry(GameObject quadrantRoot)
        {
            Transform oneWays = quadrantRoot.transform.Find("Geometry/OneWayPlatforms");
            string[] adjustedPlatforms = { "GemRoute_08_WallEntry", "GemRoute_10_UpperZig", "GemRoute_12_RoomEntry" };
            float[] expectedHeights = { 22.4f, 25.6f, 28.8f };
            for (int index = 0; index < adjustedPlatforms.Length; index++)
            {
                Transform platform = oneWays.Find(adjustedPlatforms[index]);
                if (platform == null || !Mathf.Approximately(platform.localPosition.y, expectedHeights[index]))
                {
                    throw new InvalidOperationException($"Adjusted left ascent platform '{adjustedPlatforms[index]}' is missing or has drifted from its measured height.");
                }
            }

            Transform solids = quadrantRoot.transform.Find("Geometry/SolidGeometry");
            Transform startGate = solids.Find("LowerRouteGate_Start");
            Transform chestGate = solids.Find("LegendaryChestAccessGate");
            if (startGate == null || chestGate == null ||
                !Mathf.Approximately(startGate.localPosition.y - startGate.localScale.y * 0.5f, 0f) ||
                !Mathf.Approximately(chestGate.localPosition.y - chestGate.localScale.y * 0.5f, 0f) ||
                !Mathf.Approximately(startGate.localScale.y, 3.25f) ||
                !Mathf.Approximately(chestGate.localScale.y, 3.25f))
            {
                throw new InvalidOperationException("Both lower-route gates must be solid, 3.25-unit barriers joined exactly to the outer floor.");
            }

            Transform startApproach = oneWays.Find("Main_01_StartExit");
            Transform chestApproach = oneWays.Find("ChestRoute_03_RoomApproach");
            float startRise = startGate.localScale.y - (startApproach.localPosition.y + startApproach.localScale.y * 0.5f);
            float chestRise = chestGate.localScale.y - (chestApproach.localPosition.y + chestApproach.localScale.y * 0.5f);
            float startGap = startGate.localPosition.x - startGate.localScale.x * 0.5f - (startApproach.localPosition.x + startApproach.localScale.x * 0.5f);
            float chestGap = chestGate.localPosition.x - chestGate.localScale.x * 0.5f - (chestApproach.localPosition.x + chestApproach.localScale.x * 0.5f);
            Transform chestBaffle = solids.Find("LegendaryChestRoom_LeftBaffle");
            float recoveryClearance = chestBaffle.localPosition.x - chestBaffle.localScale.x * 0.5f - (chestGate.localPosition.x + chestGate.localScale.x * 0.5f);
            QusapVerticalMotor verticalMotor = AssetDatabase.LoadAssetAtPath<GameObject>(PlayerPrefabPath).GetComponent<QusapVerticalMotor>();
            float normalJumpHeight = new SerializedObject(verticalMotor).FindProperty("jumpHeight").floatValue;
            if (!Mathf.Approximately(startRise, 1.5f) || !Mathf.Approximately(chestRise, 1.5f) ||
                !Mathf.Approximately(startGap, 0.25f) || !Mathf.Approximately(chestGap, 0.25f) ||
                recoveryClearance < 1.5f || startGate.localScale.y <= normalJumpHeight || chestGate.localScale.y <= normalJumpHeight)
            {
                throw new InvalidOperationException("The lower-route gates must block a ground jump, remain 1.5 units above their approach platforms, and preserve the chest-side recovery passage.");
            }
        }

        private static void ValidateOneWayPlatform(GameObject root, bool requirePrefabInstance)
        {
            QusapOneWayPlatform oneWay = root != null ? root.GetComponentInChildren<QusapOneWayPlatform>(true) : null;
            BoxCollider solid = root != null ? root.GetComponent<BoxCollider>() : null;
            BoxCollider trigger = oneWay != null ? oneWay.GetComponent<BoxCollider>() : null;
            GameObject nearestRoot = oneWay != null ? PrefabUtility.GetNearestPrefabInstanceRoot(oneWay.gameObject) : null;
            GameObject sourcePrefab = AssetDatabase.LoadAssetAtPath<GameObject>(OneWayPrefabPath);
            bool valid = root != null && oneWay != null && oneWay.enabled && solid != null && solid.enabled && !solid.isTrigger && trigger != null && trigger.enabled && trigger.isTrigger && oneWay.SolidCollider == solid && root.transform.localScale.x > 0f && root.transform.localScale.y > 0f && root.transform.localScale.z > 0f && root.transform.localRotation == Quaternion.identity && (!requirePrefabInstance || (nearestRoot == root && PrefabUtility.GetCorrespondingObjectFromSource(root) == sourcePrefab && root.layer == sourcePrefab.layer && root.CompareTag(sourcePrefab.tag)));
            if (!valid)
            {
                throw new InvalidOperationException("The reusable one-way platform is incomplete or no longer a valid prefab instance.");
            }
        }

        private static void CreateBlocks(Block[] blocks, Transform parent, Material material)
        {
            foreach (Block block in blocks)
            {
                CreateSolidCube(block.Name, parent, block.Position, block.Scale, material);
            }
        }

        private static void CreateSolidCube(string name, Transform parent, Vector3 position, Vector3 scale, Material material)
        {
            GameObject cube = GameObject.CreatePrimitive(PrimitiveType.Cube);
            cube.name = name;
            cube.transform.SetParent(parent, false);
            cube.transform.localPosition = position;
            cube.transform.localRotation = Quaternion.identity;
            cube.transform.localScale = scale;
            cube.GetComponent<MeshRenderer>().sharedMaterial = material;
            BoxCollider collider = cube.GetComponent<BoxCollider>();
            collider.enabled = true;
            collider.isTrigger = false;
            collider.center = Vector3.zero;
            collider.size = Vector3.one;
        }

        private static void CreateOneWayPlatform(GameObject prefab, Platform platform, Transform parent)
        {
            GameObject instance = (GameObject)PrefabUtility.InstantiatePrefab(prefab, parent);
            instance.name = platform.Name;
            instance.transform.localPosition = platform.Position;
            instance.transform.localRotation = Quaternion.identity;
            Vector3 sourceScale = prefab.transform.localScale;
            instance.transform.localScale = new Vector3(platform.Width, sourceScale.y, sourceScale.z);
            PrefabUtility.RecordPrefabInstancePropertyModifications(instance.transform);
            ValidateOneWayPlatform(instance, true);
        }

        private static Transform CreateMarker(string name, Transform parent, Vector3 position, Material material, PrimitiveType primitiveType, Vector3 scale)
        {
            Transform marker = CreateGroup(name, parent);
            marker.localPosition = position;
            GameObject visual = GameObject.CreatePrimitive(primitiveType);
            visual.name = "EditorMarker_NoGameplay";
            visual.transform.SetParent(marker, false);
            visual.transform.localScale = scale;
            Collider collider = visual.GetComponent<Collider>();
            if (collider != null)
            {
                UnityEngine.Object.DestroyImmediate(collider);
            }
            visual.GetComponent<MeshRenderer>().sharedMaterial = material;
            return marker;
        }

        private static Transform CreateGroup(string name, Transform parent)
        {
            GameObject group = new(name);
            group.transform.SetParent(parent, false);
            return group.transform;
        }

        private static Material CreateOrUpdateMaterial(string fileName, Color color)
        {
            string path = $"{MarkerMaterialFolder}/{fileName}";
            Material material = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (material == null)
            {
                Shader shader = Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard");
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
            EnsureFolder("Assets/_Qusap/Materials", "BlockoutMarkers");
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
            return AssetDatabase.LoadMainAssetAtPath(TargetScenePath) != null || AssetDatabase.LoadMainAssetAtPath(TargetPrefabPath) != null;
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
