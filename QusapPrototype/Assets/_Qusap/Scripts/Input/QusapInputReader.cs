using UnityEngine;
using UnityEngine.InputSystem;

namespace Qusap
{
    public enum QusapLocalPlayerSlot
    {
        Player1Keyboard,
        Player2Gamepad
    }

    public class QusapInputReader : MonoBehaviour
    {
        [SerializeField] private InputActionAsset inputActionAsset;
        [SerializeField] private QusapLocalPlayerSlot localPlayerSlot = QusapLocalPlayerSlot.Player1Keyboard;
        [SerializeField, Min(1)] private int combatCommandBufferCapacity = 32;

        private InputActionAsset runtimeActionAsset;
        private InputAction moveAction;
        private InputAction jumpAction;
        private InputAction dropAction;
        private InputAction dashAction;
        private InputAction weakKickAction;
        private InputAction strongKickAction;
        private InputAction headbuttAction;
        private InputAction weaponStrongAction;
        private InputAction parryAction;
        private QusapCombatCommandBuffer combatCommandBuffer;
        private float horizontalValue;
        private bool jumpPressed;
        private bool jumpReleased;
        private bool dropPressed;
        private bool dashPressed;
        private bool weakKickPressed;
        private bool strongKickPressed;
        private bool headbuttPressed;
        private bool gameplayInputBlocked;

        public float HorizontalValue => gameplayInputBlocked ? 0f : horizontalValue;
        public QusapLocalPlayerSlot LocalPlayerSlot => localPlayerSlot;
        public int PendingCombatCommandCount => combatCommandBuffer?.PendingCount ?? 0;

        private void Awake()
        {
            combatCommandBuffer = new QusapCombatCommandBuffer(combatCommandBufferCapacity);

            if (inputActionAsset == null)
            {
                Debug.LogError("QusapInputReader requires the configured Gameplay actions in its InputActionAsset.");
                enabled = false;
                return;
            }

            runtimeActionAsset = Instantiate(inputActionAsset);
            runtimeActionAsset.name = $"{inputActionAsset.name}_{localPlayerSlot}_{GetEntityId()}";
            runtimeActionAsset.hideFlags = HideFlags.HideAndDontSave;
            ConfigureRuntimeInput();

            moveAction = runtimeActionAsset.FindAction("Gameplay/Move");
            jumpAction = runtimeActionAsset.FindAction("Gameplay/Jump");
            dropAction = runtimeActionAsset.FindAction("Gameplay/Drop");
            dashAction = runtimeActionAsset.FindAction("Gameplay/Dash");
            weakKickAction = runtimeActionAsset.FindAction("Gameplay/WeakKick");
            strongKickAction = runtimeActionAsset.FindAction("Gameplay/StrongKick");
            headbuttAction = runtimeActionAsset.FindAction("Gameplay/Headbutt");
            weaponStrongAction = runtimeActionAsset.FindAction("Gameplay/WeaponStrong");
            parryAction = runtimeActionAsset.FindAction("Gameplay/Parry");

            if (moveAction == null)
            {
                Debug.LogError("QusapInputReader could not find the 'Gameplay/Move' action in the assigned InputActionAsset.");
            }

            if (jumpAction == null)
            {
                Debug.LogError("QusapInputReader could not find the 'Gameplay/Jump' action in the assigned InputActionAsset.");
            }

            if (dropAction == null)
            {
                Debug.LogError("QusapInputReader could not find the 'Gameplay/Drop' action in the assigned InputActionAsset.");
            }

            if (dashAction == null)
            {
                Debug.LogError("QusapInputReader could not find the 'Gameplay/Dash' action in the assigned InputActionAsset.");
            }

            if (weakKickAction == null)
            {
                Debug.LogError("QusapInputReader could not find the 'Gameplay/WeakKick' action in the assigned InputActionAsset.");
            }

            if (strongKickAction == null)
            {
                Debug.LogError("QusapInputReader could not find the 'Gameplay/StrongKick' action in the assigned InputActionAsset.");
            }

            if (headbuttAction == null)
            {
                Debug.LogError("QusapInputReader could not find the 'Gameplay/Headbutt' action in the assigned InputActionAsset.");
            }
        }

