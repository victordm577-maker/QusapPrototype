using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.Controls;

namespace Qusap.NewQusap75KAnimationTest.Playable.DoubleL.SwordIntegration
{
    [DisallowMultipleComponent]
    public sealed class QusapSwordIdleComparisonController : MonoBehaviour
    {
        private static readonly string[] StateNames =
        {
            "CombatIdle_B1",
            "CombatIdle_B2",
            "CombatIdle_B3"
        };

        [SerializeField] private Animator animator;
        [SerializeField, Range(1, 3)] private int selectedIdle = 1;

        public Animator Animator => animator;
        public int SelectedIdle => selectedIdle;

        public void Configure(Animator targetAnimator)
        {
            animator = targetAnimator;
            if (animator != null) animator.applyRootMotion = false;
        }

        private void Awake()
        {
            animator ??= GetComponentInChildren<Animator>(true);
            if (animator != null) animator.applyRootMotion = false;
        }

        private void Start() => SelectIdle(selectedIdle, true);

        private void Update()
        {
            Keyboard keyboard = Keyboard.current;
            if (keyboard == null) return;

            if (Pressed(keyboard.digit1Key, keyboard.numpad1Key)) SelectIdle(1);
            else if (Pressed(keyboard.digit2Key, keyboard.numpad2Key)) SelectIdle(2);
            else if (Pressed(keyboard.digit3Key, keyboard.numpad3Key)) SelectIdle(3);
        }

        public void SelectIdle(int selection, bool immediate = false)
        {
            if (animator == null || selection < 1 || selection > 3) return;
            selectedIdle = selection;
            string stateName = StateNames[selection - 1];
            if (immediate) animator.Play(stateName, 0, 0f);
            else animator.CrossFadeInFixedTime(stateName, 0.05f, 0, 0f);
        }

        public static string StateNameFor(int selection) =>
            selection >= 1 && selection <= 3 ? StateNames[selection - 1] : string.Empty;

        private static bool Pressed(KeyControl main, KeyControl numpad) =>
            main.wasPressedThisFrame || numpad.wasPressedThisFrame;

        private void OnGUI()
        {
            GUI.Box(new Rect(16f, 16f, 465f, 92f), string.Empty);
            GUI.Label(new Rect(30f, 26f, 440f, 72f),
                "SwordIntegration - comparación de Idle DoubleL\n" +
                "1: Idle B1     2: Idle B2     3: Idle B3\n" +
                $"Activo: Idle B{selectedIdle} · Loop · Root Motion OFF");
        }
    }
}
