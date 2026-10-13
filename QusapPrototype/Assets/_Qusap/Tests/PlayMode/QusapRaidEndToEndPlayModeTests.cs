#if UNITY_EDITOR
using System;
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
    public sealed class QusapRaidEndToEndPlayModeTests
    {
        private InputTestFixture devices;
        private object runtime;
        private QusapRaidEndToEndPlayground scene;
        private QusapRaidInventory P1 => scene.Raid.Participants[0];
        private QusapRaidInventory P2 => scene.Raid.Participants[1];
        private QusapLootInteractor I1 => scene.Interactors[0];
        private QusapLootInteractor I2 => scene.Interactors[1];
        private float scale, step;
        private void Clock()
        { runtime.GetType().GetProperty("currentTimeOffsetToRealtimeSinceStartup").SetValue(runtime, 0d); runtime.GetType().GetProperty("currentTimeForFixedUpdate").SetValue(runtime, devices.currentTime); }
        private IEnumerator Tick(float seconds = .1f)
        { float end = Time.time + seconds; while (Time.time < end) { devices.currentTime += Time.deltaTime; Clock(); InputSystem.Update(); yield return null; } }
        [UnitySetUp] public IEnumerator Setup()
        {
            scale = Time.timeScale; step = Time.fixedDeltaTime; devices = new InputTestFixture(); devices.Setup(); InputSystem.AddDevice<Keyboard>(); InputSystem.AddDevice<Gamepad>();
            runtime = typeof(InputTestFixture).GetProperty("runtime", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(devices);
            runtime.GetType().GetProperty("advanceTimeEachDynamicUpdate").SetValue(runtime, 0d);
            var load = EditorSceneManager.LoadSceneAsyncInPlayMode("Assets/_Qusap/Scenes/RaidEndToEndPlayground.unity", new LoadSceneParameters(LoadSceneMode.Single));
            while (!load.isDone) yield return null;
            new GameObject("EndToEndTestClock").AddComponent<QusapVitalityTestClock>().Synchronize = Clock; yield return Tick(.3f);
            scene = UnityEngine.Object.FindAnyObjectByType<QusapRaidEndToEndPlayground>(); Assert.That(scene, Is.Not.Null);
            Assert.That(scene.BeginRaid(), Is.True); yield return Tick(.3f);
        }
        [UnityTearDown] public IEnumerator Cleanup()
        {
            foreach (var root in SceneManager.GetActiveScene().GetRootGameObjects()) { root.SetActive(false); UnityEngine.Object.Destroy(root); }
            yield return null; yield return null; devices.TearDown(); Assert.That(Time.timeScale, Is.EqualTo(scale)); Assert.That(Time.fixedDeltaTime, Is.EqualTo(step));
        }
        private static void Place(QusapRaidInventory player, float x)
        { var body = player.GetComponent<Rigidbody>(); body.position = new(x, 1.05f, 0); if (!body.isKinematic) body.linearVelocity = Vector3.zero; Physics.SyncTransforms(); }
        private IEnumerator Hold(int player)
        {
            if (player == 0) devices.Press(Keyboard.current.fKey); else devices.Press(Gamepad.current.dpad.up);
            yield return Tick(.2f); Assert.That(scene.Interactors[player].Progress.IsHolding, Is.True);
            yield return Tick(.4f); Assert.That(scene.Interactors[player].PresentationOpen, Is.True);
            if (player == 0) devices.Release(Keyboard.current.fKey); else devices.Release(Gamepad.current.dpad.up); yield return Tick();
        }
        private IEnumerator Take(QusapLootInteractor interactor, string id)
        {
            for (int attempts = 0; interactor.SelectedInstanceId != id && attempts < 20; attempts++) interactor.Select(1);
            Assert.That(interactor.SelectedInstanceId, Is.EqualTo(id)); interactor.QueueCommand(new(false, takeStack: true)); yield return Tick();
            Assert.That(interactor.LastReply.Confirmed, Is.True); Assert.That(interactor.LastReply.DestinationInstanceId, Is.EqualTo(id));
        }
        private void PocketThroughHud(string id)
        {
            var slot = P1.ReadInventorySnapshot().Backpack.Single(i => i.HasValue && i.Value.LootInstanceId == id).Value.SlotIndex;
            var panel = scene.InventoryHud.Canvas.transform.Find("P1_InventoryPanel");
            panel.GetComponentsInChildren<Button>().Single(b => b.name == "Slot_" + slot).onClick.Invoke();
            panel.GetComponentsInChildren<Button>().Single(b => b.name == "A seguro").onClick.Invoke();
        }
        [UnityTest] public IEnumerator CompleteRaidPreservesPocketLootedIdentityAndSecondRaidWhileDiscardingLoansAndAbandonedLoot()
        {
            Assert.That(P1.Session, Is.SameAs(P2.Session)); Assert.That(scene.Profiles[0], Is.Not.SameAs(scene.Profiles[1]));
            Assert.That(EditorBuildSettings.scenes.Any(s => s.path.EndsWith("RaidEndToEndPlayground.unity")), Is.False);
            Assert.That(UnityEngine.Object.FindAnyObjectByType<QusapLocalProfilePersistence>(), Is.Null);
            Place(P1, -5); yield return Tick(); yield return Hold(0);
            var valuable = I1.Observed; var relic = valuable.Items.First(i => i.Category == QusapLootCategory.Relic); var cargo = valuable.Items.First(i => i.Category == QusapLootCategory.Material);
            yield return Take(I1, relic.LootInstanceId); yield return Take(I1, cargo.LootInstanceId); PocketThroughHud(relic.LootInstanceId); yield return Tick();
            Assert.That(P1.ReadInventorySnapshot().SecurePocket.Value.LootInstanceId, Is.EqualTo(relic.LootInstanceId));
            Place(P2, .4f); yield return Tick(); yield return Hold(1); Assert.That(I2.Observed.Items.Count, Is.GreaterThan(0));
            var abandoned = I2.Observed.Items.Select(i => i.LootInstanceId).ToArray(); I2.Close();
            var loan = scene.Raid.Session.World.Instances.Single(i => i.RaidLoaner && i.HolderId == "P1");
            P1.Receiver.TryReceiveEnvironmentDamage(1000, scene); yield return Tick(.3f);
            Assert.That(P1.Receiver.IsEliminated, Is.True); Assert.That(scene.Raid.ContainerViews.Count, Is.EqualTo(1)); Assert.That(scene.DeathPresentation.Projections.Count, Is.EqualTo(1));
            Assert.That(loan.Location, Is.EqualTo(QusapLootLocation.Destroyed)); Assert.That(loan.Weapon.IsRetired, Is.True);
            Assert.That(P1.ReadStashSnapshot().Single().LootInstanceId, Is.EqualTo(relic.LootInstanceId));
            Place(P2, -5); yield return Tick(); yield return Hold(1); yield return Take(I2, cargo.LootInstanceId);
            Assert.That(scene.Raid.Session.World.Get(cargo.LootInstanceId).HolderId, Is.EqualTo("P2")); I2.Close();
            Place(P2, 5.9f); yield return Tick(.2f); Assert.That(P2.GetComponent<QusapRaidExtraction>().Status, Is.EqualTo(QusapExtractionStatus.Extracting)); yield return Tick(5.2f);
            Assert.That(scene.Session.Status, Is.EqualTo(QusapRaidSessionStatus.Finished)); Assert.That(scene.Session.FinalResult.ExtractedCount, Is.EqualTo(1)); Assert.That(scene.Session.FinalResult.EliminatedCount, Is.EqualTo(1));
            Assert.That(scene.Profiles[1].ReadStashSnapshot(scene.Profiles[1].ProfileId).Any(i => i.LootInstanceId == cargo.LootInstanceId), Is.True);
            Assert.That(scene.LostItems, Is.GreaterThanOrEqualTo(abandoned.Length)); Assert.That(scene.Duplicates, Is.Zero); Assert.That(scene.SecuredItems, Is.EqualTo(1));
            var raidId = scene.Context.RaidId; var revisions = scene.Profiles.Select(p => p.Revision).ToArray();
            scene.Raid.Session.FinishSession(); scene.Raid.Session.FinishSession(); scene.Context.FinishSession(); Assert.That(scene.Session.SessionFinishedEvents, Is.EqualTo(1));
            Assert.That(scene.Profiles.Select(p => p.Revision), Is.EqualTo(revisions));
            Assert.That(scene.BeginRaid(), Is.True); yield return Tick(.3f); Assert.That(scene.Context.RaidId, Is.Not.EqualTo(raidId));
            Assert.That(scene.Profiles[0].ReadStashSnapshot(scene.Profiles[0].ProfileId).Any(i => i.LootInstanceId == relic.LootInstanceId), Is.True);
            Assert.That(scene.Profiles[1].ReadStashSnapshot(scene.Profiles[1].ProfileId).Any(i => i.LootInstanceId == cargo.LootInstanceId), Is.True);
            Assert.That(scene.Raid.Session.World.Instances.Any(i => i.LootInstanceId == loan.LootInstanceId || abandoned.Contains(i.LootInstanceId)), Is.False);
            Assert.That(scene.Duplicates, Is.Zero); Assert.That(scene.Profiles.All(p => p.HasActiveDeployment), Is.True);
            Assert.That(scene.Raid.Participants.SelectMany(p => p.GetComponentsInChildren<Animator>()).All(a => !a.applyRootMotion), Is.True);
        }
        [UnityTest] public IEnumerator PhysicalExtractionCancelsOnExitAndDamageAndDoesNotTransfer()
        {
            Place(P2, 5.9f); yield return Tick(.3f); var extraction = P2.GetComponent<QusapRaidExtraction>(); Assert.That(extraction.Status, Is.EqualTo(QusapExtractionStatus.Extracting));
            Place(P2, 4); yield return Tick(.2f); Assert.That(extraction.Countdown.Cancellation, Is.EqualTo(QusapExtractionCancellation.LeftVolume));
            Assert.That(extraction.Countdown.Elapsed, Is.Zero); Place(P2, 5.9f); yield return Tick(.2f);
            P2.Receiver.TryReceiveEnvironmentDamage(1, scene); Assert.That(extraction.Countdown.Cancellation, Is.EqualTo(QusapExtractionCancellation.Damage));
            Assert.That(scene.Raid.Session.ExtractionTransfersResolved, Is.Zero); Assert.That(scene.Profiles[1].ReadStashSnapshot(scene.Profiles[1].ProfileId), Is.Empty);
        }
        [UnityTest] public IEnumerator SimultaneousRequestsHaveSingleWinnerAndFullInventoryRejectsWithoutMutation()
        {
            Place(P1, 0); Place(P2, .7f); yield return Tick(); yield return Hold(0); yield return Hold(1);
            var id = I1.Observed.Items[0].LootInstanceId; var source = scene.Targets.Single(t => t.SpawnPointId == I1.Observed.SpawnPointId);
            var request1 = I1.CreateTakeRequest(); var request2 = I2.CreateTakeRequest(); Assert.That(request1.HasValue && request2.HasValue, Is.True);
            Assert.That(I1.Submit(request1.Value).Confirmed, Is.True); Assert.That(I2.Submit(request2.Value).Result, Is.EqualTo(QusapWorldLootResult.StaleRequest));
            Assert.That(scene.Duplicates, Is.Zero); Assert.That(scene.Raid.Session.World.Get(id).HolderId, Is.EqualTo("P1"));
            // Capacity fixture uses existing acquisition operations; it is not part of the recording.
            catalogDefinition(out var definition); while (P2.ReadInventorySnapshot().Backpack.Any(i => !i.HasValue))
            { var item = scene.Raid.Session.World.Create(definition, Guid.NewGuid().ToString("N")); Assert.That(P2.State.TryPickup(item.LootInstanceId), Is.EqualTo(QusapLootResult.Success)); }
            Place(P2, 3); yield return Tick(); I2.Close(); yield return Hold(1); var before = I2.Observed; var remaining = before.Items[0];
            var reply = I2.Submit(new("P2", before.SpawnPointId, remaining.LootInstanceId, remaining.Quantity, before.Version));
            Assert.That(reply.Result, Is.EqualTo(QusapWorldLootResult.Full)); Assert.That(scene.Owner.ReadContainer(before.SpawnPointId).Version, Is.EqualTo(before.Version));
            Assert.That(scene.Raid.Session.World.Get(remaining.LootInstanceId).Location, Is.EqualTo(QusapLootLocation.WorldContainer)); Assert.That(scene.Duplicates, Is.Zero);
        }
        private void catalogDefinition(out QusapLootDefinition definition)
        { Assert.That(scene.Raid.LootCatalog.TryResolve("raid_rare_relic", out definition), Is.True); }
        [UnityTest] public IEnumerator PhysicalMedicalLootHealsThroughAuthorizedHudAndConsumesOneUnitWithoutChangingIdentity()
        {
            Place(P1, 3); yield return Tick(); yield return Hold(0);
            var consumable = I1.Observed.Items.First(i => i.Category == QusapLootCategory.Consumable);
            yield return Take(I1, consumable.LootInstanceId); var instance = scene.Raid.Session.World.Get(consumable.LootInstanceId);
            Assert.That(P1.TryConsume(consumable.LootInstanceId), Is.EqualTo(QusapLootResult.NoEffect)); Assert.That(instance.Quantity, Is.EqualTo(consumable.Quantity));
            P1.Receiver.TryReceiveEnvironmentDamage(40, scene); float injured = P1.Receiver.CurrentHealth;
            var panel = scene.InventoryHud.Canvas.transform.Find("P1_InventoryPanel");
            panel.GetComponentsInChildren<Button>().Single(b => b.name == "Slot_" + instance.SlotIndex).onClick.Invoke();
            panel.GetComponentsInChildren<Button>().Single(b => b.name == "Consumir").onClick.Invoke(); yield return Tick();
            Assert.That(P1.Receiver.CurrentHealth, Is.GreaterThan(injured)); Assert.That(instance.Quantity, Is.EqualTo(consumable.Quantity - 1));
            Assert.That(instance.LootInstanceId, Is.EqualTo(consumable.LootInstanceId)); Assert.That(scene.Duplicates, Is.Zero);
        }
        [UnityTest] public IEnumerator EliminatedParticipantCannotMoveAttackPickupExtractOrResetInSameRaid()
        {
            P1.Receiver.TryReceiveEnvironmentDamage(1000, scene); yield return Tick(.3f); var position = P1.GetComponent<Rigidbody>().position;
            devices.Press(Keyboard.current.dKey); devices.Press(Keyboard.current.fKey); devices.Press(Keyboard.current.jKey); yield return Tick(.2f);
            Assert.That(P1.GetComponent<Rigidbody>().position, Is.EqualTo(position)); Assert.That(P1.GetComponent<QusapCombatController>().enabled, Is.False);
            Assert.That(P1.GetComponent<QusapInputReader>().enabled, Is.False); Assert.That(I1.enabled, Is.False); Assert.That(I1.PresentationOpen, Is.False);
            Assert.That(scene.Raid.Session.SettleExtraction(P1.State, P1.WeaponAdapter), Is.EqualTo(QusapLootResult.Blocked));
            Assert.That(P1.TryMoveToSecurePocket("missing"), Is.EqualTo(QusapLootResult.Blocked)); Assert.That(scene.Interaction.RequestOpen(new("P1", "common-01")).Result, Is.EqualTo(QusapWorldLootResult.Blocked));
            P1.Receiver.ResetForNewSession(); Assert.That(P1.Receiver.IsEliminated, Is.True); Assert.That(scene.Raid.ContainerViews.Count, Is.EqualTo(1));
        }
        [UnityTest] public IEnumerator ExistingBindingsRemainFAndDpadUpAndEStillThrowsNativeLoaner()
        {
            Assert.That(I1.BindingLabel, Does.StartWith("F")); Assert.That(I2.BindingLabel, Does.StartWith("D-pad arriba"));
            Place(P1, -5); yield return Tick(); var weapon = P1.WeaponAdapter.CurrentWeapon;
            var native = UnityEngine.Object.FindAnyObjectByType<QusapWeaponMatchBootstrap>();
            var replacement = native.CreateRaidLootWeapon(QusapWeaponVisualCatalog.WhiteDefinitionId);
            var create = typeof(QusapWeaponMatchBootstrap).GetMethod("TryCreateInitialDroppedWeapon", BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.That((bool)create.Invoke(native, new object[] { replacement, new Vector3(-4.7f, .35f, 0), Time.timeAsDouble }), Is.True);
            devices.Press(Keyboard.current.fKey); yield return Tick(.2f); Assert.That(I1.Progress.IsHolding, Is.True); Assert.That(P1.WeaponAdapter.CurrentWeapon, Is.SameAs(weapon));
            devices.Release(Keyboard.current.fKey); yield return Tick(); devices.Press(Keyboard.current.eKey); yield return Tick(.15f); devices.Release(Keyboard.current.eKey); yield return Tick(.2f);
            Assert.That(I1.Progress.IsHolding, Is.False); Assert.That(P1.WeaponAdapter.CurrentWeapon, Is.SameAs(replacement));
            Assert.That(scene.Raid.Session.World.FindWeapon(weapon).Location, Is.EqualTo(QusapLootLocation.World));
        }
    }
}
#endif
