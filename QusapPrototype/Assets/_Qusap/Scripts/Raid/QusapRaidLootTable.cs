using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace Qusap
{
    [Serializable] public sealed class QusapLootTableEntry
    {
        [SerializeField] private string definitionId;
        [SerializeField] private int weight;
        [SerializeField] private int minimumQuantity;
        [SerializeField] private int maximumQuantity;
        [SerializeField] private bool hasMinimumRarity;
        [SerializeField] private QusapLootRarity minimumRarity;
        public QusapLootTableEntry(string id, int chance, int min = 1, int max = 1, QusapLootRarity? tier = null)
        { definitionId = id; weight = chance; minimumQuantity = min; maximumQuantity = max; hasMinimumRarity = tier.HasValue; minimumRarity = tier.GetValueOrDefault(); }
        public string DefinitionId => definitionId;
        public int Weight => weight;
        public int MinimumQuantity => minimumQuantity;
        public int MaximumQuantity => maximumQuantity;
        public QusapLootRarity? MinimumRarity => hasMinimumRarity ? minimumRarity : null;
    }
    public readonly struct QusapLootRoll
    {
        public QusapLootRoll(QusapLootDefinition definition, int quantity) { Definition = definition; Quantity = quantity; }
        public QusapLootDefinition Definition { get; }
        public string DefinitionId => Definition.DefinitionId;
        public int Quantity { get; }
        public override string ToString() => DefinitionId + " x" + Quantity;
    }
    [CreateAssetMenu(menuName = "Qusap/Raid/Loot Table")]
    public sealed class QusapRaidLootTable : ScriptableObject
    {
        [SerializeField] private string lootTableId;
        [SerializeField] private QusapLootTableEntry[] entries = Array.Empty<QusapLootTableEntry>();
        public string LootTableId => lootTableId;
        public IReadOnlyList<QusapLootTableEntry> Entries => Array.AsReadOnly(entries ?? Array.Empty<QusapLootTableEntry>());
        public void Configure(string id, params QusapLootTableEntry[] rows)
        {
            if (!string.IsNullOrEmpty(lootTableId) && lootTableId != id) throw new InvalidOperationException("LootTableId cannot be renamed.");
            lootTableId = id; entries = rows == null ? null : (QusapLootTableEntry[])rows.Clone();
        }
        public IReadOnlyList<string> Validate(QusapRaidLootCatalog catalog)
        {
            var errors = new List<string>();
            if (catalog == null) { errors.Add("Missing catalog"); return errors.AsReadOnly(); }
            errors.AddRange(catalog.Validate());
            if (string.IsNullOrWhiteSpace(lootTableId)) errors.Add("Missing LootTableId");
            if (entries == null || entries.Length == 0) errors.Add("Empty loot table");
            var ids = new HashSet<string>(StringComparer.Ordinal);
            foreach (var entry in entries ?? Array.Empty<QusapLootTableEntry>())
            {
                if (entry == null) { errors.Add("Missing entry"); continue; }
                if (entry.Weight <= 0) errors.Add("Weight must be positive: " + entry.DefinitionId);
                if (!catalog.TryResolve(entry.DefinitionId, out var definition)) { errors.Add("Unknown DefinitionId: " + entry.DefinitionId); continue; }
                if (!ids.Add(definition.DefinitionId)) errors.Add("Duplicate table definition/alias: " + entry.DefinitionId);
                if (entry.MinimumQuantity < 1 || entry.MinimumQuantity > entry.MaximumQuantity || entry.MaximumQuantity > definition.MaxStack
                    || (!definition.IsStackable && (entry.MinimumQuantity != 1 || entry.MaximumQuantity != 1))) errors.Add("Invalid min/max quantity: " + entry.DefinitionId);
                if (entry.MinimumRarity.HasValue && (!Enum.IsDefined(typeof(QusapLootRarity), entry.MinimumRarity.Value)
                    || (int)definition.Rarity < (int)entry.MinimumRarity.Value)) errors.Add("Invalid minimum rarity: " + entry.DefinitionId);
            }
            return errors.AsReadOnly();
        }
        public QusapLootRoll[] Roll(QusapRaidLootCatalog catalog, ulong seed, int count = 1)
        {
            var errors = Validate(catalog);
            if (errors.Count != 0) throw new InvalidOperationException(string.Join("; ", errors));
            if (count < 1 || count > 10000) throw new ArgumentOutOfRangeException(nameof(count));
            ulong total = entries.Aggregate(0UL, (sum, row) => checked(sum + (ulong)row.Weight));
            var random = new QusapLootRandom(seed);
            var results = new QusapLootRoll[count];
            for (int i = 0; i < count; i++)
            {
                ulong draw = random.Below(total);
                QusapLootTableEntry selected = null;
                foreach (var entry in entries) { if (draw < (ulong)entry.Weight) { selected = entry; break; } draw -= (ulong)entry.Weight; }
                catalog.TryResolve(selected.DefinitionId, out var definition);
                int quantity = selected.MinimumQuantity + (int)random.Below((ulong)(selected.MaximumQuantity - selected.MinimumQuantity) + 1);
                results[i] = new QusapLootRoll(definition, quantity);
            }
            return results;
        }
    }
    // SplitMix64 is scoped to each roll. Rejection removes modulo bias; unchecked arithmetic is specified.
    internal sealed class QusapLootRandom
    {
        private ulong state;
        public QusapLootRandom(ulong seed) { state = seed; }
        private ulong Next()
        {
            unchecked
            {
                ulong z = (state += 0x9E3779B97F4A7C15UL);
                z = (z ^ (z >> 30)) * 0xBF58476D1CE4E5B9UL;
                z = (z ^ (z >> 27)) * 0x94D049BB133111EBUL;
                return z ^ (z >> 31);
            }
        }
        public ulong Below(ulong bound)
        {
            if (bound == 0) throw new ArgumentOutOfRangeException(nameof(bound));
            ulong threshold = unchecked(0UL - bound) % bound;
            ulong sample; do { sample = Next(); } while (sample < threshold);
            return sample % bound;
        }
    }
}
