using System;
using System.Collections.Generic;
using System.Reflection;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.InputSystem;

namespace Qusap.Tests
{
    public sealed class QusapWeaponEquipmentPlayModeTests
    {
        private readonly List<PlayerHarness> players = new();
        private readonly List<GameObject> temporaryObjects = new();

        [TearDown]
        public void TearDown()
        {
            for (int i = temporaryObjects.Count - 1; i >= 0; i--)
            {
                if (temporaryObjects[i] != null)
                {
                    UnityEngine.Object.DestroyImmediate(temporaryObjects[i]);
                }
            }

            temporaryObjects.Clear();
            for (int i = players.Count - 1; i >= 0; i--)
            {
                players[i].Dispose();
            }

            players.Clear();
        }

        [Test]
        public void EquipmentInitializesWithLocalCombatController()
        {
            PlayerHarness player = CreatePlayer("Owner");
            QusapWeaponEquipment equipment = player.AddEquipment();

            Assert.That(equipment.IsInitialized, Is.True);
            Assert.That(
                equipment.OwnerEntityId,
                Is.EqualTo(EntityId.ToULong(player.Combat.GetEntityId())));
        }

        [Test]
        public void EquipmentDoesNotSearchForAnotherPlayerController()
        {
            PlayerHarness otherPlayer = CreatePlayer("OtherPlayer");
            GameObject isolated = new("IsolatedEquipment");
            temporaryObjects.Add(isolated);
            QusapWeaponEquipment equipment = isolated.AddComponent<QusapWeaponEquipment>();

            Assert.That(equipment.TryInitialize(), Is.False);
            Assert.That(equipment.TryInitialize(otherPlayer.Combat), Is.False);
            Assert.That(equipment.IsInitialized, Is.False);
            Assert.That(equipment.OwnerEntityId, Is.Zero);
        }

        [Test]
        public void EquipmentCanEquipOneWeapon()
        {
            QusapWeaponEquipment equipment = CreatePlayer("Owner").AddEquipment();
            QusapWeaponInstance weapon = CreateWeapon();

            QusapWeaponOperationResult result = equipment.TryEquip(weapon, out _);

            Assert.That(result, Is.EqualTo(QusapWeaponOperationResult.Success));
            Assert.That(equipment.HasWeapon, Is.True);
            Assert.That(equipment.EquippedWeapon, Is.SameAs(weapon));
        }

        [Test]
        public void EquipmentRejectsSecondWeapon()
        {
            QusapWeaponEquipment equipment = CreatePlayer("Owner").AddEquipment();
            QusapWeaponInstance first = CreateWeapon(1);
            QusapWeaponInstance second = CreateWeapon(2);
            equipment.TryEquip(first, out _);

            QusapWeaponOperationResult result = equipment.TryEquip(second, out _);

            Assert.That(result, Is.EqualTo(QusapWeaponOperationResult.SlotOccupied));
            Assert.That(equipment.EquippedWeapon, Is.SameAs(first));
            Assert.That(second.IsFree, Is.True);
        }

        [Test]
        public void CanBeDisarmedIsTrueOnlyWhileArmed()
        {
            QusapWeaponEquipment equipment = CreatePlayer("Owner").AddEquipment();

            Assert.That(equipment.CanBeDisarmed, Is.False);
            equipment.TryEquip(CreateWeapon(), out _);
            Assert.That(equipment.CanBeDisarmed, Is.True);
            equipment.TryDrop(out _, out _);
            Assert.That(equipment.CanBeDisarmed, Is.False);
        }

        [Test]
        public void DisarmFromDifferentControllerSucceeds()
        {
            QusapWeaponEquipment equipment = CreatePlayer("Owner").AddEquipment();
            PlayerHarness attacker = CreatePlayer("Attacker");
            QusapWeaponInstance weapon = CreateWeapon();
            equipment.TryEquip(weapon, out _);

            bool result = equipment.TryDisarm(attacker.Combat);

            Assert.That(result, Is.True);
            Assert.That(equipment.HasWeapon, Is.False);
            Assert.That(weapon.IsFree, Is.True);
        }

