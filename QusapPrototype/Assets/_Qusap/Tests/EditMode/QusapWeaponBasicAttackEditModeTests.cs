using NUnit.Framework;
using UnityEngine;

namespace Qusap.Tests
{
    public sealed class QusapWeaponBasicAttackEditModeTests
    {
        [Test]
        public void WeaponLightRequiresEquippedWeapon()
        {
            Assert.That(QusapWeaponAttackRules.CanExecute(QusapCombatCommand.WeaponLight, false), Is.False);
            Assert.That(QusapWeaponAttackRules.CanExecute(QusapCombatCommand.WeaponLight, true), Is.True);
        }

        [Test]
        public void WeaponStrongRequiresEquippedWeapon()
        {
            Assert.That(QusapWeaponAttackRules.CanExecute(QusapCombatCommand.WeaponStrong, false), Is.False);
            Assert.That(QusapWeaponAttackRules.CanExecute(QusapCombatCommand.WeaponStrong, true), Is.True);
        }

        [Test]
        public void BodyAttackRemainsAvailableWhileUnarmed()
        {
            Assert.That(QusapWeaponAttackRules.CanExecute(QusapCombatCommand.BodyAttack, false), Is.True);
        }

        [Test]
        public void ParryRemainsAvailableWhileUnarmed()
        {
            Assert.That(QusapWeaponAttackRules.CanExecute(QusapCombatCommand.Parry, false), Is.True);
        }

        [Test]
        public void NewPressProducesExactlyOneBufferedAttack()
        {
            QusapWeaponAttackInputBuffer buffer = new();
            Assert.That(buffer.TryStore(Press(1), 1d, 0.12f), Is.True);
            Assert.That(buffer.TryConsume(1.01d, out QusapCombatCommandPress consumed), Is.True);
            Assert.That(consumed.PressId, Is.EqualTo(1));
            Assert.That(buffer.TryConsume(1.02d, out _), Is.False);
        }

        [Test]
        public void RepeatedPressIdCannotRepeatAttack()
        {
            QusapWeaponAttackInputBuffer buffer = new();
            QusapCombatCommandPress press = Press(7);
            Assert.That(buffer.TryStore(press, 1d, 0.12f), Is.True);
            Assert.That(buffer.TryConsume(1.01d, out _), Is.True);
            Assert.That(buffer.TryStore(press, 1.02d, 0.12f), Is.False);
        }

        [Test]
        public void RecoveryBufferHasCapacityOneAndNewestValidPressWins()
        {
            QusapWeaponAttackInputBuffer buffer = new();
            buffer.TryStore(Press(1), 1d, 0.12f);
            buffer.TryStore(Press(2, QusapCombatCommand.WeaponStrong), 1.01d, 0.12f);
            Assert.That(buffer.PendingCount, Is.EqualTo(1));
            Assert.That(buffer.TryConsume(1.02d, out QusapCombatCommandPress consumed), Is.True);
            Assert.That(consumed.PressId, Is.EqualTo(2));
            Assert.That(consumed.Command, Is.EqualTo(QusapCombatCommand.WeaponStrong));
        }

        [Test]
        public void BufferedPressExecutesOnlyOnce()
        {
            QusapWeaponAttackInputBuffer buffer = StoredBuffer();
            Assert.That(buffer.TryConsume(1.05d, out _), Is.True);
            Assert.That(buffer.TryConsume(1.05d, out _), Is.False);
        }

        [Test]
        public void ExpiredBufferedPressDoesNotExecute()
        {
            QusapWeaponAttackInputBuffer buffer = StoredBuffer();
            Assert.That(buffer.TryConsume(1.121d, out _), Is.False);
            Assert.That(buffer.HasPendingPress, Is.False);
        }

        [Test]
        public void HitstunClearRemovesBufferedPress()
        {
            AssertClearedBufferCannotExecute();
        }

        [Test]
        public void DashClearRemovesBufferedPress()
        {
            AssertClearedBufferCannotExecute();
        }

        [Test]
        public void DisarmClearRemovesBufferedPress()
        {
            AssertClearedBufferCannotExecute();
        }

        [Test]
        public void RespawnClearRemovesBufferedPress()
        {
            AssertClearedBufferCannotExecute();
        }

        [Test]
        public void SwapClearPreventsOldPressFromExecuting()
        {
            AssertClearedBufferCannotExecute();
        }

        [Test]
        public void SpeedMultiplierScalesAllThreeAuthoritativePhases()
        {
            QusapWeaponAttackData data = CreateData(2f);
            Assert.That(data.StartupTime, Is.EqualTo(0.1f).Within(0.0001f));
            Assert.That(data.ActiveDuration, Is.EqualTo(0.05f).Within(0.0001f));
            Assert.That(data.RecoveryTime, Is.EqualTo(0.2f).Within(0.0001f));
        }

        [Test]
        public void SpeedMultiplierDoesNotChangeDamageHitstunOrKnockback()
        {
            QusapWeaponAttackData data = CreateData(3f);
            Assert.That(data.Damage, Is.EqualTo(5f));
            Assert.That(data.HitstunDuration, Is.EqualTo(0.3f));
            Assert.That(data.HorizontalKnockback, Is.EqualTo(6f));
            Assert.That(data.VerticalKnockback, Is.EqualTo(-2f));
        }

