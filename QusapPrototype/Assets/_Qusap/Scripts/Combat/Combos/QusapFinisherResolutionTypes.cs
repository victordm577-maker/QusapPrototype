using UnityEngine;

namespace Qusap
{
    public interface IQusapDisarmable
    {
        bool CanBeDisarmed { get; }
        bool TryDisarm(QusapCombatController source);
    }

    public enum QusapFinisherImpactOutcome
    {
        Applied,
        Whiffed,
        Rejected
    }

    public enum QusapFinisherResolutionOutcome
    {
        Applied,
        Whiffed,
        Rejected,
        DisarmSucceeded,
        DisarmUnavailable,
        DisarmRejected
    }

    public readonly struct QusapFinisherHitInfo
    {
        public QusapFinisherHitInfo(
            QusapCombatController source,
            QusapComboId comboId,
            float damage,
            int horizontalDirection,
            float horizontalKnockback,
            float verticalKnockback,
            float hitstunDuration,
            bool requestsDisarm)
        {
            Source = source;
            ComboId = comboId;
            Damage = damage;
            HorizontalDirection = horizontalDirection < 0 ? -1 : 1;
            HorizontalKnockback = horizontalKnockback;
            VerticalKnockback = verticalKnockback;
            HitstunDuration = hitstunDuration;
            RequestsDisarm = requestsDisarm;
        }

        public QusapCombatController Source { get; }
        public QusapComboId ComboId { get; }
        public float Damage { get; }
        public int HorizontalDirection { get; }
        public float HorizontalKnockback { get; }
        public float VerticalKnockback { get; }
        public float HitstunDuration { get; }
        public bool RequestsDisarm { get; }
    }

    public readonly struct QusapFinisherResolution
    {
        public QusapFinisherResolution(
            QusapComboId comboId,
            QusapHitReceiver expectedTarget,
            QusapFinisherResolutionOutcome outcome,
            bool hitApplied,
            bool disarmApplied)
        {
            ComboId = comboId;
            ExpectedTarget = expectedTarget;
            Outcome = outcome;
            HitApplied = hitApplied;
            DisarmApplied = disarmApplied;
        }

        public QusapComboId ComboId { get; }
        public QusapHitReceiver ExpectedTarget { get; }
        public QusapFinisherResolutionOutcome Outcome { get; }
        public bool HitApplied { get; }
        public bool DisarmApplied { get; }
    }
}