        [Test]
        public void DisarmFromOwnerIsRejected()
        {
            PlayerHarness owner = CreatePlayer("Owner");
            QusapWeaponEquipment equipment = owner.AddEquipment();
            QusapWeaponInstance weapon = CreateWeapon();
            equipment.TryEquip(weapon, out _);

            Assert.That(equipment.TryDisarm(owner.Combat), Is.False);
            Assert.That(equipment.EquippedWeapon, Is.SameAs(weapon));
        }

        [Test]
        public void DisarmWithNullSourceIsRejected()
        {
            QusapWeaponEquipment equipment = CreatePlayer("Owner").AddEquipment();
            QusapWeaponInstance weapon = CreateWeapon();
            equipment.TryEquip(weapon, out _);

            Assert.That(equipment.TryDisarm((QusapCombatController)null), Is.False);
            Assert.That(equipment.EquippedWeapon, Is.SameAs(weapon));
        }

        [Test]
        public void DisarmEventFiresExactlyOnce()
        {
            QusapWeaponEquipment equipment = CreatePlayer("Owner").AddEquipment();
            PlayerHarness attacker = CreatePlayer("Attacker");
            equipment.TryEquip(CreateWeapon(), out _);
            int disarmEvents = 0;
            equipment.WeaponTransitioned += transition =>
            {
                if (transition.Type == QusapWeaponTransitionType.Disarmed)
                {
                    disarmEvents++;
                }
            };

            equipment.TryDisarm(attacker.Combat);

            Assert.That(disarmEvents, Is.EqualTo(1));
        }

        [Test]
        public void RepeatedDisarmDoesNotFireAnotherEvent()
        {
            QusapWeaponEquipment equipment = CreatePlayer("Owner").AddEquipment();
            PlayerHarness attacker = CreatePlayer("Attacker");
            equipment.TryEquip(CreateWeapon(), out _);
            int disarmEvents = 0;
            equipment.WeaponTransitioned += transition =>
                disarmEvents += transition.Type == QusapWeaponTransitionType.Disarmed ? 1 : 0;

            Assert.That(equipment.TryDisarm(attacker.Combat), Is.True);
            Assert.That(equipment.TryDisarm(attacker.Combat), Is.False);

            Assert.That(disarmEvents, Is.EqualTo(1));
        }

        [Test]
        public void EventObservesAlreadyUpdatedState()
        {
            QusapWeaponEquipment equipment = CreatePlayer("Owner").AddEquipment();
            PlayerHarness attacker = CreatePlayer("Attacker");
            QusapWeaponInstance weapon = CreateWeapon();
            equipment.TryEquip(weapon, out _);
            bool observedUpdatedState = false;
            equipment.WeaponTransitioned += transition =>
            {
                if (transition.Type != QusapWeaponTransitionType.Disarmed)
                {
                    return;
                }

                observedUpdatedState = !equipment.HasWeapon
                    && equipment.EquippedWeapon == null
                    && weapon.IsFree
                    && equipment.Revision == transition.ResultingRevision;
            };

            equipment.TryDisarm(attacker.Combat);

            Assert.That(observedUpdatedState, Is.True);
        }

        [Test]
        public void DisableMakesCanBeDisarmedFalse()
        {
            QusapWeaponEquipment equipment = CreatePlayer("Owner").AddEquipment();
            PlayerHarness attacker = CreatePlayer("Attacker");
            equipment.TryEquip(CreateWeapon(), out _);

            equipment.enabled = false;

            Assert.That(equipment.CanBeDisarmed, Is.False);
            Assert.That(equipment.TryDisarm(attacker.Combat), Is.False);
            Assert.That(equipment.HasWeapon, Is.True);
        }

