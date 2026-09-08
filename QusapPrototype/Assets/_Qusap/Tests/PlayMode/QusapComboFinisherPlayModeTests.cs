using System;
using System.Collections.Generic;
using System.Reflection;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.InputSystem;

namespace Qusap.Tests
{
    public sealed class QusapComboFinisherPlayModeTests
    {
        private readonly List<PlayerHarness> players = new();

        [TearDown]
        public void TearDown()
        {
            for (int i = players.Count - 1; i >= 0; i--)
            {
                players[i].Dispose();
            }

            players.Clear();
        }

        [Test]
        public void DamageFinisherAppliesExpectedDamageOnce()
        {
            PlayerHarness attacker = CreatePlayer("Attacker");
            PlayerHarness target = CreatePlayer("Target");
            ResolutionResult result = Resolve(attacker, target, QusapComboId.Damage);

            Assert.That(result.DamageApplied, Is.EqualTo(15f));
            Assert.That(result.Resolution.Outcome, Is.EqualTo(QusapFinisherResolutionOutcome.Applied));
        }

        [Test]
        public void DamageFinisherAppliesExpectedKnockbackOnce()
        {
            PlayerHarness attacker = CreatePlayer("Attacker");
            PlayerHarness target = CreatePlayer("Target");
            ResolutionResult result = Resolve(attacker, target, QusapComboId.Damage);

            Assert.That(result.Velocity.x, Is.EqualTo(3f).Within(0.001f));
            Assert.That(result.Velocity.y, Is.EqualTo(1f).Within(0.001f));
        }

        [Test]
        public void DamageFinisherAppliesExpectedHitstun()
        {
            PlayerHarness attacker = CreatePlayer("Attacker");
            PlayerHarness target = CreatePlayer("Target");
            Resolve(attacker, target, QusapComboId.Damage);

            Assert.That(target.Hitstun.IsInHitstun, Is.True);
            Assert.That(target.Hitstun.TimeRemaining, Is.EqualTo(0.35f).Within(0.001f));
        }

        [Test]
        public void DamageFinisherDoesNotRequestDisarm()
        {
            PlayerHarness attacker = CreatePlayer("Attacker");
            PlayerHarness target = CreatePlayer("Target");
            QusapTestDisarmable disarmable = target.AddDisarmable(true);
            ResolutionResult result = Resolve(attacker, target, QusapComboId.Damage);

            Assert.That(disarmable.CallCount, Is.Zero);
            Assert.That(result.Resolution.DisarmApplied, Is.False);
        }

        [Test]
        public void LaunchFinisherAppliesExpectedDamageOnce()
        {
            PlayerHarness attacker = CreatePlayer("Attacker");
            PlayerHarness target = CreatePlayer("Target");
            ResolutionResult result = Resolve(attacker, target, QusapComboId.Launch);

            Assert.That(result.DamageApplied, Is.EqualTo(3f));
        }

        [Test]
        public void LaunchFinisherAppliesConfiguredVerticalKnockback()
        {
            PlayerHarness attacker = CreatePlayer("Attacker");
            PlayerHarness target = CreatePlayer("Target");
            ResolutionResult result = Resolve(attacker, target, QusapComboId.Launch);

            Assert.That(result.Velocity.y, Is.EqualTo(10f).Within(0.001f));
        }

        [Test]
        public void LaunchFinisherUsesLessHorizontalForceThanLegacyStrongKick()
        {
            PlayerHarness attacker = CreatePlayer("Attacker");
            PlayerHarness target = CreatePlayer("Target");
            ResolutionResult result = Resolve(attacker, target, QusapComboId.Launch);

            Assert.That(Mathf.Abs(result.Velocity.x), Is.EqualTo(4f).Within(0.001f));
            Assert.That(Mathf.Abs(result.Velocity.x), Is.LessThan(7f));
        }

        [Test]
        public void LaunchFinisherDoesNotModifyTargetTransformDirectly()
        {
            PlayerHarness attacker = CreatePlayer("Attacker");
            PlayerHarness target = CreatePlayer("Target");
            ResolutionResult result = Resolve(attacker, target, QusapComboId.Launch);

            Assert.That(target.Root.transform.position, Is.EqualTo(result.PositionBefore));
        }

        [Test]
        public void LaunchFinisherDoesNotRequestDisarm()
        {
            PlayerHarness attacker = CreatePlayer("Attacker");
            PlayerHarness target = CreatePlayer("Target");
            QusapTestDisarmable disarmable = target.AddDisarmable(true);
            ResolutionResult result = Resolve(attacker, target, QusapComboId.Launch);

            Assert.That(disarmable.CallCount, Is.Zero);
            Assert.That(result.Resolution.DisarmApplied, Is.False);
        }

