using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEngine;

namespace Qusap
{
    // Isolated scene orchestration. Item, equipment and stash authorities remain the approved ones.
    [DefaultExecutionOrder(400)]
    public sealed class QusapRaidLootCatalogPlayground : MonoBehaviour
    {
        [SerializeField] private QusapRaidLootCatalog catalog;
        [SerializeField] private QusapRaidLootTable table;
        [SerializeField] private QusapRaidBootstrap raid;
        [SerializeField] private ulong seed = 4107;
        private readonly List<Material> transientMaterials = new();
#if UNITY_EDITOR
        private string ownedIsolatedDirectory;
#endif
        public QusapRaidLootCatalog Catalog => catalog;
        public QusapRaidLootTable Table => table;
        public QusapRaidBootstrap Raid => raid;
        public ulong Seed => seed;
        public QusapLootRoll[] LastRoll { get; private set; } = Array.Empty<QusapLootRoll>();
        public IReadOnlyList<string> ValidationErrors { get; private set; } = Array.Empty<string>();
        public bool SameSeedMatches { get; private set; }
        public bool DifferentSeedDiffers { get; private set; }
        public QusapPersistentStashRepository Reloaded { get; private set; }
        public QusapLootDefinition Selected { get; private set; }
        public string Action { get; set; } = "ENTER iniciar | G repetir seed | N otra seed | TAB seleccionar | H consumir | R bolsillo | B equipar";
        public void Configure(QusapRaidLootCatalog definitions, QusapRaidLootTable loot, QusapRaidBootstrap bootstrap)
        { catalog = definitions; table = loot; raid = bootstrap; }
        private void Awake()
        {
#if UNITY_EDITOR
            // The isolated scene never defaults to a real profile, including an ordinary editor Play click.
            if (string.IsNullOrWhiteSpace(QusapLocalProfilePersistence.IsolatedStorageDirectory)
                && !Environment.GetCommandLineArgs().Contains("-qusapProfileDirectory"))
            {
                ownedIsolatedDirectory = Path.Combine(Path.GetTempPath(), "QusapRaidLootPlayground", Guid.NewGuid().ToString("N"));
                QusapLocalProfilePersistence.IsolatedStorageDirectory = ownedIsolatedDirectory;
            }
#endif
        }
        private void Start()
        {
            ValidationErrors = table.Validate(catalog);
            if (ValidationErrors.Count != 0) { Debug.LogError("RAID_LOOT_CATALOG_INVALID: " + string.Join("; ", ValidationErrors)); enabled = false; return; }
            if (!raid.Definitions.SequenceEqual(catalog.Definitions)) throw new InvalidOperationException("Raid must use the validated catalog definitions.");
            Selected = catalog.Definitions[0]; Preview(seed);
        }
        public void Select(string id)
        { if (!catalog.TryResolve(id, out var definition)) throw new ArgumentException("Unknown DefinitionId"); Selected = definition; }
        public void Preview(ulong injectedSeed)
        {
            seed = injectedSeed; LastRoll = table.Roll(catalog, seed, 8);
            SameSeedMatches = LastRoll.Select(r => r.ToString()).SequenceEqual(table.Roll(catalog, seed, 8).Select(r => r.ToString()));
            DifferentSeedDiffers = !LastRoll.Select(r => r.ToString()).SequenceEqual(table.Roll(catalog, unchecked(seed + 1), 8).Select(r => r.ToString()));
        }
        public QusapLootPickup[] GenerateWorldLoot(ulong injectedSeed, int count = 8)
        {
            Preview(injectedSeed);
            var rolls = table.Roll(catalog, injectedSeed, count);
            var result = new QusapLootPickup[rolls.Length];
            for (int i = 0; i < rolls.Length; i++)
            {
                var go = GameObject.CreatePrimitive(PrimitiveType.Sphere);
                go.name = "Seed_" + injectedSeed + "_" + i + "_" + rolls[i].DefinitionId;
                go.transform.position = new Vector3(3.5f + (i % 6) * 0.7f, 0.5f, 1.4f + (i / 6) * 0.7f);
                go.transform.localScale = Vector3.one * 0.35f;
                go.GetComponent<Collider>().isTrigger = true;
                var material = new Material(go.GetComponent<Renderer>().sharedMaterial); material.color = ColorFor(rolls[i].Definition.Rarity);
                transientMaterials.Add(material); go.GetComponent<Renderer>().sharedMaterial = material;
                result[i] = go.AddComponent<QusapLootPickup>();
                result[i].Configure(raid, rolls[i].Definition, rolls[i].Quantity, table.LootTableId + "/seed/" + injectedSeed);
            }
            return result;
        }
        public void ReloadEvidenceProfile()
        {
            var profile = raid.GetComponent<QusapLocalProfilePersistence>().Repository;
            Reloaded = new QusapPersistentStashRepository(profile.Storage.DirectoryPath, catalog.Definitions);
            Action = "Recarga logica desde la misma ruta aislada: " + Reloaded.LastResult;
        }
        public static Color ColorFor(QusapLootRarity rarity) => rarity switch
        {
            QusapLootRarity.Common => new Color(0.65f, 0.65f, 0.65f),
            QusapLootRarity.Uncommon => new Color(0.1f, 0.85f, 0.2f),
            QusapLootRarity.Rare => new Color(0.1f, 0.45f, 1f),
            QusapLootRarity.Epic => new Color(0.7f, 0.2f, 0.9f),
            QusapLootRarity.Legendary => new Color(1f, 0.75f, 0.1f),
            _ => throw new ArgumentOutOfRangeException(nameof(rarity))
        };
        private void OnDestroy()
        {
            foreach (var material in transientMaterials) Destroy(material);
#if UNITY_EDITOR
            if (ownedIsolatedDirectory != null && QusapLocalProfilePersistence.IsolatedStorageDirectory == ownedIsolatedDirectory)
                QusapLocalProfilePersistence.IsolatedStorageDirectory = null;
#endif
        }
    }
}
