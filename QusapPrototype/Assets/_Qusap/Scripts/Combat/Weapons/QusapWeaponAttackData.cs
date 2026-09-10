using System;
using UnityEngine;

namespace Qusap
{
    public enum QusapWeaponAttackKind
    {
        None,
        Light,
        Strong
    }

    public static class QusapWeaponAttackRules
    {
        public static bool RequiresEquippedWeapon(QusapCombatCommand command)
        {
            return command == QusapCombatCommand.WeaponLight
                || command == QusapCombatCommand.WeaponStrong;
        }

        public static bool CanExecute(QusapCombatCommand command, bool hasEquippedWeapon)
        {
            return !RequiresEquippedWeapon(command) || hasEquippedWeapon;
        }
    }

    [Serializable]
    public sealed class QusapWeaponAttackData : IQusapAttackDefinition
    {
        public const float DefaultInputBuffer = 0.12f;
        public const float MinimumSpeedMultiplier = 0.25f;
        public const float MaximumSpeedMultiplier = 3f;

        [SerializeField] private QusapWeaponAttackKind attackKind;
        [SerializeField, Tooltip("Tiempo base antes de que se active la hitbox.")]
        private float startupTime = 0.1f;
        [SerializeField, Tooltip("Tiempo base durante el que la hitbox permanece activa.")]
        private float activeDuration = 0.08f;
        [SerializeField, Tooltip("Tiempo base de recuperacion tras el golpe.")]
        private float recoveryTime = 0.16f;
        [SerializeField, Tooltip("Ventana, durante recovery, para conservar una sola pulsacion nueva.")]
        private float inputBufferDuration = DefaultInputBuffer;
        [SerializeField, Tooltip("Dano causado por un impacto valido.")]
        private float damage = 1f;
        [SerializeField, Tooltip("Duracion del hitstun causado por el impacto.")]
        private float hitstunDuration = 0.2f;
        [SerializeField, Tooltip("Impulso horizontal aplicado al objetivo.")]
        private float horizontalKnockback = 1.25f;
        [SerializeField, Tooltip("Impulso vertical aplicado al objetivo.")]
        private float verticalKnockback = 0.25f;
        [SerializeField, Tooltip("Tamano de la hitbox explicita del ataque.")]
        private Vector2 hitboxSize = new(1.15f, 0.65f);
        [SerializeField, Tooltip("Posicion local de la hitbox respecto al jugador.")]
        private Vector2 hitboxOffset = new(0.8f, -0.25f);
        [SerializeField, Tooltip("Profundidad de la hitbox explicita.")]
        private float hitboxDepth = 1f;
        [SerializeField, Range(0f, 1f),
         Tooltip("Porcentaje de velocidad horizontal conservado al iniciar el ataque en el aire.")]
        private float airborneHorizontalVelocityRetention = 1f;
        [SerializeField, Tooltip("Bloquea movimiento horizontal mientras dura el ataque.")]
        private bool lockHorizontalMovement;
        [SerializeField, Range(MinimumSpeedMultiplier, MaximumSpeedMultiplier),
         Tooltip("Escala conjuntamente startup, active, recovery y el progreso visual; no altera dano ni impulsos.")]
        private float speedMultiplier = 1f;

        public QusapWeaponAttackData(
            QusapWeaponAttackKind attackKind,
            float startupTime,
            float activeDuration,
            float recoveryTime,
            float inputBufferDuration,
            float damage,
            float hitstunDuration,
            float horizontalKnockback,
            float verticalKnockback,
            Vector2 hitboxOffset,
            Vector2 hitboxSize,
            float hitboxDepth = 1f,
            bool lockHorizontalMovement = false,
            float speedMultiplier = 1f,
            float airborneHorizontalVelocityRetention = 1f)
        {
            this.attackKind = attackKind;
            this.startupTime = startupTime;
            this.activeDuration = activeDuration;
            this.recoveryTime = recoveryTime;
            this.inputBufferDuration = inputBufferDuration;
            this.damage = damage;
            this.hitstunDuration = hitstunDuration;
            this.horizontalKnockback = horizontalKnockback;
            this.verticalKnockback = verticalKnockback;
            this.hitboxOffset = hitboxOffset;
            this.hitboxSize = hitboxSize;
            this.hitboxDepth = hitboxDepth;
            this.lockHorizontalMovement = lockHorizontalMovement;
            this.speedMultiplier = speedMultiplier;
            this.airborneHorizontalVelocityRetention = airborneHorizontalVelocityRetention;
            Validate();
        }

        public QusapWeaponAttackKind AttackKind => attackKind;
        public QusapAttackType AttackType => QusapAttackType.StrongKick;
        public float StartupTime => startupTime / speedMultiplier;
        public float ActiveDuration => activeDuration / speedMultiplier;
        public float RecoveryTime => recoveryTime / speedMultiplier;
        public float BaseStartupTime => startupTime;
        public float BaseActiveDuration => activeDuration;
        public float BaseRecoveryTime => recoveryTime;
        public float InputBufferDuration => inputBufferDuration;
        public Vector2 HitboxSize => hitboxSize;
        public Vector2 HitboxOffset => hitboxOffset;
        public float HitboxDepth => hitboxDepth;
        public float Damage => damage;
        public float HorizontalKnockback => horizontalKnockback;
        public float VerticalKnockback => verticalKnockback;
        public float HitstunDuration => hitstunDuration;
        public bool LockHorizontalMovement => lockHorizontalMovement;
        public float SpeedMultiplier => speedMultiplier;
        public float AirborneHorizontalVelocityRetention => airborneHorizontalVelocityRetention;

