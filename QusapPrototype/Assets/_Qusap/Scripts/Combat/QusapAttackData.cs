using System;
using UnityEngine;

namespace Qusap
{
    public enum QusapAttackType
    {
        WeakKick,
        StrongKick,
        Headbutt
    }

    public enum QusapAttackPhase
    {
        Idle,
        Startup,
        Active,
        Recovery
    }

    public enum QusapAttackVariant
    {
        None,
        WeakKickGround,
        WeakKickAir,
        StrongKickGround,
        StrongKickAir,
        HeadbuttGround,
        DiveHeadbuttAir
    }

    public interface IQusapAttackDefinition
    {
        QusapAttackType AttackType { get; }
        float StartupTime { get; }
        float ActiveDuration { get; }
        float RecoveryTime { get; }
        Vector2 HitboxSize { get; }
        Vector2 HitboxOffset { get; }
        float HitboxDepth { get; }
        float Damage { get; }
        float HorizontalKnockback { get; }
        float VerticalKnockback { get; }
        float HitstunDuration { get; }
        bool LockHorizontalMovement { get; }
    }

    [Serializable]
    public sealed class QusapAttackData : IQusapAttackDefinition
    {
        [SerializeField] private QusapAttackType attackType;
        [SerializeField] private float startupTime = 0.08f;
        [SerializeField] private float activeDuration = 0.08f;
        [SerializeField] private float recoveryTime = 0.14f;
        [SerializeField] private Vector2 hitboxSize = new(1f, 0.6f);
        [SerializeField] private Vector2 hitboxOffset = new(0.75f, -0.35f);
        [SerializeField] private float hitboxDepth = 1f;
        [SerializeField] private float damage;
        [SerializeField] private float horizontalKnockback = 4f;
        [SerializeField] private float verticalKnockback = 1f;
        [SerializeField] private float hitstunDuration = 0.12f;
        [SerializeField] private bool lockHorizontalMovement;

        public QusapAttackType AttackType => attackType;
        public float StartupTime => startupTime;
        public float ActiveDuration => activeDuration;
        public float RecoveryTime => recoveryTime;
        public Vector2 HitboxSize => hitboxSize;
        public Vector2 HitboxOffset => hitboxOffset;
        public float HitboxDepth => hitboxDepth;
        public float Damage => damage;
        public float HorizontalKnockback => horizontalKnockback;
        public float VerticalKnockback => verticalKnockback;
        public float HitstunDuration => hitstunDuration;
        public bool LockHorizontalMovement => lockHorizontalMovement;

        public static QusapAttackData CreateWeakKick()
        {
            return new QusapAttackData
            {
                attackType = QusapAttackType.WeakKick,
                startupTime = 0.08f,
                activeDuration = 0.08f,
                recoveryTime = 0.14f,
                hitboxSize = new Vector2(1f, 0.6f),
                hitboxOffset = new Vector2(0.75f, -0.35f),
                hitboxDepth = 1f,
                damage = 0f,
                horizontalKnockback = 4f,
                verticalKnockback = 1f,
                hitstunDuration = 0.12f,
                lockHorizontalMovement = false
            };
        }

        public static QusapAttackData CreateStrongKick()
        {
            return new QusapAttackData
            {
                attackType = QusapAttackType.StrongKick,
                startupTime = 0.18f,
                activeDuration = 0.1f,
                recoveryTime = 0.32f,
                hitboxSize = new Vector2(1.35f, 0.75f),
                hitboxOffset = new Vector2(0.9f, -0.25f),
                hitboxDepth = 1f,
                damage = 0f,
                horizontalKnockback = 7f,
                verticalKnockback = 3f,
                hitstunDuration = 0.24f,
                lockHorizontalMovement = true
            };
        }

        public static QusapAttackData CreateHeadbutt()
        {
            return new QusapAttackData
            {
                attackType = QusapAttackType.Headbutt,
                startupTime = 0.14f,
                activeDuration = 0.1f,
                recoveryTime = 0.28f,
                hitboxSize = new Vector2(1.2f, 0.8f),
                hitboxOffset = new Vector2(0.8f, 0.45f),
                hitboxDepth = 1f,
                damage = 0f,
                horizontalKnockback = 9f,
                verticalKnockback = 4f,
                hitstunDuration = 0.4f,
                lockHorizontalMovement = true
            };
        }

