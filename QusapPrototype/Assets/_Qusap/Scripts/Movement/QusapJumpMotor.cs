using UnityEngine;

namespace Qusap
{
    [RequireComponent(typeof(QusapInputReader))]
    [RequireComponent(typeof(QusapGroundSensor))]
    [RequireComponent(typeof(Rigidbody))]
    public class QusapJumpMotor : MonoBehaviour
    {
        [SerializeField] private float jumpHeight = 2.5f;
        [SerializeField] private float timeToApex = 0.42f;

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

        private void FixedUpdate()
        {
            if (inputReader == null || groundSensor == null)
            {
                return;
            }

            if (isRising)
            {
                Vector3 velocity = rb.linearVelocity;
                if (velocity.y <= 0f)
                {
                    isRising = false;
                    return;
                }

                float additionalGravity = riseGravity - Physics.gravity.y;
                velocity.y += additionalGravity * Time.fixedDeltaTime;
                velocity.z = 0f;
                rb.linearVelocity = velocity;
                return;
            }

            if (!inputReader.ConsumeJumpPressed() || !groundSensor.IsGrounded)
            {
                return;
            }

            timeToApex = Mathf.Max(timeToApex, 0.1f);

            float jumpVelocity = (2f * jumpHeight) / timeToApex;
            riseGravity = (-2f * jumpHeight) / (timeToApex * timeToApex);
            Vector3 velocityToSet = rb.linearVelocity;
            velocityToSet.y = jumpVelocity;
            velocityToSet.z = 0f;
            rb.linearVelocity = velocityToSet;

            isRising = true;
        }
    }
}
