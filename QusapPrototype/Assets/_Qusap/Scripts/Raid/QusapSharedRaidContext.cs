using System;
using System.Collections.Generic;
using System.Linq;

namespace Qusap
{
    // Coordinates existing deployments/settlements. Owns one session, never an inventory.
    // The local shared route is atomic over separate reversible in-memory profile stores.
    public sealed class QusapSharedRaidContext : IQusapStashRepository, IQusapVictoryRepository, IDisposable
    {
        private readonly string[] required;
        private readonly Dictionary<string, QusapRaidDeploymentAuthority> players = new(StringComparer.Ordinal);
        private readonly Dictionary<string, QusapPersistentStashRepository> profiles = new(StringComparer.Ordinal);
        private readonly List<Action> rollback = new();
        private bool busy, disposed;
        public QusapSharedRaidContext(params string[] requiredParticipants)
        {
            if (requiredParticipants == null || requiredParticipants.Length < 1 || requiredParticipants.Length > 8
                || requiredParticipants.Any(string.IsNullOrWhiteSpace) || requiredParticipants.Distinct(StringComparer.Ordinal).Count() != requiredParticipants.Length)
                throw new ArgumentException("Distinct required participants expected");
            required = (string[])requiredParticipants.Clone(); RaidId = Guid.NewGuid().ToString("N");
        }
        public string RaidId { get; }
        public QusapRaidSession Session { get; private set; }
        public QusapDeploymentStatus Status { get; private set; }
        public string LastResult { get; private set; } = "Waiting for deployments";
        public IReadOnlyList<QusapRaidDeploymentAuthority> Deployments => Array.AsReadOnly(required.Where(players.ContainsKey).Select(p => players[p]).ToArray());
        private bool Reject(string message) { LastResult = message; return false; }
        public bool Add(QusapRaidDeploymentAuthority deployment)
        {
            if (busy || disposed || Status != QusapDeploymentStatus.None || deployment == null
                || deployment.Status != QusapDeploymentStatus.None || !required.Contains(deployment.ParticipantId)
                || players.ContainsKey(deployment.ParticipantId) || profiles.ContainsKey(deployment.Repository.ProfileId)
                || profiles.Values.Contains(deployment.Repository) || !deployment.Repository.Storage.SupportsMemoryRollback
                || deployment.Repository.HasActiveDeployment)
                return Reject("Duplicate/unavailable participant or profile; reversible in-memory storage required");
            if (!deployment.TryJoinShared(this)) return Reject("Deployment already enlisted elsewhere");
            players.Add(deployment.ParticipantId, deployment); profiles.Add(deployment.Repository.ProfileId, deployment.Repository); return true;
        }
        public bool CommitDeployment()
        {
            if (busy || disposed || Status != QusapDeploymentStatus.None || players.Count != required.Length) return Reject("Required deployments unavailable");
            busy = true;
            try
            {
                foreach (var profile in profiles.Values) rollback.Add(profile.CaptureSharedRollback());
                var session = new QusapRaidSession(RaidId, this);
                // Reserve past every profile counter before creating any loaner or importing items.
                session.World.ReserveInstanceIdsThrough(profiles.Values.Max(p => p.NextItemInstanceId) - 1);
                foreach (var profile in profiles.Values) profile.ObserveAllocator(session.World);
                Session = session;
                foreach (var deployment in Deployments)
                    if (!deployment.CommitShared(this, session)) { string reason = deployment.LastResult; Rollback(); return Reject(reason); }
                Status = QusapDeploymentStatus.Prepared; LastResult = "All deployments committed; shared raid prepared"; session.World.Notify(); return true;
            }
            catch (Exception exception) { Rollback(); return Reject(exception.Message); }
            finally { busy = false; }
        }
        public bool StartRunning()
        {
            if (busy || disposed || Status != QusapDeploymentStatus.Prepared) return Reject("Not Prepared");
            busy = true;
            try
            {
                foreach (var deployment in Deployments)
                    if (!deployment.StartSharedRunning(this)) { string reason = deployment.LastResult; Rollback(); return Reject(reason); }
                foreach (var deployment in Deployments) deployment.Inventory.DeploymentLocked = false;
                if (required.Length >= 2) Session.StartLastSurvivorCompetition(Deployments);
                rollback.Clear(); Status = QusapDeploymentStatus.Running;
                if (Session.Competition != null) Session.Competition.SessionFinished += _ => FinishSession();
                LastResult = "All required participants running; roster closed"; return true;
            }
            catch (Exception exception) { Rollback(); return Reject(exception.Message); }
            finally { busy = false; }
        }
        private void Rollback()
        {
            foreach (var deployment in Deployments) deployment.RollbackShared(this);
            Session?.AbortPreparedDeployment();
            for (int i = rollback.Count - 1; i >= 0; i--) rollback[i]();
            rollback.Clear(); Session = null; Status = QusapDeploymentStatus.None;
        }
        public bool CancelDeployment()
        {
            if (busy || disposed) return false;
            if (Status == QusapDeploymentStatus.Running) return Reject("Cannot cancel Running");
            busy = true;
            try { if (Status == QusapDeploymentStatus.Prepared) Rollback(); return true; }
            finally { busy = false; }
        }
        public void FinishSession()
        {
            if (busy || disposed || Status != QusapDeploymentStatus.Running) return;
            if (!Session.IsFinished) Session.FinishSession();
            if (!Session.IsFinished || Deployments.Any(d => d.Status != QusapDeploymentStatus.Resolved)) return;
            Status = QusapDeploymentStatus.Resolved;
        }
        public bool TryCommitSettlement(string settlementId, string profileId, IReadOnlyList<QusapLootInstance> items)
            => profiles.TryGetValue(profileId, out var profile) && profile.TryCommitSettlement(settlementId, profileId, items);
        public bool TryCommitVictory(string settlementId, string profileId, IReadOnlyList<QusapLootInstance> items)
            => profiles.TryGetValue(profileId, out var profile) && profile.TryCommitVictory(settlementId, profileId, items);
        public IReadOnlyList<QusapLootSnapshot> ReadStashSnapshot(string profileId)
            => profiles.TryGetValue(profileId, out var profile) ? profile.ReadStashSnapshot(profileId) : Array.Empty<QusapLootSnapshot>();
        public void Dispose()
        {
            if (disposed) return;
            disposed = true; // Fence callbacks during equipment release/rollback.
            if (Status == QusapDeploymentStatus.Prepared) Rollback();
            foreach (var deployment in Deployments) deployment.Dispose();
            Session?.Dispose();
        }
    }
}
