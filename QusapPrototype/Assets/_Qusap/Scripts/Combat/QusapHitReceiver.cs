using System;
using UnityEngine;

namespace Qusap
{
    [DisallowMultipleComponent]
    [RequireComponent(typeof(Rigidbody))]
    public sealed class QusapHitReceiver : MonoBehaviour
    {
        [SerializeField] private bool acceptsHits = true;
        [SerializeField] private float knockbackMultiplier = 1f;
        [SerializeField, Min(1f)] private float maxHealth = 100f;

        private Rigidbody rb;
        private QusapCombatController combatController;
        private QusapHitstunController hitstunController;
        private QusapPlayerElimination elimination;
        private bool depletionPending;

        public event Action<QusapHitInfo> HitReceived;
        public event Action<QusapFinisherHitInfo> FinisherReceived;
        public event Action<QusapHealthChange> HealthChanged;
        public event Action<QusapHealthChange> Healed;
        public event Action<QusapHealthChange> HealthDepleted;
        public event Action<QusapHitReceiver> Eliminated;

        public float TotalDamageReceived { get; private set; }
        public float MaxHealth => IsFiniteNonNegative(maxHealth) ? Mathf.Max(1f, maxHealth) : 100f;
        public float CurrentHealth => Mathf.Clamp(MaxHealth - TotalDamageReceived, 0f, MaxHealth);
        public bool IsHealthDepleted => depletionPending || IsEliminated;
        public bool IsEliminated { get; private set; }
        public bool IsGameplayRetired { get; private set; }
        public void RetireFromRaid() { IsGameplayRetired = true; acceptsHits = false; }

        public bool AcceptsHits
        {
            get => acceptsHits && !IsHealthDepleted && !IsGameplayRetired;
            set => acceptsHits = value && !IsHealthDepleted && !IsGameplayRetired;
        }

        private void Awake()
        {
            rb = GetComponent<Rigidbody>();
            combatController = GetComponent<QusapCombatController>();
            hitstunController = GetComponent<QusapHitstunController>();
        }

        private void OnValidate()
        {
            knockbackMultiplier = Mathf.Max(knockbackMultiplier, 0f);
            maxHealth = MaxHealth;
        }

        private void LateUpdate()
        {
            // The attacker must finish its hit callback/finisher resolution first.
            if (!depletionPending || IsEliminated) return;
            elimination ??= GetComponent<QusapPlayerElimination>();
            if (elimination == null) elimination = gameObject.AddComponent<QusapPlayerElimination>();
            IsEliminated = true;
            acceptsHits = false;
            elimination.Apply();
            Eliminated?.Invoke(this);
        }

        public bool TryReceiveHit(QusapHitInfo hitInfo)
        {
            if (!AcceptsHits
                || hitInfo.Source == null
                || !IsFiniteNonNegative(hitInfo.Damage)
                || hitInfo.Source.gameObject == gameObject
                || (combatController != null && !combatController.CombatAllowed))
            {
                return false;
            }

            Vector3 velocity = rb.linearVelocity;
            velocity.x = hitInfo.HorizontalDirection
                * hitInfo.HorizontalKnockback
                * knockbackMultiplier;
            float verticalKnockback = hitInfo.VerticalKnockback * knockbackMultiplier;
            velocity.y = verticalKnockback < 0f
                ? Mathf.Min(velocity.y, verticalKnockback)
                : Mathf.Max(velocity.y, verticalKnockback);
            velocity.z = 0f;
            rb.linearVelocity = velocity;
            rb.WakeUp();

            hitstunController?.EnterHitstun(hitInfo.HitstunDuration);

            ApplyDamage(hitInfo.Damage, QusapHealthChangeSource.Combat, hitInfo.Source);

            HitReceived?.Invoke(hitInfo);
            return true;
        }

