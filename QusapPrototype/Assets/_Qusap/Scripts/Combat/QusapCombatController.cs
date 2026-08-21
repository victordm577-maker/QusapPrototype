using System;
using UnityEngine;

namespace Qusap
{
    [DefaultExecutionOrder(50)]
    [DisallowMultipleComponent]
    [RequireComponent(typeof(Rigidbody))]
    [RequireComponent(typeof(QusapInputReader))]
    [RequireComponent(typeof(QusapDashMotor))]
    [RequireComponent(typeof(QusapHitReceiver))]
    public sealed class QusapCombatController : MonoBehaviour
    {
        [SerializeField] private bool combatAllowed = true;
        [SerializeField] private float facingInputThreshold = 0.05f;
        [SerializeField] private QusapAttackHitbox attackHitbox;
        [SerializeField] private QusapAttackData weakKick = QusapAttackData.CreateWeakKick();
        [SerializeField] private QusapAttackData strongKick = QusapAttackData.CreateStrongKick();
        [SerializeField] private QusapAttackData headbutt = QusapAttackData.CreateHeadbutt();

        private Rigidbody rb;
        private QusapInputReader inputReader;
        private QusapDashMotor dashMotor;
        private QusapAttackData currentAttack;
        private float phaseTimeRemaining;
        private int attackDirection = 1;

        public event Action<QusapAttackType> AttackStarted;
        public event Action<QusapAttackType> ActiveWindowStarted;
        public event Action<QusapAttackType, QusapHitReceiver> AttackHit;
        public event Action<QusapHitReceiver> HeadbuttConnected;
        public event Action<QusapAttackType> AttackFinished;

        public bool CombatAllowed
        {
            get => combatAllowed;
            set
            {
                combatAllowed = value;

                if (!combatAllowed)
                {
                    CancelCurrentAttack(false);
                }
            }
        }

        public bool IsAttacking => CurrentPhase != QusapAttackPhase.Idle;
        public QusapAttackPhase CurrentPhase { get; private set; } = QusapAttackPhase.Idle;
        public QusapAttackType? CurrentAttackType => currentAttack?.AttackType;
        public int FacingDirection { get; private set; } = 1;
        public int AttackDirection => attackDirection;
        public QusapHitReceiver HitReceiver { get; private set; }

        private void Awake()
        {
            rb = GetComponent<Rigidbody>();
            inputReader = GetComponent<QusapInputReader>();
            dashMotor = GetComponent<QusapDashMotor>();
            HitReceiver = GetComponent<QusapHitReceiver>();

            if (attackHitbox == null)
            {
                attackHitbox = GetComponentInChildren<QusapAttackHitbox>(true);
            }

            if (attackHitbox == null)
            {
                Debug.LogError(
                    $"{nameof(QusapCombatController)} requires a child {nameof(QusapAttackHitbox)} on '{gameObject.name}'.",
                    this);
                enabled = false;
                return;
            }

            attackHitbox.Initialize(this);
        }

        private void OnValidate()
        {
            facingInputThreshold = Mathf.Max(facingInputThreshold, 0f);
            ValidateAttackData();
        }

        private void OnDisable()
        {
            CancelCurrentAttack(false);
        }

        private void FixedUpdate()
        {
            UpdateFacingDirection();

            bool weakKickPressed = inputReader.ConsumeWeakKickPressed();
            bool strongKickPressed = inputReader.ConsumeStrongKickPressed();
            bool headbuttPressed = inputReader.ConsumeHeadbuttPressed();

            if (!IsAttacking)
            {
                QusapAttackData requestedAttack = weakKickPressed
                    ? weakKick
                    : strongKickPressed
                        ? strongKick
                        : headbuttPressed
                            ? headbutt
                            : null;

                if (requestedAttack != null)
                {
                    TryStartAttack(requestedAttack.AttackType);
                }

                ApplyMovementLock();
                return;
            }

            AdvanceAttack(Time.fixedDeltaTime);
            ApplyMovementLock();
        }

