using System;
using UnityEngine;

namespace Qusap
{
    [Serializable]
    public sealed class QusapThrownWeaponAttackProfile
    {
        public const float DefaultDamage = 6f;
        public const float DefaultHitstunDuration = 0.20f;
        public const float DefaultHorizontalKnockback = 6f;
        public const float DefaultVerticalKnockback = 1.5f;
        public const float DefaultDetectionRadius = 0.35f;
        public const float DefaultOffensiveDuration = 0.60f;

        public const float MaximumDamage = 12f;
        public const float MaximumHitstunDuration = 0.40f;
        public const float MaximumKnockback = 9f;
        public const float MaximumDetectionRadius = 2f;
        public const float MaximumOffensiveDuration = 2f;

        [SerializeField] private float damage = DefaultDamage;
        [SerializeField] private float hitstunDuration = DefaultHitstunDuration;
        [SerializeField] private float horizontalKnockback = DefaultHorizontalKnockback;
        [SerializeField] private float verticalKnockback = DefaultVerticalKnockback;
        [SerializeField] private float detectionRadius = DefaultDetectionRadius;
        [SerializeField] private float offensiveDuration = DefaultOffensiveDuration;

        public QusapThrownWeaponAttackProfile()
        {
        }

        public QusapThrownWeaponAttackProfile(
            float damage,
            float hitstunDuration,
            float horizontalKnockback,
            float verticalKnockback,
            float detectionRadius,
            float offensiveDuration)
        {
            this.damage = damage;
            this.hitstunDuration = hitstunDuration;
            this.horizontalKnockback = horizontalKnockback;
            this.verticalKnockback = verticalKnockback;
            this.detectionRadius = detectionRadius;
            this.offensiveDuration = offensiveDuration;
            Normalize();
        }

        public float Damage => damage;
        public float HitstunDuration => hitstunDuration;
        public float HorizontalKnockback => horizontalKnockback;
        public float VerticalKnockback => verticalKnockback;
        public float DetectionRadius => detectionRadius;
        public float OffensiveDuration => offensiveDuration;

        public void Normalize()
        {
            damage = NormalizeValue(damage, DefaultDamage, MaximumDamage);
            hitstunDuration = NormalizeValue(
                hitstunDuration,
                DefaultHitstunDuration,
                MaximumHitstunDuration);
            horizontalKnockback = NormalizeValue(
                horizontalKnockback,
                DefaultHorizontalKnockback,
                MaximumKnockback);
            verticalKnockback = NormalizeValue(
                verticalKnockback,
                DefaultVerticalKnockback,
                MaximumKnockback);
            detectionRadius = NormalizeValue(
                detectionRadius,
                DefaultDetectionRadius,
                MaximumDetectionRadius);
            offensiveDuration = NormalizeValue(
                offensiveDuration,
                DefaultOffensiveDuration,
                MaximumOffensiveDuration);
        }

        private static float NormalizeValue(float value, float fallback, float maximum)
        {
            return !float.IsFinite(value) || value < 0f
                ? fallback
                : Mathf.Min(value, maximum);
        }
    }

    public readonly struct QusapThrownWeaponTargetCandidate
    {
        public QusapThrownWeaponTargetCandidate(
            ulong targetEntityId,
            float distanceAlongTrajectory)
        {
            if (targetEntityId == 0)
            {
                throw new ArgumentOutOfRangeException(nameof(targetEntityId));
            }

            if (!float.IsFinite(distanceAlongTrajectory)
                || distanceAlongTrajectory < 0f)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(distanceAlongTrajectory));
            }

