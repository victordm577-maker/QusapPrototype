using System;
using NUnit.Framework;
using UnityEngine;

namespace Qusap.Tests
{
    public sealed class QusapComboFinisherDefinitionEditModeTests
    {
        [Test]
        public void DefaultDamageFinisherHasExpectedValues()
        {
            QusapComboFinisherDefinition definition =
                QusapComboFinisherDefinition.CreateDefaultDamage();

            Assert.That(definition.ComboId, Is.EqualTo(QusapComboId.Damage));
            Assert.That(definition.Damage, Is.EqualTo(15f));
            Assert.That(definition.HorizontalKnockback, Is.EqualTo(3f));
            Assert.That(definition.VerticalKnockback, Is.EqualTo(1f));
            Assert.That(definition.HitstunDuration, Is.EqualTo(0.35f));
            Assert.That(definition.HitboxSize, Is.EqualTo(new Vector2(1.40f, 1f)));
            Assert.That(definition.HitboxOffset, Is.EqualTo(new Vector2(0.90f, 0f)));
            Assert.That(definition.HitboxDepth, Is.EqualTo(1f));
            Assert.That(definition.RequestsDisarm, Is.False);
        }

        [Test]
        public void DefaultDisarmFinisherHasExpectedValues()
        {
            QusapComboFinisherDefinition definition =
                QusapComboFinisherDefinition.CreateDefaultDisarm();

            Assert.That(definition.ComboId, Is.EqualTo(QusapComboId.Disarm));
            Assert.That(definition.Damage, Is.EqualTo(2f));
            Assert.That(definition.HorizontalKnockback, Is.EqualTo(2f));
            Assert.That(definition.VerticalKnockback, Is.EqualTo(0.5f));
            Assert.That(definition.HitstunDuration, Is.EqualTo(0.30f));
            Assert.That(definition.HitboxSize, Is.EqualTo(new Vector2(1.25f, 0.90f)));
            Assert.That(definition.HitboxOffset, Is.EqualTo(new Vector2(0.80f, 0.20f)));
            Assert.That(definition.HitboxDepth, Is.EqualTo(1f));
            Assert.That(definition.RequestsDisarm, Is.True);
        }

        [Test]
        public void DefaultLaunchFinisherHasExpectedValues()
        {
            QusapComboFinisherDefinition definition =
                QusapComboFinisherDefinition.CreateDefaultLaunch();

            Assert.That(definition.ComboId, Is.EqualTo(QusapComboId.Launch));
            Assert.That(definition.Damage, Is.EqualTo(3f));
            Assert.That(definition.HorizontalKnockback, Is.EqualTo(4f));
            Assert.That(definition.VerticalKnockback, Is.EqualTo(10f));
            Assert.That(definition.HitstunDuration, Is.EqualTo(0.55f));
            Assert.That(definition.HitboxSize, Is.EqualTo(new Vector2(1.30f, 0.90f)));
            Assert.That(definition.HitboxOffset, Is.EqualTo(new Vector2(0.80f, 0f)));
            Assert.That(definition.HitboxDepth, Is.EqualTo(1f));
            Assert.That(definition.RequestsDisarm, Is.False);
        }

        [Test]
        public void EachFactoryReturnsIndependentInstance()
        {
            Assert.That(
                QusapComboFinisherDefinition.CreateDefaultDamage(),
                Is.Not.SameAs(QusapComboFinisherDefinition.CreateDefaultDamage()));
            Assert.That(
                QusapComboFinisherDefinition.CreateDefaultDisarm(),
                Is.Not.SameAs(QusapComboFinisherDefinition.CreateDefaultDisarm()));
            Assert.That(
                QusapComboFinisherDefinition.CreateDefaultLaunch(),
                Is.Not.SameAs(QusapComboFinisherDefinition.CreateDefaultLaunch()));
        }

        [Test]
        public void NegativeDamageIsRejectedOrNormalizedSafely()
        {
            Assert.Throws<ArgumentOutOfRangeException>(() => CreateDamage(damage: -0.01f));
        }

        [Test]
        public void NegativeKnockbackIsRejectedOrNormalizedSafely()
        {
            Assert.Throws<ArgumentOutOfRangeException>(() =>
                CreateDamage(horizontalKnockback: -0.01f));
            Assert.Throws<ArgumentOutOfRangeException>(() =>
                CreateDamage(verticalKnockback: -0.01f));
        }

        [Test]
        public void NegativeHitstunIsRejectedOrNormalizedSafely()
        {
            Assert.Throws<ArgumentOutOfRangeException>(() =>
                CreateDamage(hitstunDuration: -0.01f));
        }

        [Test]
        public void InvalidHitboxSizeIsRejectedOrNormalizedSafely()
        {
            Assert.Throws<ArgumentOutOfRangeException>(() =>
                CreateDamage(hitboxSize: Vector2.zero));
            Assert.Throws<ArgumentOutOfRangeException>(() =>
                CreateDamage(hitboxDepth: 0f));
        }

        [Test]
        public void DuplicateComboFinisherDefinitionsAreRejected()
        {
            QusapComboFinisherDefinition first =
                QusapComboFinisherDefinition.CreateDefaultDamage();
            QusapComboFinisherDefinition duplicate =
                QusapComboFinisherDefinition.CreateDefaultDamage();

            Assert.Throws<ArgumentException>(() =>
                QusapComboFinisherDefinition.ValidateDefinitions(new[] { first, duplicate }));
        }

        [Test]
        public void DisarmDefinitionMustRequestDisarm()
        {
            Assert.Throws<ArgumentException>(() => CreateDefinition(QusapComboId.Disarm, false));
        }

        [Test]
        public void DamageDefinitionCannotRequestDisarm()
        {
            Assert.Throws<ArgumentException>(() => CreateDefinition(QusapComboId.Damage, true));
        }

        [Test]
        public void LaunchDefinitionCannotRequestDisarm()
        {
            Assert.Throws<ArgumentException>(() => CreateDefinition(QusapComboId.Launch, true));
        }

        private static QusapComboFinisherDefinition CreateDamage(
            float damage = 15f,
            float horizontalKnockback = 3f,
            float verticalKnockback = 1f,
            float hitstunDuration = 0.35f,
            Vector2? hitboxSize = null,
            float hitboxDepth = 1f)
        {
            return new QusapComboFinisherDefinition(
                QusapComboId.Damage,
                "Damage",
                damage,
                horizontalKnockback,
                verticalKnockback,
                hitstunDuration,
                hitboxSize ?? new Vector2(1.4f, 1f),
                new Vector2(0.9f, 0f),
                hitboxDepth,
                false);
        }

        private static QusapComboFinisherDefinition CreateDefinition(
            QusapComboId comboId,
            bool requestsDisarm)
        {
            return new QusapComboFinisherDefinition(
                comboId,
                comboId.ToString(),
                1f,
                1f,
                1f,
                0.1f,
                Vector2.one,
                Vector2.zero,
                1f,
                requestsDisarm);
        }
    }
}
