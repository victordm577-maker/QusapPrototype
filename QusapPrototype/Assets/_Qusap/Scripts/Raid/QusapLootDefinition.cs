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

        public string DefinitionId => definitionId;
        public string DisplayName => displayName;
        public QusapLootCategory Category => category;
        public bool SecurePocketEligible => securePocketEligible;
        public QusapConsumableType ConsumableType => consumableType;
        public float HealAmount => healAmount;
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
            definitionId = id.Trim(); displayName = name.Trim(); category = type;
            securePocketEligible = eligible; consumableType = consumable; healAmount = healing;
            weaponCatalog = catalog; weaponDefinitionId = weaponId;
        }
    }
}
