using System;
using UnityEngine;

namespace Qusap
{
    [DefaultExecutionOrder(1000)]
    [DisallowMultipleComponent]
    public sealed class QusapLootInteractor : MonoBehaviour
    {
        [SerializeField, Min(.01f)] private float holdDuration = .45f;
        private QusapHitReceiver receiver;
        private QusapHitstunController hitstun;
        private QusapDashMotor dash;
        private QusapCombatController combat;
        private QusapInputReader nativeInput;
        private QusapRaidSessionAuthority session;
        private IQusapWorldLootAuthority authority;
        private IQusapLootInteractionInput input;
        private QusapWorldLootContainerView opened;
        private QusapWorldLootTakeRequest? pendingTake;
        private QusapLootInteractionCommand queued;
        private bool wasHeld;
        public string PlayerId { get; private set; }
        public string InteractorId { get; private set; }
        public QusapLootProximitySensor Sensor { get; private set; }
        public QusapLootInteractionProgress Progress { get; private set; }
        public QusapWorldLootContainerSnapshot Observed { get; private set; }
        public string SelectedInstanceId { get; private set; }
        public QusapWorldLootReply LastReply { get; private set; }
        public QusapLootInteractionRejection LastRejection { get; private set; }
        public string BindingLabel => input?.BindingLabel ?? "Entrada sin asignar";
        public bool PresentationOpen => opened != null && Observed != null;
        public bool Incapacitated => !isActiveAndEnabled || receiver == null || receiver.IsHealthDepleted || receiver.IsGameplayRetired
            || (hitstun != null && hitstun.IsInHitstun) || (dash != null && dash.IsDashing)
            || (combat != null && combat.IsAttacking) || (nativeInput != null && nativeInput.IsGameplayInputBlocked);
        public event Action<QusapLootInteractionRejection> Cancelled;

        public void Configure(string playerId, string interactorId, QusapLootProximitySensor sensor,
            IQusapLootInteractionInput commands, IQusapWorldLootAuthority owner, QusapRaidSessionAuthority raid, float duration = .45f)
        {
            if (string.IsNullOrWhiteSpace(playerId) || string.IsNullOrWhiteSpace(interactorId)) throw new ArgumentException("Interaction identities required");
            Unsubscribe(); PlayerId = playerId; InteractorId = interactorId;
            Sensor = sensor ?? throw new ArgumentNullException(nameof(sensor)); input = commands ?? throw new ArgumentNullException(nameof(commands));
            authority = owner ?? throw new ArgumentNullException(nameof(owner)); session = raid ?? throw new ArgumentNullException(nameof(raid));
            holdDuration = duration; Progress = new(duration);
            receiver = GetComponent<QusapHitReceiver>(); hitstun = GetComponent<QusapHitstunController>();
            dash = GetComponent<QusapDashMotor>(); combat = GetComponent<QusapCombatController>(); nativeInput = GetComponent<QusapInputReader>();
            Subscribe();
        }
        private void Subscribe()
        {
            if (receiver != null) { receiver.HealthChanged += Health; receiver.Eliminated += Eliminated; }
            if (hitstun != null) hitstun.HitstunStarted += IncapacitatedSignal;
            if (combat != null) combat.AttackStarted += Attack;
            if (session != null) session.SessionFinished += Finished;
        }
        private void Unsubscribe()
        {
            if (receiver != null) { receiver.HealthChanged -= Health; receiver.Eliminated -= Eliminated; }
            if (hitstun != null) hitstun.HitstunStarted -= IncapacitatedSignal;
            if (combat != null) combat.AttackStarted -= Attack;
            if (session != null) session.SessionFinished -= Finished;
        }
        private void OnEnable() { if (Progress != null) Subscribe(); }
        private void OnDisable() { Cancel(QusapLootInteractionRejection.Incapacitated); Close(); Unsubscribe(); }
        private void Health(QusapHealthChange change) { if (change.TotalDamage > change.PreviousDamage) IncapacitatedSignal(); }
        private void Eliminated(QusapHitReceiver _) => IncapacitatedSignal();
        private void IncapacitatedSignal() { Cancel(QusapLootInteractionRejection.Incapacitated); Close(); }
        private void Attack(QusapAttackType _) => IncapacitatedSignal();
        private void Finished(QusapRaidSessionResult _) { Cancel(QusapLootInteractionRejection.SessionNotRunning); Close(); }
        public QusapLootInteractionRejection Validate(QusapWorldLootContainerView target)
        {
            if (session == null || session.Status != QusapRaidSessionStatus.Running) return QusapLootInteractionRejection.SessionNotRunning;
            if (Incapacitated) return QusapLootInteractionRejection.Incapacitated;
            return Sensor != null ? Sensor.Validate(target) : QusapLootInteractionRejection.InvalidTarget;
        }
        private void FixedUpdate()
        {
            // Dash has no start event. Read its authoritative flag after its motor on every physical step.
            // Validate before the next progress increment, even when several physics steps occur in one frame.
            if (Progress == null) return;
            if (Progress.IsHolding)
            {
                var target = Sensor.Target;
                var reason = Validate(target);
                if (reason != QusapLootInteractionRejection.None) Cancel(reason);
            }
            if (opened != null && Validate(opened) != QusapLootInteractionRejection.None) Close();
        }
        public void Cancel(QusapLootInteractionRejection reason)
        {
            if (Progress == null || !Progress.IsHolding) return;
            Progress.Cancel(reason); LastRejection = reason; pendingTake = null; Cancelled?.Invoke(reason);
        }
        public void Close() { opened = null; Observed = null; SelectedInstanceId = null; pendingTake = null; }
        public void QueueCommand(QusapLootInteractionCommand command)
        { queued = new(false, queued.SelectionDelta + command.SelectionDelta, queued.TakeOne || command.TakeOne, queued.TakeStack || command.TakeStack, queued.Close || command.Close); }
        public void ReplaceInput(IQusapLootInteractionInput commands) { input = commands ?? throw new ArgumentNullException(nameof(commands)); }

