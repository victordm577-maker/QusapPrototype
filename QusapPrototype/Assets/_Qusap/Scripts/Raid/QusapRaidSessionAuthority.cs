using System;
using System.Collections.Generic;
using System.Linq;

namespace Qusap
{
    public enum QusapRaidSessionStatus { Waiting, Running, Finished }
    public enum QusapRaidParticipantOutcome { Active, Extracted, Eliminated, Winner }
    public enum QusapRaidSessionFinishReason { AllParticipantsResolved, LastSurvivor, Expired }
    public enum QusapRaidSessionRejection
    {
        None, NotWaiting, NoParticipants, InvalidId, DuplicateParticipant, CapacityReached,
        NotRunning, UnknownParticipant, InvalidOutcome, AlreadyResolved, AuthoritativeSourceMismatch
    }

    public sealed class QusapRaidParticipantResult
    {
        internal QusapRaidParticipantResult(string id, QusapRaidParticipantOutcome outcome)
        { ParticipantId = id; Outcome = outcome; }
        public string ParticipantId { get; }
        public QusapRaidParticipantOutcome Outcome { get; }
    }

    public sealed class QusapRaidSessionResult
    {
        internal QusapRaidSessionResult(string sessionId, IEnumerable<QusapRaidParticipantResult> participants, bool expired = false)
        {
            SessionId = sessionId;
            Participants = Array.AsReadOnly(participants.OrderBy(p => p.ParticipantId, StringComparer.Ordinal).ToArray());
            ExtractedCount = Participants.Count(p => p.Outcome == QusapRaidParticipantOutcome.Extracted);
            EliminatedCount = Participants.Count(p => p.Outcome == QusapRaidParticipantOutcome.Eliminated);
            WinnerId = Participants.SingleOrDefault(p => p.Outcome == QusapRaidParticipantOutcome.Winner)?.ParticipantId;
            Reason = expired ? QusapRaidSessionFinishReason.Expired : WinnerId == null
                ? QusapRaidSessionFinishReason.AllParticipantsResolved : QusapRaidSessionFinishReason.LastSurvivor;
        }
        public string SessionId { get; }
        public int ParticipantCount => Participants.Count;
        public IReadOnlyList<QusapRaidParticipantResult> Participants { get; }
        public int ExtractedCount { get; }
        public int EliminatedCount { get; }
        public string WinnerId { get; }
        public QusapRaidSessionFinishReason Reason { get; }
    }

    // Session authority decides victory. The injected transaction must commit before Winner is published.
    public sealed class QusapRaidSessionAuthority
    {
        public const int MaximumParticipants = 8;
        private readonly Dictionary<string, QusapRaidParticipantOutcome> outcomes = new(StringComparer.Ordinal);
        private Func<string, bool> commitWinner;
        private Func<string, QusapRaidParticipantOutcome, bool> validateResolution;
        private bool evaluating;
        private int environmentalDamageDepth;
        private readonly HashSet<string> environmentalDeaths = new(StringComparer.Ordinal);
        internal bool ResolutionInProgress => evaluating || environmentalDamageDepth != 0;
        public int EnvironmentalDeathsPending => environmentalDeaths.Count;
        public bool EnvironmentalDamageInProgress => environmentalDamageDepth != 0;
        public bool RosterClosed { get; private set; }
        public bool SharedDeploymentCompleted { get; private set; }
        public bool ExpirationStarted { get; private set; }
        public bool LastSurvivorEnabled => commitWinner != null;
        public string WinnerId => FinalResult?.WinnerId;
        internal void BindResolutionValidator(Func<string, QusapRaidParticipantOutcome, bool> validator)
        { validateResolution = validator; }
        public QusapRaidSessionAuthority(string sessionId, int participantLimit = MaximumParticipants)
        {
            if (string.IsNullOrWhiteSpace(sessionId)) throw new ArgumentException("Session ID required.", nameof(sessionId));
            if (participantLimit < 1 || participantLimit > MaximumParticipants) throw new ArgumentOutOfRangeException(nameof(participantLimit));
            SessionId = sessionId; ParticipantLimit = participantLimit;
        }
        public string SessionId { get; }
        public int ParticipantLimit { get; }
        public QusapRaidSessionStatus Status { get; private set; }
        public int ParticipantCount => outcomes.Count;
        public int ActiveCount => outcomes.Count(p => p.Value == QusapRaidParticipantOutcome.Active);
        public int ExtractedCount => outcomes.Count(p => p.Value == QusapRaidParticipantOutcome.Extracted);
        public int EliminatedCount => outcomes.Count(p => p.Value == QusapRaidParticipantOutcome.Eliminated);
        public int SessionFinishedEvents { get; private set; }
        public int RejectedSignals { get; private set; }
        public QusapRaidSessionRejection LastRejection { get; private set; }
        public QusapRaidSessionResult FinalResult { get; private set; }
        public event Action<QusapRaidSessionResult> SessionFinished;

