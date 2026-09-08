using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem.LowLevel;

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
        private const int MaxComboCommandsPerFixedUpdate = 64;

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

        [Header("Combo setup attacks")]
        [SerializeField] private QusapAttackData comboBodyAttackGround =
            QusapAttackData.CreateComboBodyAttackGround();
        [SerializeField] private QusapAttackData comboWeaponLightGround =
            QusapAttackData.CreateComboWeaponLightGround();
        [SerializeField] private QusapAirAttackData comboBodyAttackAir =
            QusapAirAttackData.CreateComboBodyAttackAir();
        [SerializeField] private QusapAirAttackData comboWeaponLightAir =
            QusapAirAttackData.CreateComboWeaponLightAir();

        [Header("Combo recognition")]
        [SerializeField] private bool comboRecognitionEnabled = true;
        [SerializeField] private bool logRecognizedCombos = true;
        [SerializeField] private QusapComboDefinition[] comboDefinitions = Array.Empty<QusapComboDefinition>();
        [SerializeField] private QusapFinisherParrySettings finisherParrySettings =
            QusapFinisherParrySettings.CreateDefault();

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
        private QusapComboSequenceMatcher comboMatcher;
        private QusapComboMatchResult lastComboMatchResult;
        private bool hasPendingComboStep;
        private QusapCombatCommandPress pendingComboPress;
        private ulong currentAttackExecutionId;
        private ulong pendingComboAttackExecutionId;
        private bool pendingComboStartsNewSequence;
        private QusapHitReceiver comboTarget;
        private readonly List<QusapCombatController> incomingFinishers = new();
        private readonly Queue<PendingParryResolution> pendingParryResolutions = new();
        private QusapFinisherParryStateMachine finisherDefense;
        private QusapCombatController armedFinisherDefender;
        private bool inputEventsSubscribed;
        private bool suppressFinisherReset;
        private bool parryFailurePending;
        private bool hasHandledParryPress;
        private ulong lastHandledParryPressId;
        private bool finisherWindowEventEmitted;
        private bool finisherReadyEventEmitted;
        private bool finisherParriedEventEmitted;

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
        public event Action<QusapComboId> ComboCompleted;
        public event Action<QusapComboId, QusapHitReceiver> FinisherArmed;
        public event Action<QusapComboId, QusapHitReceiver> FinisherParryWindowOpened;
        public event Action<QusapComboId, QusapHitReceiver> FinisherParried;
        public event Action<QusapComboId, QusapHitReceiver> FinisherReadyToResolve;
        public event Action<QusapCombatController, QusapComboId> ParrySucceeded;
        public event Action<QusapParryAttemptOutcome> ParryFailed;

        public bool CombatAllowed
        {
            get => combatAllowed;
            set
            {
                combatAllowed = value;
                if (!combatAllowed)
                {
                    CancelIncomingFinishers();
                    CancelCurrentAttack(false);
                    ResetComboRecognition();
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
        public bool ComboRecognitionEnabled => comboRecognitionEnabled;
        public bool IsComboSetupAttack { get; private set; }
        public QusapCombatCommand? CurrentComboSetupCommand { get; private set; }
        public QusapComboId? LastCompletedCombo { get; private set; }
        public bool HasArmedFinisher { get; private set; }
        public QusapComboId? ArmedFinisherCombo { get; private set; }
        public QusapHitReceiver ArmedFinisherTarget { get; private set; }
        public QusapFinisherDefensePhase FinisherDefensePhase =>
            finisherDefense?.Phase ?? QusapFinisherDefensePhase.None;
        public bool IsParryWindowOpen => finisherDefense?.IsWindowOpen ?? false;
        public bool HasFinisherReadyToResolve =>
            HasArmedFinisher && (finisherDefense?.IsReadyToResolve ?? false);
        public double ParryWindowOpensAt => finisherDefense?.WindowOpensAt ?? 0d;
        public double ParryWindowClosesAt => finisherDefense?.WindowClosesAt ?? 0d;
        public int IncomingFinisherCount
        {
            get
            {
                CleanupIncomingFinishers();
                return incomingFinishers.Count;
            }
        }
        public int ActiveComboCandidateCount => comboMatcher?.ActiveCandidateCount ?? 0;
        public bool BlocksDash => IsAttacking
            && CurrentAttackVariant == QusapAttackVariant.DiveHeadbuttAir
            && (diveHeadbuttAir.BlockDash || landedDuringAirAttack);

        private void Awake()
        {
            ValidateAttackData();
            ValidateFinisherParrySettings();
            rb = GetComponent<Rigidbody>();
            inputReader = GetComponent<QusapInputReader>();
            dashMotor = GetComponent<QusapDashMotor>();
            groundSensor = GetComponent<QusapGroundSensor>();
            hitstunController = GetComponent<QusapHitstunController>();
            finisherDefense = new QusapFinisherParryStateMachine();
            InitializeComboRecognition();
            HitReceiver = GetComponent<QusapHitReceiver>();
            HitReceiver.HitReceived += HandleOwnerHitReceived;
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
            ValidateFinisherParrySettings();
            ValidateAttackData();
        }

        private void ValidateFinisherParrySettings()
        {
            finisherParrySettings ??= QusapFinisherParrySettings.CreateDefault();
            finisherParrySettings.ValidateSerializedValues();
        }

        private void OnEnable()
        {
            SubscribeInputEvents();
        }

        private void OnDisable()
        {
            UnsubscribeInputEvents();
            CancelIncomingFinishers();
            CancelCurrentAttack(false);
            ResetComboRecognition();
        }

        private void OnDestroy()
        {
            UnsubscribeInputEvents();
            CancelIncomingFinishers();
            if (HitReceiver != null)
            {
                HitReceiver.HitReceived -= HandleOwnerHitReceived;
            }
        }

        private void FixedUpdate()
        {
            ProcessPendingParryResolutions();
            if (HasArmedFinisher && dashMotor != null && dashMotor.IsDashing)
            {
                ResetComboRecognition();
            }

            if (BlocksOffenseForFinisher())
            {
                inputReader.DiscardPendingCombatCommands();
            }

            UpdateFinisherDefense(InputState.currentTime);
            UpdateFacingDirection();
            bool grounded = groundSensor != null && groundSensor.IsGrounded;
            UpdateLandingState(grounded);

            bool weakKickPressed = inputReader.ConsumeWeakKickPressed();
            bool strongKickPressed = inputReader.ConsumeStrongKickPressed();
            bool headbuttPressed = inputReader.ConsumeHeadbuttPressed();

            bool comboAttackStarted = ProcessPendingComboCommands(grounded);

            if (hitstunController != null && hitstunController.IsInHitstun)
            {
                return;
            }

            if (!IsAttacking)
            {
                QusapAttackType? requestedAttack = !comboRecognitionEnabled && weakKickPressed
                    ? QusapAttackType.WeakKick
                    : !comboRecognitionEnabled && strongKickPressed
                        ? QusapAttackType.StrongKick
                        : !comboRecognitionEnabled && headbuttPressed
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

            if (comboAttackStarted)
            {
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
            QusapAttackVariant selectedVariant = SelectAttackVariant(attackType, groundedAtPress);
            IQusapAttackDefinition attackData = GetAttackDefinition(selectedVariant);
            return TryStartConfiguredAttack(selectedVariant, attackData, null);
        }

        private bool TryStartComboSetupAttack(
            QusapCombatCommand command,
            bool groundedAtPress)
        {
            QusapAttackVariant selectedVariant;
            IQusapAttackDefinition attackData;
            switch (command)
            {
                case QusapCombatCommand.BodyAttack:
                    selectedVariant = groundedAtPress
                        ? QusapAttackVariant.WeakKickGround
                        : QusapAttackVariant.WeakKickAir;
                    attackData = groundedAtPress
                        ? comboBodyAttackGround
                        : comboBodyAttackAir;
                    break;
                case QusapCombatCommand.WeaponLight:
                    selectedVariant = groundedAtPress
                        ? QusapAttackVariant.StrongKickGround
                        : QusapAttackVariant.StrongKickAir;
                    attackData = groundedAtPress
                        ? comboWeaponLightGround
                        : comboWeaponLightAir;
                    break;
                default:
                    return false;
            }

            return TryStartConfiguredAttack(selectedVariant, attackData, command);
        }

        private bool TryStartConfiguredAttack(
            QusapAttackVariant selectedVariant,
            IQusapAttackDefinition attackData,
            QusapCombatCommand? comboSetupCommand)
        {
            if (!combatAllowed
                || IsAttacking
                || BlocksOffenseForFinisher()
                || (hitstunController != null && hitstunController.IsInHitstun)
                || dashMotor == null
                || dashMotor.IsDashing
                || attackData == null)
            {
                return false;
            }

            if (selectedVariant == QusapAttackVariant.DiveHeadbuttAir && !DiveHeadbuttAvailable)
            {
                return false;
            }

            currentAttack = attackData;
            IsComboSetupAttack = comboSetupCommand.HasValue;
            CurrentComboSetupCommand = comboSetupCommand;
            currentAttackExecutionId++;
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
                QusapAirAttackData activeAirAttack = attackData as QusapAirAttackData;
                velocity.x *= activeAirAttack?.HorizontalVelocityRetention ?? 1f;
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
            if (IsAttacking && CurrentAttackVariant == attackVariant && currentAttack != null)
            {
                return currentAttack;
            }

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
            ResetComboRecognition();
        }

        public void ResetCombatState()
        {
            CancelCurrentAttack(true);
            ResetComboRecognition();
            DiveHeadbuttAvailable = true;
            wasGrounded = groundSensor != null && groundSensor.IsGrounded;
        }

        public void ResetComboRecognition()
        {
            if (!suppressFinisherReset)
            {
                CancelOutgoingFinisher();
            }

            comboMatcher?.Reset();
            lastComboMatchResult = default;
            hasPendingComboStep = false;
            pendingComboPress = default;
            pendingComboAttackExecutionId = 0;
            pendingComboStartsNewSequence = false;
            comboTarget = null;
            HasArmedFinisher = false;
            ArmedFinisherCombo = null;
            ArmedFinisherTarget = null;
            LastCompletedCombo = null;
            inputReader?.DiscardPendingCombatCommands();
            pendingParryResolutions.Clear();
            parryFailurePending = false;
        }

        private void InitializeComboRecognition()
        {
            System.Collections.Generic.IEnumerable<QusapComboDefinition> definitions = comboDefinitions;
            if (comboDefinitions == null || comboDefinitions.Length == 0)
            {
                definitions = QusapComboDefinition.CreateDefaultDefinitions();
            }

            try
            {
                comboMatcher = new QusapComboSequenceMatcher(definitions);
            }
            catch (ArgumentException exception)
            {
                Debug.LogError(
                    $"{nameof(QusapCombatController)} on '{gameObject.name}' has invalid combo definitions. "
                    + $"Default definitions will be used. {exception.Message}",
                    this);
                comboMatcher = new QusapComboSequenceMatcher(
                    QusapComboDefinition.CreateDefaultDefinitions());
            }

            lastComboMatchResult = default;
            hasPendingComboStep = false;
            pendingComboPress = default;
            pendingComboAttackExecutionId = 0;
            pendingComboStartsNewSequence = false;
            comboTarget = null;
            HasArmedFinisher = false;
            ArmedFinisherCombo = null;
            ArmedFinisherTarget = null;
            LastCompletedCombo = null;
            finisherDefense?.Reset();
        }

        private bool ProcessPendingComboCommands(bool grounded)
        {
            bool blocked = !comboRecognitionEnabled
                || !combatAllowed
                || (hitstunController != null && hitstunController.IsInHitstun)
                || (dashMotor != null && dashMotor.IsDashing);
            if (blocked)
            {
                if (IsComboSetupAttack && dashMotor != null && dashMotor.IsDashing)
                {
                    CancelCurrentAttack(true);
                }

                ResetComboRecognition();
                return false;
            }

            if (BlocksOffenseForFinisher())
            {
                inputReader.DiscardPendingCombatCommands();
                return false;
            }

            if (!pendingComboStartsNewSequence
                && !IsValidComboTarget(comboTarget)
                && comboMatcher.HasActiveCandidates)
            {
                if (IsComboSetupAttack)
                {
                    CancelCurrentAttack(true);
                }

                ResetComboRecognition();
                return false;
            }

            if (hasPendingComboStep || IsAttacking)
            {
                return false;
            }

            int processedCount = 0;
            while (processedCount < MaxComboCommandsPerFixedUpdate
                && inputReader.TryConsumeCombatCommand(out QusapCombatCommandPress press))
            {
                processedCount++;
                if (press.Command == QusapCombatCommand.Parry)
                {
                    continue;
                }

                QusapComboMatchResult preview = comboMatcher.PreviewPress(
                    press.Command,
                    press.PressId,
                    press.Timestamp);

                if (preview.Completed && preview.CompletedComboId.HasValue)
                {
                    if (!IsValidComboTarget(comboTarget))
                    {
                        ResetComboRecognition();
                        return false;
                    }

                    lastComboMatchResult = comboMatcher.ProcessPress(
                        press.Command,
                        press.PressId,
                        press.Timestamp);
                    if (!lastComboMatchResult.Completed
                        || lastComboMatchResult.CompletedComboId != preview.CompletedComboId)
                    {
                        ResetComboRecognition();
                        return false;
                    }

                    ArmFinisher(lastComboMatchResult.CompletedComboId.Value, comboTarget);
                    continue;
                }

                if (preview.Advanced && preview.CandidatesRemain)
                {
                    bool startsNewSequence = !comboMatcher.HasActiveCandidates
                        || !ContinuesExistingCandidate(preview);
                    if (!TryStartComboSetupAttack(press.Command, grounded))
                    {
                        ResetComboRecognition();
                        return false;
                    }

                    pendingComboPress = press;
                    pendingComboAttackExecutionId = currentAttackExecutionId;
                    pendingComboStartsNewSequence = startsNewSequence;
                    hasPendingComboStep = true;
                    return true;
                }

                lastComboMatchResult = comboMatcher.ProcessPress(
                    press.Command,
                    press.PressId,
                    press.Timestamp);

                if (!comboMatcher.HasActiveCandidates)
                {
                    comboTarget = null;
                }

                if (TryGetLegacyAttack(press.Command, out QusapAttackType legacyAttack)
                    && TryStartAttack(legacyAttack, grounded))
                {
                    return true;
                }
            }

            return false;
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

            ConfirmPendingComboStep(receiver);

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

        private void ConfirmPendingComboStep(QusapHitReceiver receiver)
        {
            if (!hasPendingComboStep
                || pendingComboAttackExecutionId != currentAttackExecutionId)
            {
                return;
            }

            if (!pendingComboStartsNewSequence
                && comboMatcher.HasActiveCandidates
                && !IsValidComboTarget(comboTarget))
            {
                ResetComboRecognition();
                return;
            }

            if (!pendingComboStartsNewSequence
                && comboTarget != null
                && receiver != comboTarget)
            {
                return;
            }

            if (pendingComboStartsNewSequence || comboTarget == null)
            {
                comboTarget = receiver;
            }

            QusapCombatCommandPress confirmedPress = pendingComboPress;
            lastComboMatchResult = comboMatcher.ProcessPress(
                confirmedPress.Command,
                confirmedPress.PressId,
                confirmedPress.Timestamp);

            hasPendingComboStep = false;
            pendingComboPress = default;
            pendingComboAttackExecutionId = 0;
            pendingComboStartsNewSequence = false;

            if (!lastComboMatchResult.Advanced || !lastComboMatchResult.CandidatesRemain)
            {
                ResetComboRecognition();
                return;
            }

            if (logRecognizedCombos)
            {
                Debug.Log(
                    $"[Qusap Combo] {gameObject.name} confirmed {confirmedPress.Command} on {receiver.gameObject.name}",
                    this);
            }
        }

        private void ArmFinisher(QusapComboId comboId, QusapHitReceiver target)
        {
            CancelOutgoingFinisher();
            HasArmedFinisher = true;
            ArmedFinisherCombo = comboId;
            ArmedFinisherTarget = target;
            LastCompletedCombo = comboId;
            finisherWindowEventEmitted = false;
            finisherReadyEventEmitted = false;
            finisherParriedEventEmitted = false;
            finisherDefense.Arm(
                comboId,
                InputState.currentTime,
                finisherParrySettings);
            armedFinisherDefender = target.GetComponent<QusapCombatController>();
            armedFinisherDefender?.RegisterIncomingFinisher(this);
            FinisherArmed?.Invoke(comboId, target);
            ComboCompleted?.Invoke(comboId);

            if (logRecognizedCombos)
            {
                Debug.Log(
                    $"[Qusap Combo] {gameObject.name} armed {comboId} against {target.gameObject.name}",
                    this);
            }
        }

        public bool TryConsumeReadyFinisher(
            out QusapComboId comboId,
            out QusapHitReceiver target)
        {
            if (!HasFinisherReadyToResolve
                || !ArmedFinisherCombo.HasValue
                || !IsValidFinisherTarget(ArmedFinisherTarget))
            {
                comboId = default;
                target = null;
                if (HasArmedFinisher && !IsValidFinisherTarget(ArmedFinisherTarget))
                {
                    CancelOutgoingFinisher();
                }

                return false;
            }

            comboId = ArmedFinisherCombo.Value;
            target = ArmedFinisherTarget;
            UnregisterFromFinisherDefender();
            HasArmedFinisher = false;
            ArmedFinisherCombo = null;
            ArmedFinisherTarget = null;
            finisherDefense.Reset();
            return true;
        }

        internal void UpdateFinisherDefense(double timestamp)
        {
            if (finisherDefense == null)
            {
                return;
            }

            if (HasArmedFinisher && !IsValidFinisherTarget(ArmedFinisherTarget))
            {
                CancelOutgoingFinisher();
                return;
            }

            PublishFinisherTransition(finisherDefense.Advance(timestamp));
        }

        private void PublishFinisherTransition(QusapFinisherDefenseTransition transition)
        {
            if (!HasArmedFinisher
                || !ArmedFinisherCombo.HasValue
                || ArmedFinisherTarget == null)
            {
                return;
            }

            QusapComboId comboId = ArmedFinisherCombo.Value;
            QusapHitReceiver target = ArmedFinisherTarget;
            if (transition.WindowOpened && !finisherWindowEventEmitted)
            {
                finisherWindowEventEmitted = true;
                FinisherParryWindowOpened?.Invoke(comboId, target);
                if (logRecognizedCombos)
                {
                    Debug.Log(
                        $"[Qusap Parry] {target.gameObject.name} parry window opened for {comboId} from {gameObject.name}",
                        this);
                }
            }

            if (transition.BecameReadyToResolve && !finisherReadyEventEmitted)
            {
                finisherReadyEventEmitted = true;
                UnregisterFromFinisherDefender();
                FinisherReadyToResolve?.Invoke(comboId, target);
                if (logRecognizedCombos)
                {
                    Debug.Log(
                        $"[Qusap Finisher] {gameObject.name} {comboId} ready to resolve against {target.gameObject.name}",
                        this);
                }
            }
        }

        private void MarkFinisherParried(QusapCombatController defender)
        {
            if (finisherDefense == null
                || finisherDefense.Phase != QusapFinisherDefensePhase.Parried
                || !HasArmedFinisher
                || !ArmedFinisherCombo.HasValue
                || ArmedFinisherTarget == null)
            {
                return;
            }

            QusapComboId comboId = ArmedFinisherCombo.Value;
            QusapHitReceiver target = ArmedFinisherTarget;
            UnregisterFromFinisherDefender();
            HasArmedFinisher = false;
            ArmedFinisherCombo = null;
            ArmedFinisherTarget = null;

            if (!finisherParriedEventEmitted)
            {
                finisherParriedEventEmitted = true;
                FinisherParried?.Invoke(comboId, target);
            }

            if (logRecognizedCombos)
            {
                Debug.Log(
                    $"[Qusap Parry] {defender.gameObject.name} parried {comboId} from {gameObject.name}",
                    this);
            }
        }

        private void ApplySuccessfulParryVulnerability()
        {
            suppressFinisherReset = true;
            try
            {
                hitstunController.EnterHitstun(
                    (float)finisherParrySettings.SuccessfulParryAttackerVulnerabilityDuration);
            }
            finally
            {
                suppressFinisherReset = false;
            }
        }

        private void CancelOutgoingFinisher()
        {
            if (finisherDefense == null)
            {
                return;
            }

            UnregisterFromFinisherDefender();
            finisherDefense.Cancel();
            HasArmedFinisher = false;
            ArmedFinisherCombo = null;
            ArmedFinisherTarget = null;
        }

        private void UnregisterFromFinisherDefender()
        {
            QusapCombatController defender = armedFinisherDefender;
            armedFinisherDefender = null;
            if (defender != null)
            {
                defender.UnregisterIncomingFinisher(this);
            }
        }

        private bool BlocksOffenseForFinisher()
        {
            return finisherDefense != null
                && (finisherDefense.Phase == QusapFinisherDefensePhase.Telegraph
                    || finisherDefense.Phase == QusapFinisherDefensePhase.ParryWindow);
        }

        private static bool IsValidFinisherTarget(QusapHitReceiver target)
        {
            if (target == null
                || !target.isActiveAndEnabled
                || !target.gameObject.activeInHierarchy
                || !target.AcceptsHits)
            {
                return false;
            }

            QusapCombatController targetCombat = target.GetComponent<QusapCombatController>();
            return targetCombat == null || targetCombat.CombatAllowed;
        }

        private void SubscribeInputEvents()
        {
            if (inputEventsSubscribed || inputReader == null)
            {
                return;
            }

            inputReader.ParryPressed += HandleParryPressed;
            inputEventsSubscribed = true;
        }

        private void UnsubscribeInputEvents()
        {
            if (!inputEventsSubscribed || inputReader == null)
            {
                return;
            }

            inputReader.ParryPressed -= HandleParryPressed;
            inputEventsSubscribed = false;
        }

        private void RegisterIncomingFinisher(QusapCombatController attacker)
        {
            CleanupIncomingFinishers();
            if (attacker != null && !incomingFinishers.Contains(attacker))
            {
                incomingFinishers.Add(attacker);
            }
        }

        private void UnregisterIncomingFinisher(QusapCombatController attacker)
        {
            incomingFinishers.Remove(attacker);
        }

        private void CleanupIncomingFinishers()
        {
            for (int i = incomingFinishers.Count - 1; i >= 0; i--)
            {
                QusapCombatController attacker = incomingFinishers[i];
                if (attacker == null || !attacker.IsIncomingOpportunityFor(this))
                {
                    incomingFinishers.RemoveAt(i);
                }
            }
        }

        private bool IsIncomingOpportunityFor(QusapCombatController defender)
        {
            if (defender == null
                || !HasArmedFinisher
                || ArmedFinisherTarget != defender.HitReceiver
                || !isActiveAndEnabled
                || !gameObject.activeInHierarchy)
            {
                return false;
            }

            return finisherDefense.Phase == QusapFinisherDefensePhase.Telegraph
                || finisherDefense.Phase == QusapFinisherDefensePhase.ParryWindow;
        }

        private void CancelIncomingFinishers()
        {
            if (incomingFinishers.Count == 0)
            {
                return;
            }

            QusapCombatController[] attackers = incomingFinishers.ToArray();
            incomingFinishers.Clear();
            for (int i = 0; i < attackers.Length; i++)
            {
                QusapCombatController attacker = attackers[i];
                if (attacker != null && attacker.armedFinisherDefender == this)
                {
                    attacker.CancelOutgoingFinisher();
                }
            }
        }

        private void HandleParryPressed(QusapCombatCommandPress press)
        {
            if (press.Command != QusapCombatCommand.Parry)
            {
                return;
            }

            if (hasHandledParryPress && press.PressId <= lastHandledParryPressId)
            {
                return;
            }

            hasHandledParryPress = true;
            lastHandledParryPressId = press.PressId;
            if (parryFailurePending)
            {
                return;
            }

            bool eligible = combatAllowed
                && isActiveAndEnabled
                && gameObject.activeInHierarchy
                && !IsAttacking
                && (dashMotor == null || !dashMotor.IsDashing)
                && (hitstunController == null || !hitstunController.IsInHitstun);

            CleanupIncomingFinishers();
            if (!eligible)
            {
                QueueFailedParry(QusapParryAttemptOutcome.Ineligible);
                return;
            }

            if (incomingFinishers.Count == 0)
            {
                QueueFailedParry(QusapParryAttemptOutcome.NoIncomingFinisher);
                return;
            }

            QusapCombatController selected = SelectIncomingFinisher(press.Timestamp, requireOpenWindow: true);
            if (selected == null)
            {
                selected = SelectIncomingFinisher(press.Timestamp, requireOpenWindow: false);
            }

            if (selected == null)
            {
                QueueFailedParry(QusapParryAttemptOutcome.NoIncomingFinisher);
                return;
            }

            QusapComboId comboId = selected.ArmedFinisherCombo.Value;
            QusapParryAttemptResult result = selected.finisherDefense.TryParry(
                press.PressId,
                press.Timestamp);
            selected.PublishFinisherTransition(result.Transition);

            if (result.Succeeded)
            {
                selected.MarkFinisherParried(this);
                pendingParryResolutions.Enqueue(
                    PendingParryResolution.Success(selected, comboId));
                return;
            }

            if (result.Outcome == QusapParryAttemptOutcome.DuplicateOrStalePressIgnored
                || result.Outcome == QusapParryAttemptOutcome.InvalidTimestampIgnored)
            {
                return;
            }

            QueueFailedParry(result.Outcome);
        }

        private QusapCombatController SelectIncomingFinisher(
            double timestamp,
            bool requireOpenWindow)
        {
            QusapCombatController selected = null;
            for (int i = 0; i < incomingFinishers.Count; i++)
            {
                QusapCombatController candidate = incomingFinishers[i];
                bool timestampInside = timestamp >= candidate.ParryWindowOpensAt
                    && timestamp <= candidate.ParryWindowClosesAt;
                if (requireOpenWindow != timestampInside)
                {
                    continue;
                }

                if (selected == null
                    || candidate.ParryWindowClosesAt < selected.ParryWindowClosesAt
                    || (candidate.ParryWindowClosesAt == selected.ParryWindowClosesAt
                        && EntityId.ToULong(candidate.GetEntityId())
                            < EntityId.ToULong(selected.GetEntityId())))
                {
                    selected = candidate;
                }
            }

            return selected;
        }

        private void QueueFailedParry(QusapParryAttemptOutcome outcome)
        {
            parryFailurePending = true;
            pendingParryResolutions.Enqueue(PendingParryResolution.Failure(outcome));
        }

        private void ProcessPendingParryResolutions()
        {
            while (pendingParryResolutions.Count > 0)
            {
                PendingParryResolution resolution = pendingParryResolutions.Dequeue();
                if (resolution.Outcome == QusapParryAttemptOutcome.Success)
                {
                    if (resolution.Attacker != null)
                    {
                        resolution.Attacker.ApplySuccessfulParryVulnerability();
                        ParrySucceeded?.Invoke(resolution.Attacker, resolution.ComboId);
                    }

                    continue;
                }

                pendingParryResolutions.Clear();
                parryFailurePending = false;
                ParryFailed?.Invoke(resolution.Outcome);
                if (logRecognizedCombos)
                {
                    Debug.Log(
                        $"[Qusap Parry] {gameObject.name} missed parry: {resolution.Outcome}",
                        this);
                }

                hitstunController.EnterHitstun(
                    (float)finisherParrySettings.FailedParryDefenderVulnerabilityDuration);
                return;
            }

            parryFailurePending = false;
        }

        private void HandleOwnerHitReceived(QusapHitInfo hitInfo)
        {
            if (IsComboSetupAttack)
            {
                CancelCurrentAttack(true);
            }

            ResetComboRecognition();
        }

        private readonly struct PendingParryResolution
        {
            private PendingParryResolution(
                QusapParryAttemptOutcome outcome,
                QusapCombatController attacker,
                QusapComboId comboId)
            {
                Outcome = outcome;
                Attacker = attacker;
                ComboId = comboId;
            }

            public QusapParryAttemptOutcome Outcome { get; }
            public QusapCombatController Attacker { get; }
            public QusapComboId ComboId { get; }

            public static PendingParryResolution Success(
                QusapCombatController attacker,
                QusapComboId comboId)
            {
                return new PendingParryResolution(
                    QusapParryAttemptOutcome.Success, attacker, comboId);
            }

            public static PendingParryResolution Failure(QusapParryAttemptOutcome outcome)
            {
                return new PendingParryResolution(outcome, null, default);
            }
        }

        private static bool TryGetLegacyAttack(
            QusapCombatCommand command,
            out QusapAttackType attackType)
        {
            switch (command)
            {
                case QusapCombatCommand.BodyAttack:
                    attackType = QusapAttackType.WeakKick;
                    return true;
                case QusapCombatCommand.WeaponLight:
                    attackType = QusapAttackType.StrongKick;
                    return true;
                case QusapCombatCommand.Headbutt:
                    attackType = QusapAttackType.Headbutt;
                    return true;
                default:
                    attackType = default;
                    return false;
            }
        }

        private static bool IsValidComboTarget(QusapHitReceiver target)
        {
            return target != null
                && target.isActiveAndEnabled
                && target.gameObject.activeInHierarchy;
        }

        private bool ContinuesExistingCandidate(QusapComboMatchResult preview)
        {
            for (int i = 0; i < preview.ActiveComboIds.Count; i++)
            {
                if (comboMatcher.HasActiveCandidate(preview.ActiveComboIds[i]))
                {
                    return true;
                }
            }

            return false;
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
            ClearComboSetupAttackState();
            phaseTimeRemaining = 0f;
            CurrentPhase = QusapAttackPhase.Idle;
            CurrentAttackVariant = QusapAttackVariant.None;
            AttackFinished?.Invoke(finishedAttack);
            AttackEnded?.Invoke(finishedVariant, false);
            AttackPhaseChanged?.Invoke(QusapAttackVariant.None, QusapAttackPhase.Idle);

            if (hasPendingComboStep
                && pendingComboAttackExecutionId == currentAttackExecutionId)
            {
                ResetComboRecognition();
            }
        }

        private void CancelCurrentAttack(bool notifyFinished)
        {
            if (!IsAttacking || currentAttack == null)
            {
                attackHitbox?.EndAttack();
                ClearComboSetupAttackState();
                CurrentPhase = QusapAttackPhase.Idle;
                CurrentAttackVariant = QusapAttackVariant.None;
                return;
            }

            QusapAttackType canceledAttack = currentAttack.AttackType;
            QusapAttackVariant canceledVariant = CurrentAttackVariant;
            attackHitbox?.EndAttack();
            currentAttack = null;
            ClearComboSetupAttackState();
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
            comboBodyAttackGround ??= QusapAttackData.CreateComboBodyAttackGround();
            comboWeaponLightGround ??= QusapAttackData.CreateComboWeaponLightGround();
            comboBodyAttackAir ??= QusapAirAttackData.CreateComboBodyAttackAir();
            comboWeaponLightAir ??= QusapAirAttackData.CreateComboWeaponLightAir();

            weakKick.SetAttackType(QusapAttackType.WeakKick);
            strongKick.SetAttackType(QusapAttackType.StrongKick);
            headbutt.SetAttackType(QusapAttackType.Headbutt);
            weakKickAir.SetAttackType(QusapAttackType.WeakKick);
            strongKickAir.SetAttackType(QusapAttackType.StrongKick);
            diveHeadbuttAir.SetAttackType(QusapAttackType.Headbutt);
            comboBodyAttackGround.SetAttackType(QusapAttackType.WeakKick);
            comboWeaponLightGround.SetAttackType(QusapAttackType.StrongKick);
            comboBodyAttackAir.SetAttackType(QusapAttackType.WeakKick);
            comboWeaponLightAir.SetAttackType(QusapAttackType.StrongKick);

            weakKick.Validate();
            strongKick.Validate();
            headbutt.Validate();
            weakKickAir.Validate();
            strongKickAir.Validate();
            diveHeadbuttAir.Validate();
            comboBodyAttackGround.Validate();
            comboWeaponLightGround.Validate();
            comboBodyAttackAir.Validate();
            comboWeaponLightAir.Validate();
        }

        private void ClearComboSetupAttackState()
        {
            IsComboSetupAttack = false;
            CurrentComboSetupCommand = null;
        }

        private static bool IsAirVariant(QusapAttackVariant variant)
        {
            return variant == QusapAttackVariant.WeakKickAir
                || variant == QusapAttackVariant.StrongKickAir
                || variant == QusapAttackVariant.DiveHeadbuttAir;
        }
    }
}
