using UnityEngine;

namespace Qusap
{
    [DisallowMultipleComponent]
    public sealed class QusapHurtbox : MonoBehaviour
    {
        [SerializeField] private Color gizmoColor = new(0.1f, 0.8f, 1f, 0.85f);
        [SerializeField] private Collider hurtboxCollider;

        public QusapCombatController Owner { get; private set; }
        public QusapHitReceiver Receiver { get; private set; }
        public Collider HurtboxCollider => hurtboxCollider;

        private void Awake()
        {
            CacheReferences();
        }

        private void OnValidate()
        {
            CacheReferences();
        }

        private void CacheReferences()
        {
            hurtboxCollider ??= GetComponent<Collider>();
            Owner = GetComponentInParent<QusapCombatController>();
            Receiver = GetComponentInParent<QusapHitReceiver>();
        }

        private void OnDrawGizmos()
        {
            hurtboxCollider ??= GetComponent<Collider>();

            if (hurtboxCollider == null)
            {
                return;
            }

            Gizmos.color = gizmoColor;
            Gizmos.DrawWireCube(hurtboxCollider.bounds.center, hurtboxCollider.bounds.size);
        }
    }
}