        public static QusapAttackData CreateComboBodyAttackGround()
        {
            return new QusapAttackData
            {
                attackType = QusapAttackType.WeakKick,
                startupTime = 0.08f,
                activeDuration = 0.08f,
                recoveryTime = 0.14f,
                hitboxSize = new Vector2(1f, 0.6f),
                hitboxOffset = new Vector2(0.75f, -0.35f),
                hitboxDepth = 1f,
                damage = 1f,
                horizontalKnockback = 1.75f,
                verticalKnockback = 0.35f,
                hitstunDuration = 0.18f,
                lockHorizontalMovement = false
            };
        }

        public static QusapAttackData CreateComboWeaponLightGround()
        {
            return new QusapAttackData
            {
                attackType = QusapAttackType.StrongKick,
                startupTime = 0.10f,
                activeDuration = 0.08f,
                recoveryTime = 0.16f,
                hitboxSize = new Vector2(1.15f, 0.65f),
                hitboxOffset = new Vector2(0.80f, -0.25f),
                hitboxDepth = 1f,
                damage = 1f,
                horizontalKnockback = 1.25f,
                verticalKnockback = 0.25f,
                hitstunDuration = 0.20f,
                lockHorizontalMovement = false
            };
        }

        internal void SetAttackType(QusapAttackType value)
        {
            attackType = value;
        }

        internal void Validate()
        {
            startupTime = Mathf.Max(startupTime, 0f);
            activeDuration = Mathf.Max(activeDuration, 0.0001f);
            recoveryTime = Mathf.Max(recoveryTime, 0f);
            hitboxSize.x = Mathf.Max(hitboxSize.x, 0.01f);
            hitboxSize.y = Mathf.Max(hitboxSize.y, 0.01f);
            hitboxDepth = Mathf.Max(hitboxDepth, 0.01f);
            damage = float.IsNaN(damage) || float.IsInfinity(damage)
                ? 0f
                : Mathf.Max(damage, 0f);
            horizontalKnockback = Mathf.Max(horizontalKnockback, 0f);
            verticalKnockback = Mathf.Max(verticalKnockback, 0f);
            hitstunDuration = Mathf.Max(hitstunDuration, 0f);
        }
    }

    [Serializable]
    public sealed class QusapAirAttackData : IQusapAttackDefinition
    {
        [SerializeField] private QusapAttackType attackType;
        [SerializeField] private float startupTime;
        [SerializeField] private float activeDuration;
        [SerializeField] private float recoveryTime;
        [SerializeField] private float landingRecoveryTime;
        [SerializeField] private float damage;
        [SerializeField] private Vector2 hitboxSize;
        [SerializeField] private Vector2 hitboxOffset;
        [SerializeField] private float hitboxDepth = 1f;
        [SerializeField] private float horizontalKnockback;
        [SerializeField] private float verticalKnockback;
        [SerializeField] private float hitstunDuration;
        [SerializeField] private bool lockHorizontalMovement;

        [Header("Air behavior")]
        [SerializeField, Range(0f, 1f)] private float horizontalVelocityRetention = 1f;
        [SerializeField] private bool endActiveWindowOnLanding = true;

        [Header("Dive behavior (DiveHeadbuttAir only)")]
        [SerializeField] private float diveBrakeDuration;
        [SerializeField, Range(0f, 1f)] private float diveVerticalBrakeMultiplier = 1f;
        [SerializeField] private float diveDownwardSpeed;
        [SerializeField, Range(0f, 1f)] private float diveHorizontalControlMultiplier = 1f;
        [SerializeField] private float diveBounceSpeed;
        [SerializeField] private bool blockDash;

