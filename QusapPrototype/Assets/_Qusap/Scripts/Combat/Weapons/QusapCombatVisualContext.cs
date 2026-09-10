using System;

namespace Qusap
{
    public enum QusapCombatVisualCancellationReason
    {
        None,
        Completed,
        Cancelled,
        Hitstun,
        Dash,
        Respawn,
        Parry,
        Disarmed,
        WeaponChanged,
        Disabled,
        CombatDisabled,
        InvalidContext
    }

    /// <summary>
    /// Immutable, gameplay-authored snapshot consumed by combat presentation only.
    /// It never grants authority to start an attack or resolve an impact.
    /// </summary>
    public readonly struct QusapCombatVisualContext
    {
        public QusapCombatVisualContext(
            ulong attackExecutionId,
            QusapCombatCommand command,
            QusapComboId? comboId,
            int comboStepIndex,
            bool isFinisher,
            int capturedFacing,
            QusapAttackPhase phase,
            float normalizedProgress,
            QusapCombatVisualCancellationReason cancellationReason =
                QusapCombatVisualCancellationReason.None)
        {
            if (comboStepIndex < -1)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(comboStepIndex), comboStepIndex, "Combo step must be -1 or greater.");
            }

            AttackExecutionId = attackExecutionId;
            Command = command;
            ComboId = comboId;
            ComboStepIndex = comboStepIndex;
            IsFinisher = isFinisher;
            CapturedFacing = capturedFacing < 0 ? -1 : 1;
            Phase = phase;
            NormalizedProgress = IsFinite(normalizedProgress)
                ? Math.Clamp(normalizedProgress, 0f, 1f)
                : 0f;
            CancellationReason = cancellationReason;
        }

        public ulong AttackExecutionId { get; }
        public QusapCombatCommand Command { get; }
        public QusapComboId? ComboId { get; }
        public int ComboStepIndex { get; }
        public bool IsFinisher { get; }
        public int CapturedFacing { get; }
        public QusapAttackPhase Phase { get; }
        public float NormalizedProgress { get; }
        public QusapCombatVisualCancellationReason CancellationReason { get; }
        public bool IsActive => AttackExecutionId != 0
            && Phase != QusapAttackPhase.Idle
            && CancellationReason == QusapCombatVisualCancellationReason.None;

        public QusapCombatVisualContext Ended(QusapCombatVisualCancellationReason reason)
        {
            return new QusapCombatVisualContext(
                AttackExecutionId,
                Command,
                ComboId,
                ComboStepIndex,
                IsFinisher,
                CapturedFacing,
                QusapAttackPhase.Idle,
                0f,
                reason == QusapCombatVisualCancellationReason.None
                    ? QusapCombatVisualCancellationReason.Cancelled
                    : reason);
        }

        private static bool IsFinite(float value)
        {
            return !float.IsNaN(value) && !float.IsInfinity(value);
        }
    }
}
