using UnityEngine;

namespace Qusap
{
    [DefaultExecutionOrder(-100)]
    [RequireComponent(typeof(Rigidbody))]
    [RequireComponent(typeof(QusapInputReader))]
    [RequireComponent(typeof(QusapGroundSensor))]
    [RequireComponent(typeof(QusapWallSensor))]
    public sealed class QusapDashMotor : MonoBehaviour
    {
        private const float DirectionDeadZone = 0.01f;

        [SerializeField] private float dashDuration = 0.16f;
        [SerializeField] private float dashDistance = 2f;
        [SerializeField] private float dashCooldown = 1f;
        [SerializeField] private float wallRechargeContactTime = 0.1f;

        private Rigidbody rb;
        private QusapInputReader inputReader;
        private QusapGroundSensor groundSensor;
        private QusapWallSensor wallSensor;
        private Collider trackedWallCollider;
        private Collider lastWallRechargeCollider;
        private float trackedWallContactTime;
        private float cooldownRemaining;
        private float dashElapsed;
        private float dashDirection = 1f;
        private float lastHorizontalDirection = 1f;
        private bool hasAirDash = true;
        private bool gravityBeforeDash;
        private bool dashEndPending;

        public bool IsDashing { get; private set; }

        private void Awake()
        {
            rb = GetComponent<Rigidbody>();
            inputReader = GetComponent<QusapInputReader>();
            groundSensor = GetComponent<QusapGroundSensor>();
            wallSensor = GetComponent<QusapWallSensor>();
        }

        private void OnValidate()
        {
            dashDuration = Mathf.Max(dashDuration, 0.0001f);
            dashDistance = Mathf.Max(dashDistance, 0f);
            dashCooldown = Mathf.Max(dashCooldown, 0f);
            wallRechargeContactTime = Mathf.Max(wallRechargeContactTime, 0f);
        }

        private void OnDisable()
        {
            CancelDash();
        }

        private void FixedUpdate()
        {
            if (rb == null || inputReader == null || groundSensor == null || wallSensor == null)
            {
                return;
            }

            if (dashEndPending)
            {
                CancelDash();
            }

            cooldownRemaining = Mathf.Max(cooldownRemaining - Time.fixedDeltaTime, 0f);
            UpdateLastHorizontalDirection();
            UpdateAirDashRecharge();

            bool dashPressed = inputReader.ConsumeDashPressed();

            if (IsDashing)
            {
                ApplyDashVelocity();
                dashElapsed += Time.fixedDeltaTime;
                dashEndPending = dashElapsed >= dashDuration;
                return;
            }

            if (!dashPressed || cooldownRemaining > 0f || !CanStartDash())
            {
                return;
            }

            StartDash();
            ApplyDashVelocity();
            dashElapsed += Time.fixedDeltaTime;
            dashEndPending = dashElapsed >= dashDuration;
        }

        public void ResetDashState()
        {
            CancelDash();
            hasAirDash = true;
            cooldownRemaining = 0f;
            trackedWallCollider = null;
            lastWallRechargeCollider = null;
            trackedWallContactTime = 0f;
        }

        private bool CanStartDash()
        {
            return groundSensor.IsGrounded || hasAirDash;
        }

        private void StartDash()
        {
            bool startsOnWall = wallSensor.IsTouchingWall
                && wallSensor.CurrentWallCollider != null
                && wallSensor.WallSide != 0;

            dashDirection = startsOnWall
                ? -wallSensor.WallSide
                : lastHorizontalDirection;

            if (!groundSensor.IsGrounded)
            {
                hasAirDash = false;
            }

            gravityBeforeDash = rb.useGravity;
            rb.useGravity = false;
            cooldownRemaining = dashCooldown;
            dashElapsed = 0f;
            dashEndPending = false;
            IsDashing = true;
        }

        private void ApplyDashVelocity()
        {
            float dashSpeed = dashDistance / dashDuration;
            rb.linearVelocity = new Vector3(dashDirection * dashSpeed, 0f, 0f);
        }

        private void CancelDash()
        {
            if (IsDashing && rb != null)
            {
                rb.useGravity = gravityBeforeDash;
            }

            IsDashing = false;
            dashEndPending = false;
            dashElapsed = 0f;
        }

        private void UpdateLastHorizontalDirection()
        {
            float horizontalInput = inputReader.HorizontalValue;

            if (Mathf.Abs(horizontalInput) > DirectionDeadZone)
            {
                lastHorizontalDirection = Mathf.Sign(horizontalInput);
            }
        }

        private void UpdateAirDashRecharge()
        {
            if (groundSensor.IsGrounded)
            {
                hasAirDash = true;
                trackedWallCollider = null;
                lastWallRechargeCollider = null;
                trackedWallContactTime = 0f;
                return;
            }

            Collider currentWallCollider = wallSensor.CurrentWallCollider;

            if (!wallSensor.IsTouchingWall || currentWallCollider == null)
            {
                trackedWallCollider = null;
                trackedWallContactTime = 0f;
                return;
            }

            if (trackedWallCollider != currentWallCollider)
            {
                trackedWallCollider = currentWallCollider;
                trackedWallContactTime = 0f;
            }

            if (currentWallCollider == lastWallRechargeCollider)
            {
                return;
            }

            trackedWallContactTime += Time.fixedDeltaTime;

            if (trackedWallContactTime < wallRechargeContactTime)
            {
                return;
            }

            hasAirDash = true;
            lastWallRechargeCollider = currentWallCollider;
        }
    }
}
