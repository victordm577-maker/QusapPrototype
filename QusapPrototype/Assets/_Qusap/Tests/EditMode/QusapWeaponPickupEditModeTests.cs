using NUnit.Framework;
using UnityEngine;

namespace Qusap.Tests
{
    public sealed class QusapWeaponPickupEditModeTests
    {
        private const ulong PlayerOneId = 101;
        private const ulong PlayerTwoId = 202;

        [Test]
        public void DroppedInstanceStartsWithoutOwner()
        {
            QusapWeaponEquipmentState equipment = new(PlayerOneId);
            QusapWeaponInstance weapon = CreateWeapon(7);
            Assert.That(equipment.TryEquip(weapon, out _),
                Is.EqualTo(QusapWeaponOperationResult.Success));

            Assert.That(equipment.TryDisarm(PlayerTwoId, out QusapWeaponInstance dropped, out _),
                Is.EqualTo(QusapWeaponOperationResult.Success));
            Assert.That(dropped, Is.SameAs(weapon));
            Assert.That(dropped.OwnerEntityId, Is.Null);
            Assert.That(dropped.IsFree, Is.True);
        }

        [Test]
        public void UnarmedPlayerInsideRadiusIsEligible()
        {
            QusapWeaponPickupResolver resolver = new();

            Assert.That(resolver.TryCreateCandidate(
                Player(PlayerOneId, false, Vector3.zero),
                Drop(7, new Vector3(0.85f, 0f, 0f)),
                5d,
                out _), Is.True);
        }

        [Test]
        public void PlayerOutsideRadiusIsNotEligible()
        {
            QusapWeaponPickupResolver resolver = new();

            Assert.That(resolver.TryCreateCandidate(
                Player(PlayerOneId, false, Vector3.zero),
                Drop(7, new Vector3(0.851f, 0f, 0f)),
                5d,
                out _), Is.False);
        }

        [Test]
        public void ArmedPlayerIsNotEligible()
        {
            QusapWeaponPickupResolver resolver = new();

            Assert.That(resolver.TryCreateCandidate(
                Player(PlayerOneId, true, Vector3.zero),
                Drop(7, Vector3.zero),
                5d,
                out _), Is.False);
        }

        [TestCase(false, true, true, false)]
        [TestCase(true, false, true, false)]
        [TestCase(true, true, false, false)]
        [TestCase(true, true, true, true)]
        public void InactiveOwnedUnsettledOrClaimedDropIsNotEligible(
            bool free,
            bool settled,
            bool active,
            bool claimed)
        {
            QusapWeaponPickupResolver resolver = new();
            QusapWeaponPickupPlayerSnapshot player =
                new(PlayerOneId, active, false, Vector3.zero);
            QusapDroppedWeaponSnapshot drop = new(
                7,
                free,
                settled,
                claimed,
                null,
                0d,
                Vector3.zero);

            Assert.That(resolver.TryCreateCandidate(player, drop, 1d, out _), Is.False);
        }

        [Test]
        public void FormerOwnerIsRejectedDuringLockout()
        {
            QusapWeaponPickupResolver resolver = new();

            Assert.That(resolver.TryCreateCandidate(
                Player(PlayerOneId, false, Vector3.zero),
                Drop(7, Vector3.zero, PlayerOneId, 10d),
                10.999d,
                out _), Is.False);
        }

        [Test]
        public void FormerOwnerIsEligibleExactlyWhenLockoutEnds()
        {
            QusapWeaponPickupResolver resolver = new();

            Assert.That(resolver.TryCreateCandidate(
                Player(PlayerOneId, false, Vector3.zero),
                Drop(7, Vector3.zero, PlayerOneId, 10d),
                11d,
                out _), Is.True);
        }

        [Test]
        public void FormerOwnerIsEligibleAtComputedFractionalLockoutBoundary()
        {
            QusapWeaponPickupResolver resolver = new();
            const double droppedAt = 1.3d;
            double lockoutBoundary = droppedAt + resolver.PreviousOwnerLockout;

            Assert.That(resolver.TryCreateCandidate(
                Player(PlayerOneId, false, Vector3.zero),
                Drop(7, Vector3.zero, PlayerOneId, droppedAt),
                lockoutBoundary,
                out _), Is.True);
        }

        [Test]
        public void RegressiveTimestampCannotEndLockoutEarly()
        {
            QusapWeaponPickupResolver resolver = new();
            QusapWeaponPickupPlayerSnapshot player =
                Player(PlayerOneId, false, Vector3.zero);
            QusapDroppedWeaponSnapshot drop =
                Drop(7, Vector3.zero, PlayerOneId, 10d);

            Assert.That(resolver.TryCreateCandidate(player, drop, 10.75d, out _), Is.False);
            Assert.That(resolver.TryCreateCandidate(player, drop, 10.50d, out _), Is.False);
            Assert.That(resolver.LastAcceptedTimestamp, Is.EqualTo(10.75d));
        }

