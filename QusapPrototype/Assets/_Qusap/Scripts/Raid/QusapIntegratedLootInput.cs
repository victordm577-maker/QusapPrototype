using UnityEngine;
using UnityEngine.InputSystem;

namespace Qusap
{
    // Extra scene-local commands; the native gameplay actions and device pairing remain unchanged.
    public sealed class QusapIntegratedLootInput : MonoBehaviour, IQusapLootInteractionInput
    {
        private QusapLocalPlayerSlot slot;
        private int previousDirection;
        private System.Func<bool> available;
        public void SetAvailable(System.Func<bool> value) => available = value;
        public string BindingLabel => slot == QusapLocalPlayerSlot.Player1Keyboard
            ? "F mantener · [ ] elegir · T stack · Y unidad · C cerrar" : "D-pad ↑ mantener · stick derecho ←/→ elegir · R3 stack";
        public void Configure(QusapLocalPlayerSlot player) => slot = player;
        public static bool InteractHeld(QusapLocalPlayerSlot player) => player == QusapLocalPlayerSlot.Player1Keyboard
            ? Keyboard.current?.fKey.isPressed == true : Gamepad.all.Count > 0 && Gamepad.all[0].dpad.up.isPressed;
        public QusapLootInteractionCommand ReadCommand()
        {
            if (!isActiveAndEnabled || available?.Invoke() == false) return default;
            if (slot == QusapLocalPlayerSlot.Player1Keyboard)
            {
                var key = Keyboard.current; if (key == null) return default;
                return new(key.fKey.isPressed, (key.rightBracketKey.wasPressedThisFrame ? 1 : 0) - (key.leftBracketKey.wasPressedThisFrame ? 1 : 0),
                    key.yKey.wasPressedThisFrame, key.tKey.wasPressedThisFrame, key.cKey.wasPressedThisFrame);
            }
            var pad = Gamepad.all.Count > 0 ? Gamepad.all[0] : null; if (pad == null) return default;
            int direction = pad.rightStick.x.ReadValue() > .6f ? 1 : pad.rightStick.x.ReadValue() < -.6f ? -1 : 0;
            int delta = direction != previousDirection ? direction : 0; previousDirection = direction;
            return new(pad.dpad.up.isPressed, delta, false, pad.rightStickButton.wasPressedThisFrame);
        }
    }
}
