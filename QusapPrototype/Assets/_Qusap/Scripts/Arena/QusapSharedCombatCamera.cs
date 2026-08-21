using UnityEngine;

namespace Qusap
{
    [DisallowMultipleComponent]
    [RequireComponent(typeof(Camera))]
    public sealed class QusapSharedCombatCamera : MonoBehaviour
    {
        [Header("Targets")]
        [SerializeField] private Transform playerOne;
        [SerializeField] private Transform playerTwo;

        [Header("Framing")]
        [SerializeField] private Vector2 framingPadding = new(3.5f, 2.5f);
        [SerializeField, Min(1f)] private float minimumOrthographicSize = 6f;
        [SerializeField, Min(1f)] private float maximumOrthographicSize = 10f;
        [SerializeField, Min(0f)] private float positionSmoothTime = 0.12f;
        [SerializeField, Min(0f)] private float sizeSmoothSpeed = 8f;
        [SerializeField] private float cameraDepth = -20f;
        [SerializeField] private Vector2 centerOffset = new(0f, 1.5f);

        private Camera targetCamera;
        private Vector3 smoothVelocity;

        private void Awake()
        {
            targetCamera = GetComponent<Camera>();
            targetCamera.orthographic = true;
        }

        private void LateUpdate()
        {
            if (playerOne == null || playerTwo == null || targetCamera == null)
            {
                return;
            }

            Vector3 midpoint = (playerOne.position + playerTwo.position) * 0.5f;
            Vector3 desiredPosition = new(
                midpoint.x + centerOffset.x,
                midpoint.y + centerOffset.y,
                cameraDepth);

            transform.position = positionSmoothTime <= 0f
                ? desiredPosition
                : Vector3.SmoothDamp(transform.position, desiredPosition, ref smoothVelocity, positionSmoothTime);

            float verticalSize = Mathf.Abs(playerOne.position.y - playerTwo.position.y) * 0.5f + framingPadding.y;
            float horizontalSize = (Mathf.Abs(playerOne.position.x - playerTwo.position.x) * 0.5f + framingPadding.x)
                / Mathf.Max(targetCamera.aspect, 0.01f);
            float desiredSize = Mathf.Clamp(
                Mathf.Max(verticalSize, horizontalSize),
                minimumOrthographicSize,
                maximumOrthographicSize);

            targetCamera.orthographicSize = sizeSmoothSpeed <= 0f
                ? desiredSize
                : Mathf.MoveTowards(
                    targetCamera.orthographicSize,
                    desiredSize,
                    sizeSmoothSpeed * Time.deltaTime);
        }

        private void OnValidate()
        {
            framingPadding.x = Mathf.Max(framingPadding.x, 0f);
            framingPadding.y = Mathf.Max(framingPadding.y, 0f);
            minimumOrthographicSize = Mathf.Max(minimumOrthographicSize, 1f);
            maximumOrthographicSize = Mathf.Max(maximumOrthographicSize, minimumOrthographicSize);
            positionSmoothTime = Mathf.Max(positionSmoothTime, 0f);
            sizeSmoothSpeed = Mathf.Max(sizeSmoothSpeed, 0f);
        }

        public void Configure(Transform firstPlayer, Transform secondPlayer)
        {
            playerOne = firstPlayer;
            playerTwo = secondPlayer;
        }
    }
}