        [Test]
        public void NonFiniteTimestampIsRejectedWithoutPoisoningClock()
        {
            QusapWeaponPickupResolver resolver = new();
            QusapWeaponPickupPlayerSnapshot player =
                Player(PlayerOneId, false, Vector3.zero);
            QusapDroppedWeaponSnapshot drop = Drop(7, Vector3.zero);

            Assert.That(resolver.TryCreateCandidate(player, drop, double.NaN, out _), Is.False);
            Assert.That(resolver.TryCreateCandidate(
                player, drop, double.PositiveInfinity, out _), Is.False);
            Assert.That(resolver.HasAcceptedTimestamp, Is.False);
            Assert.That(resolver.TryCreateCandidate(player, drop, 0d, out _), Is.True);
        }

        [Test]
        public void ClosestPlayerWinsSameWeapon()
        {
            QusapWeaponPickupResolver resolver = new();
            QusapWeaponPickupPlayerSnapshot[] players =
            {
                Player(PlayerOneId, false, new Vector3(0.70f, 0f, 0f)),
                Player(PlayerTwoId, false, new Vector3(0.20f, 0f, 0f))
            };
            QusapDroppedWeaponSnapshot[] drops = { Drop(7, Vector3.zero) };

            Assert.That(resolver.TrySelectBest(players, drops, 1d, out var selected), Is.True);
            Assert.That(selected.PlayerEntityId, Is.EqualTo(PlayerTwoId));
        }

        [Test]
        public void EqualPlayerDistanceUsesLowerEntityId()
        {
            QusapWeaponPickupResolver resolver = new();
            QusapWeaponPickupPlayerSnapshot[] players =
            {
                Player(PlayerTwoId, false, new Vector3(-0.5f, 0f, 0f)),
                Player(PlayerOneId, false, new Vector3(0.5f, 0f, 0f))
            };
            QusapDroppedWeaponSnapshot[] drops = { Drop(7, Vector3.zero) };

            Assert.That(resolver.TrySelectBest(players, drops, 1d, out var selected), Is.True);
            Assert.That(selected.PlayerEntityId, Is.EqualTo(PlayerOneId));
        }

        [Test]
        public void ClosestWeaponIsSelectedForSamePlayer()
        {
            QusapWeaponPickupResolver resolver = new();
            QusapWeaponPickupPlayerSnapshot[] players =
            {
                Player(PlayerOneId, false, Vector3.zero)
            };
            QusapDroppedWeaponSnapshot[] drops =
            {
                Drop(8, new Vector3(0.70f, 0f, 0f)),
                Drop(7, new Vector3(0.20f, 0f, 0f))
            };

            Assert.That(resolver.TrySelectBest(players, drops, 1d, out var selected), Is.True);
            Assert.That(selected.WeaponInstanceId, Is.EqualTo(7UL));
        }

        [Test]
        public void EqualWeaponDistanceUsesLowerInstanceId()
        {
            QusapWeaponPickupResolver resolver = new();
            QusapWeaponPickupPlayerSnapshot[] players =
            {
                Player(PlayerOneId, false, Vector3.zero)
            };
            QusapDroppedWeaponSnapshot[] drops =
            {
                Drop(8, new Vector3(-0.5f, 0f, 0f)),
                Drop(7, new Vector3(0.5f, 0f, 0f))
            };

            Assert.That(resolver.TrySelectBest(players, drops, 1d, out var selected), Is.True);
            Assert.That(selected.WeaponInstanceId, Is.EqualTo(7UL));
        }

        [Test]
        public void InvalidAndAbsurdConfigurationIsNormalized()
        {
            Assert.That(
                QusapWeaponPickupResolver.NormalizePickupRadius(-1f),
                Is.EqualTo(QusapWeaponPickupResolver.DefaultPickupRadius));
            Assert.That(
                QusapWeaponPickupResolver.NormalizePickupRadius(float.NaN),
                Is.EqualTo(QusapWeaponPickupResolver.DefaultPickupRadius));
            Assert.That(
                QusapWeaponPickupResolver.NormalizePickupRadius(float.MaxValue),
                Is.EqualTo(QusapWeaponPickupResolver.MaximumPickupRadius));
            Assert.That(
                QusapWeaponPickupResolver.NormalizePreviousOwnerLockout(-1d),
                Is.EqualTo(QusapWeaponPickupResolver.DefaultPreviousOwnerLockout));
            Assert.That(
                QusapWeaponPickupResolver.NormalizePreviousOwnerLockout(double.NaN),
                Is.EqualTo(QusapWeaponPickupResolver.DefaultPreviousOwnerLockout));
            Assert.That(
                QusapWeaponPickupResolver.NormalizePreviousOwnerLockout(double.MaxValue),
                Is.EqualTo(QusapWeaponPickupResolver.MaximumPreviousOwnerLockout));
        }

        private static QusapWeaponPickupPlayerSnapshot Player(
            ulong entityId,
            bool armed,
            Vector3 position)
        {
            return new QusapWeaponPickupPlayerSnapshot(
                entityId,
                true,
                armed,
                position);
        }

        private static QusapDroppedWeaponSnapshot Drop(
            ulong instanceId,
            Vector3 position,
            ulong? previousOwnerId = null,
            double droppedAt = 0d)
        {
            return new QusapDroppedWeaponSnapshot(
                instanceId,
                true,
                true,
                false,
                previousOwnerId,
                droppedAt,
                position);
        }

        private static QusapWeaponInstance CreateWeapon(ulong instanceId)
        {
            return new QusapWeaponInstance(
                instanceId,
                new QusapWeaponDefinition("training_sword", "Training Sword"));
        }
    }
}
