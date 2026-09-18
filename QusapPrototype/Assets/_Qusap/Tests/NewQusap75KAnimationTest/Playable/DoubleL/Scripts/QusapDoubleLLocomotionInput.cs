using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.Controls;

namespace Qusap.NewQusap75KAnimationTest.Playable.DoubleL
{
    [DisallowMultipleComponent]
    public sealed class QusapDoubleLLocomotionInput : MonoBehaviour
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
            if (keyboard == null)
            {
                return;
            }

            if (Pressed(keyboard.digit1Key, keyboard.numpad1Key))
            {
                PlayIdle(1, "PlayIdleB1");
            }
            else if (Pressed(keyboard.digit2Key, keyboard.numpad2Key))
            {
                PlayIdle(2, "PlayIdleB2");
            }
            else if (Pressed(keyboard.digit3Key, keyboard.numpad3Key))
            {
                PlayIdle(3, "PlayIdleB3");
            }
            else if (Pressed(keyboard.digit4Key, keyboard.numpad4Key))
            {
                animator.SetTrigger("PlayWalkForward");
            }
            else if (Pressed(keyboard.digit5Key, keyboard.numpad5Key))
            {
                animator.SetTrigger("PlayWalkBackward");
            }
            else if (Pressed(keyboard.digit6Key, keyboard.numpad6Key))
            {
                animator.SetTrigger("PlayRunForward");
            }
            else if (Pressed(keyboard.digit7Key, keyboard.numpad7Key))
            {
                animator.SetTrigger("PlayRunBackward");
            }
            else if (Pressed(keyboard.digit8Key, keyboard.numpad8Key))
            {
                animator.SetTrigger("PlayTurnLeft90");
            }
            else if (Pressed(keyboard.digit9Key, keyboard.numpad9Key))
            {
                animator.SetTrigger("PlayTurnRight90");
            }
            else if (keyboard.rKey.wasPressedThisFrame)
            {
                animator.SetTrigger("PlayRoll");
            }
        }

        private void PlayIdle(int selection, string trigger)
        {
            animator.SetInteger("SelectedIdle", selection);
            animator.SetTrigger(trigger);
        }

        private static bool Pressed(KeyControl main, KeyControl numpad)
        {
            return main.wasPressedThisFrame || numpad.wasPressedThisFrame;
        }

        private void OnGUI()
        {
            const float width = 430f;
            const float height = 235f;
            Rect box = new Rect(16f, 16f, width, height);
            GUI.Box(box, string.Empty);
            GUI.Label(new Rect(30f, 25f, width - 24f, height - 20f),
                "Qusap75K — DoubleL One Hand Base (prueba aislada)\n" +
                "1  Combat Idle B1     2  Combat Idle B2     3  Combat Idle B3\n" +
                "4  Walk Forward       5  Walk Backward\n" +
                "6  Run Forward        7  Run Backward\n" +
                "8  Turn Left 90°      9  Turn Right 90°\n" +
                "R  Rodada anterior (comparación)\n\n" +
                "Movimiento y salto: controles normales del proyecto\n" +
                "Apply Root Motion: OFF · Sin ataques · Espada de prueba");
        }
    }
}