        public bool TryReceiveFinisher(QusapFinisherHitInfo hitInfo)
        {
            if (!AcceptsHits
                || hitInfo.Source == null
                || !hitInfo.Source.CombatAllowed
                || !hitInfo.Source.isActiveAndEnabled
                || !hitInfo.Source.gameObject.activeInHierarchy
                || hitInfo.Source.gameObject == gameObject
                || !Enum.IsDefined(typeof(QusapComboId), hitInfo.ComboId)
                || hitInfo.RequestsDisarm != (hitInfo.ComboId == QusapComboId.Disarm)
                || !IsFiniteNonNegative(hitInfo.Damage)
                || !IsFiniteNonNegative(hitInfo.HorizontalKnockback)
                || !IsFiniteNonNegative(hitInfo.VerticalKnockback)
                || !IsFiniteNonNegative(hitInfo.HitstunDuration)
                || (combatController != null && !combatController.CombatAllowed))
            {
                return false;
            }

            Vector3 velocity = rb.linearVelocity;
            velocity.x = hitInfo.HorizontalDirection
                * hitInfo.HorizontalKnockback
                * knockbackMultiplier;
            float verticalKnockback = hitInfo.VerticalKnockback * knockbackMultiplier;
            velocity.y = Mathf.Max(velocity.y, verticalKnockback);
            velocity.z = 0f;
            rb.linearVelocity = velocity;
            rb.WakeUp();

            hitstunController?.EnterHitstun(hitInfo.HitstunDuration);
            ApplyDamage(hitInfo.Damage, QusapHealthChangeSource.Combat, hitInfo.Source);

            FinisherReceived?.Invoke(hitInfo);
            return true;
        }

        public void ResetDamage()
        {
            // Legacy live-player test reset. Only an explicit new session can revive.
            if (IsHealthDepleted || IsGameplayRetired) return;
            float previous = TotalDamageReceived;
            TotalDamageReceived = 0f;
            HealthChanged?.Invoke(new QusapHealthChange(this, previous,
                QusapHealthChangeSource.NewSession, null));
        }

        public void ResetForNewSession()
        {
            if (IsGameplayRetired) return; // A retired raid participant must be replaced for a new raid.
            elimination?.RestoreForNewSession();
            IsEliminated = false;
            depletionPending = false;
            acceptsHits = true;
            float previous = TotalDamageReceived;
            TotalDamageReceived = 0f;
            HealthChanged?.Invoke(new QusapHealthChange(this, previous,
                QusapHealthChangeSource.NewSession, null));
        }

        public float Heal(float amount)
        {
            if (IsHealthDepleted || IsGameplayRetired || !IsFiniteNonNegative(amount) || amount <= 0f
                || TotalDamageReceived <= 0f) return 0f;
            float previous = TotalDamageReceived;
            TotalDamageReceived = Mathf.Clamp(previous - amount, 0f, MaxHealth);
            var change = new QusapHealthChange(this, previous, QusapHealthChangeSource.Healing, null);
            HealthChanged?.Invoke(change);
            Healed?.Invoke(change);
            return change.AppliedAmount;
        }

        public bool TryReceiveEnvironmentDamage(float amount, UnityEngine.Object source,
            Vector3 knockback = default)
        {
            if (!AcceptsHits || !IsFiniteNonNegative(amount) || amount <= 0f
                || !IsFiniteVector(knockback)
                || (combatController != null && !combatController.CombatAllowed)) return false;
            if (knockback != Vector3.zero)
            {
                rb.linearVelocity += knockback;
                rb.WakeUp();
            }
            ApplyDamage(amount, QusapHealthChangeSource.Environment, source);
            return true;
        }

        private void ApplyDamage(float amount, QusapHealthChangeSource source, UnityEngine.Object sourceObject)
        {
            float previous = TotalDamageReceived;
            TotalDamageReceived += amount;
            bool depleted = CurrentHealth <= 0f && !depletionPending;
            if (depleted) depletionPending = true;
            var change = new QusapHealthChange(this, previous, source, sourceObject);
            if (amount > 0f) HealthChanged?.Invoke(change);
            if (depleted) HealthDepleted?.Invoke(change);
        }

        private static bool IsFiniteVector(Vector3 value)
        {
            return !float.IsNaN(value.x) && !float.IsInfinity(value.x)
                && !float.IsNaN(value.y) && !float.IsInfinity(value.y)
                && !float.IsNaN(value.z) && !float.IsInfinity(value.z);
        }

        private static bool IsFiniteNonNegative(float value)
        {
            return !float.IsNaN(value) && !float.IsInfinity(value) && value >= 0f;
        }
    }
}
