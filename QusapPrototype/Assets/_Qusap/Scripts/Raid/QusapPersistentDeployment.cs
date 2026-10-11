using System;
using System.Globalization;
using System.Linq;
using UnityEngine;

namespace Qusap
{
    public sealed partial class QusapPersistentStashRepository
    {
        private string volatileDeploymentId;
        public QusapActiveDeploymentManifest ReadActiveDeploymentSnapshot() => committed.ActiveDeploymentManifest == null ? null
            : JsonUtility.FromJson<QusapActiveDeploymentManifest>(JsonUtility.ToJson(committed.ActiveDeploymentManifest));
        public bool HasActiveDeployment => committed.ActiveDeploymentManifest != null || volatileDeploymentId != null;
        public string RecoveryReason => committed.RecoveryReason;
        internal QusapLootInstance[] ReadOwnedInstances() => authority.ReadOwned(ProfileId);

        private QusapProfileContent Candidate()
        {
            var candidate = JsonUtility.FromJson<QusapProfileContent>(JsonUtility.ToJson(committed));
            if (committed.ActiveDeploymentManifest == null) candidate.ActiveDeploymentManifest = null;
            candidate.SchemaVersion = 2; candidate.Revision = checked(Revision + 1);
            candidate.SavedAtUtc = DateTime.UtcNow.ToString("O", CultureInfo.InvariantCulture);
            return candidate;
        }
        private bool Publish(QusapProfileContent candidate)
        {
            var document = QusapProfileCodec.Seal(candidate);
            if (!Storage.TryWrite(document)) { LastResult = Storage.LastResult; return false; }
            committed = candidate; Checksum = document.Checksum; LastResult = Storage.LastResult; return true;
        }
        internal bool TryCommitDeployment(QusapActiveDeploymentManifest manifest, string[] ids, out QusapLootInstance[] items)
        {
            lock (gate)
            {
                items = Array.Empty<QusapLootInstance>();
                if (HasActiveDeployment) return false;
                // A loaner-only raid changes no persistent ownership; no manifest or revision is necessary.
                if (ids.Length == 0) { volatileDeploymentId = manifest.DeploymentId; return true; }
                volatileDeploymentId = manifest.DeploymentId; // Also blocks synchronous save-callback reentry.
                bool success = authority.TryWithdraw(ProfileId, ids, () =>
                {
                    var candidate = Candidate(); candidate.ActiveDeploymentManifest = manifest;
                    candidate.Stash = candidate.Stash.Where(i => !ids.Contains(i.InstanceId)).ToArray();
                    candidate.Settlements = candidate.Settlements.Select(r => new QusapProfileReceipt { SettlementId = r.SettlementId,
                        Fingerprint = ProfileId + "\n" + string.Join("\n", r.Fingerprint.Split('\n').Skip(1).Where(id => !ids.Contains(id))) })
                        .Where(r => r.Fingerprint != ProfileId + "\n").ToArray();
                    return Publish(candidate);
                }, out items);
                if (!success) volatileDeploymentId = null;
                return success;
            }
        }
        internal bool TryMarkDeploymentRunning(string deploymentId)
        {
            lock (gate)
            {
                if (volatileDeploymentId == deploymentId && committed.ActiveDeploymentManifest == null) return true;
                if (committed.ActiveDeploymentManifest?.DeploymentId != deploymentId) return false;
                if (committed.ActiveDeploymentManifest.State == QusapDeploymentStatus.Running) return true;
                var candidate = Candidate(); candidate.ActiveDeploymentManifest.State = QusapDeploymentStatus.Running;
                return Publish(candidate);
            }
        }
        internal void ReleaseVolatileDeployment(string deploymentId)
        { lock (gate) if (volatileDeploymentId == deploymentId) volatileDeploymentId = null; }

        public bool TryRecoverInterruptedDeployment()
        {
            lock (gate)
            {
                var manifest = committed.ActiveDeploymentManifest;
                if (manifest == null) return true;
                var originals = codec.RestoreItems(manifest.Items, ProfileId);
                foreach (var item in originals) item.Reserved = true;
                string key = manifest.RaidId + "/recovery/" + manifest.DeploymentId;
                bool recovered = authority.TryCommitSettlement(key, ProfileId, originals, () =>
                {
                    var candidate = Candidate(); candidate.ActiveDeploymentManifest = null;
                    candidate.RecoveryReason = manifest.State == QusapDeploymentStatus.Running ? "InterruptedLocalRaid" : "PreparedNotStarted";
                    candidate.Stash = candidate.Stash.Concat(manifest.Items).ToArray();
                    candidate.Settlements = candidate.Settlements.Append(new QusapProfileReceipt { SettlementId = key,
                        Fingerprint = ProfileId + "\n" + string.Join("\n", manifest.Items.Select(i => i.InstanceId).OrderBy(id => id, StringComparer.Ordinal)) }).ToArray();
                    return Publish(candidate);
                });
                foreach (var item in originals) item.Reserved = false;
                return recovered;
            }
        }
    }
}
