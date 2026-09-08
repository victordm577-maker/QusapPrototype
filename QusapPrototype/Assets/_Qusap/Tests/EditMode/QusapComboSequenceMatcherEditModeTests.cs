using System;
using System.Collections.Generic;
using NUnit.Framework;

namespace Qusap.Tests
{
    public sealed class QusapComboSequenceMatcherEditModeTests
    {
        private const double MinimumDelay = 0.05d;
        private const double MaximumDelay = 0.5d;

        private IReadOnlyList<QusapComboDefinition> definitions;
        private QusapComboSequenceMatcher matcher;
        private ulong nextPressId;

        [SetUp]
        public void SetUp()
        {
            definitions = QusapComboDefinition.CreateDefaultDefinitions(MinimumDelay, MaximumDelay);
            matcher = new QusapComboSequenceMatcher(definitions);
            nextPressId = 1;
        }

        [Test]
        public void DamageComboCompletesWithExactSequence()
        {
            Press(QusapCombatCommand.WeaponLight, 0d);
            Press(QusapCombatCommand.WeaponLight, 0.1d);
            Press(QusapCombatCommand.BodyAttack, 0.2d);
            QusapComboMatchResult result = Press(QusapCombatCommand.WeaponStrong, 0.3d);

            AssertCompleted(result, QusapComboId.Damage);
        }

        [Test]
        public void DisarmComboCompletesWithExactSequence()
        {
            Press(QusapCombatCommand.WeaponLight, 0d);
            Press(QusapCombatCommand.WeaponLight, 0.1d);
            QusapComboMatchResult result = Press(QusapCombatCommand.Headbutt, 0.2d);

            AssertCompleted(result, QusapComboId.Disarm);
        }

        [Test]
        public void LaunchComboCompletesWithExactSequence()
        {
            Press(QusapCombatCommand.BodyAttack, 0d);
            Press(QusapCombatCommand.WeaponLight, 0.1d);
            QusapComboMatchResult result = Press(QusapCombatCommand.BodyAttack, 0.2d);

            AssertCompleted(result, QusapComboId.Launch);
        }

        [Test]
        public void SharedPrefixKeepsDamageAndDisarmCandidatesAlive()
        {
            Press(QusapCombatCommand.WeaponLight, 0d);
            QusapComboMatchResult result = Press(QusapCombatCommand.WeaponLight, 0.1d);

            Assert.That(result.CandidatesRemain, Is.True);
            Assert.That(result.HasActiveCandidate(QusapComboId.Damage), Is.True);
            Assert.That(result.HasActiveCandidate(QusapComboId.Disarm), Is.True);
        }

        [Test]
        public void RepeatedPressIdCannotAdvanceSequence()
        {
            QusapComboMatchResult first = matcher.ProcessPress(QusapCombatCommand.WeaponLight, 10, 0d);
            QusapComboMatchResult repeated = matcher.ProcessPress(QusapCombatCommand.WeaponLight, 10, 0.1d);

            Assert.That(first.Advanced, Is.True);
            Assert.That(repeated.Advanced, Is.False);
            Assert.That(
                repeated.Flags & QusapComboMatchFlags.DuplicateOrStalePressIgnored,
                Is.Not.EqualTo(QusapComboMatchFlags.None));
        }

        [Test]
        public void TwoDistinctWeaponLightPressesCanAdvance()
        {
            QusapComboMatchResult first = matcher.ProcessPress(QusapCombatCommand.WeaponLight, 10, 0d);
            QusapComboMatchResult second = matcher.ProcessPress(QusapCombatCommand.WeaponLight, 11, 0.1d);

            Assert.That(first.Advanced, Is.True);
            Assert.That(second.Advanced, Is.True);
            Assert.That(second.HasActiveCandidate(QusapComboId.Damage), Is.True);
            Assert.That(second.HasActiveCandidate(QusapComboId.Disarm), Is.True);
        }

        [Test]
        public void HeldInputSimulationDoesNotCompleteCombo()
        {
            matcher.ProcessPress(QusapCombatCommand.WeaponLight, 7, 0d);
            for (int i = 0; i < 20; i++)
            {
                QusapComboMatchResult result = matcher.ProcessPress(
                    QusapCombatCommand.WeaponLight, 7, 0.1d + i * 0.1d);
                Assert.That(result.Completed, Is.False);
            }
        }