            TargetEntityId = targetEntityId;
            DistanceAlongTrajectory = distanceAlongTrajectory;
        }

        public ulong TargetEntityId { get; }
        public float DistanceAlongTrajectory { get; }

        public static bool IsPreferred(
            QusapThrownWeaponTargetCandidate candidate,
            QusapThrownWeaponTargetCandidate current)
        {
            int distanceComparison = candidate.DistanceAlongTrajectory.CompareTo(
                current.DistanceAlongTrajectory);
            return distanceComparison != 0
                ? distanceComparison < 0
                : candidate.TargetEntityId < current.TargetEntityId;
        }
    }

    public sealed class QusapThrownWeaponAttackState
    {
        private bool offensive;
        private bool impactConsumed;
        private bool settled;
        private bool hasAcceptedTimestamp;
        private double lastAcceptedTimestamp;

        public QusapThrownWeaponAttackState(
            ulong throwId,
            ulong throwerEntityId,
            ulong weaponInstanceId,
            QusapWeaponReleaseType releaseType,
            QusapWeaponThrowDirection direction,
            int capturedFacingDirection,
            double startedAt,
            double offensiveDuration)
        {
            if (weaponInstanceId == 0)
            {
                throw new ArgumentOutOfRangeException(nameof(weaponInstanceId));
            }

            if (!IsValidReleaseType(releaseType))
            {
                throw new ArgumentOutOfRangeException(nameof(releaseType));
            }

            if (direction != QusapWeaponThrowDirection.Forward
                && direction != QusapWeaponThrowDirection.Up)
            {
                throw new ArgumentOutOfRangeException(nameof(direction));
            }

            bool voluntary = releaseType == QusapWeaponReleaseType.VoluntarySwapThrow;
            double offensiveUntil = startedAt + offensiveDuration;
            if ((voluntary && (throwId == 0 || throwerEntityId == 0))
                || !IsFinite(startedAt)
                || !IsFinite(offensiveDuration)
                || !IsFinite(offensiveUntil)
                || offensiveDuration < 0d)
            {
                throw new ArgumentOutOfRangeException(nameof(startedAt));
            }

            ThrowId = throwId;
            ThrowerEntityId = throwerEntityId;
            WeaponInstanceId = weaponInstanceId;
            ReleaseType = releaseType;
            Direction = direction;
            CapturedFacingDirection = capturedFacingDirection < 0 ? -1 : 1;
            StartedAt = startedAt;
            OffensiveUntil = offensiveUntil;
            offensive = voluntary && offensiveDuration > 0d;
            hasAcceptedTimestamp = true;
            lastAcceptedTimestamp = startedAt;
        }

        public ulong ThrowId { get; }
        public ulong ThrowerEntityId { get; }
        public ulong WeaponInstanceId { get; }
        public QusapWeaponReleaseType ReleaseType { get; }
        public QusapWeaponThrowDirection Direction { get; }
        public int CapturedFacingDirection { get; }
        public double StartedAt { get; }
        public double OffensiveUntil { get; }
        public bool IsOffensive => offensive && !impactConsumed && !settled;
        public bool ImpactConsumed => impactConsumed;
        public bool IsSettled => settled;
        public double LastAcceptedTimestamp => lastAcceptedTimestamp;

        public bool TryAdvance(double timestamp)
        {
            if (!IsFinite(timestamp)
                || (hasAcceptedTimestamp && timestamp < lastAcceptedTimestamp))
            {
                return false;
            }

            hasAcceptedTimestamp = true;
            lastAcceptedTimestamp = timestamp;
            if (timestamp > OffensiveUntil)
            {
                offensive = false;
            }

            return IsOffensive;
        }

        public bool TryConsumeImpact(ulong targetEntityId, double timestamp)
        {
            if (targetEntityId == 0
                || targetEntityId == ThrowerEntityId
                || !TryAdvance(timestamp)
                || !IsOffensive)
            {
                return false;
            }

            impactConsumed = true;
            offensive = false;
            return true;
        }

        public bool MarkSettled(double timestamp)
        {
            settled = true;
            offensive = false;
            if (!IsFinite(timestamp)
                || (hasAcceptedTimestamp && timestamp < lastAcceptedTimestamp))
            {
                return false;
            }

            hasAcceptedTimestamp = true;
            lastAcceptedTimestamp = timestamp;
            return true;
        }

        private static bool IsValidReleaseType(QusapWeaponReleaseType releaseType)
        {
            return releaseType == QusapWeaponReleaseType.Disarmed
                || releaseType == QusapWeaponReleaseType.VoluntarySwapThrow
                || releaseType == QusapWeaponReleaseType.InitialSpawn;
        }

        private static bool IsFinite(double value)
        {
            return !double.IsNaN(value) && !double.IsInfinity(value);
        }
    }

    public sealed class QusapWeaponThrowIdGenerator
    {
        private ulong lastIssuedId;

        public ulong Next()
        {
            if (lastIssuedId == ulong.MaxValue)
            {
                throw new OverflowException("No more weapon throw identifiers are available.");
            }

            return ++lastIssuedId;
        }
    }
}