        // Coordinator prepares all players from their displayed snapshots before sending any take.
        // Two commands in one frame therefore retain the same expected version.
        public void PrepareFrame(float delta)
        {
            if (Progress == null || !isActiveAndEnabled) return;
            pendingTake = null; var previousTarget = Sensor.Target; Sensor.Scan();
            var raw = input.ReadCommand(); var command = new QusapLootInteractionCommand(raw.Held, raw.SelectionDelta + queued.SelectionDelta,
                raw.TakeOne || queued.TakeOne, raw.TakeStack || queued.TakeStack, raw.Close || queued.Close); queued = default;
            bool pressed = command.Held && !wasHeld; wasHeld = command.Held;
            if (!command.Held) Cancel(QusapLootInteractionRejection.Released);
            if (command.Close) { Cancel(QusapLootInteractionRejection.Closed); Close(); return; }
            var target = Sensor.Target;
            var validation = Validate(target);
            if (Progress.IsHolding && !string.Equals(Progress.TargetId, target?.SpawnPointId, StringComparison.Ordinal))
                Cancel(target == null ? Validate(previousTarget) : QusapLootInteractionRejection.TargetChanged);
            if (opened != null && (opened != target || Validate(opened) != QusapLootInteractionRejection.None)) Close();
            if (command.Held && validation != QusapLootInteractionRejection.None)
                LastRejection = target == null && !Incapacitated && session.Status == QusapRaidSessionStatus.Running ? Sensor.Validate(Sensor.NearestDiagnostic) : validation;
            var snapshot = target?.Snapshot;
            if (command.Held && snapshot?.State == QusapWorldLootState.Depleted) LastRejection = QusapLootInteractionRejection.Depleted;
            bool complete = Progress.Advance(command.Held, target?.SpawnPointId, snapshot?.State == QusapWorldLootState.Unopened,
                validation, delta);
            if (complete || (pressed && snapshot?.State == QusapWorldLootState.Opened && validation == QusapLootInteractionRejection.None))
            {
                LastReply = authority.RequestOpen(new(PlayerId, target.SpawnPointId));
                if (LastReply.Confirmed) { opened = target; LastRejection = QusapLootInteractionRejection.None; Observe(); }
            }
            if (!PresentationOpen || command.Close) return;
            if (command.SelectionDelta != 0) Select(command.SelectionDelta);
            if (command.TakeOne || command.TakeStack) pendingTake = CreateTakeRequest(command.TakeOne ? 1 : -1);
        }
        public void Select(int delta)
        {
            if (Observed == null || Observed.Items.Count == 0) { SelectedInstanceId = null; return; }
            int index = 0;
            for (int i = 0; i < Observed.Items.Count; i++) if (Observed.Items[i].LootInstanceId == SelectedInstanceId) index = i;
            index = ((index + delta) % Observed.Items.Count + Observed.Items.Count) % Observed.Items.Count;
            SelectedInstanceId = Observed.Items[index].LootInstanceId;
        }
        public QusapWorldLootTakeRequest? CreateTakeRequest(int quantity = -1)
        {
            if (!PresentationOpen || string.IsNullOrEmpty(SelectedInstanceId)) return null;
            foreach (var item in Observed.Items) if (item.LootInstanceId == SelectedInstanceId)
                return new QusapWorldLootTakeRequest(PlayerId, Observed.SpawnPointId, SelectedInstanceId, quantity < 0 ? item.Quantity : quantity, Observed.Version);
            return null;
        }
        public QusapWorldLootReply Submit(QusapWorldLootTakeRequest request)
        {
            var reason = Validate(opened);
            if (request.ParticipantId != PlayerId || opened == null || request.SpawnPointId != opened.SpawnPointId) reason = QusapLootInteractionRejection.InvalidTarget;
            if (reason != QusapLootInteractionRejection.None)
            {
                LastRejection = reason;
                LastReply = new(reason == QusapLootInteractionRejection.Incapacitated || reason == QusapLootInteractionRejection.SessionNotRunning
                    ? QusapWorldLootResult.Blocked : reason == QusapLootInteractionRejection.InvalidTarget ? QusapWorldLootResult.MissingContainer : QusapWorldLootResult.OutOfReach);
                return LastReply;
            }
            LastRejection = QusapLootInteractionRejection.None;
            LastReply = authority.RequestTake(request); return LastReply;
        }
        public void SubmitPrepared() { if (pendingTake.HasValue) Submit(pendingTake.Value); pendingTake = null; }
        public void Observe()
        {
            if (opened == null) return;
            if (Validate(opened) != QusapLootInteractionRejection.None) { Close(); return; }
            Observed = authority.ReadContainer(opened.SpawnPointId);
            bool exists = false;
            if (Observed != null) foreach (var item in Observed.Items) if (item.LootInstanceId == SelectedInstanceId) exists = true;
            if (!exists) SelectedInstanceId = Observed != null && Observed.Items.Count > 0 ? Observed.Items[0].LootInstanceId : null;
        }
    }
}