        [Test]
        public void DisarmFinisherCallsDisarmableExactlyOnce()
        {
            PlayerHarness attacker = CreatePlayer("Attacker");
            PlayerHarness target = CreatePlayer("Target");
            QusapTestDisarmable disarmable = target.AddDisarmable(true);
            Resolve(attacker, target, QusapComboId.Disarm);

            Assert.That(disarmable.CallCount, Is.EqualTo(1));
            Assert.That(disarmable.LastSource, Is.SameAs(attacker.Combat));
        }

        [Test]
        public void DisarmFinisherReportsSuccessWhenDisarmableAccepts()
        {
            PlayerHarness attacker = CreatePlayer("Attacker");
            PlayerHarness target = CreatePlayer("Target");
            target.AddDisarmable(true);
            ResolutionResult result = Resolve(attacker, target, QusapComboId.Disarm);

            Assert.That(result.Resolution.Outcome, Is.EqualTo(QusapFinisherResolutionOutcome.DisarmSucceeded));
            Assert.That(result.Resolution.DisarmApplied, Is.True);
        }

        [Test]
        public void DisarmFinisherReportsUnavailableWithoutDisarmable()
        {
            PlayerHarness attacker = CreatePlayer("Attacker");
            PlayerHarness target = CreatePlayer("Target");
            ResolutionResult result = Resolve(attacker, target, QusapComboId.Disarm);

            Assert.That(result.Resolution.Outcome, Is.EqualTo(QusapFinisherResolutionOutcome.DisarmUnavailable));
            Assert.That(result.Resolution.HitApplied, Is.True);
        }

        [Test]
        public void DisarmFinisherReportsRejectedWhenDisarmableRejects()
        {
            PlayerHarness attacker = CreatePlayer("Attacker");
            PlayerHarness target = CreatePlayer("Target");
            QusapTestDisarmable disarmable = target.AddDisarmable(false);
            ResolutionResult result = Resolve(attacker, target, QusapComboId.Disarm);

            Assert.That(disarmable.CallCount, Is.EqualTo(1));
            Assert.That(result.Resolution.Outcome, Is.EqualTo(QusapFinisherResolutionOutcome.DisarmRejected));
            Assert.That(result.Resolution.DisarmApplied, Is.False);
        }

        [Test]
        public void DisarmFinisherStillAppliesItsConfiguredImpactWhenUnavailable()
        {
            PlayerHarness attacker = CreatePlayer("Attacker");
            PlayerHarness target = CreatePlayer("Target");
            ResolutionResult result = Resolve(attacker, target, QusapComboId.Disarm);

            Assert.That(result.DamageApplied, Is.EqualTo(2f));
            Assert.That(result.Velocity.x, Is.EqualTo(2f).Within(0.001f));
            Assert.That(result.Velocity.y, Is.EqualTo(0.5f).Within(0.001f));
            Assert.That(target.Hitstun.TimeRemaining, Is.EqualTo(0.30f).Within(0.001f));
        }

        [Test]
        public void ReadyFinisherResolvesOnlyOnce()
        {
            PlayerHarness attacker = CreatePlayer("Attacker");
            PlayerHarness target = CreatePlayer("Target");
            attacker.ArmCombo(QusapComboId.Damage, target);
            attacker.PrepareTargetForFinisher(target, 0.9f);
            float damageBefore = target.Receiver.TotalDamageReceived;
            int eventCount = 0;
            attacker.Combat.FinisherResolved += _ => eventCount++;

            attacker.AdvanceFinisherToReady();
            attacker.ProcessFixed();
            attacker.ProcessFixed();

            Assert.That(target.Receiver.TotalDamageReceived - damageBefore, Is.EqualTo(15f));
            Assert.That(eventCount, Is.EqualTo(1));
        }

        [Test]
        public void RepeatedFixedUpdateDoesNotRepeatFinisher()
        {
            PlayerHarness attacker = CreatePlayer("Attacker");
            PlayerHarness target = CreatePlayer("Target");
            ResolutionResult result = Resolve(attacker, target, QusapComboId.Launch);

            for (int i = 0; i < 5; i++)
            {
                attacker.ProcessFixed();
            }

            Assert.That(target.Receiver.TotalDamageReceived - result.DamageBefore, Is.EqualTo(3f));
            Assert.That(target.Body.linearVelocity, Is.EqualTo(result.Velocity));
        }

