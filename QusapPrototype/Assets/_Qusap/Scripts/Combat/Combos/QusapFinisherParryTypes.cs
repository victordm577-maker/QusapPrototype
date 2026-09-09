using System;
using UnityEngine;

namespace Qusap
{
    public enum QusapFinisherDefensePhase
    {
        None,
        Telegraph,
        ParryWindow,
        ReadyToResolve,
        Parried,
        Cancelled
    }

    public enum QusapParryAttemptOutcome
    {
        None,
        Success,
        TooEarly,
        TooLate,
        NoIncomingFinisher,
        Ineligible,
        OnRecovery,
        AlreadyAttempted,
        DuplicateOrStalePressIgnored,
        InvalidTimestampIgnored
    }

    [Serializable]
    public sealed class QusapFinisherParrySettings
    {
        public const double DefaultTelegraphDelay = 0.10d;
        public const double DefaultParryWindowDuration = 0.35d;
        public const double DefaultSuccessfulParryAttackerVulnerabilityDuration = 0.60d;
        public const double DefaultFailedParryDefenderVulnerabilityDuration = 0.30d;

        [SerializeField] private double telegraphDelay = DefaultTelegraphDelay;
        [SerializeField] private double parryWindowDuration = DefaultParryWindowDuration;
        [SerializeField] private double successfulParryAttackerVulnerabilityDuration =
            DefaultSuccessfulParryAttackerVulnerabilityDuration;
        [SerializeField] private double failedParryDefenderVulnerabilityDuration =
            DefaultFailedParryDefenderVulnerabilityDuration;

        public QusapFinisherParrySettings(
            double telegraphDelay,
            double parryWindowDuration,
            double successfulParryAttackerVulnerabilityDuration,
            double failedParryDefenderVulnerabilityDuration)
        {
            ValidateValue(telegraphDelay, nameof(telegraphDelay), allowZero: true);
            ValidateValue(parryWindowDuration, nameof(parryWindowDuration), allowZero: false);
            ValidateValue(
                successfulParryAttackerVulnerabilityDuration,
                nameof(successfulParryAttackerVulnerabilityDuration),
                allowZero: true);
            ValidateValue(
                failedParryDefenderVulnerabilityDuration,
                nameof(failedParryDefenderVulnerabilityDuration),
                allowZero: true);

            this.telegraphDelay = telegraphDelay;
            this.parryWindowDuration = parryWindowDuration;
            this.successfulParryAttackerVulnerabilityDuration =
                successfulParryAttackerVulnerabilityDuration;
            this.failedParryDefenderVulnerabilityDuration =
                failedParryDefenderVulnerabilityDuration;
        }

        public double TelegraphDelay => telegraphDelay;
        public double ParryWindowDuration => parryWindowDuration;
        public double SuccessfulParryAttackerVulnerabilityDuration =>
            successfulParryAttackerVulnerabilityDuration;
        public double FailedParryDefenderVulnerabilityDuration =>
            failedParryDefenderVulnerabilityDuration;

        public static QusapFinisherParrySettings CreateDefault()
        {
            return new QusapFinisherParrySettings(
                DefaultTelegraphDelay,
                DefaultParryWindowDuration,
                DefaultSuccessfulParryAttackerVulnerabilityDuration,
                DefaultFailedParryDefenderVulnerabilityDuration);
        }

        internal void ValidateSerializedValues()
        {
            telegraphDelay = Normalize(
                telegraphDelay, DefaultTelegraphDelay, allowZero: true);
            parryWindowDuration = Normalize(
                parryWindowDuration, DefaultParryWindowDuration, allowZero: false);
            successfulParryAttackerVulnerabilityDuration = Normalize(
                successfulParryAttackerVulnerabilityDuration,
                DefaultSuccessfulParryAttackerVulnerabilityDuration,
                allowZero: true);
            failedParryDefenderVulnerabilityDuration = Normalize(
                failedParryDefenderVulnerabilityDuration,
                DefaultFailedParryDefenderVulnerabilityDuration,
                allowZero: true);
        }

        private static void ValidateValue(double value, string parameterName, bool allowZero)
        {
            if (!IsFinite(value) || value < 0d || (!allowZero && value == 0d))
            {
                throw new ArgumentOutOfRangeException(
                    parameterName,
                    value,
                    allowZero
                        ? "The duration must be finite and non-negative."
                        : "The duration must be finite and greater than zero.");
            }
        }

        private static double Normalize(double value, double fallback, bool allowZero)
        {
            return IsFinite(value) && value >= 0d && (allowZero || value > 0d)
                ? value
                : fallback;
        }

        private static bool IsFinite(double value)
        {
            return !double.IsNaN(value) && !double.IsInfinity(value);
        }
    }

    public readonly struct QusapFinisherDefenseTransition
    {
        internal QusapFinisherDefenseTransition(
            QusapFinisherDefensePhase previousPhase,
            QusapFinisherDefensePhase currentPhase)
        {
            PreviousPhase = previousPhase;
            CurrentPhase = currentPhase;
        }

        public QusapFinisherDefensePhase PreviousPhase { get; }
        public QusapFinisherDefensePhase CurrentPhase { get; }
        public bool Changed => PreviousPhase != CurrentPhase;
        public bool WindowOpened => Changed && CurrentPhase == QusapFinisherDefensePhase.ParryWindow;
        public bool BecameReadyToResolve =>
            Changed && CurrentPhase == QusapFinisherDefensePhase.ReadyToResolve;

        internal static QusapFinisherDefenseTransition None(QusapFinisherDefensePhase phase)
        {
            return new QusapFinisherDefenseTransition(phase, phase);
        }
    }

    public readonly struct QusapParryAttemptResult
    {
        internal QusapParryAttemptResult(
            QusapParryAttemptOutcome outcome,
            QusapFinisherDefenseTransition transition)
        {
            Outcome = outcome;
            Transition = transition;
        }

        public QusapParryAttemptOutcome Outcome { get; }
        public QusapFinisherDefenseTransition Transition { get; }
        public bool Succeeded => Outcome == QusapParryAttemptOutcome.Success;
    }

    public readonly struct QusapParryAttemptFeedback
    {
        internal QusapParryAttemptFeedback(
            ulong pressId,
            double timestamp,
            QusapCombatController attacker,
            QusapCombatController defender,
            QusapComboId? comboId,
            ulong finisherSequenceId,
            QusapParryAttemptOutcome outcome)
        {
            PressId = pressId;
            Timestamp = timestamp;
            Attacker = attacker;
            Defender = defender;
            ComboId = comboId;
            FinisherSequenceId = finisherSequenceId;
            Outcome = outcome;
        }

        public ulong PressId { get; }
        public double Timestamp { get; }
        public QusapCombatController Attacker { get; }
        public QusapCombatController Defender { get; }
        public QusapComboId? ComboId { get; }
        public ulong FinisherSequenceId { get; }
        public QusapParryAttemptOutcome Outcome { get; }
        public bool HasIncomingFinisher => Attacker != null && ComboId.HasValue;
        public bool Succeeded => Outcome == QusapParryAttemptOutcome.Success;
    }
}