        private void OnValidate()
        {
            combatCommandBufferCapacity = Mathf.Max(1, combatCommandBufferCapacity);
        }

        private void OnEnable()
        {
            if (moveAction != null)
            {
                moveAction.Enable();
            }

            if (jumpAction != null)
            {
                jumpAction.Enable();
                jumpAction.performed += HandleJumpPerformed;
                jumpAction.canceled += HandleJumpCanceled;
            }

            if (dropAction != null)
            {
                dropAction.Enable();
                dropAction.performed += HandleDropPerformed;
            }

            if (dashAction != null)
            {
                dashAction.Enable();
                dashAction.performed += HandleDashPerformed;
            }

            if (weakKickAction != null)
            {
                weakKickAction.Enable();
                weakKickAction.performed += HandleWeakKickPerformed;
            }

            if (strongKickAction != null)
            {
                strongKickAction.Enable();
                strongKickAction.performed += HandleStrongKickPerformed;
            }

            if (headbuttAction != null)
            {
                headbuttAction.Enable();
                headbuttAction.performed += HandleHeadbuttPerformed;
            }

            if (weaponStrongAction != null)
            {
                weaponStrongAction.Enable();
                weaponStrongAction.performed += HandleWeaponStrongPerformed;
            }

            if (parryAction != null)
            {
                parryAction.Enable();
                parryAction.performed += HandleParryPerformed;
            }
        }

        private void OnDisable()
        {
            if (parryAction != null)
            {
                parryAction.performed -= HandleParryPerformed;
                parryAction.Disable();
            }

            if (weaponStrongAction != null)
            {
                weaponStrongAction.performed -= HandleWeaponStrongPerformed;
                weaponStrongAction.Disable();
            }

            if (headbuttAction != null)
            {
                headbuttAction.performed -= HandleHeadbuttPerformed;
                headbuttAction.Disable();
            }

            if (strongKickAction != null)
            {
                strongKickAction.performed -= HandleStrongKickPerformed;
                strongKickAction.Disable();
            }

            if (weakKickAction != null)
            {
                weakKickAction.performed -= HandleWeakKickPerformed;
                weakKickAction.Disable();
            }

            if (dashAction != null)
            {
                dashAction.performed -= HandleDashPerformed;
                dashAction.Disable();
            }

            if (dropAction != null)
            {
                dropAction.performed -= HandleDropPerformed;
                dropAction.Disable();
            }

            if (jumpAction != null)
            {
                jumpAction.performed -= HandleJumpPerformed;
                jumpAction.canceled -= HandleJumpCanceled;
                jumpAction.Disable();
            }

            if (moveAction != null)
            {
                moveAction.Disable();
            }

            ClearBufferedActions();
            horizontalValue = 0f;
        }

        private void OnDestroy()
        {
            if (runtimeActionAsset == null)
            {
                return;
            }

            runtimeActionAsset.Disable();
            Destroy(runtimeActionAsset);
        }

        private void Update()
        {
            if (moveAction == null)
            {
                return;
            }

            horizontalValue = Mathf.Clamp(moveAction.ReadValue<float>(), -1f, 1f);
        }

        public bool ConsumeJumpPressed()
        {
            return ConsumeBufferedAction(ref jumpPressed);
        }

        public bool ConsumeJumpReleased()
        {
            return ConsumeBufferedAction(ref jumpReleased);
        }

        public bool ConsumeDropPressed()
        {
            return ConsumeBufferedAction(ref dropPressed);
        }

        public bool ConsumeDashPressed()
        {
            return ConsumeBufferedAction(ref dashPressed);
        }

        public bool ConsumeWeakKickPressed()
        {
            return ConsumeBufferedAction(ref weakKickPressed);
        }

        public bool ConsumeStrongKickPressed()
        {
            return ConsumeBufferedAction(ref strongKickPressed);
        }

        public bool ConsumeHeadbuttPressed()
        {
            return ConsumeBufferedAction(ref headbuttPressed);
        }