        [Test]
        public void ReadyFinisherAutoResolvesOnFollowingFixedUpdate()
        {
            PlayerHarness attacker = CreatePlayer("Attacker");
            PlayerHarness target = CreatePlayer("Target");
            attacker.ArmCombo(QusapComboId.Launch, target);
            attacker.PrepareTargetForFinisher(target, 0.9f);

            attacker.AdvanceFinisherToReady();
            Assert.That(attacker.Combat.HasFinisherReadyToResolve, Is.True);
            Assert.That(attacker.Combat.LastFinisherResolution, Is.Null);
            attacker.ProcessFixed();

            Assert.That(attacker.Combat.LastFinisherResolution.HasValue, Is.True);
            Assert.That(
                attacker.Combat.LastFinisherResolution.Value.Outcome,
                Is.EqualTo(QusapFinisherResolutionOutcome.Applied));
        }

        [Test]
        public void DeferredResolutionDoesNotExtendParryWindow()
        {
            PlayerPair pair = CreateArmedPair(QusapComboId.Launch);
            float damageBefore = pair.Defender.Receiver.TotalDamageReceived;
            pair.Attacker.AdvanceFinisherToReady();

            pair.Defender.Parry(pair.Attacker.Combat.ParryWindowClosesAt + 0.001d);
            pair.Defender.ProcessFixed();
            pair.Attacker.ProcessFixed();

            Assert.That(pair.Attacker.Combat.LastFinisherResolution.HasValue, Is.True);
            Assert.That(
                pair.Attacker.Combat.LastFinisherResolution.Value.Outcome,
                Is.EqualTo(QusapFinisherResolutionOutcome.Applied));
            Assert.That(
                pair.Defender.Receiver.TotalDamageReceived - damageBefore,
                Is.EqualTo(3f));
        }

        [Test]
        public void SuccessfulParryPreventsDeferredFinisherEffect()
        {
            PlayerPair pair = CreateArmedPair(QusapComboId.Damage);
            float damageBefore = pair.Defender.Receiver.TotalDamageReceived;

            pair.SuccessfulParry();
            pair.Attacker.AdvanceFinisher(pair.Attacker.Combat.ParryWindowClosesAt + 1d);
            pair.Attacker.ProcessFixed();
            pair.Attacker.ProcessFixed();

            Assert.That(pair.Defender.Receiver.TotalDamageReceived, Is.EqualTo(damageBefore));
            Assert.That(pair.Attacker.Combat.LastFinisherResolution, Is.Null);
        }

        [Test]
        public void DeferredFinisherStillResolvesExactlyOnceWithoutParry()
        {
            PlayerHarness attacker = CreatePlayer("Attacker");
            PlayerHarness target = CreatePlayer("Target");
            attacker.ArmCombo(QusapComboId.Damage, target);
            attacker.PrepareTargetForFinisher(target, 0.9f);
            float damageBefore = target.Receiver.TotalDamageReceived;
            int resolvedCount = 0;
            attacker.Combat.FinisherResolved += _ => resolvedCount++;

            attacker.AdvanceFinisherToReady();
            attacker.ProcessFixed();
            attacker.ProcessFixed();
            attacker.ProcessFixed();

            Assert.That(target.Receiver.TotalDamageReceived - damageBefore, Is.EqualTo(15f));
            Assert.That(resolvedCount, Is.EqualTo(1));
        }

        [Test]
        public void MultipleTargetCollidersDoNotDuplicateFinisher()
        {
            PlayerHarness attacker = CreatePlayer("Attacker");
            PlayerHarness target = CreatePlayer("Target");
            target.AddAdditionalHurtboxCollider();
            ResolutionResult result = Resolve(attacker, target, QusapComboId.Damage);

            Assert.That(result.DamageApplied, Is.EqualTo(15f));
        }

        [Test]
        public void ExpectedTargetInsideRangeReceivesFinisher()
        {
            PlayerHarness attacker = CreatePlayer("Attacker");
            PlayerHarness target = CreatePlayer("Target");
            ResolutionResult result = Resolve(attacker, target, QusapComboId.Damage);

            Assert.That(result.Resolution.ExpectedTarget, Is.SameAs(target.Receiver));
            Assert.That(result.Resolution.HitApplied, Is.True);
        }

        [Test]
        public void ExpectedTargetOutsideRangeCausesWhiff()
        {
            PlayerHarness attacker = CreatePlayer("Attacker");
            PlayerHarness target = CreatePlayer("Target");
            attacker.ArmCombo(QusapComboId.Damage, target);
            attacker.PrepareTargetForFinisher(target, 10f);
            float damageBefore = target.Receiver.TotalDamageReceived;

            QusapFinisherResolution resolution = attacker.ResolveReadyFinisher();

            Assert.That(resolution.Outcome, Is.EqualTo(QusapFinisherResolutionOutcome.Whiffed));
            Assert.That(target.Receiver.TotalDamageReceived, Is.EqualTo(damageBefore));
            Assert.That(target.Body.linearVelocity, Is.EqualTo(Vector3.zero));
        }

