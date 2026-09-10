using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.InputSystem;

namespace Qusap.Tests
{
    public sealed class QusapWeaponSwapThrowEditModeTests
    {
        private const string ControlsPath = "Assets/_Qusap/Settings/QusapControls.inputactions";
        private const ulong OwnerId = 101;

        [Test]
        public void PlayerOneSwapThrowIsBoundOnlyToE()
        {
            InputAction action = LoadAction();

            Assert.That(action.bindings.Count, Is.EqualTo(2));
            Assert.That(action.bindings[0].path, Is.EqualTo("<Keyboard>/e"));
            Assert.That(action.bindings[0].groups, Is.EqualTo("Player1"));
        }

        [Test]
        public void PlayerTwoSwapThrowIsBoundOnlyToRightTrigger()
        {
            InputAction action = LoadAction();

            Assert.That(action.bindings[1].path, Is.EqualTo("<Gamepad>/rightTrigger"));
            Assert.That(action.bindings[1].groups, Is.EqualTo("Player2"));
        }

        [Test]
        public void HeldButtonCreatesExactlyOnePress()
        {
            QusapWeaponSwapThrowInputBuffer buffer = new();

            Assert.That(buffer.SetButtonState(true, 0f, 1, 1d, out _), Is.True);
            Assert.That(buffer.SetButtonState(true, 1f, -1, 2d, out _), Is.False);
            Assert.That(buffer.TryConsume(out _), Is.True);
            Assert.That(buffer.TryConsume(out _), Is.False);
        }

        [Test]
        public void ReleaseAllowsOneNewPressWithMonotonicId()
        {
            QusapWeaponSwapThrowInputBuffer buffer = new();
            buffer.SetButtonState(true, 0f, 1, 1d, out QusapWeaponSwapThrowPress first);
            buffer.SetButtonState(false, 0f, 1, 2d, out _);

            Assert.That(
                buffer.SetButtonState(true, 0f, 1, 3d, out QusapWeaponSwapThrowPress second),
                Is.True);
            Assert.That(second.PressId, Is.EqualTo(first.PressId + 1));
        }

        [Test]
        public void DistinctEdgesQueueWithoutLosingEitherPress()
        {
            QusapWeaponSwapThrowInputBuffer buffer = new();
            buffer.SetButtonState(true, 0f, 1, 1d, out QusapWeaponSwapThrowPress first);
            buffer.SetButtonState(false, 0f, 1, 2d, out _);
            buffer.SetButtonState(true, 1f, -1, 3d, out QusapWeaponSwapThrowPress second);

            Assert.That(buffer.PendingCount, Is.EqualTo(2));
            Assert.That(buffer.TryConsume(out QusapWeaponSwapThrowPress consumedFirst), Is.True);
            Assert.That(buffer.TryConsume(out QusapWeaponSwapThrowPress consumedSecond), Is.True);
            Assert.That(consumedFirst.PressId, Is.EqualTo(first.PressId));
            Assert.That(consumedSecond.PressId, Is.EqualTo(second.PressId));
        }

        [TestCase(0.5f, QusapWeaponThrowDirection.Up)]
        [TestCase(1f, QusapWeaponThrowDirection.Up)]
        [TestCase(0.499f, QusapWeaponThrowDirection.Forward)]
        [TestCase(-1f, QusapWeaponThrowDirection.Forward)]
        public void CapturedVerticalChoosesDirection(
            float vertical,
            QusapWeaponThrowDirection expected)
        {
            QusapWeaponSwapThrowInputBuffer buffer = new();

            buffer.SetButtonState(true, vertical, 1, 4d, out QusapWeaponSwapThrowPress press);

            Assert.That(press.Direction, Is.EqualTo(expected));
        }

        [Test]
        public void FacingIsCapturedAndImmutable()
        {
            QusapWeaponSwapThrowInputBuffer buffer = new();
            buffer.SetButtonState(true, 0f, -1, 5d, out QusapWeaponSwapThrowPress press);
            buffer.SetButtonState(false, 0f, 1, 6d, out _);

            Assert.That(press.FacingDirection, Is.EqualTo(-1));
            Assert.That(press.Timestamp, Is.EqualTo(5d));
        }