        public static QusapWeaponAttackData CreateLight()
        {
            return new QusapWeaponAttackData(
                QusapWeaponAttackKind.Light,
                0.10f, 0.08f, 0.16f, DefaultInputBuffer,
                1f, 0.20f, 1.25f, 0.25f,
                new Vector2(0.80f, -0.25f), new Vector2(1.15f, 0.65f),
                1f, false, 1f, 0.98f);
        }

        public static QusapWeaponAttackData CreateStrong()
        {
            return new QusapWeaponAttackData(
                QusapWeaponAttackKind.Strong,
                0.18f, 0.10f, 0.32f, DefaultInputBuffer,
                1f, 0.24f, 7f, 3f,
                new Vector2(0.90f, -0.25f), new Vector2(1.35f, 0.75f),
                1f, true, 1f, 0.92f);
        }

        public void ConfigureSpeedMultiplier(float value)
        {
            speedMultiplier = NormalizeSpeed(value);
        }

        public void Validate()
        {
            attackKind = attackKind == QusapWeaponAttackKind.Strong
                ? QusapWeaponAttackKind.Strong
                : QusapWeaponAttackKind.Light;
            startupTime = NormalizeNonNegative(startupTime);
            activeDuration = Mathf.Max(NormalizeNonNegative(activeDuration), 0.0001f);
            recoveryTime = NormalizeNonNegative(recoveryTime);
            inputBufferDuration = NormalizeNonNegative(inputBufferDuration);
            damage = NormalizeNonNegative(damage);
            hitstunDuration = NormalizeNonNegative(hitstunDuration);
            horizontalKnockback = NormalizeNonNegative(horizontalKnockback);
            verticalKnockback = IsFinite(verticalKnockback) ? verticalKnockback : 0f;
            hitboxOffset = NormalizeFinite(hitboxOffset, Vector2.zero);
            hitboxSize = NormalizeFinite(hitboxSize, Vector2.one);
            hitboxSize.x = Mathf.Max(hitboxSize.x, 0.01f);
            hitboxSize.y = Mathf.Max(hitboxSize.y, 0.01f);
            hitboxDepth = Mathf.Max(NormalizeNonNegative(hitboxDepth), 0.01f);
            speedMultiplier = NormalizeSpeed(speedMultiplier);
            airborneHorizontalVelocityRetention = Mathf.Clamp01(
                IsFinite(airborneHorizontalVelocityRetention)
                    ? airborneHorizontalVelocityRetention
                    : 1f);
        }

        private static float NormalizeSpeed(float value)
        {
            return Mathf.Clamp(IsFinite(value) ? value : 1f,
                MinimumSpeedMultiplier, MaximumSpeedMultiplier);
        }

        private static float NormalizeNonNegative(float value)
        {
            return IsFinite(value) ? Mathf.Max(value, 0f) : 0f;
        }

        private static Vector2 NormalizeFinite(Vector2 value, Vector2 fallback)
        {
            return IsFinite(value.x) && IsFinite(value.y) ? value : fallback;
        }

        private static bool IsFinite(float value)
        {
            return !float.IsNaN(value) && !float.IsInfinity(value);
        }
    }

    public sealed class QusapWeaponAttackInputBuffer
    {
        private QusapCombatCommandPress pending;
        private double expiresAt;
        private ulong newestAcceptedPressId;

        public bool HasPendingPress { get; private set; }
        public int PendingCount => HasPendingPress ? 1 : 0;

        public bool TryStore(
            QusapCombatCommandPress press,
            double receivedAt,
            float duration)
        {
            if (!IsWeaponCommand(press.Command)
                || press.PressId == 0
                || press.PressId <= newestAcceptedPressId
                || !IsFinite(receivedAt)
                || !IsFinite(duration)
                || duration <= 0f)
            {
                return false;
            }

            newestAcceptedPressId = press.PressId;
            pending = press;
            expiresAt = receivedAt + duration;
            HasPendingPress = true;
            return true;
        }

        public bool TryConsume(double now, out QusapCombatCommandPress press)
        {
            if (!HasPendingPress || !IsFinite(now) || now > expiresAt)
            {
                Clear();
                press = default;
                return false;
            }

            press = pending;
            Clear();
            return true;
        }

        public void DiscardExpired(double now)
        {
            if (HasPendingPress && (!IsFinite(now) || now > expiresAt))
            {
                Clear();
            }
        }

        public void Clear()
        {
            pending = default;
            expiresAt = 0d;
            HasPendingPress = false;
        }

        public static bool IsWeaponCommand(QusapCombatCommand command)
        {
            return command == QusapCombatCommand.WeaponLight
                || command == QusapCombatCommand.WeaponStrong;
        }

        private static bool IsFinite(double value)
        {
            return !double.IsNaN(value) && !double.IsInfinity(value);
        }
    }
}
