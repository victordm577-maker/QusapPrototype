using UnityEngine;

namespace Qusap
{
    [RequireComponent(typeof(QusapInputReader))]
    [RequireComponent(typeof(QusapGroundSensor))]
    [RequireComponent(typeof(QusapWallSensor))]
    [RequireComponent(typeof(QusapHorizontalMotor))]
    [RequireComponent(typeof(Rigidbody))]
    public class QusapVerticalMotor : MonoBehaviour
    {
        [SerializeField] private float jumpHeight = 2.5f;
        [SerializeField] private float timeToApex = 0.42f;
        [SerializeField] private float fallGravityMultiplier = 1.6f;
        [SerializeField] private float maximumFallSpeed = 18f;
        [SerializeField] private float jumpCutMultiplier = 0.5f;
        [SerializeField] private float coyoteTime = 0.12f;
        [SerializeField] private float jumpBufferTime = 0.12f;
        [SerializeField] private float wallSlideMaximumFallSpeed = 3f;
        [SerializeField] private float wallJumpHorizontalSpeed = 9f;
        [SerializeField] private float wallJumpNeutralHorizontalSpeed = 6f;
        [SerializeField] private float wallJumpTowardWallHorizontalSpeed = 2.5f;
        [SerializeField] private float wallJumpVerticalSpeed = 9f;
        [SerializeField] private float wallJumpControlLockTime = 0.10f;

        private const float WallJumpInputDeadZone = 0.1f;

        private Rigidbody rb;
        private QusapInputReader inputReader;
        private QusapGroundSensor groundSensor;
        private QusapWallSensor wallSensor;
        private QusapHorizontalMotor horizontalMotor;
        private bool isRising;
        private float riseGravity;
        private float coyoteTimeRemaining;
        private float jumpBufferRemaining;
        private int lastWallJumpSide;

        private void Awake()
        {
            rb = GetComponent<Rigidbody>();
            inputReader = GetComponent<QusapInputReader>();
            groundSensor = GetComponent<QusapGroundSensor>();
            wallSensor = GetComponent<QusapWallSensor>();
            horizontalMotor = GetComponent<QusapHorizontalMotor>();
        }

        private void OnValidate()
        {
            jumpHeight = Mathf.Max(jumpHeight, 0.0001f);
            timeToApex = Mathf.Max(timeToApex, 0.1f);
            fallGravityMultiplier = Mathf.Max(fallGravityMultiplier, 1f);
            maximumFallSpeed = Mathf.Max(maximumFallSpeed, 0.0001f);
            jumpCutMultiplier = Mathf.Clamp(jumpCutMultiplier, 0.05f, 1f);
            coyoteTime = Mathf.Max(coyoteTime, 0f);
            jumpBufferTime = Mathf.Max(jumpBufferTime, 0f);
            wallSlideMaximumFallSpeed = Mathf.Max(wallSlideMaximumFallSpeed, 0f);
            wallJumpHorizontalSpeed = Mathf.Max(wallJumpHorizontalSpeed, 0f);
            wallJumpNeutralHorizontalSpeed = Mathf.Max(wallJumpNeutralHorizontalSpeed, 0f);
            wallJumpTowardWallHorizontalSpeed = Mathf.Max(wallJumpTowardWallHorizontalSpeed, 0f);
            wallJumpVerticalSpeed = Mathf.Max(wallJumpVerticalSpeed, 0f);
            wallJumpControlLockTime = Mathf.Max(wallJumpControlLockTime, 0f);
        }

