using System;
using NUnit.Framework;
using UnityEngine;

namespace Qusap.Tests
{
    public sealed class QusapWeaponEquipmentEditModeTests
    {
        private const ulong OwnerId = 101;
        private const ulong OtherOwnerId = 202;

        [Test]
        public void ValidDefinitionCanBeConstructedWithoutReflection()
        {
            QusapWeaponDefinition definition = new("  training_sword  ", "  Training Sword  ");

            Assert.That(definition.Id, Is.EqualTo("training_sword"));
            Assert.That(definition.DisplayName, Is.EqualTo("Training Sword"));
        }

        [Test]
        public void EmptyDefinitionIdIsRejected()
        {
            Assert.Throws<ArgumentException>(() => new QusapWeaponDefinition("   ", "Sword"));
        }

        [Test]
        public void EmptyDisplayNameIsRejected()
        {
            Assert.Throws<ArgumentException>(() => new QusapWeaponDefinition("sword", "   "));
        }

        [Test]
        public void WeaponInstanceRequiresNonZeroId()
        {
            Assert.Throws<ArgumentOutOfRangeException>(
                () => new QusapWeaponInstance(0, CreateDefinition()));
        }

        [Test]
        public void WeaponInstanceRequiresDefinition()
        {
            Assert.Throws<ArgumentNullException>(() => new QusapWeaponInstance(1, null));
        }

        [Test]
        public void IdGeneratorStartsAtOne()
        {
            Assert.That(new QusapWeaponIdGenerator().Next(), Is.EqualTo(1UL));
        }

        [Test]
        public void IdGeneratorNeverReusesIds()
        {
            QusapWeaponIdGenerator generator = new();

            Assert.That(generator.Next(), Is.EqualTo(1UL));
            Assert.That(generator.Next(), Is.EqualTo(2UL));
            generator.Reset();
            Assert.That(generator.Next(), Is.EqualTo(3UL));
        }

        [Test]
        public void IdGeneratorThrowsOnOverflow()
        {
            QusapWeaponIdGenerator generator = new(ulong.MaxValue);
            Assert.Throws<OverflowException>(() => generator.Next());
        }

        [Test]
        public void EquipmentStartsEmpty()
        {
            QusapWeaponEquipmentState equipment = new(OwnerId);

            Assert.That(equipment.HasWeapon, Is.False);
            Assert.That(equipment.EquippedWeapon, Is.Null);
            Assert.That(equipment.Revision, Is.Zero);
        }

        [Test]
        public void EquipIntoEmptySlotSucceeds()
        {
            QusapWeaponEquipmentState equipment = new(OwnerId);
            QusapWeaponInstance weapon = CreateWeapon();

            QusapWeaponOperationResult result = equipment.TryEquip(weapon, out _);

            Assert.That(result, Is.EqualTo(QusapWeaponOperationResult.Success));
            Assert.That(equipment.EquippedWeapon, Is.SameAs(weapon));
        }

        [Test]
        public void EquipAssignsCorrectOwner()
        {
            QusapWeaponEquipmentState equipment = new(OwnerId);
            QusapWeaponInstance weapon = CreateWeapon();

            equipment.TryEquip(weapon, out _);

            Assert.That(weapon.OwnerEntityId, Is.EqualTo(OwnerId));
            Assert.That(weapon.IsEquipped, Is.True);
            Assert.That(weapon.IsFree, Is.False);
        }

        [Test]
        public void SecondWeaponCannotEnterOccupiedSlot()
        {
            QusapWeaponEquipmentState equipment = new(OwnerId);
            QusapWeaponInstance first = CreateWeapon(1);
            QusapWeaponInstance second = CreateWeapon(2);
            equipment.TryEquip(first, out _);

            QusapWeaponOperationResult result = equipment.TryEquip(second, out _);

            Assert.That(result, Is.EqualTo(QusapWeaponOperationResult.SlotOccupied));
            Assert.That(equipment.EquippedWeapon, Is.SameAs(first));
            Assert.That(second.IsFree, Is.True);
        }

        [Test]
        public void SameWeaponCannotBeEquippedByTwoOwners()
        {
            QusapWeaponEquipmentState firstOwner = new(OwnerId);
            QusapWeaponEquipmentState secondOwner = new(OtherOwnerId);
            QusapWeaponInstance weapon = CreateWeapon();
            firstOwner.TryEquip(weapon, out _);

            QusapWeaponOperationResult result = secondOwner.TryEquip(weapon, out _);

            Assert.That(result, Is.EqualTo(QusapWeaponOperationResult.WeaponAlreadyOwned));
            Assert.That(firstOwner.EquippedWeapon, Is.SameAs(weapon));
            Assert.That(secondOwner.HasWeapon, Is.False);
            Assert.That(weapon.OwnerEntityId, Is.EqualTo(OwnerId));
        }

