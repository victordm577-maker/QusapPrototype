using UnityEngine;

namespace Qusap
{
    [RequireComponent(typeof(Rigidbody))]
    [RequireComponent(typeof(QusapDashMotor))]
    public class QusapRespawnController : MonoBehaviour
    {
        [SerializeField] private float fallLimitY = -8f;
        [SerializeField] private Transform optionalRespawnPoint;

        private Rigidbody rb;
        private QusapDashMotor dashMotor;
        private Vector3 initialPosition;
        private Quaternion initialRotation;

        private void Awake()
        {
            rb = GetComponent<Rigidbody>();
            dashMotor = GetComponent<QusapDashMotor>();
            initialPosition = rb.position;
            initialRotation = rb.rotation;
        }

        private void FixedUpdate()
        {
            if (rb.position.y < fallLimitY)
            {
                Respawn();
            }
        }

        public void Respawn()
        {
            dashMotor?.ResetDashState();

            Vector3 respawnPosition = optionalRespawnPoint != null
                ? optionalRespawnPoint.position
                : initialPosition;
            Quaternion respawnRotation = optionalRespawnPoint != null
                ? optionalRespawnPoint.rotation
                : initialRotation;

            rb.position = respawnPosition;
            rb.rotation = respawnRotation;
            rb.linearVelocity = Vector3.zero;
            rb.angularVelocity = Vector3.zero;
            rb.WakeUp();
        }
    }
}