        private void FixedUpdate()
        {
            if (inputReader == null || groundSensor == null || wallSensor == null || horizontalMotor == null || rb == null)
            {
                return;
            }

            Vector3 velocity = rb.linearVelocity;

            if (groundSensor.IsGrounded)
            {
                lastWallJumpSide = 0;
            }

            bool isSameWallBlocked = lastWallJumpSide != 0
                && wallSensor.IsTouchingWall
                && wallSensor.WallSide == lastWallJumpSide;
            bool jumpPressed = inputReader.ConsumeJumpPressed();

            if (isSameWallBlocked)
            {
                jumpBufferRemaining = 0f;
            }
            else if (jumpPressed)
            {
                jumpBufferRemaining = jumpBufferTime;
            }
            else
            {
                jumpBufferRemaining = Mathf.Max(jumpBufferRemaining - Time.fixedDeltaTime, 0f);
            }

            if (groundSensor.IsGrounded && velocity.y <= 0f)
            {
                coyoteTimeRemaining = coyoteTime;
            }
            else if (!groundSensor.IsGrounded)
            {
                coyoteTimeRemaining = Mathf.Max(coyoteTimeRemaining - Time.fixedDeltaTime, 0f);
            }

            if (inputReader.ConsumeJumpReleased() && velocity.y > 0f && !groundSensor.IsGrounded)
            {
                velocity.y *= jumpCutMultiplier;
                velocity.z = 0f;
                rb.linearVelocity = velocity;
                isRising = false;
                return;
            }

            if (isRising)
            {
                if (velocity.y <= 0f)
                {
                    isRising = false;
                }
                else
                {
                    float additionalGravity = riseGravity - Physics.gravity.y;
                    velocity.y += additionalGravity * Time.fixedDeltaTime;
                    velocity.z = 0f;
                    rb.linearVelocity = velocity;
                    return;
                }
            }

            bool canJump = jumpBufferRemaining > 0f && (groundSensor.IsGrounded || coyoteTimeRemaining > 0f);
            bool canWallJump = jumpBufferRemaining > 0f
                && !groundSensor.IsGrounded
                && wallSensor.IsTouchingWall
                && !isSameWallBlocked;

            if (!canJump && !canWallJump)
            {
                if (velocity.y < 0f)
                {
                    float adjustedGravity = Physics.gravity.y * (Mathf.Max(fallGravityMultiplier, 1f) - 1f);
                    velocity.y += adjustedGravity * Time.fixedDeltaTime;
                    velocity.y = Mathf.Max(velocity.y, -maximumFallSpeed);

                    bool isPushingTowardWall = wallSensor.IsTouchingWall
                        && inputReader.HorizontalValue * wallSensor.WallSide > 0f;

                    if (!groundSensor.IsGrounded && isPushingTowardWall)
                    {
                        velocity.y = Mathf.Max(velocity.y, -wallSlideMaximumFallSpeed);
                    }

                    velocity.z = 0f;
                    rb.linearVelocity = velocity;
                }

                return;
            }

            jumpBufferRemaining = 0f;
            coyoteTimeRemaining = 0f;

            if (canWallJump && !canJump)
            {
                int wallJumpSide = wallSensor.WallSide;
                float horizontalInput = inputReader.HorizontalValue;
                float wallJumpSpeed;

                if (Mathf.Abs(horizontalInput) <= WallJumpInputDeadZone)
                {
                    wallJumpSpeed = wallJumpNeutralHorizontalSpeed;
                }
                else if (horizontalInput * wallSensor.WallSide < 0f)
                {
                    wallJumpSpeed = wallJumpHorizontalSpeed;
                }
                else
                {
                    wallJumpSpeed = wallJumpTowardWallHorizontalSpeed;
                }

                velocity.y = wallJumpVerticalSpeed;
                velocity.z = 0f;
                rb.linearVelocity = velocity;

                horizontalMotor.ApplyWallJumpVelocity(
                    -wallJumpSide * wallJumpSpeed,
                    wallJumpControlLockTime);

                lastWallJumpSide = wallJumpSide;

                jumpHeight = Mathf.Max(jumpHeight, 0.0001f);
                timeToApex = Mathf.Max(timeToApex, 0.1f);
                riseGravity = (-2f * jumpHeight) / (timeToApex * timeToApex);
                isRising = true;
                return;
            }

            timeToApex = Mathf.Max(timeToApex, 0.1f);
            jumpHeight = Mathf.Max(jumpHeight, 0.0001f);

            float jumpVelocity = (2f * jumpHeight) / timeToApex;
            riseGravity = (-2f * jumpHeight) / (timeToApex * timeToApex);

            velocity.y = jumpVelocity;
            velocity.z = 0f;
            rb.linearVelocity = velocity;

            isRising = true;
        }
    }
}
