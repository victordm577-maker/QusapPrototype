using System;
using UnityEngine;

namespace Qusap
{
    public enum QusapRecoveryCause { TechnicalRecovery, NewSession }

    [RequireComponent(typeof(Rigidbody))]
    [RequireComponent(typeof(QusapDashMotor))]
    public class QusapRespawnController : MonoBehaviour
    {
        [SerializeField] private float fallLimitY = -8f;
        [SerializeField] private Transform optionalRespawnPoint;

        private Rigidbody rb;
        private QusapDashMotor dashMotor;
        private QusapHitstunController hitstunController;
        private QusapCombatController combatController;
        private QusapHitReceiver hitReceiver;
        private Vector3 initialPosition;
        private Quaternion initialRotation;
        public event Action<QusapRecoveryCause> RecoveryCompleted;

        private void Awake()
        {
            rb = GetComponent<Rigidbody>();
            dashMotor = GetComponent<QusapDashMotor>();
            hitstunController = GetComponent<QusapHitstunController>();
            combatController = GetComponent<QusapCombatController>();
            hitReceiver = GetComponent<QusapHitReceiver>();
            initialPosition = rb.position;
            initialRotation = rb.rotation;
        }

        private void FixedUpdate()
        {
            if (rb.position.y < fallLimitY)
            {
                TechnicalRecovery();
            }
        }

        public void Respawn()
        {
            // Compatibility entry point: recovery within a session never heals.
            TechnicalRecovery();
        }

        public void TechnicalRecovery()
        {
            if (hitReceiver != null && hitReceiver.IsHealthDepleted) return;
            Recover(QusapRecoveryCause.TechnicalRecovery);
        }

        public void ResetForNewSession()
        {
            hitReceiver?.ResetForNewSession();
            Recover(QusapRecoveryCause.NewSession);
        }

        private void Recover(QusapRecoveryCause cause)
        {
            hitstunController?.ResetHitstun();
            dashMotor?.ResetDashState();
            combatController?.ResetCombatState();

            Vector3 respawnPosition = optionalRespawnPoint != null
                ? optionalRespawnPoint.position
                : initialPosition;
            Quaternion respawnRotation = optionalRespawnPoint != null
                ? optionalRespawnPoint.rotation
                : initialRotation;

            rb.position = respawnPosition;
            rb.rotation = respawnRotation;
            rb.linearVelocity = Vector3.zero;
            rb.angularVelocity = Vector3.zero;
            rb.WakeUp();
            RecoveryCompleted?.Invoke(cause);
        }
    }
}
