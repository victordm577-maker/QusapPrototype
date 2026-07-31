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

        private Rigidbody rb;
        private QusapInputReader inputReader;
        private QusapGroundSensor groundSensor;
        private bool isRising;
        private float riseGravity;

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
        }

        private void FixedUpdate()
        {
            if (inputReader == null || groundSensor == null || rb == null)
            {
                return;
            }

            Vector3 velocity = rb.linearVelocity;

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

            if (!inputReader.ConsumeJumpPressed() || !groundSensor.IsGrounded)
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
