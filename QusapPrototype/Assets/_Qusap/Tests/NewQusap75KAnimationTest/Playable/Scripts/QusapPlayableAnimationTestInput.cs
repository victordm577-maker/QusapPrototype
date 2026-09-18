using UnityEngine;
using UnityEngine.InputSystem;

namespace Qusap.NewQusap75KAnimationTest.Playable
{
    [DisallowMultipleComponent]
    public sealed class QusapPlayableAnimationTestInput : MonoBehaviour
    {
        [SerializeField] private Animator animator;

        public Animator Animator => animator;

        public void Configure(Animator targetAnimator)
        {
            animator = targetAnimator;
        }

        private void Update()
        {
            if (animator == null)
            {
                return;
            }

            Keyboard keyboard = Keyboard.current;
            if (keyboard != null)
            {
                if (keyboard.digit1Key.wasPressedThisFrame || keyboard.numpad1Key.wasPressedThisFrame)
                {
                    animator.SetTrigger("PlayIdle");
                }
                else if (keyboard.digit2Key.wasPressedThisFrame || keyboard.numpad2Key.wasPressedThisFrame)
                {
                    animator.SetTrigger("PlayWalk");
                }
                else if (keyboard.digit3Key.wasPressedThisFrame || keyboard.numpad3Key.wasPressedThisFrame)
                {
                    animator.SetTrigger("PlayRun");
                }
                else if (keyboard.digit4Key.wasPressedThisFrame || keyboard.numpad4Key.wasPressedThisFrame)
                {
                    animator.SetTrigger("PlayJumping");
                }
                else if (keyboard.digit5Key.wasPressedThisFrame || keyboard.numpad5Key.wasPressedThisFrame)
                {
                    animator.SetTrigger("PlayDying");
                }
                else if (keyboard.rKey.wasPressedThisFrame)
                {
                    animator.SetTrigger("PlayRoll");
                }
            }

            if (Gamepad.current != null && Gamepad.current.dpad.up.wasPressedThisFrame)
            {
                animator.SetTrigger("PlayRoll");
            }
        }
    }
}
