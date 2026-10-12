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
    public sealed class QusapLootInteractionPlayModeTests
    {
        private sealed class Commands : IQusapLootInteractionInput
        {
            public QusapLootInteractionCommand Command;
            public string BindingLabel => "Test replaceable commands";
            public QusapLootInteractionCommand ReadCommand() => Command;
        }
        private InputTestFixture devices;
        private object runtime;
        private QusapRaidLootInteractionPlayground scene;
        private Commands[] inputs;
        private float scale, step;
        private QusapLootInteractor P1 => scene.Interactors[0];
        private QusapLootInteractor P2 => scene.Interactors[1];
        private QusapWorldLootContainerView Common => scene.Containers.Single(c => c.SpawnPointId == "common-01");
        private void Clock() { runtime.GetType().GetProperty("currentTimeOffsetToRealtimeSinceStartup").SetValue(runtime, 0d); runtime.GetType().GetProperty("currentTimeForFixedUpdate").SetValue(runtime, devices.currentTime); }
        private IEnumerator Tick(float seconds = .12f) { float end = Time.time + seconds; while (Time.time < end) { devices.currentTime += Time.deltaTime; Clock(); InputSystem.Update(); yield return null; } }
        [UnitySetUp] public IEnumerator Setup()
        {
            scale = Time.timeScale; step = Time.fixedDeltaTime;
            devices = new InputTestFixture(); devices.Setup(); InputSystem.AddDevice<Keyboard>(); InputSystem.AddDevice<Gamepad>();
            runtime = typeof(InputTestFixture).GetProperty("runtime", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(devices); runtime.GetType().GetProperty("advanceTimeEachDynamicUpdate").SetValue(runtime, 0d);
            var load = EditorSceneManager.LoadSceneAsyncInPlayMode("Assets/_Qusap/Scenes/RaidWorldLootInteractionPlayground.unity", new LoadSceneParameters(LoadSceneMode.Single)); while (!load.isDone) yield return null;
            new GameObject("InteractionTestClock").AddComponent<QusapVitalityTestClock>().Synchronize = Clock; yield return Tick(.3f);
            scene = UnityEngine.Object.FindAnyObjectByType<QusapRaidLootInteractionPlayground>();
            inputs = new[] { new Commands(), new Commands() }; P1.ReplaceInput(inputs[0]); P2.ReplaceInput(inputs[1]);
        }
        [UnityTearDown] public IEnumerator Cleanup()
        {
            foreach (var root in SceneManager.GetActiveScene().GetRootGameObjects()) { root.SetActive(false); UnityEngine.Object.Destroy(root); }
            yield return null; yield return null; devices.TearDown();
            Assert.That(Time.timeScale, Is.EqualTo(scale)); Assert.That(Time.fixedDeltaTime, Is.EqualTo(step));
        }
        private void Place(QusapLootInteractor player, float x, float y = 1.05f)
        { var body = player.GetComponent<Rigidbody>(); body.position = new(x, y, 0); body.linearVelocity = Vector3.zero; Physics.SyncTransforms(); player.Sensor.Scan(); }
        private IEnumerator Begin(QusapLootInteractor player = null)
        { player ??= P1; Place(player, -4); inputs[player == P1 ? 0 : 1].Command = new(true); yield return Tick(.16f); Assert.That(player.Progress.IsHolding, Is.True); }
        private IEnumerator OpenBoth()
        {
            Place(P1, -4.5f); Place(P2, -3.5f); inputs[0].Command = inputs[1].Command = new(true); yield return Tick(.55f);
            Assert.That(P1.PresentationOpen && P2.PresentationOpen, Is.True); inputs[0].Command = inputs[1].Command = default; yield return Tick(.05f);
        }
        [UnityTest] public IEnumerator RealPlayersDetectTheSameNearestCacheAndHaveNoProfileComponents()
        {
            Place(P1, -4.5f); Place(P2, -3.5f); yield return Tick();
            Assert.That(P1.Sensor.Target, Is.SameAs(Common)); Assert.That(P2.Sensor.Target, Is.SameAs(Common));
            Assert.That(scene.Raid.Participants.All(p => p.GetComponent<QusapHorizontalMotor>() != null && p.GetComponent<QusapVerticalMotor>() != null), Is.True);
            Assert.That(UnityEngine.Object.FindAnyObjectByType<QusapLocalProfilePersistence>(), Is.Null);
            Assert.That(scene.Raid.Session.Stash.ReadStashSnapshot(scene.Raid.Participants[0].ProfileId), Is.Empty);
            Assert.That(EditorBuildSettings.scenes.Any(s => s.path.EndsWith("RaidWorldLootInteractionPlayground.unity")), Is.False);
        }
        [UnityTest] public IEnumerator StrictRangeRejectsStartEvenWithHysteresisPrompt()
        {
            Place(P1, -4); yield return Tick(.05f); Place(P1, -6.8f); inputs[0].Command = new(true); yield return Tick(.5f);
            Assert.That(P1.Progress.IsHolding, Is.False); Assert.That(Common.Snapshot.State, Is.EqualTo(QusapWorldLootState.Unopened));
            Assert.That(P1.LastRejection, Is.EqualTo(QusapLootInteractionRejection.OutOfRange));
            Assert.That(scene.Authority.RequestOpen(new(P1.PlayerId, Common.SpawnPointId)).Result, Is.EqualTo(QusapWorldLootResult.OutOfReach));
        }
        [UnityTest] public IEnumerator SolidWallBlocksPromptHoldAndAuthorityOpening()
        {
            Place(P2, 6.2f); inputs[1].Command = new(true); yield return Tick(.55f);
            Assert.That(P2.Sensor.LineOfSight, Is.False); Assert.That(P2.Sensor.Target, Is.Null);
            Assert.That(P2.LastRejection, Is.EqualTo(QusapLootInteractionRejection.NoLineOfSight));
            Assert.That(scene.Authority.RequestOpen(new(P2.PlayerId, "highvalue-01")).Result, Is.EqualTo(QusapWorldLootResult.OutOfReach));
            Assert.That(scene.Owner.RequestOpen(new(P2.PlayerId, "highvalue-01")).Result, Is.EqualTo(QusapWorldLootResult.OutOfReach));
        }
        [UnityTest] public IEnumerator ReleasingBeforeDurationCancelsWithoutOpening()
        { yield return Begin(); inputs[0].Command = default; yield return Tick(.5f); Assert.That(Common.Snapshot.GenerationCount, Is.Zero); Assert.That(P1.Progress.LastCancellation, Is.EqualTo(QusapLootInteractionRejection.Released)); }
        [UnityTest] public IEnumerator LeavingRangeCancelsImmediatelyAndDoesNotRetryHeldButton()
        { yield return Begin(); Place(P1, -7); yield return Tick(.05f); Assert.That(P1.Progress.IsHolding, Is.False); Place(P1, -4); yield return Tick(.55f); Assert.That(Common.Snapshot.GenerationCount, Is.Zero); }
        [UnityTest] public IEnumerator LosingLineOfSightCancelsAndTakingBehindWallIsRejected()
        {
            yield return Begin(); var wall = GameObject.CreatePrimitive(PrimitiveType.Cube); wall.name = "TestWall"; wall.transform.position = new(-4, .8f, 0); wall.transform.localScale = new(.2f, 1.8f, 2); Physics.SyncTransforms();
            yield return Tick(.05f); Assert.That(P1.Progress.IsHolding, Is.False); Assert.That(Common.Snapshot.GenerationCount, Is.Zero); UnityEngine.Object.Destroy(wall);
        }
        [UnityTest] public IEnumerator DamageSignalCancelsSynchronouslyAndHealingDoesNot()
        {
            P1.GetComponent<QusapHitReceiver>().TryReceiveEnvironmentDamage(10, scene);
            yield return Begin(); P1.GetComponent<QusapHitReceiver>().Heal(1); Assert.That(P1.Progress.IsHolding, Is.True);
            Assert.That(P1.GetComponent<QusapHitReceiver>().TryReceiveEnvironmentDamage(5, scene), Is.True);
            Assert.That(P1.Progress.IsHolding, Is.False); Assert.That(P1.LastRejection, Is.EqualTo(QusapLootInteractionRejection.Incapacitated)); yield return Tick(.5f); Assert.That(Common.Snapshot.GenerationCount, Is.Zero);
        }
        [UnityTest] public IEnumerator AuthoritativeHitstunSignalCancelsSynchronously()
        { yield return Begin(); P1.GetComponent<QusapHitstunController>().EnterHitstun(.3f); Assert.That(P1.Progress.IsHolding, Is.False); yield return Tick(.6f); Assert.That(Common.Snapshot.GenerationCount, Is.Zero); }
        [UnityTest] public IEnumerator RealOpponentBodyAttackCancelsHoldThroughDamageAndHitstun()
        {
            Place(P2, -3); devices.Set(Gamepad.current.leftStick, new Vector2(-1, 0)); yield return Tick(.04f);
            devices.Set(Gamepad.current.leftStick, Vector2.zero); yield return Begin();
            float health = P1.GetComponent<QusapHitReceiver>().CurrentHealth; devices.Press(Gamepad.current.buttonWest); yield return Tick(.12f);
            Assert.That(P1.GetComponent<QusapHitReceiver>().CurrentHealth, Is.LessThan(health));
            Assert.That(P1.Progress.IsHolding, Is.False); Assert.That(Common.Snapshot.GenerationCount, Is.Zero);
        }
        [UnityTest] public IEnumerator EliminationCancelsAndCannotOpenOrTake()
        { yield return Begin(); P1.GetComponent<QusapHitReceiver>().TryReceiveEnvironmentDamage(1000, scene); Assert.That(P1.Progress.IsHolding, Is.False); yield return Tick(.15f); Assert.That(P1.GetComponent<QusapHitReceiver>().IsEliminated, Is.True); Assert.That(scene.Authority.RequestOpen(new(P1.PlayerId, Common.SpawnPointId)).Confirmed, Is.False); }
        [UnityTest] public IEnumerator NativeDashCancelsInThePhysicalStep()
        { yield return Begin(); devices.Press(Keyboard.current.leftShiftKey); yield return Tick(.04f); Assert.That(P1.GetComponent<QusapDashMotor>().IsDashing, Is.True); Assert.That(P1.Progress.IsHolding, Is.False); Assert.That(Common.Snapshot.GenerationCount, Is.Zero); }
        [UnityTest] public IEnumerator NativeAttackSignalCancelsOpening()
        { yield return Begin(); devices.Press(Keyboard.current.jKey); yield return Tick(.06f); Assert.That(P1.GetComponent<QusapCombatController>().IsAttacking, Is.True); Assert.That(P1.Progress.IsHolding, Is.False); Assert.That(Common.Snapshot.GenerationCount, Is.Zero); }
        [UnityTest] public IEnumerator RaidFinishSignalCancelsOpeningSynchronously()
        {
            yield return Begin(); scene.Session.TryResolve(P1.PlayerId, QusapRaidParticipantOutcome.Extracted); scene.Session.TryResolve(P2.PlayerId, QusapRaidParticipantOutcome.Extracted);
            Assert.That(scene.Session.Status, Is.EqualTo(QusapRaidSessionStatus.Finished)); Assert.That(P1.Progress.IsHolding, Is.False); Assert.That(P1.LastRejection, Is.EqualTo(QusapLootInteractionRejection.SessionNotRunning)); yield return Tick(.5f); Assert.That(Common.Snapshot.GenerationCount, Is.Zero);
        }
        [UnityTest] public IEnumerator NativeMovementWithinRangeKeepsHoldAndFullHoldOpensOnce()
        {
            yield return Begin(); devices.Press(Keyboard.current.dKey); yield return Tick(.1f); devices.Release(Keyboard.current.dKey);
            Assert.That(P1.Progress.IsHolding, Is.True); Assert.That(P1.GetComponent<Rigidbody>().position.x, Is.GreaterThan(-4));
            yield return Tick(.4f); Assert.That(Common.Snapshot.GenerationCount, Is.EqualTo(1)); yield return Tick(.5f); Assert.That(Common.Snapshot.GenerationCount, Is.EqualTo(1));
        }
        [UnityTest] public IEnumerator TwoHoldsShareIdsQuantitiesVersionAndSameFrameTakeHasOneStaleLoser()
        {
            yield return OpenBoth(); Assert.That(Common.Snapshot.GenerationCount, Is.EqualTo(1));
            Assert.That(P1.Observed.Items.Select(i => (i.LootInstanceId, i.Quantity)), Is.EqualTo(P2.Observed.Items.Select(i => (i.LootInstanceId, i.Quantity)))); Assert.That(P1.Observed.Version, Is.EqualTo(P2.Observed.Version));
            var requested = P1.CreateTakeRequest(1).Value;
            P1.QueueCommand(new(false, takeOne: true)); P2.QueueCommand(new(false, takeOne: true)); yield return Tick(.05f);
            Assert.That(P1.LastReply.Confirmed, Is.True); Assert.That(P2.LastReply.Result, Is.EqualTo(QusapWorldLootResult.StaleRequest));
            Assert.That(scene.Owner.ConfirmedTransfers, Is.EqualTo(1)); Assert.That(scene.Owner.DuplicateInstances, Is.Zero);
            Assert.That(scene.Raid.Participants[0].ReadInventorySnapshot().Backpack.Where(i => i.HasValue).Sum(i => i.Value.Quantity), Is.EqualTo(requested.Quantity));
            Assert.That(scene.Raid.Participants[1].ReadInventorySnapshot().Backpack.All(i => !i.HasValue), Is.True);
            Assert.That(P1.Observed.Items.Select(i => (i.LootInstanceId, i.Quantity)), Is.EqualTo(P2.Observed.Items.Select(i => (i.LootInstanceId, i.Quantity)))); Assert.That(P1.Observed.Version, Is.EqualTo(P2.Observed.Version));
            Assert.That(QusapRaidLootInteractionHud.Result(P2.LastReply), Does.Contain("Solicitud obsoleta"));
        }
        [UnityTest] public IEnumerator DifferentItemsConfirmWithRefreshedContainerVersions()
        {
            for (ulong seed = 0; seed < 1000; seed++)
                if (Common.Table.Resolve(scene.Raid.LootCatalog, scene.Owner.RaidId, Common.SpawnPointId, seed).Length >= 2) { scene.Owner.TrySetSeedBeforeOpening(seed); break; }
            yield return OpenBoth(); P2.Select(1); string second = P2.SelectedInstanceId;
            Assert.That(P1.Submit(P1.CreateTakeRequest().Value).Confirmed, Is.True); P1.Observe(); P2.Observe();
            Assert.That(P2.SelectedInstanceId, Is.EqualTo(second)); Assert.That(P2.Submit(P2.CreateTakeRequest().Value).Confirmed, Is.True); Assert.That(scene.Owner.ConfirmedTransfers, Is.EqualTo(2)); Assert.That(scene.Owner.DuplicateInstances, Is.Zero);
        }
        [UnityTest] public IEnumerator FullBackpackRetainsSourceAndPocketRemainsEmpty()
        {
            yield return OpenBoth(); var inventory = scene.Raid.Participants[0]; var definition = scene.Raid.LootCatalog.Definitions.Single(d => d.DefinitionId == "raid_major_heal");
            while (inventory.ReadInventorySnapshot().Backpack.Any(i => !i.HasValue)) inventory.State.TryPickup(scene.Raid.Session.World.Create(definition, "test capacity").LootInstanceId);
            var before = Common.Snapshot; Assert.That(P1.Submit(P1.CreateTakeRequest().Value).Result, Is.EqualTo(QusapWorldLootResult.Full));
            Assert.That(Common.Snapshot.Version, Is.EqualTo(before.Version)); Assert.That(Common.Snapshot.Items.Select(i => (i.LootInstanceId, i.Quantity)), Is.EqualTo(before.Items.Select(i => (i.LootInstanceId, i.Quantity))));
            Assert.That(inventory.ReadInventorySnapshot().SecurePocket, Is.Null); Assert.That(QusapRaidLootInteractionHud.Result(P1.LastReply), Is.EqualTo("Mochila llena"));
        }
        [UnityTest] public IEnumerator CloseWalkAwayAndReopenPreserveContentAndGeneration()
        {
            yield return OpenBoth(); var before = Common.Snapshot; P1.QueueCommand(new(false, close: true)); yield return Tick(.05f); Assert.That(P1.PresentationOpen, Is.False);
            inputs[0].Command = new(true); yield return Tick(.05f); Assert.That(P1.PresentationOpen, Is.True); inputs[0].Command = default;
            Place(P1, -9); yield return Tick(.05f); Assert.That(P1.PresentationOpen, Is.False); Place(P1, -4); inputs[0].Command = new(true); yield return Tick(.05f);
            Assert.That(P1.PresentationOpen, Is.True); Assert.That(Common.Snapshot.GenerationCount, Is.EqualTo(1)); Assert.That(Common.Snapshot.Version, Is.EqualTo(before.Version));
            Assert.That(Common.Snapshot.Items.Select(i => (i.LootInstanceId, i.Quantity)), Is.EqualTo(before.Items.Select(i => (i.LootInstanceId, i.Quantity))));
        }
        [UnityTest] public IEnumerator DepletedDoesNotStartHoldAndDestroyedTargetCancels()
        {
            yield return OpenBoth(); while (Common.Snapshot.Items.Count > 0) { P1.Observe(); Assert.That(P1.Submit(P1.CreateTakeRequest().Value).Confirmed, Is.True); }
            P1.Close(); inputs[0].Command = new(true); yield return Tick(.2f); Assert.That(P1.Progress.IsHolding, Is.False); Assert.That(P1.LastRejection, Is.EqualTo(QusapLootInteractionRejection.Depleted));
            inputs[0].Command = default; Place(P1, 0); yield return Tick(.05f); inputs[0].Command = new(true); yield return Tick(.15f); Assert.That(P1.Progress.IsHolding, Is.True);
            UnityEngine.Object.Destroy(P1.Sensor.Target.gameObject); yield return Tick(.1f); Assert.That(P1.Progress.IsHolding, Is.False);
        }
        [UnityTest] public IEnumerator SolidMaskAndOneWayUseCurrentCollisionConfiguration()
        {
            Place(P1, -5); var root = new GameObject("TestOneWay"); root.SetActive(false);
            var trigger = root.AddComponent<BoxCollider>(); trigger.isTrigger = true;
            var solid = new GameObject("Solid").AddComponent<BoxCollider>(); solid.transform.SetParent(root.transform); solid.transform.position = new(-4.5f, .8f, 0); solid.size = new(.1f, 1.5f, 2);
            var platform = root.AddComponent<QusapOneWayPlatform>(); typeof(QusapOneWayPlatform).GetField("solidCollider", BindingFlags.Instance | BindingFlags.NonPublic).SetValue(platform, solid);
            root.SetActive(true); Physics.SyncTransforms();
            Assert.That(P1.Sensor.Visible(Common), Is.False);
            foreach (var own in P1.GetComponentsInChildren<Collider>()) Physics.IgnoreCollision(own, solid, true);
            Assert.That(P1.Sensor.Visible(Common), Is.True);
            foreach (var own in P1.GetComponentsInChildren<Collider>()) Physics.IgnoreCollision(own, solid, false);
            solid.gameObject.layer = 8; P1.Sensor.Configure(scene.Containers, mask: 1); Assert.That(P1.Sensor.Visible(Common), Is.True);
            solid.gameObject.layer = 0; Assert.That(P1.Sensor.Visible(Common), Is.False);
            P1.Sensor.Configure(scene.Containers, mask: 0); Assert.That(P1.Sensor.Visible(Common), Is.True);
            UnityEngine.Object.Destroy(root); yield return null;
        }
        [UnityTest] public IEnumerator DiagnosticBridgeReadsFAndDpadUpWithoutChangingNativeAssets()
        {
            Place(P1, -4.5f); Place(P2, -3.5f); P1.ReplaceInput(P1.GetComponent<QusapLootInteractionInputBridge>()); P2.ReplaceInput(P2.GetComponent<QusapLootInteractionInputBridge>());
            devices.Press(Keyboard.current.fKey); devices.Press(Gamepad.current.dpad.up); yield return Tick(.12f);
            Assert.That(P1.Progress.IsHolding && P2.Progress.IsHolding, Is.True); devices.Release(Keyboard.current.fKey); devices.Release(Gamepad.current.dpad.up); yield return Tick(.1f);
            Assert.That(P1.Progress.IsHolding || P2.Progress.IsHolding, Is.False); Assert.That(Common.Snapshot.GenerationCount, Is.Zero);
        }
    }
}
#endif
