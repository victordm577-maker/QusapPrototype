using System.Linq;
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
        private static readonly int CombatAnimatingParameter = Animator.StringToHash("CombatAnimating");
        private static readonly int AttackVariantParameter = Animator.StringToHash("AttackVariant");
        private static readonly int WallJumpState = Animator.StringToHash("Qusap_WallJump");
        private const float WallJumpFacingHold = 0.10f;
        private const float WallJumpVisualTimeout = 0.33f; // 0.30s clip + 0.03s entry blend.

        [SerializeField] private float rightFacingYaw = 150f;
        [SerializeField] private float leftFacingYaw = 210f;
        [SerializeField] private float turnSpeedDegrees = 720f;
        [SerializeField] private float facingThreshold = 0.05f;
        [SerializeField] private Animator animator;
        [SerializeField] private Transform playerVisual;

        private Rigidbody rb;
        private QusapGroundSensor groundSensor;
        private QusapInputReader inputReader;
        private float targetFacingYaw;
        private QusapVerticalMotor verticalMotor;
        private QusapHorizontalMotor horizontalMotor;
        private QusapDashMotor dashMotor;
        private QusapCombatController combatController;
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
        private bool hasCombatAnimatingParameter;
        private bool hasAttackVariantParameter;
        private bool combatFacingLocked;
        private float combatFacingYaw;
        private bool invalidAnimatorReported;

        private void OnEnable()
        {
            // Do not replay a jump that occurred while the visual driver was disabled.
            var motor = GetComponent<QusapVerticalMotor>();
            observedWallJumpSequence = motor != null ? motor.WallJumpSequence : 0;
            wallJumping = false;
            enteredWallJump = false;
            wasDashing = false;
            SubscribeToCombatEvents();
            SynchronizeCombatAnimation();
        }

        private void OnDisable()
        {
            UnsubscribeFromCombatEvents();
            ClearCombatAnimation();
            wallJumping = false;
            if (HasValidAnimatorController(animator) && hasWallJumpingParameter)
                animator.SetBool(WallJumpingParameter, false);
            if (HasValidAnimatorController(animator) && hasDashingParameter)
                animator.SetBool(DashingParameter, false);
        }

        private void Awake()
        {
            if (!enabled)
                return;

            rb = GetComponent<Rigidbody>();
            groundSensor = GetComponent<QusapGroundSensor>();
            inputReader = GetComponent<QusapInputReader>();
            verticalMotor = GetComponent<QusapVerticalMotor>();
            horizontalMotor = GetComponent<QusapHorizontalMotor>();
            dashMotor = GetComponent<QusapDashMotor>();
            combatController = GetComponent<QusapCombatController>();

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

            if (!TryResolveAnimator())
            {
                enabled = false;
                return;
            }

            animator.applyRootMotion = false;
            CacheAnimatorParameters();
            targetFacingYaw = playerVisual.localEulerAngles.y;
        }

        private void CacheAnimatorParameters()
        {
            if (!HasValidAnimatorController(animator))
            {
                cachedController = null;
                hasWallSlidingParameter = false;
                hasWallJumpingParameter = false;
                hasDashingParameter = false;
                hasCombatAnimatingParameter = false;
                hasAttackVariantParameter = false;
                return;
            }

            cachedController = animator.runtimeAnimatorController;
            hasWallSlidingParameter = false;
            hasWallJumpingParameter = false;
            hasDashingParameter = false;
            hasCombatAnimatingParameter = false;
            hasAttackVariantParameter = false;
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
                    if (parameter.nameHash == CombatAnimatingParameter
                        && parameter.type == AnimatorControllerParameterType.Bool)
                        hasCombatAnimatingParameter = true;
                    if (parameter.nameHash == AttackVariantParameter
                        && parameter.type == AnimatorControllerParameterType.Int)
                        hasAttackVariantParameter = true;
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

            SynchronizeCombatAnimation();
        }

        private void Update()
        {
            if (!EnsureAnimatorReady())
                return;

            Vector3 velocity = rb.linearVelocity;
            float horizontalSpeed = Mathf.Abs(velocity.x);

            animator.SetFloat(SpeedParameter, horizontalSpeed, SpeedDampTime, Time.deltaTime);
            animator.SetFloat(VerticalSpeedParameter, velocity.y);
            animator.SetBool(GroundedParameter, groundSensor.IsGrounded);

            if (cachedController != animator.runtimeAnimatorController)
                CacheAnimatorParameters();

            if (combatFacingLocked && (combatController == null
                || !IsAnimatedCombatKick(combatController.CurrentAttackVariant)
                || combatController.CurrentPhase == QusapAttackPhase.Idle))
                ClearCombatAnimation();

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
            if (combatFacingLocked)
            {
                targetFacingYaw = combatFacingYaw;
            }
            else if (dashing)
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

        private void SubscribeToCombatEvents()
        {
            if (combatController == null)
                combatController = GetComponent<QusapCombatController>();
            if (combatController == null)
                return;

            UnsubscribeFromCombatEvents();
            combatController.AttackVariantStarted += HandleAttackVariantStarted;
            combatController.AttackPhaseChanged += HandleAttackPhaseChanged;
            combatController.AttackEnded += HandleAttackEnded;
        }

        private void UnsubscribeFromCombatEvents()
        {
            if (combatController == null)
                return;
            combatController.AttackVariantStarted -= HandleAttackVariantStarted;
            combatController.AttackPhaseChanged -= HandleAttackPhaseChanged;
            combatController.AttackEnded -= HandleAttackEnded;
        }

        private void HandleAttackVariantStarted(QusapAttackVariant variant)
        {
            if (!IsAnimatedCombatKick(variant))
            {
                ClearCombatAnimation();
                return;
            }

            int direction = combatController != null ? combatController.AttackDirection : 0;
            combatFacingYaw = direction > 0 ? rightFacingYaw
                : direction < 0 ? leftFacingYaw
                : playerVisual.localEulerAngles.y;
            targetFacingYaw = combatFacingYaw;
            combatFacingLocked = true;
            SetCombatParameters(true, variant);
        }

        private void HandleAttackPhaseChanged(QusapAttackVariant variant, QusapAttackPhase phase)
        {
            if (phase == QusapAttackPhase.Idle || !IsAnimatedCombatKick(variant))
                ClearCombatAnimation();
        }

        private void HandleAttackEnded(QusapAttackVariant variant, bool canceled)
        {
            if (IsAnimatedCombatKick(variant) || combatFacingLocked)
                ClearCombatAnimation();
        }

        private void SynchronizeCombatAnimation()
        {
            if (combatController != null && combatController.CurrentPhase != QusapAttackPhase.Idle
                && IsAnimatedCombatKick(combatController.CurrentAttackVariant))
            {
                if (!combatFacingLocked)
                {
                    int direction = combatController.AttackDirection;
                    combatFacingYaw = direction > 0 ? rightFacingYaw
                        : direction < 0 ? leftFacingYaw
                        : playerVisual != null ? playerVisual.localEulerAngles.y : targetFacingYaw;
                    combatFacingLocked = true;
                }
                SetCombatParameters(true, combatController.CurrentAttackVariant);
                return;
            }

            ClearCombatAnimation();
        }

        private void ClearCombatAnimation()
        {
            combatFacingLocked = false;
            SetCombatParameters(false, QusapAttackVariant.None);
        }

        private void SetCombatParameters(bool active, QusapAttackVariant variant)
        {
            if (!HasValidAnimatorController(animator))
                return;
            if (hasCombatAnimatingParameter)
                animator.SetBool(CombatAnimatingParameter, active);
            if (hasAttackVariantParameter)
                animator.SetInteger(AttackVariantParameter, (int)variant);
        }

        private static bool IsAnimatedWeakKick(QusapAttackVariant variant)
        {
            return variant == QusapAttackVariant.WeakKickGround
                || variant == QusapAttackVariant.WeakKickAir;
        }

        private static bool IsAnimatedCombatKick(QusapAttackVariant variant)
        {
            return IsAnimatedWeakKick(variant)
                || variant == QusapAttackVariant.StrongKickGround
                || variant == QusapAttackVariant.StrongKickAir;
        }

        private bool EnsureAnimatorReady()
        {
            if (HasValidAnimatorController(animator)
                && playerVisual != null && playerVisual.gameObject.activeInHierarchy)
                return true;
            return TryResolveAnimator();
        }

        private bool TryResolveAnimator()
        {
            Animator[] candidates = GetComponentsInChildren<Animator>(true)
                .Where(IsValidVisualAnimator).ToArray();
            if (candidates.Length == 1)
            {
                animator = candidates[0];
                playerVisual = DirectChildUnderRoot(animator.transform);
                invalidAnimatorReported = false;
                return true;
            }

            if (!invalidAnimatorReported)
            {
                Animator[] found = GetComponentsInChildren<Animator>(true);
                string inventory = found.Length == 0
                    ? "ningún Animator"
                    : string.Join("; ", found.Select(item =>
                        $"{HierarchyPath(item.transform)} [active={item.gameObject.activeInHierarchy}, "
                        + $"enabled={item.enabled}, controller="
                        + $"{(item.runtimeAnimatorController != null ? item.runtimeAnimatorController.name : "null")}, "
                        + $"avatar={(item.avatar != null ? item.avatar.name : "null")}, "
                        + $"avatarValid={(item.avatar != null && item.avatar.isValid)}]"));
                Debug.LogError(
                    $"{nameof(QusapAnimationDriver)} on '{HierarchyPath(transform)}' requires exactly one active, "
                    + "enabled Animator with a RuntimeAnimatorController and valid Avatar under the active "
                    + $"PlayerVisual. Valid candidates: {candidates.Length}. Found: {inventory}.",
                    this);
                invalidAnimatorReported = true;
            }
            return false;
        }

        private bool IsValidVisualAnimator(Animator candidate)
        {
            if (!HasValidAnimatorController(candidate) || candidate.avatar == null
                || !candidate.avatar.isValid || !candidate.gameObject.activeInHierarchy)
                return false;
            Transform visualRoot = DirectChildUnderRoot(candidate.transform);
            return visualRoot != null && visualRoot.name == "PlayerVisual"
                && visualRoot.gameObject.activeInHierarchy;
        }

        private static bool HasValidAnimatorController(Animator candidate)
        {
            return candidate != null && candidate.enabled
                && candidate.runtimeAnimatorController != null;
        }

        private Transform DirectChildUnderRoot(Transform descendant)
        {
            if (descendant == null)
                return null;
            Transform current = descendant;
            while (current.parent != null && current.parent != transform)
                current = current.parent;
            return current.parent == transform ? current : null;
        }

        private static string HierarchyPath(Transform item)
        {
            if (item == null)
                return "<null>";
            string path = item.name;
            for (Transform parent = item.parent; parent != null; parent = parent.parent)
                path = parent.name + "/" + path;
            return path;
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
