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

namespace Qusap.Tests
{
    public sealed class QusapDeathLootContainerPlayModeTests
    {
        private InputTestFixture devices;
        private object runtime;
        private QusapRaidDeathLootContainerPlayground scene;
        private float scale, step;
        private QusapRaidInventory P1 => scene.Raid.Participants.Single(p => p.ParticipantId == "P1");
        private QusapRaidInventory P2 => scene.Raid.Participants.Single(p => p.ParticipantId == "P2");
        private QusapLootInteractor I2 => scene.Interactors[1];
        private QusapDeathLootProjection Death => scene.Presentation.Projections.First();
        private void Clock() { runtime.GetType().GetProperty("currentTimeOffsetToRealtimeSinceStartup").SetValue(runtime, 0d); runtime.GetType().GetProperty("currentTimeForFixedUpdate").SetValue(runtime, devices.currentTime); }
        private IEnumerator Tick(float seconds = .12f)
        { float end = Time.time + seconds; while (Time.time < end) { devices.currentTime += Time.deltaTime; Clock(); InputSystem.Update(); yield return null; } }
        [UnitySetUp] public IEnumerator Setup()
        {
            scale = Time.timeScale; step = Time.fixedDeltaTime;
            devices = new InputTestFixture(); devices.Setup(); InputSystem.AddDevice<Keyboard>(); InputSystem.AddDevice<Gamepad>();
            runtime = typeof(InputTestFixture).GetProperty("runtime", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(devices);
            runtime.GetType().GetProperty("advanceTimeEachDynamicUpdate").SetValue(runtime, 0d);
            var load = EditorSceneManager.LoadSceneAsyncInPlayMode("Assets/_Qusap/Scenes/RaidDeathLootContainerPlayground.unity", new LoadSceneParameters(LoadSceneMode.Single));
            while (!load.isDone) yield return null;
            new GameObject("DeathLootTestClock").AddComponent<QusapVitalityTestClock>().Synchronize = Clock; yield return Tick(.3f);
            scene = UnityEngine.Object.FindAnyObjectByType<QusapRaidDeathLootContainerPlayground>();
            Assert.That(scene, Is.Not.Null); Assert.That(scene.Session.Status, Is.EqualTo(QusapRaidSessionStatus.Running));
        }
        [UnityTearDown] public IEnumerator Cleanup()
        {
            foreach (var root in SceneManager.GetActiveScene().GetRootGameObjects()) { root.SetActive(false); UnityEngine.Object.Destroy(root); }
            yield return null; yield return null; devices.TearDown();
            Assert.That(Time.timeScale, Is.EqualTo(scale)); Assert.That(Time.fixedDeltaTime, Is.EqualTo(step));
        }
        private static void Place(QusapRaidInventory player, Vector3 position)
        { var body = player.GetComponent<Rigidbody>(); body.position = position; body.linearVelocity = Vector3.zero; Physics.SyncTransforms(); }
        private IEnumerator KillP1()
        { Assert.That(P1.Receiver.TryReceiveEnvironmentDamage(1000, scene), Is.True); yield return Tick(.15f); Assert.That(P1.Receiver.IsEliminated, Is.True); Assert.That(scene.Raid.Session.Containers.Count, Is.EqualTo(1)); }
        private IEnumerator BeginHold()
        { devices.Press(Gamepad.current.dpad.up); yield return Tick(.12f); Assert.That(I2.Progress.IsHolding, Is.True); }
        private IEnumerator Open()
        { yield return KillP1(); yield return BeginHold(); yield return Tick(.4f); Assert.That(I2.PresentationOpen, Is.True); devices.Release(Gamepad.current.dpad.up); yield return Tick(.06f); }
        private IEnumerator TakeSword()
        {
            yield return Open(); Assert.That(scene.ConsumeRivalHealing(), Is.EqualTo(QusapLootResult.Success)); yield return Tick(.04f);
            Assert.That(I2.SelectedInstanceId, Is.EqualTo(scene.Sword.LootInstanceId)); I2.QueueCommand(new(false, takeOne: true)); yield return Tick(.06f);
            Assert.That(I2.LastReply.Confirmed, Is.True); Assert.That(scene.Sword.HolderId, Is.EqualTo("P2"));
        }
        [UnityTest] public IEnumerator IsolatedSceneHasRealPlayersSeededInventoryPocketAndNoProfileComponents()
        {
            Assert.That(scene.Raid.Participants.Count, Is.EqualTo(2)); Assert.That(P1.WeaponAdapter.CurrentWeapon, Is.SameAs(scene.Sword.Weapon));
            Assert.That(scene.RemainingHeal.Quantity, Is.EqualTo(1)); Assert.That(scene.Consumed.Quantity, Is.Zero); Assert.That(scene.Consumed.Location, Is.EqualTo(QusapLootLocation.Consumed));
            Assert.That(P1.ReadInventorySnapshot().SecurePocket.Value.LootInstanceId, Is.EqualTo(scene.Pocket.LootInstanceId));
            Assert.That(P2.BackpackCapacity, Is.EqualTo(2)); Assert.That(P2.ReadInventorySnapshot().Backpack.All(i => i.HasValue), Is.True);
            Assert.That(UnityEngine.Object.FindAnyObjectByType<QusapLocalProfilePersistence>(), Is.Null);
            Assert.That(EditorBuildSettings.scenes.Any(s => s.path.EndsWith("RaidDeathLootContainerPlayground.unity")), Is.False);
            Assert.That(scene.Raid.Participants.All(p => p.GetComponent<QusapCombatController>() != null && p.GetComponent<QusapHorizontalMotor>() != null), Is.True);
            yield return null;
        }
        [UnityTest] public IEnumerator RealOpponentBodyAttackCreatesOnePhysicalContainerAndDuplicateNotificationDoesNot()
        {
            devices.Set(Gamepad.current.leftStick, new Vector2(-1,0)); yield return Tick(.04f); devices.Set(Gamepad.current.leftStick, Vector2.zero); yield return Tick(.08f);
            float health = P1.Receiver.CurrentHealth; devices.Press(Gamepad.current.buttonWest); yield return Tick(.2f);
            Assert.That(P1.Receiver.CurrentHealth, Is.LessThan(health)); Assert.That(P1.Receiver.IsEliminated, Is.True);
            Assert.That(scene.Presentation.Projections.Count, Is.EqualTo(1)); Assert.That(scene.Raid.ContainerViews.Count, Is.EqualTo(1));
            Assert.That(P1.RetryDeathSettlement(), Is.EqualTo(QusapLootResult.Success)); scene.Presentation.Bind(Death.Record);
            Assert.That(scene.Raid.ContainerViews.Count, Is.EqualTo(1)); Assert.That(scene.Presentation.Projections.Count, Is.EqualTo(1));
            Assert.That(Death.Record.ContainerId, Is.EqualTo(QusapDeathLootIdentity.Format(scene.Raid.Session.World.RaidId, "P1", 1)));
        }
        [UnityTest] public IEnumerator DeathContentsKeepOriginalIdsQuantitiesAndExcludePocketConsumedAndLoaner()
        {
            var before = scene.InitialVictim; yield return KillP1(); var state = Death.View.Snapshot;
            var expected = before.Backpack.Where(i => i.HasValue && !i.Value.RaidLoaner).Select(i => (i.Value.LootInstanceId, i.Value.Quantity))
                .Append((scene.Sword.LootInstanceId, 1));
            Assert.That(state.Items.Select(i => (i.LootInstanceId, i.Quantity)), Is.EquivalentTo(expected));
            Assert.That(state.Items.Any(i => i.LootInstanceId == scene.Pocket.LootInstanceId || i.LootInstanceId == scene.Consumed.LootInstanceId || i.RaidLoaner), Is.False);
            Assert.That(scene.Loaner.Location, Is.EqualTo(QusapLootLocation.Destroyed)); Assert.That(scene.Loaner.Weapon.IsRetired, Is.True);
            Assert.That(UnityEngine.Object.FindAnyObjectByType<QusapWeaponMatchBootstrap>().DroppedWeapons.Any(v => ReferenceEquals(v.Weapon, scene.Loaner.Weapon)), Is.False);
            Assert.That(P1.ReadInventorySnapshot().Backpack.All(i => !i.HasValue), Is.True); Assert.That(P1.GetComponent<QusapWeaponEquipment>().HasWeapon, Is.False);
            Assert.That(P1.ReadStashSnapshot().Single().LootInstanceId, Is.EqualTo(scene.Pocket.LootInstanceId));
        }
        [UnityTest] public IEnumerator EliminatedPlayerHasNoPromptInputOrOperationsAndCannotReequipOrRevive()
        {
            yield return KillP1(); devices.Press(Keyboard.current.fKey); yield return Tick(.1f);
            var player = scene.Interactors[0]; Assert.That(player.enabled, Is.False); Assert.That(player.Sensor.Target, Is.Null); Assert.That(player.Sensor.NearestDiagnostic, Is.Null);
            Assert.That(player.PresentationOpen || player.Progress.IsHolding, Is.False); Assert.That(P1.TryConsume(scene.RemainingHeal.LootInstanceId), Is.EqualTo(QusapLootResult.Blocked));
            Assert.That(P1.TryMoveToBackpack(scene.Pocket.LootInstanceId), Is.EqualTo(QusapLootResult.Blocked));
            Assert.That(scene.Authority.RequestOpen(new("P1", Death.Record.ContainerId)).Result, Is.EqualTo(QusapWorldLootResult.Blocked));
            Assert.That(P1.GetComponent<QusapWeaponEquipment>().TryEquip(scene.Sword.Weapon, out _), Is.Not.EqualTo(QusapWeaponOperationResult.Success));
            P1.Receiver.ResetForNewSession(); Assert.That(P1.Receiver.IsEliminated, Is.True); Assert.That(P1.State.CanOperate, Is.False);
        }
        [UnityTest] public IEnumerator NewDeathSourceRequiresRealDpadHoldAndDoesNotGenerateOtherGameplayAction()
        {
            yield return KillP1(); var reader = P2.GetComponent<QusapInputReader>();
            var asset = (InputActionAsset)typeof(QusapInputReader).GetField("runtimeActionAsset", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(reader);
            int performed = 0; Action<InputAction.CallbackContext> observe = c => { if (c.performed) performed++; };
            foreach (var map in asset.actionMaps) map.actionTriggered += observe;
            try
            {
                yield return BeginHold(); yield return Tick(.2f); Assert.That(I2.PresentationOpen, Is.False); Assert.That(Death.View.Snapshot.State, Is.EqualTo(QusapWorldLootState.Unopened));
                yield return Tick(.2f); Assert.That(I2.PresentationOpen, Is.True); Assert.That(I2.Progress.Elapsed, Is.GreaterThanOrEqualTo(.45f));
                Assert.That(performed, Is.Zero); Assert.That(reader.HasPendingWeaponSwapThrow, Is.False); Assert.That(scene.Owner.ConfirmedTransfers, Is.Zero);
                Assert.That(Death.View.Snapshot.GenerationCount, Is.Zero); Assert.That(Death.View.Table, Is.Null);
            }
            finally { foreach (var map in asset.actionMaps) map.actionTriggered -= observe; }
        }
        [UnityTest] public IEnumerator StartsOpenedOptionStillRequiresTheFullHoldBeforePresentation()
        {
            scene.Presentation.StartsOpened = true; yield return KillP1(); Assert.That(Death.View.Snapshot.State, Is.EqualTo(QusapWorldLootState.Opened));
            yield return BeginHold(); Assert.That(I2.PresentationOpen, Is.False); yield return Tick(.4f); Assert.That(I2.PresentationOpen, Is.True);
        }
        [UnityTest] public IEnumerator RealDamageAndHitstunCancelDeathHoldWithoutOpening()
        {
            yield return KillP1(); yield return BeginHold(); float before = P2.Receiver.CurrentHealth;
            Assert.That(P2.Receiver.TryReceiveEnvironmentDamage(1, scene), Is.True); Assert.That(P2.Receiver.CurrentHealth, Is.LessThan(before)); Assert.That(I2.Progress.IsHolding, Is.False);
            devices.Release(Gamepad.current.dpad.up); yield return Tick(.06f); yield return BeginHold(); P2.GetComponent<QusapHitstunController>().EnterHitstun(.2f);
            Assert.That(I2.Progress.IsHolding, Is.False); yield return Tick(.4f); Assert.That(I2.PresentationOpen, Is.False);
        }
        [UnityTest] public IEnumerator ReleaseAndRangeExitCancelDeathHoldAndHeldButtonCannotRetry()
        {
            yield return KillP1(); yield return BeginHold(); devices.Release(Gamepad.current.dpad.up); yield return Tick(.1f); Assert.That(I2.Progress.IsHolding, Is.False);
            yield return BeginHold(); Place(P2, new(-6,1.05f,0)); yield return Tick(.05f); Assert.That(I2.Progress.IsHolding, Is.False);
            Place(P2, new(.7f,1.05f,0)); yield return Tick(.5f); Assert.That(I2.PresentationOpen, Is.False);
        }
        [UnityTest] public IEnumerator SolidObstacleBlocksDeathHoldAndOpeningAuthority()
        {
            yield return KillP1(); var wall = GameObject.CreatePrimitive(PrimitiveType.Cube); wall.name = "DeathTestLineOfSightWall"; wall.transform.position = new(0,.9f,0); wall.transform.localScale = new(.2f,1.8f,2); Physics.SyncTransforms();
            devices.Press(Gamepad.current.dpad.up); yield return Tick(.15f); Assert.That(I2.Progress.IsHolding, Is.False); Assert.That(I2.Sensor.Target, Is.Null);
            Assert.That(scene.Authority.RequestOpen(new("P2", Death.Record.ContainerId)).Result, Is.EqualTo(QusapWorldLootResult.OutOfReach));
            UnityEngine.Object.Destroy(wall); yield return null;
        }
        [UnityTest] public IEnumerator FullBackpackKeepsSwordInSourceThenRealConsumptionFreesSlotForSameInstance()
        {
            yield return Open(); string id = scene.Sword.LootInstanceId; long version = Death.View.Snapshot.Version;
            I2.QueueCommand(new(false, takeOne: true)); yield return Tick(.06f); Assert.That(I2.LastReply.Result, Is.EqualTo(QusapWorldLootResult.Full));
            Assert.That(scene.Sword.HolderId, Is.EqualTo(Death.Record.ContainerId)); Assert.That(Death.View.Snapshot.Version, Is.EqualTo(version));
            Assert.That(scene.ConsumeRivalHealing(), Is.EqualTo(QusapLootResult.Success)); I2.QueueCommand(new(false, takeOne: true)); yield return Tick(.06f);
            Assert.That(I2.LastReply.Confirmed, Is.True); Assert.That(I2.LastReply.DestinationInstanceId, Is.EqualTo(id));
            Assert.That(P2.ReadInventorySnapshot().Backpack.Count(i => i.HasValue && i.Value.LootInstanceId == id), Is.EqualTo(1)); Assert.That(scene.Owner.DuplicateInstances, Is.Zero);
        }
        [UnityTest] public IEnumerator RepeatedRequestIsStaleAndDoesNotChangeOwnershipAgain()
        {
            yield return Open(); scene.ConsumeRivalHealing(); var request = I2.CreateTakeRequest(1).Value;
            Assert.That(I2.Submit(request).Confirmed, Is.True); Assert.That(I2.Submit(request).Result, Is.EqualTo(QusapWorldLootResult.StaleRequest));
            Assert.That(scene.Owner.ConfirmedTransfers, Is.EqualTo(1)); Assert.That(scene.Sword.Location, Is.EqualTo(QusapLootLocation.Backpack));
            Assert.That(scene.Custody(scene.Sword.LootInstanceId), Is.EqualTo(new[] { "Equipped/P1", "DeathContainer/" + Death.Record.ContainerId, "Backpack/P2" })); yield return null;
        }
        [UnityTest] public IEnumerator PhysicalExtractionDeliversSwordOnlyToP2AndPocketOnlyToP1AndDestroysAbandonedCargo()
        {
            yield return TakeSword(); var zone = UnityEngine.Object.FindAnyObjectByType<QusapExtractionVolume>(); Place(P2, zone.transform.position); yield return Tick(5.3f);
            Assert.That(P2.GetComponent<QusapRaidExtraction>().Status, Is.EqualTo(QusapExtractionStatus.Extracted)); Assert.That(scene.Session.Status, Is.EqualTo(QusapRaidSessionStatus.Finished));
            Assert.That(P2.ReadStashSnapshot().Count(i => i.LootInstanceId == scene.Sword.LootInstanceId), Is.EqualTo(1)); Assert.That(P1.ReadStashSnapshot().Any(i => i.LootInstanceId == scene.Sword.LootInstanceId), Is.False);
            Assert.That(P1.ReadStashSnapshot().Single().LootInstanceId, Is.EqualTo(scene.Pocket.LootInstanceId)); Assert.That(P2.ReadStashSnapshot().Any(i => i.LootInstanceId == scene.Pocket.LootInstanceId), Is.False);
            Assert.That(scene.Sword.Location, Is.EqualTo(QusapLootLocation.Stash)); Assert.That(scene.Sword.HolderId, Is.EqualTo("memory/P2"));
            Assert.That(Death.View.Snapshot.State, Is.EqualTo(QusapWorldLootState.Depleted)); Assert.That(scene.Raid.Session.World.DeathLootDestroyed, Is.EqualTo(2));
            scene.Raid.Session.FinishSession(); scene.Raid.Session.FinishSession(); P1.RetryDeathSettlement();
            Assert.That(scene.Raid.Session.World.DeathLootDestroyed, Is.EqualTo(2)); Assert.That(scene.Raid.Session.World.LoanersDestroyed, Is.EqualTo(1)); Assert.That(scene.Owner.DuplicateInstances, Is.Zero);
        }
        [UnityTest] public IEnumerator RivalDeathRecordsSameLootInSecondContainerThenRaidEndDestroysIt()
        {
            yield return TakeSword(); Assert.That(P2.Receiver.TryReceiveEnvironmentDamage(1000, scene), Is.True); yield return Tick(.15f);
            var second = scene.Raid.Session.Containers.Single(r => r.ParticipantId == "P2");
            Assert.That(second.EliminationOrdinal, Is.EqualTo(2)); Assert.That(second.InitialTransferredSnapshot.Single(i => i.LootInstanceId == scene.Sword.LootInstanceId).Location, Is.EqualTo(QusapLootLocation.DeathContainer));
            Assert.That(scene.Custody(scene.Sword.LootInstanceId), Does.Contain("DeathContainer/" + second.ContainerId));
            Assert.That(scene.Sword.Location, Is.EqualTo(QusapLootLocation.Destroyed)); Assert.That(scene.Sword.Weapon.IsRetired, Is.True);
            Assert.That(P1.ReadStashSnapshot().Any(i => i.LootInstanceId == scene.Sword.LootInstanceId), Is.False); Assert.That(scene.Raid.ContainerViews.Count, Is.EqualTo(2)); Assert.That(scene.Owner.DuplicateInstances, Is.Zero);
        }
        [UnityTest] public IEnumerator EnclosedDeathFallsBackToLastSafeGroundPositionWithoutMovingTheVictim()
        {
            var safe = P1.GetComponent<QusapDeathLootSafePosition>(); safe.Capture(); Assert.That(safe.LastSafePosition.HasValue, Is.True);
            var wall = GameObject.Find("DeathLoot_SolidObstacle"); Vector3 invalid = wall.transform.position;
            Place(P1, invalid); yield return KillP1();
            Assert.That(Death.Placement.Kind, Is.EqualTo(QusapDeathLootPlacementKind.LastSafePosition));
            Assert.That(P1.GetComponent<Rigidbody>().position, Is.EqualTo(invalid)); Assert.That(Death.Placement.Position.z, Is.Zero);
            Assert.That(Physics.OverlapSphere(Death.Placement.Position, .34f, 1, QueryTriggerInteraction.Ignore)
                .Any(c => c.GetComponentInParent<QusapHitReceiver>() == null && c.GetComponentInParent<QusapDeathLootContainer>() == null), Is.False);
        }
        [UnityTest] public IEnumerator DepletedDeathProjectionRemainsVisibleButCannotStartAnotherHold()
        {
            yield return Open(); scene.ConsumeRivalHealing();
            string rivalMaterial = P2.ReadInventorySnapshot().Backpack.Single(i => i.HasValue).Value.LootInstanceId;
            I2.Select(2); Assert.That(I2.SelectedInstanceId, Is.EqualTo(scene.RemainingHeal.LootInstanceId));
            I2.QueueCommand(new(false, takeStack: true)); yield return Tick(.04f); Assert.That(I2.LastReply.Confirmed, Is.True);
            int remainingUses = scene.RemainingHeal.Quantity;
            for (int use = 0; use < remainingUses; use++)
            {
                Assert.That(P2.Receiver.TryReceiveEnvironmentDamage(1, scene), Is.True);
                Assert.That(P2.TryConsume(scene.RemainingHeal.LootInstanceId), Is.EqualTo(QusapLootResult.Success)); yield return Tick(.03f);
            }
            Assert.That(P2.TryMoveToSecurePocket(rivalMaterial), Is.EqualTo(QusapLootResult.Success));
            // Taking damage correctly closed presentation. Reopen through another complete real hold.
            devices.Press(Gamepad.current.dpad.up); yield return Tick(.55f); devices.Release(Gamepad.current.dpad.up); yield return Tick(.04f);
            Assert.That(I2.PresentationOpen, Is.True);
            int itemsToTake = Death.View.Snapshot.Items.Count;
            for (int item = 0; item < itemsToTake; item++)
            { I2.QueueCommand(new(false, takeStack: true)); yield return Tick(.04f); Assert.That(I2.LastReply.Confirmed, Is.True); }
            I2.QueueCommand(new(false, close: true)); yield return Tick(.04f);
            Assert.That(Death.View.gameObject.activeSelf, Is.True); Assert.That(Death.View.Snapshot.State, Is.EqualTo(QusapWorldLootState.Depleted));
            devices.Press(Gamepad.current.dpad.up); yield return Tick(.5f); Assert.That(I2.Progress.IsHolding, Is.False);
            Assert.That(I2.LastRejection, Is.EqualTo(QusapLootInteractionRejection.Depleted)); Assert.That(scene.Session.Status, Is.EqualTo(QusapRaidSessionStatus.Running));
        }
        [UnityTest] public IEnumerator AllSceneComponentsAndSerializedReferencesArePresent()
        {
            foreach (var root in SceneManager.GetActiveScene().GetRootGameObjects())
                foreach (var component in root.GetComponentsInChildren<Component>(true))
                {
                    Assert.That(component, Is.Not.Null, root.name);
                    var properties = new SerializedObject(component).GetIterator();
                    while (properties.NextVisible(true)) if (properties.propertyType == SerializedPropertyType.ObjectReference
                        && properties.objectReferenceValue == null && !properties.objectReferenceEntityIdValue.Equals(default(UnityEngine.EntityId)))
                        Assert.Fail(component.name + ":" + properties.propertyPath);
                }
            yield return null;
        }
    }
}
#endif
