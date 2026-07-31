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
        private float horizontalValue;
        private bool jumpPressed;
        private bool jumpReleased;

        public float HorizontalValue => horizontalValue;

        private void Awake()
        {
            if (inputActionAsset == null)
            {
                Debug.LogError("QusapInputReader requires an InputActionAsset with the 'Gameplay/Move' and 'Gameplay/Jump' actions assigned.");
                return;
            }

            moveAction = inputActionAsset.FindAction("Gameplay/Move");
            jumpAction = inputActionAsset.FindAction("Gameplay/Jump");

            if (moveAction == null)
            {
                Debug.LogError("QusapInputReader could not find the 'Gameplay/Move' action in the assigned InputActionAsset.");
            }

            if (jumpAction == null)
            {
                Debug.LogError("QusapInputReader could not find the 'Gameplay/Jump' action in the assigned InputActionAsset.");
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
        }

        private void OnDisable()
        {
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

        private void HandleJumpPerformed(InputAction.CallbackContext context)
        {
            jumpPressed = true;
        }

        private void HandleJumpCanceled(InputAction.CallbackContext context)
        {
            jumpReleased = true;
        }
    }
}
