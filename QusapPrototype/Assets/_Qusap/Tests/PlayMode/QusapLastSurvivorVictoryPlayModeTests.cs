#if UNITY_EDITOR
using System.Collections;
using System.Linq;
using System.Reflection;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using UnityEngine.UI;

namespace Qusap.Tests
{
    public sealed class QusapLastSurvivorVictoryPlayModeTests
    {
        private InputTestFixture devices;
        private object runtime;
        private QusapRaidLastSurvivorPlayground scene;
        private float scale, step;
        private QusapRaidInventory P1 => scene.Raid.Participants[0];
        private QusapRaidInventory P2 => scene.Raid.Participants[1];
        private void Clock()
        { runtime.GetType().GetProperty("currentTimeOffsetToRealtimeSinceStartup").SetValue(runtime, 0d); runtime.GetType().GetProperty("currentTimeForFixedUpdate").SetValue(runtime, devices.currentTime); }
        private IEnumerator Tick(float seconds = .1f)
        { float end = Time.time + seconds; while (Time.time < end) { devices.currentTime += Time.deltaTime; Clock(); InputSystem.Update(); yield return null; } }
        [UnitySetUp] public IEnumerator Setup()
        {
            scale = Time.timeScale; step = Time.fixedDeltaTime; devices = new InputTestFixture(); devices.Setup(); InputSystem.AddDevice<Keyboard>(); InputSystem.AddDevice<Gamepad>();
            runtime = typeof(InputTestFixture).GetProperty("runtime", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(devices);
            runtime.GetType().GetProperty("advanceTimeEachDynamicUpdate").SetValue(runtime, 0d);
            var load = EditorSceneManager.LoadSceneAsyncInPlayMode("Assets/_Qusap/Scenes/RaidLastSurvivorPlayground.unity", new LoadSceneParameters(LoadSceneMode.Single));
            while (!load.isDone) yield return null;
            new GameObject("VictoryTestClock").AddComponent<QusapVitalityTestClock>().Synchronize = Clock; yield return Tick(.3f);
            scene = Object.FindAnyObjectByType<QusapRaidLastSurvivorPlayground>(); Assert.That(scene, Is.Not.Null);
            scene.Canvas.GetComponentsInChildren<Button>().Single(b => b.name == "IniciarRaid").onClick.Invoke(); yield return Tick(.3f);
            Assert.That(scene.Session.RosterClosed && scene.Session.SharedDeploymentCompleted, Is.True);
        }
        [UnityTearDown] public IEnumerator Cleanup()
        {
            foreach (var root in SceneManager.GetActiveScene().GetRootGameObjects()) { root.SetActive(false); Object.Destroy(root); }
            yield return null; yield return null; devices.TearDown(); Assert.That(Time.timeScale, Is.EqualTo(scale)); Assert.That(Time.fixedDeltaTime, Is.EqualTo(step));
        }
        private static void Place(QusapRaidInventory player, float x)
        { var body = player.GetComponent<Rigidbody>(); body.position = new(x, 1.05f, 0); if (!body.isKinematic) body.linearVelocity = Vector3.zero; Physics.SyncTransforms(); }
        private IEnumerator OpponentElimination()
        {
            Place(P1, -.65f); Place(P2, .65f); yield return Tick(.1f);
            // Face the rival using the existing gamepad movement binding, then press BodyAttack.
            devices.Set(Gamepad.current.leftStick, new Vector2(-1, 0)); yield return Tick(.06f); devices.Set(Gamepad.current.leftStick, Vector2.zero); yield return Tick(.1f);
            devices.Press(Gamepad.current.buttonWest); yield return Tick(.16f); devices.Release(Gamepad.current.buttonWest); yield return Tick(.45f);
            Assert.That(P1.Receiver.IsEliminated, Is.True); Assert.That(scene.Session.WinnerId, Is.EqualTo("P2"));
        }
        private IEnumerator VoluntaryExtraction()
        {
            Place(P1, 5.9f); yield return Tick(.25f);
            Assert.That(P1.GetComponent<QusapRaidExtraction>().Status, Is.EqualTo(QusapExtractionStatus.Extracting));
            Assert.That(scene.Session.Status, Is.EqualTo(QusapRaidSessionStatus.Running)); yield return Tick(5.1f);
            Assert.That(scene.Session.WinnerId, Is.EqualTo("P2"));
        }
        [UnityTest] public IEnumerator RealOpponentEliminationAwardsWinnerAndPersistsExactInventory()
        {
            var inventory = P2.ReadInventorySnapshot(); var ids = inventory.Backpack.Where(i => i.HasValue).Select(i => i.Value.LootInstanceId).Append(inventory.SecurePocket.Value.LootInstanceId).ToArray();
            Assert.That(P1.ReadInventorySnapshot().Backpack.Any(i => i.HasValue), Is.True);
            yield return OpponentElimination(); Assert.That(scene.Raid.Session.Containers.Count, Is.EqualTo(1));
            Assert.That(scene.Profiles[1].ReadStashSnapshot(scene.Profiles[1].ProfileId).Select(i => i.LootInstanceId), Is.EquivalentTo(ids));
            Assert.That(scene.Profiles[1].HasTriumphHorn, Is.True); Assert.That(scene.Raid.Session.World.LoanersDestroyed, Is.EqualTo(2)); Assert.That(scene.Duplicates, Is.Zero);
        }
        [UnityTest] public IEnumerator RealVoluntaryExtractionAwardsRemainingActiveAndExtractedParticipantGetsNoHorn()
        {
            yield return VoluntaryExtraction(); Assert.That(P1.State.Status, Is.EqualTo(QusapRaidInventoryStatus.Extracted)); Assert.That(P2.State.Status, Is.EqualTo(QusapRaidInventoryStatus.Winner));
            Assert.That(scene.Profiles[0].HasTriumphHorn, Is.False); Assert.That(scene.Profiles[0].ReadStashSnapshot(scene.Profiles[0].ProfileId).Count, Is.EqualTo(1));
            Assert.That(scene.Profiles[1].HasTriumphHorn, Is.True); Assert.That(scene.Session.FinalResult.ExtractedCount, Is.EqualTo(1)); Assert.That(scene.Session.FinalResult.EliminatedCount, Is.Zero);
        }
        [UnityTest] public IEnumerator WinnerCannotMoveAttackExtractAcquireLootOrResetDuringResolvedRaid()
        {
            yield return OpponentElimination(); var body = P2.GetComponent<Rigidbody>(); var position = body.position;
            devices.Set(Gamepad.current.leftStick, Vector2.right); devices.Press(Gamepad.current.buttonWest); yield return Tick(.3f);
            Assert.That(body.position, Is.EqualTo(position)); Assert.That(P2.GetComponent<QusapInputReader>().enabled, Is.False); Assert.That(P2.GetComponent<QusapCombatController>().enabled, Is.False);
            Assert.That(P2.CanOperate, Is.False); Assert.That(P2.State.TryPickup("missing"), Is.EqualTo(QusapLootResult.Blocked));
            Assert.That(scene.Raid.Session.SettleExtraction(P2.State, P2.WeaponAdapter), Is.EqualTo(QusapLootResult.Blocked));
            Assert.That(P2.TryMoveToSecurePocket("missing"), Is.EqualTo(QusapLootResult.Blocked));
            P2.Receiver.ResetForNewSession(); Assert.That(P2.Receiver.IsGameplayRetired, Is.True); Assert.That(P2.Receiver.AcceptsHits, Is.False);
        }
        [UnityTest] public IEnumerator SummaryShowsAuthoritativeWinnerLootHornAndNoDuplicates()
        {
            yield return OpponentElimination(); Assert.That(scene.LastSummary, Does.Contain("Ganador: P2").And.Contain("P1: Eliminated").And.Contain("P2: Winner").And.Contain("desbloqueado").And.Contain("Concesiones: 1"));
            string diagnostics = scene.Canvas.transform.Find("VictoryDiagnostics/Text").GetComponent<Text>().text;
            Assert.That(diagnostics, Does.Contain(scene.Context.RaidId).And.Contain("Roster cerrado: True").And.Contain("Active: 0").And.Contain("Duplicados detectados: 0"));
        }
        [UnityTest] public IEnumerator SecondRaidUsesNewIdKeepsUnlockAndRepeatedVictoryDoesNotDuplicateHorn()
        {
            yield return OpponentElimination(); string first = scene.Context.RaidId;
            string sword = scene.Profiles[1].ReadStashSnapshot(scene.Profiles[1].ProfileId).Single(i => i.Category == QusapLootCategory.Weapon).LootInstanceId;
            Assert.That(scene.BeginRaid(), Is.True); yield return Tick(.4f);
            Assert.That(scene.Context.RaidId, Is.Not.EqualTo(first)); Assert.That(scene.Profiles[1].IsNextModeUnlocked, Is.True);
            Assert.That(P2.ReadInventorySnapshot().EquippedWeapon.Value.LootInstanceId, Is.EqualTo(sword));
            Assert.That(scene.Session.WinnerId, Is.Null); Assert.That(scene.Session.ActiveCount, Is.EqualTo(2)); yield return VoluntaryExtraction();
            Assert.That(scene.Profiles[1].TriumphHornGrantsPerformed, Is.EqualTo(1)); Assert.That(scene.Profiles[1].TriumphHornGrantCount, Is.EqualTo(1)); Assert.That(scene.Duplicates, Is.Zero);
        }
        [UnityTest] public IEnumerator RepeatedFinishResolveAndDeathNotificationProduceZeroDuplicatesAndOneRevision()
        {
            long revision = scene.Profiles[1].Revision; yield return OpponentElimination();
            for (int i = 0; i < 3; i++)
            { scene.Raid.Session.FinishSession(); scene.Context.FinishSession(); scene.Session.EvaluateVictory(); Assert.That(P1.RetryDeathSettlement(), Is.EqualTo(QusapLootResult.Success)); }
            Assert.That(scene.Profiles[1].Revision, Is.EqualTo(revision + 1)); Assert.That(scene.Session.SessionFinishedEvents, Is.EqualTo(1)); Assert.That(scene.Duplicates, Is.Zero);
        }
        [UnityTest] public IEnumerator RootMotionRemainsDisabledAndSceneStaysOutsideBuildSettings()
        {
            Assert.That(scene.Raid.Participants.SelectMany(p => p.GetComponentsInChildren<Animator>(true)).All(a => !a.applyRootMotion), Is.True);
            Assert.That(EditorBuildSettings.scenes.Any(s => s.path.EndsWith("RaidLastSurvivorPlayground.unity")), Is.False);
            Assert.That(Object.FindAnyObjectByType<QusapLocalProfilePersistence>(), Is.Null); yield return Tick();
        }
        [UnityTest] public IEnumerator NativeKeyboardAndGamepadInputsMoveBothActiveParticipantsBeforeResolution()
        {
            float start1 = P1.transform.position.x, start2 = P2.transform.position.x;
            devices.Press(Keyboard.current.aKey); devices.Set(Gamepad.current.leftStick, Vector2.right); yield return Tick(.25f);
            devices.Release(Keyboard.current.aKey); devices.Set(Gamepad.current.leftStick, Vector2.zero); yield return Tick(.15f);
            Assert.That(P1.transform.position.x, Is.LessThan(start1)); Assert.That(P2.transform.position.x, Is.GreaterThan(start2));
            Assert.That(P1.GetComponent<QusapCombatController>().enabled && P2.GetComponent<QusapCombatController>().enabled, Is.True);
            Assert.That(scene.Session.ActiveCount, Is.EqualTo(2)); Assert.That(scene.Session.WinnerId, Is.Null);
        }
    }
}
#endif
