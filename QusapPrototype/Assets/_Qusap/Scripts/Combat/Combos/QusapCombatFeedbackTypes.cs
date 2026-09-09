using System;
using UnityEngine;

namespace Qusap
{
    public enum QusapCombatFeedbackType
    {
        None,
        Damage,
        Launch,
        Disarm,
        Whiff,
        Rejected,
        ParryFailed,
        ParrySucceeded
    }

    public readonly struct QusapCombatFeedbackEvent
    {
        internal QusapCombatFeedbackEvent(
            QusapCombatFeedbackType feedbackType,
            QusapCombatController attacker,
            QusapHitReceiver target,
            QusapComboId? comboId,
            Vector3 worldPosition,
            double timestamp,
            QusapFinisherResolutionOutcome? finisherOutcome,
            QusapParryAttemptOutcome? parryOutcome)
        {
            FeedbackType = feedbackType;
            Attacker = attacker;
            Target = target;
            ComboId = comboId;
            WorldPosition = worldPosition;
            Timestamp = timestamp;
            FinisherOutcome = finisherOutcome;
            ParryOutcome = parryOutcome;
        }

        public QusapCombatFeedbackType FeedbackType { get; }
        public QusapCombatController Attacker { get; }
        public QusapHitReceiver Target { get; }
        public QusapComboId? ComboId { get; }
        public Vector3 WorldPosition { get; }
        public double Timestamp { get; }
        public QusapFinisherResolutionOutcome? FinisherOutcome { get; }
        public QusapParryAttemptOutcome? ParryOutcome { get; }
    }

    [Serializable]
    public sealed class QusapCombatFeedbackSettings
    {
        public const int DefaultPoolCapacity = 8;
        public const int MinimumPoolCapacity = 1;
        public const int MaximumPoolCapacity = 32;
        public const int DefaultSortingOrder = 1010;
        public const int MinimumSortingOrder = short.MinValue;
        public const int MaximumSortingOrder = short.MaxValue;

        public const float DefaultDamageDuration = 0.22f;
        public const float DefaultLaunchDuration = 0.28f;
        public const float DefaultDisarmDuration = 0.25f;
        public const float DefaultWhiffDuration = 0.16f;
        public const float DefaultRejectedDuration = 0.16f;
        public const float DefaultFailedParryDuration = 0.18f;

        public const float DefaultDamageMaximumSize = 0.70f;
        public const float DefaultLaunchMaximumHeight = 0.95f;
        public const float DefaultDisarmMaximumSize = 0.70f;
        public const float DefaultWhiffMaximumSize = 0.32f;
        public const float DefaultRejectedMaximumSize = 0.45f;
        public const float DefaultFailedParryMaximumSize = 0.55f;
        public const float DefaultExpansionSpeed = 2f;

        public static readonly Vector3 DefaultTargetOffset = new(0f, 1.10f, -0.20f);
        public static readonly Vector3 DefaultAttackerOffset = new(0f, 1.35f, -0.20f);
        public static readonly Color DefaultDamageColor = new(1f, 0.10f, 0.06f, 0.95f);
        public static readonly Color DefaultDamageCenterColor = Color.white;
        public static readonly Color DefaultLaunchColor = new(0.05f, 0.90f, 1f, 0.95f);
        public static readonly Color DefaultDisarmColor = new(0.72f, 0.20f, 1f, 0.95f);
        public static readonly Color DefaultDisarmSuccessCenterColor = new(1f, 0.72f, 0.08f, 1f);
        public static readonly Color DefaultWhiffColor = new(0.65f, 0.68f, 0.72f, 0.85f);
        public static readonly Color DefaultRejectedColor = new(0.58f, 0.30f, 0.32f, 0.90f);
        public static readonly Color DefaultFailedParryColor = new(1f, 0.08f, 0.06f, 1f);

