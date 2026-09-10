using UnityEngine;

namespace Qusap
{
    [DisallowMultipleComponent]
    public sealed class QusapModularFacingPresenter : MonoBehaviour
    {
        [SerializeField] private QusapCombatController combatController;
        [SerializeField] private Transform orientationPivot;
        [SerializeField] private float rightFacingYaw = 60f;
        [SerializeField] private float leftFacingYaw = -60f;

        public QusapCombatController CombatController => combatController;
        public Transform OrientationPivot => orientationPivot;
        public float RightFacingYaw => rightFacingYaw;
        public float LeftFacingYaw => leftFacingYaw;

        private void Awake()
        {
            combatController ??= GetComponent<QusapCombatController>();
        }

        private void OnEnable()
        {
            ApplyAuthoritativeFacing();
        }

        private void LateUpdate()
        {
            ApplyAuthoritativeFacing();
        }

        public void Configure(
            QusapCombatController configuredCombatController,
            Transform configuredOrientationPivot,
            float configuredRightFacingYaw = 60f,
            float configuredLeftFacingYaw = -60f)
        {
            combatController = configuredCombatController;
            orientationPivot = configuredOrientationPivot;
            rightFacingYaw = configuredRightFacingYaw;
            leftFacingYaw = configuredLeftFacingYaw;
            ApplyAuthoritativeFacing();
        }

        public bool ApplyAuthoritativeFacing()
        {
            if (combatController == null || orientationPivot == null)
                return false;

            Vector3 euler = orientationPivot.localEulerAngles;
            euler.y = combatController.FacingDirection < 0 ? leftFacingYaw : rightFacingYaw;
            orientationPivot.localEulerAngles = euler;
            orientationPivot.localScale = Vector3.one;
            return true;
        }
    }
}
