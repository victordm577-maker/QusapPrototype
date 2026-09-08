using System;
using NUnit.Framework;

namespace Qusap.Tests
{
    public sealed class QusapFinisherParryStateMachineEditModeTests
    {
        private const double ArmedAt = 10d;
        private QusapFinisherParrySettings settings;
        private QusapFinisherParryStateMachine machine;

        [SetUp]
        public void SetUp()
        {
            settings = new QusapFinisherParrySettings(0.1d, 0.35d, 0.6d, 0.3d);
            machine = new QusapFinisherParryStateMachine();
        }

        [Test]
        public void ArmStartsInTelegraph()
        {
            Arm();
            Assert.That(machine.Phase, Is.EqualTo(QusapFinisherDefensePhase.Telegraph));
            Assert.That(machine.ComboId, Is.EqualTo(QusapComboId.Launch));
        }

        [Test]
        public void WindowOpensAfterConfiguredDelay()
        {
            Arm();
            QusapFinisherDefenseTransition transition = machine.Advance(machine.WindowOpensAt);
            Assert.That(transition.WindowOpened, Is.True);
            Assert.That(machine.IsWindowOpen, Is.True);
        }

        [Test]
        public void WindowDoesNotOpenEarly()
        {
            Arm();
            machine.Advance(machine.WindowOpensAt - 0.001d);
            Assert.That(machine.Phase, Is.EqualTo(QusapFinisherDefensePhase.Telegraph));
        }

        [Test]
        public void ParrySucceedsAtOpeningBoundary()
        {
            Arm();
            AssertSuccess(machine.TryParry(1, machine.WindowOpensAt));
        }

        [Test]
        public void ParrySucceedsInsideWindow()
        {
            Arm();
            AssertSuccess(machine.TryParry(1, Midpoint()));
        }

        [Test]
        public void ParrySucceedsAtClosingBoundary()
        {
            Arm();
            AssertSuccess(machine.TryParry(1, machine.WindowClosesAt));
        }

        [Test]
        public void ParryBeforeWindowIsTooEarly()
        {
            Arm();
            QusapParryAttemptResult result = machine.TryParry(1, machine.WindowOpensAt - 0.001d);
            Assert.That(result.Outcome, Is.EqualTo(QusapParryAttemptOutcome.TooEarly));
            Assert.That(machine.Phase, Is.EqualTo(QusapFinisherDefensePhase.Telegraph));
        }

        [Test]
        public void ParryAfterWindowIsTooLate()
        {
            Arm();
            QusapParryAttemptResult result = machine.TryParry(1, machine.WindowClosesAt + 0.001d);
            Assert.That(result.Outcome, Is.EqualTo(QusapParryAttemptOutcome.TooLate));
            Assert.That(machine.IsReadyToResolve, Is.True);
        }

        [Test]
        public void ExpiredWindowBecomesReadyToResolve()
        {
            Arm();
            QusapFinisherDefenseTransition transition = machine.Advance(machine.WindowClosesAt + 0.001d);
            Assert.That(transition.BecameReadyToResolve, Is.True);
            Assert.That(machine.IsReadyToResolve, Is.True);
        }

        [Test]
        public void ReadyTransitionOccursOnlyOnce()
        {
            Arm();
            Assert.That(machine.Advance(machine.WindowClosesAt + 0.001d).BecameReadyToResolve, Is.True);
            Assert.That(machine.Advance(machine.WindowClosesAt + 1d).BecameReadyToResolve, Is.False);
        }

        [Test]
        public void SuccessfulParryNeverBecomesReady()
        {
            Arm();
            AssertSuccess(machine.TryParry(1, Midpoint()));
            machine.Advance(machine.WindowClosesAt + 10d);
            Assert.That(machine.Phase, Is.EqualTo(QusapFinisherDefensePhase.Parried));
            Assert.That(machine.IsReadyToResolve, Is.False);
        }

        [Test]
        public void CancelPreventsFutureResolution()
        {
            Arm();
            machine.Cancel();
            machine.Advance(machine.WindowClosesAt + 10d);
            Assert.That(machine.Phase, Is.EqualTo(QusapFinisherDefensePhase.Cancelled));
        }

        [Test]
        public void RepeatedPressIdCannotParryTwice()
        {
            Arm();
            machine.TryParry(5, machine.WindowOpensAt - 0.01d);
            QusapParryAttemptResult repeated = machine.TryParry(5, Midpoint());
            Assert.That(repeated.Outcome, Is.EqualTo(QusapParryAttemptOutcome.DuplicateOrStalePressIgnored));
            Assert.That(machine.Phase, Is.EqualTo(QusapFinisherDefensePhase.Telegraph));
        }

