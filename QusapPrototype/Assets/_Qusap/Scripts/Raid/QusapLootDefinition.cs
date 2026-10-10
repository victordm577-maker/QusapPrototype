using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace Qusap
{
    [CreateAssetMenu(menuName = "Qusap/Raid/Loot Definition")]
    public sealed class QusapLootDefinition : ScriptableObject
    {
        [SerializeField] private string definitionId;
        [SerializeField] private string displayName;
        [SerializeField] private QusapLootCategory category;
        [SerializeField] private bool securePocketEligible;
        [SerializeField] private QusapConsumableType consumableType;
        [SerializeField, Min(0f)] private float healAmount;
        [SerializeField] private QusapWeaponVisualCatalog weaponCatalog;
        [SerializeField] private string weaponDefinitionId;
        [SerializeField] private QusapLootRarity rarity;
        [SerializeField] private bool inventoryRulesConfigured;
        [SerializeField] private int maxStack = 1;
        [SerializeField] private bool canEnterBackpack = true;
        [SerializeField] private bool canDropOnDeath = true;
        [SerializeField] private bool canPersistInStash = true;
        [SerializeField] private bool consumeOnUse = true;
        [SerializeField] private int baseValue;
        [SerializeField] private string worldPickupPresentationId;
        [SerializeField] private string[] tags = Array.Empty<string>();

        public string DefinitionId => definitionId;
        public string DisplayName => displayName;
        public QusapLootCategory Category => category;
        public bool SecurePocketEligible => securePocketEligible;
        public QusapConsumableType ConsumableType => consumableType;
        public float HealAmount => healAmount;
        public QusapLootRarity Rarity => rarity;
        public int MaxStack => inventoryRulesConfigured ? maxStack : 1;
        public bool CanEnterBackpack => !inventoryRulesConfigured || canEnterBackpack;
        public bool CanDropOnDeath => !inventoryRulesConfigured || canDropOnDeath;
        public bool CanPersistInStash => !inventoryRulesConfigured || canPersistInStash;
        public bool ConsumeOnUse => !inventoryRulesConfigured || consumeOnUse;
        public int BaseValue => baseValue;
        public string WeaponDefinitionId => weaponDefinitionId;
        public string WorldPickupPresentationId => worldPickupPresentationId;
        public IReadOnlyList<string> Tags => Array.AsReadOnly(tags ?? Array.Empty<string>());
        public bool IsStackable => category == QusapLootCategory.Material || category == QusapLootCategory.Consumable;
        public bool SecurePocketAllowed => CanEnterSecurePocket;
        public QusapWeaponDefinition WeaponDefinition => weaponCatalog != null
            && weaponCatalog.TryGetDefinition(weaponDefinitionId, out var definition) ? definition : null;
        public bool CanEnterSecurePocket => securePocketEligible
            && category != QusapLootCategory.Weapon && category != QusapLootCategory.Armor
            && category != QusapLootCategory.Horn && category != QusapLootCategory.Objective;

        public void Configure(string id, string name, QusapLootCategory type, bool eligible,
            QusapConsumableType consumable = QusapConsumableType.None, float healing = 0f,
            QusapWeaponVisualCatalog catalog = null, string weaponId = null)
        {
            if (string.IsNullOrWhiteSpace(id) || string.IsNullOrWhiteSpace(name)
                || !float.IsFinite(healing) || healing < 0f)
                throw new System.ArgumentException("Invalid loot definition.");
            if (!string.IsNullOrEmpty(definitionId) && !string.Equals(definitionId, id.Trim(), StringComparison.Ordinal))
                throw new InvalidOperationException("A published DefinitionId cannot be renamed.");
            definitionId = id.Trim(); displayName = name.Trim(); category = type;
            securePocketEligible = eligible; consumableType = consumable; healAmount = healing;
            weaponCatalog = catalog; weaponDefinitionId = weaponId;
        }

        public void ConfigureMetadata(QusapLootRarity tier, int stack, int value, string presentation,
            bool backpack = true, bool dropOnDeath = true, bool persist = true, bool consume = true,
            params string[] itemTags)
        {
            rarity = tier; inventoryRulesConfigured = true; maxStack = stack; baseValue = value;
            worldPickupPresentationId = presentation; canEnterBackpack = backpack;
            canDropOnDeath = dropOnDeath; canPersistInStash = persist; consumeOnUse = consume;
            tags = itemTags == null ? null : itemTags.OrderBy(t => t, StringComparer.Ordinal).ToArray();
        }

        public IReadOnlyList<string> ValidateMetadata()
        {
            var errors = new List<string>();
            if (string.IsNullOrWhiteSpace(definitionId) || definitionId != definitionId.Trim()) errors.Add("Invalid DefinitionId");
            if (string.IsNullOrWhiteSpace(displayName)) errors.Add("Missing DisplayName");
            if (category != QusapLootCategory.Material && category != QusapLootCategory.Consumable
                && category != QusapLootCategory.Weapon && category != QusapLootCategory.Armor && category != QusapLootCategory.Relic)
                errors.Add("Unsupported Category");
            if (!Enum.IsDefined(typeof(QusapLootRarity), rarity)) errors.Add("Invalid Rarity");
            if (MaxStack < 1 || (!IsStackable && MaxStack != 1)) errors.Add("Invalid MaxStack for category");
            if (baseValue < 0) errors.Add("Invalid BaseValue");
            if (string.IsNullOrWhiteSpace(worldPickupPresentationId)) errors.Add("Missing WorldPickupPresentationId");
            if (tags == null || tags.Any(string.IsNullOrWhiteSpace) || tags.Distinct(StringComparer.Ordinal).Count() != tags.Length
                || !tags.SequenceEqual(tags.OrderBy(t => t, StringComparer.Ordinal))) errors.Add("Invalid deterministic Tags");
            if (category == QusapLootCategory.Weapon)
            {
                if (WeaponDefinition == null || string.IsNullOrWhiteSpace(weaponDefinitionId)) errors.Add("Unknown WeaponDefinitionId");
            }
            else if (!string.IsNullOrEmpty(weaponDefinitionId) || weaponCatalog != null) errors.Add("Native weapon data on non-weapon");
            if (category == QusapLootCategory.Consumable)
            {
                if (consumableType != QusapConsumableType.Healing || !float.IsFinite(healAmount) || healAmount <= 0) errors.Add("Invalid explicit HealAmount");
            }
            else if (consumableType != QusapConsumableType.None || healAmount != 0) errors.Add("Consumable data on non-consumable");
            if ((category == QusapLootCategory.Weapon || category == QusapLootCategory.Armor) && securePocketEligible)
                errors.Add("Weapon/Armor cannot enter secure pocket");
            return errors.AsReadOnly();
        }
    }
}