        [Test]
        public void DifferentTargetInsideRangeIsIgnored()
        {
            PlayerHarness attacker = CreatePlayer("Attacker");
            PlayerHarness expected = CreatePlayer("Expected");
            PlayerHarness other = CreatePlayer("Other");
            attacker.ArmCombo(QusapComboId.Damage, expected);
            attacker.PrepareTargetForFinisher(expected, 10f);
            attacker.PrepareTargetForFinisher(other, 0.9f);
            float otherDamage = other.Receiver.TotalDamageReceived;

            QusapFinisherResolution resolution = attacker.ResolveReadyFinisher();

            Assert.That(resolution.Outcome, Is.EqualTo(QusapFinisherResolutionOutcome.Whiffed));
            Assert.That(other.Receiver.TotalDamageReceived, Is.EqualTo(otherDamage));
        }

        [Test]
        public void DifferentTargetCannotReplaceConfirmedTarget()
        {
            PlayerHarness attacker = CreatePlayer("Attacker");
            PlayerHarness expected = CreatePlayer("Expected");
            PlayerHarness other = CreatePlayer("Other");
            attacker.ArmCombo(QusapComboId.Launch, expected);
            attacker.PrepareTargetForFinisher(expected, 10f);
            attacker.PrepareTargetForFinisher(other, 0.8f);

            QusapFinisherResolution resolution = attacker.ResolveReadyFinisher();

            Assert.That(resolution.ExpectedTarget, Is.SameAs(expected.Receiver));
            Assert.That(other.Body.linearVelocity, Is.EqualTo(Vector3.zero));
        }

        [Test]
        public void TargetCrossingBehindAttackerIsNotAutoTracked()
        {
            PlayerHarness attacker = CreatePlayer("Attacker");
            PlayerHarness target = CreatePlayer("Target");
            attacker.ArmCombo(QusapComboId.Launch, target);
            attacker.PrepareTargetForFinisher(target, -0.9f);

            QusapFinisherResolution resolution = attacker.ResolveReadyFinisher();

            Assert.That(resolution.Outcome, Is.EqualTo(QusapFinisherResolutionOutcome.Whiffed));
            Assert.That(target.Body.linearVelocity, Is.EqualTo(Vector3.zero));
        }

        [Test]
        public void SuccessfulParryPreventsFinisherDamage()
        {
            PlayerPair pair = CreateArmedPair(QusapComboId.Damage);
            float damageBefore = pair.Defender.Receiver.TotalDamageReceived;

            pair.SuccessfulParry();
            pair.Attacker.ProcessFixed();

            Assert.That(pair.Defender.Receiver.TotalDamageReceived, Is.EqualTo(damageBefore));
            Assert.That(pair.Attacker.Combat.LastFinisherResolution, Is.Null);
        }

        [Test]
        public void SuccessfulParryPreventsLaunch()
        {
            PlayerPair pair = CreateArmedPair(QusapComboId.Launch);
            pair.Defender.Body.linearVelocity = Vector3.zero;

            pair.SuccessfulParry();
            pair.Attacker.ProcessFixed();

            Assert.That(pair.Defender.Body.linearVelocity, Is.EqualTo(Vector3.zero));
            Assert.That(pair.Attacker.Combat.LastFinisherResolution, Is.Null);
        }

        [Test]
        public void SuccessfulParryPreventsDisarm()
        {
            PlayerPair pair = CreateArmedPair(QusapComboId.Disarm);
            QusapTestDisarmable disarmable = pair.Defender.AddDisarmable(true);

            pair.SuccessfulParry();
            pair.Attacker.ProcessFixed();

            Assert.That(disarmable.CallCount, Is.Zero);
            Assert.That(pair.Attacker.Combat.LastFinisherResolution, Is.Null);
        }

        [Test]
        public void CancelledFinisherAppliesNoEffect()
        {
            PlayerHarness attacker = CreatePlayer("Attacker");
            PlayerHarness target = CreatePlayer("Target");
            attacker.ArmCombo(QusapComboId.Damage, target);
            float damageBefore = target.Receiver.TotalDamageReceived;

            attacker.Combat.ResetComboRecognition();
            attacker.ProcessFixed();

            Assert.That(target.Receiver.TotalDamageReceived, Is.EqualTo(damageBefore));
            Assert.That(attacker.Combat.LastFinisherResolution, Is.Null);
        }

