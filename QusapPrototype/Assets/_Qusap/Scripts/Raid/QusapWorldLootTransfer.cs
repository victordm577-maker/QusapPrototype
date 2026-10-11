using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;

namespace Qusap
{
    public sealed partial class QusapLootWorld
    {
        private readonly HashSet<string> worldSpawnPoints = new(StringComparer.Ordinal);
        private readonly Dictionary<string, ulong> worldLootSplits = new(StringComparer.Ordinal);
        internal bool RegisterWorldSpawn(string id) => worldSpawnPoints.Add(id);
        internal bool CreateWorldContainerItems(string spawn, QusapLootRoll[] rolls,
            Func<QusapLootDefinition, QusapWeaponInstance> factory, CancellationToken cancellation)
        {
            var generated = new List<QusapLootInstance>();
            foreach (var roll in rolls)
            {
                string id = QusapWorldLootTable.InstanceId(RaidId, spawn, generated.Count);
                if (items.ContainsKey(id)) return false;
                var weapon = roll.Definition.Category == QusapLootCategory.Weapon ? factory?.Invoke(roll.Definition) : null;
                if ((roll.Definition.Category == QusapLootCategory.Weapon) != (weapon != null)
                    || (weapon != null && (!weapon.IsFree || weapon.IsRetired || weapon.Definition.Id != roll.Definition.WeaponDefinitionId
                        || items.Values.Concat(generated).Any(i => i.Weapon?.InstanceId == weapon.InstanceId)))) return false;
                generated.Add(new QusapLootInstance(id, RaidId, roll.Definition, "World loot", weapon, roll.Quantity)
                    { Location = QusapLootLocation.WorldContainer, HolderId = spawn, SlotIndex = generated.Count });
            }
            if (cancellation.IsCancellationRequested) return false;
            foreach (var item in generated) items.Add(item.LootInstanceId, item);
            return true;
        }

        // Exact requested quantity or no changes. The caller holds this same ledger's gate.
        internal QusapWorldLootReply TransferWorldContainer(QusapRaidInventoryState destination, string spawn, string id,
            int quantity, CancellationToken cancellation)
        {
            QusapWorldLootReply Reject(QusapWorldLootResult result) => new(result, id);
            var item = Get(id);
            if (item == null) return Reject(QusapWorldLootResult.MissingInstance);
            if (item.Location != QusapLootLocation.WorldContainer || item.HolderId != spawn) return Reject(QusapWorldLootResult.InvalidSource);
            if (item.Reserved) return Reject(QusapWorldLootResult.Reserved);
            if (!item.Definition.CanEnterBackpack || item.RaidLoaner || item.Weapon?.IsEquipped == true) return Reject(QusapWorldLootResult.Ineligible);
            if (quantity < 1 || quantity > item.Quantity || (!item.Definition.IsStackable && quantity != 1)) return Reject(QusapWorldLootResult.InvalidQuantity);
            var partials = item.Definition.IsStackable
                ? Find(QusapLootLocation.Backpack, destination.ParticipantId).Where(i => i.DefinitionId == item.DefinitionId
                    && i.RaidId == item.RaidId && i.Provenance == item.Provenance && i.Weapon == null && i.Quantity < i.Definition.MaxStack).ToArray()
                : Array.Empty<QusapLootInstance>();
            int remaining = quantity;
            var additions = partials.Select(i => { int add = Math.Min(remaining, i.Definition.MaxStack - i.Quantity); remaining -= add; return add; }).ToArray();
            int slot = remaining == 0 ? -1 : destination.FindEmptyBackpackSlot();
            if (remaining > 0 && slot < 0) return Reject(QusapWorldLootResult.Full);
            int sourceAfter = item.Quantity - quantity;
            string splitId = null; ulong nextSplit = 0;
            if (sourceAfter > 0 && remaining > 0)
            {
                worldLootSplits.TryGetValue(id, out var prior); nextSplit = checked(prior + 1);
                splitId = id + "/part/" + nextSplit.ToString(System.Globalization.CultureInfo.InvariantCulture);
                if (items.ContainsKey(splitId)) return Reject(QusapWorldLootResult.InvalidSource);
            }
            var targets = partials.Where((p, i) => additions[i] > 0).ToArray();
            var transaction = targets.Concat(new[] { item }).ToArray();
            if (cancellation.IsCancellationRequested) return Reject(QusapWorldLootResult.Cancelled);
            if (!Reserve(transaction)) return Reject(QusapWorldLootResult.Reserved);
            try
            {
                for (int i = 0; i < partials.Length; i++) partials[i].Quantity += additions[i];
                if (sourceAfter > 0)
                {
                    item.Quantity = sourceAfter;
                    if (splitId != null)
                    {
                        var split = new QusapLootInstance(splitId, RaidId, item.Definition, item.Provenance, null, remaining)
                            { Location = QusapLootLocation.Backpack, HolderId = destination.ParticipantId, SlotIndex = slot };
                        items.Add(splitId, split); worldLootSplits[id] = nextSplit;
                    }
                }
                else
                {
                    item.Quantity = remaining;
                    Move(item, remaining == 0 ? QusapLootLocation.Merged : QusapLootLocation.Backpack,
                        remaining == 0 ? null : destination.ParticipantId, slot);
                }
            }
            finally { Release(transaction); }
            string destinationId = remaining > 0 ? splitId ?? id : targets[0].LootInstanceId;
            return new QusapWorldLootReply(QusapWorldLootResult.Success, id, quantity, destinationId, targets.Select(i => i.LootInstanceId).ToArray());
        }
    }
}
