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
    [DefaultExecutionOrder(-10000)]
    public sealed class QusapVitalityTestClock : MonoBehaviour
    {
        public Action Synchronize;
        private void FixedUpdate() => Synchronize?.Invoke();
    }

    public sealed class QusapVitalityPlayModeTests
    {
        private const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;
        private InputTestFixture devices;
        private Keyboard keyboard;
        private Gamepad gamepad;
        private object runtime;
        private GameObject[] players;
        private float initialScale, initialFixed;
        private QusapDamageVolume volume;
        private QusapVitalityDebugHud hud;
        private static QusapHitReceiver Receiver(GameObject p) => p.GetComponent<QusapHitReceiver>();
        private static QusapCombatController Combat(GameObject p) => p.GetComponent<QusapCombatController>();

        private void Clock()
        {
            runtime.GetType().GetProperty("currentTimeOffsetToRealtimeSinceStartup").SetValue(runtime, 0d);
            runtime.GetType().GetProperty("currentTimeForFixedUpdate").SetValue(runtime, devices.currentTime);
        }
        private IEnumerator Tick(float seconds)
        {
            float end = Time.time + seconds;
            while (Time.time < end) { devices.currentTime += Time.deltaTime; Clock(); InputSystem.Update(); yield return null; }
        }
        private static void Place(GameObject player, Vector3 position)
        {
            player.GetComponent<Rigidbody>().position = position;
            player.GetComponent<Rigidbody>().linearVelocity = Vector3.zero;
            Physics.SyncTransforms();
        }

        [UnitySetUp] public IEnumerator SetUp()
        {
            initialScale = Time.timeScale; initialFixed = Time.fixedDeltaTime;
            devices = new InputTestFixture(); devices.Setup();
            keyboard = InputSystem.AddDevice<Keyboard>(); gamepad = InputSystem.AddDevice<Gamepad>();
            runtime = typeof(InputTestFixture).GetProperty("runtime", Private).GetValue(devices);
            runtime.GetType().GetProperty("advanceTimeEachDynamicUpdate").SetValue(runtime, 0d);
            var load = EditorSceneManager.LoadSceneAsyncInPlayMode("Assets/_Qusap/Scenes/CombatPlayground.unity", new LoadSceneParameters(LoadSceneMode.Single));
            while (!load.isDone) yield return null;
            new GameObject("Vitality_TestClock").AddComponent<QusapVitalityTestClock>().Synchronize = Clock;
            yield return Tick(0.2f);
            var arena = UnityEngine.Object.FindAnyObjectByType<QusapCombatArenaController>();
            players = new[] { arena.PlayerOne.gameObject, arena.PlayerTwo.gameObject };
            var zone = new GameObject("Vitality_TestDamageVolume", typeof(BoxCollider), typeof(QusapDamageVolume));
            zone.transform.position = new Vector3(8, 0.65f, 0);
            var box = zone.GetComponent<BoxCollider>();
            box.isTrigger = true; box.size = new Vector3(3, 1.3f, 2);
            volume = zone.GetComponent<QusapDamageVolume>();
            typeof(QusapDamageVolume).GetField("damage", Private).SetValue(volume, 20f);
            hud = UnityEngine.Object.FindAnyObjectByType<QusapVitalityDebugHud>();
            Assert.That(volume, Is.Not.Null); Assert.That(hud, Is.Not.Null);
        }

        [UnityTearDown] public IEnumerator TearDown()
        {
            Assert.That(Time.timeScale, Is.EqualTo(initialScale));
            Assert.That(Time.fixedDeltaTime, Is.EqualTo(initialFixed));
            foreach (var root in SceneManager.GetActiveScene().GetRootGameObjects())
            { root.SetActive(false); UnityEngine.Object.Destroy(root); }
            yield return null; yield return null; devices.TearDown();
        }

        [UnityTest] public IEnumerator BothPlayersBeginFullAndHudObservesHealthAndElimination()
        {
            foreach (var p in players) Assert.That(Receiver(p).CurrentHealth, Is.EqualTo(100));
            Assert.That(hud.PlayerLabel(0), Does.Contain("100 / 100"));
            Receiver(players[0]).TryReceiveEnvironmentDamage(25, volume);
            Assert.That(hud.PlayerLabel(0), Does.Contain("75 / 100"));
            Assert.That(hud.PlayerLabel(1), Does.Contain("100 / 100"));
            Receiver(players[0]).TryReceiveEnvironmentDamage(75, volume);
            yield return Tick(0.05f);
            Assert.That(hud.PlayerLabel(0), Does.Contain("ELIMINADO"));
        }

        [UnityTest] public IEnumerator NativeLightHeavyAndBodyHitboxesReduceHealth()
        {
            // Knockback can reach the diagnostic hazard; isolate native combat damage.
            volume.gameObject.SetActive(false);
            var attacker = players[0]; var target = players[1];
            foreach (var command in new[] { QusapCombatCommand.WeaponLight, QusapCombatCommand.WeaponStrong, QusapCombatCommand.BodyAttack })
            {
                Combat(attacker).ResetCombatState(); Combat(target).ResetCombatState();
                Place(attacker, new Vector3(2.425f, 1.01f, 0)); Place(target, new Vector3(3.575f, 1.01f, 0));
                target.GetComponent<QusapHitstunController>().ResetHitstun();
                yield return Tick(0.04f);
                float before = Receiver(target).CurrentHealth;
                attacker.GetComponent<QusapInputReader>().EnqueueCombatCommand(command, InputState.currentTime);
                yield return Tick(0.65f);
                Assert.That(Receiver(target).CurrentHealth, Is.EqualTo(before - 1), command.ToString());
            }
        }

        [UnityTest] public IEnumerator ExistingAirHeadbuttAndAllFinisherPacketsUseHealthAuthority()
        {
            var a = Combat(players[0]); var r = Receiver(players[1]);
            var headbutt = a.GetAttackDefinition(QusapAttackVariant.DiveHeadbuttAir);
            Assert.That(headbutt.Damage, Is.EqualTo(12));
            Assert.That(a.GetAttackDefinition(QusapAttackVariant.HeadbuttGround).Damage, Is.Zero,
                "Standalone ground headbutt keeps its approved zero damage.");
            Assert.That(r.TryReceiveHit(new QusapHitInfo(a, headbutt.AttackType, QusapAttackVariant.DiveHeadbuttAir,
                headbutt.Damage, 1, headbutt.HorizontalKnockback, headbutt.VerticalKnockback, headbutt.HitstunDuration, Vector3.zero)), Is.True);
            Assert.That(r.CurrentHealth, Is.EqualTo(88));
            foreach (var combo in new[] { QusapComboId.Damage, QusapComboId.Disarm, QusapComboId.Launch })
            {
                float before = r.CurrentHealth; var data = a.GetFinisherDefinition(combo);
                Assert.That(r.TryReceiveFinisher(new QusapFinisherHitInfo(a, combo, data.Damage, 1,
                    data.HorizontalKnockback, data.VerticalKnockback, data.HitstunDuration, data.RequestsDisarm)), Is.True);
                Assert.That(r.CurrentHealth, Is.EqualTo(before - data.Damage));
            }
            yield return Tick(0.05f);
        }

        [UnityTest] public IEnumerator PhysicalEnvironmentEntryExitAndMultipleCollidersDoNotDoubleDamage()
        {
            var p = players[0]; var r = Receiver(p);
            var child = new GameObject("ExtraContact_TestOnly", typeof(BoxCollider)); child.transform.SetParent(p.transform, false);
            child.GetComponent<BoxCollider>().size = Vector3.one * 0.3f;
            QusapHealthChange? last = null; r.HealthChanged += change => last = change;
            Place(p, new Vector3(8, 1.01f, 0)); yield return Tick(0.10f);
            Assert.That(r.TotalDamageReceived, Is.EqualTo(volume.Damage));
            Assert.That(last.Value.Source, Is.EqualTo(QusapHealthChangeSource.Environment));
            Assert.That(last.Value.SourceObject, Is.SameAs(volume));
            yield return Tick(1.05f); Assert.That(r.TotalDamageReceived, Is.EqualTo(volume.Damage * 2));
            Place(p, new Vector3(5, 1.01f, 0)); yield return Tick(0.10f);
            float damage = r.TotalDamageReceived;
            yield return Tick(1.1f); Assert.That(r.TotalDamageReceived, Is.EqualTo(damage));
        }

        [UnityTest] public IEnumerator OncePerEntryVolumeAndDefaultNoKnockbackWork()
        {
            typeof(QusapDamageVolume).GetField("mode", Private).SetValue(volume, QusapDamageVolumeMode.OncePerEntry);
            var p = players[0]; var r = Receiver(p); var body = p.GetComponent<Rigidbody>();
            body.linearVelocity = new Vector3(2, 3, 0);
            var velocity = body.linearVelocity;
            r.TryReceiveEnvironmentDamage(5, volume);
            Assert.That(body.linearVelocity, Is.EqualTo(velocity));
            Place(p, new Vector3(8, 1.01f, 0)); yield return Tick(1.15f);
            Assert.That(r.TotalDamageReceived, Is.EqualTo(5 + volume.Damage));
            Place(p, new Vector3(5, 1.01f, 0)); yield return Tick(0.10f);
            Place(p, new Vector3(8, 1.01f, 0)); yield return Tick(0.10f);
            Assert.That(r.TotalDamageReceived, Is.EqualTo(5 + volume.Damage * 2));
        }

        [UnityTest] public IEnumerator DisabledEnvironmentFixtureDoesNotDamageOverlappingPlayers()
        {
            volume.enabled = false;
            Place(players[0], new Vector3(8, 1.01f, 0));
            yield return Tick(1.1f);
            Assert.That(Receiver(players[0]).TotalDamageReceived, Is.Zero);
            Place(players[0], new Vector3(5, 1.01f, 0)); yield return Tick(0.1f);
            volume.enabled = true;
            Place(players[0], new Vector3(8, 1.01f, 0)); yield return Tick(0.1f);
            Assert.That(Receiver(players[0]).TotalDamageReceived, Is.EqualTo(volume.Damage));
        }

        [UnityTest] public IEnumerator PartialHealingUpdatesHudAndCannotReviveAnEliminatedPlayer()
        {
            var p = players[0]; var r = Receiver(p);
            Assert.That(r.Heal(25), Is.Zero);
            r.TryReceiveEnvironmentDamage(40, volume);
            Assert.That(r.Heal(25), Is.EqualTo(25));
            Assert.That(r.CurrentHealth, Is.EqualTo(85));
            Assert.That(hud.PlayerLabel(0), Does.Contain("85 / 100"));
            Assert.That(r.Heal(1000), Is.EqualTo(15));
            Assert.That(r.TotalDamageReceived, Is.Zero);
            Assert.That(r.CurrentHealth, Is.EqualTo(r.MaxHealth));
            r.TryReceiveEnvironmentDamage(100, volume); yield return Tick(0.05f);
            Assert.That(r.Heal(25), Is.Zero);
            Assert.That(r.CurrentHealth, Is.Zero);
            Assert.That(hud.PlayerLabel(0), Does.Contain("ELIMINADO"));
        }

        [UnityTest] public IEnumerator DepletionDefersRemovalThenBlocksEveryActionAndCollisionExactlyOnce()
        {
            var p = players[0]; var r = Receiver(p); var input = p.GetComponent<QusapInputReader>();
            int eliminations = 0, depleted = 0;
            r.Eliminated += _ => eliminations++;
            r.HealthDepleted += _ => { depleted++; Assert.That(r.IsEliminated, Is.False); Assert.That(p.GetComponent<Collider>().enabled, Is.True); };
            r.TryReceiveEnvironmentDamage(150, volume);
            Assert.That(r.IsEliminated, Is.False);
            yield return Tick(0.05f);
            var position = p.GetComponent<Rigidbody>().position;
            Assert.That(r.IsEliminated, Is.True); Assert.That(r.AcceptsHits, Is.False);
            Assert.That(r.CurrentHealth, Is.Zero); Assert.That(eliminations, Is.EqualTo(1)); Assert.That(depleted, Is.EqualTo(1));
            Assert.That(p.GetComponentsInChildren<Collider>(true).All(c => !c.enabled), Is.True);
            Assert.That(p.GetComponent<Rigidbody>().detectCollisions, Is.False);
            Assert.That(p.GetComponent<Rigidbody>().isKinematic, Is.True);
            input.SetGameplayInputBlocked(false); // Hitstun cleanup cannot release terminal blocking.
            Assert.That(input.IsGameplayInputBlocked, Is.True);
            int parries = 0;
            Combat(p).ParryAttemptAccepted += _ => parries++;
            devices.Press(keyboard.dKey); devices.Press(keyboard.spaceKey);
            devices.Press(keyboard.leftShiftKey); devices.Press(keyboard.eKey);
            devices.Press(keyboard.jKey); devices.Press(keyboard.kKey); devices.Press(keyboard.lKey);
            yield return Tick(0.2f);
            Assert.That(p.GetComponent<Rigidbody>().position, Is.EqualTo(position));
            Assert.That(input.HorizontalValue, Is.Zero);
            Assert.That(Combat(p).IsAttacking, Is.False);
            Assert.That(p.GetComponent<QusapDashMotor>().IsDashing, Is.False);
            Assert.That(parries, Is.Zero);
            Assert.That(Combat(p).IsParryWindowOpen, Is.False);
            devices.Release(keyboard.dKey); devices.Release(keyboard.spaceKey);
            devices.Release(keyboard.leftShiftKey); devices.Release(keyboard.eKey);
            devices.Release(keyboard.jKey); devices.Release(keyboard.kKey); devices.Release(keyboard.lKey);
            foreach (QusapCombatCommand command in Enum.GetValues(typeof(QusapCombatCommand)))
                Assert.That(input.EnqueueCombatCommand(command, InputState.currentTime).PressId, Is.Zero);
            foreach (QusapAttackType attack in Enum.GetValues(typeof(QusapAttackType)))
                Assert.That(Combat(p).TryStartAttack(attack), Is.False);
            Assert.That(input.ConsumeJumpPressed(), Is.False); Assert.That(input.ConsumeDashPressed(), Is.False);
            Assert.That(p.GetComponent<QusapHitstunController>().IsInHitstun, Is.False);
            foreach (var type in new[] { typeof(QusapHorizontalMotor), typeof(QusapVerticalMotor), typeof(QusapDashMotor), typeof(QusapCombatController), typeof(QusapRespawnController), typeof(QusapWeaponEquipment) })
                Assert.That(((Behaviour)p.GetComponent(type)).enabled, Is.False, type.Name);
            Assert.That(r.Heal(100), Is.Zero); Assert.That(r.TryReceiveEnvironmentDamage(10, volume), Is.False);
            p.GetComponent<QusapRespawnController>().Respawn();
            yield return Tick(0.2f);
            Assert.That(p.GetComponent<Rigidbody>().position, Is.EqualTo(position));
            Assert.That(eliminations, Is.EqualTo(1));
            Place(players[1], position); yield return Tick(0.1f);
            Assert.That(players[1].GetComponent<Rigidbody>().position.x, Is.EqualTo(position.x).Within(0.01f), "No eliminated body collision.");
        }

        [UnityTest] public IEnumerator FallingBelowMinusEightPreservesHealthAndInventoryWithoutElimination()
        {
            var p = players[0]; var r = Receiver(p); var recovery = p.GetComponent<QusapRespawnController>();
            var weapon = p.GetComponent<QusapWeaponEquipment>().EquippedWeapon;
            int recovered = 0, eliminated = 0; QusapRecoveryCause? cause = null;
            recovery.RecoveryCompleted += value => { recovered++; cause = value; }; r.Eliminated += _ => eliminated++;
            r.TryReceiveEnvironmentDamage(37, volume);
            Place(p, new Vector3(-5, -9, 0)); yield return Tick(0.10f);
            Assert.That(r.CurrentHealth, Is.EqualTo(63)); Assert.That(r.TotalDamageReceived, Is.EqualTo(37));
            Assert.That(p.GetComponent<Rigidbody>().position.y, Is.GreaterThan(-8));
            Assert.That(p.GetComponent<QusapWeaponEquipment>().EquippedWeapon, Is.SameAs(weapon));
            Assert.That(cause, Is.EqualTo(QusapRecoveryCause.TechnicalRecovery)); Assert.That(recovered, Is.EqualTo(1)); Assert.That(eliminated, Is.Zero);
        }

        [UnityTest] public IEnumerator EliminatedGamepadPlayerRejectsNativeMovementJumpDashAttackAndParry()
        {
            var p = players[1]; var receiver = Receiver(p); var combat = Combat(p);
            var input = p.GetComponent<QusapInputReader>();
            int parries = 0; combat.ParryAttemptAccepted += _ => parries++;
            receiver.TryReceiveEnvironmentDamage(100, volume); yield return Tick(0.05f);
            var position = p.GetComponent<Rigidbody>().position;
            devices.Set(gamepad.leftStick, Vector2.right);
            devices.Press(gamepad.buttonSouth); devices.Press(gamepad.rightShoulder);
            devices.Press(gamepad.buttonWest); devices.Press(gamepad.buttonEast);
            devices.Press(gamepad.buttonNorth); devices.Press(gamepad.leftTrigger);
            yield return Tick(0.2f);
            Assert.That(p.GetComponent<Rigidbody>().position, Is.EqualTo(position));
            Assert.That(input.HorizontalValue, Is.Zero);
            Assert.That(input.ConsumeJumpPressed(), Is.False); Assert.That(input.ConsumeDashPressed(), Is.False);
            Assert.That(combat.IsAttacking, Is.False); Assert.That(combat.IsParryWindowOpen, Is.False);
            Assert.That(parries, Is.Zero); Assert.That(p.GetComponent<QusapDashMotor>().IsDashing, Is.False);
            Assert.That(receiver.Heal(25), Is.Zero);
            devices.Set(gamepad.leftStick, Vector2.zero);
            devices.Release(gamepad.buttonSouth); devices.Release(gamepad.rightShoulder);
            devices.Release(gamepad.buttonWest); devices.Release(gamepad.buttonEast);
            devices.Release(gamepad.buttonNorth); devices.Release(gamepad.leftTrigger);
        }

        [UnityTest] public IEnumerator ExplicitNewSessionRestoresTerminalAuthoritiesOnlyWhenRequested()
        {
            var p = players[0]; var r = Receiver(p); r.TryReceiveEnvironmentDamage(100, volume);
            yield return Tick(0.05f); Assert.That(r.IsEliminated, Is.True);
            p.GetComponent<QusapRespawnController>().ResetForNewSession(); yield return Tick(0.05f);
            Assert.That(r.IsEliminated, Is.False); Assert.That(r.CurrentHealth, Is.EqualTo(100));
            Assert.That(p.GetComponent<Collider>().enabled, Is.True); Assert.That(p.GetComponent<Rigidbody>().isKinematic, Is.False);
            Assert.That(p.GetComponent<QusapInputReader>().IsGameplayInputBlocked, Is.False);
            Assert.That(Combat(p).CombatAllowed, Is.True);
        }

        [UnityTest] public IEnumerator ApprovedVisualsAndClockRemainUnchanged()
        {
            foreach (var p in players)
            {
                Assert.That(p.GetComponentsInChildren<Animator>(true).All(a => !a.applyRootMotion), Is.True);
                var source = p.GetComponents<MonoBehaviour>().OfType<IQusapImpactVisualSource>().Single();
                Assert.That(source.ImpactVisualRoot.GetComponentsInChildren<MeshFilter>(false).Length, Is.EqualTo(1));
            }
            yield return Tick(0.05f);
        }
    }
}
#endif