        [Test]
        public void InputBeforeMinimumDelayDoesNotAdvance()
        {
            QusapComboDefinition launch = Definition(QusapComboId.Launch);
            matcher = new QusapComboSequenceMatcher(new[] { launch });
            Press(QusapCombatCommand.BodyAttack, 0d);
            QusapComboMatchResult early = Press(
                QusapCombatCommand.WeaponLight,
                launch.GetStep(1).MinimumDelay / 2d);

            Assert.That(early.Advanced, Is.False);
            Assert.That(early.HasActiveCandidate(QusapComboId.Launch), Is.True);
            Assert.That(Press(QusapCombatCommand.WeaponLight, MinimumDelay).Advanced, Is.True);
        }

        [Test]
        public void InputAfterMaximumDelayExpiresCandidate()
        {
            Press(QusapCombatCommand.BodyAttack, 0d);
            double lateTime = Definition(QusapComboId.Launch).GetStep(1).MaximumDelay + 0.001d;
            QusapComboMatchResult late = Press(QusapCombatCommand.WeaponLight, lateTime);

            Assert.That(late.Completed, Is.False);
            Assert.That(late.CandidatesWereDiscarded, Is.True);
            Assert.That(late.HasActiveCandidate(QusapComboId.Launch), Is.False);
        }

        [Test]
        public void WrongInputDoesNotCompleteComboAccidentally()
        {
            Press(QusapCombatCommand.WeaponLight, 0d);
            Press(QusapCombatCommand.WeaponLight, 0.1d);
            QusapComboMatchResult wrong = Press(QusapCombatCommand.WeaponStrong, 0.2d);

            Assert.That(wrong.Completed, Is.False);
            Assert.That(wrong.CandidatesWereDiscarded, Is.True);
        }

        [Test]
        public void WrongInputCanStartAnotherValidCombo()
        {
            Press(QusapCombatCommand.WeaponLight, 0d);
            QusapComboMatchResult restart = Press(QusapCombatCommand.BodyAttack, 0.1d);
            Press(QusapCombatCommand.WeaponLight, 0.2d);
            QusapComboMatchResult result = Press(QusapCombatCommand.BodyAttack, 0.3d);

            Assert.That(restart.SequenceWasRestarted, Is.True);
            AssertCompleted(result, QusapComboId.Launch);
        }

        [Test]
        public void ResetClearsAllCandidates()
        {
            Press(QusapCombatCommand.WeaponLight, 0d);
            Assert.That(matcher.HasActiveCandidates, Is.True);

            matcher.Reset();

            Assert.That(matcher.HasActiveCandidates, Is.False);
            Assert.That(matcher.ActiveCandidateCount, Is.Zero);
        }

        [Test]
        public void ResetSimulatesThirdPartyInterruption()
        {
            Press(QusapCombatCommand.WeaponLight, 0d);
            Press(QusapCombatCommand.WeaponLight, 0.1d);
            matcher.Reset();

            Press(QusapCombatCommand.BodyAttack, 0.2d);
            QusapComboMatchResult result = Press(QusapCombatCommand.WeaponStrong, 0.3d);

            Assert.That(result.Completed, Is.False);
        }

        [Test]
        public void CompletionResetsMatcher()
        {
            CompleteLaunch(0d);

            Assert.That(matcher.HasActiveCandidates, Is.False);
            Assert.That(matcher.ActiveCandidateCount, Is.Zero);
        }

        [Test]
        public void FinisherCannotRepeatWithoutReenteringFullSequence()
        {
            CompleteLaunch(0d);

            QusapComboMatchResult repeatedFinisher = Press(QusapCombatCommand.BodyAttack, 0.3d);
            QusapComboMatchResult nextInput = Press(QusapCombatCommand.WeaponLight, 0.4d);

            Assert.That(repeatedFinisher.Completed, Is.False);
            Assert.That(nextInput.Completed, Is.False);
        }

