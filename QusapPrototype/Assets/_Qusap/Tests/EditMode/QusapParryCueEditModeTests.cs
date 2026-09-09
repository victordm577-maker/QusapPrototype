using NUnit.Framework;
using UnityEngine;

namespace Qusap.Tests
{
    public sealed class QusapParryCueEditModeTests
    {
        [Test]
        public void DefaultVisualSettingsAreValid()
        {
            QusapParryCueVisualSettings settings = QusapParryCueVisualSettings.CreateDefault();
            Assert.That(settings.IndicatorEnabled, Is.True);
            Assert.That(settings.MinimumSize, Is.GreaterThan(0f));
            Assert.That(settings.MaximumSize, Is.GreaterThanOrEqualTo(settings.MinimumSize));
            Assert.That(settings.PulseFrequency, Is.GreaterThanOrEqualTo(0f));
            Assert.That(settings.SuccessFlashDuration, Is.GreaterThanOrEqualTo(0f));
        }

        [Test]
        public void InvalidMinimumSizeIsNormalized()
        {
            QusapParryCueVisualSettings settings = Create(minimumSize: 0f);
            Assert.That(settings.MinimumSize, Is.EqualTo(QusapParryCueVisualSettings.DefaultMinimumSize));
        }

        [Test]
        public void MaximumSizeCannotBeLowerThanMinimum()
        {
            QusapParryCueVisualSettings settings = Create(minimumSize: 0.5f, maximumSize: 0.2f);
            Assert.That(settings.MaximumSize, Is.EqualTo(settings.MinimumSize));
        }

        [Test]
        public void NegativePulseFrequencyIsNormalized()
        {
            QusapParryCueVisualSettings settings = Create(pulseFrequency: -1f);
            Assert.That(settings.PulseFrequency, Is.EqualTo(QusapParryCueVisualSettings.DefaultPulseFrequency));
        }

        [Test]
        public void InvalidFlashDurationIsNormalized()
        {
            QusapParryCueVisualSettings settings = Create(successFlashDuration: float.NaN);
            Assert.That(
                settings.SuccessFlashDuration,
                Is.EqualTo(QusapParryCueVisualSettings.DefaultSuccessFlashDuration));
        }

        [Test]
        public void NonFiniteOffsetIsRejectedOrNormalized()
        {
            QusapParryCueVisualSettings settings = Create(
                localOffset: new Vector3(float.PositiveInfinity, 2f, 0f));
            Assert.That(settings.LocalOffset, Is.EqualTo(QusapParryCueVisualSettings.DefaultLocalOffset));
        }

        [Test]
        public void DefaultWindowColorIsVisible()
        {
            Color color = QusapParryCueVisualSettings.CreateDefault().WindowColor;
            Assert.That(color.a, Is.GreaterThan(0f));
            Assert.That(color.maxColorComponent, Is.GreaterThan(0f));
        }

        [Test]
        public void DefaultSuccessColorIsVisible()
        {
            Color color = QusapParryCueVisualSettings.CreateDefault().SuccessColor;
            Assert.That(color.a, Is.GreaterThan(0f));
            Assert.That(color.g, Is.GreaterThan(color.r));
        }

        private static QusapParryCueVisualSettings Create(
            Vector3? localOffset = null,
            float minimumSize = QusapParryCueVisualSettings.DefaultMinimumSize,
            float maximumSize = QusapParryCueVisualSettings.DefaultMaximumSize,
            float pulseFrequency = QusapParryCueVisualSettings.DefaultPulseFrequency,
            float successFlashDuration = QusapParryCueVisualSettings.DefaultSuccessFlashDuration)
        {
            return new QusapParryCueVisualSettings(
                true,
                localOffset ?? QusapParryCueVisualSettings.DefaultLocalOffset,
                minimumSize,
                maximumSize,
                pulseFrequency,
                QusapParryCueVisualSettings.DefaultWindowColor,
                QusapParryCueVisualSettings.DefaultSuccessColor,
                successFlashDuration,
                QusapParryCueVisualSettings.DefaultSortingOrder);
        }
    }
}