        [Test]
        public void DropReturnsSameWeaponInstance()
        {
            QusapWeaponEquipmentState equipment = ArmedEquipment(out QusapWeaponInstance weapon);

            QusapWeaponOperationResult result = equipment.TryDrop(
                out QusapWeaponInstance dropped,
                out _);

            Assert.That(result, Is.EqualTo(QusapWeaponOperationResult.Success));
            Assert.That(dropped, Is.SameAs(weapon));
        }

        [Test]
        public void DropClearsWeaponOwner()
        {
            QusapWeaponEquipmentState equipment = ArmedEquipment(out QusapWeaponInstance weapon);

            equipment.TryDrop(out _, out _);

            Assert.That(equipment.HasWeapon, Is.False);
            Assert.That(weapon.OwnerEntityId, Is.Null);
            Assert.That(weapon.IsFree, Is.True);
        }

        [Test]
        public void DropIncrementsRevisionOnce()
        {
            QusapWeaponEquipmentState equipment = ArmedEquipment(out _);
            ulong before = equipment.Revision;

            equipment.TryDrop(out _, out QusapWeaponTransition transition);

            Assert.That(equipment.Revision, Is.EqualTo(before + 1));
            Assert.That(transition.ResultingRevision, Is.EqualTo(equipment.Revision));
        }

        [Test]
        public void RepeatedDropDoesNotIncrementRevision()
        {
            QusapWeaponEquipmentState equipment = ArmedEquipment(out _);
            equipment.TryDrop(out _, out _);
            ulong afterFirstDrop = equipment.Revision;

            QusapWeaponOperationResult result = equipment.TryDrop(out _, out _);

            Assert.That(result, Is.EqualTo(QusapWeaponOperationResult.SlotEmpty));
            Assert.That(equipment.Revision, Is.EqualTo(afterFirstDrop));
        }

        [Test]
        public void DisarmReturnsSameWeaponInstance()
        {
            QusapWeaponEquipmentState equipment = ArmedEquipment(out QusapWeaponInstance weapon);

            QusapWeaponOperationResult result = equipment.TryDisarm(
                OtherOwnerId,
                out QusapWeaponInstance disarmed,
                out _);

            Assert.That(result, Is.EqualTo(QusapWeaponOperationResult.Success));
            Assert.That(disarmed, Is.SameAs(weapon));
        }

        [Test]
        public void DisarmClearsSlotAndOwner()
        {
            QusapWeaponEquipmentState equipment = ArmedEquipment(out QusapWeaponInstance weapon);

            equipment.TryDisarm(OtherOwnerId, out _, out _);

            Assert.That(equipment.HasWeapon, Is.False);
            Assert.That(equipment.EquippedWeapon, Is.Null);
            Assert.That(weapon.OwnerEntityId, Is.Null);
        }

        [Test]
        public void RepeatedDisarmHasNoEffect()
        {
            QusapWeaponEquipmentState equipment = ArmedEquipment(out _);
            equipment.TryDisarm(OtherOwnerId, out _, out _);
            ulong afterFirstDisarm = equipment.Revision;

            QusapWeaponOperationResult result = equipment.TryDisarm(
                OtherOwnerId,
                out QusapWeaponInstance repeatedWeapon,
                out _);

            Assert.That(result, Is.EqualTo(QusapWeaponOperationResult.SlotEmpty));
            Assert.That(repeatedWeapon, Is.Null);
            Assert.That(equipment.Revision, Is.EqualTo(afterFirstDisarm));
        }

        [Test]
        public void OwnerCannotDisarmSelf()
        {
            QusapWeaponEquipmentState equipment = ArmedEquipment(out QusapWeaponInstance weapon);

            QusapWeaponOperationResult result = equipment.TryDisarm(OwnerId, out _, out _);

            Assert.That(result, Is.EqualTo(QusapWeaponOperationResult.SelfDisarmRejected));
            Assert.That(equipment.EquippedWeapon, Is.SameAs(weapon));
        }

        [Test]
        public void InvalidSourceCannotDisarm()
        {
            QusapWeaponEquipmentState equipment = ArmedEquipment(out QusapWeaponInstance weapon);

            QusapWeaponOperationResult result = equipment.TryDisarm(0, out _, out _);

            Assert.That(result, Is.EqualTo(QusapWeaponOperationResult.InvalidOwner));
            Assert.That(equipment.EquippedWeapon, Is.SameAs(weapon));
        }

