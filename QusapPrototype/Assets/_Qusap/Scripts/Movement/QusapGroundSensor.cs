using UnityEngine;

namespace Qusap
{
    [RequireComponent(typeof(CapsuleCollider))]
    [RequireComponent(typeof(Rigidbody))]
    public class QusapGroundSensor : MonoBehaviour
    {
        [SerializeField] private float minimumGroundNormalY = 0.6f;

        public bool IsGrounded { get; private set; }

        private void OnCollisionStay(Collision collision)
        {
            IsGrounded = false;

            foreach (ContactPoint contact in collision.contacts)
            {
                if (contact.normal.y >= minimumGroundNormalY)
                {
                    IsGrounded = true;
                    return;
                }
            }
        }

        private void OnCollisionExit(Collision collision)
        {
            IsGrounded = false;
        }
    }
}
