using UnityEngine;
using UnityEngine.InputSystem;

namespace Qusap
{
    [RequireComponent(typeof(QusapInputReader))]
    public class QusapInputReader : MonoBehaviour
    {
        [SerializeField] private InputActionAsset inputActionAsset;

        private InputAction moveAction;
        private float horizontalValue;

        public float HorizontalValue => horizontalValue;

        private void Awake()
        {
            if (inputActionAsset == null)
            {
                Debug.LogError("QusapInputReader requires an InputActionAsset with the 'Gameplay/Move' action assigned.");
                return;
            }

            moveAction = inputActionAsset.FindAction("Gameplay/Move");

            if (moveAction == null)
            {
                Debug.LogError("QusapInputReader could not find the 'Gameplay/Move' action in the assigned InputActionAsset.");
            }
        }

        private void OnEnable()
        {
            if (moveAction == null)
            {
                return;
            }

            moveAction.Enable();
        }

        private void OnDisable()
        {
            if (moveAction == null)
            {
                return;
            }

            moveAction.Disable();
        }

        private void Update()
        {
            if (moveAction == null)
            {
                return;
            }

            horizontalValue = Mathf.Clamp(moveAction.ReadValue<float>(), -1f, 1f);
        }
    }
}
