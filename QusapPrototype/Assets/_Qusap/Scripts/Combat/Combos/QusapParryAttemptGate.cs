using System;
using System.Collections.Generic;

namespace Qusap
{
    /// <summary>
    /// Pure, deterministic policy for accepting fresh parry presses.
    /// It deliberately owns no Unity or input state.
    /// </summary>
    public sealed class QusapParryAttemptGate
    {
        public const double DefaultRecoveryDuration = 0.50d;

        private readonly HashSet<ulong> attemptedFinishers = new();
        private bool hasProcessedPress;
        private ulong lastProcessedPressId;
        private bool hasAcceptedTimestamp;
        private double lastAcceptedTimestamp;

        public QusapParryAttemptGate(double recoveryDuration = DefaultRecoveryDuration)
        {
            RecoveryDuration = NormalizeRecoveryDuration(recoveryDuration);
        }

        public double RecoveryDuration { get; }
        public double RecoveryEndsAt { get; private set; }

        public QusapParryAttemptGateResult ProcessPress(
            ulong pressId,
            double timestamp,
            ulong? incomingFinisherIdentity,
            QusapFinisherDefensePhase phase,
            bool eligible)
        {
            if (hasProcessedPress && pressId <= lastProcessedPressId)
            {
                return Result(QusapParryAttemptOutcome.DuplicateOrStalePressIgnored);
            }

            hasProcessedPress = true;
            lastProcessedPressId = pressId;
            if (!IsFinite(timestamp)
                || (hasAcceptedTimestamp && timestamp < lastAcceptedTimestamp))
            {
                return Result(QusapParryAttemptOutcome.InvalidTimestampIgnored);
            }

            bool wasOnRecovery = IsOnRecovery(timestamp);
            hasAcceptedTimestamp = true;
            lastAcceptedTimestamp = timestamp;
            RecoveryEndsAt = SaturatingAdd(timestamp, RecoveryDuration);

            if (wasOnRecovery)
            {
                return Result(QusapParryAttemptOutcome.OnRecovery);
            }

            if (!incomingFinisherIdentity.HasValue)
            {
                return Result(QusapParryAttemptOutcome.NoIncomingFinisher);
            }

            ulong identity = incomingFinisherIdentity.Value;
            if (!attemptedFinishers.Add(identity))
            {
                return Result(QusapParryAttemptOutcome.AlreadyAttempted);
            }

            if (!eligible)
            {
                return Result(QusapParryAttemptOutcome.Ineligible, consumed: true);
            }

            QusapParryAttemptOutcome outcome = phase switch
            {
                QusapFinisherDefensePhase.Telegraph => QusapParryAttemptOutcome.TooEarly,
                QusapFinisherDefensePhase.ParryWindow => QusapParryAttemptOutcome.Success,
                QusapFinisherDefensePhase.ReadyToResolve => QusapParryAttemptOutcome.TooLate,
                _ => QusapParryAttemptOutcome.NoIncomingFinisher
            };
            return Result(outcome, consumed: true);
        }

        public bool CanAttempt(ulong finisherIdentity, double timestamp, bool eligible)
        {
            return eligible
                && IsFinite(timestamp)
                && (!hasAcceptedTimestamp || timestamp >= lastAcceptedTimestamp)
                && !IsOnRecovery(timestamp)
                && !attemptedFinishers.Contains(finisherIdentity);
        }

        public bool IsOnRecovery(double timestamp)
        {
            return IsFinite(timestamp)
                && hasAcceptedTimestamp
                && timestamp < RecoveryEndsAt;
        }

        public bool HasAttempted(ulong finisherIdentity)
        {
            return attemptedFinishers.Contains(finisherIdentity);
        }

        public void ReleaseFinisher(ulong finisherIdentity)
        {
            attemptedFinishers.Remove(finisherIdentity);
        }

        public void Reset()
        {
            attemptedFinishers.Clear();
            hasProcessedPress = false;
            lastProcessedPressId = 0;
            hasAcceptedTimestamp = false;
            lastAcceptedTimestamp = 0d;
            RecoveryEndsAt = 0d;
        }

        public static double NormalizeRecoveryDuration(double value)
        {
            return IsFinite(value) && value >= 0d ? value : DefaultRecoveryDuration;
        }

        private static QusapParryAttemptGateResult Result(
            QusapParryAttemptOutcome outcome,
            bool consumed = false)
        {
            return new QusapParryAttemptGateResult(outcome, consumed);
        }

        private static double SaturatingAdd(double timestamp, double duration)
        {
            double result = timestamp + duration;
            return IsFinite(result) ? result : double.MaxValue;
        }

        private static bool IsFinite(double value)
        {
            return !double.IsNaN(value) && !double.IsInfinity(value);
        }
    }

    public readonly struct QusapParryAttemptGateResult
    {
        internal QusapParryAttemptGateResult(
            QusapParryAttemptOutcome outcome,
            bool consumedFinisherOpportunity)
        {
            Outcome = outcome;
            ConsumedFinisherOpportunity = consumedFinisherOpportunity;
        }

        public QusapParryAttemptOutcome Outcome { get; }
        public bool ConsumedFinisherOpportunity { get; }
        public bool Succeeded => Outcome == QusapParryAttemptOutcome.Success;
    }
}
