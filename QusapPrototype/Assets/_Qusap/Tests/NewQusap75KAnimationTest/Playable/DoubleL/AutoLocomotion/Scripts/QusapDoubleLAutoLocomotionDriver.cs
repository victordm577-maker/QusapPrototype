using UnityEngine;
using UnityEngine.InputSystem;

namespace Qusap.NewQusap75KAnimationTest.Playable.DoubleL.AutoLocomotion
{
    [DisallowMultipleComponent]
    [RequireComponent(typeof(Rigidbody))]
    public sealed class QusapDoubleLAutoLocomotionDriver : MonoBehaviour
    {
        private static readonly int IsMovingId = Animator.StringToHash("IsMoving");
        private static readonly int RunPlaybackSpeedId = Animator.StringToHash("RunPlaybackSpeed");
        private static readonly int PlayRollId = Animator.StringToHash("PlayRoll");

        [SerializeField] private Rigidbody targetRigidbody;
        [SerializeField] private Animator animator;
        [SerializeField, Min(0f)] private float enterRunThreshold = 0.10f;
        [SerializeField, Min(0f)] private float exitRunThreshold = 0.05f;
        [SerializeField, Min(0.01f)] private float runPlaybackSpeed = 1.0f;

        private bool isMoving;

        public Rigidbody TargetRigidbody => targetRigidbody;
        public Animator Animator => animator;
        public float EnterRunThreshold => enterRunThreshold;
        public float ExitRunThreshold => exitRunThreshold;
        public float RunPlaybackSpeed => runPlaybackSpeed;
        public bool IsMoving => isMoving;
        public float HorizontalSpeed => targetRigidbody != null
            ? Mathf.Abs(targetRigidbody.linearVelocity.x)
            : 0f;

        public void Configure(Rigidbody body, Animator targetAnimator)
        {
            targetRigidbody = body;
            animator = targetAnimator;
            if (animator != null)
            {
                animator.applyRootMotion = false;
            }
            RefreshFromRigidbody();
        }

        private void Awake()
        {
            targetRigidbody ??= GetComponent<Rigidbody>();
            animator ??= GetComponentInChildren<Animator>(true);
            if (animator != null)
            {
                animator.applyRootMotion = false;
            }
            RefreshFromRigidbody();
        }

        private void OnValidate()
        {
            enterRunThreshold = Mathf.Max(0f, enterRunThreshold);
            exitRunThreshold = Mathf.Clamp(exitRunThreshold, 0f, enterRunThreshold);
            runPlaybackSpeed = Mathf.Max(0.01f, runPlaybackSpeed);
            if (animator != null)
            {
                animator.applyRootMotion = false;
            }
        }

        private void Update()
        {
            RefreshFromRigidbody();

            bool keyboardRoll = Keyboard.current != null && Keyboard.current.rKey.wasPressedThisFrame;
            bool gamepadRoll = Gamepad.current != null && Gamepad.current.dpad.up.wasPressedThisFrame;
            if (keyboardRoll || gamepadRoll)
            {
                RequestRoll();
            }
        }

        public void RefreshFromRigidbody()
        {
            if (targetRigidbody == null || animator == null)
            {
                return;
            }

            float speed = Mathf.Abs(targetRigidbody.linearVelocity.x);
            if (isMoving)
            {
                if (speed <= exitRunThreshold)
                {
                    isMoving = false;
                }
            }
            else if (speed >= enterRunThreshold)
            {
                isMoving = true;
            }

            animator.SetBool(IsMovingId, isMoving);
            animator.SetFloat(RunPlaybackSpeedId, runPlaybackSpeed);
        }

        public void RequestRoll()
        {
            if (animator != null)
            {
                animator.SetTrigger(PlayRollId);
            }
        }

        private void OnGUI()
        {
            GUI.Box(new Rect(16f, 16f, 470f, 120f), string.Empty);
            GUI.Label(new Rect(30f, 26f, 445f, 100f),
                "Qusap75K - DoubleL Auto Locomotion (prueba aislada)\n" +
                "Idle B1 / Run Forward segun velocidad horizontal real\n" +
                "R o D-pad arriba: rodada\n" +
                "Movimiento, salto y dash: controles normales del proyecto\n" +
                "Apply Root Motion: OFF");
        }
    }
}
