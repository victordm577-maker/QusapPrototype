#if UNITY_EDITOR
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
    public sealed class QusapRaidIntegratedPlayModeTests
    {
        private InputTestFixture devices;
        private object runtime;
        private QusapRaidIntegratedPlaytest scene;
        private float scale, step;
        private Keyboard key;
        private Gamepad pad;
        private int jumpNumber;
        private QusapRaidInventory P1 => scene.Raid.Participants[0];
        private QusapRaidInventory P2 => scene.Raid.Participants[1];
        private void InputClock()
        {
            runtime.GetType().GetProperty("currentTimeOffsetToRealtimeSinceStartup").SetValue(runtime, 0d);
            runtime.GetType().GetProperty("currentTimeForFixedUpdate").SetValue(runtime, devices.currentTime);
        }
        private IEnumerator Tick(float seconds = .1f)
        {
            float end = Time.time + seconds;
            while (Time.time < end)
            {
                devices.currentTime += Time.deltaTime; InputClock();
                yield return null;
            }
        }
        private void Press(UnityEngine.InputSystem.Controls.ButtonControl button) => devices.Press(button, queueEventOnly: true);
        private void Release(UnityEngine.InputSystem.Controls.ButtonControl button) => devices.Release(button, queueEventOnly: true);
        [UnitySetUp] public IEnumerator Setup()
        {
            scale = Time.timeScale; step = Time.fixedDeltaTime; devices = new InputTestFixture(); devices.Setup();
            key = InputSystem.AddDevice<Keyboard>(); pad = InputSystem.AddDevice<Gamepad>();
            runtime = typeof(InputTestFixture).GetProperty("runtime", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(devices);
            runtime.GetType().GetProperty("advanceTimeEachDynamicUpdate").SetValue(runtime, 0d);
            var load = EditorSceneManager.LoadSceneAsyncInPlayMode("Assets/_Qusap/Scenes/RaidIntegratedPlaytest.unity", new LoadSceneParameters(LoadSceneMode.Single));
            while (!load.isDone) yield return null;
            new GameObject("IntegratedInputTestClock").AddComponent<QusapVitalityTestClock>().Synchronize = InputClock;
            yield return Tick(.2f); scene = Object.FindAnyObjectByType<QusapRaidIntegratedPlaytest>();
            Assert.That(scene, Is.Not.Null); Assert.That(scene.Profiles.Length, Is.EqualTo(2));
        }
        [UnityTearDown] public IEnumerator Cleanup()
        {
            foreach (var root in SceneManager.GetActiveScene().GetRootGameObjects()) { root.SetActive(false); Object.Destroy(root); }
            yield return null; yield return null; devices.TearDown();
            Assert.That(Time.timeScale, Is.EqualTo(scale)); Assert.That(Time.fixedDeltaTime, Is.EqualTo(step));
        }
        private IEnumerator Begin(bool reduced = true, bool manualClock = true)
        {
            Assert.That(scene.SetPreset(reduced), Is.True); scene.SetReady(0, true); scene.SetReady(1, true);
            Assert.That(scene.BeginRaid(), Is.True); yield return Tick(.15f);
            foreach (var actor in scene.Raid.Participants)
            {
                string diagnostic = actor.ParticipantId + " · " + string.Join(",", actor.GetComponents<Behaviour>().Select(b => b.GetType().Name + "=" + b.enabled))
                    + " · input=" + actor.GetComponent<QusapInputReader>().IsGameplayInputBlocked + " · retired=" + actor.Receiver.IsGameplayRetired;
                Assert.That(actor.GetComponent<QusapInputReader>().IsGameplayInputBlocked, Is.False, diagnostic);
                Assert.That(actor.GetComponent<QusapHorizontalMotor>().enabled, Is.True, diagnostic);
            }
            if (manualClock) scene.Raid.GetComponent<QusapRaidTimingDriver>().enabled = false;
        }
        private static void Place(QusapRaidInventory actor, float x, float y = 1.05f)
        { var body = actor.GetComponent<Rigidbody>(); body.position = new(x, y, 0); if (!body.isKinematic) body.linearVelocity = Vector3.zero; Physics.SyncTransforms(); }
        private void Advance(double target)
        { Assert.That(scene.Context.Session.AdvanceTime(target - scene.Context.Session.Timing.ElapsedSeconds), Is.True); scene.Hazard.Process(); }
        private IEnumerator TakeMaterial(int player = 0, bool throughNative = true)
        {
            var actor = scene.Raid.Participants[player]; Place(actor, player == 0 ? -7.5f : 7.5f); yield return Tick(.15f);
            var button = player == 0 ? (UnityEngine.InputSystem.Controls.ButtonControl)key.fKey : pad.dpad.up;
            Press(button); yield return Tick(.6f); Release(button); yield return Tick(.05f);
            Assert.That(scene.Interactors[player].PresentationOpen, Is.True, scene.Interactors[player].LastRejection + " target=" + scene.Interactors[player].Sensor.Target?.SpawnPointId
                + " range=" + scene.Interactors[player].Sensor.Distance + " blocked=" + actor.GetComponent<QusapInputReader>().IsGameplayInputBlocked);
            if (throughNative)
            {
                var take = player == 0 ? (UnityEngine.InputSystem.Controls.ButtonControl)key.tKey : pad.rightStickButton;
                Press(take); yield return Tick(.05f); Release(take); yield return Tick(.05f);
            }
            else scene.Interactors[player].Submit(scene.Interactors[player].CreateTakeRequest().Value);
            Assert.That(actor.ReadInventorySnapshot().Backpack.Any(i => i.HasValue && i.Value.Category == QusapLootCategory.Material), Is.True);
        }
        [UnityTest] public IEnumerator PreparationIsMemoryOnlyAndClockWaitsForBothLoadouts()
        {
            var profiles = scene.Profiles.ToArray(); var revisions = profiles.Select(p => p.Revision).ToArray();
            Assert.That(profiles[0].ProfileId, Is.Not.EqualTo(profiles[1].ProfileId));
            Assert.That(profiles.All(p => p.Storage.SupportsMemoryRollback), Is.True); Assert.That(scene.DiagnosticSeedGrants, Is.EqualTo(2));
            var owned = scene.LoadoutChoices(0).First(i => i.Category == QusapLootCategory.Weapon);
            var heal = scene.LoadoutChoices(0).First(i => i.Category == QusapLootCategory.Consumable);
            Assert.That(scene.ToggleLoadout(0, owned.LootInstanceId), Is.True); Assert.That(scene.ToggleLoadout(0, heal.LootInstanceId), Is.True);
            scene.SetReady(0, true); yield return Tick(.3f);
            Assert.That(scene.Context, Is.Null); Assert.That(scene.Hazard, Is.Null); Assert.That(scene.BeginRaid(), Is.False);
            Assert.That(profiles.Select(p => p.Revision), Is.EqualTo(revisions));
            scene.SetReady(1, true); Assert.That(scene.BeginRaid(), Is.True); yield return Tick(.15f);
            Assert.That(scene.Context.Session.Timing.HasStarted, Is.True);
            Assert.That(P1.ReadInventorySnapshot().EquippedWeapon.Value.LootInstanceId, Is.EqualTo(owned.LootInstanceId));
            Assert.That(P1.ReadInventorySnapshot().Backpack.Any(i => i?.LootInstanceId == heal.LootInstanceId), Is.True);
            Assert.That(P1.Receiver.CurrentHealth, Is.EqualTo(100)); Assert.That(P2.Receiver.CurrentHealth, Is.EqualTo(100));
            Assert.That(scene.Context.Session.World.LoanersCreated, Is.EqualTo(1));
        }
        [UnityTest] public IEnumerator KeyboardAndGamepadExplicitlyOpenTakeAndProtectWithNoDuplicateTransfer()
        {
            yield return Begin(); yield return Tick(.3f); Assert.That(scene.LootOwner.ConfirmedTransfers, Is.Zero);
            yield return TakeMaterial(0); yield return TakeMaterial(1);
            Press(key.bKey); Press(pad.leftStickButton); yield return Tick(.05f);
            Release(key.bKey); Release(pad.leftStickButton); yield return Tick(.05f);
            Assert.That(P1.ReadInventorySnapshot().SecurePocket.HasValue, Is.True); Assert.That(P2.ReadInventorySnapshot().SecurePocket.HasValue, Is.True);
            Assert.That(scene.LootOwner.ConfirmedTransfers, Is.EqualTo(2)); Assert.That(scene.LootOwner.DuplicateInstances, Is.Zero);
            var request = scene.Interactors[0].CreateTakeRequest().Value;
            Assert.That(scene.Interactors[0].Submit(request).Confirmed, Is.True);
            Assert.That(scene.Interactors[0].Submit(request).Result, Is.EqualTo(QusapWorldLootResult.StaleRequest));
            Assert.That(scene.LootOwner.ConfirmedTransfers, Is.EqualTo(3)); Assert.That(scene.LootOwner.DuplicateInstances, Is.Zero);
        }
        [UnityTest] public IEnumerator NativeGamepadLootHoldReleaseAndRepressNeverRequestsPresentationRoll()
        {
            yield return Begin();
            Debug.Log("INTEGRATED_INPUT_LISTENERS " + string.Join(" | ", P2.GetComponentsInChildren<MonoBehaviour>(true)
                .Where(b => b != null).Select(b => b.GetType().FullName + " enabled=" + b.enabled + " active=" + b.gameObject.activeInHierarchy)));
            var presenter = P2.GetComponents<MonoBehaviour>().Single(b => b.GetType().Name == "QusapDoubleLGroundAttackPresenter");
            bool Rolling() => (bool)presenter.GetType().GetProperty("IsRolling").GetValue(presenter);
            foreach (float target in new[] { 10.5f, 7.5f })
            {
                yield return WalkTo(1, target);
                for (int press = 0; press < 2; press++)
                {
                    Press(pad.dpad.up); yield return Tick(.08f);
                    Debug.Log("INTEGRATED_LOOT_HOLD x=" + P2.GetComponent<Rigidbody>().position.x + " rolling=" + Rolling()
                        + " state=" + presenter.GetType().GetProperty("PresentationState").GetValue(presenter));
                    Assert.That(Rolling(), Is.False, "D-pad up must only interact; diagnostic presenter shortcut must not run");
                    yield return Tick(.5f); Assert.That(Rolling(), Is.False);
                    Release(pad.dpad.up); yield return Tick(.15f); Assert.That(Rolling(), Is.False);
                }
                Assert.That(scene.LootOwner.ConfirmedTransfers, Is.Zero);
                Assert.That(scene.Interactors[1].PresentationOpen, Is.EqualTo(target == 7.5f));
            }
            Press(pad.rightStickButton); yield return Tick(.08f); Release(pad.rightStickButton); yield return Tick(.08f);
            Assert.That(scene.LootOwner.ConfirmedTransfers, Is.EqualTo(1)); Assert.That(Rolling(), Is.False);
        }
        [UnityTest] public IEnumerator NativeLtButtonSemanticsReportThresholdHoldReleaseAndRepressIncludingBlockedPreparation()
        {
            var observation = scene.ParryDiagnostic.Players[1];
            Assert.That(observation.Action, Is.Not.Null); Assert.That(observation.PairedPad, Is.SameAs(pad));
            Assert.That(observation.Action.bindings.Any(b => b.effectivePath == "<Gamepad>/leftTrigger"), Is.True);
            devices.Set(pad.leftTrigger, .2f, queueEventOnly: true); yield return Tick(.06f);
            Assert.That(observation.ActionPerformed, Is.Zero);
            devices.Set(pad.leftTrigger, .8f, queueEventOnly: true); yield return Tick(.08f);
            Assert.That(observation.TriggerPresses, Is.EqualTo(1)); Assert.That(observation.ActionPerformed, Is.EqualTo(1));
            Assert.That(observation.CommandsForwarded, Is.Zero, "Preparation blocks the reader, not physical detection");
            yield return Tick(.2f); Assert.That(observation.ActionPerformed, Is.EqualTo(1));
            yield return Begin(); Assert.That(observation.CommandsForwarded, Is.Zero, "Holding through deployment must not synthesize a new press");
            devices.Set(pad.leftTrigger, .1f, queueEventOnly: true); yield return Tick(.08f);
            Assert.That(observation.TriggerReleases, Is.EqualTo(1));
            devices.Set(pad.leftTrigger, .8f, queueEventOnly: true); yield return Tick(.08f);
            Assert.That(observation.CommandsForwarded, Is.EqualTo(1)); Assert.That(observation.AttemptsReported, Is.EqualTo(1));
            Assert.That(P2.GetComponent<QusapCombatController>().LastParryAttemptOutcome, Is.EqualTo(QusapParryAttemptOutcome.NoIncomingFinisher));
            Assert.That(observation.Failures, Is.EqualTo(1)); yield return Tick(.65f);
            Assert.That(observation.TriggerHeld, Is.True); Assert.That(observation.AttemptsReported, Is.EqualTo(1));
            devices.Set(pad.leftTrigger, .1f, queueEventOnly: true); yield return Tick(.08f);
            devices.Set(pad.leftTrigger, .8f, queueEventOnly: true); yield return Tick(.08f);
            Assert.That(observation.TriggerPresses, Is.EqualTo(3)); Assert.That(observation.CommandsForwarded, Is.EqualTo(2));
            Assert.That(observation.AttemptsReported, Is.EqualTo(2)); Assert.That(observation.Failures, Is.EqualTo(2));
            devices.Set(pad.leftTrigger, 0, queueEventOnly: true); yield return Tick(.06f);
            Debug.Log("INTEGRATED_LT_EDGES raw=" + observation.TriggerPresses + "/" + observation.TriggerReleases
                + " performed=" + observation.ActionPerformed + " forwarded=" + observation.CommandsForwarded + " result=" + observation.LastOutcome);
        }
        [UnityTest] public IEnumerator NativeJkjFinisherIsParriedByLtAndDiagnosticObservesRealResolution()
        {
            yield return Begin(); var attacker = P1.GetComponent<QusapCombatController>();
            var defender = P2.GetComponent<QusapCombatController>(); var observation = scene.ParryDiagnostic.Players[1];
            int hits = 0; attacker.AttackHit += (_, _) => hits++;
            Place(P1, -.575f); Place(P2, .575f); yield return Tick(.1f);
            Press(key.jKey); yield return Tick(.025f); Release(key.jKey); yield return Tick(.32f);
            Place(P1, -.575f); Place(P2, .575f);
            Press(key.kKey); yield return Tick(.025f); Release(key.kKey); yield return Tick(.36f);
            Assert.That(hits, Is.EqualTo(2), "Two native setup hits must target the same defender");
            Place(P1, -.575f); Place(P2, .575f);
            Press(key.jKey); yield return Tick(.025f); Release(key.jKey);
            Assert.That(attacker.ArmedFinisherCombo, Is.EqualTo(QusapComboId.Launch));
            double opens = attacker.ParryWindowOpensAt, closes = attacker.ParryWindowClosesAt;
            Assert.That(closes - opens, Is.EqualTo(.35).Within(.00001));
            while (InputState.currentTime < opens + .05) yield return Tick(.005f);
            Assert.That(defender.TryGetCurrentParryCue(out _), Is.True);
            Assert.That(scene.ParryDiagnostic.IncomingDescription(1), Does.Contain("ABIERTA"));
            // Reproduce a real queued device event arriving after a newer fixed-step sample.
            double deviceTimestamp = InputState.currentTime;
            typeof(QusapCombatController).GetMethod("UpdateFinisherDefense", BindingFlags.Instance | BindingFlags.NonPublic)
                .Invoke(attacker, new object[] { deviceTimestamp + .01 });
            InputSystem.QueueDeltaStateEvent(pad.leftTrigger, .8f, deviceTimestamp); yield return Tick(.08f);
            Assert.That(defender.LastParryAttemptOutcome, Is.EqualTo(QusapParryAttemptOutcome.Success));
            Assert.That(observation.CommandsForwarded, Is.EqualTo(1)); Assert.That(observation.AttemptsAccepted, Is.EqualTo(1));
            Assert.That(observation.Successes, Is.EqualTo(1)); Assert.That(observation.Failures, Is.Zero);
            Assert.That(observation.LastResolution, Does.Contain("Éxito resuelto"));
            yield return Tick(.65f); Assert.That(observation.AttemptsReported, Is.EqualTo(1), "Held LT must never retry automatically");
            devices.Set(pad.leftTrigger, .1f, queueEventOnly: true); yield return Tick(.08f);
            devices.Set(pad.leftTrigger, .8f, queueEventOnly: true); yield return Tick(.08f);
            Assert.That(defender.LastParryAttemptOutcome, Is.EqualTo(QusapParryAttemptOutcome.NoIncomingFinisher));
            Assert.That(observation.CommandsForwarded, Is.EqualTo(2)); Assert.That(observation.Failures, Is.EqualTo(1));
            devices.Set(pad.leftTrigger, 0, queueEventOnly: true); yield return Tick(.05f);
            Debug.Log("INTEGRATED_NATIVE_PARRY_SUCCESS combo=J,K,J window=" + opens + ".." + closes
                + " accepted=" + observation.AttemptsAccepted + " successes=" + observation.Successes + " failures=" + observation.Failures);
        }
        [UnityTest] public IEnumerator LateConnectedGamepadIsObservedButNeverMisreportedAsNativePaired()
        {
            InputSystem.RemoveDevice(pad);
            LogAssert.Expect(LogType.Warning, new System.Text.RegularExpressions.Regex("Player2_Qusap has no gamepad available for Player2Gamepad"));
            Assert.That(scene.PrepareNext(), Is.True); yield return Tick(.1f);
            pad = InputSystem.AddDevice<Gamepad>(); yield return Tick(.05f);
            var observation = scene.ParryDiagnostic.Players[1];
            Assert.That(observation.PairedPad, Is.Null); Assert.That(observation.ObservedPad, Is.SameAs(pad));
            devices.Set(pad.leftTrigger, .8f, queueEventOnly: true); yield return Tick(.08f);
            Assert.That(observation.TriggerPresses, Is.EqualTo(1)); Assert.That(observation.ActionPerformed, Is.Zero);
            Assert.That(observation.CommandsForwarded, Is.Zero); Assert.That(observation.CurrentBlock, Does.Contain("NO emparejado"));
            devices.Set(pad.leftTrigger, 0, queueEventOnly: true); yield return Tick(.05f);
        }
        [UnityTest] public IEnumerator SharedPresenterKeepsLegacyReviewShortcutsAndNativeControlsInCombatPlayground()
        {
            var load = EditorSceneManager.LoadSceneAsyncInPlayMode("Assets/_Qusap/Scenes/CombatPlayground.unity", new LoadSceneParameters(LoadSceneMode.Single));
            while (!load.isDone) yield return null;
            yield return Tick(.3f);
            var actors = Object.FindObjectsByType<QusapInputReader>().OrderBy(p => p.LocalPlayerSlot).ToArray();
            var views = actors.Select(a => a.GetComponents<MonoBehaviour>().Single(b => b.GetType().Name == "QusapDoubleLGroundAttackPresenter")).ToArray();
            bool Rolling(int index) => (bool)views[index].GetType().GetProperty("IsRolling").GetValue(views[index]);
            Assert.That(views.All(v => (bool)v.GetType().GetProperty("DiagnosticRollShortcutsEnabled").GetValue(v)), Is.True);
            Press(key.rKey); yield return Tick(.08f); Assert.That(Rolling(0), Is.True); Assert.That(Rolling(1), Is.False);
            Release(key.rKey); yield return Tick(1.3f);
            Press(pad.dpad.up); yield return Tick(.08f); Assert.That(Rolling(1), Is.True); Assert.That(Rolling(0), Is.False);
            Release(pad.dpad.up); yield return Tick(1.3f);
            var body = actors[1].GetComponent<Rigidbody>(); var before = body.position;
            devices.Set(pad.leftStick, Vector2.left, queueEventOnly: true); yield return Tick(.2f);
            devices.Set(pad.leftStick, Vector2.zero, queueEventOnly: true); yield return Tick(.2f);
            Assert.That(body.position.x, Is.LessThan(before.x));
            Press(pad.buttonSouth); yield return Tick(.1f); Assert.That(body.position.y, Is.GreaterThan(before.y + .4f));
            Release(pad.buttonSouth); yield return Tick(.9f);
            Press(pad.rightShoulder); yield return Tick(.04f); Assert.That(actors[1].GetComponent<QusapDashMotor>().IsDashing, Is.True);
            Release(pad.rightShoulder); yield return Tick(.6f);
            Press(pad.buttonWest); yield return Tick(.06f); Assert.That(actors[1].GetComponent<QusapCombatController>().IsAttacking, Is.True);
            Release(pad.buttonWest); yield return Tick(.1f);
        }
        [UnityTest] public IEnumerator BothExitsRespectOpeningDamageReentryAndResolveExactlyOnce()
        {
            foreach (float x in new[] { -9f, 9f })
            {
                yield return Begin(); Place(P1, x); Place(P2, 0); yield return Tick(.15f);
                var extraction = P1.GetComponent<QusapRaidExtraction>(); Advance(19); yield return Tick(.05f);
                Assert.That(extraction.Countdown.Elapsed, Is.Zero); Assert.That(extraction.Status, Is.EqualTo(QusapExtractionStatus.Active));
                Advance(20); yield return Tick(.05f); Assert.That(extraction.Status, Is.EqualTo(QusapExtractionStatus.Extracting));
                Advance(22); yield return Tick(.05f); Assert.That(extraction.Countdown.Elapsed, Is.EqualTo(2).Within(.01));
                P1.Receiver.TryReceiveEnvironmentDamage(10, scene); yield return Tick(.05f);
                Assert.That(extraction.Countdown.Cancellation, Is.EqualTo(QusapExtractionCancellation.Damage)); Assert.That(extraction.Countdown.Elapsed, Is.Zero);
                Advance(23); yield return Tick(.05f); Assert.That(extraction.Status, Is.EqualTo(QusapExtractionStatus.Active));
                Place(P1, x < 0 ? -5 : 5); yield return Tick(.1f); Place(P1, x); yield return Tick(.1f);
                Assert.That(extraction.Status, Is.EqualTo(QusapExtractionStatus.Extracting)); Advance(28); yield return Tick(.15f);
                Assert.That(P1.State.Status, Is.EqualTo(QusapRaidInventoryStatus.Extracted)); Assert.That(scene.Context.Session.Competition.WinnerId, Is.EqualTo("P2"));
                Assert.That(scene.Context.Session.Competition.SessionFinishedEvents, Is.EqualTo(1)); Assert.That(scene.Context.Session.World.LoanersDestroyed, Is.EqualTo(2));
                Assert.That(scene.Context.Session.SettleExtraction(P1.State, P1.WeaponAdapter), Is.EqualTo(QusapLootResult.Blocked));
                Assert.That(scene.Context.Session.Competition.SessionFinishedEvents, Is.EqualTo(1));
                Assert.That(scene.PrepareNext(), Is.True); yield return Tick(.15f);
            }
            Assert.That(scene.DiagnosticSeedGrants, Is.EqualTo(2));
        }
        [UnityTest] public IEnumerator LootDamagePocketVictoryAndSecondRaidKeepProfilesWithoutResidualState()
        {
            yield return Begin(); var profiles = scene.Profiles.ToArray(); var id = scene.Context.RaidId; var actor = P1;
            yield return TakeMaterial();
            var protectedId = P1.ReadInventorySnapshot().Backpack.First(i => i.HasValue).Value.LootInstanceId;
            Assert.That(scene.InventoryAction(0, "pocket", protectedId), Is.EqualTo(QusapLootResult.Success));
            var interactor = scene.Interactors[0]; var cargo = interactor.Observed.Items.First().LootInstanceId;
            Assert.That(interactor.Submit(interactor.CreateTakeRequest().Value).Confirmed, Is.True);
            var loaner = P1.ReadInventorySnapshot().EquippedWeapon.Value.Weapon;
            Place(P1, -10.5f); Place(P2, 0); Advance(70); yield return Tick(.2f);
            Assert.That(actor.State.Status, Is.EqualTo(QusapRaidInventoryStatus.Settled));
            Assert.That(scene.Context.Session.Competition.WinnerId, Is.EqualTo("P2"));
            Assert.That(profiles[0].ReadStashSnapshot(profiles[0].ProfileId).Any(i => i.LootInstanceId == protectedId), Is.True);
            Assert.That(profiles[0].ReadStashSnapshot(profiles[0].ProfileId).Any(i => i.LootInstanceId == cargo), Is.False);
            Assert.That(loaner.IsRetired, Is.True); Assert.That(profiles[1].HasTriumphHorn, Is.True);
            var snapshots = profiles.Select(p => p.ReadStashSnapshot(p.ProfileId).Select(i => i.LootInstanceId).ToArray()).ToArray();
            Assert.That(scene.PrepareNext(), Is.True); yield return Tick(.15f);
            Assert.That(scene.Profiles, Is.EqualTo(profiles)); Assert.That(scene.Context, Is.Null); Assert.That(scene.Hazard, Is.Null);
            Assert.That(scene.Raid.Participants.All(p => p.Receiver.CurrentHealth == 100 && !p.Receiver.IsGameplayRetired), Is.True);
            Assert.That(profiles.Select((p, i) => p.ReadStashSnapshot(p.ProfileId).Select(item => item.LootInstanceId).SequenceEqual(snapshots[i])).All(v => v), Is.True);
            Assert.That(scene.DiagnosticSeedGrants, Is.EqualTo(2)); yield return Begin();
            Assert.That(scene.Context.RaidId, Is.Not.EqualTo(id)); Assert.That(scene.Hazard.StageIndex, Is.EqualTo(-1));
            Assert.That(scene.Hazard.TicksProcessed, Is.Zero); Assert.That(scene.LootOwner.ConfirmedTransfers, Is.Zero);
            Assert.That(scene.Containers.All(v => v.Snapshot.GenerationCount == 0), Is.True);
            Assert.That(P1, Is.Not.SameAs(actor)); Assert.That(profiles[1].TriumphHornGrantsPerformed, Is.EqualTo(1));
        }
        [UnityTest] public IEnumerator ExpirationSettlesTogetherWithoutWinnerOrNewHornAndBlocksFurtherLoot()
        {
            yield return Begin(); Place(P1, -.6f); Place(P2, .6f); Advance(120); yield return Tick(.2f);
            Assert.That(scene.Context.Session.Competition.FinalResult.Reason, Is.EqualTo(QusapRaidSessionFinishReason.Expired));
            Assert.That(scene.Context.Session.Competition.WinnerId, Is.Null); Assert.That(scene.Profiles.All(p => !p.HasTriumphHorn), Is.True);
            Assert.That(scene.Profiles.All(p => !p.HasActiveDeployment), Is.True);
            Assert.That(scene.InventoryAction(0, "pocket", "missing"), Is.EqualTo(QusapLootResult.Blocked));
            Assert.That(scene.PrepareNext(), Is.True); yield return Tick(.1f); Assert.That(scene.DiagnosticSeedGrants, Is.EqualTo(2));
        }
        [UnityTest] public IEnumerator NativeDroppedSwordsRequireExplicitInteractionInThisScene()
        {
            yield return Begin(); var native = scene.Raid.GetComponentInChildren<QusapWeaponMatchBootstrap>();
            if (native == null) native = Object.FindAnyObjectByType<QusapWeaponMatchBootstrap>();
            var equipment = P1.GetComponent<QusapWeaponEquipment>();
            var sword = equipment.EquippedWeapon;
            Assert.That(equipment.TryDisarm(P2.GetComponent<QusapCombatController>()), Is.True);
            yield return Tick(2); Assert.That(equipment.HasWeapon, Is.False);
            var dropped = native.DroppedWeapons.Single(d => d.Weapon == sword); Place(P1, dropped.transform.position.x); yield return Tick(.15f);
            Assert.That(equipment.HasWeapon, Is.False); Press(key.fKey); yield return Tick(.1f); Release(key.fKey);
            Assert.That(equipment.EquippedWeapon, Is.SameAs(sword));
        }
        [UnityTest] public IEnumerator ExistingConsumableAndBackpackEquipmentWorkAlongsideLoot()
        {
            var owned = scene.LoadoutChoices(0).First(i => i.Category == QusapLootCategory.Weapon);
            var healing = scene.LoadoutChoices(0).First(i => i.DefinitionId == "raid_healing");
            scene.ToggleLoadout(0, owned.LootInstanceId); scene.ToggleLoadout(0, healing.LootInstanceId); yield return Begin();
            P1.Receiver.TryReceiveEnvironmentDamage(30, scene); yield return Tick(.05f);
            Assert.That(scene.InventoryAction(0, "consume", healing.LootInstanceId), Is.EqualTo(QusapLootResult.Success));
            Assert.That(P1.Receiver.CurrentHealth, Is.GreaterThan(70));
            Assert.That(scene.InventoryAction(0, "store"), Is.EqualTo(QusapLootResult.Success));
            Assert.That(scene.InventoryAction(0, "equip", owned.LootInstanceId), Is.EqualTo(QusapLootResult.Success));
            Assert.That(P1.ReadInventorySnapshot().EquippedWeapon.Value.LootInstanceId, Is.EqualTo(owned.LootInstanceId));
        }
        private IEnumerator WalkTo(int player, float target)
        {
            var actor = scene.Raid.Participants[player]; var body = actor.GetComponent<Rigidbody>();
            int direction = 0, budget = 180;
            while (budget-- > 0)
            {
                float error = target - body.position.x, speed = body.linearVelocity.x;
                if (Mathf.Abs(error) < .5f && Mathf.Abs(speed) < 2f) break;
                // Brake using the opposite native control; never alter body position or velocity.
                int desired = Mathf.Sign(error) == Mathf.Sign(speed) && Mathf.Abs(error) < speed * speed / 80 + .25f
                    ? -(int)Mathf.Sign(speed) : (int)Mathf.Sign(error);
                if (direction != desired)
                {
                    if (player == 0 && direction != 0) { Release(direction > 0 ? key.dKey : key.aKey); yield return Tick(.02f); }
                    direction = desired;
                    if (player == 0) Press(direction > 0 ? key.dKey : key.aKey);
                    else devices.Set(pad.leftStick, new Vector2(direction, 0));
                }
                yield return Tick(.02f);
            }
            if (player == 0 && direction != 0) Release(direction > 0 ? key.dKey : key.aKey); else if (player == 1) devices.Set(pad.leftStick, Vector2.zero);
            yield return Tick(.1f); Assert.That(budget, Is.GreaterThan(0), "Native walk timed out at " + body.position + " velocity=" + body.linearVelocity);
            Assert.That(body.position.x, Is.EqualTo(target).Within(.65f));
        }
        private IEnumerator JumpTo(int player, float x, float expectedY)
        {
            int number = ++jumpNumber; var before = scene.Raid.Participants[player].GetComponent<Rigidbody>().position;
            var button = player == 0 ? (UnityEngine.InputSystem.Controls.ButtonControl)key.spaceKey : pad.buttonSouth;
            Press(button); yield return Tick(.12f); var takeoff = scene.Raid.Participants[player].GetComponent<Rigidbody>().position;
            yield return WalkTo(player, x); yield return Tick(.7f);
            Release(button); yield return Tick(.12f);
            Assert.That(scene.Raid.Participants[player].GetComponent<Rigidbody>().position.y, Is.EqualTo(expectedY).Within(.2f),
                "Jump " + number + " P" + (player + 1) + " from=" + before + " takeoff=" + takeoff + " target=" + x);
        }
        private IEnumerator DropToFloor(int player)
        {
            var button = player == 0 ? (UnityEngine.InputSystem.Controls.ButtonControl)key.sKey : pad.dpad.down;
            // A drop press is buffered by the existing input reader until a platform consumes it.
            // Stop on the floor rather than leaving an extra press for the next landing.
            for (int i = 0; i < 4 && scene.Raid.Participants[player].GetComponent<Rigidbody>().position.y > 1.3f; i++)
            { Press(button); yield return Tick(.12f); Release(button); yield return Tick(.5f); }
            Assert.That(scene.Raid.Participants[player].GetComponent<Rigidbody>().position.y, Is.EqualTo(1f).Within(.2f));
        }
        [UnityTest] public IEnumerator NativeKeyboardUpperLowerAndFinalRoutesRemainReachableAfterEveryClosure()
        {
            yield return Begin(); yield return JumpTo(0, -8.5f, 3.15f);
            yield return JumpTo(0, -5.5f, 4.6f); yield return JumpTo(0, -2.8f, 5.95f); yield return WalkTo(0, 0);
            yield return WalkTo(1, .5f);
            foreach (double closure in new double[] { 60, 75, 90, 105 })
            {
                Advance(closure); yield return Tick(.1f); yield return DropToFloor(0);
                yield return JumpTo(0, 1.65f, 3.15f); yield return JumpTo(0, 0, 4.6f); yield return JumpTo(0, 0, 5.95f);
                Assert.That(scene.Hazard.CurrentArea.Contains((Vector2)P1.GetComponent<Rigidbody>().position), Is.True);
            }
            Assert.That(P1.Receiver.CurrentHealth, Is.EqualTo(100)); Assert.That(P2.Receiver.CurrentHealth, Is.EqualTo(100));
            Assert.That(P1.GetComponent<Rigidbody>().position.z, Is.EqualTo(0).Within(.001));
        }
        [UnityTest] public IEnumerator NativeGamepadTraversesUpperRouteAndReturnsThroughOneWayPlatforms()
        {
            yield return Begin(); yield return JumpTo(1, 8.5f, 3.15f);
            yield return JumpTo(1, 5.5f, 4.6f); yield return JumpTo(1, 2.8f, 5.95f); yield return WalkTo(1, 0);
            yield return DropToFloor(1); Assert.That(P2.Receiver.CurrentHealth, Is.EqualTo(100));
        }
        [UnityTest] public IEnumerator NativeSwordCombatUsesNormalHealthWhileLootAndHazardAreBound()
        {
            yield return Begin(); yield return TakeMaterial(); Place(P1, -.8f); Place(P2, .8f); yield return Tick(.15f);
            Press(key.kKey); yield return Tick(.35f); Release(key.kKey); yield return Tick(.3f);
            Assert.That(P2.Receiver.CurrentHealth, Is.LessThan(100).And.GreaterThan(0));
            Assert.That(scene.Context.Session.IsFinished, Is.False); Assert.That(scene.LootOwner.ConfirmedTransfers, Is.EqualTo(1));
        }
        [UnityTest] public IEnumerator ClosingDangerInterruptsPhysicalExtractionWithoutGrantingImmunity()
        {
            yield return Begin(); Place(P1, 0); Place(P2, -.5f); Advance(73); yield return Tick(.1f);
            Place(P1, 9); yield return Tick(.15f); var extraction = P1.GetComponent<QusapRaidExtraction>();
            Assert.That(extraction.Status, Is.EqualTo(QusapExtractionStatus.Extracting));
            Advance(74.5); yield return Tick(.05f); Assert.That(extraction.Countdown.Elapsed, Is.EqualTo(1.5).Within(.01));
            Advance(75); yield return Tick(.05f); Assert.That(P1.Receiver.CurrentHealth, Is.EqualTo(88));
            Assert.That(extraction.Countdown.Elapsed, Is.Zero); Assert.That(extraction.Countdown.Cancellation, Is.EqualTo(QusapExtractionCancellation.Damage));
            Assert.That(scene.Context.Session.IsFinished, Is.False);
        }
        [UnityTest] public IEnumerator GamepadInventoryDetailsUseExistingStoreEquipAndConsumeWithoutDuplicateTake()
        {
            var sword = scene.LoadoutChoices(1).First(i => i.Category == QusapLootCategory.Weapon);
            var heal = scene.LoadoutChoices(1).First(i => i.DefinitionId == "raid_healing");
            scene.ToggleLoadout(1, sword.LootInstanceId); scene.ToggleLoadout(1, heal.LootInstanceId); yield return Begin();
            Press(pad.startButton); yield return Tick(.05f); Release(pad.startButton); yield return Tick(.05f);
            Assert.That(scene.DetailsVisible, Is.True); Assert.That(scene.DetailPlayer, Is.EqualTo(1));
            devices.Set(pad.rightStick, Vector2.up, queueEventOnly: true); yield return Tick(.05f);
            devices.Set(pad.rightStick, Vector2.zero, queueEventOnly: true); yield return Tick(.05f);
            Assert.That(P2.ReadInventorySnapshot().EquippedWeapon.HasValue, Is.False);
            devices.Set(pad.rightStick, Vector2.right, queueEventOnly: true); yield return Tick(.05f);
            devices.Set(pad.rightStick, Vector2.zero, queueEventOnly: true); yield return Tick(.05f);
            Press(pad.rightStickButton); yield return Tick(.05f); Release(pad.rightStickButton); yield return Tick(.05f);
            Assert.That(P2.ReadInventorySnapshot().EquippedWeapon.Value.LootInstanceId, Is.EqualTo(sword.LootInstanceId));
            P2.Receiver.TryReceiveEnvironmentDamage(30, scene);
            devices.Set(pad.rightStick, Vector2.left, queueEventOnly: true); yield return Tick(.05f);
            devices.Set(pad.rightStick, Vector2.zero, queueEventOnly: true); yield return Tick(.05f);
            Press(pad.rightStickButton); yield return Tick(.05f); Release(pad.rightStickButton); yield return Tick(.05f);
            Assert.That(P2.Receiver.CurrentHealth, Is.GreaterThan(70)); Assert.That(scene.LootOwner.ConfirmedTransfers, Is.Zero);
        }
    }
}
#endif