        [Test]
        public void HeldInputSimulationCannotParryTwice()
        {
            Arm();
            AssertSuccess(machine.TryParry(7, Midpoint()));
            for (int i = 0; i < 10; i++)
            {
                Assert.That(
                    machine.TryParry(7, Midpoint()).Outcome,
                    Is.EqualTo(QusapParryAttemptOutcome.DuplicateOrStalePressIgnored));
            }
        }

        [Test]
        public void BackwardTimestampIsHandledSafely()
        {
            Arm();
            machine.Advance(machine.WindowOpensAt);
            QusapParryAttemptResult result = machine.TryParry(1, ArmedAt);
            Assert.That(result.Outcome, Is.EqualTo(QusapParryAttemptOutcome.InvalidTimestampIgnored));
            Assert.That(machine.Phase, Is.EqualTo(QusapFinisherDefensePhase.ParryWindow));
        }

        [Test]
        public void NonFiniteTimestampIsHandledSafely()
        {
            Arm();
            Assert.That(
                machine.TryParry(1, double.NaN).Outcome,
                Is.EqualTo(QusapParryAttemptOutcome.InvalidTimestampIgnored));
            Assert.That(machine.Advance(double.PositiveInfinity).Changed, Is.False);
            Assert.That(machine.Phase, Is.EqualTo(QusapFinisherDefensePhase.Telegraph));
        }

        [Test]
        public void AdvanceIsIdempotent()
        {
            Arm();
            QusapFinisherDefenseTransition first = machine.Advance(machine.WindowOpensAt);
            QusapFinisherDefenseTransition second = machine.Advance(machine.WindowOpensAt);
            Assert.That(first.WindowOpened, Is.True);
            Assert.That(second.Changed, Is.False);
        }

        [Test]
        public void ResetClearsAllState()
        {
            Arm();
            machine.Advance(machine.WindowOpensAt);
            machine.Reset();
            Assert.That(machine.Phase, Is.EqualTo(QusapFinisherDefensePhase.None));
            Assert.That(machine.ComboId, Is.Null);
            Assert.That(machine.WindowOpensAt, Is.Zero);
            Assert.That(machine.WindowClosesAt, Is.Zero);
        }

        [Test]
        public void InvalidSettingsAreRejectedOrNormalizedSafely()
        {
            Assert.Throws<ArgumentOutOfRangeException>(() =>
                new QusapFinisherParrySettings(-0.1d, 0.35d, 0.6d, 0.3d));
            Assert.Throws<ArgumentOutOfRangeException>(() =>
                new QusapFinisherParrySettings(0.1d, 0d, 0.6d, 0.3d));
            Assert.Throws<ArgumentOutOfRangeException>(() =>
                new QusapFinisherParrySettings(0.1d, double.NaN, 0.6d, 0.3d));
            Assert.Throws<ArgumentOutOfRangeException>(() =>
                new QusapFinisherParrySettings(0.1d, 0.35d, double.PositiveInfinity, 0.3d));
        }

        [Test]
        public void StateMachineDoesNotDependOnUnityTime()
        {
            QusapFinisherParryStateMachine other = new();
            Arm();
            other.Arm(QusapComboId.Launch, ArmedAt, settings);

            QusapFinisherDefenseTransition first = machine.Advance(Midpoint());
            QusapFinisherDefenseTransition second = other.Advance(Midpoint());
            Assert.That(second.CurrentPhase, Is.EqualTo(first.CurrentPhase));
            Assert.That(other.WindowOpensAt, Is.EqualTo(machine.WindowOpensAt));
            Assert.That(other.WindowClosesAt, Is.EqualTo(machine.WindowClosesAt));
        }

        private void Arm()
        {
            machine.Arm(QusapComboId.Launch, ArmedAt, settings);
        }

        private double Midpoint()
        {
            return machine.WindowOpensAt
                + (machine.WindowClosesAt - machine.WindowOpensAt) / 2d;
        }

        private static void AssertSuccess(QusapParryAttemptResult result)
        {
            Assert.That(result.Outcome, Is.EqualTo(QusapParryAttemptOutcome.Success));
            Assert.That(result.Succeeded, Is.True);
        }
    }
}
