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
                || items.Any(i => i == null || !i.CanPersist || i.Quantity < 1 || i.Quantity > i.Definition.MaxStack)
                || items.Select(i => i.LootInstanceId).Distinct().Count() != items.Count) return false;
            string fingerprint = profileId + "\n" + string.Join("\n", items.Select(i => i.LootInstanceId).OrderBy(id => id, StringComparer.Ordinal));
            lock (gate)
            {
                if (settlements.TryGetValue(settlementId, out var previous)) return previous == fingerprint;
                if (items.Any(i => !i.Reserved || i.Weapon?.IsEquipped == true)) return false;
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

        internal QusapLootInstance[] ReadOwned(string profile)
        { lock (gate) return stored.Values.Where(v => v.profile == profile).Select(v => v.item).ToArray(); }

        internal bool TryWithdraw(string profile, string[] ids, Func<bool> persist, out QusapLootInstance[] withdrawn)
        {
            withdrawn = Array.Empty<QusapLootInstance>();
            lock (gate)
            {
                if (ids.Distinct(StringComparer.Ordinal).Count() != ids.Length || ids.Any(id =>
                    !stored.TryGetValue(id, out var value) || value.profile != profile || value.item.Reserved
                    || value.item.Location != QusapLootLocation.Stash || !value.item.CanPersist)) return false;
                if (!persist()) return false;
                withdrawn = ids.Select(id => stored[id].item).ToArray();
                foreach (string id in ids) stored.Remove(id);
                foreach (string key in settlements.Keys.ToArray())
                {
                    var parts = settlements[key].Split('\n');
                    if (parts[0] != profile) continue;
                    var remaining = parts.Skip(1).Where(id => !ids.Contains(id)).ToArray();
                    if (remaining.Length == 0) settlements.Remove(key);
                    else settlements[key] = profile + "\n" + string.Join("\n", remaining);
                }
                return true;
            }
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