        [Test]
        public void NegativeAndNonFiniteAttackValuesAreNormalized()
        {
            QusapWeaponAttackData data = new(
                QusapWeaponAttackKind.Light,
                float.NaN, -2f, float.PositiveInfinity, float.NaN,
                -1f, float.NegativeInfinity, -3f, float.NaN,
                new Vector2(float.NaN, 1f), new Vector2(-1f, float.PositiveInfinity),
                float.NaN, false, 0f);
            Assert.That(float.IsFinite(data.StartupTime), Is.True);
            Assert.That(data.StartupTime, Is.GreaterThanOrEqualTo(0f));
            Assert.That(data.ActiveDuration, Is.GreaterThan(0f));
            Assert.That(data.RecoveryTime, Is.GreaterThanOrEqualTo(0f));
            Assert.That(data.SpeedMultiplier, Is.EqualTo(QusapWeaponAttackData.MinimumSpeedMultiplier));
            Assert.That(data.HitboxSize.x, Is.GreaterThan(0f));
            Assert.That(data.HitboxSize.y, Is.GreaterThan(0f));
        }

        [Test]
        public void VisualProfileProducesDeterministicPose()
        {
            QusapWeaponAttackVisualProfile profile = QusapWeaponAttackVisualProfile.CreateLight();
            QusapWeaponVisualPose first = profile.Evaluate(QusapAttackPhase.Active, 0.5f, 1);
            QusapWeaponVisualPose second = profile.Evaluate(QusapAttackPhase.Active, 0.5f, 1);
            Assert.That(second.LocalPosition, Is.EqualTo(first.LocalPosition));
            Assert.That(second.LocalEulerAngles, Is.EqualTo(first.LocalEulerAngles));
        }

        [Test]
        public void VisualProfileNormalizesNonFiniteValues()
        {
            QusapWeaponAttackVisualProfile profile = new(
                new Vector3(float.NaN, 1f, 0f),
                new Vector3(0f, float.PositiveInfinity, 0f),
                Vector3.zero,
                Vector3.zero,
                Vector3.zero,
                Vector3.zero,
                (QusapWeaponVisualInterpolation)99,
                float.NaN);
            QusapWeaponVisualPose pose = profile.Evaluate(QusapAttackPhase.Active, float.NaN, 1);
            Assert.That(float.IsFinite(pose.LocalPosition.x), Is.True);
            Assert.That(float.IsFinite(pose.LocalEulerAngles.z), Is.True);
            Assert.That(profile.ArcAmplitude, Is.Zero);
        }

        [Test]
        public void CanceledOrIdleVisualPoseIsExactlyRestOffset()
        {
            QusapWeaponVisualPose pose = QusapWeaponAttackVisualProfile.CreateStrong()
                .Evaluate(QusapAttackPhase.Idle, 0.7f, -1);
            Assert.That(pose.LocalPosition, Is.EqualTo(Vector3.zero));
            Assert.That(pose.LocalEulerAngles, Is.EqualTo(Vector3.zero));
        }

        [Test]
        public void CapturedFacingRemainsStableAndMirrorsPose()
        {
            QusapWeaponAttackVisualProfile profile = QusapWeaponAttackVisualProfile.CreateStrong();
            const int capturedFacing = -1;
            QusapWeaponVisualPose beforeExternalFacingChange =
                profile.Evaluate(QusapAttackPhase.Active, 0.5f, capturedFacing);
            int unrelatedCurrentFacing = 1;
            QusapWeaponVisualPose afterExternalFacingChange =
                profile.Evaluate(QusapAttackPhase.Active, 0.5f, capturedFacing);
            QusapWeaponVisualPose opposite =
                profile.Evaluate(QusapAttackPhase.Active, 0.5f, unrelatedCurrentFacing);
            Assert.That(afterExternalFacingChange.LocalPosition,
                Is.EqualTo(beforeExternalFacingChange.LocalPosition));
            Assert.That(afterExternalFacingChange.LocalEulerAngles,
                Is.EqualTo(beforeExternalFacingChange.LocalEulerAngles));
            Assert.That(opposite.LocalEulerAngles.z,
                Is.EqualTo(-beforeExternalFacingChange.LocalEulerAngles.z).Within(0.0001f));
        }

        private static QusapCombatCommandPress Press(
            ulong id,
            QusapCombatCommand command = QusapCombatCommand.WeaponLight)
        {
            return new QusapCombatCommandPress(command, id, 1d);
        }

        private static QusapWeaponAttackInputBuffer StoredBuffer()
        {
            QusapWeaponAttackInputBuffer buffer = new();
            buffer.TryStore(Press(1), 1d, 0.12f);
            return buffer;
        }

        private static void AssertClearedBufferCannotExecute()
        {
            QusapWeaponAttackInputBuffer buffer = StoredBuffer();
            buffer.Clear();
            Assert.That(buffer.HasPendingPress, Is.False);
            Assert.That(buffer.TryConsume(1.01d, out _), Is.False);
        }

        private static QusapWeaponAttackData CreateData(float speed)
        {
            return new QusapWeaponAttackData(
                QusapWeaponAttackKind.Light,
                0.2f, 0.1f, 0.4f, 0.12f,
                5f, 0.3f, 6f, -2f,
                new Vector2(0.8f, 0f), new Vector2(1f, 0.6f),
                1f, false, speed);
        }
    }
}