        public bool TryStartAttack(QusapAttackType attackType)
        {
            if (!combatAllowed
                || IsAttacking
                || dashMotor == null
                || dashMotor.IsDashing)
            {
                return false;
            }

            QusapAttackData attackData = GetAttackData(attackType);
            if (attackData == null)
            {
                return false;
            }

            currentAttack = attackData;
            attackDirection = FacingDirection;
            CurrentPhase = QusapAttackPhase.Startup;
            phaseTimeRemaining = currentAttack.StartupTime;
            AttackStarted?.Invoke(currentAttack.AttackType);

            if (phaseTimeRemaining <= 0f)
            {
                AdvanceAttack(0f);
            }

            return true;
        }

        public QusapAttackData GetAttackData(QusapAttackType attackType)
        {
            return attackType switch
            {
                QusapAttackType.WeakKick => weakKick,
                QusapAttackType.StrongKick => strongKick,
                QusapAttackType.Headbutt => headbutt,
                _ => null
            };
        }

        internal void NotifyAttackHit(QusapHitReceiver receiver)
        {
            if (currentAttack == null || receiver == null)
            {
                return;
            }

            AttackHit?.Invoke(currentAttack.AttackType, receiver);

            if (currentAttack.AttackType == QusapAttackType.Headbutt)
            {
                HeadbuttConnected?.Invoke(receiver);
            }
        }

        private void UpdateFacingDirection()
        {
            float horizontalInput = inputReader.HorizontalValue;

            if (horizontalInput > facingInputThreshold)
            {
                FacingDirection = 1;
            }
            else if (horizontalInput < -facingInputThreshold)
            {
                FacingDirection = -1;
            }
        }

        private void AdvanceAttack(float elapsedTime)
        {
            phaseTimeRemaining -= elapsedTime;
            int transitionsRemaining = 3;

            while (IsAttacking && phaseTimeRemaining <= 0f && transitionsRemaining-- > 0)
            {
                float overflowTime = -phaseTimeRemaining;

                switch (CurrentPhase)
                {
                    case QusapAttackPhase.Startup:
                        CurrentPhase = QusapAttackPhase.Active;
                        phaseTimeRemaining = currentAttack.ActiveDuration - overflowTime;
                        attackHitbox.BeginAttack(currentAttack, attackDirection);
                        ActiveWindowStarted?.Invoke(currentAttack.AttackType);
                        break;

                    case QusapAttackPhase.Active:
                        attackHitbox.EndAttack();
                        CurrentPhase = QusapAttackPhase.Recovery;
                        phaseTimeRemaining = currentAttack.RecoveryTime - overflowTime;
                        break;

                    case QusapAttackPhase.Recovery:
                        FinishCurrentAttack();
                        break;
                }
            }
        }

        private void FinishCurrentAttack()
        {
            QusapAttackType finishedAttack = currentAttack.AttackType;
            attackHitbox.EndAttack();
            currentAttack = null;
            phaseTimeRemaining = 0f;
            CurrentPhase = QusapAttackPhase.Idle;
            AttackFinished?.Invoke(finishedAttack);
        }

        private void CancelCurrentAttack(bool notifyFinished)
        {
            if (!IsAttacking || currentAttack == null)
            {
                attackHitbox?.EndAttack();
                CurrentPhase = QusapAttackPhase.Idle;
                return;
            }

            QusapAttackType canceledAttack = currentAttack.AttackType;
            attackHitbox?.EndAttack();
            currentAttack = null;
            phaseTimeRemaining = 0f;
            CurrentPhase = QusapAttackPhase.Idle;

            if (notifyFinished)
            {
                AttackFinished?.Invoke(canceledAttack);
            }
        }

        private void ApplyMovementLock()
        {
            if (currentAttack == null
                || !currentAttack.LockHorizontalMovement
                || dashMotor.IsDashing)
            {
                return;
            }

            Vector3 velocity = rb.linearVelocity;
            velocity.x = 0f;
            velocity.z = 0f;
            rb.linearVelocity = velocity;
        }

        private void ValidateAttackData()
        {
            weakKick ??= QusapAttackData.CreateWeakKick();
            strongKick ??= QusapAttackData.CreateStrongKick();
            headbutt ??= QusapAttackData.CreateHeadbutt();

            weakKick.SetAttackType(QusapAttackType.WeakKick);
            strongKick.SetAttackType(QusapAttackType.StrongKick);
            headbutt.SetAttackType(QusapAttackType.Headbutt);
            weakKick.Validate();
            strongKick.Validate();
            headbutt.Validate();
        }
    }
}
