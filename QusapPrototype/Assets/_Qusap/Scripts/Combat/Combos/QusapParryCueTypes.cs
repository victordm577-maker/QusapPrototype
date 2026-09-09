using System;
using UnityEngine;

namespace Qusap
{
    public readonly struct QusapParryCueInfo
    {
        internal QusapParryCueInfo(
            QusapCombatController attacker,
            QusapCombatController defender,
            QusapComboId comboId,
            double windowOpensAt,
            double windowClosesAt,
            double sampledAt)
        {
            Attacker = attacker;
            Defender = defender;
            ComboId = comboId;
            WindowOpensAt = windowOpensAt;
            WindowClosesAt = windowClosesAt;
            SampledAt = sampledAt;

            double duration = windowClosesAt - windowOpensAt;
            TimeRemaining = Math.Max(0d, windowClosesAt - sampledAt);
            WindowProgressNormalized = duration > 0d
                ? Math.Max(0d, Math.Min(1d, (sampledAt - windowOpensAt) / duration))
                : 1d;
        }

        public QusapCombatController Attacker { get; }
        public QusapCombatController Defender { get; }
        public QusapComboId ComboId { get; }
        public double WindowOpensAt { get; }
        public double WindowClosesAt { get; }
        public double SampledAt { get; }
        public double TimeRemaining { get; }
        public double WindowProgressNormalized { get; }
    }

    [Serializable]
    public sealed class QusapParryCueVisualSettings
    {
        public static readonly Vector3 DefaultLocalOffset = new(0f, 1.65f, -0.25f);
        public static readonly Color DefaultWindowColor = new(0.55f, 0.95f, 1f, 1f);
        public static readonly Color DefaultSuccessColor = new(0.2f, 1f, 0.35f, 1f);

        public const float DefaultMinimumSize = 0.28f;
        public const float DefaultMaximumSize = 0.38f;
        public const float DefaultPulseFrequency = 4f;
        public const float DefaultSuccessFlashDuration = 0.15f;
        public const int DefaultSortingOrder = 1000;

        [SerializeField] private bool indicatorEnabled = true;
        [SerializeField] private Vector3 localOffset = new(0f, 1.65f, -0.25f);
        [SerializeField] private float minimumSize = DefaultMinimumSize;
        [SerializeField] private float maximumSize = DefaultMaximumSize;
        [SerializeField] private float pulseFrequency = DefaultPulseFrequency;
        [SerializeField] private Color windowColor = new(0.55f, 0.95f, 1f, 1f);
        [SerializeField] private Color successColor = new(0.2f, 1f, 0.35f, 1f);
        [SerializeField] private float successFlashDuration = DefaultSuccessFlashDuration;
        [SerializeField] private int sortingOrder = DefaultSortingOrder;

        public QusapParryCueVisualSettings()
        {
        }

        public QusapParryCueVisualSettings(
            bool indicatorEnabled,
            Vector3 localOffset,
            float minimumSize,
            float maximumSize,
            float pulseFrequency,
            Color windowColor,
            Color successColor,
            float successFlashDuration,
            int sortingOrder)
        {
            this.indicatorEnabled = indicatorEnabled;
            this.localOffset = localOffset;
            this.minimumSize = minimumSize;
            this.maximumSize = maximumSize;
            this.pulseFrequency = pulseFrequency;
            this.windowColor = windowColor;
            this.successColor = successColor;
            this.successFlashDuration = successFlashDuration;
            this.sortingOrder = sortingOrder;
            ValidateSerializedValues();
        }

        public bool IndicatorEnabled => indicatorEnabled;
        public Vector3 LocalOffset => localOffset;
        public float MinimumSize => minimumSize;
        public float MaximumSize => maximumSize;
        public float PulseFrequency => pulseFrequency;
        public Color WindowColor => windowColor;
        public Color SuccessColor => successColor;
        public float SuccessFlashDuration => successFlashDuration;
        public int SortingOrder => sortingOrder;

        public static QusapParryCueVisualSettings CreateDefault()
        {
            return new QusapParryCueVisualSettings();
        }

        internal void ValidateSerializedValues()
        {
            if (!IsFinite(localOffset))
            {
                localOffset = DefaultLocalOffset;
            }

            minimumSize = IsFinitePositive(minimumSize)
                ? minimumSize
                : DefaultMinimumSize;
            maximumSize = IsFinitePositive(maximumSize)
                ? maximumSize
                : DefaultMaximumSize;
            if (maximumSize < minimumSize)
            {
                maximumSize = minimumSize;
            }

            pulseFrequency = IsFiniteNonNegative(pulseFrequency)
                ? pulseFrequency
                : DefaultPulseFrequency;
            successFlashDuration = IsFiniteNonNegative(successFlashDuration)
                ? successFlashDuration
                : DefaultSuccessFlashDuration;
            windowColor = IsFinite(windowColor) ? windowColor : DefaultWindowColor;
            successColor = IsFinite(successColor) ? successColor : DefaultSuccessColor;
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

        private static bool IsFinitePositive(float value)
        {
            return IsFinite(value) && value > 0f;
        }

        private static bool IsFiniteNonNegative(float value)
        {
            return IsFinite(value) && value >= 0f;
        }

        private static bool IsFinite(float value)
        {
            return !float.IsNaN(value) && !float.IsInfinity(value);
        }
    }
}
