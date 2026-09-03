using System.Collections.Generic;
using UnityEngine;

namespace Qusap
{
    [DefaultExecutionOrder(-200)]
    [DisallowMultipleComponent]
    [RequireComponent(typeof(Rigidbody))]
    [RequireComponent(typeof(BoxCollider))]
    public sealed class QusapMovingPlatform : MonoBehaviour
    {
        public enum MovementProfile
        {
            Constant,
            Smooth
        }

        [Header("Path (local offsets, world-unit distance)")]
        [SerializeField] private Vector3 startOffset = Vector3.zero;
        [SerializeField] private Vector3 endOffset = new(3.5f, 0f, 0f);
        [SerializeField, Min(0.01f)] private float speed = 2.25f;
        [SerializeField, Min(0f)] private float pauseAtEndpoints = 0.35f;
        [SerializeField, Range(0f, 1f)] private float initialPhase;
        [SerializeField] private bool startActive = true;
        [SerializeField] private MovementProfile movementProfile = MovementProfile.Constant;

        [Header("Rider transport")]
        [SerializeField, Range(0f, 1f)] private float minimumRiderNormalY = 0.6f;
        [SerializeField, Min(0.01f)] private float standingTolerance = 0.18f;

        [Header("Scene view")]
        [SerializeField] private bool showGizmos = true;

        private sealed class RiderContact
        {
            public Collider Collider;
            public float LastSeenFixedTime;
        }

        private readonly Dictionary<Rigidbody, RiderContact> riders = new();
        private Rigidbody platformBody;
        private BoxCollider platformCollider;
        private Vector3 authoredOrigin;
        private float planeZ;
        private float pathProgress;
        private float pauseRemaining;
        private bool movingForward;
        private bool movementActive;
        private bool runtimeInitialized;

        public Vector3 StartPoint => BuildPathPoint(startOffset);
        public Vector3 EndPoint => BuildPathPoint(endOffset);
        public bool IsMovementActive => movementActive;

        private void Awake()
        {
            platformBody = GetComponent<Rigidbody>();
            platformCollider = GetComponent<BoxCollider>();
            ConfigureBody();

            authoredOrigin = platformBody.position;
            authoredOrigin.z = 0f;
            planeZ = 0f;
            runtimeInitialized = true;
        }

        private void OnEnable()
        {
            if (!runtimeInitialized)
            {
                return;
            }

            ResetMotion();
        }

        private void OnDisable()
        {
            riders.Clear();
        }

        private void OnValidate()
        {
            startOffset.z = 0f;
            endOffset.z = 0f;
            speed = Mathf.Max(speed, 0.01f);
            pauseAtEndpoints = Mathf.Max(pauseAtEndpoints, 0f);
            initialPhase = Mathf.Repeat(initialPhase, 1f);
            minimumRiderNormalY = Mathf.Clamp01(minimumRiderNormalY);
            standingTolerance = Mathf.Max(standingTolerance, 0.01f);
        }

        private void FixedUpdate()
        {
            if (platformBody == null || platformCollider == null)
            {
                return;
            }

            Vector3 currentPosition = platformBody.position;
            Vector3 targetPosition = movementActive
                ? AdvanceMotion(Time.fixedDeltaTime)
                : EvaluatePathPosition(pathProgress);
            targetPosition.z = planeZ;

            Vector3 platformDelta = targetPosition - currentPosition;
            platformDelta.z = 0f;

            RemoveInvalidRiders();
            if (platformDelta.sqrMagnitude > 0f)
            {
                CarryRiders(platformDelta);
                platformBody.MovePosition(targetPosition);
            }
        }

        public void SetMovementActive(bool active)
        {
            movementActive = active;
        }

        public void ResetMotion()
        {
            float cyclePosition = initialPhase * 2f;
            movingForward = cyclePosition < 1f;
            pathProgress = movingForward ? cyclePosition : 2f - cyclePosition;
            pathProgress = Mathf.Clamp01(pathProgress);
            pauseRemaining = 0f;
            movementActive = startActive;

            Vector3 initialPosition = EvaluatePathPosition(pathProgress);
            initialPosition.z = planeZ;
            platformBody.position = initialPosition;
        }

        private Vector3 AdvanceMotion(float deltaTime)
        {
            if (pauseRemaining > 0f)
            {
                pauseRemaining = Mathf.Max(pauseRemaining - deltaTime, 0f);
                if (pauseRemaining <= 0f)
                {
                    movingForward = !movingForward;
                }

                return EvaluatePathPosition(pathProgress);
            }

            float pathLength = Vector3.Distance(StartPoint, EndPoint);
            if (pathLength <= Mathf.Epsilon)
            {
                return StartPoint;
            }

            float targetProgress = movingForward ? 1f : 0f;
            pathProgress = Mathf.MoveTowards(
                pathProgress,
                targetProgress,
                speed * deltaTime / pathLength);

            if (Mathf.Approximately(pathProgress, targetProgress))
            {
                pathProgress = targetProgress;
                pauseRemaining = pauseAtEndpoints;

                if (pauseRemaining <= 0f)
                {
                    movingForward = !movingForward;
                }
            }

            return EvaluatePathPosition(pathProgress);
        }

