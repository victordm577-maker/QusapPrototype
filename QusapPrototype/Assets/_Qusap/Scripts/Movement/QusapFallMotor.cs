using UnityEngine;

namespace Qusap
{
    [RequireComponent(typeof(Rigidbody))]
    public class QusapFallMotor : MonoBehaviour
    {
        [SerializeField] private float fallGravityMultiplier = 2.5f;
        [SerializeField] private float maximumFallSpeed = 18f;

        private Rigidbody rb;

        private void Awake()
        {
            rb = GetComponent<Rigidbody>();
        }

        private void OnValidate()
        {
            fallGravityMultiplier = Mathf.Max(fallGravityMultiplier, 1f);
            maximumFallSpeed = Mathf.Max(maximumFallSpeed, 0.0001f);
        }

        private void FixedUpdate()
        {
            if (rb == null)
            {
                return;
            }

            Vector3 velocity = rb.linearVelocity;

            if (velocity.y < 0f)
            {
                float adjustedGravity = Physics.gravity.y * (Mathf.Max(fallGravityMultiplier, 1f) - 1f);
                velocity.y += adjustedGravity * Time.fixedDeltaTime;
                velocity.y = Mathf.Max(velocity.y, -maximumFallSpeed);
            }

            velocity.z = 0f;
            rb.linearVelocity = velocity;
        }
    }
}