        public bool Register(string participantId)
        {
            if (Status != QusapRaidSessionStatus.Waiting || RosterClosed) return Reject(QusapRaidSessionRejection.NotWaiting);
            if (string.IsNullOrWhiteSpace(participantId)) return Reject(QusapRaidSessionRejection.InvalidId);
            if (outcomes.ContainsKey(participantId)) return Reject(QusapRaidSessionRejection.DuplicateParticipant);
            if (ParticipantCount >= ParticipantLimit) return Reject(QusapRaidSessionRejection.CapacityReached);
            outcomes.Add(participantId, QusapRaidParticipantOutcome.Active);
            LastRejection = QusapRaidSessionRejection.None; return true;
        }
        public bool StartSession()
        {
            if (Status != QusapRaidSessionStatus.Waiting) return Reject(QusapRaidSessionRejection.NotWaiting);
            if (ParticipantCount == 0) return Reject(QusapRaidSessionRejection.NoParticipants);
            Status = QusapRaidSessionStatus.Running; LastRejection = QusapRaidSessionRejection.None; return true;
        }
        public bool TryGetOutcome(string id, out QusapRaidParticipantOutcome outcome)
        {
            outcome = default;
            return id != null && outcomes.TryGetValue(id, out outcome);
        }
        public IReadOnlyList<QusapRaidParticipantResult> ReadParticipantsSnapshot() => Array.AsReadOnly(outcomes
            .OrderBy(p => p.Key, StringComparer.Ordinal).Select(p => new QusapRaidParticipantResult(p.Key, p.Value)).ToArray());

        internal bool CompleteSharedDeployment(IReadOnlyList<string> deployed, Func<string, bool> transaction)
        {
            if (Status != QusapRaidSessionStatus.Waiting || RosterClosed || transaction == null || deployed == null
                || deployed.Count < 2 || deployed.Count > MaximumParticipants || deployed.Distinct(StringComparer.Ordinal).Count() != deployed.Count
                || !new HashSet<string>(deployed, StringComparer.Ordinal).SetEquals(outcomes.Keys)
                || ActiveCount != ParticipantCount) return false;
            commitWinner = transaction; SharedDeploymentCompleted = true; RosterClosed = true;
            return true;
        }

        // Called by the event observer after validating the registered authoritative source.
        internal bool BeginExpiration()
        {
            if (evaluating || Status != QusapRaidSessionStatus.Running || !SharedDeploymentCompleted || !RosterClosed) return false;
            ExpirationStarted = true; return true;
        }
        internal bool BeginEnvironmentalDamage()
        {
            if (ResolutionInProgress || ExpirationStarted || Status != QusapRaidSessionStatus.Running
                || !SharedDeploymentCompleted || !RosterClosed) return false;
            environmentalDamageDepth++;
            return true;
        }
        internal void RecordEnvironmentalDeath(string id)
        {
            if (outcomes.TryGetValue(id, out var outcome) && outcome == QusapRaidParticipantOutcome.Active)
                environmentalDeaths.Add(id);
        }
        internal void EndEnvironmentalDamage()
        {
            environmentalDamageDepth--;
            EvaluateVictory();
        }
        public bool TryResolve(string participantId, QusapRaidParticipantOutcome outcome)
        {
            if (evaluating) return RejectSignal(QusapRaidSessionRejection.AlreadyResolved);
            if (Status != QusapRaidSessionStatus.Running) return RejectSignal(QusapRaidSessionRejection.NotRunning);
            if (participantId == null || !outcomes.TryGetValue(participantId, out var current))
                return RejectSignal(QusapRaidSessionRejection.UnknownParticipant);
            if (outcome != QusapRaidParticipantOutcome.Extracted && outcome != QusapRaidParticipantOutcome.Eliminated)
                return RejectSignal(QusapRaidSessionRejection.InvalidOutcome);
            if (ExpirationStarted && outcome != QusapRaidParticipantOutcome.Eliminated)
                return RejectSignal(QusapRaidSessionRejection.NotRunning);
            if (current != QusapRaidParticipantOutcome.Active) return RejectSignal(QusapRaidSessionRejection.AlreadyResolved);
            if (validateResolution != null && !validateResolution(participantId, outcome))
                return RejectSignal(QusapRaidSessionRejection.AuthoritativeSourceMismatch);
            outcomes[participantId] = outcome; LastRejection = QusapRaidSessionRejection.None;
            if (outcome == QusapRaidParticipantOutcome.Eliminated) environmentalDeaths.Remove(participantId);
            EvaluateVictory();
            return true;
        }
        public bool EvaluateVictory()
        {
            if (evaluating || environmentalDamageDepth != 0 || environmentalDeaths.Count != 0
                || Status != QusapRaidSessionStatus.Running) return false;
            if (LastSurvivorEnabled && (!SharedDeploymentCompleted || !RosterClosed || ParticipantCount < 2)) return false;
            evaluating = true;
            try
            {
                if (!ExpirationStarted && LastSurvivorEnabled && ActiveCount == 1)
                {
                    string last = outcomes.Single(p => p.Value == QusapRaidParticipantOutcome.Active).Key;
                    if (!commitWinner(last)) return false; // Keep Running and Active for a safe retry.
                    outcomes[last] = QusapRaidParticipantOutcome.Winner;
                }
                if (ActiveCount != 0) return false;
                FinalResult = new QusapRaidSessionResult(SessionId, ReadParticipantsSnapshot(), ExpirationStarted);
                Status = QusapRaidSessionStatus.Finished; SessionFinishedEvents++;
                SessionFinished?.Invoke(FinalResult); return true;
            }
            finally { evaluating = false; }
        }
        public bool RejectSignal(QusapRaidSessionRejection reason)
        { RejectedSignals++; return Reject(reason); }
        private bool Reject(QusapRaidSessionRejection reason) { LastRejection = reason; return false; }
    }
}
