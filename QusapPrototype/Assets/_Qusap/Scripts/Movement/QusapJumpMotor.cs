using UnityEngine;

namespace Qusap
{
    [RequireComponent(typeof(QusapInputReader))]
    [RequireComponent(typeof(QusapGroundSensor))]
    [RequireComponent(typeof(Rigidbody))]
    public class QusapJumpMotor : MonoBehaviour
    {
        [SerializeField] private float jumpHeight = 2.5f;

        private Rigidbody rb;
        private QusapInputReader inputReader;
        private QusapGroundSensor groundSensor;

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

            if (!inputReader.ConsumeJumpPressed() || !groundSensor.IsGrounded)
            {
                return;
            }

            float gravityMagnitude = Mathf.Abs(Physics.gravity.y);
            float upwardSpeed = Mathf.Sqrt(2f * gravityMagnitude * jumpHeight);
            Vector3 velocity = rb.linearVelocity;
            velocity.y = upwardSpeed;
            velocity.z = 0f;
            rb.linearVelocity = velocity;
        }
    }
}
