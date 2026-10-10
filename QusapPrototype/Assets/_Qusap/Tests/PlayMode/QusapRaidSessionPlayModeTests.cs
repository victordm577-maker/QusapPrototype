#if UNITY_EDITOR
using System;
using System.Collections;
using System.Linq;
using System.Reflection;
using NUnit.Framework;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

namespace Qusap.Tests
{
    public sealed class QusapRaidSessionPlayModeTests
    {
        private const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;
        private InputTestFixture devices;
        private object runtime;
        private QusapRaidBootstrap raid;
        private QusapRaidSessionObserver observer;
        private QusapRaidInventory[] players;
        private QusapExtractionVolume zone;
        private Gamepad pad;
        private float scale, step;
        private QusapRaidSessionAuthority Model => observer.Authority;
        private void Clock()
        {
            runtime.GetType().GetProperty("currentTimeOffsetToRealtimeSinceStartup").SetValue(runtime, 0d);
            runtime.GetType().GetProperty("currentTimeForFixedUpdate").SetValue(runtime, devices.currentTime);
        }
        private IEnumerator Tick(float seconds = 0.08f)
        { float end = Time.time + seconds; while (Time.time < end) { devices.currentTime += Time.deltaTime; Clock(); InputSystem.Update(); yield return null; } }
        private void Place(int player, float x)
        { var body = players[player].GetComponent<Rigidbody>(); body.position = new Vector3(x, 1.01f, 0); if (!body.isKinematic) body.linearVelocity = Vector3.zero; Physics.SyncTransforms(); }
        private QusapLootInstance Bag(int player, QusapLootCategory category = QusapLootCategory.Fragment)
        { var item = raid.Session.World.Create(raid.Definitions.First(d => d.Category == category), "session test " + raid.Session.World.NextItemInstanceId); Assert.That(players[player].State.TryPickup(item.LootInstanceId), Is.EqualTo(QusapLootResult.Success)); return item; }
        [UnitySetUp] public IEnumerator Setup()
        {
            scale = Time.timeScale; step = Time.fixedDeltaTime; devices = new InputTestFixture(); devices.Setup();
            InputSystem.AddDevice<Keyboard>(); pad = InputSystem.AddDevice<Gamepad>();
            runtime = typeof(InputTestFixture).GetProperty("runtime", Private).GetValue(devices);
            runtime.GetType().GetProperty("advanceTimeEachDynamicUpdate").SetValue(runtime, 0d);
            var load = EditorSceneManager.LoadSceneAsyncInPlayMode("Assets/_Qusap/Scenes/RaidSessionPlayground.unity", new LoadSceneParameters(LoadSceneMode.Single));
            while (!load.isDone) yield return null;
            new GameObject("Session_TestClock").AddComponent<QusapVitalityTestClock>().Synchronize = Clock;
            foreach (var pickup in UnityEngine.Object.FindObjectsByType<QusapLootPickup>()) pickup.GetComponent<Collider>().enabled = false;
            UnityEngine.Object.FindAnyObjectByType<QusapDamageVolume>().gameObject.SetActive(false);
            yield return Tick(0.2f);
            raid = UnityEngine.Object.FindAnyObjectByType<QusapRaidBootstrap>(); observer = UnityEngine.Object.FindAnyObjectByType<QusapRaidSessionObserver>();
            players = raid.Participants.ToArray(); zone = UnityEngine.Object.FindAnyObjectByType<QusapExtractionVolume>();
            Place(0, -5); Place(1, 5); yield return Tick();
        }
        [UnityTearDown] public IEnumerator Cleanup()
        {
            Assert.That(Time.timeScale, Is.EqualTo(scale)); Assert.That(Time.fixedDeltaTime, Is.EqualTo(step));
            foreach (var root in SceneManager.GetActiveScene().GetRootGameObjects()) { root.SetActive(false); UnityEngine.Object.Destroy(root); }
            yield return null; yield return null; devices.TearDown();
        }
        [UnityTest] public IEnumerator WaitingRegistrationStartAndRepeatedStart()
        {
            Assert.That(Model.Status, Is.EqualTo(QusapRaidSessionStatus.Waiting)); Assert.That(Model.ParticipantCount, Is.EqualTo(2));
            Assert.That(Model.SessionId, Is.EqualTo(raid.Session.World.RaidId)); Assert.That(zone.RequiredSeconds, Is.EqualTo(5));
            yield return Tick(0.2f); Assert.That(Model.Status, Is.EqualTo(QusapRaidSessionStatus.Waiting));
            Assert.That(observer.StartSession(), Is.True); Assert.That(observer.StartSession(), Is.False);
            Assert.That(Model.ActiveCount, Is.EqualTo(2)); Assert.That(Model.FinalResult, Is.Null);
        }
        [UnityTest] public IEnumerator RealExtractionLeavesP2PlayingWithNoSecondTransferOrContainer()
        {
            observer.StartSession(); var loot = Bag(0); var safe = Bag(0, QusapLootCategory.Relic); players[0].TryMoveToSecurePocket(safe.LootInstanceId);
            zone.Configure(0.15f); Place(0, -2); yield return Tick(0.35f);
            Model.TryGetOutcome("P1", out var outcome); Assert.That(outcome, Is.EqualTo(QusapRaidParticipantOutcome.Extracted));
            Assert.That(Model.Status, Is.EqualTo(QusapRaidSessionStatus.Running)); Assert.That(Model.ActiveCount, Is.EqualTo(1));
            Assert.That(Model.SessionFinishedEvents, Is.Zero); Assert.That(Model.FinalResult, Is.Null);
            Assert.That(raid.Session.Containers, Is.Empty); Assert.That(raid.Session.ExtractionTransfersResolved, Is.EqualTo(1));
            Assert.That(players[0].ReadStashSnapshot().Count, Is.EqualTo(3));
            Assert.That(players[0].ReadStashSnapshot().Any(i => i.LootInstanceId == loot.LootInstanceId), Is.True);
            Assert.That(observer.ObserveExtraction(players[0].State), Is.False); Assert.That(observer.ObserveElimination(players[0].Receiver), Is.False);
            var before = players[1].transform.position; devices.Set(pad.leftStick, Vector2.left); yield return Tick(0.4f);
            Assert.That(players[1].transform.position.x, Is.LessThan(before.x - 0.3f)); devices.Set(pad.leftStick, Vector2.zero);
            Bag(1); Assert.That(players[1].CanOperate, Is.True); Assert.That(Model.Status, Is.EqualTo(QusapRaidSessionStatus.Running));
            Assert.That(players[1].GetComponentsInChildren<Animator>(true).All(a => !a.applyRootMotion), Is.True);
        }
        [UnityTest] public IEnumerator DepletionDoesNotResolveUntilAuthoritativeEliminatedEvent()
        {
            observer.StartSession(); Bag(0); players[0].Receiver.TryReceiveEnvironmentDamage(1000, zone);
            Assert.That(players[0].Receiver.IsEliminated, Is.False); Assert.That(Model.EliminatedCount, Is.Zero);
            Assert.That(observer.ObserveElimination(players[0].Receiver), Is.False);
            yield return Tick(); Assert.That(Model.EliminatedCount, Is.EqualTo(1)); Assert.That(Model.ActiveCount, Is.EqualTo(1));
            Assert.That(Model.Status, Is.EqualTo(QusapRaidSessionStatus.Running)); Assert.That(raid.Session.Containers.Count, Is.EqualTo(1));
            Assert.That(observer.ObserveElimination(players[0].Receiver), Is.False);
            Assert.That(observer.ObserveExtraction(players[0].State), Is.False); Assert.That(raid.Session.Containers.Count, Is.EqualTo(1));
        }
        [UnityTest] public IEnumerator RealMixedFinishPublishesOneImmutableResultAndKeepsLootResolutionExclusive()
        {
            observer.StartSession(); var extractedLoot = Bag(0); var deathLoot = Bag(1); var safe = Bag(1, QusapLootCategory.Relic);
            players[1].TryMoveToSecurePocket(safe.LootInstanceId); int events = 0; Model.SessionFinished += _ => events++;
            zone.Configure(0.15f); Place(0, -2); yield return Tick(0.35f);
            players[1].Receiver.TryReceiveEnvironmentDamage(1000, zone); yield return Tick();
            var result = Model.FinalResult; Assert.That(Model.Status, Is.EqualTo(QusapRaidSessionStatus.Finished));
            Assert.That(result.ExtractedCount, Is.EqualTo(1)); Assert.That(result.EliminatedCount, Is.EqualTo(1));
            CollectionAssert.AreEqual(new[] { QusapRaidParticipantOutcome.Extracted, QusapRaidParticipantOutcome.Eliminated }, result.Participants.Select(p => p.Outcome));
            var stashBefore = players.Select(p => p.ReadStashSnapshot().Select(i => i.LootInstanceId).ToArray()).ToArray();
            for (int i = 0; i < 2; i++) { Assert.That(observer.ObserveExtraction(players[i].State), Is.False); Assert.That(observer.ObserveElimination(players[i].Receiver), Is.False); }
            Assert.That(raid.Session.SettleExtraction(players[0].State, players[0].WeaponAdapter), Is.EqualTo(QusapLootResult.Blocked));
            Assert.That(players[1].RetryDeathSettlement(), Is.EqualTo(QusapLootResult.Success));
            Assert.That(raid.Session.SettleExtraction(players[1].State, players[1].WeaponAdapter), Is.EqualTo(QusapLootResult.Blocked));
            Assert.That(raid.Session.SettleDeath(players[0].State, players[0].WeaponAdapter), Is.EqualTo(QusapLootResult.Blocked));
            yield return Tick(); Assert.That(events, Is.EqualTo(1)); Assert.That(Model.SessionFinishedEvents, Is.EqualTo(1));
            Assert.That(Model.FinalResult, Is.SameAs(result)); Assert.That(raid.Session.ExtractionTransfersResolved, Is.EqualTo(1));
            Assert.That(raid.Session.Containers.Count, Is.EqualTo(1)); Assert.That(raid.ContainerViews.Count, Is.EqualTo(1));
            Assert.That(deathLoot.Location, Is.EqualTo(QusapLootLocation.DeathContainer)); Assert.That(safe.Location, Is.EqualTo(QusapLootLocation.Stash));
            Assert.That(extractedLoot.Location, Is.EqualTo(QusapLootLocation.Stash));
            for (int i = 0; i < 2; i++) CollectionAssert.AreEqual(stashBefore[i], players[i].ReadStashSnapshot().Select(x => x.LootInstanceId));
        }
        [UnityTest] public IEnumerator BothExtractionsFinishAndBothDeathsFinishWithoutCrossResolution()
        {
            observer.StartSession(); zone.Configure(0.15f); Place(0, -2); yield return Tick(0.35f);
            Place(1, -2); yield return Tick(0.35f); Assert.That(Model.ExtractedCount, Is.EqualTo(2));
            Assert.That(Model.Status, Is.EqualTo(QusapRaidSessionStatus.Finished)); Assert.That(raid.Session.Containers, Is.Empty);
            Assert.That(raid.Session.ExtractionTransfersResolved, Is.EqualTo(2));
        }
        [UnityTest] public IEnumerator BothEliminationsFinishWithoutExtraction()
        {
            observer.StartSession(); foreach (var p in players) p.Receiver.TryReceiveEnvironmentDamage(1000, zone);
            yield return Tick(); Assert.That(Model.EliminatedCount, Is.EqualTo(2)); Assert.That(Model.Status, Is.EqualTo(QusapRaidSessionStatus.Finished));
            Assert.That(Model.SessionFinishedEvents, Is.EqualTo(1)); Assert.That(raid.Session.Containers.Count, Is.EqualTo(2));
            Assert.That(raid.Session.ExtractionTransfersResolved, Is.Zero);
        }
        [UnityTest] public IEnumerator DamageHealingHitstunCombatAndTechnicalRecoveryDoNotChangeSession()
        {
            observer.StartSession(); var loot = Bag(1); var sword = players[1].WeaponAdapter.CurrentWeapon;
            players[1].Receiver.TryReceiveEnvironmentDamage(25, zone); players[1].Receiver.Heal(5);
            players[1].GetComponent<QusapHitstunController>().EnterHitstun(0.1f); yield return Tick(0.2f);
            Assert.That(players[1].GetComponent<QusapCombatController>().TryStartAttack(QusapAttackType.WeakKick), Is.True);
            players[1].GetComponent<QusapRespawnController>().TechnicalRecovery(); yield return Tick();
            Assert.That(Model.ActiveCount, Is.EqualTo(2)); Assert.That(Model.ExtractedCount + Model.EliminatedCount, Is.Zero);
            Assert.That(Model.RejectedSignals, Is.Zero); Assert.That(Model.Status, Is.EqualTo(QusapRaidSessionStatus.Running));
            Assert.That(players[1].Receiver.CurrentHealth, Is.EqualTo(80)); Assert.That(loot.Location, Is.EqualTo(QusapLootLocation.Backpack));
            Assert.That(players[1].WeaponAdapter.CurrentWeapon, Is.SameAs(sword)); Assert.That(raid.Session.Containers, Is.Empty);
        }
        [UnityTest] public IEnumerator ForgedLiveAndForeignSourcesAreRejected()
        {
            observer.StartSession(); using var foreign = new QusapRaidSession("foreign"); var state = foreign.Register("P1", "profile"); foreign.SettleExtraction(state);
            Assert.That(observer.ObserveExtraction(state), Is.False); Assert.That(observer.ObserveExtraction(players[0].State), Is.False);
            Assert.That(observer.ObserveElimination(players[0].Receiver), Is.False); Assert.That(observer.ObserveElimination(null), Is.False);
            Assert.That(Model.ActiveCount, Is.EqualTo(2)); Assert.That(Model.RejectedSignals, Is.EqualTo(4)); yield return Tick();
        }
        [UnityTest] public IEnumerator WaitingCannotStartWithAlreadyEliminatedParticipant()
        {
            players[0].Receiver.TryReceiveEnvironmentDamage(1000, zone); yield return Tick();
            Assert.That(Model.Status, Is.EqualTo(QusapRaidSessionStatus.Waiting)); Assert.That(observer.StartSession(), Is.False);
            Assert.That(Model.EliminatedCount, Is.Zero); Assert.That(Model.SessionFinishedEvents, Is.Zero);
        }
        [UnityTest] public IEnumerator DestroyingObserverUnsubscribesWithoutChangingGameplay()
        {
            observer.StartSession(); var model = Model; UnityEngine.Object.Destroy(observer); yield return Tick();
            Assert.That(raid.Session.SettleExtraction(players[0].State, players[0].WeaponAdapter), Is.EqualTo(QusapLootResult.Success));
            players[1].Receiver.TryReceiveEnvironmentDamage(1000, zone); yield return Tick();
            Assert.That(model.ActiveCount, Is.EqualTo(2)); Assert.That(model.SessionFinishedEvents, Is.Zero);
            Assert.That(raid.Session.ExtractionTransfersResolved, Is.EqualTo(1)); Assert.That(raid.Session.Containers.Count, Is.EqualTo(1));
        }
    }

