using System;
using UnityEngine;

namespace Qusap
{
    [DefaultExecutionOrder(-50)]
    [DisallowMultipleComponent]
    [RequireComponent(typeof(QusapInputReader))]
    [RequireComponent(typeof(QusapHorizontalMotor))]
    [RequireComponent(typeof(QusapVerticalMotor))]
    [RequireComponent(typeof(QusapDashMotor))]
    public sealed class QusapHitstunController : MonoBehaviour
    {
        private QusapInputReader inputReader;
        private QusapHorizontalMotor horizontalMotor;
        private QusapVerticalMotor verticalMotor;
        private QusapDashMotor dashMotor;
        private QusapCombatController combatController;
        private bool horizontalMotorWasEnabled;
        private bool verticalMotorWasEnabled;
        private bool dashMotorWasEnabled;
        private float timeRemaining;

        public event Action HitstunStarted;
        public event Action HitstunEnded;

        public bool IsInHitstun { get; private set; }
        public float TimeRemaining => timeRemaining;

        private void Awake()
        {
            inputReader = GetComponent<QusapInputReader>();
            horizontalMotor = GetComponent<QusapHorizontalMotor>();
            verticalMotor = GetComponent<QusapVerticalMotor>();
            dashMotor = GetComponent<QusapDashMotor>();
            combatController = GetComponent<QusapCombatController>();
        }

        private void Update()
        {
            if (!IsInHitstun)
            {
                return;
            }

            timeRemaining = Mathf.Max(timeRemaining - Time.deltaTime, 0f);
            if (timeRemaining <= 0f)
            {
                ExitHitstun();
            }
        }

        private void OnDisable()
        {
            ResetHitstun();
        }

        public void EnterHitstun(float duration)
        {
            duration = Mathf.Max(duration, 0f);
            if (duration <= 0f)
            {
                return;
            }

            timeRemaining = duration;
            combatController?.CancelAttack();
            dashMotor?.ResetDashState();
            inputReader?.SetGameplayInputBlocked(true);

            if (IsInHitstun)
            {
                return;
            }

            horizontalMotorWasEnabled = horizontalMotor != null && horizontalMotor.enabled;
            verticalMotorWasEnabled = verticalMotor != null && verticalMotor.enabled;
            dashMotorWasEnabled = dashMotor != null && dashMotor.enabled;

            if (horizontalMotor != null)
            {
                horizontalMotor.enabled = false;
            }

            if (verticalMotor != null)
            {
                verticalMotor.enabled = false;
            }

            if (dashMotor != null)
            {
                dashMotor.enabled = false;
            }

            IsInHitstun = true;
            HitstunStarted?.Invoke();
        }

        public void ResetHitstun()
        {
            if (!IsInHitstun)
            {
                timeRemaining = 0f;
                inputReader?.SetGameplayInputBlocked(false);
                return;
            }

            ExitHitstun();
        }

        private void ExitHitstun()
        {
            timeRemaining = 0f;
            IsInHitstun = false;

            if (horizontalMotor != null)
            {
                horizontalMotor.enabled = horizontalMotorWasEnabled;
            }

            if (verticalMotor != null)
            {
                verticalMotor.enabled = verticalMotorWasEnabled;
            }

            if (dashMotor != null)
            {
                dashMotor.enabled = dashMotorWasEnabled;
            }

            inputReader?.SetGameplayInputBlocked(false);
            HitstunEnded?.Invoke();
        }
    }
}
