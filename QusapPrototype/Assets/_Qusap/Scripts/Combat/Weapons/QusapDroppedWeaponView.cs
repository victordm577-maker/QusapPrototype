using UnityEngine;

namespace Qusap
{
    [DisallowMultipleComponent]
    public sealed class QusapDroppedWeaponView : MonoBehaviour
    {
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
            return CompleteInitialization(
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
        }

        public bool InitializeVoluntaryThrow(
            QusapWeaponInstance droppedWeapon,
            GameObject droppedVisualPrefab,
            Vector3 origin,
            QusapWeaponThrowDirection direction,
            int capturedFacing,
            QusapWeaponThrowTrajectoryProfile profile,
            ulong formerOwnerEntityId,
            double dropTimestamp)
        {
            if (!ValidateInitialization(
                    droppedWeapon,
                    droppedVisualPrefab,
                    origin,
                    profile.Duration,
                    profile.HorizontalDistance,
                    DefaultFallDistance,
                    formerOwnerEntityId,
                    dropTimestamp))
            {
                return false;
            }

            int facing = capturedFacing < 0 ? -1 : 1;
            return CompleteInitialization(
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

            return CompleteInitialization(
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

            elapsed = Mathf.Min(elapsed + deltaTime, duration);
            float normalized = elapsed / duration;
            float eased = normalized * normalized * (3f - 2f * normalized);
            Vector3 position = Vector3.LerpUnclamped(startPosition, endPosition, eased);
            position.y += Mathf.Sin(normalized * Mathf.PI) * arcHeight;
            position.z = planeZ;
            transform.SetPositionAndRotation(
                position,
                Quaternion.SlerpUnclamped(startRotation, endRotation, eased));
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