        [Test]
        public void InvalidTargetBeforeResolutionAppliesNoEffect()
        {
            PlayerHarness attacker = CreatePlayer("Attacker");
            PlayerHarness target = CreatePlayer("Target");
            attacker.ArmCombo(QusapComboId.Damage, target);
            float damageBefore = target.Receiver.TotalDamageReceived;
            target.Receiver.AcceptsHits = false;

            attacker.AdvanceFinisherToReady();
            attacker.ProcessFixed();

            Assert.That(target.Receiver.TotalDamageReceived, Is.EqualTo(damageBefore));
            Assert.That(attacker.Combat.HasArmedFinisher, Is.False);
            Assert.That(attacker.Combat.LastFinisherResolution.HasValue, Is.True);
            Assert.That(
                attacker.Combat.LastFinisherResolution.Value.Outcome,
                Is.EqualTo(QusapFinisherResolutionOutcome.Rejected));
        }

        [Test]
        public void AttackerHitBeforeResolutionAppliesNoEffect()
        {
            PlayerHarness attacker = CreatePlayer("Attacker");
            PlayerHarness target = CreatePlayer("Target");
            PlayerHarness third = CreatePlayer("Third");
            attacker.ArmCombo(QusapComboId.Damage, target);
            float damageBefore = target.Receiver.TotalDamageReceived;

            attacker.ReceiveInterruptingHitFrom(third);
            attacker.ProcessFixed();

            Assert.That(target.Receiver.TotalDamageReceived, Is.EqualTo(damageBefore));
            Assert.That(attacker.Combat.HasArmedFinisher, Is.False);
        }

        [Test]
        public void TargetHitByThirdPlayerBeforeResolutionCancelsCorrectly()
        {
            PlayerHarness attacker = CreatePlayer("Attacker");
            PlayerHarness target = CreatePlayer("Target");
            PlayerHarness third = CreatePlayer("Third");
            attacker.ArmCombo(QusapComboId.Damage, target);
            float damageBefore = target.Receiver.TotalDamageReceived;

            target.ReceiveInterruptingHitFrom(third);
            attacker.ProcessFixed();

            Assert.That(target.Receiver.TotalDamageReceived, Is.EqualTo(damageBefore));
            Assert.That(attacker.Combat.HasArmedFinisher, Is.False);
            Assert.That(target.Combat.IncomingFinisherCount, Is.Zero);
        }

        [Test]
        public void FinisherResolvedEventFiresOnce()
        {
            PlayerHarness attacker = CreatePlayer("Attacker");
            PlayerHarness target = CreatePlayer("Target");
            int eventCount = 0;
            QusapFinisherResolution received = default;
            attacker.Combat.FinisherResolved += resolution =>
            {
                eventCount++;
                received = resolution;
            };

            ResolutionResult result = Resolve(attacker, target, QusapComboId.Launch);
            attacker.ProcessFixed();

            Assert.That(eventCount, Is.EqualTo(1));
            Assert.That(received.ComboId, Is.EqualTo(result.Resolution.ComboId));
        }

        [Test]
        public void LastFinisherResolutionMatchesAppliedCombo()
        {
            PlayerHarness attacker = CreatePlayer("Attacker");
            PlayerHarness target = CreatePlayer("Target");
            Resolve(attacker, target, QusapComboId.Damage);

            Assert.That(attacker.Combat.LastFinisherResolution.HasValue, Is.True);
            Assert.That(attacker.Combat.LastFinisherResolution.Value.ComboId, Is.EqualTo(QusapComboId.Damage));
            Assert.That(attacker.Combat.LastFinisherResolution.Value.ExpectedTarget, Is.SameAs(target.Receiver));
        }

        [Test]
        public void WhiffStillClearsArmedFinisher()
        {
            PlayerHarness attacker = CreatePlayer("Attacker");
            PlayerHarness target = CreatePlayer("Target");
            attacker.ArmCombo(QusapComboId.Launch, target);
            attacker.PrepareTargetForFinisher(target, 10f);

            QusapFinisherResolution resolution = attacker.ResolveReadyFinisher();

            Assert.That(resolution.Outcome, Is.EqualTo(QusapFinisherResolutionOutcome.Whiffed));
            Assert.That(attacker.Combat.HasArmedFinisher, Is.False);
            Assert.That(attacker.Combat.ArmedFinisherTarget, Is.Null);
        }

        [Test]
        public void ResolutionClearsDefenderRegistration()
        {
            PlayerHarness attacker = CreatePlayer("Attacker");
            PlayerHarness target = CreatePlayer("Target");
            attacker.ArmCombo(QusapComboId.Damage, target);
            Assert.That(target.Combat.IncomingFinisherCount, Is.EqualTo(1));
            attacker.PrepareTargetForFinisher(target, 0.9f);

            attacker.ResolveReadyFinisher();

            Assert.That(target.Combat.IncomingFinisherCount, Is.Zero);
        }

