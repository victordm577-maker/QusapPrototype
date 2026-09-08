using System;
using System.Collections.Generic;
using UnityEngine;

namespace Qusap
{
    [Serializable]
    public sealed class QusapComboFinisherDefinition
    {
        public const float DefaultDamageDamage = 15f;
        public const float DefaultDamageHorizontalKnockback = 3f;
        public const float DefaultDamageVerticalKnockback = 1f;
        public const float DefaultDamageHitstun = 0.35f;

        public const float DefaultDisarmDamage = 2f;
        public const float DefaultDisarmHorizontalKnockback = 2f;
        public const float DefaultDisarmVerticalKnockback = 0.5f;
        public const float DefaultDisarmHitstun = 0.30f;

        public const float DefaultLaunchDamage = 3f;
        public const float DefaultLaunchHorizontalKnockback = 4f;
        public const float DefaultLaunchVerticalKnockback = 10f;
        public const float DefaultLaunchHitstun = 0.55f;

        [SerializeField] private QusapComboId comboId;
        [SerializeField] private string displayName;
        [SerializeField] private float damage;
        [SerializeField] private float horizontalKnockback;
        [SerializeField] private float verticalKnockback;
        [SerializeField] private float hitstunDuration;
        [SerializeField] private Vector2 hitboxSize;
        [SerializeField] private Vector2 hitboxOffset;
        [SerializeField] private float hitboxDepth;
        [SerializeField] private bool requestsDisarm;

        public QusapComboFinisherDefinition(
            QusapComboId comboId,
            string displayName,
            float damage,
            float horizontalKnockback,
            float verticalKnockback,
            float hitstunDuration,
            Vector2 hitboxSize,
            Vector2 hitboxOffset,
            float hitboxDepth,
            bool requestsDisarm)
        {
            ValidateComboId(comboId);
            ValidateNonNegativeFinite(damage, nameof(damage));
            ValidateNonNegativeFinite(horizontalKnockback, nameof(horizontalKnockback));
            ValidateNonNegativeFinite(verticalKnockback, nameof(verticalKnockback));
            ValidateNonNegativeFinite(hitstunDuration, nameof(hitstunDuration));
            ValidatePositiveSize(hitboxSize, nameof(hitboxSize));
            ValidateFiniteVector(hitboxOffset, nameof(hitboxOffset));
            ValidatePositiveFinite(hitboxDepth, nameof(hitboxDepth));
            ValidateDisarmContract(comboId, requestsDisarm);

            this.comboId = comboId;
            this.displayName = NormalizeDisplayName(displayName, comboId);
            this.damage = damage;
            this.horizontalKnockback = horizontalKnockback;
            this.verticalKnockback = verticalKnockback;
            this.hitstunDuration = hitstunDuration;
            this.hitboxSize = hitboxSize;
            this.hitboxOffset = hitboxOffset;
            this.hitboxDepth = hitboxDepth;
            this.requestsDisarm = requestsDisarm;
        }

        public QusapComboId ComboId => comboId;
        public string DisplayName => displayName;
        public float Damage => damage;
        public float HorizontalKnockback => horizontalKnockback;
        public float VerticalKnockback => verticalKnockback;
        public float HitstunDuration => hitstunDuration;
        public Vector2 HitboxSize => hitboxSize;
        public Vector2 HitboxOffset => hitboxOffset;
        public float HitboxDepth => hitboxDepth;
        public bool RequestsDisarm => requestsDisarm;

        public static QusapComboFinisherDefinition CreateDefaultDamage()
        {
            return new QusapComboFinisherDefinition(
                QusapComboId.Damage,
                "Damage",
                DefaultDamageDamage,
                DefaultDamageHorizontalKnockback,
                DefaultDamageVerticalKnockback,
                DefaultDamageHitstun,
                new Vector2(1.40f, 1.00f),
                new Vector2(0.90f, 0.00f),
                1.00f,
                false);
        }

        public static QusapComboFinisherDefinition CreateDefaultDisarm()
        {
            return new QusapComboFinisherDefinition(
                QusapComboId.Disarm,
                "Disarm",
                DefaultDisarmDamage,
                DefaultDisarmHorizontalKnockback,
                DefaultDisarmVerticalKnockback,
                DefaultDisarmHitstun,
                new Vector2(1.25f, 0.90f),
                new Vector2(0.80f, 0.20f),
                1.00f,
                true);
        }

        public static QusapComboFinisherDefinition CreateDefaultLaunch()
        {
            return new QusapComboFinisherDefinition(
                QusapComboId.Launch,
                "Launch",
                DefaultLaunchDamage,
                DefaultLaunchHorizontalKnockback,
                DefaultLaunchVerticalKnockback,
                DefaultLaunchHitstun,
                new Vector2(1.30f, 0.90f),
                new Vector2(0.80f, 0.00f),
                1.00f,
                false);
        }

        public static QusapComboFinisherDefinition[] CreateDefaultDefinitions()
        {
            return new[]
            {
                CreateDefaultDamage(),
                CreateDefaultDisarm(),
                CreateDefaultLaunch()
            };
        }

