#if UNITY_EDITOR
using System;
using System.Collections;
using System.Collections.Generic;
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
    public sealed class QusapLootBindingCorrectionPlayModeTests
    {
        private InputTestFixture devices;
        private object runtime;
        private QusapRaidLootInteractionPlayground scene;
        private QusapWeaponMatchBootstrap native;
        private QusapWeaponEquipment equipment;
        private QusapWeaponInstance sword;
        private ulong revision;
        private int swaps, drops, transitions;
        private float originalScale, originalStep;
        private readonly List<string>[] actions = { new(), new() };
        private InputActionAsset[] assets;
        private Action<InputAction.CallbackContext>[] observers;
        private QusapLootInteractor P1 => scene.Interactors[0];
        private QusapLootInteractor P2 => scene.Interactors[1];
        private QusapWorldLootContainerView Common => scene.Containers.Single(c => c.SpawnPointId == "common-01");
        private void Clock() { runtime.GetType().GetProperty("currentTimeOffsetToRealtimeSinceStartup").SetValue(runtime, 0d); runtime.GetType().GetProperty("currentTimeForFixedUpdate").SetValue(runtime, devices.currentTime); }
        private IEnumerator Tick(float seconds = .12f)
        { float end = Time.time + seconds; while (Time.time < end) { devices.currentTime += Time.deltaTime; Clock(); InputSystem.Update(); yield return null; } }
        [UnitySetUp] public IEnumerator Setup()
        {
            originalScale = Time.timeScale; originalStep = Time.fixedDeltaTime;
            devices = new InputTestFixture(); devices.Setup(); InputSystem.AddDevice<Keyboard>(); InputSystem.AddDevice<Gamepad>();
            runtime = typeof(InputTestFixture).GetProperty("runtime", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(devices);
            runtime.GetType().GetProperty("advanceTimeEachDynamicUpdate").SetValue(runtime, 0d);
            var load = EditorSceneManager.LoadSceneAsyncInPlayMode("Assets/_Qusap/Scenes/RaidWorldLootInteractionPlayground.unity", new LoadSceneParameters(LoadSceneMode.Single));
            while (!load.isDone) yield return null;
            new GameObject("BindingRegressionClock").AddComponent<QusapVitalityTestClock>().Synchronize = Clock; yield return Tick(.3f);
            scene = UnityEngine.Object.FindAnyObjectByType<QusapRaidLootInteractionPlayground>(); native = UnityEngine.Object.FindAnyObjectByType<QusapWeaponMatchBootstrap>();
            equipment = P1.GetComponent<QusapWeaponEquipment>(); sword = native.CreateRaidLootWeapon(QusapWeaponVisualCatalog.BlueDefinitionId);
            Assert.That(equipment.TryEquip(sword, out _), Is.EqualTo(QusapWeaponOperationResult.Success)); revision = equipment.Revision;
            swaps = drops = transitions = 0; equipment.WeaponTransitioned += Transition;
            assets = new[] { P1, P2 }.Select(p => (InputActionAsset)typeof(QusapInputReader).GetField("runtimeActionAsset", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(p.GetComponent<QusapInputReader>())).ToArray();
            observers = new Action<InputAction.CallbackContext>[2];
            for (int p = 0; p < 2; p++)
            {
                int player = p; actions[p].Clear(); observers[p] = context => { if (context.performed) actions[player].Add(context.action.name); };
                foreach (var map in assets[p].actionMaps) map.actionTriggered += observers[p];
                Debug.Log("BINDING_RESOLVED P" + (p + 1) + " " + string.Join("; ", assets[p].Select(a => a.name + "=" + string.Join(",", a.controls.Select(c => c.path)))));
            }
            // Choose reproducible transferable contents before generation, using unchanged approved tables.
            for (ulong seed = 0; seed < 1000; seed++)
                if (Common.Table.Resolve(scene.Raid.LootCatalog, scene.Owner.RaidId, Common.SpawnPointId, seed)[0].Definition.Category != QusapLootCategory.Weapon)
                { Assert.That(scene.Owner.TrySetSeedBeforeOpening(seed), Is.True); break; }
        }
        [UnityTearDown] public IEnumerator Cleanup()
        {
            if (equipment != null) equipment.WeaponTransitioned -= Transition;
            if (assets != null) for (int p = 0; p < assets.Length; p++) if (assets[p] != null) foreach (var map in assets[p].actionMaps) map.actionTriggered -= observers[p];
            foreach (var root in SceneManager.GetActiveScene().GetRootGameObjects()) { root.SetActive(false); UnityEngine.Object.Destroy(root); }
            yield return null; yield return null; devices.TearDown();
            Assert.That(Time.timeScale, Is.EqualTo(originalScale)); Assert.That(Time.fixedDeltaTime, Is.EqualTo(originalStep));
        }
        private void Transition(QusapWeaponTransition transition)
        { transitions++; if (transition.Type == QusapWeaponTransitionType.VoluntarySwapThrow) swaps++; if (transition.Type == QusapWeaponTransitionType.Dropped) drops++; }
        private void Place(QusapLootInteractor player, float x)
        { var body = player.GetComponent<Rigidbody>(); body.position = new(x, 1.05f, 0); body.linearVelocity = Vector3.zero; Physics.SyncTransforms(); player.Sensor.Scan(); }
        private void SameSword()
        {
            Assert.That(equipment.EquippedWeapon, Is.SameAs(sword)); Assert.That(equipment.EquippedWeapon.InstanceId, Is.EqualTo(sword.InstanceId));
            Assert.That(equipment.Revision, Is.EqualTo(revision)); Assert.That(scene.Raid.Participants[0].WeaponAdapter.CurrentWeapon, Is.SameAs(sword));
            Assert.That(swaps, Is.Zero); Assert.That(drops, Is.Zero); Assert.That(transitions, Is.Zero);
            Assert.That(native.DroppedWeapons.Count(v => v.ReleaseType == QusapWeaponReleaseType.VoluntarySwapThrow), Is.Zero);
            Assert.That(actions[0], Does.Not.Contain("WeaponSwapThrow"));
        }
        private void Candidate()
        {
            var white = native.CreateRaidLootWeapon(QusapWeaponVisualCatalog.WhiteDefinitionId);
            var create = typeof(QusapWeaponMatchBootstrap).GetMethod("TryCreateInitialDroppedWeapon", BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.That((bool)create.Invoke(native, new object[] { white, new Vector3(P1.GetComponent<Rigidbody>().position.x + .3f, .35f, 0), Time.timeAsDouble }), Is.True);
        }
        [UnityTest] public IEnumerator FAndDpadUpTriggerOnlyInteractionAndNoOtherPlayerAction()
        {
            Place(P1, -4.5f); Place(P2, -.5f); Candidate();
            var p2Equipment = P2.GetComponent<QusapWeaponEquipment>(); var p2Sword = native.CreateRaidLootWeapon(QusapWeaponVisualCatalog.BlueDefinitionId);
            Assert.That(p2Equipment.TryEquip(p2Sword, out _), Is.EqualTo(QusapWeaponOperationResult.Success));
            int p2Transitions = 0; p2Equipment.WeaponTransitioned += _ => p2Transitions++;
            Assert.That(assets[0].SelectMany(a => a.controls).Any(c => c == Keyboard.current.fKey), Is.False);
            Assert.That(assets[1].SelectMany(a => a.controls).Any(c => c == Gamepad.current.dpad.up || c == Gamepad.current.dpad), Is.False);
            actions[0].Clear(); actions[1].Clear();
            devices.Press(Keyboard.current.fKey); devices.Press(Gamepad.current.dpad.up); yield return Tick(.12f);
            Assert.That(P1.Progress.IsHolding && P2.Progress.IsHolding, Is.True);
            Assert.That(actions[0], Is.Empty); Assert.That(actions[1], Is.Empty); SameSword();
            Assert.That(p2Equipment.EquippedWeapon, Is.SameAs(p2Sword)); Assert.That(p2Transitions, Is.Zero);
            Assert.That(P1.GetComponent<QusapInputReader>().HasPendingWeaponSwapThrow || P2.GetComponent<QusapInputReader>().HasPendingWeaponSwapThrow, Is.False);
            devices.Release(Keyboard.current.fKey); devices.Release(Gamepad.current.dpad.up); yield return Tick(.1f);
            Assert.That(actions[0], Is.Empty); Assert.That(actions[1], Is.Empty); SameSword();
        }
        [UnityTest] public IEnumerator SameSwordAfterRangeCancellationCompletedOpeningAndLootTransfer()
        {
            Place(P1, -6.3f); Candidate(); devices.Press(Keyboard.current.fKey); yield return Tick(.12f); Assert.That(P1.Progress.IsHolding, Is.True); SameSword();
            Place(P1, -7.2f); yield return Tick(.06f); Assert.That(P1.Progress.IsHolding, Is.False); Assert.That(Common.Snapshot.GenerationCount, Is.Zero); SameSword();
            devices.Release(Keyboard.current.fKey); yield return Tick(.06f); Place(P1, -4.5f); devices.Press(Keyboard.current.fKey); yield return Tick(.55f);
            Assert.That(Common.Snapshot.GenerationCount, Is.EqualTo(1)); Assert.That(P1.PresentationOpen, Is.True); SameSword();
            devices.Release(Keyboard.current.fKey); P1.QueueCommand(new(false, takeOne: true)); yield return Tick(.06f);
            Assert.That(P1.LastReply.Confirmed, Is.True); Assert.That(P1.LastReply.ConfirmedQuantity, Is.EqualTo(1)); SameSword();
            Assert.That(scene.Raid.Participants[0].ReadInventorySnapshot().Backpack.Any(i => i.HasValue), Is.True);
        }
        [UnityTest] public IEnumerator SameSwordAfterDamageAndHitstunCancellation()
        {
            Place(P1, -4.5f); Candidate(); devices.Press(Keyboard.current.fKey); yield return Tick(.12f);
            P1.GetComponent<QusapHitReceiver>().TryReceiveEnvironmentDamage(5, scene); Assert.That(P1.Progress.IsHolding, Is.False); SameSword();
            devices.Release(Keyboard.current.fKey); yield return Tick(.06f); devices.Press(Keyboard.current.fKey); yield return Tick(.12f);
            Assert.That(P1.Progress.IsHolding, Is.True); P1.GetComponent<QusapHitstunController>().EnterHitstun(.2f); Assert.That(P1.Progress.IsHolding, Is.False); SameSword();
            yield return Tick(.3f); Assert.That(Common.Snapshot.GenerationCount, Is.Zero); SameSword();
        }
        [UnityTest] public IEnumerator RealOpponentBodyAttackCancelsFAndKeepsTheEquippedSword()
        {
            Place(P1, -.6f); Place(P2, .6f); devices.Set(Gamepad.current.leftStick, new Vector2(-1, 0)); yield return Tick(.04f);
            devices.Set(Gamepad.current.leftStick, Vector2.zero); yield return Tick(.06f); actions[0].Clear();
            float health = P1.GetComponent<QusapHitReceiver>().CurrentHealth; devices.Press(Keyboard.current.fKey); yield return Tick(.12f);
            Assert.That(P1.Progress.IsHolding, Is.True); devices.Press(Gamepad.current.buttonWest); yield return Tick(.12f);
            Assert.That(P1.GetComponent<QusapHitReceiver>().CurrentHealth, Is.LessThan(health)); Assert.That(P1.Progress.IsHolding, Is.False); SameSword();
            Assert.That(actions[1], Does.Contain("WeakKick")); Assert.That(actions[0], Is.Empty);
        }
        [UnityTest] public IEnumerator NativeEStillPerformsSwapThrowAndDoesNotStartInteraction()
        {
            Place(P1, -4.5f); Candidate(); devices.Press(Keyboard.current.fKey); yield return Tick(.12f); SameSword();
            devices.Release(Keyboard.current.fKey); yield return Tick(.06f); devices.Press(Keyboard.current.eKey); yield return Tick(.12f);
            Assert.That(actions[0], Does.Contain("WeaponSwapThrow")); Assert.That(swaps, Is.EqualTo(1));
            Assert.That(equipment.EquippedWeapon, Is.Not.SameAs(sword)); Assert.That(P1.Progress.IsHolding, Is.False);
            Assert.That(native.DroppedWeapons.Any(v => v.ReleaseType == QusapWeaponReleaseType.VoluntarySwapThrow), Is.True);
        }
    }
}
#endif