        [Test]
        public void ReenablePreservesEquippedWeapon()
        {
            QusapWeaponEquipment equipment = CreatePlayer("Owner").AddEquipment();
            QusapWeaponInstance weapon = CreateWeapon();
            equipment.TryEquip(weapon, out _);

            equipment.enabled = false;
            equipment.enabled = true;

            Assert.That(equipment.EquippedWeapon, Is.SameAs(weapon));
            Assert.That(weapon.OwnerEntityId, Is.EqualTo(equipment.OwnerEntityId));
            Assert.That(equipment.CanBeDisarmed, Is.True);
        }

        [Test]
        public void DestroyingEquipmentReleasesLogicalOwnership()
        {
            PlayerHarness player = CreatePlayer("Owner");
            QusapWeaponEquipment equipment = player.AddEquipment();
            QusapWeaponInstance weapon = CreateWeapon();
            equipment.TryEquip(weapon, out _);

            UnityEngine.Object.DestroyImmediate(equipment);

            Assert.That(weapon.OwnerEntityId, Is.Null);
            Assert.That(weapon.IsFree, Is.True);
        }

        [Test]
        public void EquipmentAddsNoCollider()
        {
            PlayerHarness player = CreatePlayer("Owner");
            Collider[] before = player.Root.GetComponents<Collider>();

            player.AddEquipment();

            Assert.That(player.Root.GetComponents<Collider>(), Has.Length.EqualTo(before.Length));
            Assert.That(player.Root.GetComponentsInChildren<Collider>(true), Has.Length.EqualTo(before.Length));
        }

        [Test]
        public void EquipmentAddsNoRigidbody()
        {
            PlayerHarness player = CreatePlayer("Owner");
            Rigidbody[] before = player.Root.GetComponents<Rigidbody>();

            player.AddEquipment();

            Assert.That(player.Root.GetComponents<Rigidbody>(), Has.Length.EqualTo(before.Length));
            Assert.That(player.Root.GetComponent<Rigidbody>(), Is.SameAs(player.Body));
        }

        [Test]
        public void EquipmentDoesNotMoveOwner()
        {
            PlayerHarness player = CreatePlayer("Owner");
            QusapWeaponEquipment equipment = player.AddEquipment();
            Vector3 position = player.Root.transform.position;
            Quaternion rotation = player.Root.transform.rotation;

            equipment.TryEquip(CreateWeapon(), out _);
            equipment.TryDrop(out _, out _);

            Assert.That(player.Root.transform.position, Is.EqualTo(position));
            Assert.That(player.Root.transform.rotation, Is.EqualTo(rotation));
        }

        [Test]
        public void EquipmentDoesNotChangeVelocity()
        {
            PlayerHarness player = CreatePlayer("Owner");
            QusapWeaponEquipment equipment = player.AddEquipment();
            Vector3 velocity = new(3f, -2f, 1f);
            Vector3 angularVelocity = new(0.5f, 1f, -0.5f);
            player.Body.linearVelocity = velocity;
            player.Body.angularVelocity = angularVelocity;

            equipment.TryEquip(CreateWeapon(), out _);
            equipment.TryDrop(out _, out _);

            Assert.That(player.Body.linearVelocity, Is.EqualTo(velocity));
            Assert.That(player.Body.angularVelocity, Is.EqualTo(angularVelocity));
        }

        [Test]
        public void EquipmentDoesNotModifyDamageOrCombat()
        {
            PlayerHarness player = CreatePlayer("Owner");
            bool combatAllowed = player.Combat.CombatAllowed;
            float damage = player.Receiver.TotalDamageReceived;
            QusapWeaponEquipment equipment = player.AddEquipment();

            equipment.TryEquip(CreateWeapon(), out _);
            equipment.TryDrop(out _, out _);

            Assert.That(player.Combat.CombatAllowed, Is.EqualTo(combatAllowed));
            Assert.That(player.Receiver.TotalDamageReceived, Is.EqualTo(damage));
            Assert.That(player.Combat.IsAttacking, Is.False);
        }