        public QusapAttackType AttackType => attackType;
        public float StartupTime => startupTime;
        public float ActiveDuration => activeDuration;
        public float RecoveryTime => recoveryTime;
        public float LandingRecoveryTime => landingRecoveryTime;
        public float Damage => damage;
        public Vector2 HitboxSize => hitboxSize;
        public Vector2 HitboxOffset => hitboxOffset;
        public float HitboxDepth => hitboxDepth;
        public float HorizontalKnockback => horizontalKnockback;
        public float VerticalKnockback => verticalKnockback;
        public float HitstunDuration => hitstunDuration;
        public bool LockHorizontalMovement => lockHorizontalMovement;
        public float HorizontalVelocityRetention => horizontalVelocityRetention;
        public bool EndActiveWindowOnLanding => endActiveWindowOnLanding;
        public float DiveBrakeDuration => diveBrakeDuration;
        public float DiveVerticalBrakeMultiplier => diveVerticalBrakeMultiplier;
        public float DiveDownwardSpeed => diveDownwardSpeed;
        public float DiveHorizontalControlMultiplier => diveHorizontalControlMultiplier;
        public float DiveBounceSpeed => diveBounceSpeed;
        public bool BlockDash => blockDash;

        public static QusapAirAttackData CreateWeakKickAir()
        {
            return new QusapAirAttackData
            {
                attackType = QusapAttackType.WeakKick,
                startupTime = 0.07f,
                activeDuration = 0.07f,
                recoveryTime = 0.12f,
                landingRecoveryTime = 0.08f,
                damage = 4f,
                hitboxSize = new Vector2(1f, 0.55f),
                hitboxOffset = new Vector2(0.75f, 0f),
                hitboxDepth = 1f,
                horizontalKnockback = 3.5f,
                verticalKnockback = 0.75f,
                hitstunDuration = 0.1f,
                lockHorizontalMovement = false,
                horizontalVelocityRetention = 1f,
                endActiveWindowOnLanding = true
            };
        }

        public static QusapAirAttackData CreateStrongKickAir()
        {
            return new QusapAirAttackData
            {
                attackType = QusapAttackType.StrongKick,
                startupTime = 0.16f,
                activeDuration = 0.1f,
                recoveryTime = 0.3f,
                landingRecoveryTime = 0.2f,
                damage = 9f,
                hitboxSize = new Vector2(1.35f, 0.75f),
                hitboxOffset = new Vector2(0.9f, -0.45f),
                hitboxDepth = 1f,
                horizontalKnockback = 7.5f,
                verticalKnockback = -2.25f,
                hitstunDuration = 0.24f,
                lockHorizontalMovement = false,
                horizontalVelocityRetention = 0.92f,
                endActiveWindowOnLanding = true
            };
        }

        public static QusapAirAttackData CreateDiveHeadbuttAir()
        {
            return new QusapAirAttackData
            {
                attackType = QusapAttackType.Headbutt,
                startupTime = 0.12f,
                activeDuration = 0.65f,
                recoveryTime = 0.14f,
                landingRecoveryTime = 0.42f,
                damage = 12f,
                hitboxSize = new Vector2(0.9f, 0.95f),
                hitboxOffset = new Vector2(0f, -0.85f),
                hitboxDepth = 1f,
                horizontalKnockback = 2f,
                verticalKnockback = -9f,
                hitstunDuration = 0.35f,
                lockHorizontalMovement = false,
                horizontalVelocityRetention = 1f,
                endActiveWindowOnLanding = true,
                diveBrakeDuration = 0.06f,
                diveVerticalBrakeMultiplier = 0.25f,
                diveDownwardSpeed = 13f,
                diveHorizontalControlMultiplier = 0.25f,
                diveBounceSpeed = 5.5f,
                blockDash = true
            };
        }

        public static QusapAirAttackData CreateComboBodyAttackAir()
        {
            return new QusapAirAttackData
            {
                attackType = QusapAttackType.WeakKick,
                startupTime = 0.07f,
                activeDuration = 0.07f,
                recoveryTime = 0.12f,
                landingRecoveryTime = 0.08f,
                damage = 1f,
                hitboxSize = new Vector2(1f, 0.55f),
                hitboxOffset = new Vector2(0.75f, 0f),
                hitboxDepth = 1f,
                horizontalKnockback = 1.50f,
                verticalKnockback = 0.50f,
                hitstunDuration = 0.16f,
                lockHorizontalMovement = false,
                horizontalVelocityRetention = 1f,
                endActiveWindowOnLanding = true
            };
        }

