using UnityEngine;

namespace Qusap
{
    [RequireComponent(typeof(Rigidbody))]
    [RequireComponent(typeof(QusapInputReader))]
    [RequireComponent(typeof(QusapGroundSensor))]
    public class QusapHorizontalMotor : MonoBehaviour
    {
        [SerializeField] private float maxSpeed = 7f;
        [SerializeField] private float acceleration = 45f;
        [SerializeField] private float deceleration = 60f;
        [SerializeField] private float turnAcceleration = 75f;
        [SerializeField] private float airAcceleration = 30f;
        [SerializeField] private float airDeceleration = 15f;
        [SerializeField] private float airTurnAcceleration = 40f;

        private Rigidbody rb;
        private QusapInputReader inputReader;
        private QusapGroundSensor groundSensor;

        private void Awake()
        {
            rb = GetComponent<Rigidbody>();
            inputReader = GetComponent<QusapInputReader>();
            groundSensor = GetComponent<QusapGroundSensor>();
        }

        private void OnValidate()
        {
            maxSpeed = Mathf.Max(maxSpeed, 0.0001f);
            acceleration = Mathf.Max(acceleration, 0f);
            deceleration = Mathf.Max(deceleration, 0f);
            turnAcceleration = Mathf.Max(turnAcceleration, 0f);
            airAcceleration = Mathf.Max(airAcceleration, 0f);
            airDeceleration = Mathf.Max(airDeceleration, 0f);
            airTurnAcceleration = Mathf.Max(airTurnAcceleration, 0f);
        }

        private void FixedUpdate()
        {
            if (inputReader == null || groundSensor == null || rb == null)
            {
                return;
            }

            Vector3 velocity = rb.linearVelocity;
            float currentSpeed = velocity.x;
            float targetSpeed = inputReader.HorizontalValue * maxSpeed;
            bool isGrounded = groundSensor.IsGrounded && velocity.y <= 0f;

            float accelerationRate;
            float decelerationRate;
            float turnAccelerationRate;

            if (isGrounded)
            {
                accelerationRate = acceleration;
                decelerationRate = deceleration;
                turnAccelerationRate = turnAcceleration;
            }
            else
            {
                accelerationRate = airAcceleration;
                decelerationRate = airDeceleration;
                turnAccelerationRate = airTurnAcceleration;
            }

            float desiredSpeed;
            if (Mathf.Approximately(inputReader.HorizontalValue, 0f))
            {
                desiredSpeed = Mathf.MoveTowards(currentSpeed, 0f, decelerationRate * Time.fixedDeltaTime);
            }
            else if (Mathf.Sign(inputReader.HorizontalValue) != Mathf.Sign(currentSpeed))
            {
                desiredSpeed = Mathf.MoveTowards(currentSpeed, targetSpeed, turnAccelerationRate * Time.fixedDeltaTime);
            }
            else
            {
                desiredSpeed = Mathf.MoveTowards(currentSpeed, targetSpeed, accelerationRate * Time.fixedDeltaTime);
            }

            velocity.x = desiredSpeed;
            velocity.z = 0f;
            rb.linearVelocity = velocity;
        }
    }
}
