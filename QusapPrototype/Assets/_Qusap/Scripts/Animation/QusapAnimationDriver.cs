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
        private static readonly int WallSlidingParameter = Animator.StringToHash("WallSliding");
        private static readonly int WallJumpingParameter = Animator.StringToHash("WallJumping");
        private static readonly int DashingParameter = Animator.StringToHash("Dashing");
        private static readonly int WallJumpState = Animator.StringToHash("Qusap_WallJump");
        private const float WallJumpFacingHold = 0.10f;
        private const float WallJumpVisualTimeout = 0.33f; // 0.30s clip + 0.03s entry blend.

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
        private QusapVerticalMotor verticalMotor;
        private QusapHorizontalMotor horizontalMotor;
        private QusapDashMotor dashMotor;
        private RuntimeAnimatorController cachedController;
        private bool hasWallSlidingParameter;
        private bool missingWallProviderReported;
        private bool missingDashProviderReported;
        private bool hasWallJumpingParameter;
        private uint observedWallJumpSequence;
        private bool wallJumping;
        private bool enteredWallJump;
        private float wallJumpStartedAt;
        private float heldWallJumpYaw;
        private bool hasDashingParameter;
        private bool wasDashing;
        private float dashFacingYaw;

        private void OnEnable()
        {
            // Do not replay a jump that occurred while the visual driver was disabled.
            var motor = GetComponent<QusapVerticalMotor>();
            observedWallJumpSequence = motor != null ? motor.WallJumpSequence : 0;
            wallJumping = false;
            enteredWallJump = false;
            wasDashing = false;
        }

        private void OnDisable()
        {
            wallJumping = false;
            if (animator != null && hasWallJumpingParameter)
                animator.SetBool(WallJumpingParameter, false);
            if (animator != null && hasDashingParameter)
                animator.SetBool(DashingParameter, false);
        }

        private void Awake()
        {
            rb = GetComponent<Rigidbody>();
            groundSensor = GetComponent<QusapGroundSensor>();
            inputReader = GetComponent<QusapInputReader>();
            verticalMotor = GetComponent<QusapVerticalMotor>();
            horizontalMotor = GetComponent<QusapHorizontalMotor>();
            dashMotor = GetComponent<QusapDashMotor>();

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
            CacheWallSlidingParameter();
            targetFacingYaw = playerVisual.localEulerAngles.y;
        }

        private void CacheWallSlidingParameter()
        {
            cachedController = animator.runtimeAnimatorController;
            hasWallSlidingParameter = false;
            hasWallJumpingParameter = false;
            hasDashingParameter = false;
            wallJumping = false;
            wasDashing = false;
            if (cachedController != null)
            {
                foreach (AnimatorControllerParameter parameter in animator.parameters)
                {
                    if (parameter.nameHash == WallSlidingParameter
                        && parameter.type == AnimatorControllerParameterType.Bool)
                    {
                        hasWallSlidingParameter = true;
                    }
                    if (parameter.nameHash == WallJumpingParameter
                        && parameter.type == AnimatorControllerParameterType.Bool)
                        hasWallJumpingParameter = true;
                    if (parameter.nameHash == DashingParameter
                        && parameter.type == AnimatorControllerParameterType.Bool)
                        hasDashingParameter = true;
                }
            }

            if (hasWallSlidingParameter && verticalMotor == null && !missingWallProviderReported)
            {
                Debug.LogError(
                    $"{nameof(QusapAnimationDriver)} requires {nameof(QusapVerticalMotor)} on '{gameObject.name}' to provide the real WallSliding state.",
                    this);
                missingWallProviderReported = true;
            }

            if (hasDashingParameter && dashMotor == null && !missingDashProviderReported)
            {
                Debug.LogError(
                    $"{nameof(QusapAnimationDriver)} requires {nameof(QusapDashMotor)} on '{gameObject.name}' to provide the real Dashing state.",
                    this);
                missingDashProviderReported = true;
            }
        }

        private void Update()
        {
            Vector3 velocity = rb.linearVelocity;
            float horizontalSpeed = Mathf.Abs(velocity.x);

            animator.SetFloat(SpeedParameter, horizontalSpeed, SpeedDampTime, Time.deltaTime);
            animator.SetFloat(VerticalSpeedParameter, velocity.y);
            animator.SetBool(GroundedParameter, groundSensor.IsGrounded);

            if (cachedController != animator.runtimeAnimatorController)
                CacheWallSlidingParameter();

            bool wallSliding = hasWallSlidingParameter && verticalMotor != null
                && verticalMotor.isActiveAndEnabled && verticalMotor.IsWallSliding;
            if (hasWallSlidingParameter)
                animator.SetBool(WallSlidingParameter, wallSliding);

            UpdateWallJumpVisual();

            bool dashing = hasDashingParameter && dashMotor != null
                && dashMotor.isActiveAndEnabled && dashMotor.IsDashing;
            if (hasDashingParameter)
                animator.SetBool(DashingParameter, dashing);

            if (dashing && !wasDashing && Mathf.Abs(velocity.x) > facingThreshold)
                dashFacingYaw = velocity.x > 0f ? rightFacingYaw : leftFacingYaw;
            wasDashing = dashing;

            float horizontalIntent = inputReader.HorizontalValue;
            if (dashing)
            {
                targetFacingYaw = dashFacingYaw;
            }
            else if (wallJumping && Time.time - wallJumpStartedAt < WallJumpFacingHold)
            {
                targetFacingYaw = heldWallJumpYaw;
            }
            else if (wallJumping && Mathf.Abs(velocity.x) > facingThreshold)
            {
                targetFacingYaw = velocity.x > 0f ? rightFacingYaw : leftFacingYaw;
            }
            else if (wallSliding)
            {
                targetFacingYaw = verticalMotor.WallSide > 0 ? rightFacingYaw : leftFacingYaw;
            }
            else if (hasWallSlidingParameter && horizontalMotor != null
                && horizontalMotor.IsWallJumpControlLocked && Mathf.Abs(velocity.x) > facingThreshold)
            {
                // The existing wall jump owns movement during its control lock.
                targetFacingYaw = velocity.x > 0f ? rightFacingYaw : leftFacingYaw;
            }
            else if (horizontalIntent > facingThreshold)
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

        private void UpdateWallJumpVisual()
        {
            if (verticalMotor == null)
                return;

            bool newWallJump = observedWallJumpSequence != verticalMotor.WallJumpSequence;
            observedWallJumpSequence = verticalMotor.WallJumpSequence;
            if (!hasWallJumpingParameter)
                return;

            if (newWallJump && verticalMotor.isActiveAndEnabled && !groundSensor.IsGrounded)
            {
                wallJumping = true;
                enteredWallJump = false;
                wallJumpStartedAt = Time.time;
                heldWallJumpYaw = playerVisual.localEulerAngles.y;
            }

            if (wallJumping)
            {
                bool inWallJump = animator.GetCurrentAnimatorStateInfo(0).shortNameHash == WallJumpState
                    || (animator.IsInTransition(0)
                        && animator.GetNextAnimatorStateInfo(0).shortNameHash == WallJumpState);
                if (inWallJump)
                    enteredWallJump = true;
                if (groundSensor.IsGrounded || !verticalMotor.isActiveAndEnabled
                    || (enteredWallJump && !inWallJump)
                    || Time.time - wallJumpStartedAt >= WallJumpVisualTimeout)
                    wallJumping = false;
            }

            animator.SetBool(WallJumpingParameter, wallJumping);
        }
    }
}
