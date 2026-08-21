using System.Collections.Generic;
using UnityEngine;

namespace Qusap
{
    [DefaultExecutionOrder(60)]
    [DisallowMultipleComponent]
    public sealed class QusapAttackHitbox : MonoBehaviour
    {
        [SerializeField] private LayerMask targetLayers = ~0;
        [SerializeField] private QusapAttackType inactivePreview = QusapAttackType.WeakKick;

        private readonly HashSet<QusapHitReceiver> hitTargets = new();
        private QusapCombatController owner;
        private QusapAttackData currentAttack;
        private int attackDirection = 1;

        public bool IsActive { get; private set; }

        internal void Initialize(QusapCombatController combatOwner)
        {
            owner = combatOwner;
        }

        internal void BeginAttack(QusapAttackData attackData, int horizontalDirection)
        {
            currentAttack = attackData;
            attackDirection = horizontalDirection < 0 ? -1 : 1;
            hitTargets.Clear();
            IsActive = currentAttack != null;
        }

        internal void EndAttack()
        {
            IsActive = false;
            currentAttack = null;
            hitTargets.Clear();
        }

        private void Awake()
        {
            owner ??= GetComponentInParent<QusapCombatController>();
        }

        private void OnDisable()
        {
            EndAttack();
        }

        private void FixedUpdate()
        {
            if (!IsActive || currentAttack == null || owner == null || !owner.CombatAllowed)
            {
                return;
            }

            Vector3 center = GetWorldCenter(currentAttack, attackDirection);
            Vector3 halfExtents = GetWorldHalfExtents(currentAttack);
            Collider[] overlaps = Physics.OverlapBox(
                center,
                halfExtents,
                owner.transform.rotation,
                targetLayers,
                QueryTriggerInteraction.Collide);

            foreach (Collider overlap in overlaps)
            {
                QusapHurtbox hurtbox = overlap.GetComponent<QusapHurtbox>();
                if (hurtbox == null)
                {
                    hurtbox = overlap.GetComponentInParent<QusapHurtbox>();
                }

                QusapHitReceiver receiver = hurtbox != null ? hurtbox.Receiver : null;

                if (receiver == null
                    || receiver == owner.HitReceiver
                    || hitTargets.Contains(receiver))
                {
                    continue;
                }

                QusapHitInfo hitInfo = new(
                    owner,
                    currentAttack.AttackType,
                    attackDirection,
                    currentAttack.HorizontalKnockback,
                    currentAttack.VerticalKnockback,
                    currentAttack.HitstunDuration,
                    center);

                if (!receiver.TryReceiveHit(hitInfo))
                {
                    continue;
                }

                hitTargets.Add(receiver);
                owner.NotifyAttackHit(receiver);
            }
        }

        private Vector3 GetWorldCenter(QusapAttackData attackData, int horizontalDirection)
        {
            Vector2 offset = attackData.HitboxOffset;
            Vector3 localCenter = new(offset.x * horizontalDirection, offset.y, 0f);
            return owner.transform.TransformPoint(localCenter);
        }

        private Vector3 GetWorldHalfExtents(QusapAttackData attackData)
        {
            Vector3 scale = owner.transform.lossyScale;
            return new Vector3(
                attackData.HitboxSize.x * Mathf.Abs(scale.x) * 0.5f,
                attackData.HitboxSize.y * Mathf.Abs(scale.y) * 0.5f,
                attackData.HitboxDepth * Mathf.Abs(scale.z) * 0.5f);
        }

        private void OnDrawGizmos()
        {
            owner ??= GetComponentInParent<QusapCombatController>();

            if (owner == null)
            {
                return;
            }

            QusapAttackData previewData = IsActive && currentAttack != null
                ? currentAttack
                : owner.GetAttackData(inactivePreview);

            if (previewData == null)
            {
                return;
            }

            int previewDirection = IsActive ? attackDirection : owner.FacingDirection;
            Vector2 offset = previewData.HitboxOffset;
            Vector3 localCenter = new(offset.x * previewDirection, offset.y, 0f);
            Vector3 localSize = new(
                previewData.HitboxSize.x,
                previewData.HitboxSize.y,
                previewData.HitboxDepth);

            Matrix4x4 previousMatrix = Gizmos.matrix;
            Gizmos.matrix = owner.transform.localToWorldMatrix;
            Gizmos.color = IsActive
                ? new Color(1f, 0.15f, 0.05f, 0.28f)
                : new Color(1f, 0.75f, 0.1f, 0.12f);
            Gizmos.DrawCube(localCenter, localSize);
            Gizmos.color = IsActive
                ? new Color(1f, 0.1f, 0.05f, 1f)
                : new Color(1f, 0.75f, 0.1f, 0.8f);
            Gizmos.DrawWireCube(localCenter, localSize);
            Gizmos.matrix = previousMatrix;
        }
    }
}
