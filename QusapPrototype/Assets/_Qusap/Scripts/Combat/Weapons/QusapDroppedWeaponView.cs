using UnityEngine;

namespace Qusap
{
    [DisallowMultipleComponent]
    public sealed class QusapDroppedWeaponView : MonoBehaviour
    {
        private const int PhysicsQueryCapacity = 32;

        public const float DefaultDuration = 0.40f;
        public const float DefaultOutwardDistance = 0.55f;
        public const float DefaultFallDistance = 0.70f;
        public const float DefaultRestingTilt = 68f;

        private QusapWeaponInstance weapon;
        private GameObject visualPrefab;
        private GameObject visualInstance;
        private Vector3 startPosition;
        private Vector3 endPosition;
        private Quaternion startRotation;
        private Quaternion endRotation;
        private float duration;
        private float arcHeight;
        private float elapsed;
        private float planeZ;
        private bool initialized;
        private bool claimed;
        private ulong? previousOwnerEntityId;
        private double droppedAt;
        private ulong? reservedByEntityId;
        private QusapWeaponReleaseType releaseType;
        private QusapWeaponThrowDirection throwDirection;
        private int capturedFacingDirection;
        private readonly RaycastHit[] sweepHits = new RaycastHit[PhysicsQueryCapacity];
        private readonly Collider[] overlapHits = new Collider[PhysicsQueryCapacity];
        private readonly RuntimeTargetCandidate[] targetCandidates =
            new RuntimeTargetCandidate[PhysicsQueryCapacity];
        private readonly bool[] attemptedCandidates = new bool[PhysicsQueryCapacity];
        private QusapThrownWeaponAttackState attackState;
        private QusapThrownWeaponAttackProfile attackProfile;
        private QusapCombatController thrower;
        private LayerMask targetLayers;
        private bool resolvingImpact;
        private int confirmedImpactCount;
        private QusapHitReceiver confirmedTarget;

        public QusapWeaponInstance Weapon => weapon;
        public ulong InstanceId => weapon?.InstanceId ?? 0;
        public GameObject VisualPrefab => visualPrefab;
        public GameObject VisualInstance => visualInstance;
        public bool IsInitialized => initialized;
        public bool IsSettled => initialized && elapsed >= duration;
        public bool IsClaimed => claimed;
        public bool IsReserved => reservedByEntityId.HasValue;
        public ulong? ReservedByEntityId => reservedByEntityId;
        public ulong? PreviousOwnerEntityId => previousOwnerEntityId;
        public double DroppedAt => droppedAt;
        public float PlaneZ => planeZ;
        public QusapWeaponReleaseType ReleaseType => releaseType;
        public QusapWeaponThrowDirection ThrowDirection => throwDirection;
        public int CapturedFacingDirection => capturedFacingDirection;
        public QusapThrownWeaponAttackState AttackState => attackState;
        public QusapThrownWeaponAttackProfile AttackProfile => attackProfile;
        public bool IsOffensive => attackState?.IsOffensive ?? false;
        public ulong ThrowId => attackState?.ThrowId ?? 0;
        public ulong ThrowerEntityId => attackState?.ThrowerEntityId ?? 0;
        public int ConfirmedImpactCount => confirmedImpactCount;
        public QusapHitReceiver ConfirmedTarget => confirmedTarget;

        private void Update()
        {
            if (initialized && !IsSettled)
            {
                Advance(Time.deltaTime);
            }
        }

        public bool Initialize(
            QusapWeaponInstance droppedWeapon,
            GameObject droppedVisualPrefab,
            Vector3 origin,
            int outwardDirection,
            float animationDuration = DefaultDuration,
            float outwardDistance = DefaultOutwardDistance,
            float fallDistance = DefaultFallDistance,
            ulong? formerOwnerEntityId = null,
            double dropTimestamp = 0d)
        {
            if (!ValidateInitialization(
                    droppedWeapon,
                    droppedVisualPrefab,
                    origin,
                    animationDuration,
                    outwardDistance,
                    fallDistance,
                    formerOwnerEntityId,
                    dropTimestamp))
            {
                return false;
            }

            int direction = outwardDirection < 0 ? -1 : 1;
            bool completed = CompleteInitialization(
                droppedWeapon,
                droppedVisualPrefab,
                origin,
                origin + new Vector3(direction * outwardDistance, -fallDistance, 0f),
                animationDuration,
                0.18f,
                Quaternion.identity,
                Quaternion.Euler(0f, 0f, -DefaultRestingTilt * direction),
                QusapWeaponReleaseType.Disarmed,
                QusapWeaponThrowDirection.Forward,
                direction,
                formerOwnerEntityId,
                dropTimestamp,
                false);
            if (completed)
            {
                attackState = CreateNonOffensiveState(
                    droppedWeapon,
                    QusapWeaponReleaseType.Disarmed,
                    QusapWeaponThrowDirection.Forward,
                    direction,
                    dropTimestamp);
            }

            return completed;
        }

