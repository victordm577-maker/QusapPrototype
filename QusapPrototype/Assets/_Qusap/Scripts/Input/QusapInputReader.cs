using UnityEngine;
using UnityEngine.InputSystem;

namespace Qusap
{
    [RequireComponent(typeof(QusapInputReader))]
    public class QusapInputReader : MonoBehaviour
    {
        [SerializeField] private InputActionAsset inputActionAsset;

        private InputAction moveAction;
        private InputAction jumpAction;
        private InputAction dropAction;
        private InputAction dashAction;
        private InputAction weakKickAction;
        private InputAction strongKickAction;
        private InputAction headbuttAction;
        private float horizontalValue;
        private bool jumpPressed;
        private bool jumpReleased;
        private bool dropPressed;
        private bool dashPressed;
        private bool weakKickPressed;
        private bool strongKickPressed;
        private bool headbuttPressed;

        public float HorizontalValue => horizontalValue;

        private void Awake()
        {
            if (inputActionAsset == null)
            {
                Debug.LogError("QusapInputReader requires the configured Gameplay actions in its InputActionAsset.");
                return;
            }

            moveAction = inputActionAsset.FindAction("Gameplay/Move");
            jumpAction = inputActionAsset.FindAction("Gameplay/Jump");
            dropAction = inputActionAsset.FindAction("Gameplay/Drop");
            dashAction = inputActionAsset.FindAction("Gameplay/Dash");
            weakKickAction = inputActionAsset.FindAction("Gameplay/WeakKick");
            strongKickAction = inputActionAsset.FindAction("Gameplay/StrongKick");
            headbuttAction = inputActionAsset.FindAction("Gameplay/Headbutt");

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
        }

        private void OnDisable()
        {
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
            if (!jumpPressed)
            {
                return false;
            }

            jumpPressed = false;
            return true;
        }

        public bool ConsumeJumpReleased()
        {
            if (!jumpReleased)
            {
                return false;
            }

            jumpReleased = false;
            return true;
        }

        public bool ConsumeDropPressed()
        {
            if (!dropPressed)
            {
                return false;
            }

            dropPressed = false;
            return true;
        }

        public bool ConsumeDashPressed()
        {
            if (!dashPressed)
            {
                return false;
            }

            dashPressed = false;
            return true;
        }

        public bool ConsumeWeakKickPressed()
        {
            if (!weakKickPressed)
            {
                return false;
            }

            weakKickPressed = false;
            return true;
        }

        public bool ConsumeStrongKickPressed()
        {
            if (!strongKickPressed)
            {
                return false;
            }

            strongKickPressed = false;
            return true;
        }

        public bool ConsumeHeadbuttPressed()
        {
            if (!headbuttPressed)
            {
                return false;
            }

            headbuttPressed = false;
            return true;
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
            weakKickPressed = true;
        }

        private void HandleStrongKickPerformed(InputAction.CallbackContext context)
        {
            strongKickPressed = true;
        }

        private void HandleHeadbuttPerformed(InputAction.CallbackContext context)
        {
            headbuttPressed = true;
        }
    }
}
