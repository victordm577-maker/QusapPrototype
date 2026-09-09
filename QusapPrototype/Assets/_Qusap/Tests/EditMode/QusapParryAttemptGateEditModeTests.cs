using NUnit.Framework;

namespace Qusap.Tests
{
    public sealed class QusapParryAttemptGateEditModeTests
    {
        private const ulong FirstFinisher = 101;
        private const ulong SecondFinisher = 202;

        [Test]
        public void FirstPressStartsRecovery()
        {
            QusapParryAttemptGate gate = new();

            QusapParryAttemptGateResult result = gate.ProcessPress(
                1, 10d, null, QusapFinisherDefensePhase.None, eligible: true);

            Assert.That(result.Outcome, Is.EqualTo(QusapParryAttemptOutcome.NoIncomingFinisher));
            Assert.That(gate.RecoveryEndsAt, Is.EqualTo(10.5d));
            Assert.That(gate.IsOnRecovery(10.499d), Is.True);
            Assert.That(gate.IsOnRecovery(10.5d), Is.False);
        }

        [Test]
        public void RepeatedPressIdIsIgnored()
        {
            QusapParryAttemptGate gate = new();
            gate.ProcessPress(7, 1d, null, QusapFinisherDefensePhase.None, eligible: true);

            QusapParryAttemptGateResult result = gate.ProcessPress(
                7, 2d, FirstFinisher, QusapFinisherDefensePhase.ParryWindow, eligible: true);

            Assert.That(result.Outcome, Is.EqualTo(QusapParryAttemptOutcome.DuplicateOrStalePressIgnored));
            Assert.That(gate.RecoveryEndsAt, Is.EqualTo(1.5d));
            Assert.That(gate.HasAttempted(FirstFinisher), Is.False);
        }

        [Test]
        public void NewPressDuringRecoveryIsDiscarded()
        {
            QusapParryAttemptGate gate = new();
            gate.ProcessPress(1, 10d, null, QusapFinisherDefensePhase.None, eligible: true);

            QusapParryAttemptGateResult result = gate.ProcessPress(
                2, 10.1d, FirstFinisher, QusapFinisherDefensePhase.ParryWindow, eligible: true);

            Assert.That(result.Outcome, Is.EqualTo(QusapParryAttemptOutcome.OnRecovery));
            Assert.That(gate.RecoveryEndsAt, Is.EqualTo(10.6d));
            Assert.That(gate.HasAttempted(FirstFinisher), Is.False);
        }

        [Test]
        public void DiscardedPressIsNeverBuffered()
        {
            QusapParryAttemptGate gate = new();
            gate.ProcessPress(1, 1d, null, QusapFinisherDefensePhase.None, eligible: true);
            gate.ProcessPress(2, 1.1d, FirstFinisher, QusapFinisherDefensePhase.ParryWindow, eligible: true);

            Assert.That(gate.HasAttempted(FirstFinisher), Is.False);
            QusapParryAttemptGateResult result = gate.ProcessPress(
                3, 1.6d, FirstFinisher, QusapFinisherDefensePhase.ParryWindow, eligible: true);
            Assert.That(result.Outcome, Is.EqualTo(QusapParryAttemptOutcome.Success));
        }

        [Test]
        public void TooEarlyAttemptConsumesFinisherOpportunity()
        {
            QusapParryAttemptGate gate = new();

            QusapParryAttemptGateResult result = gate.ProcessPress(
                1, 2d, FirstFinisher, QusapFinisherDefensePhase.Telegraph, eligible: true);

            Assert.That(result.Outcome, Is.EqualTo(QusapParryAttemptOutcome.TooEarly));
            Assert.That(result.ConsumedFinisherOpportunity, Is.True);
            Assert.That(gate.HasAttempted(FirstFinisher), Is.True);
        }

        [Test]
        public void LaterPressCannotCorrectTooEarlyAttempt()
        {
            QusapParryAttemptGate gate = new();
            gate.ProcessPress(1, 2d, FirstFinisher, QusapFinisherDefensePhase.Telegraph, eligible: true);

            QusapParryAttemptGateResult result = gate.ProcessPress(
                2, 2.5d, FirstFinisher, QusapFinisherDefensePhase.ParryWindow, eligible: true);

            Assert.That(result.Outcome, Is.EqualTo(QusapParryAttemptOutcome.AlreadyAttempted));
            Assert.That(result.Succeeded, Is.False);
        }

