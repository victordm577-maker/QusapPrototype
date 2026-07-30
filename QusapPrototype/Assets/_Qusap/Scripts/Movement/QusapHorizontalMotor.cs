using UnityEngine;

namespace Qusap
{
    [RequireComponent(typeof(Rigidbody))]
    [RequireComponent(typeof(QusapInputReader))]
    public class QusapHorizontalMotor : MonoBehaviour
    {
        [SerializeField] private float maxSpeed = 7f;
        [SerializeField] private float acceleration = 45f;
        [SerializeField] private float deceleration = 60f;
        [SerializeField] private float turnAcceleration = 75f;

        private Rigidbody rb;
        private QusapInputReader inputReader;

        private void Awake()
        {
            rb = GetComponent<Rigidbody>();
            inputReader = GetComponent<QusapInputReader>();
        }

        private void FixedUpdate()
        {
            if (inputReader == null)
            {
                return;
            }

            float targetSpeed = inputReader.HorizontalValue * maxSpeed;
            Vector3 velocity = rb.linearVelocity;
            float currentSpeed = velocity.x;

            float desiredSpeed;
            if (Mathf.Approximately(inputReader.HorizontalValue, 0f))
            {
                desiredSpeed = Mathf.MoveTowards(currentSpeed, 0f, deceleration * Time.fixedDeltaTime);
            }
            else if (Mathf.Sign(inputReader.HorizontalValue) != Mathf.Sign(currentSpeed))
            {
                desiredSpeed = Mathf.MoveTowards(currentSpeed, targetSpeed, turnAcceleration * Time.fixedDeltaTime);
            }
            else
            {
                desiredSpeed = Mathf.MoveTowards(currentSpeed, targetSpeed, acceleration * Time.fixedDeltaTime);
            }

            velocity.x = desiredSpeed;
            velocity.z = 0f;
            rb.linearVelocity = velocity;
        }
    }
}
