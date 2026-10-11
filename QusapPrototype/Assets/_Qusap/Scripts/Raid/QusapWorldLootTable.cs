using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using UnityEngine;

namespace Qusap
{
    [CreateAssetMenu(menuName = "Qusap/Raid/World Loot Container Table")]
    public sealed class QusapWorldLootTable : ScriptableObject
    {
        [SerializeField] private QusapRaidLootTable lootTable;
        [SerializeField] private int minimumRolls = 1;
        [SerializeField] private int maximumRolls = 3;
        public QusapRaidLootTable LootTable => lootTable;
        public int MinimumRolls => minimumRolls;
        public int MaximumRolls => maximumRolls;
        public void Configure(QusapRaidLootTable table, int min, int max)
        { lootTable = table; minimumRolls = min; maximumRolls = max; }
        public IReadOnlyList<string> Validate(QusapRaidLootCatalog catalog)
        {
            var errors = new List<string>();
            if (lootTable == null) errors.Add("Missing weighted table"); else errors.AddRange(lootTable.Validate(catalog));
            if (minimumRolls < 1 || maximumRolls < minimumRolls || maximumRolls > 10000) errors.Add("Invalid nonempty roll range");
            if (lootTable != null && catalog != null)
                foreach (var row in lootTable.Entries)
                    if (row != null && catalog.TryResolve(row.DefinitionId, out var d) && !d.CanEnterBackpack) errors.Add("Nontransferable definition: " + d.DefinitionId);
            return errors.AsReadOnly();
        }
        public QusapLootRoll[] Resolve(QusapRaidLootCatalog catalog, string raidId, string spawnPointId, ulong seed)
            => Freeze(catalog).Resolve(raidId, spawnPointId, seed);
        internal QusapWorldLootRecipe Freeze(QusapRaidLootCatalog catalog)
        {
            var errors = Validate(catalog);
            if (errors.Count != 0) throw new InvalidOperationException(string.Join("; ", errors));
            return new QusapWorldLootRecipe(catalog, lootTable, minimumRolls, maximumRolls);
        }
        public static string InstanceId(string raidId, string spawnPointId, int index)
        {
            if (string.IsNullOrWhiteSpace(raidId) || !QusapRaidInstanceIdentity.ValidSpawnPoint(spawnPointId) || index < 0) throw new ArgumentException("Invalid deterministic identity context");
            // Escape both components independently; separators cannot introduce identity ambiguity.
            return Uri.EscapeDataString(raidId) + "/world/" + Uri.EscapeDataString(spawnPointId) + "/roll/" + index.ToString(System.Globalization.CultureInfo.InvariantCulture);
        }
        internal static ulong ContextSeed(string raid, string spawn, ulong seed, string table, string domain)
        {
            if (string.IsNullOrWhiteSpace(raid) || string.IsNullOrWhiteSpace(spawn)) throw new ArgumentException("Missing deterministic context");
            using var bytes = new MemoryStream();
            using (var writer = new BinaryWriter(bytes, Encoding.UTF8, true)) { writer.Write(raid); writer.Write(spawn); writer.Write(seed); writer.Write(table); writer.Write(domain); }
            using var sha = SHA256.Create(); var digest = sha.ComputeHash(bytes.ToArray());
            ulong value = 0; for (int i = 0; i < 8; i++) value |= (ulong)digest[i] << (8 * i); return value;
        }
    }
    // Freeze configurable data at spawn registration; no asset changes can alter this raid's recipe.
    internal sealed class QusapWorldLootRecipe
    {
        private readonly QusapLootDefinition[] definitions;
        private readonly QusapLootTableEntry[] rows;
        private readonly int minimum, maximum;
        public string TableId { get; }
        internal QusapWorldLootRecipe(QusapRaidLootCatalog catalog, QusapRaidLootTable table, int min, int max)
        {
            TableId = table.LootTableId; minimum = min; maximum = max;
            rows = table.Entries.Select(e => new QusapLootTableEntry(e.DefinitionId, e.Weight, e.MinimumQuantity, e.MaximumQuantity, e.MinimumRarity)).ToArray();
            definitions = rows.Select(e => { catalog.TryResolve(e.DefinitionId, out var d); return d; }).ToArray();
        }
        internal QusapLootRoll[] Resolve(string raid, string spawn, ulong seed)
        {
            var countRandom = new QusapLootRandom(QusapWorldLootTable.ContextSeed(raid, spawn, seed, TableId, "roll-count-v1"));
            int count = minimum + (int)countRandom.Below((ulong)(maximum - minimum + 1));
            var random = new QusapLootRandom(QusapWorldLootTable.ContextSeed(raid, spawn, seed, TableId, "weighted-content-v1"));
            ulong total = rows.Aggregate(0UL, (sum, row) => checked(sum + (ulong)row.Weight));
            var result = new QusapLootRoll[count];
            for (int i = 0; i < count; i++)
            {
                ulong draw = random.Below(total); int index = 0;
                while (draw >= (ulong)rows[index].Weight) { draw -= (ulong)rows[index].Weight; index++; }
                int quantity = rows[index].MinimumQuantity + (int)random.Below((ulong)(rows[index].MaximumQuantity - rows[index].MinimumQuantity + 1));
                result[i] = new QusapLootRoll(definitions[index], quantity);
            }
            return result;
        }
    }
}