        public bool InitializeVoluntaryThrow(
            QusapWeaponInstance droppedWeapon,
            GameObject droppedVisualPrefab,
            Vector3 origin,
            QusapWeaponThrowDirection direction,
            int capturedFacing,
            QusapWeaponThrowTrajectoryProfile profile,
            ulong throwId,
            ulong formerOwnerEntityId,
            double dropTimestamp,
            QusapCombatController capturedThrower,
            QusapThrownWeaponAttackProfile configuredAttackProfile,
            LayerMask configuredTargetLayers)
        {
            ulong capturedThrowerEntityId = capturedThrower != null
                ? EntityId.ToULong(capturedThrower.GetEntityId())
                : 0;
            if (!ValidateInitialization(
                    droppedWeapon,
                    droppedVisualPrefab,
                    origin,
                    profile.Duration,
                    profile.HorizontalDistance,
                    DefaultFallDistance,
                    formerOwnerEntityId,
                    dropTimestamp)
                || throwId == 0
                || capturedThrowerEntityId != formerOwnerEntityId
                || configuredAttackProfile == null
                || configuredTargetLayers.value == 0)
            {
                return false;
            }

            int facing = capturedFacing < 0 ? -1 : 1;
            QusapThrownWeaponAttackProfile normalizedAttackProfile = new(
                configuredAttackProfile.Damage,
                configuredAttackProfile.HitstunDuration,
                configuredAttackProfile.HorizontalKnockback,
                configuredAttackProfile.VerticalKnockback,
                configuredAttackProfile.DetectionRadius,
                configuredAttackProfile.OffensiveDuration);
            QusapThrownWeaponAttackState configuredAttackState = new(
                throwId,
                capturedThrowerEntityId,
                droppedWeapon.InstanceId,
                QusapWeaponReleaseType.VoluntarySwapThrow,
                direction,
                facing,
                dropTimestamp,
                normalizedAttackProfile.OffensiveDuration);
            bool completed = CompleteInitialization(
                droppedWeapon,
                droppedVisualPrefab,
                origin,
                origin + new Vector3(
                    facing * profile.HorizontalDistance,
                    -DefaultFallDistance,
                    0f),
                profile.Duration,
                profile.ArcHeight,
                Quaternion.identity,
                Quaternion.Euler(0f, 0f, -540f * facing),
                QusapWeaponReleaseType.VoluntarySwapThrow,
                direction,
                facing,
                formerOwnerEntityId,
                dropTimestamp,
                false);
            if (completed)
            {
                attackState = configuredAttackState;
                attackProfile = normalizedAttackProfile;
                thrower = capturedThrower;
                targetLayers = configuredTargetLayers;
            }

            return completed;
        }

        public bool InitializeSettled(
            QusapWeaponInstance droppedWeapon,
            GameObject droppedVisualPrefab,
            Vector3 position,
            double spawnTimestamp)
        {
            if (!ValidateInitialization(
                    droppedWeapon,
                    droppedVisualPrefab,
                    position,
                    DefaultDuration,
                    0f,
                    0f,
                    null,
                    spawnTimestamp))
            {
                return false;
            }

            bool completed = CompleteInitialization(
                droppedWeapon,
                droppedVisualPrefab,
                position,
                position,
                DefaultDuration,
                0f,
                Quaternion.Euler(0f, 0f, DefaultRestingTilt),
                Quaternion.Euler(0f, 0f, DefaultRestingTilt),
                QusapWeaponReleaseType.InitialSpawn,
                QusapWeaponThrowDirection.Forward,
                1,
                null,
                spawnTimestamp,
                true);
            if (completed)
            {
                attackState = CreateNonOffensiveState(
                    droppedWeapon,
                    QusapWeaponReleaseType.InitialSpawn,
                    QusapWeaponThrowDirection.Forward,
                    1,
                    spawnTimestamp);
                attackState.MarkSettled(spawnTimestamp);
            }

            return completed;
        }