        [Test]
        public void CorrectFirstAttemptDuringWindowSucceeds()
        {
            QusapParryAttemptGate gate = new();

            QusapParryAttemptGateResult result = gate.ProcessPress(
                1, 3d, FirstFinisher, QusapFinisherDefensePhase.ParryWindow, eligible: true);

            Assert.That(result.Outcome, Is.EqualTo(QusapParryAttemptOutcome.Success));
            Assert.That(result.Succeeded, Is.True);
        }

        [Test]
        public void SuccessfulAttemptCannotRepeat()
        {
            QusapParryAttemptGate gate = new();
            gate.ProcessPress(1, 3d, FirstFinisher, QusapFinisherDefensePhase.ParryWindow, eligible: true);

            QusapParryAttemptGateResult result = gate.ProcessPress(
                2, 3.5d, FirstFinisher, QusapFinisherDefensePhase.ParryWindow, eligible: true);

            Assert.That(result.Outcome, Is.EqualTo(QusapParryAttemptOutcome.AlreadyAttempted));
        }

        [Test]
        public void NewFinisherGetsNewOpportunity()
        {
            QusapParryAttemptGate gate = new();
            gate.ProcessPress(1, 4d, FirstFinisher, QusapFinisherDefensePhase.Telegraph, eligible: true);

            QusapParryAttemptGateResult result = gate.ProcessPress(
                2, 4.5d, SecondFinisher, QusapFinisherDefensePhase.ParryWindow, eligible: true);

            Assert.That(result.Outcome, Is.EqualTo(QusapParryAttemptOutcome.Success));
        }

        [Test]
        public void AttemptIsScopedToSelectedIncomingFinisher()
        {
            QusapParryAttemptGate gate = new();
            gate.ProcessPress(1, 5d, FirstFinisher, QusapFinisherDefensePhase.Telegraph, eligible: true);

            Assert.That(gate.HasAttempted(FirstFinisher), Is.True);
            Assert.That(gate.HasAttempted(SecondFinisher), Is.False);
            Assert.That(gate.CanAttempt(SecondFinisher, 5.5d, eligible: true), Is.True);
        }

        [Test]
        public void NonFiniteTimestampIsRejected()
        {
            QusapParryAttemptGate gate = new();

            QusapParryAttemptGateResult result = gate.ProcessPress(
                1, double.NaN, FirstFinisher, QusapFinisherDefensePhase.ParryWindow, eligible: true);

            Assert.That(result.Outcome, Is.EqualTo(QusapParryAttemptOutcome.InvalidTimestampIgnored));
            Assert.That(gate.HasAttempted(FirstFinisher), Is.False);
            Assert.That(gate.RecoveryEndsAt, Is.Zero);
        }

        [Test]
        public void BackwardTimestampIsHandledSafely()
        {
            QusapParryAttemptGate gate = new();
            gate.ProcessPress(1, 10d, null, QusapFinisherDefensePhase.None, eligible: true);

            QusapParryAttemptGateResult result = gate.ProcessPress(
                2, 9d, FirstFinisher, QusapFinisherDefensePhase.ParryWindow, eligible: true);

            Assert.That(result.Outcome, Is.EqualTo(QusapParryAttemptOutcome.InvalidTimestampIgnored));
            Assert.That(gate.HasAttempted(FirstFinisher), Is.False);
            Assert.That(gate.RecoveryEndsAt, Is.EqualTo(10.5d));
        }

        [Test]
        public void InvalidRecoveryDurationIsNormalized()
        {
            Assert.That(
                new QusapParryAttemptGate(double.NaN).RecoveryDuration,
                Is.EqualTo(QusapParryAttemptGate.DefaultRecoveryDuration));
            Assert.That(
                new QusapParryAttemptGate(double.PositiveInfinity).RecoveryDuration,
                Is.EqualTo(QusapParryAttemptGate.DefaultRecoveryDuration));
            Assert.That(
                new QusapParryAttemptGate(-1d).RecoveryDuration,
                Is.EqualTo(QusapParryAttemptGate.DefaultRecoveryDuration));
            Assert.That(new QusapParryAttemptGate(0d).RecoveryDuration, Is.Zero);
        }
    }
}