        [Test]
        public void ExistingComboParryAndFeedbackSuitesStillPass()
        {
            PlayerHarness player = CreatePlayer("Owner");
            QusapParryCuePresenter cuePresenter = player.Combat.ParryCuePresenter;
            QusapCombatFeedbackPresenter feedbackPresenter = player.Combat.CombatFeedbackPresenter;

            Assert.That(player.Root.GetComponent<QusapWeaponEquipment>(), Is.Null);
            QusapWeaponEquipment equipment = player.AddEquipment();
            equipment.TryEquip(CreateWeapon(), out _);

            Assert.That(player.Combat.ParryCuePresenter, Is.SameAs(cuePresenter));
            Assert.That(player.Combat.CombatFeedbackPresenter, Is.SameAs(feedbackPresenter));
            Assert.That(player.Combat.ComboRecognitionEnabled, Is.True);
            Assert.That(player.Combat.FinisherDefensePhase, Is.EqualTo(QusapFinisherDefensePhase.None));
        }

        private PlayerHarness CreatePlayer(string name)
        {
            PlayerHarness player = new(name);
            players.Add(player);
            return player;
        }

        private static QusapWeaponInstance CreateWeapon(ulong instanceId = 1)
        {
            return new QusapWeaponInstance(
                instanceId,
                new QusapWeaponDefinition("training_sword", "Training Sword"));
        }

        private sealed class PlayerHarness : IDisposable
        {
            private static int nextPlayerId;
            private readonly InputActionAsset inputAsset;

            public PlayerHarness(string name)
            {
                Root = new GameObject($"{name}_WeaponEquipment_{++nextPlayerId}");
                Root.SetActive(false);
                Root.transform.position = new Vector3(nextPlayerId * 10f, 2f, 0f);
                Body = Root.AddComponent<Rigidbody>();
                Body.useGravity = false;
                Root.AddComponent<CapsuleCollider>();

                inputAsset = ScriptableObject.CreateInstance<InputActionAsset>();
                InputActionMap map = new("Gameplay");
                inputAsset.AddActionMap(map);
                foreach (string action in new[]
                {
                    "Move", "Jump", "Drop", "Dash", "WeakKick", "StrongKick",
                    "Headbutt", "WeaponStrong", "Parry"
                })
                {
                    map.AddAction(
                        action,
                        action == "Move" ? InputActionType.Value : InputActionType.Button);
                }

                QusapInputReader input = Root.AddComponent<QusapInputReader>();
                SetField(input, "inputActionAsset", inputAsset);
                Root.AddComponent<QusapGroundSensor>();
                Root.AddComponent<QusapWallSensor>();
                Root.AddComponent<QusapHorizontalMotor>();
                Root.AddComponent<QusapVerticalMotor>();
                Root.AddComponent<QusapDashMotor>();
                Receiver = Root.AddComponent<QusapHitReceiver>();
                Root.AddComponent<QusapHitstunController>();
                Root.AddComponent<QusapRespawnController>();
                Root.AddComponent<QusapHurtbox>();
                GameObject hitboxObject = new("AttackHitbox");
                hitboxObject.transform.SetParent(Root.transform, false);
                hitboxObject.AddComponent<QusapAttackHitbox>();
                Combat = Root.AddComponent<QusapCombatController>();
                Root.SetActive(true);
            }

            public GameObject Root { get; }
            public Rigidbody Body { get; }
            public QusapHitReceiver Receiver { get; }
            public QusapCombatController Combat { get; }

            public QusapWeaponEquipment AddEquipment()
            {
                QusapWeaponEquipment equipment = Root.AddComponent<QusapWeaponEquipment>();
                Assert.That(equipment.TryInitialize(Combat), Is.True);
                return equipment;
            }

            public void Dispose()
            {
                if (Root != null)
                {
                    UnityEngine.Object.DestroyImmediate(Root);
                }

                if (inputAsset != null)
                {
                    UnityEngine.Object.DestroyImmediate(inputAsset);
                }
            }

            private static void SetField(object target, string name, object value)
            {
                FieldInfo field = target.GetType().GetField(
                    name,
                    BindingFlags.Instance | BindingFlags.NonPublic);
                Assert.That(field, Is.Not.Null, $"Missing field {name}");
                field.SetValue(target, value);
            }
        }
    }
}
