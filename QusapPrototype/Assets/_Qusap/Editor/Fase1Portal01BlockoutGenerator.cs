#if UNITY_EDITOR
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Qusap.EditorTools
{
    public static class Fase1Portal01BlockoutGenerator
    {
        private const string BaseScenePath = "Assets/_Qusap/Scenes/CombatPlayground.unity";
        private const string TargetScenePath = "Assets/_Qusap/Scenes/Fase1_Portal01_Blockout.unity";
        private const string PlayerPrefabPath = "Assets/_Qusap/Prefabs/QusapCombatPlayer.prefab";
        private const string OneWayPlatformPrefabPath = "Assets/_Qusap/Prefabs/QusapOneWayPlatform.prefab";
        private const string BlockoutMaterialPath = "Assets/_Qusap/Materials/CombatArenaGeometry.mat";

        private static readonly Vector3 PlayerOneStart = new(-14f, 1.05f, 0f);
        private static readonly Vector3 PlayerTwoStart = new(14f, 1.05f, 0f);

        [MenuItem("Tools/Qusap/Build Fase 1 Portal 01 Blockout")]
        private static void BuildFromMenu()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode)
            {
                EditorUtility.DisplayDialog(
                    "Fase 1 Portal 01 Blockout",
                    "El blockout no puede generarse mientras Unity está en Play Mode.",
                    "Aceptar");
                return;
            }

            if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo())
            {
                return;
            }

            SceneAsset existingScene = AssetDatabase.LoadAssetAtPath<SceneAsset>(TargetScenePath);
            if (existingScene != null
                && !EditorUtility.DisplayDialog(
                    "Regenerate Fase 1 Portal 01 Blockout",
                    "Se reemplazará únicamente Fase1_Portal01_Blockout.unity. Las escenas base no se modificarán.",
                    "Regenerar",
                    "Cancelar"))
            {
                return;
            }

            BuildSceneAsset();
            EditorUtility.DisplayDialog(
                "Fase 1 Portal 01 Blockout",
                "La arena gris fue generada, validada y guardada. La escena quedó abierta para inspección y Play Mode.",
                "Aceptar");
        }

        private static void BuildSceneAsset()
        {
            ValidateRequiredAssets();
            CloseLoadedTargetScene();

            if (AssetDatabase.LoadAssetAtPath<SceneAsset>(TargetScenePath) != null
                && !AssetDatabase.DeleteAsset(TargetScenePath))
            {
                throw new System.InvalidOperationException(
                    $"Could not replace generated scene at {TargetScenePath}.");
            }

            if (!AssetDatabase.CopyAsset(BaseScenePath, TargetScenePath))
            {
                throw new System.InvalidOperationException(
                    $"Could not copy the stable combat scene from {BaseScenePath} to {TargetScenePath}.");
            }

            AssetDatabase.ImportAsset(TargetScenePath, ImportAssetOptions.ForceSynchronousImport);
            Scene blockoutScene = EditorSceneManager.OpenScene(TargetScenePath, OpenSceneMode.Single);
            SceneManager.SetActiveScene(blockoutScene);

            BuildBlockoutHierarchy(blockoutScene);
            ValidateBuiltScene(blockoutScene);
            EditorSceneManager.MarkSceneDirty(blockoutScene);

            if (!EditorSceneManager.SaveScene(blockoutScene, TargetScenePath))
            {
                throw new System.InvalidOperationException(
                    $"Could not save generated scene at {TargetScenePath}.");
            }

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            Selection.activeGameObject = FindGameObjectInScene(
                blockoutScene,
                "Fase1_Portal01_Blockout");
        }

        private static void ValidateRequiredAssets()
        {
            if (AssetDatabase.LoadAssetAtPath<SceneAsset>(BaseScenePath) == null)
            {
                throw new System.InvalidOperationException(
                    $"Stable base scene is missing at {BaseScenePath}.");
            }

            GameObject playerPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(PlayerPrefabPath);
            if (playerPrefab == null
                || playerPrefab.GetComponent<CapsuleCollider>() == null
                || playerPrefab.GetComponent<Rigidbody>() == null
                || playerPrefab.GetComponent<QusapInputReader>() == null)
            {
                throw new System.InvalidOperationException(
                    $"Playable combat player prefab is incomplete at {PlayerPrefabPath}.");
            }

            GameObject oneWayPrefab =
                AssetDatabase.LoadAssetAtPath<GameObject>(OneWayPlatformPrefabPath);
            ValidateOneWayPlatform(oneWayPrefab, false);

            if (AssetDatabase.LoadAssetAtPath<Material>(BlockoutMaterialPath) == null)
            {
                throw new System.InvalidOperationException(
                    $"Shared blockout material is missing at {BlockoutMaterialPath}.");
            }
        }

        private static void CloseLoadedTargetScene()
        {
            Scene loadedTarget = SceneManager.GetSceneByPath(TargetScenePath);
            if (loadedTarget.IsValid() && loadedTarget.isLoaded)
            {
                EditorSceneManager.CloseScene(loadedTarget, true);
            }
        }

        private static void BuildBlockoutHierarchy(Scene scene)
        {
            GameObject root = RequireGameObject(scene, "CombatArena");
            root.name = "Fase1_Portal01_Blockout";

            QusapCombatArenaController arenaController =
                root.GetComponent<QusapCombatArenaController>();
            if (arenaController == null
                || arenaController.PlayerOne == null
                || arenaController.PlayerTwo == null)
            {
                throw new System.InvalidOperationException(
                    "The copied combat scene does not contain its configured two-player arena controller.");
            }

            Transform legacyGeometry = root.transform.Find("Geometry");
            if (legacyGeometry != null)
            {
                Object.DestroyImmediate(legacyGeometry.gameObject);
            }

            Transform environment = CreateGroup("Environment", root.transform);
            Transform outerBounds = CreateGroup("OuterBounds", environment);
            Transform solidPlatforms = CreateGroup("SolidPlatforms", environment);
            Transform oneWayPlatforms = CreateGroup("OneWayPlatforms", environment);
            Transform wallJumpSections = CreateGroup("WallJumpSections", environment);
            Transform futureGameplay = CreateGroup("FutureGameplay", root.transform);
            Transform respawns = CreateGroup("Respawns", root.transform);
            Transform players = CreateGroup("Players", root.transform);
            Transform cameras = CreateGroup("Cameras", root.transform);
            Transform lighting = CreateGroup("Lighting", root.transform);

            Material blockoutMaterial =
                AssetDatabase.LoadAssetAtPath<Material>(BlockoutMaterialPath);
            GameObject oneWayPrefab =
                AssetDatabase.LoadAssetAtPath<GameObject>(OneWayPlatformPrefabPath);

            CreateOuterBounds(outerBounds, blockoutMaterial);
            CreateSolidMaze(solidPlatforms, wallJumpSections, blockoutMaterial);
            CreateOneWayMaze(oneWayPlatforms, oneWayPrefab);
            CreateFutureRedZone(futureGameplay, blockoutMaterial);
            CreateRespawns(respawns);
            OrganizePlayers(arenaController, players);
            OrganizeCamera(scene, arenaController, cameras);
            OrganizeLighting(scene, lighting);
        }

        private static void CreateOuterBounds(Transform parent, Material material)
        {
            CreateSolidCube(
                "OuterFloor_32x2",
                parent,
                new Vector3(0f, -0.5f, 0f),
                new Vector3(32f, 1f, 2f),
                material);
            CreateSolidCube(
                "OuterCeiling_32x2",
                parent,
                new Vector3(0f, 20.5f, 0f),
                new Vector3(32f, 1f, 2f),
                material);
            CreateSolidCube(
                "OuterWall_Left",
                parent,
                new Vector3(-16.5f, 10f, 0f),
                new Vector3(1f, 20f, 2f),
                material);
            CreateSolidCube(
                "OuterWall_Right",
                parent,
                new Vector3(16.5f, 10f, 0f),
                new Vector3(1f, 20f, 2f),
                material);
        }

        private static void CreateSolidMaze(
            Transform solidParent,
            Transform wallJumpParent,
            Material material)
        {
            // Sight breaks keep opposing respawns from facing each other immediately.
            CreateSolidCube(
                "BottomSafeBand_SightBreak",
                solidParent,
                new Vector3(0f, 0.75f, 0f),
                new Vector3(0.5f, 1.5f, 2f),
                material);
            CreateSolidCube(
                "TopSafeBand_SightBreak",
                solidParent,
                new Vector3(0f, 18.75f, 0f),
                new Vector3(0.5f, 2.5f, 2f),
                material);

            // Wide central approaches and 1v1 surfaces.
            CreateSolidCube(
                "CentralRoom_LeftApproach",
                solidParent,
                new Vector3(-7.5f, 7f, 0f),
                new Vector3(5f, 0.5f, 2f),
                material);
            CreateSolidCube(
                "CentralRoom_RightApproach",
                solidParent,
                new Vector3(7.5f, 7f, 0f),
                new Vector3(5f, 0.5f, 2f),
                material);
            CreateSolidCube(
                "CentralRoom_LeftUpperApproach",
                solidParent,
                new Vector3(-8.25f, 10.2f, 0f),
                new Vector3(6.5f, 0.5f, 2f),
                material);
            CreateSolidCube(
                "CentralRoom_RightUpperApproach",
                solidParent,
                new Vector3(8.25f, 10.2f, 0f),
                new Vector3(6.5f, 0.5f, 2f),
                material);

            // Intermediate steps keep mandatory vertical rises at roughly
            // 1.6 units (about 64% of the serialized 2.5-unit jump height).
            CreateSolidCube(
                "LowerMaze_LeftStep",
                solidParent,
                new Vector3(-10f, 8.6f, 0f),
                new Vector3(3f, 0.5f, 2f),
                material);
            CreateSolidCube(
                "LowerMaze_RightStep",
                solidParent,
                new Vector3(10f, 8.6f, 0f),
                new Vector3(3f, 0.5f, 2f),
                material);

            // Stable upper respawn islands have an exit on each side.
            CreateSolidCube(
                "UpperSafeBand_LeftRespawnPlatform",
                solidParent,
                new Vector3(-11.5f, 16.5f, 0f),
                new Vector3(5f, 1f, 2f),
                material);
            CreateSolidCube(
                "UpperSafeBand_RightRespawnPlatform",
                solidParent,
                new Vector3(11.5f, 16.5f, 0f),
                new Vector3(5f, 1f, 2f),
                material);

            // Optional 6.5-unit jump + dash shortcuts. One-way side tiers below
            // catch a missed dash and remain part of the normal route.
            CreateSolidCube(
                "DashShortcut_Left_Launch",
                solidParent,
                new Vector3(-13f, 13f, 0f),
                new Vector3(3f, 0.5f, 2f),
                material);
            CreateSolidCube(
                "DashShortcut_Left_Landing",
                solidParent,
                new Vector3(-3.5f, 13f, 0f),
                new Vector3(3f, 0.5f, 2f),
                material);
            CreateSolidCube(
                "DashShortcut_Right_Launch",
                solidParent,
                new Vector3(13f, 13f, 0f),
                new Vector3(3f, 0.5f, 2f),
                material);
            CreateSolidCube(
                "DashShortcut_Right_Landing",
                solidParent,
                new Vector3(3.5f, 13f, 0f),
                new Vector3(3f, 0.5f, 2f),
                material);

            // Wall-jump sector 1: outer walls plus these hanging inner walls form two loops.
            CreateSolidCube(
                "WallJump_LowerLeft_InnerWall",
                wallJumpParent,
                new Vector3(-12.5f, 4.75f, 0f),
                new Vector3(1f, 4.5f, 2f),
                material);
            CreateSolidCube(
                "WallJump_LowerRight_InnerWall",
                wallJumpParent,
                new Vector3(12.5f, 4.75f, 0f),
                new Vector3(1f, 4.5f, 2f),
                material);

            // Wall-jump sector 2: a three-unit central shaft in the upper maze.
            CreateSolidCube(
                "WallJump_UpperCenter_LeftWall",
                wallJumpParent,
                new Vector3(-1.75f, 15.25f, 0f),
                new Vector3(0.5f, 4.5f, 2f),
                material);
            CreateSolidCube(
                "WallJump_UpperCenter_RightWall",
                wallJumpParent,
                new Vector3(1.75f, 15.25f, 0f),
                new Vector3(0.5f, 4.5f, 2f),
                material);
        }

        private static void CreateOneWayMaze(Transform parent, GameObject prefab)
        {
            // Lower maze: rises are 1.6 units between surfaces, keeping the
            // one-way restoration threshold below 70% of full jump height.
            CreateOneWayPlatform(prefab, "Lower_Entry_Left", parent, new Vector3(-8f, 1.35f, 0f));
            CreateOneWayPlatform(prefab, "Lower_Entry_Center", parent, new Vector3(0f, 1.35f, 0f));
            CreateOneWayPlatform(prefab, "Lower_Entry_Right", parent, new Vector3(8f, 1.35f, 0f));
            CreateOneWayPlatform(prefab, "Lower_Cross_Left", parent, new Vector3(-4f, 2.95f, 0f));
            CreateOneWayPlatform(prefab, "Lower_Cross_Right", parent, new Vector3(4f, 2.95f, 0f));
            CreateOneWayPlatform(prefab, "Lower_Reconnect_Center", parent, new Vector3(0f, 4.55f, 0f));
            CreateOneWayPlatform(prefab, "Lower_UpperRoute_Left", parent, new Vector3(-4f, 6.15f, 0f));
            CreateOneWayPlatform(prefab, "Lower_UpperRoute_Right", parent, new Vector3(4f, 6.15f, 0f));

            // Pass-through floor gives the central room access from below and voluntary exits.
            CreateOneWayPlatform(prefab, "CentralRoom_Floor_Left", parent, new Vector3(-3f, 7f, 0f));
            CreateOneWayPlatform(prefab, "CentralRoom_Floor_Center", parent, new Vector3(0f, 7f, 0f));
            CreateOneWayPlatform(prefab, "CentralRoom_Floor_Right", parent, new Vector3(3f, 7f, 0f));

            // Side tiers reach the dash launch/landing surfaces through normal jumps.
            CreateOneWayPlatform(prefab, "UpperMaze_SideTier_Left", parent, new Vector3(-8f, 11.8f, 0f));
            CreateOneWayPlatform(prefab, "UpperMaze_SideTier_Right", parent, new Vector3(8f, 11.8f, 0f));

            // The room's open top leads into the central wall-jump shaft.
            CreateOneWayPlatform(prefab, "UpperMaze_Entry_Center", parent, new Vector3(0f, 13f, 0f));

            // The upper maze splits again and reconnects at the safe band.
            CreateOneWayPlatform(prefab, "UpperMaze_LeftStep", parent, new Vector3(-7.5f, 14.6f, 0f));
            CreateOneWayPlatform(prefab, "WallJump_UpperCenter_Rest", parent, new Vector3(0f, 14.6f, 0f));
            CreateOneWayPlatform(prefab, "UpperMaze_RightStep", parent, new Vector3(7.5f, 14.6f, 0f));

            // Upper safe-band connectors allow normal routes to both upper respawns.
            CreateOneWayPlatform(prefab, "Upper_Reconnect_FarLeft", parent, new Vector3(-7.5f, 16.2f, 0f));
            CreateOneWayPlatform(prefab, "Upper_Reconnect_Left", parent, new Vector3(-4f, 16.2f, 0f));
            CreateOneWayPlatform(prefab, "Upper_Reconnect_Right", parent, new Vector3(4f, 16.2f, 0f));
            CreateOneWayPlatform(prefab, "Upper_Reconnect_FarRight", parent, new Vector3(7.5f, 16.2f, 0f));
        }

        private static void CreateFutureRedZone(Transform parent, Material material)
        {
            Transform reservedZone = CreateGroup(
                "FutureRedZone_Reserved_NoGameplay",
                parent);

            CreateVisualCube(
                "ReservedBoundary_Left_NoCollider",
                reservedZone,
                new Vector3(-5f, 10f, 0f),
                new Vector3(0.08f, 6f, 0.08f),
                material);
            CreateVisualCube(
                "ReservedBoundary_Right_NoCollider",
                reservedZone,
                new Vector3(5f, 10f, 0f),
                new Vector3(0.08f, 6f, 0.08f),
                material);
            CreateVisualCube(
                "ReservedBoundary_Bottom_NoCollider",
                reservedZone,
                new Vector3(0f, 7f, 0f),
                new Vector3(10f, 0.08f, 0.08f),
                material);
            CreateVisualCube(
                "ReservedBoundary_Top_NoCollider",
                reservedZone,
                new Vector3(0f, 13f, 0f),
                new Vector3(10f, 0.08f, 0.08f),
                material);
        }

        private static void CreateRespawns(Transform parent)
        {
            CreateMarker("Respawn_01_BottomLeft", parent, PlayerOneStart);
            CreateMarker("Respawn_02_BottomRight", parent, PlayerTwoStart);
            CreateMarker(
                "Respawn_03_TopLeft",
                parent,
                new Vector3(-11.5f, 18.05f, 0f));
            CreateMarker(
                "Respawn_04_TopRight",
                parent,
                new Vector3(11.5f, 18.05f, 0f));
        }

        private static void OrganizePlayers(
            QusapCombatArenaController arenaController,
            Transform parent)
        {
            QusapInputReader playerOne = arenaController.PlayerOne;
            QusapInputReader playerTwo = arenaController.PlayerTwo;

            playerOne.name = "Player1_Qusap";
            playerTwo.name = "Player2_Qusap";
            playerOne.transform.SetParent(parent, true);
            playerTwo.transform.SetParent(parent, true);
            playerOne.transform.SetPositionAndRotation(PlayerOneStart, Quaternion.identity);
            playerTwo.transform.SetPositionAndRotation(PlayerTwoStart, Quaternion.identity);
            playerOne.SetLocalPlayerSlot(QusapLocalPlayerSlot.Player1Keyboard);
            playerTwo.SetLocalPlayerSlot(QusapLocalPlayerSlot.Player2Gamepad);

            arenaController.Configure(
                playerOne,
                playerTwo,
                PlayerOneStart,
                PlayerTwoStart,
                true);
        }

        private static void OrganizeCamera(
            Scene scene,
            QusapCombatArenaController arenaController,
            Transform parent)
        {
            GameObject cameraObject = RequireGameObject(scene, "Main Camera");
            cameraObject.transform.SetParent(parent, true);

            QusapSharedCombatCamera cameraController =
                cameraObject.GetComponent<QusapSharedCombatCamera>();
            if (cameraController == null)
            {
                throw new System.InvalidOperationException(
                    "The copied stable combat scene is missing QusapSharedCombatCamera.");
            }

            cameraController.Configure(
                arenaController.PlayerOne.transform,
                arenaController.PlayerTwo.transform);
        }

        private static void OrganizeLighting(Scene scene, Transform parent)
        {
            GameObject lightObject = RequireGameObject(scene, "Directional Light");
            lightObject.transform.SetParent(parent, true);
        }

        private static Transform CreateGroup(string name, Transform parent)
        {
            GameObject group = new(name);
            group.transform.SetParent(parent, false);
            return group.transform;
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
            cube.transform.localPosition = position;
            cube.transform.localRotation = Quaternion.identity;
            cube.transform.localScale = scale;
            cube.GetComponent<MeshRenderer>().sharedMaterial = material;
            return cube;
        }

        private static GameObject CreateVisualCube(
            string name,
            Transform parent,
            Vector3 position,
            Vector3 scale,
            Material material)
        {
            GameObject visual = CreateSolidCube(name, parent, position, scale, material);
            Object.DestroyImmediate(visual.GetComponent<BoxCollider>());
            return visual;
        }

        private static void CreateOneWayPlatform(
            GameObject prefab,
            string name,
            Transform parent,
            Vector3 position)
        {
            GameObject instance =
                (GameObject)PrefabUtility.InstantiatePrefab(prefab, parent);
            instance.name = name;
            instance.transform.localPosition = position;
            instance.transform.localRotation = Quaternion.identity;
            ValidateOneWayPlatform(instance, true);
        }

        private static void ValidateOneWayPlatform(GameObject root, bool requireInstanceRoot)
        {
            QusapOneWayPlatform oneWay =
                root != null ? root.GetComponentInChildren<QusapOneWayPlatform>(true) : null;
            BoxCollider solid = root != null ? root.GetComponent<BoxCollider>() : null;
            BoxCollider trigger = oneWay != null ? oneWay.GetComponent<BoxCollider>() : null;
            GameObject nearestInstanceRoot = oneWay != null
                ? PrefabUtility.GetNearestPrefabInstanceRoot(oneWay.gameObject)
                : null;

            bool valid = root != null
                && oneWay != null
                && oneWay.enabled
                && solid != null
                && solid.enabled
                && !solid.isTrigger
                && trigger != null
                && trigger.enabled
                && trigger.isTrigger
                && oneWay.SolidCollider == solid
                && (!requireInstanceRoot || nearestInstanceRoot == root);

            if (!valid)
            {
                throw new System.InvalidOperationException(
                    "The reusable one-way platform is incomplete or references a collider outside its own instance.");
            }
        }

        private static Transform CreateMarker(
            string name,
            Transform parent,
            Vector3 position)
        {
            Transform marker = CreateGroup(name, parent);
            marker.localPosition = position;
            return marker;
        }

        private static void ValidateBuiltScene(Scene scene)
        {
            GameObject root = RequireGameObject(scene, "Fase1_Portal01_Blockout");
            string[] requiredGroups =
            {
                "Environment",
                "FutureGameplay",
                "Respawns",
                "Players",
                "Cameras",
                "Lighting"
            };

            foreach (string requiredGroup in requiredGroups)
            {
                if (root.transform.Find(requiredGroup) == null)
                {
                    throw new System.InvalidOperationException(
                        $"Generated scene is missing hierarchy group {requiredGroup}.");
                }
            }

            QusapInputReader[] players = root.GetComponentsInChildren<QusapInputReader>(true);
            if (players.Length != 2)
            {
                throw new System.InvalidOperationException(
                    $"Generated scene must contain exactly two playable Qusap instances; found {players.Length}.");
            }

            QusapOneWayPlatform[] oneWayPlatforms =
                root.GetComponentsInChildren<QusapOneWayPlatform>(true);
            if (oneWayPlatforms.Length != 21)
            {
                throw new System.InvalidOperationException(
                    $"Generated scene must contain 21 reusable one-way platforms; found {oneWayPlatforms.Length}.");
            }

            foreach (QusapOneWayPlatform oneWay in oneWayPlatforms)
            {
                ValidateOneWayPlatform(
                    PrefabUtility.GetNearestPrefabInstanceRoot(oneWay.gameObject),
                    true);
            }

            string[] respawnNames =
            {
                "Respawn_01_BottomLeft",
                "Respawn_02_BottomRight",
                "Respawn_03_TopLeft",
                "Respawn_04_TopRight"
            };
            foreach (string respawnName in respawnNames)
            {
                if (FindGameObjectInScene(scene, respawnName) == null)
                {
                    throw new System.InvalidOperationException(
                        $"Generated scene is missing {respawnName}.");
                }
            }

            GameObject futureZone = RequireGameObject(
                scene,
                "FutureRedZone_Reserved_NoGameplay");
            if (futureZone.GetComponentsInChildren<Collider>(true).Length != 0
                || futureZone.GetComponentsInChildren<MonoBehaviour>(true).Length != 0)
            {
                throw new System.InvalidOperationException(
                    "FutureRedZone_Reserved_NoGameplay must remain visual-only with no colliders or runtime logic.");
            }

            int missingScriptCount = 0;
            foreach (Transform candidate in root.GetComponentsInChildren<Transform>(true))
            {
                missingScriptCount +=
                    GameObjectUtility.GetMonoBehavioursWithMissingScriptCount(candidate.gameObject);
            }

            if (missingScriptCount != 0)
            {
                throw new System.InvalidOperationException(
                    $"Generated scene contains {missingScriptCount} missing script references.");
            }

            CapsuleCollider playerCollider = players[0].GetComponent<CapsuleCollider>();
            Rigidbody playerBody = players[0].GetComponent<Rigidbody>();
            if (playerCollider == null
                || !Mathf.Approximately(playerCollider.radius, 0.5f)
                || !Mathf.Approximately(playerCollider.height, 2f)
                || playerBody == null
                || (playerBody.constraints & RigidbodyConstraints.FreezePositionZ) == 0)
            {
                throw new System.InvalidOperationException(
                    "Generated scene no longer preserves the playable capsule or its Z-axis constraint.");
            }
        }

        private static GameObject RequireGameObject(Scene scene, string objectName)
        {
            GameObject result = FindGameObjectInScene(scene, objectName);
            if (result == null)
            {
                throw new System.InvalidOperationException(
                    $"Required object {objectName} is missing from copied scene {scene.path}.");
            }

            return result;
        }

        private static GameObject FindGameObjectInScene(Scene scene, string objectName)
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

            return null;
        }
    }
}
#endif
