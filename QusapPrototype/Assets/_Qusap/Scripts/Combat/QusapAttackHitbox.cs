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
        [SerializeField] private QusapAttackVariant inactiveAirPreview = QusapAttackVariant.None;

        private readonly HashSet<QusapHitReceiver> hitTargets = new();
        private QusapCombatController owner;
        private IQusapAttackDefinition currentAttack;
        private QusapAttackVariant currentVariant;
        private int attackDirection = 1;

        public bool IsActive { get; private set; }

        internal void Initialize(QusapCombatController combatOwner)
        {
            owner = combatOwner;
        }

        internal void BeginAttack(
            IQusapAttackDefinition attackData,
            QusapAttackVariant attackVariant,
            int horizontalDirection)
        {
            currentAttack = attackData;
            currentVariant = attackVariant;
            attackDirection = horizontalDirection < 0 ? -1 : 1;
            hitTargets.Clear();
            IsActive = currentAttack != null;
        }

        internal void EndAttack()
        {
            IsActive = false;
            currentAttack = null;
            currentVariant = QusapAttackVariant.None;
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
                    currentVariant,
                    currentAttack.Damage,
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

                if (!IsActive)
                {
                    return;
                }
            }
        }

        private Vector3 GetWorldCenter(IQusapAttackDefinition attackData, int horizontalDirection)
        {
            Vector2 offset = attackData.HitboxOffset;
            Vector3 localCenter = new(offset.x * horizontalDirection, offset.y, 0f);
            return owner.transform.TransformPoint(localCenter);
        }

        private Vector3 GetWorldHalfExtents(IQusapAttackDefinition attackData)
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

            QusapAttackVariant previewVariant = IsActive
                ? currentVariant
                : inactiveAirPreview;
            IQusapAttackDefinition previewData = IsActive && currentAttack != null
                ? currentAttack
                : previewVariant != QusapAttackVariant.None
                    ? owner.GetAttackDefinition(previewVariant)
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
            Color wireColor = GetVariantColor(previewVariant);
            Color fillColor = wireColor;
            fillColor.a = IsActive ? 0.28f : 0.12f;
            wireColor.a = IsActive ? 1f : 0.8f;
            Gizmos.color = fillColor;
            Gizmos.DrawCube(localCenter, localSize);
            Gizmos.color = wireColor;
            Gizmos.DrawWireCube(localCenter, localSize);
            Gizmos.matrix = previousMatrix;
        }

        private static Color GetVariantColor(QusapAttackVariant variant)
        {
            return variant switch
            {
                QusapAttackVariant.WeakKickAir => new Color(1f, 0.85f, 0.05f, 1f),
                QusapAttackVariant.StrongKickAir => new Color(1f, 0.4f, 0.05f, 1f),
                QusapAttackVariant.DiveHeadbuttAir => new Color(0f, 0.75f, 0.78f, 1f),
                QusapAttackVariant.None => new Color(1f, 0.75f, 0.1f, 1f),
                _ => new Color(1f, 0.15f, 0.05f, 1f)
            };
        }
    }
}
