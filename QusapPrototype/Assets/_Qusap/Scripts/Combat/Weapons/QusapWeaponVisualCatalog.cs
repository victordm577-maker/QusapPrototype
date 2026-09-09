using System;
using System.Collections.Generic;
using UnityEngine;

namespace Qusap
{
    [Serializable]
    public sealed class QusapWeaponVisualEntry
    {
        [SerializeField] private string definitionId;
        [SerializeField] private string displayName;
        [SerializeField] private GameObject visualPrefab;

        public QusapWeaponVisualEntry(
            string definitionId,
            string displayName,
            GameObject visualPrefab)
        {
            QusapWeaponDefinition definition = new(definitionId, displayName);
            this.definitionId = definition.Id;
            this.displayName = definition.DisplayName;
            this.visualPrefab = visualPrefab != null
                ? visualPrefab
                : throw new ArgumentNullException(nameof(visualPrefab));
        }

        public string DefinitionId => definitionId;
        public string DisplayName => displayName;
        public GameObject VisualPrefab => visualPrefab;

        public QusapWeaponDefinition CreateDefinition()
        {
            return new QusapWeaponDefinition(definitionId, displayName);
        }

        internal void Validate()
        {
            _ = new QusapWeaponDefinition(definitionId, displayName);
            if (visualPrefab == null)
            {
                throw new InvalidOperationException(
                    $"Weapon definition '{definitionId}' requires a visual prefab.");
            }
        }
    }

    [CreateAssetMenu(
        fileName = "QusapWeaponVisualCatalog",
        menuName = "Qusap/Combat/Weapon Visual Catalog")]
    public sealed class QusapWeaponVisualCatalog : ScriptableObject
    {
        public const string BlueDefinitionId = "qusap_sword_blue";
        public const string PurpleDefinitionId = "qusap_sword_purple";
        public const string WhiteDefinitionId = "qusap_sword_white";

        [SerializeField] private QusapWeaponVisualEntry[] entries =
            Array.Empty<QusapWeaponVisualEntry>();

        private Dictionary<string, QusapWeaponVisualEntry> entriesById;
        private Dictionary<string, QusapWeaponDefinition> definitionsById;

        public IReadOnlyList<QusapWeaponVisualEntry> Entries => entries;
        public int Count => entries?.Length ?? 0;

        public void Configure(params QusapWeaponVisualEntry[] configuredEntries)
        {
            entries = configuredEntries != null
                ? (QusapWeaponVisualEntry[])configuredEntries.Clone()
                : throw new ArgumentNullException(nameof(configuredEntries));
            RebuildLookup();
        }

        public bool TryGetEntry(string definitionId, out QusapWeaponVisualEntry entry)
        {
            EnsureLookup();
            entry = null;
            return !string.IsNullOrWhiteSpace(definitionId)
                && entriesById.TryGetValue(definitionId.Trim(), out entry);
        }

        public bool TryGetDefinition(
            string definitionId,
            out QusapWeaponDefinition definition)
        {
            EnsureLookup();
            definition = null;
            return !string.IsNullOrWhiteSpace(definitionId)
                && definitionsById.TryGetValue(definitionId.Trim(), out definition);
        }

        private void OnEnable()
        {
            entriesById = null;
            definitionsById = null;
        }

        private void OnValidate()
        {
            entriesById = null;
            definitionsById = null;
        }

        private void EnsureLookup()
        {
            if (entriesById == null || definitionsById == null)
            {
                RebuildLookup();
            }
        }

        private void RebuildLookup()
        {
            entriesById = new Dictionary<string, QusapWeaponVisualEntry>(StringComparer.Ordinal);
            definitionsById = new Dictionary<string, QusapWeaponDefinition>(StringComparer.Ordinal);
            if (entries == null)
            {
                throw new InvalidOperationException("The weapon visual catalog entries cannot be null.");
            }

            for (int i = 0; i < entries.Length; i++)
            {
                QusapWeaponVisualEntry entry = entries[i]
                    ?? throw new InvalidOperationException($"Weapon visual entry {i} is null.");
                entry.Validate();
                if (!entriesById.TryAdd(entry.DefinitionId, entry))
                {
                    throw new InvalidOperationException(
                        $"Duplicate weapon definition ID '{entry.DefinitionId}'.");
                }

                definitionsById.Add(entry.DefinitionId, entry.CreateDefinition());
            }
        }
    }
}
