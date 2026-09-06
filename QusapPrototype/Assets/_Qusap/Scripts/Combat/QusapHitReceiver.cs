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

        private Rigidbody rb;
        private QusapCombatController combatController;
        private QusapHitstunController hitstunController;

        public event Action<QusapHitInfo> HitReceived;

        public float TotalDamageReceived { get; private set; }

        public bool AcceptsHits
        {
            get => acceptsHits;
            set => acceptsHits = value;
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
        }

        public bool TryReceiveHit(QusapHitInfo hitInfo)
        {
            if (!acceptsHits
                || hitInfo.Source == null
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

            TotalDamageReceived += Mathf.Max(hitInfo.Damage, 0f);

            HitReceived?.Invoke(hitInfo);
            return true;
        }

        public void ResetDamage()
        {
            TotalDamageReceived = 0f;
        }
    }
}