        [Test]
        public void ArmedPlayerCanCreateSettledSwapCandidate()
        {
            QusapWeaponPickupResolver resolver = new(1f, 0d);

            Assert.That(resolver.TryCreateSwapCandidate(
                Player(true, Vector3.zero),
                Dropped(3, new Vector3(0.5f, 0f, 0f)),
                1d,
                out _), Is.True);
        }

        [Test]
        public void UnarmedPlayerCannotCreateSwapCandidate()
        {
            QusapWeaponPickupResolver resolver = new(1f, 0d);

            Assert.That(resolver.TryCreateSwapCandidate(
                Player(false, Vector3.zero),
                Dropped(3, Vector3.zero),
                1d,
                out _), Is.False);
        }

        [TestCase(false, true, 0.25f)]
        [TestCase(true, false, 0.25f)]
        [TestCase(true, true, 1.01f)]
        public void IneligibleDroppedWeaponCannotBecomeSwapCandidate(
            bool isFree,
            bool isSettled,
            float distance)
        {
            QusapWeaponPickupResolver resolver = new(1f, 0d);
            QusapDroppedWeaponSnapshot dropped = new(
                3,
                isFree,
                isSettled,
                false,
                null,
                0d,
                new Vector3(distance, 0f, 0f));

            Assert.That(resolver.TryCreateSwapCandidate(
                Player(true, Vector3.zero), dropped, 1d, out _), Is.False);
        }

        [Test]
        public void SwapSelectionPrefersClosestWeapon()
        {
            QusapWeaponPickupResolver resolver = new(2f, 0d);
            QusapDroppedWeaponSnapshot[] drops =
            {
                Dropped(3, new Vector3(1.5f, 0f, 0f)),
                Dropped(4, new Vector3(0.5f, 0f, 0f))
            };

            Assert.That(resolver.TrySelectBestSwap(
                Player(true, Vector3.zero), drops, 1d, out QusapWeaponPickupCandidate selected),
                Is.True);
            Assert.That(selected.WeaponInstanceId, Is.EqualTo(4UL));
        }

        [Test]
        public void EqualDistanceSwapSelectionPrefersLowestInstanceId()
        {
            QusapWeaponPickupResolver resolver = new(2f, 0d);
            QusapDroppedWeaponSnapshot[] drops =
            {
                Dropped(9, new Vector3(-0.5f, 0f, 0f)),
                Dropped(4, new Vector3(0.5f, 0f, 0f))
            };

            resolver.TrySelectBestSwap(
                Player(true, Vector3.zero), drops, 1d, out QusapWeaponPickupCandidate selected);

            Assert.That(selected.WeaponInstanceId, Is.EqualTo(4UL));
        }

        [Test]
        public void VoluntarySwapPreservesBothIdentitiesAndCommitsOwnersAtomically()
        {
            QusapWeaponEquipmentState equipment = Armed(out QusapWeaponInstance original);
            QusapWeaponInstance replacement = Weapon(2);

            QusapWeaponOperationResult result = equipment.TryVoluntarySwap(
                original, replacement, out QusapWeaponSwapTransition swap);

            Assert.That(result, Is.EqualTo(QusapWeaponOperationResult.Success));
            Assert.That(swap.ReleasedWeapon, Is.SameAs(original));
            Assert.That(swap.EquippedWeapon, Is.SameAs(replacement));
            Assert.That(original.IsFree, Is.True);
            Assert.That(replacement.OwnerEntityId, Is.EqualTo(OwnerId));
            Assert.That(equipment.EquippedWeapon, Is.SameAs(replacement));
            Assert.That(swap.ReleaseTransition.Type,
                Is.EqualTo(QusapWeaponTransitionType.VoluntarySwapThrow));
            Assert.That(swap.EquipTransition.Type,
                Is.EqualTo(QusapWeaponTransitionType.Equipped));
        }

