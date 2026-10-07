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
    public sealed class QusapCombatImpactFeedbackPlayModeTests
    {
        const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;
        InputTestFixture devices; Keyboard keyboard; Gamepad pad; GameObject[] players; object runtime;
        float originalScale, originalFixed;
        static QusapCombatController Combat(GameObject p) => p.GetComponent<QusapCombatController>();
        static QusapCombatFeedbackPresenter Feedback(GameObject p) => Combat(p).CombatFeedbackPresenter;
        static IQusapImpactVisualSource Visual(GameObject p) => p.GetComponents<MonoBehaviour>().OfType<IQusapImpactVisualSource>().Single();
        void Clock()
        {
            runtime.GetType().GetProperty("currentTimeOffsetToRealtimeSinceStartup").SetValue(runtime, 0d);
            runtime.GetType().GetProperty("currentTimeForFixedUpdate").SetValue(runtime, devices.currentTime);
        }
        IEnumerator Tick(float seconds)
        {
            float end = Time.time + seconds;
            while (Time.time < end) { devices.currentTime += Time.deltaTime; Clock(); InputSystem.Update(); yield return null; }
        }
        IEnumerator Button(GameObject p, QusapCombatCommand command)
        {
            if (p == players[0] && command == QusapCombatCommand.WeaponStrong)
            {
                // P1 has no Heavy InputAction binding. Exercise its existing
                // FIFO in the affected fixture without changing controls.
                p.GetComponent<QusapInputReader>().EnqueueCombatCommand(command, InputState.currentTime);
                yield return Tick(.025f); yield break;
            }
            var key = command == QusapCombatCommand.BodyAttack ? keyboard.jKey : command == QusapCombatCommand.WeaponLight ? keyboard.kKey : keyboard.lKey;
            var button = command == QusapCombatCommand.BodyAttack ? pad.buttonWest : command == QusapCombatCommand.WeaponLight ? pad.buttonNorth :
                command == QusapCombatCommand.WeaponStrong ? pad.buttonEast : command == QusapCombatCommand.Parry ? pad.leftTrigger : pad.leftShoulder;
            if (p == players[0]) devices.Press(key); else devices.Press(button);
            yield return Tick(.025f);
            if (p == players[0]) devices.Release(key); else devices.Release(button);
        }
        IEnumerator Face(GameObject p, int side)
        {
            if (p == players[0]) devices.Press(side < 0 ? keyboard.aKey : keyboard.dKey); else devices.Set(pad.leftStick, new Vector2(side, 0));
            yield return Tick(.05f);
            if (p == players[0]) devices.Release(side < 0 ? keyboard.aKey : keyboard.dKey); else devices.Set(pad.leftStick, Vector2.zero);
            yield return Tick(.16f);
        }
        void Place(GameObject a, GameObject b, int side, float y = 1.01f)
        {
            a.GetComponent<Rigidbody>().position = new Vector3(3 - side * .575f, y, 0);
            b.GetComponent<Rigidbody>().position = new Vector3(3 + side * .575f, y, 0);
            foreach (var p in players) p.GetComponent<Rigidbody>().linearVelocity = Vector3.zero;
            Physics.SyncTransforms();
        }
        [UnitySetUp] public IEnumerator SetUp()
        {
            originalScale = Time.timeScale; originalFixed = Time.fixedDeltaTime;
            devices = new InputTestFixture(); devices.Setup(); keyboard = InputSystem.AddDevice<Keyboard>(); pad = InputSystem.AddDevice<Gamepad>();
            runtime = typeof(InputTestFixture).GetProperty("runtime", Private).GetValue(devices);
            runtime.GetType().GetProperty("advanceTimeEachDynamicUpdate").SetValue(runtime, 0d);
            yield return LoadArena();
        }
        IEnumerator LoadArena()
        {
            var load = EditorSceneManager.LoadSceneAsyncInPlayMode("Assets/_Qusap/Scenes/CombatPlayground.unity", new LoadSceneParameters(LoadSceneMode.Single));
            while (!load.isDone) yield return null;
            new GameObject("Impact_TestClock").AddComponent<QusapBodyHeadbuttTestClock>().Synchronize = Clock;
            yield return Tick(.25f);
            players = UnityEngine.Object.FindObjectsByType<QusapInputReader>().OrderBy(p => p.LocalPlayerSlot).Select(p => p.gameObject).ToArray();
            Assert.That(players.Length, Is.EqualTo(2));
        }
        [UnityTearDown] public IEnumerator TearDown()
        {
            foreach (var root in SceneManager.GetActiveScene().GetRootGameObjects()) { root.SetActive(false); UnityEngine.Object.Destroy(root); }
            yield return null; yield return null; devices.TearDown();
            Assert.That(Time.timeScale, Is.EqualTo(originalScale)); Assert.That(Time.fixedDeltaTime, Is.EqualTo(originalFixed));
        }

        [UnityTest] public IEnumerator LightAndHeavyHaveOneFeedbackPerContactForBothPlayersDirectionsAndAir()
        {
            foreach (var a in players) foreach (int side in new[] { -1, 1 }) foreach (bool air in new[] { false, true })
            foreach (var command in new[] { QusapCombatCommand.WeaponLight, QusapCombatCommand.WeaponStrong })
            {
                var b = players.Single(p => p != a);
                Combat(a).ResetCombatState(); Combat(b).ResetCombatState();
                yield return Face(a, side); Place(a, b, side, air ? 2.2f : 1.01f); yield return Tick(.03f);
                int before = Feedback(b).ConfirmedImpactCount, hits = 0; Vector3 contact = Vector3.zero;
                void Hit(QusapHitInfo h) { hits++; contact = QusapCombatFeedbackPresenter.ContactOnTarget(b.GetComponent<QusapHitReceiver>(), h.HitboxCenter); }
                b.GetComponent<QusapHitReceiver>().HitReceived += Hit;
                yield return Button(a, command); yield return Tick(.6f);
                b.GetComponent<QusapHitReceiver>().HitReceived -= Hit;
                Assert.That(hits, Is.EqualTo(1), $"{a.name} side={side} air={air} {command}");
                Assert.That(Feedback(b).ConfirmedImpactCount - before, Is.EqualTo(1));
                Assert.That(Feedback(b).LastImpactType, Is.EqualTo(command == QusapCombatCommand.WeaponLight ? QusapCombatFeedbackType.Light : QusapCombatFeedbackType.Heavy));
                Assert.That(contact, Is.Not.EqualTo(Vector3.zero));
                Assert.That(Feedback(a).ImpactTrail.Trail.emitting, Is.False);
                Assert.That(Visual(a).ImpactSword.GetComponentsInChildren<TrailRenderer>(true).Length, Is.EqualTo(1));
                Assert.That(Time.timeScale, Is.EqualTo(1f));
                yield return Tick(.25f);
            }
        }

        IEnumerator Prefix(GameObject a, GameObject b, QusapComboId combo, int side)
        {
            yield return Face(a, side); Place(a, b, side); yield return Tick(.05f);
            yield return Button(a, combo == QusapComboId.Launch ? QusapCombatCommand.BodyAttack : QusapCombatCommand.WeaponLight);
            yield return Tick(.39f); Place(a, b, side);
            yield return Button(a, QusapCombatCommand.WeaponLight); yield return Tick(.39f); Place(a, b, side);
            if (combo == QusapComboId.Damage) { yield return Button(a, QusapCombatCommand.BodyAttack); yield return Tick(.36f); Place(a, b, side); }
            yield return Button(a, combo == QusapComboId.Damage ? QusapCombatCommand.WeaponStrong : combo == QusapComboId.Disarm ? QusapCombatCommand.Headbutt : QusapCombatCommand.BodyAttack);
            Assert.That(Combat(a).ArmedFinisherCombo, Is.EqualTo(combo));
        }

        [UnityTest] public IEnumerator DamageDisarmLaunchUseOneImpactAndPreserveParryDeadlines()
        {
            int cases = 0;
            foreach (int who in new[] { 0, 1 }) foreach (int side in new[] { -1, 1 }) foreach (var combo in new[] { QusapComboId.Damage, QusapComboId.Disarm, QusapComboId.Launch })
            {
                if (cases++ > 0) yield return LoadArena();
                var a = players[who];
                var b = players.Single(p => p != a); Combat(a).ResetCombatState(); Combat(b).ResetCombatState();
                yield return Prefix(a, b, combo, side);
                double opens = Combat(a).ParryWindowOpensAt, closes = Combat(a).ParryWindowClosesAt;
                int before = Feedback(b).ConfirmedImpactCount;
                float damage = b.GetComponent<QusapHitReceiver>().TotalDamageReceived;
                Assert.That(closes - opens, Is.EqualTo(.35d).Within(.000001));
                Assert.That(Feedback(a).ImpactTrail.Trail.emitting, Is.False, "No finisher trail during startup");
                bool sawTrail = false;
                while (Combat(a).HasArmedFinisher) { sawTrail |= Feedback(a).ImpactTrail.Trail.emitting; yield return Tick(.01f); }
                Assert.That(sawTrail, Is.EqualTo(combo != QusapComboId.Disarm), "Sword finishers use source swing windows; H2 has no sword swing");
                yield return Tick(.12f);
                Assert.That(Feedback(a).ImpactTrail.Trail.emitting, Is.False, "No finisher trail during recovery");
                Assert.That(Feedback(b).ConfirmedImpactCount - before, Is.EqualTo(1));
                Assert.That(Combat(a).LastFinisherResolution.Value.HitApplied, Is.True);
                Assert.That(b.GetComponent<QusapHitReceiver>().TotalDamageReceived - damage, Is.EqualTo(Combat(a).GetFinisherDefinition(combo).Damage));
                Assert.That(Feedback(b).LastImpactType, Is.EqualTo(combo == QusapComboId.Damage ? QusapCombatFeedbackType.Damage : combo == QusapComboId.Disarm ? QusapCombatFeedbackType.Disarm : QusapCombatFeedbackType.Launch));
                yield return Tick(.55f);
            }
        }

        [UnityTest] public IEnumerator SuccessfulAndFailedParryDoNotDuplicateOrSuppressDamageIncorrectly()
        {
            foreach (bool success in new[] { true, false })
            {
                var a = players[0]; var b = players[1]; Combat(a).ResetCombatState(); Combat(b).ResetCombatState();
                yield return Prefix(a, b, QusapComboId.Launch, 1);
                float damage = b.GetComponent<QusapHitReceiver>().TotalDamageReceived;
                int before = Feedback(b).ConfirmedImpactCount;
                if (success) yield return Tick(.14f);
                yield return Button(b, QusapCombatCommand.Parry); yield return Tick(.6f);
                Assert.That(Feedback(b).ConfirmedImpactCount - before, Is.EqualTo(1));
                Assert.That(Feedback(b).LastImpactType, Is.EqualTo(success ? QusapCombatFeedbackType.ParrySucceeded : QusapCombatFeedbackType.Launch));
                Assert.That(b.GetComponent<QusapHitReceiver>().TotalDamageReceived - damage, Is.EqualTo(success ? 0 : 3));
                yield return Tick(.8f);
            }
        }

        [UnityTest] public IEnumerator PresentationPauseCannotChangeAttackTimingOrFixedClock()
        {
            var a = players[0]; var b = players[1];
            Vector3? baseline = null;
            foreach (bool feedback in new[] { false, true })
            {
                Feedback(a).SetImpactFeedbackEnabled(feedback); Feedback(b).SetImpactFeedbackEnabled(feedback);
                Combat(a).ResetCombatState(); Combat(b).ResetCombatState(); yield return Face(a, 1); Place(a, b, 1); yield return Tick(.03f);
                int activeSteps = 0, startupSteps = 0, recoverySteps = 0;
                float started = 0, active = 0, recovery = 0, ended = 0;
                void Phase(QusapAttackVariant variant, QusapAttackPhase phase)
                {
                    if (phase == QusapAttackPhase.Startup) started = Time.fixedTime;
                    if (phase == QusapAttackPhase.Active) active = Time.fixedTime;
                    if (phase == QusapAttackPhase.Recovery) recovery = Time.fixedTime;
                    if (phase == QusapAttackPhase.Idle) ended = Time.fixedTime;
                }
                Combat(a).AttackPhaseChanged += Phase;
                yield return Button(a, QusapCombatCommand.WeaponLight);
                while (Combat(a).IsAttacking)
                {
                    switch (Combat(a).CurrentPhase) { case QusapAttackPhase.Startup: startupSteps++; break; case QusapAttackPhase.Active: activeSteps++; break; case QusapAttackPhase.Recovery: recoverySteps++; break; }
                    Assert.That(Time.timeScale, Is.EqualTo(originalScale)); Assert.That(Time.fixedDeltaTime, Is.EqualTo(originalFixed));
                    yield return Tick(.02f);
                }
                Assert.That(startupSteps, Is.GreaterThan(0)); Assert.That(activeSteps, Is.GreaterThan(0)); Assert.That(recoverySteps, Is.GreaterThan(0));
                Assert.That(Combat(a).GetAttackDefinition(QusapAttackVariant.WeaponLight).StartupTime, Is.GreaterThan(0f));
                Combat(a).AttackPhaseChanged -= Phase;
                Vector3 durations = new(active - started, recovery - active, ended - recovery);
                if (!baseline.HasValue) baseline = durations;
                else Assert.That(Vector3.Distance(baseline.Value, durations), Is.LessThan(.0001f), "Same fixed-step attack durations with feedback off/on");
                yield return Tick(.35f);
            }
        }

        [UnityTest] public IEnumerator FlashRestoresExistingPropertyBlockWithoutCreatingMaterials()
        {
            var a = players[0]; var b = players[1];
            var renderer = Visual(b).ImpactVisualRoot.GetComponentInChildren<SkinnedMeshRenderer>();
            var material = renderer.sharedMaterial;
            int property = Shader.PropertyToID("_BaseColor");
            var block = new MaterialPropertyBlock(); block.SetColor(property, new Color(.22f, .31f, .43f)); block.SetFloat("_ImpactTestSentinel", 17f); renderer.SetPropertyBlock(block);
            renderer.GetPropertyBlock(block); Color originalColor = block.GetColor(property);
            yield return Face(a, 1); Place(a, b, 1); yield return Tick(.03f); yield return Button(a, QusapCombatCommand.WeaponStrong);
            yield return Tick(.5f);
            renderer.GetPropertyBlock(block);
            Assert.That(block.GetColor(property), Is.EqualTo(originalColor));
            Assert.That(block.GetFloat("_ImpactTestSentinel"), Is.EqualTo(17f)); Assert.That(renderer.sharedMaterial, Is.SameAs(material));
            Assert.That(b.GetComponent<QusapHitReactionVisual>().IsImpactFlashActive, Is.False);
        }

        [UnityTest] public IEnumerator ConsecutiveHitsHaveBoundedHoldsAndRestoreCameraAndPoseExactly()
        {
            var world = QusapCombatImpactWorld.GetOrCreate(); var root = Visual(players[0]).ImpactVisualRoot;
            Camera camera = Camera.main; Vector3 cameraPosition = camera.transform.position; Vector3 playerPosition = players[0].transform.position;
            world.Request(root, QusapCombatImpactProfile.For(QusapCombatFeedbackType.Heavy));
            world.BeginCamera(camera); world.EndCamera();
            Assert.That(camera.transform.position, Is.EqualTo(cameraPosition));
            Assert.That(players[0].transform.position, Is.EqualTo(playerPosition));
            yield return Tick(.02f);
            world.Request(root, QusapCombatImpactProfile.For(QusapCombatFeedbackType.Launch));
            Assert.That(world.HoldCount, Is.EqualTo(1));
            yield return Tick(.2f); Assert.That(world.HoldCount, Is.Zero); Assert.That(world.IsShaking, Is.False);
            world.Request(root, QusapCombatImpactProfile.For(QusapCombatFeedbackType.Launch)); world.BeginCamera(camera);
            world.enabled = false;
            Assert.That(world.HoldCount, Is.Zero); Assert.That(Time.timeScale, Is.EqualTo(1f));
            Assert.That(Time.fixedDeltaTime, Is.EqualTo(originalFixed));
        }

        [UnityTest] public IEnumerator DisablingInactiveFlashPreservesExternalTintAndSceneReloadClearsHolds()
        {
            var p = players[0]; var root = Visual(p).ImpactVisualRoot;
            var renderer = root.GetComponentInChildren<SkinnedMeshRenderer>();
            int property = Shader.PropertyToID("_BaseColor");
            var block = new MaterialPropertyBlock(); block.SetColor(property, Color.magenta); renderer.SetPropertyBlock(block);
            renderer.GetPropertyBlock(block); Color original = block.GetColor(property);
            Feedback(p).SetImpactFeedbackEnabled(false); renderer.GetPropertyBlock(block);
            Assert.That(block.GetColor(property), Is.EqualTo(original));
            Feedback(p).SetImpactFeedbackEnabled(true);
            p.GetComponent<QusapHitReactionVisual>().PlayImpactFlash(QusapCombatImpactProfile.For(QusapCombatFeedbackType.Heavy));
            var world = QusapCombatImpactWorld.GetOrCreate(); world.Request(root, QusapCombatImpactProfile.For(QusapCombatFeedbackType.Heavy));
            Camera camera = Camera.main; Vector3 normalCamera = camera.transform.position;
            world.BeginCamera(camera); p.SetActive(false);
            renderer.GetPropertyBlock(block);
            Assert.That(block.GetColor(property), Is.EqualTo(original)); Assert.That(camera.transform.position, Is.EqualTo(normalCamera));
            Assert.That(world.HoldCount, Is.Zero); Assert.That(p.GetComponent<QusapHitReactionVisual>().IsImpactFlashActive, Is.False);
            yield return LoadArena();
            Assert.That(QusapCombatImpactWorld.GetOrCreate().HoldCount, Is.Zero);
            Assert.That(QusapCombatImpactWorld.GetOrCreate().IsShaking, Is.False); Assert.That(Time.timeScale, Is.EqualTo(1f));
        }

        [UnityTest] public IEnumerator TrailStopsAtRecoveryAndDisableAndCannotTurnOnDuringDash()
        {
            var a = players[0]; var b = players[1]; yield return Face(a, 1); Place(a, b, 1); yield return Tick(.03f);
            yield return Button(a, QusapCombatCommand.WeaponLight);
            bool sawTrail = false;
            while (Combat(a).IsAttacking) { sawTrail |= Feedback(a).ImpactTrail.Trail.emitting; yield return Tick(.01f); }
            Assert.That(sawTrail, Is.True); Assert.That(Feedback(a).ImpactTrail.Trail.emitting, Is.False);
            Feedback(a).ImpactTrail.enabled = false; Assert.That(Feedback(a).ImpactTrail.Trail.enabled, Is.False);
            Feedback(a).ImpactTrail.enabled = true;
            yield return Tick(.4f);
            devices.Press(keyboard.leftShiftKey); yield return Tick(.025f); devices.Release(keyboard.leftShiftKey);
            Assert.That(a.GetComponent<QusapDashMotor>().IsDashing, Is.True);
            yield return Button(a, QusapCombatCommand.WeaponLight);
            Assert.That(Combat(a).IsAttacking, Is.False); Assert.That(Feedback(a).ImpactTrail.Trail.emitting, Is.False);
        }
    }
}
#endif
