using System;
using System.Reflection;
using NUnit.Framework;
using UnityEngine;

namespace Qusap.Tests
{
    public sealed class QusapComboSetupAttackEditModeTests
    {
        [Test]
        public void LegacyGroundAttackDamageRemainsZero()
        {
            Assert.That(QusapAttackData.CreateWeakKick().Damage, Is.Zero);
            Assert.That(QusapAttackData.CreateStrongKick().Damage, Is.Zero);
            Assert.That(QusapAttackData.CreateHeadbutt().Damage, Is.Zero);
        }

        [Test]
        public void LegacyStrongKickKnockbackRemainsUnchanged()
        {
            QusapAttackData attack = QusapAttackData.CreateStrongKick();
            Assert.That(attack.HorizontalKnockback, Is.EqualTo(7f));
            Assert.That(attack.VerticalKnockback, Is.EqualTo(3f));
        }

        [Test]
        public void LegacyHeadbuttKnockbackRemainsUnchanged()
        {
            QusapAttackData attack = QusapAttackData.CreateHeadbutt();
            Assert.That(attack.HorizontalKnockback, Is.EqualTo(9f));
            Assert.That(attack.VerticalKnockback, Is.EqualTo(4f));
        }

        [Test]
        public void ComboBodyGroundUsesLowKnockback()
        {
            QusapAttackData attack = QusapAttackData.CreateComboBodyAttackGround();
            Assert.That(attack.StartupTime, Is.EqualTo(0.08f));
            Assert.That(attack.ActiveDuration, Is.EqualTo(0.08f));
            Assert.That(attack.RecoveryTime, Is.EqualTo(0.14f));
            Assert.That(attack.HitboxSize, Is.EqualTo(new Vector2(1f, 0.6f)));
            Assert.That(attack.HitboxOffset, Is.EqualTo(new Vector2(0.75f, -0.35f)));
            Assert.That(attack.HitboxDepth, Is.EqualTo(1f));
            Assert.That(attack.HorizontalKnockback, Is.EqualTo(1.75f));
            Assert.That(attack.VerticalKnockback, Is.EqualTo(0.35f));
            Assert.That(attack.HitstunDuration, Is.EqualTo(0.18f));
            Assert.That(attack.LockHorizontalMovement, Is.False);
        }

        [Test]
        public void ComboWeaponLightGroundUsesLowKnockback()
        {
            QusapAttackData attack = QusapAttackData.CreateComboWeaponLightGround();
            Assert.That(attack.StartupTime, Is.EqualTo(0.10f));
            Assert.That(attack.ActiveDuration, Is.EqualTo(0.08f));
            Assert.That(attack.RecoveryTime, Is.EqualTo(0.16f));
            Assert.That(attack.HitboxSize, Is.EqualTo(new Vector2(1.15f, 0.65f)));
            Assert.That(attack.HitboxOffset, Is.EqualTo(new Vector2(0.80f, -0.25f)));
            Assert.That(attack.HitboxDepth, Is.EqualTo(1f));
            Assert.That(attack.HorizontalKnockback, Is.EqualTo(1.25f));
            Assert.That(attack.VerticalKnockback, Is.EqualTo(0.25f));
            Assert.That(attack.HitstunDuration, Is.EqualTo(0.20f));
            Assert.That(attack.LockHorizontalMovement, Is.False);
        }

        [Test]
        public void ComboWeaponLightGroundKnockbackIsLowerThanLegacyStrongKick()
        {
            QusapAttackData setup = QusapAttackData.CreateComboWeaponLightGround();
            QusapAttackData legacy = QusapAttackData.CreateStrongKick();
            Assert.That(setup.HorizontalKnockback, Is.LessThan(legacy.HorizontalKnockback));
            Assert.That(setup.VerticalKnockback, Is.LessThan(legacy.VerticalKnockback));
        }

        [Test]
        public void ComboGroundSetupDamageIsSmallAndPositive()
        {
            Assert.That(QusapAttackData.CreateComboBodyAttackGround().Damage, Is.EqualTo(1f));
            Assert.That(QusapAttackData.CreateComboWeaponLightGround().Damage, Is.EqualTo(1f));
        }