        [Test]
        public void OccupiedReplacementRejectsWithoutChangingOriginal()
        {
            QusapWeaponEquipmentState equipment = Armed(out QusapWeaponInstance original);
            QusapWeaponEquipmentState other = new(202);
            QusapWeaponInstance replacement = Weapon(2);
            other.TryEquip(replacement, out _);
            ulong revision = equipment.Revision;

            QusapWeaponOperationResult result = equipment.TryVoluntarySwap(
                original, replacement, out _);

            Assert.That(result, Is.EqualTo(QusapWeaponOperationResult.WeaponAlreadyOwned));
            Assert.That(equipment.EquippedWeapon, Is.SameAs(original));
            Assert.That(original.OwnerEntityId, Is.EqualTo(OwnerId));
            Assert.That(replacement.OwnerEntityId, Is.EqualTo(202UL));
            Assert.That(equipment.Revision, Is.EqualTo(revision));
        }

        [Test]
        public void StaleExpectedWeaponRejectsWithoutChangingEitherWeapon()
        {
            QusapWeaponEquipmentState equipment = Armed(out QusapWeaponInstance original);
            QusapWeaponInstance stale = Weapon(2);
            QusapWeaponInstance replacement = Weapon(3);

            QusapWeaponOperationResult result = equipment.TryVoluntarySwap(
                stale, replacement, out _);

            Assert.That(result, Is.EqualTo(QusapWeaponOperationResult.InconsistentState));
            Assert.That(equipment.EquippedWeapon, Is.SameAs(original));
            Assert.That(original.OwnerEntityId, Is.EqualTo(OwnerId));
            Assert.That(replacement.IsFree, Is.True);
        }

        [Test]
        public void InvalidTrajectoryValuesNormalizeToRequestedDefaults()
        {
            QusapWeaponThrowTrajectoryProfile profile = new(
                float.NaN, -1f, 0f, 4.5f, 1.2f, 0.65f);

            Assert.That(profile.HorizontalDistance, Is.EqualTo(4.5f));
            Assert.That(profile.ArcHeight, Is.EqualTo(1.2f));
            Assert.That(profile.Duration, Is.EqualTo(0.65f));
        }

        [Test]
        public void ExcessiveTrajectoryValuesAreCapped()
        {
            QusapWeaponThrowTrajectoryProfile profile = new(
                100f, 100f, 100f, 4.5f, 1.2f, 0.65f);

            Assert.That(profile.HorizontalDistance,
                Is.EqualTo(QusapWeaponThrowTrajectoryProfile.MaximumDistance));
            Assert.That(profile.ArcHeight,
                Is.EqualTo(QusapWeaponThrowTrajectoryProfile.MaximumHeight));
            Assert.That(profile.Duration,
                Is.EqualTo(QusapWeaponThrowTrajectoryProfile.MaximumDuration));
        }

        private static InputAction LoadAction()
        {
            InputActionAsset asset = AssetDatabase.LoadAssetAtPath<InputActionAsset>(ControlsPath);
            Assert.That(asset, Is.Not.Null);
            InputAction action = asset.FindAction("Gameplay/WeaponSwapThrow");
            Assert.That(action, Is.Not.Null);
            return action;
        }

        private static QusapWeaponPickupPlayerSnapshot Player(
            bool hasWeapon,
            Vector3 position)
        {
            return new QusapWeaponPickupPlayerSnapshot(OwnerId, true, hasWeapon, position);
        }

        private static QusapDroppedWeaponSnapshot Dropped(ulong id, Vector3 position)
        {
            return new QusapDroppedWeaponSnapshot(
                id, true, true, false, null, 0d, position);
        }

        private static QusapWeaponEquipmentState Armed(out QusapWeaponInstance weapon)
        {
            QusapWeaponEquipmentState equipment = new(OwnerId);
            weapon = Weapon(1);
            equipment.TryEquip(weapon, out _);
            return equipment;
        }

        private static QusapWeaponInstance Weapon(ulong id)
        {
            return new QusapWeaponInstance(
                id,
                new QusapWeaponDefinition("test_sword", "Test Sword"));
        }
    }
}