        [Test]
        public void LegacyAttacksKeepPreviousValues()
        {
            PlayerHarness attacker = CreatePlayer("Attacker");
            PlayerHarness target = CreatePlayer("Target");
            attacker.StartDirectAttack(QusapAttackType.StrongKick, true);
            HitResult result = attacker.HitCurrentAttack(target);

            Assert.That(result.DamageApplied, Is.Zero);
            Assert.That(result.Velocity.x, Is.EqualTo(7f).Within(0.001f));
            Assert.That(result.Velocity.y, Is.EqualTo(3f).Within(0.001f));
        }

        [Test]
        public void ComboSetupAttacksKeepStageSixValues()
        {
            PlayerHarness bodyAttacker = CreatePlayer("BodyAttacker");
            PlayerHarness bodyTarget = CreatePlayer("BodyTarget");
            HitResult body = bodyAttacker.PerformSetupHit(
                QusapCombatCommand.BodyAttack, 1d, true, bodyTarget);

            PlayerHarness weaponAttacker = CreatePlayer("WeaponAttacker");
            PlayerHarness weaponTarget = CreatePlayer("WeaponTarget");
            HitResult weapon = weaponAttacker.PerformSetupHit(
                QusapCombatCommand.WeaponLight, 1d, true, weaponTarget);

            Assert.That(body.DamageApplied, Is.EqualTo(1f));
            Assert.That(body.Velocity.x, Is.EqualTo(1.75f).Within(0.001f));
            Assert.That(body.Velocity.y, Is.EqualTo(0.35f).Within(0.001f));
            Assert.That(weapon.DamageApplied, Is.EqualTo(1f));
            Assert.That(weapon.Velocity.x, Is.EqualTo(1.25f).Within(0.001f));
            Assert.That(weapon.Velocity.y, Is.EqualTo(0.25f).Within(0.001f));
        }

        [Test]
        public void ExistingComboAndParrySuitesStillPass()
        {
            PlayerPair pair = CreateArmedPair(QusapComboId.Launch);
            pair.SuccessfulParry();

            Assert.That(pair.Attacker.Combat.HasArmedFinisher, Is.False);
            Assert.That(pair.Attacker.Combat.FinisherDefensePhase, Is.EqualTo(QusapFinisherDefensePhase.Parried));
            Assert.That(pair.Attacker.Combat.LastFinisherResolution, Is.Null);
        }

        private ResolutionResult Resolve(
            PlayerHarness attacker,
            PlayerHarness target,
            QusapComboId comboId)
        {
            attacker.ArmCombo(comboId, target);
            attacker.PrepareTargetForFinisher(target, 0.9f);
            float damageBefore = target.Receiver.TotalDamageReceived;
            Vector3 positionBefore = target.Root.transform.position;

            QusapFinisherResolution resolution = attacker.ResolveReadyFinisher();
            return new ResolutionResult(
                resolution,
                damageBefore,
                target.Receiver.TotalDamageReceived - damageBefore,
                target.Body.linearVelocity,
                positionBefore);
        }

        private PlayerPair CreateArmedPair(QusapComboId comboId)
        {
            PlayerHarness attacker = CreatePlayer("Attacker");
            PlayerHarness defender = CreatePlayer("Defender");
            attacker.ArmCombo(comboId, defender);
            attacker.PrepareTargetForFinisher(defender, 0.9f);
            return new PlayerPair(attacker, defender);
        }

        private PlayerHarness CreatePlayer(string name)
        {
            PlayerHarness player = new(name);
            players.Add(player);
            return player;
        }

        private readonly struct ResolutionResult
        {
            public ResolutionResult(
                QusapFinisherResolution resolution,
                float damageBefore,
                float damageApplied,
                Vector3 velocity,
                Vector3 positionBefore)
            {
                Resolution = resolution;
                DamageBefore = damageBefore;
                DamageApplied = damageApplied;
                Velocity = velocity;
                PositionBefore = positionBefore;
            }

            public QusapFinisherResolution Resolution { get; }
            public float DamageBefore { get; }
            public float DamageApplied { get; }
            public Vector3 Velocity { get; }
            public Vector3 PositionBefore { get; }
        }

        private readonly struct HitResult
        {
            public HitResult(Vector3 velocity, float damageApplied)
            {
                Velocity = velocity;
                DamageApplied = damageApplied;
            }

            public Vector3 Velocity { get; }
            public float DamageApplied { get; }
        }