        [SerializeField] private bool feedbackEnabled = true;
        [SerializeField] private Vector3 targetOffset = new(0f, 1.10f, -0.20f);
        [SerializeField] private Vector3 attackerOffset = new(0f, 1.35f, -0.20f);
        [SerializeField] private Color damageColor = new(1f, 0.10f, 0.06f, 0.95f);
        [SerializeField] private Color damageCenterColor = new(1f, 1f, 1f, 1f);
        [SerializeField] private Color launchColor = new(0.05f, 0.90f, 1f, 0.95f);
        [SerializeField] private Color disarmColor = new(0.72f, 0.20f, 1f, 0.95f);
        [SerializeField] private Color disarmSuccessCenterColor = new(1f, 0.72f, 0.08f, 1f);
        [SerializeField] private Color whiffColor = new(0.65f, 0.68f, 0.72f, 0.85f);
        [SerializeField] private Color rejectedColor = new(0.58f, 0.30f, 0.32f, 0.90f);
        [SerializeField] private Color failedParryColor = new(1f, 0.08f, 0.06f, 1f);

        [SerializeField] private float damageDuration = DefaultDamageDuration;
        [SerializeField] private float launchDuration = DefaultLaunchDuration;
        [SerializeField] private float disarmDuration = DefaultDisarmDuration;
        [SerializeField] private float whiffDuration = DefaultWhiffDuration;
        [SerializeField] private float rejectedDuration = DefaultRejectedDuration;
        [SerializeField] private float failedParryDuration = DefaultFailedParryDuration;

        [SerializeField] private float damageMaximumSize = DefaultDamageMaximumSize;
        [SerializeField] private float launchMaximumHeight = DefaultLaunchMaximumHeight;
        [SerializeField] private float disarmMaximumSize = DefaultDisarmMaximumSize;
        [SerializeField] private float whiffMaximumSize = DefaultWhiffMaximumSize;
        [SerializeField] private float rejectedMaximumSize = DefaultRejectedMaximumSize;
        [SerializeField] private float failedParryMaximumSize = DefaultFailedParryMaximumSize;
        [SerializeField] private float expansionSpeed = DefaultExpansionSpeed;
        [SerializeField] private int sortingOrder = DefaultSortingOrder;
        [SerializeField, Range(MinimumPoolCapacity, MaximumPoolCapacity)]
        private int poolCapacity = DefaultPoolCapacity;

        public QusapCombatFeedbackSettings()
        {
        }

        public QusapCombatFeedbackSettings(
            int poolCapacity,
            float damageDuration,
            float damageMaximumSize,
            Vector3 targetOffset)
        {
            this.poolCapacity = poolCapacity;
            this.damageDuration = damageDuration;
            this.damageMaximumSize = damageMaximumSize;
            this.targetOffset = targetOffset;
            ValidateSerializedValues();
        }

        public bool FeedbackEnabled => feedbackEnabled;
        public Vector3 TargetOffset => targetOffset;
        public Vector3 AttackerOffset => attackerOffset;
        public Color DamageColor => damageColor;
        public Color DamageCenterColor => damageCenterColor;
        public Color LaunchColor => launchColor;
        public Color DisarmColor => disarmColor;
        public Color DisarmSuccessCenterColor => disarmSuccessCenterColor;
        public Color WhiffColor => whiffColor;
        public Color RejectedColor => rejectedColor;
        public Color FailedParryColor => failedParryColor;
        public float DamageDuration => damageDuration;
        public float LaunchDuration => launchDuration;
        public float DisarmDuration => disarmDuration;
        public float WhiffDuration => whiffDuration;
        public float RejectedDuration => rejectedDuration;
        public float FailedParryDuration => failedParryDuration;
        public float DamageMaximumSize => damageMaximumSize;
        public float LaunchMaximumHeight => launchMaximumHeight;
        public float DisarmMaximumSize => disarmMaximumSize;
        public float WhiffMaximumSize => whiffMaximumSize;
        public float RejectedMaximumSize => rejectedMaximumSize;
        public float FailedParryMaximumSize => failedParryMaximumSize;
        public float ExpansionSpeed => expansionSpeed;
        public int SortingOrder => sortingOrder;
        public int PoolCapacity => poolCapacity;

