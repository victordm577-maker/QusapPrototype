#if UNITY_EDITOR
using System.Linq;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace Qusap.Tests
{
    public sealed class QusapRaidIntegratedEditModeTests
    {
        private SceneSetup[] previous;
        private QusapRaidIntegratedPlaytest scene;
        private GameObject template;
        [SetUp] public void Open()
        {
            previous = EditorSceneManager.GetSceneManagerSetup();
            EditorSceneManager.OpenScene("Assets/_Qusap/Scenes/RaidIntegratedPlaytest.unity");
            scene = Object.FindAnyObjectByType<QusapRaidIntegratedPlaytest>();
            template = (GameObject)new SerializedObject(scene).FindProperty("roundTemplate").objectReferenceValue;
        }
        [TearDown] public void Close()
        {
            if (previous.Length == 0 || previous.Any(s => string.IsNullOrEmpty(s.path)))
                EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            else EditorSceneManager.RestoreSceneManagerSetup(previous);
        }
        [Test] public void SceneIsIsolatedAndDoesNotDependOnPreparationOrCaptureHelpers()
        {
            var dependencies = AssetDatabase.GetDependencies("Assets/_Qusap/Scenes/RaidIntegratedPlaytest.unity", true);
            Assert.That(dependencies.Any(p => p.Contains("Temporary") || p.Contains("CaptureRunner") || p.Contains("SceneBuilder")), Is.False);
            Assert.That(EditorBuildSettings.scenes.Any(s => s.path.EndsWith("RaidIntegratedPlaytest.unity")), Is.False);
            Assert.That(template.activeSelf, Is.False);
            Assert.That(template.GetComponentsInChildren<QusapLocalProfilePersistence>(true), Is.Empty);
        }
        [Test] public void NormalAndReducedPresetsRetainApprovedDamageAndFiveSecondExtraction()
        {
            Assert.That(scene.TimingSettings.DurationSeconds, Is.EqualTo(480));
            Assert.That(scene.TimingSettings.ExtractionOpeningSeconds, Is.EqualTo(180));
            Assert.That(scene.HazardSettings.Stages.Select(s => s.StartsAt), Is.EqualTo(new double[] { 240, 300, 360, 420 }));
            Assert.That(scene.SetPreset(true), Is.True);
            Assert.That(scene.TimingSettings.DurationSeconds, Is.EqualTo(120));
            Assert.That(scene.TimingSettings.ExtractionOpeningSeconds, Is.EqualTo(20));
            Assert.That(scene.HazardSettings.Stages.Select(s => s.StartsAt), Is.EqualTo(new double[] { 60, 75, 90, 105 }));
            Assert.That(scene.HazardSettings.Stages.Select(s => s.Damage), Is.EqualTo(new float[] { 10, 12, 15, 20 }));
            Assert.That(template.GetComponentsInChildren<QusapExtractionVolume>(true).All(e => e.RequiredSeconds == 5), Is.True);
        }
        [Test] public void TwoExitsAreDisjointAndShareOneBootstrapAndTimingDriver()
        {
            var exits = template.GetComponentsInChildren<QusapExtractionVolume>(true);
            Assert.That(exits.Length, Is.EqualTo(2));
            Assert.That(Mathf.Abs(exits[0].transform.position.x - exits[1].transform.position.x), Is.GreaterThan(15));
            Assert.That(template.GetComponentsInChildren<QusapRaidBootstrap>(true).Length, Is.EqualTo(1));
            Assert.That(template.GetComponentsInChildren<QusapRaidTimingDriver>(true).Length, Is.EqualTo(1));
            Assert.That(template.GetComponentsInChildren<QusapRaidHazardDriver>(true).Length, Is.EqualTo(1));
            Assert.That(exits.All(e => e.GetComponent<BoxCollider>().isTrigger), Is.True);
        }
        [Test] public void LootTablesUseExistingDefinitionsAndOnlyApprovedSwords()
        {
            var raid = template.GetComponentInChildren<QusapRaidBootstrap>(true);
            var views = template.GetComponentsInChildren<QusapWorldLootContainerView>(true);
            Assert.That(views.Length, Is.EqualTo(7));
            Assert.That(views.Select(v => v.SpawnPointId).Distinct().Count(), Is.EqualTo(7));
            foreach (var view in views)
            {
                Assert.That(view.Table.Validate(raid.LootCatalog), Is.Empty);
                foreach (var row in view.Table.LootTable.Entries)
                {
                    Assert.That(raid.LootCatalog.TryResolve(row.DefinitionId, out var definition), Is.True);
                    if (definition.Category == QusapLootCategory.Weapon)
                        Assert.That(new[] { QusapWeaponVisualCatalog.BlueDefinitionId,
                            QusapWeaponVisualCatalog.PurpleDefinitionId, QusapWeaponVisualCatalog.WhiteDefinitionId }, Does.Contain(definition.WeaponDefinitionId));
                }
            }
        }
        [Test] public void BlockoutUsesApprovedOneWayPrefabAndLeavesActorsAndRootMotionUnchanged()
        {
            var platforms = template.GetComponentsInChildren<QusapOneWayPlatform>(true);
            Assert.That(platforms.Length, Is.EqualTo(10));
            Assert.That(platforms.All(p => PrefabUtility.GetPrefabAssetPathOfNearestInstanceRoot(p.gameObject)
                == "Assets/_Qusap/Prefabs/QusapOneWayPlatform.prefab"), Is.True);
            var raid = template.GetComponentInChildren<QusapRaidBootstrap>(true);
            Assert.That(raid.Participants.Count, Is.EqualTo(2));
            Assert.That(raid.Participants.Select(p => p.GetComponent<QusapInputReader>().LocalPlayerSlot),
                Is.EqualTo(new[] { QusapLocalPlayerSlot.Player1Keyboard, QusapLocalPlayerSlot.Player2Gamepad }));
            Assert.That(template.GetComponentsInChildren<Animator>(true).All(a => !a.applyRootMotion), Is.True);
            Assert.That(template.GetComponentsInChildren<QusapSharedCombatCamera>(true).Length, Is.EqualTo(1));
        }
        [Test] public void OnlyIntegratedInstancesDisableDiagnosticRollShortcutsAndRetainApprovedParryTimes()
        {
            var presenters = template.GetComponentsInChildren<MonoBehaviour>(true)
                .Where(c => c != null && c.GetType().Name == "QusapDoubleLGroundAttackPresenter").ToArray();
            Assert.That(presenters.Length, Is.EqualTo(2));
            Assert.That(presenters.All(p => !new SerializedObject(p).FindProperty("diagnosticRollShortcutsEnabled").boolValue), Is.True);
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/_Qusap/Prefabs/QusapCombatPlayer.prefab");
            var original = prefab.GetComponentsInChildren<MonoBehaviour>(true).Single(c => c != null && c.GetType().Name == "QusapDoubleLGroundAttackPresenter");
            Assert.That(new SerializedObject(original).FindProperty("diagnosticRollShortcutsEnabled").boolValue, Is.True, "Shared prefab retains review shortcut default");
            foreach (var combat in template.GetComponentsInChildren<QusapCombatController>(true))
            {
                var data = new SerializedObject(combat);
                Assert.That(data.FindProperty("finisherParrySettings.telegraphDelay").doubleValue, Is.EqualTo(.10));
                Assert.That(data.FindProperty("finisherParrySettings.parryWindowDuration").doubleValue, Is.EqualTo(.35));
                Assert.That(data.FindProperty("parryAttemptRecoveryDuration").doubleValue, Is.EqualTo(.50));
            }
        }
    }
}
#endif
