using System;
using System.Collections;
using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;
using UnityEngine.TestTools;

namespace Qusap.Tests
{
    public sealed class QusapWeaponBasicAttackPlayModeTests
    {
        private readonly List<AttackRig> rigs = new();

        [TearDown]
        public void TearDown()
        {
            for (int i = rigs.Count - 1; i >= 0; i--)
            {
                rigs[i].Dispose();
            }
            rigs.Clear();
        }

        [UnityTest]
        public IEnumerator ArmedYStartsWeaponLightAndCanHit()
        {
            AttackRig attacker = CreateRig("Attacker", true, Vector3.zero);
            AttackRig target = CreateRig("Target", false, new Vector3(0.8f, 0f, 0f));
            float before = target.Receiver.TotalDamageReceived;
            attacker.Press(QusapCombatCommand.WeaponLight);
            yield return WaitForAttack(attacker, QusapAttackVariant.WeaponLight);
            yield return WaitForIdle(attacker);
            Assert.That(target.Receiver.TotalDamageReceived, Is.GreaterThan(before));
        }

        [UnityTest]
        public IEnumerator ArmedBStartsWeaponStrongAndCanHit()
        {
            AttackRig attacker = CreateRig("Attacker", true, Vector3.zero);
            AttackRig target = CreateRig("Target", false, new Vector3(0.9f, 0f, 0f));
            float before = target.Receiver.TotalDamageReceived;
            attacker.Press(QusapCombatCommand.WeaponStrong);
            yield return WaitForAttack(attacker, QusapAttackVariant.WeaponStrong);
            yield return WaitForIdle(attacker);
            Assert.That(target.Receiver.TotalDamageReceived, Is.GreaterThan(before));
        }

        [UnityTest]
        public IEnumerator UnarmedYDoesNotOpenHitboxOrDealDamage()
        {
            AttackRig attacker = CreateRig("Attacker", false, Vector3.zero);
            AttackRig target = CreateRig("Target", false, new Vector3(0.8f, 0f, 0f));
            attacker.Press(QusapCombatCommand.WeaponLight);
            yield return new WaitForFixedUpdate();
            Assert.That(attacker.Combat.IsAttacking, Is.False);
            Assert.That(attacker.Hitbox.IsActive, Is.False);
            Assert.That(target.Receiver.TotalDamageReceived, Is.Zero);
        }

        [UnityTest]
        public IEnumerator UnarmedBDoesNotOpenHitboxOrDealDamage()
        {
            AttackRig attacker = CreateRig("Attacker", false, Vector3.zero);
            AttackRig target = CreateRig("Target", false, new Vector3(0.9f, 0f, 0f));
            attacker.Press(QusapCombatCommand.WeaponStrong);
            yield return new WaitForFixedUpdate();
            Assert.That(attacker.Combat.IsAttacking, Is.False);
            Assert.That(attacker.Hitbox.IsActive, Is.False);
            Assert.That(target.Receiver.TotalDamageReceived, Is.Zero);
        }

        [UnityTest]
        public IEnumerator BodyAttackAndHeadbuttRemainAvailableUnarmed()
        {
            AttackRig player = CreateRig("Player", false, Vector3.zero);
            player.Press(QusapCombatCommand.BodyAttack);
            yield return WaitForAnyAttack(player);
            Assert.That(player.Combat.CurrentAttackType, Is.EqualTo(QusapAttackType.WeakKick));
            player.Combat.CancelAttack();
            player.Press(QusapCombatCommand.Headbutt);
            yield return WaitForAnyAttack(player);
            Assert.That(player.Combat.CurrentAttackType, Is.EqualTo(QusapAttackType.Headbutt));
        }

        [UnityTest]
        public IEnumerator ParryPressStillReachesExistingSystemUnarmed()
        {
            AttackRig player = CreateRig("Player", false, Vector3.zero);
            int observed = 0;
            player.Input.ParryPressed += _ => observed++;
            player.Press(QusapCombatCommand.Parry);
            yield return null;
            Assert.That(observed, Is.EqualTo(1));
        }