        public static QusapAirAttackData CreateComboWeaponLightAir()
        {
            return new QusapAirAttackData
            {
                attackType = QusapAttackType.StrongKick,
                startupTime = 0.10f,
                activeDuration = 0.08f,
                recoveryTime = 0.16f,
                landingRecoveryTime = 0.12f,
                damage = 1f,
                hitboxSize = new Vector2(1.15f, 0.65f),
                hitboxOffset = new Vector2(0.80f, -0.30f),
                hitboxDepth = 1f,
                horizontalKnockback = 1.25f,
                verticalKnockback = 0.25f,
                hitstunDuration = 0.20f,
                lockHorizontalMovement = false,
                horizontalVelocityRetention = 0.98f,
                endActiveWindowOnLanding = true
            };
        }

        internal void SetAttackType(QusapAttackType value)
        {
            attackType = value;
        }

        internal void Validate()
        {
            startupTime = Mathf.Max(startupTime, 0f);
            activeDuration = Mathf.Max(activeDuration, 0.0001f);
            recoveryTime = Mathf.Max(recoveryTime, 0f);
            landingRecoveryTime = Mathf.Max(landingRecoveryTime, 0f);
            damage = Mathf.Max(damage, 0f);
            hitboxSize.x = Mathf.Max(hitboxSize.x, 0.01f);
            hitboxSize.y = Mathf.Max(hitboxSize.y, 0.01f);
            hitboxDepth = Mathf.Max(hitboxDepth, 0.01f);
            horizontalKnockback = Mathf.Max(horizontalKnockback, 0f);
            hitstunDuration = Mathf.Max(hitstunDuration, 0f);
            horizontalVelocityRetention = Mathf.Clamp01(horizontalVelocityRetention);
            diveBrakeDuration = Mathf.Max(diveBrakeDuration, 0f);
            diveVerticalBrakeMultiplier = Mathf.Clamp01(diveVerticalBrakeMultiplier);
            diveDownwardSpeed = Mathf.Max(diveDownwardSpeed, 0f);
            diveHorizontalControlMultiplier = Mathf.Clamp01(diveHorizontalControlMultiplier);
            diveBounceSpeed = Mathf.Max(diveBounceSpeed, 0f);
        }
    }

    public readonly struct QusapHitInfo
    {
        public QusapHitInfo(
            QusapCombatController source,
            QusapAttackType attackType,
            int horizontalDirection,
            float horizontalKnockback,
            float verticalKnockback,
            float hitstunDuration,
            Vector3 hitboxCenter)
            : this(source, attackType, QusapAttackVariant.None, 0f, horizontalDirection,
                horizontalKnockback, verticalKnockback, hitstunDuration, hitboxCenter)
        {
        }

        public QusapHitInfo(
            QusapCombatController source,
            QusapAttackType attackType,
            QusapAttackVariant attackVariant,
            float damage,
            int horizontalDirection,
            float horizontalKnockback,
            float verticalKnockback,
            float hitstunDuration,
            Vector3 hitboxCenter)
        {
            Source = source;
            AttackType = attackType;
            AttackVariant = attackVariant;
            Damage = damage;
            HorizontalDirection = horizontalDirection;
            HorizontalKnockback = horizontalKnockback;
            VerticalKnockback = verticalKnockback;
            HitstunDuration = hitstunDuration;
            HitboxCenter = hitboxCenter;
        }

        public QusapCombatController Source { get; }
        public QusapAttackType AttackType { get; }
        public QusapAttackVariant AttackVariant { get; }
        public float Damage { get; }
        public int HorizontalDirection { get; }
        public float HorizontalKnockback { get; }
        public float VerticalKnockback { get; }
        public float HitstunDuration { get; }
        public Vector3 HitboxCenter { get; }
    }
}
