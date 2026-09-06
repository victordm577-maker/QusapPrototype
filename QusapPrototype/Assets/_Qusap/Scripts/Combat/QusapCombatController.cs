using System;
using UnityEngine;

namespace Qusap
{
    [DefaultExecutionOrder(50)]
    [DisallowMultipleComponent]
    [RequireComponent(typeof(Rigidbody))]
    [RequireComponent(typeof(QusapInputReader))]
    [RequireComponent(typeof(QusapDashMotor))]
    [RequireComponent(typeof(QusapGroundSensor))]
    [RequireComponent(typeof(QusapHitReceiver))]
    [RequireComponent(typeof(QusapHitstunController))]
    public sealed class QusapCombatController : MonoBehaviour
    {
        [SerializeField] private bool combatAllowed = true;
        [SerializeField] private float facingInputThreshold = 0.05f;
        [SerializeField, Range(-1, 1)] private int initialFacingDirection = 1;
        [SerializeField] private QusapAttackHitbox attackHitbox;

        [Header("Existing ground attacks - do not retune")]
        [SerializeField] private QusapAttackData weakKick = QusapAttackData.CreateWeakKick();
        [SerializeField] private QusapAttackData strongKick = QusapAttackData.CreateStrongKick();
        [SerializeField] private QusapAttackData headbutt = QusapAttackData.CreateHeadbutt();

        [Header("Air attacks")]
        [SerializeField] private QusapAirAttackData weakKickAir = QusapAirAttackData.CreateWeakKickAir();
        [SerializeField] private QusapAirAttackData strongKickAir = QusapAirAttackData.CreateStrongKickAir();
        [SerializeField] private QusapAirAttackData diveHeadbuttAir = QusapAirAttackData.CreateDiveHeadbuttAir();

        private Rigidbody rb;
        private QusapInputReader inputReader;
        private QusapDashMotor dashMotor;
        private QusapGroundSensor groundSensor;
        private QusapHitstunController hitstunController;
        private IQusapAttackDefinition currentAttack;
        private float phaseTimeRemaining;
        private int attackDirection = 1;
        private bool diveBraking;
        private bool landedDuringAirAttack;
        private bool diveConnected;
        private bool wasGrounded;
        private float diveHorizontalVelocity;

        // Legacy events remain available to avoid breaking existing integrations.
        public event Action<QusapAttackType> AttackStarted;
        public event Action<QusapAttackType> ActiveWindowStarted;
        public event Action<QusapAttackType, QusapHitReceiver> AttackHit;
        public event Action<QusapHitReceiver> HeadbuttConnected;
        public event Action<QusapAttackType> AttackFinished;

        // Exact, read-only signals for the future animation driver.
        public event Action<QusapAttackVariant> AttackVariantStarted;
        public event Action<QusapAttackVariant, QusapAttackPhase> AttackPhaseChanged;
        public event Action<QusapAttackVariant, bool> AttackEnded;

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
        public QusapAttackVariant CurrentAttackVariant { get; private set; } = QusapAttackVariant.None;
        public int FacingDirection { get; private set; } = 1;
        public int AttackDirection => attackDirection;
        public QusapHitReceiver HitReceiver { get; private set; }
        public bool DiveHeadbuttAvailable { get; private set; } = true;
        public bool BlocksDash => IsAttacking
            && CurrentAttackVariant == QusapAttackVariant.DiveHeadbuttAir
            && (diveHeadbuttAir.BlockDash || landedDuringAirAttack);

        private void Awake()
        {
            ValidateAttackData();
            rb = GetComponent<Rigidbody>();
            inputReader = GetComponent<QusapInputReader>();
            dashMotor = GetComponent<QusapDashMotor>();
            groundSensor = GetComponent<QusapGroundSensor>();
            hitstunController = GetComponent<QusapHitstunController>();
            HitReceiver = GetComponent<QusapHitReceiver>();
            FacingDirection = initialFacingDirection < 0 ? -1 : 1;
            wasGrounded = groundSensor != null && groundSensor.IsGrounded;

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
            initialFacingDirection = initialFacingDirection < 0 ? -1 : 1;
            ValidateAttackData();
        }

        private void OnDisable()
        {
            CancelCurrentAttack(false);
        }

