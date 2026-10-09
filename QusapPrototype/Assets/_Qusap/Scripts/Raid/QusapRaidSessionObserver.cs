using System.Collections.Generic;
using UnityEngine;

namespace Qusap
{
    [DefaultExecutionOrder(700)]
    [DisallowMultipleComponent]
    public sealed class QusapRaidSessionObserver : MonoBehaviour
    {
        [SerializeField] private QusapRaidBootstrap raid;
        [SerializeField, Range(1, 8)] private int participantLimit = 8;
        private readonly Dictionary<string, QusapRaidInventory> participants = new(System.StringComparer.Ordinal);
        private readonly Dictionary<QusapHitReceiver, QusapRaidInventory> receivers = new();
        private QusapRaidSession ledger;
        public QusapRaidSessionAuthority Authority { get; private set; }
        public QusapRaidBootstrap Raid => raid;
        public void Configure(QusapRaidBootstrap bootstrap, int limit = 8)
        {
            if (Authority != null) throw new System.InvalidOperationException("Observer already initialized.");
            raid = bootstrap; participantLimit = limit;
        }
        private void Start()
        {
            ledger = raid.Session;
            Authority = new QusapRaidSessionAuthority(ledger.World.RaidId, participantLimit);
            foreach (var player in raid.Participants)
            {
                if (player == null || player.State == null || player.Session != ledger || player.Receiver == null
                    || receivers.ContainsKey(player.Receiver) || !Authority.Register(player.ParticipantId))
                    throw new System.InvalidOperationException("Invalid raid participant configuration.");
                participants.Add(player.ParticipantId, player); receivers.Add(player.Receiver, player);
            }
            ledger.ExtractionSettled += Extracted;
            foreach (var receiver in receivers.Keys) receiver.Eliminated += Eliminated;
        }
        public bool StartSession()
        {
            if (Authority == null) return false;
            if (Authority.Status == QusapRaidSessionStatus.Waiting)
                foreach (var player in participants.Values)
                    if (!player.State.CanOperate || player.Receiver.IsHealthDepleted)
                        return Authority.RejectSignal(QusapRaidSessionRejection.AuthoritativeSourceMismatch);
            return Authority.StartSession();
        }
        private void Extracted(QusapRaidInventoryState state) => ObserveExtraction(state);
        private void Eliminated(QusapHitReceiver receiver) => ObserveElimination(receiver);
        // Same entry points for diagnostics that replay an existing notification; never settle loot here.
        public bool ObserveExtraction(QusapRaidInventoryState state)
        {
            if (Authority == null) return false;
            if (state == null || !participants.TryGetValue(state.ParticipantId, out var player)
                || !ReferenceEquals(player.State, state) || state.Status != QusapRaidInventoryStatus.Extracted)
                return Authority.RejectSignal(QusapRaidSessionRejection.AuthoritativeSourceMismatch);
            return Authority.TryResolve(state.ParticipantId, QusapRaidParticipantOutcome.Extracted);
        }
        public bool ObserveElimination(QusapHitReceiver receiver)
        {
            if (Authority == null) return false;
            if (receiver == null || !receivers.TryGetValue(receiver, out var player) || !receiver.IsEliminated)
                return Authority.RejectSignal(QusapRaidSessionRejection.AuthoritativeSourceMismatch);
            return Authority.TryResolve(player.ParticipantId, QusapRaidParticipantOutcome.Eliminated);
        }
        private void OnDestroy()
        {
            if (ledger != null) ledger.ExtractionSettled -= Extracted;
            foreach (var receiver in receivers.Keys) if (receiver != null) receiver.Eliminated -= Eliminated;
        }
    }
}
