using UnityEngine;

namespace Qusap
{
    [RequireComponent(typeof(Rigidbody))]
    public sealed class QusapAnimationDriver : MonoBehaviour
    {
        private const float SpeedDampTime = 0.08f;
        private static readonly int SpeedParameter = Animator.StringToHash("Speed");
        private static readonly int VerticalSpeedParameter = Animator.StringToHash("VerticalSpeed");
        private static readonly int GroundedParameter = Animator.StringToHash("Grounded");

        [SerializeField] private float rightFacingYaw = 150f;
        [SerializeField] private float leftFacingYaw = 210f;
        [SerializeField] private float turnSpeedDegrees = 720f;
        [SerializeField] private float facingThreshold = 0.05f;

        private Rigidbody rb;
        private Animator animator;
        private QusapGroundSensor groundSensor;
        private QusapInputReader inputReader;
        private Transform playerVisual;
        private float targetFacingYaw;

        private void Awake()
        {
            rb = GetComponent<Rigidbody>();
            groundSensor = GetComponent<QusapGroundSensor>();
            inputReader = GetComponent<QusapInputReader>();

            if (rb == null)
            {
                Debug.LogError(
                    $"{nameof(QusapAnimationDriver)} requires a Rigidbody on '{gameObject.name}'.",
                    this);
                enabled = false;
                return;
            }

            if (groundSensor == null)
            {
                Debug.LogError(
                    $"{nameof(QusapAnimationDriver)} requires a {nameof(QusapGroundSensor)} on '{gameObject.name}' to provide the Grounded state.",
                    this);
                enabled = false;
                return;
            }

            if (inputReader == null)
            {
                Debug.LogError(
                    $"{nameof(QusapAnimationDriver)} requires a {nameof(QusapInputReader)} on '{gameObject.name}' to provide horizontal movement intent.",
                    this);
                enabled = false;
                return;
            }

            playerVisual = transform.Find("PlayerVisual");
            if (playerVisual == null || !playerVisual.gameObject.activeInHierarchy)
            {
                Debug.LogError(
                    $"{nameof(QusapAnimationDriver)} could not find an active child named 'PlayerVisual' on '{gameObject.name}'.",
                    this);
                enabled = false;
                return;
            }

            animator = playerVisual.GetComponent<Animator>();
            if (animator == null)
            {
                Debug.LogError(
                    $"{nameof(QusapAnimationDriver)} requires an Animator on the child 'PlayerVisual'.",
                    this);
                enabled = false;
                return;
            }

            animator.applyRootMotion = false;
            targetFacingYaw = playerVisual.localEulerAngles.y;
        }

        private void Update()
        {
            Vector3 velocity = rb.linearVelocity;
            float horizontalSpeed = Mathf.Abs(velocity.x);

            animator.SetFloat(SpeedParameter, horizontalSpeed, SpeedDampTime, Time.deltaTime);
            animator.SetFloat(VerticalSpeedParameter, velocity.y);
            animator.SetBool(GroundedParameter, groundSensor.IsGrounded);

            float horizontalIntent = inputReader.HorizontalValue;
            if (horizontalIntent > facingThreshold)
            {
                targetFacingYaw = rightFacingYaw;
            }
            else if (horizontalIntent < -facingThreshold)
            {
                targetFacingYaw = leftFacingYaw;
            }

            Vector3 localEulerAngles = playerVisual.localEulerAngles;
            localEulerAngles.y = Mathf.MoveTowardsAngle(
                localEulerAngles.y,
                targetFacingYaw,
                turnSpeedDegrees * Time.deltaTime);
            playerVisual.localEulerAngles = localEulerAngles;
        }
    }
}
