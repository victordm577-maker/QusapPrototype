#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Qusap.EditorTools
{
    public static class QusapAirCombatMechanicsTestCreator
    {
        private const string ScenesFolder = "Assets/_Qusap/Scenes";
        private const string TargetScenePath = ScenesFolder + "/Qusap_AirCombatMechanicsTest_v1.unity";

        [MenuItem("Tools/Qusap/Create Air Combat Mechanics Test")]
        private static void CreateTestScene()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode)
            {
                EditorUtility.DisplayDialog(
                    "Qusap Air Combat",
                    "La escena de prueba no puede crearse mientras Unity está en Play Mode.",
                    "Aceptar");
                return;
            }

            if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo())
            {
                return;
            }

            string sourcePath = FindLatestFunctionalOneVsOneScene();
            if (string.IsNullOrEmpty(sourcePath))
            {
                EditorUtility.DisplayDialog(
                    "Qusap Air Combat",
                    "No se encontró una escena 1v1 funcional con QusapCombatArenaController y dos jugadores de combate.",
                    "Aceptar");
                return;
            }

            if (AssetDatabase.LoadAssetAtPath<SceneAsset>(TargetScenePath) != null)
            {
                bool replace = EditorUtility.DisplayDialog(
                    "Qusap Air Combat",
                    $"Ya existe {TargetScenePath}. ¿Deseas reemplazar únicamente esta copia de prueba?",
                    "Reemplazar copia",
                    "Cancelar");
                if (!replace)
                {
                    return;
                }

                Scene loadedTarget = SceneManager.GetSceneByPath(TargetScenePath);
                if (loadedTarget.IsValid() && loadedTarget.isLoaded)
                {
                    EditorSceneManager.OpenScene(sourcePath, OpenSceneMode.Single);
                }

                AssetDatabase.DeleteAsset(TargetScenePath);
            }

            if (!AssetDatabase.CopyAsset(sourcePath, TargetScenePath))
            {
                throw new InvalidOperationException($"No se pudo copiar {sourcePath} a {TargetScenePath}.");
            }

            AssetDatabase.ImportAsset(TargetScenePath, ImportAssetOptions.ForceSynchronousImport);
            Scene targetScene = EditorSceneManager.OpenScene(TargetScenePath, OpenSceneMode.Single);
            QusapCombatController[] players = FindArenaPlayers(targetScene);
            if (players.Length < 2)
            {
                throw new InvalidOperationException("La copia dejó de contener los dos jugadores de combate requeridos.");
            }

            string groundBefore = CaptureGroundData(players);
            foreach (QusapCombatController player in players)
            {
                InstallAirDefinitions(player);
            }

            string groundAfter = CaptureGroundData(players);
            if (!string.Equals(groundBefore, groundAfter, StringComparison.Ordinal))
            {
                throw new InvalidOperationException("La instalación aérea intentó cambiar datos de ataques terrestres.");
            }

            GameObject hudObject = new("AirCombatDebugHUD");
            SceneManager.MoveGameObjectToScene(hudObject, targetScene);
            hudObject.AddComponent<QusapAirCombatDebugHud>().Configure(players);

            EditorSceneManager.MarkSceneDirty(targetScene);
            if (!EditorSceneManager.SaveScene(targetScene, TargetScenePath))
            {
                throw new InvalidOperationException($"No se pudo guardar {TargetScenePath}.");
            }

            Selection.activeObject = AssetDatabase.LoadAssetAtPath<SceneAsset>(TargetScenePath);
            EditorUtility.DisplayDialog(
                "Qusap Air Combat Mechanics Test",
                $"Escena creada: {TargetScenePath}\n" +
                $"Origen 1v1: {sourcePath}\n" +
                $"Jugadores encontrados: {players.Length}\n\n" +
                "Definiciones asignadas:\n" + AirValuesSummary() + "\n\n" +
                "Ataques terrestres: intactos (comparación antes/después correcta).\n" +
                "Locomoción y Dash: no se retunearon ni reemplazaron.\n" +
                "Animator, clips, modelos y prefabs originales: sin cambios.",
                "Aceptar");
        }

        private static string FindLatestFunctionalOneVsOneScene()
        {
            IEnumerable<string> candidates = AssetDatabase.FindAssets("t:Scene", new[] { ScenesFolder })
                .Select(AssetDatabase.GUIDToAssetPath)
                .Where(path => path != TargetScenePath)
                .OrderByDescending(GetLastWriteTimeUtc)
                .ThenByDescending(path => path, StringComparer.Ordinal);

            foreach (string path in candidates)
            {
                Scene loaded = SceneManager.GetSceneByPath(path);
                bool openedForInspection = !loaded.IsValid() || !loaded.isLoaded;
                Scene scene = openedForInspection
                    ? EditorSceneManager.OpenScene(path, OpenSceneMode.Additive)
                    : loaded;

                try
                {
                    if (FindArenaPlayers(scene).Length >= 2)
                    {
                        return path;
                    }
                }
                finally
                {
                    if (openedForInspection && scene.IsValid() && scene.isLoaded)
                    {
                        EditorSceneManager.CloseScene(scene, true);
                    }
                }
            }

            return null;
        }

        private static DateTime GetLastWriteTimeUtc(string assetPath)
        {
            string absolutePath = Path.GetFullPath(assetPath);
            return File.Exists(absolutePath) ? File.GetLastWriteTimeUtc(absolutePath) : DateTime.MinValue;
        }

        private static QusapCombatController[] FindArenaPlayers(Scene scene)
        {
            List<QusapCombatController> players = new();
            foreach (GameObject root in scene.GetRootGameObjects())
            {
                foreach (QusapCombatArenaController arena in root.GetComponentsInChildren<QusapCombatArenaController>(true))
                {
                    AddPlayer(arena.PlayerOne, players);
                    AddPlayer(arena.PlayerTwo, players);
                }
            }

            return players.Distinct().ToArray();
        }

        private static void AddPlayer(QusapInputReader input, ICollection<QusapCombatController> players)
        {
            QusapCombatController combat = input != null ? input.GetComponent<QusapCombatController>() : null;
            if (combat != null)
            {
                players.Add(combat);
            }
        }

        private static void InstallAirDefinitions(QusapCombatController controller)
        {
            SerializedObject serialized = new(controller);
            serialized.Update();
            WriteAirDefinition(serialized.FindProperty("weakKickAir"), QusapAirAttackData.CreateWeakKickAir());
            WriteAirDefinition(serialized.FindProperty("strongKickAir"), QusapAirAttackData.CreateStrongKickAir());
            WriteAirDefinition(serialized.FindProperty("diveHeadbuttAir"), QusapAirAttackData.CreateDiveHeadbuttAir());
            serialized.ApplyModifiedPropertiesWithoutUndo();
            PrefabUtility.RecordPrefabInstancePropertyModifications(controller);
            EditorUtility.SetDirty(controller);
        }

        private static void WriteAirDefinition(SerializedProperty property, QusapAirAttackData data)
        {
            if (property == null)
            {
                throw new InvalidOperationException("QusapCombatController no expone las definiciones aéreas esperadas.");
            }

            property.FindPropertyRelative("attackType").enumValueIndex = (int)data.AttackType;
            property.FindPropertyRelative("startupTime").floatValue = data.StartupTime;
            property.FindPropertyRelative("activeDuration").floatValue = data.ActiveDuration;
            property.FindPropertyRelative("recoveryTime").floatValue = data.RecoveryTime;
            property.FindPropertyRelative("landingRecoveryTime").floatValue = data.LandingRecoveryTime;
            property.FindPropertyRelative("damage").floatValue = data.Damage;
            property.FindPropertyRelative("hitboxSize").vector2Value = data.HitboxSize;
            property.FindPropertyRelative("hitboxOffset").vector2Value = data.HitboxOffset;
            property.FindPropertyRelative("hitboxDepth").floatValue = data.HitboxDepth;
            property.FindPropertyRelative("horizontalKnockback").floatValue = data.HorizontalKnockback;
            property.FindPropertyRelative("verticalKnockback").floatValue = data.VerticalKnockback;
            property.FindPropertyRelative("hitstunDuration").floatValue = data.HitstunDuration;
            property.FindPropertyRelative("lockHorizontalMovement").boolValue = data.LockHorizontalMovement;
            property.FindPropertyRelative("horizontalVelocityRetention").floatValue = data.HorizontalVelocityRetention;
            property.FindPropertyRelative("endActiveWindowOnLanding").boolValue = data.EndActiveWindowOnLanding;
            property.FindPropertyRelative("diveBrakeDuration").floatValue = data.DiveBrakeDuration;
            property.FindPropertyRelative("diveVerticalBrakeMultiplier").floatValue = data.DiveVerticalBrakeMultiplier;
            property.FindPropertyRelative("diveDownwardSpeed").floatValue = data.DiveDownwardSpeed;
            property.FindPropertyRelative("diveHorizontalControlMultiplier").floatValue = data.DiveHorizontalControlMultiplier;
            property.FindPropertyRelative("diveBounceSpeed").floatValue = data.DiveBounceSpeed;
            property.FindPropertyRelative("blockDash").boolValue = data.BlockDash;
        }

        private static string CaptureGroundData(IEnumerable<QusapCombatController> players)
        {
            return string.Join("|", players.SelectMany(player => new[]
            {
                EditorJsonUtility.ToJson(player.GetAttackData(QusapAttackType.WeakKick)),
                EditorJsonUtility.ToJson(player.GetAttackData(QusapAttackType.StrongKick)),
                EditorJsonUtility.ToJson(player.GetAttackData(QusapAttackType.Headbutt))
            }));
        }

        private static string AirValuesSummary()
        {
            return Describe("WeakKickAir", QusapAirAttackData.CreateWeakKickAir()) + "\n" +
                Describe("StrongKickAir", QusapAirAttackData.CreateStrongKickAir()) + "\n" +
                Describe("DiveHeadbuttAir", QusapAirAttackData.CreateDiveHeadbuttAir());
        }

        private static string Describe(string name, QusapAirAttackData data)
        {
            string common = $"{name}: startup {data.StartupTime:0.00}, active {data.ActiveDuration:0.00}, " +
                $"recovery {data.RecoveryTime:0.00}, landing {data.LandingRecoveryTime:0.00}, " +
                $"damage {data.Damage:0.##}, KB ({data.HorizontalKnockback:0.##}, {data.VerticalKnockback:0.##}), " +
                $"hitstun {data.HitstunDuration:0.##}, box {data.HitboxSize} @ {data.HitboxOffset}, " +
                $"depth {data.HitboxDepth:0.##}, retain X {data.HorizontalVelocityRetention:0.##}, " +
                $"end-on-land {data.EndActiveWindowOnLanding}, lock X {data.LockHorizontalMovement}.";

            if (data.AttackType != QusapAttackType.Headbutt)
            {
                return common;
            }

            return common +
                $" Dive: brake {data.DiveBrakeDuration:0.##}, brake multiplier {data.DiveVerticalBrakeMultiplier:0.##}, " +
                $"speed {data.DiveDownwardSpeed:0.##}, control X {data.DiveHorizontalControlMultiplier:0.##}, " +
                $"bounce {data.DiveBounceSpeed:0.##}, block Dash {data.BlockDash}.";
        }
    }
}
#endif