        [Test]
        public void ComboBodyAirUsesLowKnockback()
        {
            QusapAirAttackData attack = QusapAirAttackData.CreateComboBodyAttackAir();
            Assert.That(attack.StartupTime, Is.EqualTo(0.07f));
            Assert.That(attack.ActiveDuration, Is.EqualTo(0.07f));
            Assert.That(attack.RecoveryTime, Is.EqualTo(0.12f));
            Assert.That(attack.LandingRecoveryTime, Is.EqualTo(0.08f));
            Assert.That(attack.HitboxSize, Is.EqualTo(new Vector2(1f, 0.55f)));
            Assert.That(attack.HitboxOffset, Is.EqualTo(new Vector2(0.75f, 0f)));
            Assert.That(attack.HitboxDepth, Is.EqualTo(1f));
            Assert.That(attack.HorizontalKnockback, Is.EqualTo(1.5f));
            Assert.That(attack.VerticalKnockback, Is.EqualTo(0.5f));
            Assert.That(attack.HitstunDuration, Is.EqualTo(0.16f));
            Assert.That(attack.HorizontalVelocityRetention, Is.EqualTo(1f));
            Assert.That(attack.EndActiveWindowOnLanding, Is.True);
        }

        [Test]
        public void ComboWeaponLightAirUsesLowKnockback()
        {
            QusapAirAttackData attack = QusapAirAttackData.CreateComboWeaponLightAir();
            Assert.That(attack.StartupTime, Is.EqualTo(0.10f));
            Assert.That(attack.ActiveDuration, Is.EqualTo(0.08f));
            Assert.That(attack.RecoveryTime, Is.EqualTo(0.16f));
            Assert.That(attack.LandingRecoveryTime, Is.EqualTo(0.12f));
            Assert.That(attack.HitboxSize, Is.EqualTo(new Vector2(1.15f, 0.65f)));
            Assert.That(attack.HitboxOffset, Is.EqualTo(new Vector2(0.80f, -0.30f)));
            Assert.That(attack.HitboxDepth, Is.EqualTo(1f));
            Assert.That(attack.HorizontalKnockback, Is.EqualTo(1.25f));
            Assert.That(attack.VerticalKnockback, Is.EqualTo(0.25f));
            Assert.That(attack.HitstunDuration, Is.EqualTo(0.20f));
            Assert.That(attack.HorizontalVelocityRetention, Is.EqualTo(0.98f));
            Assert.That(attack.EndActiveWindowOnLanding, Is.True);
        }

        [Test]
        public void ComboAirSetupDamageIsSmallAndPositive()
        {
            Assert.That(QusapAirAttackData.CreateComboBodyAttackAir().Damage, Is.EqualTo(1f));
            Assert.That(QusapAirAttackData.CreateComboWeaponLightAir().Damage, Is.EqualTo(1f));
        }

        [Test]
        public void SetupProfilesHaveValidFiniteTimings()
        {
            foreach (IQusapAttackDefinition attack in SetupProfiles())
            {
                AssertFiniteNonNegative(attack.StartupTime);
                AssertFinitePositive(attack.ActiveDuration);
                AssertFiniteNonNegative(attack.RecoveryTime);
                AssertFiniteNonNegative(attack.HitstunDuration);
            }

            AssertFiniteNonNegative(QusapAirAttackData.CreateComboBodyAttackAir().LandingRecoveryTime);
            AssertFiniteNonNegative(QusapAirAttackData.CreateComboWeaponLightAir().LandingRecoveryTime);
        }

        [Test]
        public void SetupProfilesHaveFiniteHitboxValues()
        {
            foreach (IQusapAttackDefinition attack in SetupProfiles())
            {
                AssertFinitePositive(attack.HitboxSize.x);
                AssertFinitePositive(attack.HitboxSize.y);
                AssertFinite(attack.HitboxOffset.x);
                AssertFinite(attack.HitboxOffset.y);
                AssertFinitePositive(attack.HitboxDepth);
            }
        }