        [Test]
        public void BackwardTimestampCannotCompleteCombo()
        {
            Press(QusapCombatCommand.BodyAttack, 10d);
            Press(QusapCombatCommand.WeaponLight, 10.1d);
            QusapComboMatchResult backward = Press(QusapCombatCommand.BodyAttack, 9d);

            Assert.That(backward.Completed, Is.False);
            Assert.That(
                backward.Flags & QusapComboMatchFlags.BackwardTimestampIgnored,
                Is.Not.EqualTo(QusapComboMatchFlags.None));
        }

        [Test]
        public void OffensiveComboCannotContainParry()
        {
            Assert.Throws<ArgumentException>(() => new QusapComboDefinition(
                QusapComboId.Damage,
                "Invalid",
                new[] { Step(QusapCombatCommand.Parry) }));
        }

        [Test]
        public void InvalidTimingWindowIsRejectedOrNormalizedSafely()
        {
            Assert.Throws<ArgumentOutOfRangeException>(() =>
                new QusapComboStep(QusapCombatCommand.BodyAttack, -0.1d, 0.5d));
            Assert.Throws<ArgumentException>(() =>
                new QusapComboStep(QusapCombatCommand.BodyAttack, 0.5d, 0.1d));

            QusapComboDefinition definition = new(
                QusapComboId.Launch,
                "First timing normalized",
                new[]
                {
                    new QusapComboStep(QusapCombatCommand.BodyAttack, 0.2d, 0.4d),
                    Step(QusapCombatCommand.WeaponLight)
                });
            Assert.That(definition.GetStep(0).MinimumDelay, Is.Zero);
            Assert.That(definition.GetStep(0).MaximumDelay, Is.Zero);
        }

        [Test]
        public void EmptyComboDefinitionIsRejected()
        {
            Assert.Throws<ArgumentException>(() => new QusapComboDefinition(
                QusapComboId.Damage,
                "Empty",
                Array.Empty<QusapComboStep>()));
        }

        [Test]
        public void NullComboSequenceIsRejected()
        {
            Assert.Throws<ArgumentNullException>(() => new QusapComboDefinition(
                QusapComboId.Damage,
                "Null",
                null));
        }

        [Test]
        public void ExcessiveStepCountIsRejected()
        {
            QusapComboStep[] steps = new QusapComboStep[QusapComboDefinition.MaximumStepCount + 1];
            for (int i = 0; i < steps.Length; i++)
            {
                steps[i] = Step(QusapCombatCommand.BodyAttack);
            }

            Assert.Throws<ArgumentException>(() => new QusapComboDefinition(
                QusapComboId.Damage,
                "Too long",
                steps));
        }

        [Test]
        public void DuplicateComboSequencesAreRejected()
        {
            QusapComboDefinition first = new(
                QusapComboId.Damage,
                "First",
                new[] { Step(QusapCombatCommand.BodyAttack), Step(QusapCombatCommand.WeaponLight) });
            QusapComboDefinition duplicate = new(
                QusapComboId.Disarm,
                "Duplicate",
                new[] { Step(QusapCombatCommand.BodyAttack), Step(QusapCombatCommand.WeaponLight) });

            Assert.Throws<ArgumentException>(() =>
                new QusapComboSequenceMatcher(new[] { first, duplicate }));
        }

        private QusapComboMatchResult Press(QusapCombatCommand command, double timestamp)
        {
            return matcher.ProcessPress(command, nextPressId++, timestamp);
        }

        private QusapComboDefinition Definition(QusapComboId comboId)
        {
            for (int i = 0; i < definitions.Count; i++)
            {
                if (definitions[i].ComboId == comboId)
                {
                    return definitions[i];
                }
            }

            Assert.Fail($"Missing definition for {comboId}.");
            return null;
        }

        private static QusapComboStep Step(QusapCombatCommand command)
        {
            return new QusapComboStep(command, MinimumDelay, MaximumDelay);
        }

        private void CompleteLaunch(double startTime)
        {
            Press(QusapCombatCommand.BodyAttack, startTime);
            Press(QusapCombatCommand.WeaponLight, startTime + 0.1d);
            AssertCompleted(Press(QusapCombatCommand.BodyAttack, startTime + 0.2d), QusapComboId.Launch);
        }

        private static void AssertCompleted(QusapComboMatchResult result, QusapComboId expected)
        {
            Assert.That(result.Completed, Is.True);
            Assert.That(result.CompletedComboId, Is.EqualTo(expected));
        }
    }
}
