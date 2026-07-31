using UnityEngine;

namespace Qusap
{
    public sealed class QusapCameraFollow : MonoBehaviour
    {
        [SerializeField]
        private Transform target;

        [SerializeField]
        private Vector3 offset = new(0f, 1.5f, -10f);

        [SerializeField, Min(0f)]
        private float horizontalSmoothTime = 0.15f;

        [SerializeField, Min(0f)]
        private float verticalSmoothTime = 0.22f;

        private Vector3 horizontalVelocity;
        private Vector3 verticalVelocity;
        private Quaternion fixedRotation;

        private void OnEnable()
        {
            fixedRotation = transform.rotation;

            if (target == null)
            {
                Debug.LogWarning(
                    $"{nameof(QusapCameraFollow)} on '{gameObject.name}' needs a Target assigned in the Inspector.",
                    this);
            }
        }

        private void LateUpdate()
        {
            if (target == null)
            {
                return;
            }

            Vector3 currentPosition = transform.position;
            float targetX = target.position.x + offset.x;
            float targetY = target.position.y + offset.y;

            Vector3 horizontallySmoothedPosition = Vector3.SmoothDamp(
                currentPosition,
                new Vector3(targetX, currentPosition.y, currentPosition.z),
                ref horizontalVelocity,
                horizontalSmoothTime);

            Vector3 verticallySmoothedPosition = Vector3.SmoothDamp(
                currentPosition,
                new Vector3(currentPosition.x, targetY, currentPosition.z),
                ref verticalVelocity,
                verticalSmoothTime);

            transform.SetPositionAndRotation(
                new Vector3(
                    horizontallySmoothedPosition.x,
                    verticallySmoothedPosition.y,
                    offset.z),
                fixedRotation);
        }
    }
}