        [UnityTest]
        public IEnumerator RecoveryPressBuffersAndExecutesOneLaterAttack()
        {
            AttackRig player = CreateRig("Player", true, Vector3.zero);
            player.Press(QusapCombatCommand.WeaponLight);
            yield return WaitForPhase(player, QusapAttackPhase.Recovery);
            player.Press(QusapCombatCommand.WeaponStrong);
            yield return new WaitForFixedUpdate();
            Assert.That(player.Combat.HasBufferedWeaponAttack, Is.True);
            yield return WaitForAttack(player, QusapAttackVariant.WeaponStrong);
            Assert.That(player.Combat.HasBufferedWeaponAttack, Is.False);
        }

        [UnityTest]
        public IEnumerator OneYPressCannotProduceAutomaticChain()
        {
            AttackRig player = CreateRig("Player", true, Vector3.zero);
            int starts = 0;
            player.Combat.AttackVariantStarted += variant =>
            {
                if (variant == QusapAttackVariant.WeaponLight) starts++;
            };
            player.Press(QusapCombatCommand.WeaponLight);
            yield return WaitForIdle(player);
            yield return new WaitForSeconds(0.25f);
            Assert.That(starts, Is.EqualTo(1));
        }

        [UnityTest]
        public IEnumerator ButtonMashCreatesOnlyLegalSequentialAttacks()
        {
            AttackRig player = CreateRig("Player", true, Vector3.zero);
            int starts = 0;
            player.Combat.AttackVariantStarted += _ => starts++;
            player.Press(QusapCombatCommand.WeaponLight);
            yield return WaitForPhase(player, QusapAttackPhase.Recovery);
            player.Press(QusapCombatCommand.WeaponLight);
            player.Press(QusapCombatCommand.WeaponStrong);
            player.Press(QusapCombatCommand.WeaponLight);
            int frames = 0;
            while ((starts < 2 || player.Combat.IsAttacking) && frames++ < 150)
                yield return new WaitForFixedUpdate();
            Assert.That(starts, Is.EqualTo(2));
        }

        [UnityTest]
        public IEnumerator DamageDisarmAndLaunchSequencesStillArmFinishers()
        {
            AttackRig attacker = CreateRig("Attacker", true, Vector3.zero);
            AttackRig target = CreateRig("Target", true, new Vector3(0.8f, 0f, 0f));
            foreach (QusapComboId comboId in new[] { QusapComboId.Damage, QusapComboId.Disarm, QusapComboId.Launch })
            {
                yield return ArmCombo(attacker, target, comboId);
                Assert.That(attacker.Combat.ArmedFinisherCombo, Is.EqualTo(comboId));
                attacker.Combat.CancelAttack();
                target.Hitstun.ResetHitstun();
            }
        }

        [UnityTest]
        public IEnumerator LosingWeaponDuringAttackCancelsHitboxAndVisual()
        {
            AttackRig player = CreateRig("Player", true, Vector3.zero);
            player.Press(QusapCombatCommand.WeaponLight);
            yield return WaitForPhase(player, QusapAttackPhase.Active);
            Assert.That(player.Equipment.TryDrop(out _, out _), Is.EqualTo(QusapWeaponOperationResult.Success));
            Assert.That(player.Combat.IsAttacking, Is.False);
            Assert.That(player.Hitbox.IsActive, Is.False);
            yield return null;
            Assert.That(player.WeaponVisualPresenter.IsAnimating, Is.False);
        }

        [UnityTest]
        public IEnumerator VoluntarySwapUsesNewVisualForFollowingAttack()
        {
            AttackRig player = CreateRig("Player", true, Vector3.zero);
            QusapWeaponInstance original = player.Equipment.EquippedWeapon;
            GameObject originalVisual = player.EquippedPresenter.EquippedVisual;
            QusapWeaponInstance replacement = player.NewWeapon(2, "purple");
            Assert.That(player.Equipment.TryVoluntarySwap(original, replacement, out _),
                Is.EqualTo(QusapWeaponOperationResult.Success));
            Assert.That(player.EquippedPresenter.EquippedVisual, Is.Not.SameAs(originalVisual));
            player.Press(QusapCombatCommand.WeaponLight);
            yield return WaitForAttack(player, QusapAttackVariant.WeaponLight);
            Assert.That(player.EquippedPresenter.DisplayedWeapon, Is.SameAs(replacement));
        }