        private void FixedUpdate()
        {
            UpdateFacingDirection();
            bool grounded = groundSensor != null && groundSensor.IsGrounded;
            UpdateLandingState(grounded);

            bool weakKickPressed = inputReader.ConsumeWeakKickPressed();
            bool strongKickPressed = inputReader.ConsumeStrongKickPressed();
            bool headbuttPressed = inputReader.ConsumeHeadbuttPressed();

            if (hitstunController != null && hitstunController.IsInHitstun)
            {
                return;
            }

            if (!IsAttacking)
            {
                QusapAttackType? requestedAttack = weakKickPressed
                    ? QusapAttackType.WeakKick
                    : strongKickPressed
                        ? QusapAttackType.StrongKick
                        : headbuttPressed
                            ? QusapAttackType.Headbutt
                            : null;

                if (requestedAttack.HasValue)
                {
                    // Grounded is sampled exactly once for this button press.
                    TryStartAttack(requestedAttack.Value, grounded);
                }

                ApplyMovementLock();
                return;
            }

            AdvanceAttack(Time.fixedDeltaTime);
            ApplyDiveHorizontalControl();
            ApplyMovementLock();
        }

        public bool TryStartAttack(QusapAttackType attackType)
        {
            bool grounded = groundSensor != null && groundSensor.IsGrounded;
            return TryStartAttack(attackType, grounded);
        }

        internal bool TryStartAttack(QusapAttackType attackType, bool groundedAtPress)
        {
            if (!combatAllowed
                || IsAttacking
                || (hitstunController != null && hitstunController.IsInHitstun)
                || dashMotor == null
                || dashMotor.IsDashing)
            {
                return false;
            }

            QusapAttackVariant selectedVariant = SelectAttackVariant(attackType, groundedAtPress);
            if (selectedVariant == QusapAttackVariant.DiveHeadbuttAir && !DiveHeadbuttAvailable)
            {
                return false;
            }

            IQusapAttackDefinition attackData = GetAttackDefinition(selectedVariant);
            if (attackData == null)
            {
                return false;
            }

            currentAttack = attackData;
            CurrentAttackVariant = selectedVariant;
            attackDirection = FacingDirection;
            CurrentPhase = QusapAttackPhase.Startup;
            phaseTimeRemaining = currentAttack.StartupTime;
            diveBraking = false;
            landedDuringAirAttack = false;
            diveConnected = false;
            diveHorizontalVelocity = rb.linearVelocity.x;

            if (selectedVariant == QusapAttackVariant.DiveHeadbuttAir)
            {
                DiveHeadbuttAvailable = false;
            }
            else if (selectedVariant == QusapAttackVariant.StrongKickAir)
            {
                Vector3 velocity = rb.linearVelocity;
                velocity.x *= strongKickAir.HorizontalVelocityRetention;
                rb.linearVelocity = velocity;
            }

            AttackStarted?.Invoke(currentAttack.AttackType);
            AttackVariantStarted?.Invoke(CurrentAttackVariant);
            AttackPhaseChanged?.Invoke(CurrentAttackVariant, CurrentPhase);

            if (phaseTimeRemaining <= 0f)
            {
                AdvanceAttack(0f);
            }

            return true;
        }