        [Test]
        public void SetupProfilesUseExpectedAttackTypes()
        {
            Assert.That(
                QusapAttackData.CreateComboBodyAttackGround().AttackType,
                Is.EqualTo(QusapAttackType.WeakKick));
            Assert.That(
                QusapAttackData.CreateComboWeaponLightGround().AttackType,
                Is.EqualTo(QusapAttackType.StrongKick));
            Assert.That(
                QusapAirAttackData.CreateComboBodyAttackAir().AttackType,
                Is.EqualTo(QusapAttackType.WeakKick));
            Assert.That(
                QusapAirAttackData.CreateComboWeaponLightAir().AttackType,
                Is.EqualTo(QusapAttackType.StrongKick));
        }

        [Test]
        public void SetupFactoriesReturnIndependentInstances()
        {
            Assert.That(
                QusapAttackData.CreateComboBodyAttackGround(),
                Is.Not.SameAs(QusapAttackData.CreateComboBodyAttackGround()));
            Assert.That(
                QusapAttackData.CreateComboWeaponLightGround(),
                Is.Not.SameAs(QusapAttackData.CreateComboWeaponLightGround()));
            Assert.That(
                QusapAirAttackData.CreateComboBodyAttackAir(),
                Is.Not.SameAs(QusapAirAttackData.CreateComboBodyAttackAir()));
            Assert.That(
                QusapAirAttackData.CreateComboWeaponLightAir(),
                Is.Not.SameAs(QusapAirAttackData.CreateComboWeaponLightAir()));
            Assert.That(
                QusapAttackData.CreateComboBodyAttackGround(),
                Is.Not.SameAs(QusapAttackData.CreateWeakKick()));
        }

        [Test]
        public void ValidationDoesNotModifyValidConfiguredValues()
        {
            QusapAttackData attack = QusapAttackData.CreateComboWeaponLightGround();
            float startup = attack.StartupTime;
            float damage = attack.Damage;
            Vector2 hitboxSize = attack.HitboxSize;
            float horizontalKnockback = attack.HorizontalKnockback;

            MethodInfo validate = typeof(QusapAttackData).GetMethod(
                "Validate", BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.That(validate, Is.Not.Null);
            validate.Invoke(attack, null);

            Assert.That(attack.StartupTime, Is.EqualTo(startup));
            Assert.That(attack.Damage, Is.EqualTo(damage));
            Assert.That(attack.HitboxSize, Is.EqualTo(hitboxSize));
            Assert.That(attack.HorizontalKnockback, Is.EqualTo(horizontalKnockback));
        }

        [Test]
        public void GroundDamageValidationNormalizesInvalidValues()
        {
            foreach (float invalidDamage in new[]
            {
                -1f,
                float.NaN,
                float.PositiveInfinity,
                float.NegativeInfinity
            })
            {
                QusapAttackData attack = QusapAttackData.CreateComboBodyAttackGround();
                FieldInfo damage = typeof(QusapAttackData).GetField(
                    "damage", BindingFlags.Instance | BindingFlags.NonPublic);
                Assert.That(damage, Is.Not.Null);
                damage.SetValue(attack, invalidDamage);

                MethodInfo validate = typeof(QusapAttackData).GetMethod(
                    "Validate", BindingFlags.Instance | BindingFlags.NonPublic);
                Assert.That(validate, Is.Not.Null);
                validate.Invoke(attack, null);
                Assert.That(attack.Damage, Is.Zero);
            }
        }

        private static IQusapAttackDefinition[] SetupProfiles()
        {
            return new IQusapAttackDefinition[]
            {
                QusapAttackData.CreateComboBodyAttackGround(),
                QusapAttackData.CreateComboWeaponLightGround(),
                QusapAirAttackData.CreateComboBodyAttackAir(),
                QusapAirAttackData.CreateComboWeaponLightAir()
            };
        }

        private static void AssertFinite(float value)
        {
            Assert.That(float.IsNaN(value), Is.False);
            Assert.That(float.IsInfinity(value), Is.False);
        }

        private static void AssertFiniteNonNegative(float value)
        {
            AssertFinite(value);
            Assert.That(value, Is.GreaterThanOrEqualTo(0f));
        }

        private static void AssertFinitePositive(float value)
        {
            AssertFinite(value);
            Assert.That(value, Is.GreaterThan(0f));
        }
    }
}