        [Test]
        public void BluePurpleAndWhiteUseIdenticalBaseAttackProfiles()
        {
            QusapWeaponAttackData light = QusapWeaponAttackData.CreateLight();
            QusapWeaponAttackData strong = QusapWeaponAttackData.CreateStrong();
            Assert.That(light.Damage, Is.EqualTo(QusapWeaponAttackData.CreateLight().Damage));
            Assert.That(strong.StartupTime, Is.EqualTo(QusapWeaponAttackData.CreateStrong().StartupTime));
        }

        [Test]
        public void LightAttackProfileUsesShortArc()
        {
            Assert.That(QusapWeaponAttackVisualProfile.CreateLight().ArcAmplitude,
                Is.LessThan(QusapWeaponAttackVisualProfile.CreateStrong().ArcAmplitude));
        }

        [Test]
        public void StrongAttackProfileUsesWiderArc()
        {
            Assert.That(QusapWeaponAttackVisualProfile.CreateStrong().ArcAmplitude, Is.GreaterThan(10f));
        }

        [Test]
        public void VisualProfileMirrorsWithFacing()
        {
            QusapWeaponAttackVisualProfile profile = QusapWeaponAttackVisualProfile.CreateLight();
            QusapWeaponVisualPose right = profile.Evaluate(QusapAttackPhase.Active, 0.5f, 1);
            QusapWeaponVisualPose left = profile.Evaluate(QusapAttackPhase.Active, 0.5f, -1);
            Assert.That(left.LocalPosition.x, Is.EqualTo(-right.LocalPosition.x).Within(0.0001f));
            Assert.That(left.LocalEulerAngles.z, Is.EqualTo(-right.LocalEulerAngles.z).Within(0.0001f));
        }

        [UnityTest]
        public IEnumerator ProceduralVisualDoesNotMoveRootRigidbodyOrCollider()
        {
            AttackRig player = CreateRig("Player", true, Vector3.zero);
            Vector3 position = player.Root.transform.position;
            Quaternion rotation = player.Root.transform.rotation;
            Vector3 colliderCenter = player.Collider.center;
            player.Press(QusapCombatCommand.WeaponStrong);
            yield return WaitForPhase(player, QusapAttackPhase.Active);
            Assert.That(player.Root.transform.position, Is.EqualTo(position));
            Assert.That(player.Root.transform.rotation, Is.EqualTo(rotation));
            Assert.That(player.Collider.center, Is.EqualTo(colliderCenter));
        }

        [UnityTest]
        public IEnumerator VisualAndHitboxFollowSameAuthoritativePhases()
        {
            AttackRig player = CreateRig("Player", true, Vector3.zero);
            player.Press(QusapCombatCommand.WeaponLight);
            yield return WaitForPhase(player, QusapAttackPhase.Startup);
            Assert.That(player.Hitbox.IsActive, Is.False);
            Assert.That(player.WeaponVisualPresenter.IsAnimating, Is.True);
            yield return WaitForPhase(player, QusapAttackPhase.Active);
            Assert.That(player.Hitbox.IsActive, Is.True);
            Assert.That(player.WeaponVisualPresenter.IsAnimating, Is.True);
            yield return WaitForPhase(player, QusapAttackPhase.Recovery);
            Assert.That(player.Hitbox.IsActive, Is.False);
        }

