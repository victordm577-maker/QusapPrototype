using UnityEngine;

namespace Qusap
{
    [RequireComponent(typeof(QusapInputReader))]
    [RequireComponent(typeof(QusapGroundSensor))]
    [RequireComponent(typeof(Rigidbody))]
    public class QusapVerticalMotor : MonoBehaviour
    {
        [SerializeField] private float jumpHeight = 2.5f;
        [SerializeField] private float timeToApex = 0.42f;
        [SerializeField] private float fallGravityMultiplier = 1.6f;
        [SerializeField] private float maximumFallSpeed = 18f;
        [SerializeField] private float jumpCutMultiplier = 0.5f;
        [SerializeField] private float coyoteTime = 0.12f;

        private Rigidbody rb;
        private QusapInputReader inputReader;
        private QusapGroundSensor groundSensor;
        private bool isRising;
        private float riseGravity;
        private float coyoteTimeRemaining;

        private void Awake()
        {
            rb = GetComponent<Rigidbody>();
            inputReader = GetComponent<QusapInputReader>();
            groundSensor = GetComponent<QusapGroundSensor>();
        }

        private void OnValidate()
        {
            jumpHeight = Mathf.Max(jumpHeight, 0.0001f);
            timeToApex = Mathf.Max(timeToApex, 0.1f);
            fallGravityMultiplier = Mathf.Max(fallGravityMultiplier, 1f);
            maximumFallSpeed = Mathf.Max(maximumFallSpeed, 0.0001f);
            jumpCutMultiplier = Mathf.Clamp(jumpCutMultiplier, 0.05f, 1f);
            coyoteTime = Mathf.Max(coyoteTime, 0f);
        }

        private void FixedUpdate()
        {
            if (inputReader == null || groundSensor == null || rb == null)
            {
                return;
            }

            Vector3 velocity = rb.linearVelocity;
            bool jumpPressed = inputReader.ConsumeJumpPressed();

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

            if (!jumpPressed || coyoteTimeRemaining <= 0f)
            {
                if (velocity.y < 0f)
                {
                    float adjustedGravity = Physics.gravity.y * (Mathf.Max(fallGravityMultiplier, 1f) - 1f);
                    velocity.y += adjustedGravity * Time.fixedDeltaTime;
                    velocity.y = Mathf.Max(velocity.y, -maximumFallSpeed);
                    velocity.z = 0f;
                    rb.linearVelocity = velocity;
                }

                return;
            }

            coyoteTimeRemaining = 0f;
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
