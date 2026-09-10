using NUnit.Framework;

namespace Qusap.Tests
{
    public sealed class QusapThrownWeaponAttackEditModeTests
    {
        private const ulong ThrowId = 41;
        private const ulong ThrowerId = 101;
        private const ulong WeaponId = 301;

        [Test]
        public void InvalidProfileValuesUseSafeDefaults()
        {
            QusapThrownWeaponAttackProfile profile = new(
                float.NaN,
                float.NegativeInfinity,
                -1f,
                float.PositiveInfinity,
                -0.1f,
                float.NaN);

            Assert.That(profile.Damage,
                Is.EqualTo(QusapThrownWeaponAttackProfile.DefaultDamage));
            Assert.That(profile.HitstunDuration,
                Is.EqualTo(QusapThrownWeaponAttackProfile.DefaultHitstunDuration));
            Assert.That(profile.HorizontalKnockback,
                Is.EqualTo(QusapThrownWeaponAttackProfile.DefaultHorizontalKnockback));
            Assert.That(profile.VerticalKnockback,
                Is.EqualTo(QusapThrownWeaponAttackProfile.DefaultVerticalKnockback));
            Assert.That(profile.DetectionRadius,
                Is.EqualTo(QusapThrownWeaponAttackProfile.DefaultDetectionRadius));
            Assert.That(profile.OffensiveDuration,
                Is.EqualTo(QusapThrownWeaponAttackProfile.DefaultOffensiveDuration));
        }

        [Test]
        public void ExcessiveProfileValuesAreCapped()
        {
            QusapThrownWeaponAttackProfile profile = new(
                float.MaxValue,
                float.MaxValue,
                float.MaxValue,
                float.MaxValue,
                float.MaxValue,
                float.MaxValue);

            Assert.That(profile.Damage,
                Is.EqualTo(QusapThrownWeaponAttackProfile.MaximumDamage));
            Assert.That(profile.HitstunDuration,
                Is.EqualTo(QusapThrownWeaponAttackProfile.MaximumHitstunDuration));
            Assert.That(profile.HorizontalKnockback,
                Is.EqualTo(QusapThrownWeaponAttackProfile.MaximumKnockback));
            Assert.That(profile.VerticalKnockback,
                Is.EqualTo(QusapThrownWeaponAttackProfile.MaximumKnockback));
            Assert.That(profile.DetectionRadius,
                Is.EqualTo(QusapThrownWeaponAttackProfile.MaximumDetectionRadius));
            Assert.That(profile.OffensiveDuration,
                Is.EqualTo(QusapThrownWeaponAttackProfile.MaximumOffensiveDuration));
        }

        [Test]
        public void VoluntaryThrowBeginsOffensive()
        {
            QusapThrownWeaponAttackState state = Voluntary();

            Assert.That(state.IsOffensive, Is.True);
            Assert.That(state.ImpactConsumed, Is.False);
        }

        [Test]
        public void DisarmDropNeverBeginsOffensive()
        {
            QusapThrownWeaponAttackState state = new(
                0,
                0,
                WeaponId,
                QusapWeaponReleaseType.Disarmed,
                QusapWeaponThrowDirection.Forward,
                1,
                10d,
                1d);

            Assert.That(state.IsOffensive, Is.False);
            Assert.That(state.TryAdvance(10.5d), Is.False);
        }

        [Test]
        public void StatePreservesCapturedIdentityAndThrowMetadata()
        {
            QusapThrownWeaponAttackState state = new(
                ThrowId,
                ThrowerId,
                WeaponId,
                QusapWeaponReleaseType.VoluntarySwapThrow,
                QusapWeaponThrowDirection.Up,
                -1,
                10d,
                0.6d);

            Assert.That(state.ThrowId, Is.EqualTo(ThrowId));
            Assert.That(state.ThrowerEntityId, Is.EqualTo(ThrowerId));
            Assert.That(state.WeaponInstanceId, Is.EqualTo(WeaponId));
            Assert.That(state.ReleaseType,
                Is.EqualTo(QusapWeaponReleaseType.VoluntarySwapThrow));
            Assert.That(state.Direction, Is.EqualTo(QusapWeaponThrowDirection.Up));
            Assert.That(state.CapturedFacingDirection, Is.EqualTo(-1));
        }

