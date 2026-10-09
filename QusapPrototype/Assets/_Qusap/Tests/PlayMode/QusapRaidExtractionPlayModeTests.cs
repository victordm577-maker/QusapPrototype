#if UNITY_EDITOR
using System;
using System.Collections;
using System.Linq;
using System.Reflection;
using NUnit.Framework;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

namespace Qusap.Tests
{
    public sealed class QusapRaidExtractionPlayModeTests
    {
        private const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;
        private InputTestFixture devices;
        private object runtime;
        private QusapRaidBootstrap raid;
        private QusapRaidInventory[] players;
        private QusapRaidExtraction[] extraction;
        private QusapExtractionVolume zone;
        private Gamepad pad;
        private float scale, step;
        private void Clock()
        {
            runtime.GetType().GetProperty("currentTimeOffsetToRealtimeSinceStartup").SetValue(runtime, 0d);
            runtime.GetType().GetProperty("currentTimeForFixedUpdate").SetValue(runtime, devices.currentTime);
        }
        private IEnumerator Tick(float seconds = 0.08f)
        { float end = Time.time + seconds; while (Time.time < end) { devices.currentTime += Time.deltaTime; Clock(); InputSystem.Update(); yield return null; } }
        private void Place(int player, float x, float y = 1.01f)
        { var body = players[player].GetComponent<Rigidbody>(); body.position = new Vector3(x, y, 0); if (!body.isKinematic) body.linearVelocity = Vector3.zero; Physics.SyncTransforms(); }
        private QusapLootInstance Bag(int player = 0, QusapLootCategory category = QusapLootCategory.Fragment)
        { var item = raid.Session.World.Create(raid.Definitions.First(d => d.Category == category), "extraction setup"); Assert.That(players[player].State.TryPickup(item.LootInstanceId), Is.EqualTo(QusapLootResult.Success)); return item; }
        [UnitySetUp] public IEnumerator Setup()
        {
            scale = Time.timeScale; step = Time.fixedDeltaTime; devices = new InputTestFixture(); devices.Setup();
            InputSystem.AddDevice<Keyboard>(); pad = InputSystem.AddDevice<Gamepad>();
            runtime = typeof(InputTestFixture).GetProperty("runtime", Private).GetValue(devices);
            runtime.GetType().GetProperty("advanceTimeEachDynamicUpdate").SetValue(runtime, 0d);
            var load = EditorSceneManager.LoadSceneAsyncInPlayMode("Assets/_Qusap/Scenes/RaidExtractionPlayground.unity", new LoadSceneParameters(LoadSceneMode.Single));
            while (!load.isDone) yield return null;
            new GameObject("Extraction_TestClock").AddComponent<QusapVitalityTestClock>().Synchronize = Clock;
            foreach (var pickup in UnityEngine.Object.FindObjectsByType<QusapLootPickup>()) pickup.GetComponent<Collider>().enabled = false;
            UnityEngine.Object.FindAnyObjectByType<QusapDamageVolume>().gameObject.SetActive(false);
            yield return Tick(0.2f);
            raid = UnityEngine.Object.FindAnyObjectByType<QusapRaidBootstrap>(); players = raid.Participants.ToArray();
            extraction = players.Select(p => p.GetComponent<QusapRaidExtraction>()).ToArray();
            zone = UnityEngine.Object.FindAnyObjectByType<QusapExtractionVolume>(); Place(0, -5); Place(1, 5); yield return Tick();
        }
        [UnityTearDown] public IEnumerator Cleanup()
        {
            Assert.That(Time.timeScale, Is.EqualTo(scale)); Assert.That(Time.fixedDeltaTime, Is.EqualTo(step));
            foreach (var root in SceneManager.GetActiveScene().GetRootGameObjects()) { root.SetActive(false); UnityEngine.Object.Destroy(root); }
            yield return null; yield return null; devices.TearDown();
        }
        [UnityTest] public IEnumerator EntryStartsFiveSecondsMovementInsideContinuesAndExitResets()
        {
            Assert.That(zone.RequiredSeconds, Is.EqualTo(5)); Place(0, -2); yield return Tick(0.2f);
            Assert.That(extraction[0].Status, Is.EqualTo(QusapExtractionStatus.Extracting)); float elapsed = extraction[0].Countdown.Elapsed;
            Place(0, -1.5f); yield return Tick(); Assert.That(extraction[0].Countdown.Elapsed, Is.GreaterThan(elapsed));
            Place(0, -5); yield return Tick(); Assert.That(extraction[0].Countdown.Elapsed, Is.Zero);
            Assert.That(extraction[0].Countdown.Cancellation, Is.EqualTo(QusapExtractionCancellation.LeftVolume));
            Place(0, -2); yield return Tick(); Assert.That(extraction[0].Countdown.Elapsed, Is.LessThan(0.2f));
        }
        [UnityTest] public IEnumerator DamageCancelsSynchronouslyAndHitstunWithoutDamageDoesNot()
        {
            Place(0, -2); yield return Tick(0.2f); players[0].GetComponent<QusapHitstunController>().EnterHitstun(0.5f);
            yield return Tick(); Assert.That(extraction[0].Status, Is.EqualTo(QusapExtractionStatus.Extracting));
            Assert.That(players[0].Receiver.TryReceiveEnvironmentDamage(1, zone), Is.True);
            Assert.That(extraction[0].Status, Is.EqualTo(QusapExtractionStatus.Active)); Assert.That(extraction[0].Countdown.Elapsed, Is.Zero);
            Assert.That(extraction[0].Countdown.Cancellation, Is.EqualTo(QusapExtractionCancellation.Damage));
            yield return Tick(); Assert.That(extraction[0].Countdown.Elapsed, Is.Zero);
            Place(0, -5); yield return Tick(); Place(0, -2); yield return Tick(); Assert.That(extraction[0].Status, Is.EqualTo(QusapExtractionStatus.Extracting));
        }
        [UnityTest] public IEnumerator FatalDamageCancelsBeforeLateUpdateAndEliminatedCannotExtract()
        {
            Place(0, -2); yield return Tick(); players[0].Receiver.TryReceiveEnvironmentDamage(1000, zone);
            Assert.That(extraction[0].Status, Is.EqualTo(QusapExtractionStatus.Eliminated)); Assert.That(extraction[0].Countdown.Elapsed, Is.Zero);
            yield return Tick(); Assert.That(extraction[0].TryBegin(zone), Is.False);
            Assert.That(raid.Session.SettleExtraction(players[0].State, players[0].WeaponAdapter), Is.EqualTo(QusapLootResult.Blocked));
            Assert.That(raid.Session.Containers.Count, Is.EqualTo(1)); Assert.That(raid.Session.ExtractionTransfersResolved, Is.Zero);
        }
        [UnityTest] public IEnumerator TechnicalRecoveryCancelsSynchronouslyAndKeepsHealthLootAndSword()
        {
            var bag = Bag(); var safe = Bag(0, QusapLootCategory.Relic); players[0].TryMoveToSecurePocket(safe.LootInstanceId);
            var sword = players[0].WeaponAdapter.CurrentWeapon; players[0].Receiver.TryReceiveEnvironmentDamage(25, zone);
            Place(0, -2); yield return Tick(); players[0].GetComponent<QusapRespawnController>().TechnicalRecovery();
            Assert.That(extraction[0].Countdown.Elapsed, Is.Zero); Assert.That(extraction[0].Countdown.Cancellation, Is.EqualTo(QusapExtractionCancellation.TechnicalRecovery));
            Assert.That(players[0].Receiver.CurrentHealth, Is.EqualTo(75)); Assert.That(players[0].WeaponAdapter.CurrentWeapon, Is.SameAs(sword));
            Assert.That(bag.Location, Is.EqualTo(QusapLootLocation.Backpack)); Assert.That(safe.Location, Is.EqualTo(QusapLootLocation.SecurePocket));
            Assert.That(raid.Session.Containers, Is.Empty); Assert.That(players[0].ReadStashSnapshot(), Is.Empty); yield return Tick();
        }
        [UnityTest] public IEnumerator DisablingZoneOrColliderCancelsWithoutSettlement()
        {
            Place(0, -2); yield return Tick(); zone.enabled = false;
            Assert.That(extraction[0].Countdown.Elapsed, Is.Zero); Assert.That(extraction[0].Countdown.Cancellation, Is.EqualTo(QusapExtractionCancellation.ZoneDisabled));
            Place(0, -5); yield return Tick(); zone.enabled = true; yield return Tick(); Place(0, -2); yield return Tick();
            zone.GetComponent<BoxCollider>().enabled = false; yield return Tick(); Assert.That(extraction[0].Countdown.Elapsed, Is.Zero);
            Assert.That(raid.Session.ExtractionTransfersResolved, Is.Zero);
        }
        [UnityTest] public IEnumerator MultiplePlayerCollidersDoNotCancelUntilLastExit()
        {
            var child = new GameObject("ExtraContact", typeof(BoxCollider)); child.transform.SetParent(players[0].transform, false);
            child.GetComponent<BoxCollider>().size = Vector3.one * 0.3f;
            Place(0, -2); yield return Tick(); child.GetComponent<Collider>().enabled = false; yield return Tick();
            Assert.That(extraction[0].Status, Is.EqualTo(QusapExtractionStatus.Extracting)); Place(0, -5); yield return Tick(); Assert.That(extraction[0].Countdown.Elapsed, Is.Zero);
        }
        [UnityTest] public IEnumerator FullLootTransfersExactlyOnceAndAllGameplayIsRetiredWithoutDeath()
        {
            var safe = Bag(0, QusapLootCategory.Relic); players[0].TryMoveToSecurePocket(safe.LootInstanceId);
            var cargo = Enumerable.Range(0, 6).Select(_ => Bag()).Append(safe).ToArray();
            var sword = players[0].WeaponAdapter.CurrentWeapon; var weaponLoot = raid.Session.World.FindWeapon(sword); ulong weaponId = sword.InstanceId;
            players[0].Receiver.TryReceiveEnvironmentDamage(10, zone); zone.Configure(0.2f); Place(0, -2); yield return Tick(0.4f);
            Assert.That(extraction[0].Status, Is.EqualTo(QusapExtractionStatus.Extracted)); Assert.That(players[0].Receiver.IsEliminated, Is.False);
            CollectionAssert.AreEquivalent(cargo.Append(weaponLoot).Select(x => x.LootInstanceId), players[0].ReadStashSnapshot().Select(x => x.LootInstanceId));
            Assert.That(weaponLoot.Weapon, Is.SameAs(sword)); Assert.That(sword.InstanceId, Is.EqualTo(weaponId)); Assert.That(players[0].WeaponAdapter.CurrentWeapon, Is.Null);
            Assert.That(players[0].ReadInventorySnapshot().Backpack.All(x => !x.HasValue), Is.True); Assert.That(players[0].ReadInventorySnapshot().SecurePocket, Is.Null);
            Assert.That(players[0].Receiver.TryReceiveEnvironmentDamage(1, zone), Is.False); Assert.That(players[0].Receiver.Heal(25), Is.Zero);
            Assert.That(players[0].TryPickup(UnityEngine.Object.FindAnyObjectByType<QusapLootPickup>()), Is.EqualTo(QusapLootResult.Blocked));
            Assert.That(players[0].TryConsume(safe.LootInstanceId), Is.EqualTo(QusapLootResult.Blocked));
            Assert.That(players[0].GetComponent<QusapCombatController>().TryStartAttack(QusapAttackType.WeakKick), Is.False);
            Assert.That(extraction[0].TryBegin(zone), Is.False); Assert.That(raid.Session.SettleExtraction(players[0].State, players[0].WeaponAdapter), Is.EqualTo(QusapLootResult.Blocked));
            Assert.That(raid.Session.SettleDeath(players[0].State, players[0].WeaponAdapter), Is.EqualTo(QusapLootResult.Blocked));
            Assert.That(players[0].GetComponentsInChildren<Collider>(true).All(x => !x.enabled), Is.True);
            Assert.That(players[0].GetComponentsInChildren<Renderer>(true).All(x => !x.enabled), Is.True);
            Assert.That(players[0].GetComponent<Rigidbody>().isKinematic, Is.True); Assert.That(raid.ContainerViews, Is.Empty); Assert.That(raid.Session.Containers, Is.Empty);
            Assert.That(raid.Session.ExtractionTransfersResolved, Is.EqualTo(1)); Assert.That(extraction[0].Countdown.TransfersResolved, Is.EqualTo(1));
            Assert.That(players[0].GetComponentsInChildren<Animator>(true).All(x => !x.applyRootMotion), Is.True);
            var position = players[0].transform.position; players[0].GetComponent<QusapRespawnController>().TechnicalRecovery(); yield return Tick(); Assert.That(players[0].transform.position, Is.EqualTo(position));
        }
        [UnityTest] public IEnumerator SettlementRetiresBeforeLedgerObserversAndDirectResolutionCannotLeavePlayerActive()
        {
            players[0].Receiver.TryReceiveEnvironmentDamage(10, zone);
            bool observed = false;
            raid.Session.World.Changed += () =>
            {
                if (players[0].State.Status != QusapRaidInventoryStatus.Extracted) return;
                observed = true;
                Assert.That(players[0].Receiver.TryReceiveEnvironmentDamage(1000, zone), Is.False);
                Assert.That(players[0].Receiver.Heal(25), Is.Zero);
                Assert.That(players[0].GetComponent<QusapWeaponEquipment>().TryEquip(players[1].WeaponAdapter.CurrentWeapon, out _), Is.EqualTo(QusapWeaponOperationResult.InvalidOwner));
            };
            Assert.That(raid.Session.SettleExtraction(players[0].State, players[0].WeaponAdapter), Is.EqualTo(QusapLootResult.Success));
            Assert.That(observed, Is.True); Assert.That(extraction[0].Status, Is.EqualTo(QusapExtractionStatus.Extracted));
            Assert.That(extraction[0].Countdown.TransfersResolved, Is.EqualTo(1)); yield return Tick(); Assert.That(raid.Session.Containers, Is.Empty);
        }
        [UnityTest] public IEnumerator BothPlayersExtractIndependentlyAndP2CanMoveHealPickupAndAttackAfterP1()
        {
            var a = Bag(); zone.Configure(0.2f); Place(0, -2); yield return Tick(0.4f);
            Assert.That(players[1].CanOperate, Is.True); var before = players[1].transform.position.x;
            devices.Set(pad.leftStick, Vector2.left); yield return Tick(0.2f); Assert.That(players[1].transform.position.x, Is.LessThan(before));
            devices.Set(pad.leftStick, Vector2.zero); yield return Tick();
            var heal = Bag(1, QusapLootCategory.Consumable); players[1].Receiver.TryReceiveEnvironmentDamage(10, zone);
            Assert.That(players[1].TryConsume(heal.LootInstanceId), Is.EqualTo(QusapLootResult.Success)); Assert.That(players[1].Receiver.CurrentHealth, Is.EqualTo(100));
            var b = Bag(1); Assert.That(players[1].GetComponent<QusapCombatController>().TryStartAttack(QusapAttackType.WeakKick), Is.True);
            yield return Tick(0.8f); Place(1, -2); yield return Tick(0.4f);
            Assert.That(extraction.All(x => x.Status == QusapExtractionStatus.Extracted), Is.True);
            Assert.That(players[0].ReadStashSnapshot().Any(x => x.LootInstanceId == a.LootInstanceId), Is.True);
            Assert.That(players[0].ReadStashSnapshot().Any(x => x.LootInstanceId == b.LootInstanceId), Is.False);
            Assert.That(players[1].ReadStashSnapshot().Any(x => x.LootInstanceId == b.LootInstanceId), Is.True); Assert.That(raid.Session.ExtractionTransfersResolved, Is.EqualTo(2));
        }
    }
}
#endif