        [UnityTest]
        public IEnumerator VisualReturnsExactlyToRestAfterCompletion()
        {
            AttackRig player = CreateRig("Player", true, Vector3.zero);
            Transform visual = player.EquippedPresenter.EquippedVisual.transform;
            Vector3 restPosition = visual.localPosition;
            Quaternion restRotation = visual.localRotation;
            player.Press(QusapCombatCommand.WeaponLight);
            yield return WaitForIdle(player);
            yield return null;
            Assert.That(visual.localPosition, Is.EqualTo(restPosition));
            Assert.That(visual.localRotation, Is.EqualTo(restRotation));
        }

        [UnityTest]
        public IEnumerator RepeatedAttacksNeverDuplicateWeaponVisualOrInstance()
        {
            AttackRig player = CreateRig("Player", true, Vector3.zero);
            QusapWeaponInstance instance = player.Equipment.EquippedWeapon;
            for (int i = 0; i < 3; i++)
            {
                player.Press(i % 2 == 0 ? QusapCombatCommand.WeaponLight : QusapCombatCommand.WeaponStrong);
                yield return WaitForIdle(player);
            }
            Assert.That(player.Equipment.EquippedWeapon, Is.SameAs(instance));
            Assert.That(player.EquippedPresenter.WeaponSocket.childCount, Is.EqualTo(1));
        }

        [UnityTest]
        public IEnumerator DropAndPickupPreserveNormalAttackAvailability()
        {
            AttackRig player = CreateRig("Player", true, Vector3.zero);
            Assert.That(player.Equipment.TryDrop(out QusapWeaponInstance dropped, out _),
                Is.EqualTo(QusapWeaponOperationResult.Success));
            player.Press(QusapCombatCommand.WeaponLight);
            yield return new WaitForFixedUpdate();
            Assert.That(player.Combat.IsAttacking, Is.False);
            Assert.That(player.Equipment.TryEquip(dropped, out _), Is.EqualTo(QusapWeaponOperationResult.Success));
            player.Press(QusapCombatCommand.WeaponLight);
            yield return WaitForAttack(player, QusapAttackVariant.WeaponLight);
        }

        [UnityTest]
        public IEnumerator HitstunAndRespawnClearAttackAndBufferedPose()
        {
            AttackRig player = CreateRig("Player", true, Vector3.zero);
            player.Press(QusapCombatCommand.WeaponLight);
            yield return WaitForPhase(player, QusapAttackPhase.Recovery);
            player.Press(QusapCombatCommand.WeaponStrong);
            yield return new WaitForFixedUpdate();
            Assert.That(player.Combat.HasBufferedWeaponAttack, Is.True);
            player.Hitstun.EnterHitstun(0.1f);
            Assert.That(player.Combat.IsAttacking, Is.False);
            Assert.That(player.Combat.HasBufferedWeaponAttack, Is.False);
            player.Hitstun.ResetHitstun();
            player.Respawn.Respawn();
            yield return null;
            Assert.That(player.WeaponVisualPresenter.IsAnimating, Is.False);
            Assert.That(player.Hitbox.IsActive, Is.False);
        }

        [UnityTest]
        public IEnumerator InvalidWeaponInputDuringHitstunIsNotStoredForLater()
        {
            AttackRig player = CreateRig("Player", true, Vector3.zero);
            player.Hitstun.EnterHitstun(0.1f);
            QusapCombatCommandPress rejected = player.Input.EnqueueCombatCommand(
                QusapCombatCommand.WeaponLight, InputState.currentTime);
            Assert.That(rejected.PressId, Is.Zero);
            player.Hitstun.ResetHitstun();
            yield return new WaitForFixedUpdate();
            Assert.That(player.Combat.IsAttacking, Is.False);
        }