        [Test]
        public void RejectedOperationDoesNotChangeRevision()
        {
            QusapWeaponEquipmentState equipment = ArmedEquipment(out _);
            ulong before = equipment.Revision;

            QusapWeaponOperationResult result = equipment.TryEquip(CreateWeapon(2), out _);

            Assert.That(result, Is.EqualTo(QusapWeaponOperationResult.SlotOccupied));
            Assert.That(equipment.Revision, Is.EqualTo(before));
        }

        [Test]
        public void TransitionContainsExpectedOwners()
        {
            QusapWeaponEquipmentState equipment = new(OwnerId);
            QusapWeaponInstance weapon = CreateWeapon();
            equipment.TryEquip(weapon, out QusapWeaponTransition equipped);
            equipment.TryDisarm(OtherOwnerId, out _, out QusapWeaponTransition disarmed);

            Assert.That(equipped.Type, Is.EqualTo(QusapWeaponTransitionType.Equipped));
            Assert.That(equipped.PreviousOwnerEntityId, Is.Null);
            Assert.That(equipped.NewOwnerEntityId, Is.EqualTo(OwnerId));
            Assert.That(equipped.SourceEntityId, Is.EqualTo(OwnerId));
            Assert.That(disarmed.Type, Is.EqualTo(QusapWeaponTransitionType.Disarmed));
            Assert.That(disarmed.PreviousOwnerEntityId, Is.EqualTo(OwnerId));
            Assert.That(disarmed.NewOwnerEntityId, Is.Null);
            Assert.That(disarmed.SourceEntityId, Is.EqualTo(OtherOwnerId));
        }

        [Test]
        public void TransitionIsPublishedAfterStateChanges()
        {
            GameObject root = CreateInactiveEquipment(out QusapWeaponEquipment equipment);
            try
            {
                QusapWeaponInstance weapon = CreateWeapon();
                int eventCount = 0;
                equipment.WeaponTransitioned += transition =>
                {
                    eventCount++;
                    Assert.That(equipment.HasWeapon, Is.True);
                    Assert.That(equipment.EquippedWeapon, Is.SameAs(weapon));
                    Assert.That(equipment.Revision, Is.EqualTo(transition.ResultingRevision));
                };

                Assert.That(
                    equipment.TryEquip(weapon, out _),
                    Is.EqualTo(QusapWeaponOperationResult.Success));
                Assert.That(eventCount, Is.EqualTo(1));
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(root);
            }
        }

        [Test]
        public void ReentrantOperationCannotDuplicateWeapon()
        {
            GameObject root = CreateInactiveEquipment(out QusapWeaponEquipment equipment);
            try
            {
                QusapWeaponInstance first = CreateWeapon(1);
                QusapWeaponInstance second = CreateWeapon(2);
                int eventCount = 0;
                QusapWeaponOperationResult reentrantResult = QusapWeaponOperationResult.Success;
                equipment.WeaponTransitioned += _ =>
                {
                    eventCount++;
                    reentrantResult = equipment.TryEquip(second, out _);
                };

                equipment.TryEquip(first, out _);

                Assert.That(reentrantResult, Is.EqualTo(QusapWeaponOperationResult.SlotOccupied));
                Assert.That(eventCount, Is.EqualTo(1));
                Assert.That(equipment.EquippedWeapon, Is.SameAs(first));
                Assert.That(first.OwnerEntityId, Is.EqualTo(equipment.OwnerEntityId));
                Assert.That(second.IsFree, Is.True);
                Assert.That(equipment.Revision, Is.EqualTo(1UL));
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(root);
            }
        }

        private static QusapWeaponDefinition CreateDefinition()
        {
            return new QusapWeaponDefinition("training_sword", "Training Sword");
        }

        private static QusapWeaponInstance CreateWeapon(ulong id = 1)
        {
            return new QusapWeaponInstance(id, CreateDefinition());
        }

        private static QusapWeaponEquipmentState ArmedEquipment(
            out QusapWeaponInstance weapon)
        {
            QusapWeaponEquipmentState equipment = new(OwnerId);
            weapon = CreateWeapon();
            equipment.TryEquip(weapon, out _);
            return equipment;
        }

        private static GameObject CreateInactiveEquipment(out QusapWeaponEquipment equipment)
        {
            GameObject root = new("WeaponEquipmentEditModeOwner");
            root.SetActive(false);
            QusapCombatController combat = root.AddComponent<QusapCombatController>();
            equipment = root.AddComponent<QusapWeaponEquipment>();
            Assert.That(equipment.TryInitialize(combat), Is.True);
            return root;
        }
    }
}
