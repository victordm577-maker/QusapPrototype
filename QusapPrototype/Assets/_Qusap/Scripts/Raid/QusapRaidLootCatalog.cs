using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace Qusap
{
    [Serializable] public sealed class QusapLootAlias
    {
        [SerializeField] private string alias;
        [SerializeField] private string definitionId;
        public QusapLootAlias(string name, string target) { alias = name; definitionId = target; }
        public string Alias => alias;
        public string DefinitionId => definitionId;
    }

    [CreateAssetMenu(menuName = "Qusap/Raid/Loot Catalog")]
    public sealed class QusapRaidLootCatalog : ScriptableObject
    {
        [SerializeField] private QusapLootDefinition[] definitions = Array.Empty<QusapLootDefinition>();
        [SerializeField] private QusapLootAlias[] aliases = Array.Empty<QusapLootAlias>();
        public IReadOnlyList<QusapLootDefinition> Definitions => Array.AsReadOnly(definitions ?? Array.Empty<QusapLootDefinition>());
        public IReadOnlyList<QusapLootAlias> Aliases => Array.AsReadOnly(aliases ?? Array.Empty<QusapLootAlias>());
        public void Configure(QusapLootDefinition[] items, params QusapLootAlias[] names)
        { definitions = items == null ? null : (QusapLootDefinition[])items.Clone(); aliases = names == null ? null : (QusapLootAlias[])names.Clone(); }

        public IReadOnlyList<string> Validate()
        {
            var errors = new List<string>();
            var primary = new Dictionary<string, QusapLootDefinition>(StringComparer.Ordinal);
            var native = new HashSet<string>(StringComparer.Ordinal);
            if (definitions == null || definitions.Length == 0) errors.Add("Missing definitions");
            foreach (var definition in definitions ?? Array.Empty<QusapLootDefinition>())
            {
                if (definition == null) { errors.Add("Missing definition asset"); continue; }
                if (string.IsNullOrWhiteSpace(definition.DefinitionId) || !primary.TryAdd(definition.DefinitionId, definition)) errors.Add("Duplicate/invalid DefinitionId: " + definition.DefinitionId);
                try
                {
                    errors.AddRange(definition.ValidateMetadata().Select(e => definition.DefinitionId + ": " + e));
                    if (definition.Category == QusapLootCategory.Weapon && definition.WeaponDefinition != null
                        && !native.Add(definition.WeaponDefinition.Id)) errors.Add("Two loot definitions for one native weapon: " + definition.WeaponDefinition.Id);
                }
                catch (Exception exception) { errors.Add(definition.DefinitionId + ": " + exception.Message); }
            }
            var names = new HashSet<string>(primary.Keys, StringComparer.Ordinal);
            if (aliases == null) errors.Add("Missing alias array");
            foreach (var alias in aliases ?? Array.Empty<QusapLootAlias>())
                if (alias == null || string.IsNullOrWhiteSpace(alias.Alias) || !names.Add(alias.Alias)
                    || string.IsNullOrWhiteSpace(alias.DefinitionId) || !primary.ContainsKey(alias.DefinitionId)) errors.Add("Invalid/duplicate alias");
            return errors.AsReadOnly();
        }

        public bool TryResolve(string id, out QusapLootDefinition definition)
        {
            definition = null;
            if (id == null) return false;
            definition = Definitions.FirstOrDefault(d => d != null && d.DefinitionId == id);
            if (definition != null) return true;
            var alias = Aliases.FirstOrDefault(a => a != null && a.Alias == id);
            definition = alias == null ? null : Definitions.FirstOrDefault(d => d != null && d.DefinitionId == alias.DefinitionId);
            return definition != null;
        }
    }
}