        public bool TryConsumeCombatCommand(out QusapCombatCommandPress press)
        {
            if (gameplayInputBlocked || combatCommandBuffer == null)
            {
                press = default;
                return false;
            }

            return combatCommandBuffer.TryDequeue(out press);
        }

        public void SetLocalPlayerSlot(QusapLocalPlayerSlot slot)
        {
            if (Application.isPlaying && runtimeActionAsset != null)
            {
                Debug.LogWarning("The local player slot cannot be changed after QusapInputReader has initialized.", this);
                return;
            }

            localPlayerSlot = slot;
        }

        public void SetGameplayInputBlocked(bool blocked)
        {
            gameplayInputBlocked = blocked;
            horizontalValue = blocked ? 0f : horizontalValue;
            ClearBufferedActions();
        }

        public void ClearBufferedActions()
        {
            jumpPressed = false;
            jumpReleased = false;
            dropPressed = false;
            dashPressed = false;
            weakKickPressed = false;
            strongKickPressed = false;
            headbuttPressed = false;
            combatCommandBuffer?.Clear();
        }

        private void ConfigureRuntimeInput()
        {
            bool isPlayerOne = localPlayerSlot == QusapLocalPlayerSlot.Player1Keyboard;
            string bindingGroup = isPlayerOne ? "Player1" : "Player2";
            InputDevice device = isPlayerOne ? Keyboard.current : GetFirstGamepad();

            runtimeActionAsset.bindingMask = InputBinding.MaskByGroup(bindingGroup);
            runtimeActionAsset.devices = device != null
                ? new[] { device }
                : new InputDevice[0];

            if (device == null)
            {
                string expectedDevice = isPlayerOne ? "keyboard" : "gamepad";
                Debug.LogWarning($"{name} has no {expectedDevice} available for {localPlayerSlot}. It will remain unpaired.", this);
            }
        }

        private static Gamepad GetFirstGamepad()
        {
            return Gamepad.all.Count > 0 ? Gamepad.all[0] : null;
        }

        private bool ConsumeBufferedAction(ref bool bufferedAction)
        {
            bool wasBuffered = bufferedAction;
            bufferedAction = false;
            return wasBuffered && !gameplayInputBlocked;
        }

        private void HandleJumpPerformed(InputAction.CallbackContext context)
        {
            jumpPressed = true;
        }

        private void HandleJumpCanceled(InputAction.CallbackContext context)
        {
            jumpReleased = true;
        }

        private void HandleDropPerformed(InputAction.CallbackContext context)
        {
            dropPressed = true;
        }

        private void HandleDashPerformed(InputAction.CallbackContext context)
        {
            dashPressed = true;
        }

        private void HandleWeakKickPerformed(InputAction.CallbackContext context)
        {
            if (gameplayInputBlocked)
            {
                return;
            }

            weakKickPressed = true;
            combatCommandBuffer.Enqueue(QusapCombatCommand.BodyAttack, context.time);
        }

        private void HandleStrongKickPerformed(InputAction.CallbackContext context)
        {
            if (gameplayInputBlocked)
            {
                return;
            }

            strongKickPressed = true;
            combatCommandBuffer.Enqueue(QusapCombatCommand.WeaponLight, context.time);
        }

        private void HandleHeadbuttPerformed(InputAction.CallbackContext context)
        {
            if (gameplayInputBlocked)
            {
                return;
            }

            headbuttPressed = true;
            combatCommandBuffer.Enqueue(QusapCombatCommand.Headbutt, context.time);
        }

        private void HandleWeaponStrongPerformed(InputAction.CallbackContext context)
        {
            if (!gameplayInputBlocked)
            {
                combatCommandBuffer.Enqueue(QusapCombatCommand.WeaponStrong, context.time);
            }
        }

        private void HandleParryPerformed(InputAction.CallbackContext context)
        {
            if (!gameplayInputBlocked)
            {
                combatCommandBuffer.Enqueue(QusapCombatCommand.Parry, context.time);
            }
        }
    }
}