        public bool TryReserve(ulong entityId)
        {
            if (!initialized || claimed || entityId == 0 || reservedByEntityId.HasValue)
            {
                return false;
            }

            reservedByEntityId = entityId;
            return true;
        }

        public bool ReleaseReservation(ulong entityId)
        {
            if (entityId == 0 || reservedByEntityId != entityId || claimed)
            {
                return false;
            }

            reservedByEntityId = null;
            return true;
        }

        private bool ValidateInitialization(
            QusapWeaponInstance droppedWeapon,
            GameObject droppedVisualPrefab,
            Vector3 origin,
            float animationDuration,
            float outwardDistance,
            float fallDistance,
            ulong? formerOwnerEntityId,
            double dropTimestamp)
        {
            return !(initialized
                || droppedWeapon == null
                || !droppedWeapon.IsFree
                || droppedVisualPrefab == null
                || !IsFinite(origin)
                || !float.IsFinite(animationDuration)
                || animationDuration <= 0f
                || !float.IsFinite(outwardDistance)
                || outwardDistance < 0f
                || !float.IsFinite(fallDistance)
                || fallDistance < 0f
                || (formerOwnerEntityId.HasValue
                    && formerOwnerEntityId.Value == 0)
                || !IsFinite(dropTimestamp));
        }

        private bool CompleteInitialization(
            QusapWeaponInstance droppedWeapon,
            GameObject droppedVisualPrefab,
            Vector3 origin,
            Vector3 destination,
            float animationDuration,
            float configuredArcHeight,
            Quaternion originRotation,
            Quaternion destinationRotation,
            QusapWeaponReleaseType configuredReleaseType,
            QusapWeaponThrowDirection configuredThrowDirection,
            int configuredFacingDirection,
            ulong? formerOwnerEntityId,
            double dropTimestamp,
            bool settled)
        {
            weapon = droppedWeapon;
            visualPrefab = droppedVisualPrefab;
            duration = animationDuration;
            arcHeight = configuredArcHeight;
            planeZ = origin.z;
            startPosition = origin;
            endPosition = destination;
            startRotation = originRotation;
            endRotation = destinationRotation;
            elapsed = settled ? duration : 0f;
            claimed = false;
            reservedByEntityId = null;
            previousOwnerEntityId = formerOwnerEntityId;
            droppedAt = dropTimestamp;
            releaseType = configuredReleaseType;
            throwDirection = configuredThrowDirection;
            capturedFacingDirection = configuredFacingDirection < 0 ? -1 : 1;
            transform.SetPositionAndRotation(startPosition, startRotation);
            transform.localScale = Vector3.one;

            visualInstance = Instantiate(visualPrefab, transform);
            visualInstance.name = $"Dropped_{visualPrefab.name}_{weapon.InstanceId}";
            visualInstance.transform.localPosition = Vector3.zero;
            visualInstance.transform.localRotation = Quaternion.identity;
            visualInstance.transform.localScale = Vector3.one;
            visualInstance.SetActive(true);
            initialized = true;
            return true;
        }

        internal QusapDroppedWeaponSnapshot CreatePickupSnapshot()
        {
            return new QusapDroppedWeaponSnapshot(
                InstanceId,
                weapon != null && weapon.IsFree,
                IsSettled,
                claimed || reservedByEntityId.HasValue,
                previousOwnerEntityId,
                droppedAt,
                transform.position);
        }

        internal void MarkClaimed()
        {
            if (!initialized || claimed)
            {
                return;
            }

            claimed = true;
            reservedByEntityId = null;
            gameObject.SetActive(false);
        }

        public void Advance(float deltaTime)
        {
            if (!initialized
                || IsSettled
                || !float.IsFinite(deltaTime)
                || deltaTime <= 0f)
            {
                return;
            }

            Vector3 previousPosition = transform.position;
            float previousElapsed = elapsed;
            elapsed = Mathf.Min(elapsed + deltaTime, duration);
            float normalized = elapsed / duration;
            float eased = normalized * normalized * (3f - 2f * normalized);
            Vector3 position = Vector3.LerpUnclamped(startPosition, endPosition, eased);
            position.y += Mathf.Sin(normalized * Mathf.PI) * arcHeight;
            position.z = planeZ;
            transform.SetPositionAndRotation(
                position,
                Quaternion.SlerpUnclamped(startRotation, endRotation, eased));

            ProcessOffensiveSweep(
                previousPosition,
                position,
                droppedAt + previousElapsed,
                droppedAt + elapsed);
            if (IsSettled)
            {
                attackState?.MarkSettled(droppedAt + elapsed);
            }
        }

