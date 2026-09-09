using NUnit.Framework;
using UnityEngine;

namespace Qusap.Tests
{
    public sealed class QusapCombatFeedbackEditModeTests
    {
        [Test]
        public void DefaultFeedbackSettingsAreValid()
        {
            QusapCombatFeedbackSettings settings = QusapCombatFeedbackSettings.CreateDefault();

            Assert.That(settings.FeedbackEnabled, Is.True);
            Assert.That(settings.PoolCapacity, Is.EqualTo(8));
            Assert.That(settings.DamageDuration, Is.EqualTo(0.22f));
            Assert.That(settings.LaunchDuration, Is.EqualTo(0.28f));
            Assert.That(settings.DisarmDuration, Is.EqualTo(0.25f));
            Assert.That(settings.WhiffDuration, Is.EqualTo(0.16f));
            Assert.That(settings.FailedParryDuration, Is.EqualTo(0.18f));
        }

        [TestCase(0, 1)]
        [TestCase(-10, 1)]
        [TestCase(33, 32)]
        [TestCase(1000, 32)]
        public void PoolCapacityIsClampedToSafeRange(int requested, int expected)
        {
            QusapCombatFeedbackSettings settings = CreateSettings(poolCapacity: requested);
            Assert.That(settings.PoolCapacity, Is.EqualTo(expected));
        }

        [Test]
        public void NegativeDurationIsNormalized()
        {
            QusapCombatFeedbackSettings settings = CreateSettings(damageDuration: -1f);
            Assert.That(
                settings.DamageDuration,
                Is.EqualTo(QusapCombatFeedbackSettings.DefaultDamageDuration));
        }

        [TestCase(float.NaN)]
        [TestCase(float.PositiveInfinity)]
        [TestCase(float.NegativeInfinity)]
        public void NonFiniteDurationIsRejectedOrNormalized(float duration)
        {
            QusapCombatFeedbackSettings settings = CreateSettings(damageDuration: duration);
            Assert.That(
                settings.DamageDuration,
                Is.EqualTo(QusapCombatFeedbackSettings.DefaultDamageDuration));
        }

        [TestCase(0f)]
        [TestCase(-1f)]
        [TestCase(float.NaN)]
        public void InvalidSizeIsNormalized(float size)
        {
            QusapCombatFeedbackSettings settings = CreateSettings(damageMaximumSize: size);
            Assert.That(
                settings.DamageMaximumSize,
                Is.EqualTo(QusapCombatFeedbackSettings.DefaultDamageMaximumSize));
        }

        [Test]
        public void NonFiniteOffsetIsRejectedOrNormalized()
        {
            QusapCombatFeedbackSettings settings = CreateSettings(
                targetOffset: new Vector3(float.NaN, 2f, 3f));
            Assert.That(settings.TargetOffset, Is.EqualTo(QusapCombatFeedbackSettings.DefaultTargetOffset));
        }

        [Test]
        public void DefaultDamageColorsAreVisible()
        {
            QusapCombatFeedbackSettings settings = QusapCombatFeedbackSettings.CreateDefault();
            AssertVisible(settings.DamageColor);
            AssertVisible(settings.DamageCenterColor);
            Assert.That(settings.DamageColor.r, Is.GreaterThan(settings.DamageColor.b));
        }

        [Test]
        public void DefaultLaunchColorIsVisible()
        {
            Color color = QusapCombatFeedbackSettings.CreateDefault().LaunchColor;
            AssertVisible(color);
            Assert.That(color.b, Is.GreaterThan(color.r));
            Assert.That(color.g, Is.GreaterThan(color.r));
        }

        [Test]
        public void DefaultDisarmColorIsVisible()
        {
            QusapCombatFeedbackSettings settings = QusapCombatFeedbackSettings.CreateDefault();
            AssertVisible(settings.DisarmColor);
            AssertVisible(settings.DisarmSuccessCenterColor);
            Assert.That(settings.DisarmColor.b, Is.GreaterThan(settings.DisarmColor.g));
        }

        [Test]
        public void DefaultFailureColorIsVisible()
        {
            Color color = QusapCombatFeedbackSettings.CreateDefault().FailedParryColor;
            AssertVisible(color);
            Assert.That(color.r, Is.GreaterThan(color.g));
            Assert.That(color.r, Is.GreaterThan(color.b));
        }

        private static QusapCombatFeedbackSettings CreateSettings(
            int poolCapacity = QusapCombatFeedbackSettings.DefaultPoolCapacity,
            float damageDuration = QusapCombatFeedbackSettings.DefaultDamageDuration,
            float damageMaximumSize = QusapCombatFeedbackSettings.DefaultDamageMaximumSize,
            Vector3? targetOffset = null)
        {
            return new QusapCombatFeedbackSettings(
                poolCapacity,
                damageDuration,
                damageMaximumSize,
                targetOffset ?? QusapCombatFeedbackSettings.DefaultTargetOffset);
        }

        private static void AssertVisible(Color color)
        {
            Assert.That(float.IsNaN(color.r) || float.IsInfinity(color.r), Is.False);
            Assert.That(float.IsNaN(color.g) || float.IsInfinity(color.g), Is.False);
            Assert.That(float.IsNaN(color.b) || float.IsInfinity(color.b), Is.False);
            Assert.That(color.a, Is.GreaterThan(0f));
        }
    }
}