        public static QusapAttackVariant SelectAttackVariant(QusapAttackType attackType, bool grounded)
        {
            return (attackType, grounded) switch
            {
                (QusapAttackType.WeakKick, true) => QusapAttackVariant.WeakKickGround,
                (QusapAttackType.WeakKick, false) => QusapAttackVariant.WeakKickAir,
                (QusapAttackType.StrongKick, true) => QusapAttackVariant.StrongKickGround,
                (QusapAttackType.StrongKick, false) => QusapAttackVariant.StrongKickAir,
                (QusapAttackType.Headbutt, true) => QusapAttackVariant.HeadbuttGround,
                (QusapAttackType.Headbutt, false) => QusapAttackVariant.DiveHeadbuttAir,
                _ => QusapAttackVariant.None
            };
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

        public QusapAirAttackData GetAirAttackData(QusapAttackVariant attackVariant)
        {
            return attackVariant switch
            {
                QusapAttackVariant.WeakKickAir => weakKickAir,
                QusapAttackVariant.StrongKickAir => strongKickAir,
                QusapAttackVariant.DiveHeadbuttAir => diveHeadbuttAir,
                _ => null
            };
        }

        internal IQusapAttackDefinition GetAttackDefinition(QusapAttackVariant attackVariant)
        {
            return attackVariant switch
            {
                QusapAttackVariant.WeakKickGround => weakKick,
                QusapAttackVariant.WeakKickAir => weakKickAir,
                QusapAttackVariant.StrongKickGround => strongKick,
                QusapAttackVariant.StrongKickAir => strongKickAir,
                QusapAttackVariant.HeadbuttGround => headbutt,
                QusapAttackVariant.DiveHeadbuttAir => diveHeadbuttAir,
                _ => null
            };
        }

        public void CancelAttack()
        {
            CancelCurrentAttack(true);
        }

        public void ResetCombatState()
        {
            CancelCurrentAttack(true);
            DiveHeadbuttAvailable = true;
            wasGrounded = groundSensor != null && groundSensor.IsGrounded;
        }

        internal void NotifyAttackHit(QusapHitReceiver receiver)
        {
            if (currentAttack == null || receiver == null || CurrentPhase != QusapAttackPhase.Active)
            {
                return;
            }

            QusapAttackType attackType = currentAttack.AttackType;
            QusapAttackVariant variant = CurrentAttackVariant;
            AttackHit?.Invoke(attackType, receiver);

            if (attackType == QusapAttackType.Headbutt)
            {
                HeadbuttConnected?.Invoke(receiver);
            }

            if (variant != QusapAttackVariant.DiveHeadbuttAir || diveConnected)
            {
                return;
            }

            diveConnected = true;
            attackHitbox.EndAttack();
            Vector3 velocity = rb.linearVelocity;
            velocity.y = diveHeadbuttAir.DiveBounceSpeed;
            velocity.z = 0f;
            rb.linearVelocity = velocity;
            EnterRecovery(diveHeadbuttAir.RecoveryTime);
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

        private void UpdateLandingState(bool grounded)
        {
            if (grounded && !wasGrounded)
            {
                DiveHeadbuttAvailable = true;
                if (IsAirVariant(CurrentAttackVariant))
                {
                    HandleAirAttackLanding();
                }
            }

            wasGrounded = grounded;
        }

        private void HandleAirAttackLanding()
        {
            QusapAirAttackData airAttack = currentAttack as QusapAirAttackData;
            if (airAttack == null || landedDuringAirAttack)
            {
                return;
            }

            landedDuringAirAttack = true;
            if (airAttack.EndActiveWindowOnLanding)
            {
                attackHitbox.EndAttack();
            }

            if (CurrentAttackVariant == QusapAttackVariant.DiveHeadbuttAir)
            {
                Vector3 velocity = rb.linearVelocity;
                velocity.y = Mathf.Max(velocity.y, 0f);
                velocity.z = 0f;
                rb.linearVelocity = velocity;
            }

            if (!diveConnected || CurrentPhase != QusapAttackPhase.Recovery)
            {
                EnterRecovery(airAttack.LandingRecoveryTime);
            }
        }

        private void AdvanceAttack(float elapsedTime)
        {
            phaseTimeRemaining -= elapsedTime;
            int transitionsRemaining = 4;

            while (IsAttacking && phaseTimeRemaining <= 0f && transitionsRemaining-- > 0)
            {
                float overflowTime = -phaseTimeRemaining;

                switch (CurrentPhase)
                {
                    case QusapAttackPhase.Startup:
                        if (CurrentAttackVariant == QusapAttackVariant.DiveHeadbuttAir && !diveBraking)
                        {
                            diveBraking = true;
                            Vector3 velocity = rb.linearVelocity;
                            velocity.y *= diveHeadbuttAir.DiveVerticalBrakeMultiplier;
                            velocity.z = 0f;
                            rb.linearVelocity = velocity;
                            phaseTimeRemaining = diveHeadbuttAir.DiveBrakeDuration - overflowTime;
                            break;
                        }

                        BeginActiveWindow(overflowTime);
                        break;

                    case QusapAttackPhase.Active:
                        attackHitbox.EndAttack();
                        EnterRecovery(currentAttack.RecoveryTime - overflowTime);
                        break;

                    case QusapAttackPhase.Recovery:
                        FinishCurrentAttack();
                        break;
                }
            }
        }

        private void BeginActiveWindow(float overflowTime)
        {
            CurrentPhase = QusapAttackPhase.Active;
            phaseTimeRemaining = currentAttack.ActiveDuration - overflowTime;

            if (CurrentAttackVariant == QusapAttackVariant.DiveHeadbuttAir)
            {
                Vector3 velocity = rb.linearVelocity;
                velocity.y = -diveHeadbuttAir.DiveDownwardSpeed;
                velocity.z = 0f;
                rb.linearVelocity = velocity;
                diveHorizontalVelocity = velocity.x;
            }

            attackHitbox.BeginAttack(currentAttack, CurrentAttackVariant, attackDirection);
            ActiveWindowStarted?.Invoke(currentAttack.AttackType);
            AttackPhaseChanged?.Invoke(CurrentAttackVariant, CurrentPhase);
        }

        private void EnterRecovery(float duration)
        {
            CurrentPhase = QusapAttackPhase.Recovery;
            phaseTimeRemaining = Mathf.Max(duration, 0f);
            AttackPhaseChanged?.Invoke(CurrentAttackVariant, CurrentPhase);
            if (phaseTimeRemaining <= 0f)
            {
                FinishCurrentAttack();
            }
        }

        private void FinishCurrentAttack()
        {
            if (currentAttack == null)
            {
                return;
            }

            QusapAttackType finishedAttack = currentAttack.AttackType;
            QusapAttackVariant finishedVariant = CurrentAttackVariant;
            attackHitbox.EndAttack();
            currentAttack = null;
            phaseTimeRemaining = 0f;
            CurrentPhase = QusapAttackPhase.Idle;
            CurrentAttackVariant = QusapAttackVariant.None;
            AttackFinished?.Invoke(finishedAttack);
            AttackEnded?.Invoke(finishedVariant, false);
            AttackPhaseChanged?.Invoke(QusapAttackVariant.None, QusapAttackPhase.Idle);
        }

        private void CancelCurrentAttack(bool notifyFinished)
        {
            if (!IsAttacking || currentAttack == null)
            {
                attackHitbox?.EndAttack();
                CurrentPhase = QusapAttackPhase.Idle;
                CurrentAttackVariant = QusapAttackVariant.None;
                return;
            }

            QusapAttackType canceledAttack = currentAttack.AttackType;
            QusapAttackVariant canceledVariant = CurrentAttackVariant;
            attackHitbox?.EndAttack();
            currentAttack = null;
            phaseTimeRemaining = 0f;
            CurrentPhase = QusapAttackPhase.Idle;
            CurrentAttackVariant = QusapAttackVariant.None;
            diveBraking = false;
            landedDuringAirAttack = false;
            diveConnected = false;

            if (notifyFinished)
            {
                AttackFinished?.Invoke(canceledAttack);
            }

            AttackEnded?.Invoke(canceledVariant, true);
            AttackPhaseChanged?.Invoke(QusapAttackVariant.None, QusapAttackPhase.Idle);
        }

        private void ApplyDiveHorizontalControl()
        {
            if (CurrentAttackVariant != QusapAttackVariant.DiveHeadbuttAir
                || CurrentPhase != QusapAttackPhase.Active)
            {
                return;
            }

            Vector3 velocity = rb.linearVelocity;
            velocity.x = Mathf.Lerp(
                diveHorizontalVelocity,
                velocity.x,
                diveHeadbuttAir.DiveHorizontalControlMultiplier);
            velocity.z = 0f;
            rb.linearVelocity = velocity;
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
            weakKickAir ??= QusapAirAttackData.CreateWeakKickAir();
            strongKickAir ??= QusapAirAttackData.CreateStrongKickAir();
            diveHeadbuttAir ??= QusapAirAttackData.CreateDiveHeadbuttAir();

            weakKick.SetAttackType(QusapAttackType.WeakKick);
            strongKick.SetAttackType(QusapAttackType.StrongKick);
            headbutt.SetAttackType(QusapAttackType.Headbutt);
            weakKickAir.SetAttackType(QusapAttackType.WeakKick);
            strongKickAir.SetAttackType(QusapAttackType.StrongKick);
            diveHeadbuttAir.SetAttackType(QusapAttackType.Headbutt);

            weakKick.Validate();
            strongKick.Validate();
            headbutt.Validate();
            weakKickAir.Validate();
            strongKickAir.Validate();
            diveHeadbuttAir.Validate();
        }

        private static bool IsAirVariant(QusapAttackVariant variant)
        {
            return variant == QusapAttackVariant.WeakKickAir
                || variant == QusapAttackVariant.StrongKickAir
                || variant == QusapAttackVariant.DiveHeadbuttAir;
        }
    }
}