        [Test]
        public void ConsumedImpactCannotBeConfirmedAgain()
        {
            QusapThrownWeaponAttackState state = Voluntary();

            Assert.That(state.TryConsumeImpact(202, 10.1d), Is.True);
            Assert.That(state.TryConsumeImpact(203, 10.2d), Is.False);
            Assert.That(state.IsOffensive, Is.False);
        }

        [Test]
        public void SettlingPermanentlyDisablesOffense()
        {
            QusapThrownWeaponAttackState state = Voluntary();

            Assert.That(state.MarkSettled(10.2d), Is.True);
            Assert.That(state.TryAdvance(10.3d), Is.False);
            Assert.That(state.TryConsumeImpact(202, 10.3d), Is.False);
            Assert.That(state.IsSettled, Is.True);
        }

        [Test]
        public void RegressiveTimestampDoesNotReactivateAttack()
        {
            QusapThrownWeaponAttackState state = Voluntary();
            state.TryAdvance(10.4d);

            Assert.That(state.TryAdvance(10.2d), Is.False);
            Assert.That(state.LastAcceptedTimestamp, Is.EqualTo(10.4d));
            Assert.That(state.IsOffensive, Is.True);
        }

        [TestCase(double.NaN)]
        [TestCase(double.PositiveInfinity)]
        [TestCase(double.NegativeInfinity)]
        public void NonFiniteTimestampCannotChangeAttack(double timestamp)
        {
            QusapThrownWeaponAttackState state = Voluntary();

            Assert.That(state.TryAdvance(timestamp), Is.False);
            Assert.That(state.LastAcceptedTimestamp, Is.EqualTo(10d));
            Assert.That(state.IsOffensive, Is.True);
        }

        [Test]
        public void ExpiredAttackCannotBeReactivatedByEarlierTimestamp()
        {
            QusapThrownWeaponAttackState state = Voluntary();
            state.TryAdvance(10.61d);

            Assert.That(state.IsOffensive, Is.False);
            Assert.That(state.TryAdvance(10.2d), Is.False);
            Assert.That(state.IsOffensive, Is.False);
        }

        [Test]
        public void SelectionPrefersClosestTargetThenLowestEntityId()
        {
            QusapThrownWeaponTargetCandidate closest = new(300, 0.5f);
            QusapThrownWeaponTargetCandidate farther = new(100, 0.6f);
            QusapThrownWeaponTargetCandidate tiedHigher = new(301, 0.5f);

            Assert.That(QusapThrownWeaponTargetCandidate.IsPreferred(
                closest, farther), Is.True);
            Assert.That(QusapThrownWeaponTargetCandidate.IsPreferred(
                closest, tiedHigher), Is.True);
            Assert.That(QusapThrownWeaponTargetCandidate.IsPreferred(
                tiedHigher, closest), Is.False);
        }

        [Test]
        public void ThrowerCannotConsumeOwnThrow()
        {
            QusapThrownWeaponAttackState state = Voluntary();

            Assert.That(state.TryConsumeImpact(ThrowerId, 10.1d), Is.False);
            Assert.That(state.ImpactConsumed, Is.False);
            Assert.That(state.IsOffensive, Is.True);
        }

        [Test]
        public void ThrowIdentifiersAreUniqueAndMonotonic()
        {
            QusapWeaponThrowIdGenerator generator = new();

            ulong first = generator.Next();
            ulong second = generator.Next();

            Assert.That(first, Is.Not.Zero);
            Assert.That(second, Is.EqualTo(first + 1));
        }

        private static QusapThrownWeaponAttackState Voluntary()
        {
            return new QusapThrownWeaponAttackState(
                ThrowId,
                ThrowerId,
                WeaponId,
                QusapWeaponReleaseType.VoluntarySwapThrow,
                QusapWeaponThrowDirection.Forward,
                1,
                10d,
                0.6d);
        }
    }
}
