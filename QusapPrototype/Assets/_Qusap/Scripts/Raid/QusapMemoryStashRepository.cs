using System;
using System.Collections.Generic;
using System.Linq;

namespace Qusap
{
    // Sole stash authority. Optional persistence prepares a complete transaction before publication.
    public sealed class QusapMemoryStashRepository : IQusapStashRepository
    {
        private readonly object gate = new();
        private readonly Dictionary<string, string> settlements = new(StringComparer.Ordinal);
        private readonly Dictionary<string, (string profile, QusapLootInstance item)> stored = new(StringComparer.Ordinal);
        public bool TryCommitSettlement(string settlementId, string profileId, IReadOnlyList<QusapLootInstance> items)
            => TryCommitSettlement(settlementId, profileId, items, null);

        internal bool TryCommitSettlement(string settlementId, string profileId,
            IReadOnlyList<QusapLootInstance> items, Func<bool> prepareCompleteTransaction)
        {
            if (string.IsNullOrWhiteSpace(settlementId) || string.IsNullOrWhiteSpace(profileId) || items == null
                || items.Any(i => i == null || !i.Definition.CanPersistInStash || i.Quantity < 1 || i.Quantity > i.Definition.MaxStack)
                || items.Select(i => i.LootInstanceId).Distinct().Count() != items.Count) return false;
            string fingerprint = profileId + "\n" + string.Join("\n", items.Select(i => i.LootInstanceId).OrderBy(id => id, StringComparer.Ordinal));
            lock (gate)
            {
                if (settlements.TryGetValue(settlementId, out var previous)) return previous == fingerprint;
                if (items.Any(i => stored.ContainsKey(i.LootInstanceId))) return false;
                if (prepareCompleteTransaction != null && !prepareCompleteTransaction()) return false;
                foreach (var item in items) stored.Add(item.LootInstanceId, (profileId, item));
                settlements.Add(settlementId, fingerprint);
                return true;
            }
        }
        public IReadOnlyList<QusapLootSnapshot> ReadStashSnapshot(string profileId)
        { lock (gate) return Array.AsReadOnly(stored.Values.Where(v => v.profile == profileId).Select(v => new QusapLootSnapshot(v.item)).OrderBy(s => s.LootInstanceId, StringComparer.Ordinal).ToArray()); }

        internal QusapProfileReceipt[] ReadReceipts(string profileId)
        {
            lock (gate) return settlements.Where(p => p.Value.StartsWith(profileId + "\n", StringComparison.Ordinal))
                .OrderBy(p => p.Key, StringComparer.Ordinal)
                .Select(p => new QusapProfileReceipt { SettlementId = p.Key, Fingerprint = p.Value }).ToArray();
        }

        internal void Restore(string profileId, QusapLootInstance[] items, QusapProfileReceipt[] receipts)
        {
            lock (gate)
            {
                if (stored.Count != 0 || settlements.Count != 0) throw new InvalidOperationException("Restore requires an empty authority.");
                foreach (var item in items) stored.Add(item.LootInstanceId, (profileId, item));
                foreach (var receipt in receipts) settlements.Add(receipt.SettlementId, receipt.Fingerprint);
            }
        }
    }
}
