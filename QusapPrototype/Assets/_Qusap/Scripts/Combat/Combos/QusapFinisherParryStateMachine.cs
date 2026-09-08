using System;

namespace Qusap
{
    public sealed class QusapFinisherParryStateMachine
    {
        private bool hasProcessedParryPress;
        private ulong lastProcessedParryPressId;
        private bool hasAcceptedTimestamp;
        private double lastAcceptedTimestamp;

        public QusapFinisherDefensePhase Phase { get; private set; }
        public QusapComboId? ComboId { get; private set; }
        public double WindowOpensAt { get; private set; }
        public double WindowClosesAt { get; private set; }
        public bool IsWindowOpen => Phase == QusapFinisherDefensePhase.ParryWindow;
        public bool IsReadyToResolve => Phase == QusapFinisherDefensePhase.ReadyToResolve;

        public void Arm(
            QusapComboId comboId,
            double armedTimestamp,
            QusapFinisherParrySettings settings)
        {
            if (!Enum.IsDefined(typeof(QusapComboId), comboId))
            {
                throw new ArgumentOutOfRangeException(nameof(comboId), comboId, "Unknown combo identifier.");
            }

            if (!IsFinite(armedTimestamp))
            {
                throw new ArgumentOutOfRangeException(
                    nameof(armedTimestamp), armedTimestamp, "The armed timestamp must be finite.");
            }

            if (settings == null)
            {
                throw new ArgumentNullException(nameof(settings));
            }

            ComboId = comboId;
            WindowOpensAt = armedTimestamp + settings.TelegraphDelay;
            WindowClosesAt = WindowOpensAt + settings.ParryWindowDuration;
            if (!IsFinite(WindowOpensAt) || !IsFinite(WindowClosesAt))
            {
                throw new ArgumentOutOfRangeException(
                    nameof(armedTimestamp), armedTimestamp, "The configured parry timeline exceeds the supported range.");
            }

            Phase = QusapFinisherDefensePhase.Telegraph;
            hasProcessedParryPress = false;
            lastProcessedParryPressId = 0;
            hasAcceptedTimestamp = true;
            lastAcceptedTimestamp = armedTimestamp;
        }

        public QusapFinisherDefenseTransition Advance(double timestamp)
        {
            if (!TryAcceptTimestamp(timestamp))
            {
                return QusapFinisherDefenseTransition.None(Phase);
            }

            return AdvanceAcceptedTimestamp(timestamp);
        }

        public QusapParryAttemptResult TryParry(ulong pressId, double timestamp)
        {
            if (hasProcessedParryPress && pressId <= lastProcessedParryPressId)
            {
                return Result(QusapParryAttemptOutcome.DuplicateOrStalePressIgnored);
            }

            hasProcessedParryPress = true;
            lastProcessedParryPressId = pressId;

            if (!TryAcceptTimestamp(timestamp))
            {
                return Result(QusapParryAttemptOutcome.InvalidTimestampIgnored);
            }

            if (Phase == QusapFinisherDefensePhase.None
                || Phase == QusapFinisherDefensePhase.Cancelled
                || Phase == QusapFinisherDefensePhase.Parried)
            {
                return Result(QusapParryAttemptOutcome.NoIncomingFinisher);
            }

            QusapFinisherDefenseTransition transition = AdvanceAcceptedTimestamp(timestamp);
            if (Phase == QusapFinisherDefensePhase.Telegraph)
            {
                return new QusapParryAttemptResult(QusapParryAttemptOutcome.TooEarly, transition);
            }

            if (Phase == QusapFinisherDefensePhase.ReadyToResolve)
            {
                return new QusapParryAttemptResult(QusapParryAttemptOutcome.TooLate, transition);
            }

            if (Phase != QusapFinisherDefensePhase.ParryWindow)
            {
                return new QusapParryAttemptResult(
                    QusapParryAttemptOutcome.NoIncomingFinisher, transition);
            }

            Phase = QusapFinisherDefensePhase.Parried;
            return new QusapParryAttemptResult(QusapParryAttemptOutcome.Success, transition);
        }

        public void Cancel()
        {
            if (Phase == QusapFinisherDefensePhase.Telegraph
                || Phase == QusapFinisherDefensePhase.ParryWindow
                || Phase == QusapFinisherDefensePhase.ReadyToResolve)
            {
                Phase = QusapFinisherDefensePhase.Cancelled;
            }
        }

        public void Reset()
        {
            Phase = QusapFinisherDefensePhase.None;
            ComboId = null;
            WindowOpensAt = 0d;
            WindowClosesAt = 0d;
            hasProcessedParryPress = false;
            lastProcessedParryPressId = 0;
            hasAcceptedTimestamp = false;
            lastAcceptedTimestamp = 0d;
        }

        private QusapFinisherDefenseTransition AdvanceAcceptedTimestamp(double timestamp)
        {
            QusapFinisherDefensePhase previousPhase = Phase;
            if (Phase == QusapFinisherDefensePhase.Telegraph)
            {
                if (timestamp > WindowClosesAt)
                {
                    Phase = QusapFinisherDefensePhase.ReadyToResolve;
                }
                else if (timestamp >= WindowOpensAt)
                {
                    Phase = QusapFinisherDefensePhase.ParryWindow;
                }
            }
            else if (Phase == QusapFinisherDefensePhase.ParryWindow
                && timestamp > WindowClosesAt)
            {
                Phase = QusapFinisherDefensePhase.ReadyToResolve;
            }

            return new QusapFinisherDefenseTransition(previousPhase, Phase);
        }

        private bool TryAcceptTimestamp(double timestamp)
        {
            if (!IsFinite(timestamp)
                || (hasAcceptedTimestamp && timestamp < lastAcceptedTimestamp))
            {
                return false;
            }

            hasAcceptedTimestamp = true;
            lastAcceptedTimestamp = timestamp;
            return true;
        }

        private QusapParryAttemptResult Result(QusapParryAttemptOutcome outcome)
        {
            return new QusapParryAttemptResult(
                outcome, QusapFinisherDefenseTransition.None(Phase));
        }

        private static bool IsFinite(double value)
        {
            return !double.IsNaN(value) && !double.IsInfinity(value);
        }
    }
}