    public sealed class QusapRaidSessionLogicPlayModeTests
    {
        [TestCase(1)] [TestCase(2)] [TestCase(3)] [TestCase(4)] [TestCase(5)] [TestCase(6)] [TestCase(7)] [TestCase(8)]
        public void PureLogicRegistrationStartAndCapacity(int count)
        {
            var s = new QusapRaidSessionAuthority("logic", count); Assert.That(s.StartSession(), Is.False);
            for (int i = 0; i < count; i++) Assert.That(s.Register("P" + i), Is.True);
            Assert.That(s.Register("P0"), Is.False); Assert.That(s.Register("overflow"), Is.False);
            Assert.That(s.StartSession(), Is.True); Assert.That(s.StartSession(), Is.False); Assert.That(s.ActiveCount, Is.EqualTo(count));
        }
        [TestCase(0)] [TestCase(1)] [TestCase(2)] public void EightLogicalParticipantsResolveInDifferentOrders(int order)
        {
            var s = new QusapRaidSessionAuthority("eight"); foreach (int i in Enumerable.Range(1, 8).Reverse()) s.Register("P" + i);
            s.StartSession(); int calls = 0; s.SessionFinished += _ => calls++;
            int[] sequence = order == 0 ? Enumerable.Range(1, 8).ToArray() : order == 1 ? Enumerable.Range(1, 8).Reverse().ToArray() : new[] { 4, 8, 1, 6, 2, 7, 3, 5 };
            foreach (var id in sequence)
            {
                s.TryResolve("P" + id, id % 2 == 0 ? QusapRaidParticipantOutcome.Eliminated : QusapRaidParticipantOutcome.Extracted);
                Assert.That(calls, Is.EqualTo(s.ActiveCount == 0 ? 1 : 0));
                Assert.That(s.Status, Is.EqualTo(s.ActiveCount == 0 ? QusapRaidSessionStatus.Finished : QusapRaidSessionStatus.Running));
            }
            Assert.That(s.FinalResult.ExtractedCount, Is.EqualTo(4)); Assert.That(s.FinalResult.EliminatedCount, Is.EqualTo(4));
            CollectionAssert.AreEqual(Enumerable.Range(1, 8).Select(i => "P" + i), s.FinalResult.Participants.Select(p => p.ParticipantId));
            var result = s.FinalResult; Assert.That(s.TryResolve("P1", QusapRaidParticipantOutcome.Eliminated), Is.False);
            Assert.That(s.FinalResult, Is.SameAs(result)); Assert.That(calls, Is.EqualTo(1));
        }
    }
}
#endif