        private void ProcessOffensiveSweep(
            Vector3 previousPosition,
            Vector3 currentPosition,
            double previousTimestamp,
            double currentTimestamp)
        {
            if (resolvingImpact
                || attackState == null
                || attackProfile == null
                || thrower == null
                || !attackState.TryAdvance(previousTimestamp)
                || !attackState.IsOffensive)
            {
                attackState?.TryAdvance(currentTimestamp);
                return;
            }

            double interval = currentTimestamp - previousTimestamp;
            float offensiveFraction = interval <= 0d
                ? 1f
                : Mathf.Clamp01((float)(
                    (attackState.OffensiveUntil - previousTimestamp) / interval));
            Vector3 offensiveEnd = Vector3.LerpUnclamped(
                previousPosition,
                currentPosition,
                offensiveFraction);
            offensiveEnd.z = planeZ;
            int candidateCount = CollectTargetCandidates(
                previousPosition,
                offensiveEnd);
            TryResolveFirstValidCandidate(
                candidateCount,
                previousPosition,
                offensiveEnd,
                previousTimestamp,
                currentTimestamp,
                offensiveFraction);
            attackState.TryAdvance(currentTimestamp);
        }

        private int CollectTargetCandidates(Vector3 start, Vector3 end)
        {
            int candidateCount = 0;
            int overlapCount = Physics.OverlapSphereNonAlloc(
                start,
                attackProfile.DetectionRadius,
                overlapHits,
                targetLayers,
                QueryTriggerInteraction.Collide);
            for (int i = 0; i < overlapCount; i++)
            {
                AddTargetCandidate(
                    overlapHits[i],
                    0f,
                    start,
                    ref candidateCount);
                overlapHits[i] = null;
            }

            Vector3 displacement = end - start;
            float distance = displacement.magnitude;
            if (distance <= Mathf.Epsilon)
            {
                return candidateCount;
            }

            int hitCount = Physics.SphereCastNonAlloc(
                start,
                attackProfile.DetectionRadius,
                displacement / distance,
                sweepHits,
                distance,
                targetLayers,
                QueryTriggerInteraction.Collide);
            for (int i = 0; i < hitCount; i++)
            {
                RaycastHit hit = sweepHits[i];
                AddTargetCandidate(
                    hit.collider,
                    Mathf.Clamp(hit.distance, 0f, distance),
                    hit.point,
                    ref candidateCount);
                sweepHits[i] = default;
            }

            return candidateCount;
        }

        private void AddTargetCandidate(
            Collider candidateCollider,
            float distance,
            Vector3 impactPoint,
            ref int candidateCount)
        {
            if (candidateCollider == null || !candidateCollider.enabled)
            {
                return;
            }

            QusapHurtbox hurtbox = candidateCollider.GetComponent<QusapHurtbox>();
            hurtbox ??= candidateCollider.GetComponentInParent<QusapHurtbox>();
            QusapHitReceiver receiver = hurtbox != null ? hurtbox.Receiver : null;
            QusapCombatController target = hurtbox != null ? hurtbox.Owner : null;
            if (receiver == null
                || target == null
                || !hurtbox.isActiveAndEnabled
                || !receiver.isActiveAndEnabled
                || !target.isActiveAndEnabled
                || !receiver.AcceptsHits
                || !target.CombatAllowed)
            {
                return;
            }

            ulong targetEntityId = EntityId.ToULong(target.GetEntityId());
            if (targetEntityId == 0
                || targetEntityId == attackState.ThrowerEntityId
                || receiver.gameObject == thrower.gameObject)
            {
                return;
            }

            RuntimeTargetCandidate candidate = new(
                receiver,
                new QusapThrownWeaponTargetCandidate(targetEntityId, distance),
                impactPoint);
            for (int i = 0; i < candidateCount; i++)
            {
                if (targetCandidates[i].Receiver != receiver)
                {
                    continue;
                }

                if (QusapThrownWeaponTargetCandidate.IsPreferred(
                    candidate.LogicalCandidate,
                    targetCandidates[i].LogicalCandidate))
                {
                    targetCandidates[i] = candidate;
                }

                return;
            }

            if (candidateCount < targetCandidates.Length)
            {
                targetCandidates[candidateCount++] = candidate;
            }
        }