        private Vector3 EvaluatePathPosition(float progress)
        {
            float evaluatedProgress = movementProfile == MovementProfile.Smooth
                ? Mathf.SmoothStep(0f, 1f, progress)
                : progress;

            return Vector3.LerpUnclamped(StartPoint, EndPoint, evaluatedProgress);
        }

        private Vector3 BuildPathPoint(Vector3 offset)
        {
            Vector3 point = authoredOrigin + transform.rotation * offset;
            point.z = runtimeInitialized ? planeZ : 0f;
            return point;
        }

        private void ConfigureBody()
        {
            platformBody.useGravity = false;
            platformBody.isKinematic = true;
            platformBody.interpolation = RigidbodyInterpolation.Interpolate;
            platformBody.collisionDetectionMode = CollisionDetectionMode.ContinuousSpeculative;
            platformBody.constraints = RigidbodyConstraints.FreezePositionZ
                | RigidbodyConstraints.FreezeRotation;
        }

        private void OnCollisionEnter(Collision collision)
        {
            RefreshRiderContact(collision);
        }

        private void OnCollisionStay(Collision collision)
        {
            RefreshRiderContact(collision);
        }

        private void OnCollisionExit(Collision collision)
        {
            Rigidbody riderBody = collision.rigidbody;
            if (riderBody != null)
            {
                riders.Remove(riderBody);
            }
        }

        private void RefreshRiderContact(Collision collision)
        {
            Rigidbody riderBody = collision.rigidbody;
            if (riderBody == null || riderBody == platformBody || riderBody.isKinematic)
            {
                return;
            }

            bool isStandingOnTop = false;
            foreach (ContactPoint contact in collision.contacts)
            {
                if (contact.thisCollider == platformCollider
                    && contact.normal.y <= -minimumRiderNormalY)
                {
                    isStandingOnTop = true;
                    break;
                }
            }

            if (!isStandingOnTop)
            {
                riders.Remove(riderBody);
                return;
            }

            if (!riders.TryGetValue(riderBody, out RiderContact riderContact))
            {
                riderContact = new RiderContact();
                riders.Add(riderBody, riderContact);
            }

            riderContact.Collider = collision.collider;
            riderContact.LastSeenFixedTime = Time.fixedTime;
        }

        private void RemoveInvalidRiders()
        {
            List<Rigidbody> invalidRiders = null;
            Bounds platformBounds = platformCollider.bounds;
            float maximumContactAge = Time.fixedDeltaTime * 1.5f;

            foreach (KeyValuePair<Rigidbody, RiderContact> entry in riders)
            {
                Rigidbody riderBody = entry.Key;
                RiderContact riderContact = entry.Value;

                bool invalid = riderBody == null
                    || riderBody.isKinematic
                    || riderContact.Collider == null
                    || Time.fixedTime - riderContact.LastSeenFixedTime > maximumContactAge;

                if (!invalid)
                {
                    Bounds riderBounds = riderContact.Collider.bounds;
                    float platformTop = platformBounds.max.y;
                    bool overlapsHorizontally = riderBounds.max.x >= platformBounds.min.x - standingTolerance
                        && riderBounds.min.x <= platformBounds.max.x + standingTolerance;
                    bool remainsOnTop = riderBounds.min.y >= platformTop - standingTolerance
                        && riderBounds.min.y <= platformTop + standingTolerance;
                    invalid = !overlapsHorizontally || !remainsOnTop;
                }

                if (invalid)
                {
                    invalidRiders ??= new List<Rigidbody>();
                    invalidRiders.Add(riderBody);
                }
            }

            if (invalidRiders == null)
            {
                return;
            }

            foreach (Rigidbody invalidRider in invalidRiders)
            {
                riders.Remove(invalidRider);
            }
        }

        private void CarryRiders(Vector3 platformDelta)
        {
            foreach (Rigidbody riderBody in riders.Keys)
            {
                if (riderBody == null)
                {
                    continue;
                }

                Vector3 carriedPosition = riderBody.position + platformDelta;
                carriedPosition.z = riderBody.position.z;
                riderBody.MovePosition(carriedPosition);
            }
        }

        private void OnDrawGizmosSelected()
        {
            if (!showGizmos)
            {
                return;
            }

            Vector3 origin = Application.isPlaying && runtimeInitialized
                ? authoredOrigin
                : transform.position;
            Vector3 start = origin + transform.rotation * startOffset;
            Vector3 end = origin + transform.rotation * endOffset;
            start.z = 0f;
            end.z = 0f;

            Gizmos.color = new Color(0.2f, 0.85f, 1f, 1f);
            Gizmos.DrawLine(start, end);

            BoxCollider box = platformCollider != null
                ? platformCollider
                : GetComponent<BoxCollider>();
            Vector3 size = box != null
                ? Vector3.Scale(box.size, Abs(transform.lossyScale))
                : Vector3.one;

            Gizmos.color = new Color(0.2f, 0.85f, 1f, 0.8f);
            Gizmos.DrawWireCube(start, size);
            Gizmos.DrawWireCube(end, size);
        }

        private static Vector3 Abs(Vector3 value)
        {
            return new Vector3(Mathf.Abs(value.x), Mathf.Abs(value.y), Mathf.Abs(value.z));
        }
    }
}