        [UnityTest]
        public IEnumerator DashClearsBufferAndRejectsWeaponInputWhileDashing()
        {
            AttackRig player = CreateRig("Player", true, Vector3.zero);
            player.Press(QusapCombatCommand.WeaponLight);
            yield return WaitForPhase(player, QusapAttackPhase.Recovery);
            player.Press(QusapCombatCommand.WeaponStrong);
            yield return new WaitForFixedUpdate();
            Assert.That(player.Combat.HasBufferedWeaponAttack, Is.True);

            Assert.That(Keyboard.current, Is.Not.Null);
            InputSystem.QueueStateEvent(Keyboard.current, new KeyboardState(Key.LeftShift));
            InputSystem.Update();
            yield return new WaitForFixedUpdate();
            InputSystem.QueueStateEvent(Keyboard.current, new KeyboardState());
            InputSystem.Update();

            Assert.That(player.Dash.IsDashing, Is.True);
            Assert.That(player.Combat.HasBufferedWeaponAttack, Is.False);
            player.Press(QusapCombatCommand.WeaponLight);
            yield return new WaitForFixedUpdate();
            Assert.That(player.Combat.HasBufferedWeaponAttack, Is.False);
        }

        private AttackRig CreateRig(string name, bool armed, Vector3 position)
        {
            AttackRig rig = new(name, armed, position);
            rigs.Add(rig);
            return rig;
        }

        private static IEnumerator WaitForAnyAttack(AttackRig rig)
        {
            int frames = 0;
            while (!rig.Combat.IsAttacking && frames++ < 20) yield return new WaitForFixedUpdate();
            Assert.That(rig.Combat.IsAttacking, Is.True);
        }

        private static IEnumerator WaitForAttack(AttackRig rig, QusapAttackVariant expected)
        {
            int frames = 0;
            while (rig.Combat.CurrentAttackVariant != expected && frames++ < 40)
                yield return new WaitForFixedUpdate();
            Assert.That(rig.Combat.CurrentAttackVariant, Is.EqualTo(expected));
        }

        private static IEnumerator WaitForPhase(AttackRig rig, QusapAttackPhase phase)
        {
            int frames = 0;
            while (rig.Combat.CurrentPhase != phase && frames++ < 50)
                yield return new WaitForFixedUpdate();
            yield return null;
            Assert.That(rig.Combat.CurrentPhase, Is.EqualTo(phase));
        }

        private static IEnumerator WaitForIdle(AttackRig rig)
        {
            int frames = 0;
            while ((!rig.Combat.IsAttacking || frames == 0) && frames++ < 4)
                yield return new WaitForFixedUpdate();
            while (rig.Combat.IsAttacking && frames++ < 100) yield return new WaitForFixedUpdate();
            Assert.That(rig.Combat.IsAttacking, Is.False);
        }

        private static IEnumerator ArmCombo(AttackRig attacker, AttackRig target, QusapComboId id)
        {
            QusapComboDefinition definition = null;
            foreach (QusapComboDefinition candidate in QusapComboDefinition.CreateDefaultDefinitions())
                if (candidate.ComboId == id) definition = candidate;
            Assert.That(definition, Is.Not.Null);
            double timestamp = 10d + ((int)id * 10d);
            for (int i = 0; i < definition.StepCount - 1; i++)
            {
                if (i > 0) timestamp += 0.2d;
                attacker.Input.EnqueueCombatCommand(definition.GetStep(i).Command, timestamp);
                yield return WaitForAnyAttack(attacker);
                int activeFrames = 0;
                while (attacker.Combat.CurrentPhase != QusapAttackPhase.Active && activeFrames++ < 50)
                    yield return new WaitForFixedUpdate();
                Assert.That(attacker.Combat.CurrentPhase, Is.EqualTo(QusapAttackPhase.Active));
                attacker.Combat.NotifyAttackHit(target.Receiver);
                yield return WaitForIdle(attacker);
                target.Hitstun.ResetHitstun();
            }
            timestamp += 0.2d;
            attacker.Input.EnqueueCombatCommand(
                definition.GetStep(definition.StepCount - 1).Command, timestamp);
            yield return new WaitForFixedUpdate();
        }

        private sealed class AttackRig : IDisposable
        {
            private readonly QusapWeaponVisualCatalog catalog;
            private readonly GameObject visualPrefab;
            private ulong nextWeaponId = 1;