        private readonly struct PlayerPair
        {
            public PlayerPair(PlayerHarness attacker, PlayerHarness defender)
            {
                Attacker = attacker;
                Defender = defender;
            }

            public PlayerHarness Attacker { get; }
            public PlayerHarness Defender { get; }

            public void SuccessfulParry()
            {
                Attacker.AdvanceFinisher(Attacker.Combat.ParryWindowOpensAt);
                double midpoint = Attacker.Combat.ParryWindowOpensAt
                    + (Attacker.Combat.ParryWindowClosesAt - Attacker.Combat.ParryWindowOpensAt) / 2d;
                Defender.Parry(midpoint);
                Defender.ProcessFixed();
            }
        }

        private sealed class PlayerHarness : IDisposable
        {
            private static int nextPlayerId;
            private readonly InputActionAsset inputAsset;

            public PlayerHarness(string name)
            {
                int playerId = ++nextPlayerId;
                Root = new GameObject($"{name}_{playerId}");
                Root.SetActive(false);
                Root.transform.position = new Vector3(playerId * 20f, 0f, 0f);
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

                Input = Root.AddComponent<QusapInputReader>();
                SetField(Input, "inputActionAsset", inputAsset);
                Ground = Root.AddComponent<QusapGroundSensor>();
                Root.AddComponent<QusapWallSensor>();
                Root.AddComponent<QusapHorizontalMotor>();
                Root.AddComponent<QusapVerticalMotor>();
                Root.AddComponent<QusapDashMotor>();
                Receiver = Root.AddComponent<QusapHitReceiver>();
                Hitstun = Root.AddComponent<QusapHitstunController>();
                Root.AddComponent<QusapRespawnController>();
                Root.AddComponent<QusapHurtbox>();

                GameObject hitboxObject = new("AttackHitbox");
                hitboxObject.transform.SetParent(Root.transform, false);
                Hitbox = hitboxObject.AddComponent<QusapAttackHitbox>();
                Combat = Root.AddComponent<QusapCombatController>();
                SetField(Combat, "attackHitbox", Hitbox);
                SetField(Combat, "logRecognizedCombos", false);
                Root.SetActive(true);
                SetGrounded(true);
            }

            public GameObject Root { get; }
            public Rigidbody Body { get; }
            public QusapInputReader Input { get; }
            public QusapGroundSensor Ground { get; }
            public QusapHitReceiver Receiver { get; }
            public QusapHitstunController Hitstun { get; }
            public QusapAttackHitbox Hitbox { get; }
            public QusapCombatController Combat { get; }
            private IQusapAttackDefinition ActiveAttack =>
                Combat.GetAttackDefinition(Combat.CurrentAttackVariant);

            public QusapTestDisarmable AddDisarmable(bool accepts)
            {
                QusapTestDisarmable disarmable = Root.AddComponent<QusapTestDisarmable>();
                disarmable.CanBeDisarmedValue = true;
                disarmable.AcceptsDisarm = accepts;
                return disarmable;
            }

            public void AddAdditionalHurtboxCollider()
            {
                GameObject child = new("AdditionalHurtboxCollider");
                child.transform.SetParent(Root.transform, false);
                child.AddComponent<BoxCollider>();
            }

            public void ArmCombo(QusapComboId comboId, PlayerHarness target)
            {
                QusapComboDefinition definition = FindDefinition(comboId);
                double timestamp = 10d;
                for (int i = 0; i < definition.StepCount - 1; i++)
                {
                    if (i > 0)
                    {
                        timestamp += ValidDelay(definition.GetStep(i));
                    }

                    PerformSetupHit(definition.GetStep(i).Command, timestamp, true, target);
                    FinishAttack();
                    target.Hitstun.ResetHitstun();
                }

                timestamp += ValidDelay(definition.GetStep(definition.StepCount - 1));
                Enqueue(definition.GetStep(definition.StepCount - 1).Command, timestamp);
                ProcessFixed();
                Assert.That(Combat.HasArmedFinisher, Is.True);
                Assert.That(Combat.ArmedFinisherTarget, Is.SameAs(target.Receiver));
            }

            public QusapFinisherResolution ResolveReadyFinisher()
            {
                AdvanceFinisherToReady();
                Assert.That(Combat.HasFinisherReadyToResolve, Is.True);
                ProcessFixed();
                Assert.That(Combat.LastFinisherResolution.HasValue, Is.True);
                return Combat.LastFinisherResolution.Value;
            }

            public void AdvanceFinisherToReady()
            {
                AdvanceFinisher(Combat.ParryWindowClosesAt + 0.001d);
            }

            public void AdvanceFinisher(double timestamp)
            {
                Combat.UpdateFinisherDefense(timestamp);
            }

            public void PrepareTargetForFinisher(PlayerHarness target, float horizontalOffset)
            {
                target.Root.transform.position = Root.transform.position
                    + new Vector3(horizontalOffset, 0f, 0f);
                target.Body.linearVelocity = Vector3.zero;
                target.Hitstun.ResetHitstun();
                Physics.SyncTransforms();
            }