        public static IReadOnlyList<QusapComboFinisherDefinition> ValidateDefinitions(
            IEnumerable<QusapComboFinisherDefinition> definitions)
        {
            if (definitions == null)
            {
                return Array.AsReadOnly(CreateDefaultDefinitions());
            }

            List<QusapComboFinisherDefinition> validated = new();
            HashSet<QusapComboId> comboIds = new();
            foreach (QusapComboFinisherDefinition definition in definitions)
            {
                if (definition == null)
                {
                    throw new ArgumentException(
                        "Finisher definitions cannot contain null entries.", nameof(definitions));
                }

                definition.ValidateSerializedValues();
                if (!comboIds.Add(definition.ComboId))
                {
                    throw new ArgumentException(
                        $"Duplicate finisher definition for {definition.ComboId}.", nameof(definitions));
                }

                validated.Add(definition);
            }

            return validated.Count == 0
                ? Array.AsReadOnly(CreateDefaultDefinitions())
                : validated.AsReadOnly();
        }

        internal void ValidateSerializedValues()
        {
            if (!Enum.IsDefined(typeof(QusapComboId), comboId))
            {
                comboId = QusapComboId.Damage;
            }

            QusapComboFinisherDefinition fallback = CreateDefault(comboId);
            displayName = NormalizeDisplayName(displayName, comboId);
            damage = NormalizeNonNegative(damage, fallback.Damage);
            horizontalKnockback = NormalizeNonNegative(
                horizontalKnockback, fallback.HorizontalKnockback);
            verticalKnockback = NormalizeNonNegative(
                verticalKnockback, fallback.VerticalKnockback);
            hitstunDuration = NormalizeNonNegative(hitstunDuration, fallback.HitstunDuration);
            hitboxSize = IsPositiveFinite(hitboxSize)
                ? hitboxSize
                : fallback.HitboxSize;
            hitboxOffset = IsFinite(hitboxOffset)
                ? hitboxOffset
                : fallback.HitboxOffset;
            hitboxDepth = IsPositiveFinite(hitboxDepth)
                ? hitboxDepth
                : fallback.HitboxDepth;
            requestsDisarm = comboId == QusapComboId.Disarm;
        }

        private static QusapComboFinisherDefinition CreateDefault(QusapComboId id)
        {
            return id switch
            {
                QusapComboId.Damage => CreateDefaultDamage(),
                QusapComboId.Disarm => CreateDefaultDisarm(),
                QusapComboId.Launch => CreateDefaultLaunch(),
                _ => CreateDefaultDamage()
            };
        }

        private static void ValidateComboId(QusapComboId value)
        {
            if (!Enum.IsDefined(typeof(QusapComboId), value))
            {
                throw new ArgumentOutOfRangeException(nameof(value), value, "Unknown combo identifier.");
            }
        }

        private static void ValidateDisarmContract(QusapComboId id, bool value)
        {
            bool expected = id == QusapComboId.Disarm;
            if (value != expected)
            {
                throw new ArgumentException(
                    expected
                        ? "The Disarm finisher must request disarm."
                        : $"The {id} finisher cannot request disarm.",
                    nameof(value));
            }
        }

        private static void ValidateNonNegativeFinite(float value, string parameterName)
        {
            if (!IsFinite(value) || value < 0f)
            {
                throw new ArgumentOutOfRangeException(
                    parameterName, value, "The value must be finite and non-negative.");
            }
        }

        private static void ValidatePositiveFinite(float value, string parameterName)
        {
            if (!IsPositiveFinite(value))
            {
                throw new ArgumentOutOfRangeException(
                    parameterName, value, "The value must be finite and greater than zero.");
            }
        }

        private static void ValidatePositiveSize(Vector2 value, string parameterName)
        {
            if (!IsPositiveFinite(value))
            {
                throw new ArgumentOutOfRangeException(
                    parameterName, value, "Both dimensions must be finite and greater than zero.");
            }
        }

        private static void ValidateFiniteVector(Vector2 value, string parameterName)
        {
            if (!IsFinite(value))
            {
                throw new ArgumentOutOfRangeException(
                    parameterName, value, "Both components must be finite.");
            }
        }

        private static string NormalizeDisplayName(string value, QusapComboId id)
        {
            return string.IsNullOrWhiteSpace(value) ? id.ToString() : value.Trim();
        }

        private static float NormalizeNonNegative(float value, float fallback)
        {
            return IsFinite(value) && value >= 0f ? value : fallback;
        }

        private static bool IsPositiveFinite(Vector2 value)
        {
            return IsFinite(value) && value.x > 0f && value.y > 0f;
        }

        private static bool IsFinite(Vector2 value)
        {
            return IsFinite(value.x) && IsFinite(value.y);
        }

        private static bool IsPositiveFinite(float value)
        {
            return IsFinite(value) && value > 0f;
        }

        private static bool IsFinite(float value)
        {
            return !float.IsNaN(value) && !float.IsInfinity(value);
        }
    }
}