        private void TryResolveFirstValidCandidate(
            int candidateCount,
            Vector3 sweepStart,
            Vector3 sweepEnd,
            double previousTimestamp,
            double currentTimestamp,
            float offensiveFraction)
        {
            for (int i = 0; i < candidateCount; i++)
            {
                attemptedCandidates[i] = false;
            }

            for (int attempt = 0; attempt < candidateCount; attempt++)
            {
                int selectedIndex = -1;
                for (int i = 0; i < candidateCount; i++)
                {
                    if (attemptedCandidates[i]
                        || (selectedIndex >= 0
                            && !QusapThrownWeaponTargetCandidate.IsPreferred(
                                targetCandidates[i].LogicalCandidate,
                                targetCandidates[selectedIndex].LogicalCandidate)))
                    {
                        continue;
                    }

                    selectedIndex = i;
                }

                if (selectedIndex < 0)
                {
                    break;
                }

                attemptedCandidates[selectedIndex] = true;
                RuntimeTargetCandidate selected = targetCandidates[selectedIndex];
                float sweepDistance = Vector3.Distance(sweepStart, sweepEnd);
                float trajectoryFraction = sweepDistance <= Mathf.Epsilon
                    ? 0f
                    : Mathf.Clamp01(
                        selected.LogicalCandidate.DistanceAlongTrajectory
                        / sweepDistance);
                double impactTimestamp = previousTimestamp
                    + ((currentTimestamp - previousTimestamp)
                        * offensiveFraction
                        * trajectoryFraction);
                if (TryDeliverImpact(selected, impactTimestamp))
                {
                    break;
                }
            }

            for (int i = 0; i < candidateCount; i++)
            {
                targetCandidates[i] = default;
                attemptedCandidates[i] = false;
            }
        }

        private bool TryDeliverImpact(
            RuntimeTargetCandidate candidate,
            double impactTimestamp)
        {
            if (!attackState.IsOffensive
                || candidate.Receiver == null
                || !candidate.Receiver.AcceptsHits
                || EntityId.ToULong(thrower.GetEntityId())
                    != attackState.ThrowerEntityId)
            {
                return false;
            }

            bool upward = attackState.Direction == QusapWeaponThrowDirection.Up;
            float horizontalKnockback = upward
                ? attackProfile.VerticalKnockback
                : attackProfile.HorizontalKnockback;
            float verticalKnockback = upward
                ? attackProfile.HorizontalKnockback
                : attackProfile.VerticalKnockback;
            QusapHitInfo hitInfo = new(
                thrower,
                QusapAttackType.StrongKick,
                QusapAttackVariant.None,
                attackProfile.Damage,
                attackState.CapturedFacingDirection,
                horizontalKnockback,
                verticalKnockback,
                attackProfile.HitstunDuration,
                candidate.ImpactPoint);

            resolvingImpact = true;
            bool accepted = candidate.Receiver.TryReceiveHit(hitInfo);
            resolvingImpact = false;
            if (!accepted
                || !attackState.TryConsumeImpact(
                    candidate.LogicalCandidate.TargetEntityId,
                    impactTimestamp))
            {
                return false;
            }

            confirmedImpactCount = 1;
            confirmedTarget = candidate.Receiver;
            return true;
        }

        private static QusapThrownWeaponAttackState CreateNonOffensiveState(
            QusapWeaponInstance configuredWeapon,
            QusapWeaponReleaseType configuredReleaseType,
            QusapWeaponThrowDirection configuredDirection,
            int configuredFacing,
            double timestamp)
        {
            return new QusapThrownWeaponAttackState(
                0,
                0,
                configuredWeapon.InstanceId,
                configuredReleaseType,
                configuredDirection,
                configuredFacing,
                timestamp,
                0d);
        }

        private readonly struct RuntimeTargetCandidate
        {
            public RuntimeTargetCandidate(
                QusapHitReceiver receiver,
                QusapThrownWeaponTargetCandidate logicalCandidate,
                Vector3 impactPoint)
            {
                Receiver = receiver;
                LogicalCandidate = logicalCandidate;
                ImpactPoint = impactPoint;
            }

            public QusapHitReceiver Receiver { get; }
            public QusapThrownWeaponTargetCandidate LogicalCandidate { get; }
            public Vector3 ImpactPoint { get; }
        }

        private static bool IsFinite(Vector3 value)
        {
            return float.IsFinite(value.x)
                && float.IsFinite(value.y)
                && float.IsFinite(value.z);
        }

        private static bool IsFinite(double value)
        {
            return !double.IsNaN(value) && !double.IsInfinity(value);
        }
    }
}