        public static QusapCombatFeedbackSettings CreateDefault()
        {
            return new QusapCombatFeedbackSettings();
        }

        internal void ValidateSerializedValues()
        {
            targetOffset = IsFinite(targetOffset) ? targetOffset : DefaultTargetOffset;
            attackerOffset = IsFinite(attackerOffset) ? attackerOffset : DefaultAttackerOffset;
            damageColor = IsFinite(damageColor) ? damageColor : DefaultDamageColor;
            damageCenterColor = IsFinite(damageCenterColor)
                ? damageCenterColor
                : DefaultDamageCenterColor;
            launchColor = IsFinite(launchColor) ? launchColor : DefaultLaunchColor;
            disarmColor = IsFinite(disarmColor) ? disarmColor : DefaultDisarmColor;
            disarmSuccessCenterColor = IsFinite(disarmSuccessCenterColor)
                ? disarmSuccessCenterColor
                : DefaultDisarmSuccessCenterColor;
            whiffColor = IsFinite(whiffColor) ? whiffColor : DefaultWhiffColor;
            rejectedColor = IsFinite(rejectedColor) ? rejectedColor : DefaultRejectedColor;
            failedParryColor = IsFinite(failedParryColor)
                ? failedParryColor
                : DefaultFailedParryColor;

            damageDuration = NormalizeNonNegative(damageDuration, DefaultDamageDuration);
            launchDuration = NormalizeNonNegative(launchDuration, DefaultLaunchDuration);
            disarmDuration = NormalizeNonNegative(disarmDuration, DefaultDisarmDuration);
            whiffDuration = NormalizeNonNegative(whiffDuration, DefaultWhiffDuration);
            rejectedDuration = NormalizeNonNegative(rejectedDuration, DefaultRejectedDuration);
            failedParryDuration = NormalizeNonNegative(
                failedParryDuration, DefaultFailedParryDuration);

            damageMaximumSize = NormalizePositive(
                damageMaximumSize, DefaultDamageMaximumSize);
            launchMaximumHeight = NormalizePositive(
                launchMaximumHeight, DefaultLaunchMaximumHeight);
            disarmMaximumSize = NormalizePositive(
                disarmMaximumSize, DefaultDisarmMaximumSize);
            whiffMaximumSize = NormalizePositive(
                whiffMaximumSize, DefaultWhiffMaximumSize);
            rejectedMaximumSize = NormalizePositive(
                rejectedMaximumSize, DefaultRejectedMaximumSize);
            failedParryMaximumSize = NormalizePositive(
                failedParryMaximumSize, DefaultFailedParryMaximumSize);
            expansionSpeed = NormalizePositive(expansionSpeed, DefaultExpansionSpeed);
            sortingOrder = Mathf.Clamp(
                sortingOrder, MinimumSortingOrder, MaximumSortingOrder);
            poolCapacity = Mathf.Clamp(
                poolCapacity, MinimumPoolCapacity, MaximumPoolCapacity);
        }

        private static float NormalizePositive(float value, float fallback)
        {
            return IsFinite(value) && value > 0f ? value : fallback;
        }

        private static float NormalizeNonNegative(float value, float fallback)
        {
            return IsFinite(value) && value >= 0f ? value : fallback;
        }

        private static bool IsFinite(Vector3 value)
        {
            return IsFinite(value.x) && IsFinite(value.y) && IsFinite(value.z);
        }

        private static bool IsFinite(Color value)
        {
            return IsFinite(value.r)
                && IsFinite(value.g)
                && IsFinite(value.b)
                && IsFinite(value.a);
        }

        private static bool IsFinite(float value)
        {
            return !float.IsNaN(value) && !float.IsInfinity(value);
        }
    }
}