            public HitResult PerformSetupHit(
                QusapCombatCommand command,
                double timestamp,
                bool grounded,
                PlayerHarness target)
            {
                PrepareTargetForFinisher(target, 0.9f);
                StartSetup(command, timestamp, grounded);
                return HitCurrentAttack(target);
            }

            public void StartSetup(QusapCombatCommand command, double timestamp, bool grounded)
            {
                SetGrounded(grounded);
                Enqueue(command, timestamp);
                ProcessFixed();
                Assert.That(Combat.IsAttacking, Is.True);
                Assert.That(Combat.IsComboSetupAttack, Is.True);
            }

            public void StartDirectAttack(QusapAttackType attackType, bool grounded)
            {
                SetGrounded(grounded);
                Assert.That(Combat.TryStartAttack(attackType), Is.True);
            }

            public HitResult HitCurrentAttack(PlayerHarness target)
            {
                PrepareTargetForFinisher(target, 0.9f);
                EnterActive();
                float damageBefore = target.Receiver.TotalDamageReceived;
                Physics.SyncTransforms();
                Invoke(Hitbox, "FixedUpdate");
                return new HitResult(
                    target.Body.linearVelocity,
                    target.Receiver.TotalDamageReceived - damageBefore);
            }

            public void EnterActive()
            {
                Assert.That(Combat.CurrentPhase, Is.EqualTo(QusapAttackPhase.Startup));
                Assert.That(ActiveAttack, Is.Not.Null);
                Invoke(Combat, "AdvanceAttack", ActiveAttack.StartupTime);
                Assert.That(Combat.CurrentPhase, Is.EqualTo(QusapAttackPhase.Active));
            }

            public void FinishAttack()
            {
                Invoke(Combat, "AdvanceAttack", 10f);
                Assert.That(Combat.IsAttacking, Is.False);
            }

            public void ReceiveInterruptingHitFrom(PlayerHarness source)
            {
                QusapHitInfo hit = new(
                    source.Combat,
                    QusapAttackType.WeakKick,
                    QusapAttackVariant.WeakKickGround,
                    0f,
                    1,
                    0f,
                    0f,
                    0.2f,
                    Vector3.zero);
                Assert.That(Receiver.TryReceiveHit(hit), Is.True);
            }

            public void Parry(double timestamp)
            {
                Enqueue(QusapCombatCommand.Parry, timestamp);
            }

            public void Enqueue(QusapCombatCommand command, double timestamp)
            {
                Input.EnqueueCombatCommand(command, timestamp);
            }

            public void ProcessFixed()
            {
                Invoke(Combat, "FixedUpdate");
            }

            public void SetGrounded(bool grounded)
            {
                PropertyInfo property = Ground.GetType().GetProperty(
                    "IsGrounded", BindingFlags.Instance | BindingFlags.Public);
                Assert.That(property, Is.Not.Null);
                property.SetValue(Ground, grounded);
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

            private static QusapComboDefinition FindDefinition(QusapComboId comboId)
            {
                IReadOnlyList<QusapComboDefinition> definitions =
                    QusapComboDefinition.CreateDefaultDefinitions();
                for (int i = 0; i < definitions.Count; i++)
                {
                    if (definitions[i].ComboId == comboId)
                    {
                        return definitions[i];
                    }
                }

                Assert.Fail($"Missing combo definition {comboId}.");
                return null;
            }

            private static double ValidDelay(QusapComboStep step)
            {
                return (step.MinimumDelay + step.MaximumDelay) / 2d;
            }

            private static void SetField(object target, string name, object value)
            {
                FieldInfo field = target.GetType().GetField(
                    name, BindingFlags.Instance | BindingFlags.NonPublic);
                Assert.That(field, Is.Not.Null, $"Missing field {name}");
                field.SetValue(target, value);
            }

            private static void Invoke(object target, string name, params object[] arguments)
            {
                MethodInfo method = target.GetType().GetMethod(
                    name, BindingFlags.Instance | BindingFlags.NonPublic);
                Assert.That(method, Is.Not.Null, $"Missing method {name}");
                method.Invoke(target, arguments);
            }
        }
    }

    public sealed class QusapTestDisarmable : MonoBehaviour, IQusapDisarmable
    {
        public bool CanBeDisarmedValue { get; set; }
        public bool AcceptsDisarm { get; set; }
        public int CallCount { get; private set; }
        public QusapCombatController LastSource { get; private set; }
        public bool CanBeDisarmed => CanBeDisarmedValue;

        public bool TryDisarm(QusapCombatController source)
        {
            CallCount++;
            LastSource = source;
            return AcceptsDisarm;
        }
    }
}
