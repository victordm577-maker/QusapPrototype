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
    public sealed class QusapRaidInventoryPlayModeTests
    {
        private const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;
        private InputTestFixture devices;
        private object runtime;
        private QusapRaidBootstrap raid;
        private QusapRaidInventory[] players;
        private QusapRaidInventoryHud hud;
        private QusapWeaponMatchBootstrap weapons;
        private QusapDamageVolume volume;
        private float scale, step;
        private void Clock()
        {
            runtime.GetType().GetProperty("currentTimeOffsetToRealtimeSinceStartup").SetValue(runtime, 0d);
            runtime.GetType().GetProperty("currentTimeForFixedUpdate").SetValue(runtime, devices.currentTime);
        }
        private IEnumerator Tick(float duration = 0.06f)
        { float end = Time.time + duration; while (Time.time < end) { devices.currentTime += Time.deltaTime; Clock(); InputSystem.Update(); yield return null; } }
        private static void Place(QusapRaidInventory p, Vector3 position)
        { var body = p.GetComponent<Rigidbody>(); body.position = position; if (!body.isKinematic) body.linearVelocity = Vector3.zero; Physics.SyncTransforms(); }
        private QusapLootInstance Bag(int player = 0, QusapLootCategory category = QusapLootCategory.Fragment)
        {
            var d = raid.Definitions.First(x => x.Category == category); var item = raid.Session.World.Create(d, "PlayMode setup");
            Assert.That(players[player].State.TryPickup(item.LootInstanceId), Is.EqualTo(QusapLootResult.Success)); return item;
        }
        private void Kill(int p = 0) => Assert.That(players[p].Receiver.TryReceiveEnvironmentDamage(1000, volume), Is.True);
        [UnitySetUp] public IEnumerator SetUp()
        {
            scale = Time.timeScale; step = Time.fixedDeltaTime; devices = new InputTestFixture(); devices.Setup();
            InputSystem.AddDevice<Keyboard>(); InputSystem.AddDevice<Gamepad>();
            runtime = typeof(InputTestFixture).GetProperty("runtime", Private).GetValue(devices);
            runtime.GetType().GetProperty("advanceTimeEachDynamicUpdate").SetValue(runtime, 0d);
            var load = EditorSceneManager.LoadSceneAsyncInPlayMode("Assets/_Qusap/Scenes/RaidInventoryPlayground.unity", new LoadSceneParameters(LoadSceneMode.Single));
            while (!load.isDone) yield return null;
            new GameObject("Raid_TestClock").AddComponent<QusapVitalityTestClock>().Synchronize = Clock;
            foreach (var pickup in UnityEngine.Object.FindObjectsByType<QusapLootPickup>()) pickup.GetComponent<Collider>().enabled = false;
            yield return Tick(0.20f);
            raid = UnityEngine.Object.FindAnyObjectByType<QusapRaidBootstrap>(); players = raid.Participants.ToArray();
            hud = UnityEngine.Object.FindAnyObjectByType<QusapRaidInventoryHud>(); weapons = UnityEngine.Object.FindAnyObjectByType<QusapWeaponMatchBootstrap>();
            volume = UnityEngine.Object.FindAnyObjectByType<QusapDamageVolume>(); volume.gameObject.SetActive(false);
        }
        [UnityTearDown] public IEnumerator TearDown()
        {
            Assert.That(Time.timeScale, Is.EqualTo(scale)); Assert.That(Time.fixedDeltaTime, Is.EqualTo(step));
            foreach (var root in SceneManager.GetActiveScene().GetRootGameObjects()) { root.SetActive(false); UnityEngine.Object.Destroy(root); }
            yield return null; yield return null; devices.TearDown();
        }
        [UnityTest] public IEnumerator NativeEquipmentIsOnlyAuthorityAndStarterDoesNotUseBackpack()
        {
            var weapon = players[0].GetComponent<QusapWeaponEquipment>().EquippedWeapon;
            Assert.That(players[0].ReadInventorySnapshot().Backpack.All(x => !x.HasValue), Is.True);
            Assert.That(players[0].ReadInventorySnapshot().EquippedWeapon.Value.Weapon, Is.SameAs(weapon));
            Assert.That(raid.Session.World.FindWeapon(weapon).Weapon, Is.SameAs(weapon));
            yield return Tick();
        }
        [UnityTest] public IEnumerator DepletionBlocksImmediatelyAndSettlementWaitsForEliminatedThenOccursOnce()
        {
            var cargo = Bag(); var safe = Bag(0, QusapLootCategory.Relic); players[0].TryMoveToSecurePocket(safe.LootInstanceId);
            int events = 0; raid.Session.DeathSettled += _ => events++;
            players[0].Receiver.HealthDepleted += _ =>
            {
                Assert.That(players[0].CanOperate, Is.False); Assert.That(raid.Session.Containers, Is.Empty);
                Assert.That(players[0].TryMoveToBackpack(safe.LootInstanceId), Is.EqualTo(QusapLootResult.Blocked));
                Assert.That(players[0].TryConsume(cargo.LootInstanceId), Is.EqualTo(QusapLootResult.Blocked));
            };
            Kill(); Assert.That(raid.Session.Containers, Is.Empty); yield return Tick();
            Assert.That(players[0].Receiver.IsEliminated, Is.True); Assert.That(events, Is.EqualTo(1));
            Assert.That(raid.Session.Containers.Single().InitialTransferredSnapshot.Count, Is.EqualTo(2));
            Assert.That(cargo.Location, Is.EqualTo(QusapLootLocation.DeathContainer)); Assert.That(safe.Location, Is.EqualTo(QusapLootLocation.Stash));
            Assert.That(players[0].RetryDeathSettlement(), Is.EqualTo(QusapLootResult.Success)); Assert.That(events, Is.EqualTo(1));
        }
        [UnityTest] public IEnumerator EquippedDeathTransfersExactNativeSwordAndRivalEquipsSameInstance()
        {
            var equipment = players[0].GetComponent<QusapWeaponEquipment>(); var sword = equipment.EquippedWeapon; var nativeId = sword.InstanceId;
            Kill(); yield return Tick(); var container = raid.ContainerViews.Single(); var item = raid.Session.World.FindWeapon(sword);
            Assert.That(item.Location, Is.EqualTo(QusapLootLocation.DeathContainer)); Assert.That(sword.IsFree, Is.True); Assert.That(equipment.HasWeapon, Is.False);
            Place(players[1], players[0].transform.position + Vector3.right); yield return Tick();
            Assert.That(players[1].TryEquipWeaponFromContainer(container, item.LootInstanceId), Is.EqualTo(QusapLootResult.Occupied));
            Assert.That(players[1].TryStoreEquippedWeapon(), Is.EqualTo(QusapLootResult.Success));
            Assert.That(players[1].TryEquipWeaponFromContainer(container, item.LootInstanceId), Is.EqualTo(QusapLootResult.Success));
            Assert.That(players[1].GetComponent<QusapWeaponEquipment>().EquippedWeapon, Is.SameAs(sword)); Assert.That(sword.InstanceId, Is.EqualTo(nativeId));
            Assert.That(sword.OwnerEntityId, Is.EqualTo(players[1].GetComponent<QusapWeaponEquipment>().OwnerEntityId));
            Assert.That(item.Location, Is.EqualTo(QusapLootLocation.Equipped)); Assert.That(item.HolderId, Is.EqualTo("P2"));
            yield return Tick();
        }
        [UnityTest] public IEnumerator DisarmedDeathDoesNotInventSwordOrClaimDroppedWorldWeapon()
        {
            var sword = players[0].WeaponAdapter.CurrentWeapon;
            Assert.That(players[0].GetComponent<QusapWeaponEquipment>().TryDisarm(players[1].GetComponent<QusapCombatController>()), Is.True);
            Assert.That(weapons.DroppedWeapons.Any(v => ReferenceEquals(v.Weapon, sword)), Is.True);
            Kill(); yield return Tick();
            Assert.That(raid.Session.Containers.Single().InitialTransferredSnapshot.All(i => i.Weapon == null), Is.True);
            Assert.That(raid.Session.World.FindWeapon(sword).Location, Is.EqualTo(QusapLootLocation.World)); Assert.That(players[0].WeaponAdapter.CurrentWeapon, Is.Null);
        }
        [UnityTest] public IEnumerator DeathDuringNativeThrowLeavesFlyingSwordInWorld()
        {
            var sword = players[0].WeaponAdapter.CurrentWeapon;
            // Native swap requires a settled replacement in reach; retain its real resolver.
            Place(players[0], new Vector3(0, 1.01f, 0)); yield return Tick(0.5f);
            Assert.That(weapons.TryProcessVoluntarySwap(players[0].GetComponent<QusapWeaponEquipment>(),
                new QusapWeaponSwapThrowPress(900, Time.timeAsDouble, QusapWeaponThrowDirection.Up, -1), Time.timeAsDouble), Is.True);
            var view = weapons.DroppedWeapons.Single(v => ReferenceEquals(v.Weapon, sword)); Assert.That(view.IsSettled, Is.False);
            Kill(); yield return Tick();
            Assert.That(raid.Session.World.FindWeapon(sword).Location, Is.EqualTo(QusapLootLocation.World));
            Assert.That(raid.Session.Containers.Single().InitialTransferredSnapshot.All(i => !ReferenceEquals(i.Weapon, sword)), Is.True);
            Assert.That(view.Weapon, Is.SameAs(sword));
        }
        [UnityTest] public IEnumerator PhysicalEnvironmentDeathUsesSameSettlementRoute()
        {
            Bag(); volume.gameObject.SetActive(true);
            typeof(QusapDamageVolume).GetField("damage", Private).SetValue(volume, 1000f); // Test-only volume instance.
            Place(players[0], volume.transform.position + Vector3.up * 0.4f); yield return Tick(0.12f);
            Assert.That(players[0].Receiver.IsEliminated, Is.True); Assert.That(raid.Session.Containers.Count, Is.EqualTo(1));
        }
        [UnityTest] public IEnumerator SimultaneousDeathsCreateOneDistinctContainerPerParticipant()
        {
            Bag(); Bag(1); Kill(0); Kill(1); yield return Tick();
            Assert.That(raid.Session.Containers.Count, Is.EqualTo(2)); Assert.That(raid.ContainerViews.Count, Is.EqualTo(2));
            Assert.That(raid.Session.Containers.Select(c => c.ContainerId).Distinct().Count(), Is.EqualTo(2));
        }
        [UnityTest] public IEnumerator EliminatedPlayerCannotPickupMoveConsumeLootOrEquip()
        {
            var consumable = Bag(0, QusapLootCategory.Consumable); Kill(); yield return Tick(); var container = raid.ContainerViews.Single();
            var pickup = UnityEngine.Object.FindAnyObjectByType<QusapLootPickup>();
            Assert.That(players[0].TryPickup(pickup), Is.EqualTo(QusapLootResult.Blocked));
            Assert.That(players[0].TryConsume(consumable.LootInstanceId), Is.EqualTo(QusapLootResult.Blocked));
            Assert.That(players[0].TryMoveToSecurePocket(consumable.LootInstanceId), Is.EqualTo(QusapLootResult.Blocked));
            Assert.That(players[0].TryMoveToBackpack(consumable.LootInstanceId), Is.EqualTo(QusapLootResult.Blocked));
            Assert.That(players[0].TryTakeFromDeathContainer(container, consumable.LootInstanceId), Is.EqualTo(QusapLootResult.Blocked));
            Assert.That(players[0].TryEquipWeaponFromContainer(container, consumable.LootInstanceId), Is.EqualTo(QusapLootResult.Blocked));
            Assert.That(players[0].TryStoreEquippedWeapon(), Is.EqualTo(QusapLootResult.Blocked));
        }
        [UnityTest] public IEnumerator RivalLootsCargoOnceAndFullBackpackPreservesRemainingCargo()
        {
            var a = Bag(); var b = Bag(); Kill(); yield return Tick(); var container = raid.ContainerViews.Single();
            Place(players[1], players[0].transform.position + Vector3.right); yield return Tick();
            Assert.That(players[1].TryTakeFromDeathContainer(container, a.LootInstanceId), Is.EqualTo(QusapLootResult.Success));
            Assert.That(players[1].TryTakeFromDeathContainer(container, a.LootInstanceId), Is.EqualTo(QusapLootResult.InvalidSource));
            while (players[1].ReadInventorySnapshot().Backpack.Any(x => !x.HasValue)) Bag(1);
            Assert.That(players[1].TryTakeFromDeathContainer(container, b.LootInstanceId), Is.EqualTo(QusapLootResult.Full)); Assert.That(b.Location, Is.EqualTo(QusapLootLocation.DeathContainer));
        }
        [UnityTest] public IEnumerator TechnicalRecoveryPreservesHealthSwordBackpackPocketAndDoesNotSettle()
        {
            var bag = Bag(); var safe = Bag(0, QusapLootCategory.Relic); players[0].TryMoveToSecurePocket(safe.LootInstanceId);
            players[0].Receiver.TryReceiveEnvironmentDamage(35, volume); var sword = players[0].WeaponAdapter.CurrentWeapon;
            Place(players[0], new Vector3(-5, -9, 0)); yield return Tick(0.12f);
            Assert.That(players[0].transform.position.y, Is.GreaterThan(-8)); Assert.That(players[0].Receiver.CurrentHealth, Is.EqualTo(65));
            Assert.That(players[0].WeaponAdapter.CurrentWeapon, Is.SameAs(sword)); Assert.That(bag.Location, Is.EqualTo(QusapLootLocation.Backpack)); Assert.That(safe.Location, Is.EqualTo(QusapLootLocation.SecurePocket));
            Assert.That(raid.Session.Containers, Is.Empty); Assert.That(players[0].ReadStashSnapshot(), Is.Empty);
        }
        [UnityTest] public IEnumerator NativeHealConsumesOnlyWhenAppliedAndCannotHealDeadPlayer()
        {
            var item = Bag(0, QusapLootCategory.Consumable);
            Assert.That(players[0].TryConsume(item.LootInstanceId), Is.EqualTo(QusapLootResult.NoEffect));
            Assert.That(item.Location, Is.EqualTo(QusapLootLocation.Backpack));
            players[0].Receiver.TryReceiveEnvironmentDamage(10, volume);
            Assert.That(players[0].TryConsume(item.LootInstanceId), Is.EqualTo(QusapLootResult.Success)); Assert.That(players[0].Receiver.CurrentHealth, Is.EqualTo(100));
            Assert.That(item.Location, Is.EqualTo(QusapLootLocation.Consumed));
            Kill(); yield return Tick(); Assert.That(players[0].Receiver.Heal(25), Is.Zero);
        }
        [UnityTest] public IEnumerator HudObservesBackpackPocketStashAndTerminalAuthority()
        {
            var bag = Bag(); var safe = Bag(0, QusapLootCategory.Relic); players[0].TryMoveToSecurePocket(safe.LootInstanceId); yield return Tick();
            Assert.That(hud.BackpackLabel(0, bag.SlotIndex), Does.Contain(bag.Definition.DisplayName)); Assert.That(hud.PocketLabel(0), Does.Contain("Reliquia"));
            Kill(); yield return Tick(); Assert.That(hud.PlayerLabel(0), Does.Contain("ELIMINADO")); Assert.That(hud.StashLabel(0), Does.Contain("Reliquia")); Assert.That(hud.ContainerLabel(1), Does.Contain("Fragmento"));
        }
        [UnityTest] public IEnumerator ApprovedRootMotionClocksAndSingleSwordRemainIntactAfterTransfer()
        {
            foreach (var p in players)
            {
                Assert.That(p.GetComponentsInChildren<Animator>(true).All(a => !a.applyRootMotion), Is.True);
                var source = p.GetComponents<MonoBehaviour>().OfType<IQusapImpactVisualSource>().Single();
                Assert.That(source.ImpactVisualRoot.GetComponentsInChildren<MeshFilter>(false).Length, Is.EqualTo(1));
            }
            var sword = players[0].WeaponAdapter.CurrentWeapon; Kill(); yield return Tick(); Place(players[1], players[0].transform.position + Vector3.right);
            players[1].TryStoreEquippedWeapon(); players[1].TryEquipWeaponFromContainer(raid.ContainerViews.Single(), raid.Session.World.FindWeapon(sword).LootInstanceId); yield return Tick();
            var root = players[1].GetComponents<MonoBehaviour>().OfType<IQusapImpactVisualSource>().Single().ImpactVisualRoot;
            Assert.That(root.GetComponentsInChildren<MeshFilter>(false).Length, Is.EqualTo(1));
        }
    }
}
#endif
