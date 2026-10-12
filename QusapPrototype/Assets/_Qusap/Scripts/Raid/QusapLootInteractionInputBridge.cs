using UnityEngine;
using UnityEngine.InputSystem;

namespace Qusap
{
    // Attach only to the isolated scene. No InputAction asset or native input is changed.
    public sealed class QusapLootInteractionInputBridge : MonoBehaviour, IQusapLootInteractionInput
    {
        [SerializeField] private QusapLocalPlayerSlot deviceSlot;
        public string BindingLabel => deviceSlot == QusapLocalPlayerSlot.Player1Keyboard ? "F | [ ] elegir | T stack | Y unidad | C cerrar" : "D-pad arriba | HUD elegir/tomar/cerrar";
        public void Configure(QusapLocalPlayerSlot slot) => deviceSlot = slot;
        public QusapLootInteractionCommand ReadCommand()
        {
            if (!isActiveAndEnabled) return default;
            if (deviceSlot == QusapLocalPlayerSlot.Player1Keyboard)
            {
                var key = Keyboard.current; if (key == null) return default;
                return new(key.fKey.isPressed, (key.rightBracketKey.wasPressedThisFrame ? 1 : 0) - (key.leftBracketKey.wasPressedThisFrame ? 1 : 0),
                    key.yKey.wasPressedThisFrame, key.tKey.wasPressedThisFrame, key.cKey.wasPressedThisFrame);
            }
            var pad = Gamepad.all.Count > 0 ? Gamepad.all[0] : null;
            return new(pad != null && pad.dpad.up.isPressed);
        }
    }
}
