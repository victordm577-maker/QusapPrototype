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

    [Serializable]
    public sealed class QusapAttackData
    {
        [SerializeField] private QusapAttackType attackType;
        [SerializeField] private float startupTime = 0.08f;
        [SerializeField] private float activeDuration = 0.08f;
        [SerializeField] private float recoveryTime = 0.14f;
        [SerializeField] private Vector2 hitboxSize = new(1f, 0.6f);
        [SerializeField] private Vector2 hitboxOffset = new(0.75f, -0.35f);
        [SerializeField] private float hitboxDepth = 1f;
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
                horizontalKnockback = 9f,
                verticalKnockback = 4f,
                hitstunDuration = 0.4f,
                lockHorizontalMovement = true
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
            horizontalKnockback = Mathf.Max(horizontalKnockback, 0f);
            verticalKnockback = Mathf.Max(verticalKnockback, 0f);
            hitstunDuration = Mathf.Max(hitstunDuration, 0f);
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
        {
            Source = source;
            AttackType = attackType;
            HorizontalDirection = horizontalDirection;
            HorizontalKnockback = horizontalKnockback;
            VerticalKnockback = verticalKnockback;
            HitstunDuration = hitstunDuration;
            HitboxCenter = hitboxCenter;
        }

        public QusapCombatController Source { get; }
        public QusapAttackType AttackType { get; }
        public int HorizontalDirection { get; }
        public float HorizontalKnockback { get; }
        public float VerticalKnockback { get; }
        public float HitstunDuration { get; }
        public Vector3 HitboxCenter { get; }
    }
}