            public AttackRig(string name, bool armed, Vector3 position)
            {
                GameObject inputTemplate = Resources.Load<GameObject>(
                    "QusapWeaponAttackTestInput");
                Assert.That(inputTemplate, Is.Not.Null);
                Root = UnityEngine.Object.Instantiate(inputTemplate);
                Root.name = name;
                Root.transform.position = position;
                Body = Root.AddComponent<Rigidbody>();
                Body.useGravity = false;
                Body.constraints = RigidbodyConstraints.FreezeAll;
                Collider = Root.AddComponent<CapsuleCollider>();

                Input = Root.GetComponent<QusapInputReader>();
                Assert.That(Input, Is.Not.Null);
                Root.AddComponent<QusapGroundSensor>();
                Root.AddComponent<QusapWallSensor>();
                Root.AddComponent<QusapHorizontalMotor>();
                Root.AddComponent<QusapVerticalMotor>();
                Dash = Root.AddComponent<QusapDashMotor>();
                Receiver = Root.AddComponent<QusapHitReceiver>();
                Hitstun = Root.AddComponent<QusapHitstunController>();
                Respawn = Root.AddComponent<QusapRespawnController>();
                Root.AddComponent<QusapHurtbox>();
                Equipment = Root.AddComponent<QusapWeaponEquipment>();

                GameObject hitboxObject = new("AttackHitbox");
                hitboxObject.transform.SetParent(Root.transform, false);
                Hitbox = hitboxObject.AddComponent<QusapAttackHitbox>();
                Combat = Root.AddComponent<QusapCombatController>();

                visualPrefab = new GameObject($"{name}_SwordVisual");
                visualPrefab.SetActive(false);
                catalog = ScriptableObject.CreateInstance<QusapWeaponVisualCatalog>();
                catalog.Configure(
                    new QusapWeaponVisualEntry("blue", "Blue", visualPrefab),
                    new QusapWeaponVisualEntry("purple", "Purple", visualPrefab),
                    new QusapWeaponVisualEntry("white", "White", visualPrefab));
                GameObject socket = new("WeaponSocket");
                socket.transform.SetParent(Root.transform, false);
                EquippedPresenter = Root.AddComponent<QusapEquippedWeaponPresenter>();
                EquippedPresenter.Configure(Equipment, Combat, catalog, socket.transform);
                WeaponVisualPresenter = Root.AddComponent<QusapWeaponAttackVisualPresenter>();

                Root.SetActive(true);
                Combat.GetWeaponAttackData(QusapWeaponAttackKind.Light).ConfigureSpeedMultiplier(3f);
                Combat.GetWeaponAttackData(QusapWeaponAttackKind.Strong).ConfigureSpeedMultiplier(3f);
                if (armed)
                {
                    Assert.That(Equipment.TryEquip(NewWeapon(nextWeaponId++, "blue"), out _),
                        Is.EqualTo(QusapWeaponOperationResult.Success));
                }
            }

            public GameObject Root { get; }
            public Rigidbody Body { get; }
            public CapsuleCollider Collider { get; }
            public QusapInputReader Input { get; }
            public QusapHitReceiver Receiver { get; }
            public QusapHitstunController Hitstun { get; }
            public QusapDashMotor Dash { get; }
            public QusapRespawnController Respawn { get; }
            public QusapAttackHitbox Hitbox { get; }
            public QusapCombatController Combat { get; }
            public QusapWeaponEquipment Equipment { get; }
            public QusapEquippedWeaponPresenter EquippedPresenter { get; }
            public QusapWeaponAttackVisualPresenter WeaponVisualPresenter { get; }

            public void Press(QusapCombatCommand command)
            {
                Input.EnqueueCombatCommand(command, InputState.currentTime);
            }

            public QusapWeaponInstance NewWeapon(ulong id, string definition)
            {
                return new QusapWeaponInstance(
                    id,
                    new QusapWeaponDefinition(definition, definition));
            }

            public void Dispose()
            {
                if (Root != null) UnityEngine.Object.DestroyImmediate(Root);
                if (visualPrefab != null) UnityEngine.Object.DestroyImmediate(visualPrefab);
                if (catalog != null) UnityEngine.Object.DestroyImmediate(catalog);
            }
        }
    }
}
