using UnityEngine;

namespace Qusap
{
    [DisallowMultipleComponent]
    public sealed class QusapLootPickup : MonoBehaviour
    {
        [SerializeField] private QusapRaidBootstrap raid;
        [SerializeField] private QusapLootDefinition definition;
        [SerializeField] private bool automaticPickup = true;
        [SerializeField, Min(0.1f)] private float pickupRadius = QusapWeaponPickupResolver.DefaultPickupRadius;
        [SerializeField, Min(1)] private int quantity = 1;
        [SerializeField] private string provenance;
        private QusapLootWorld world;
        private QusapLootInstance item;
        public string InstanceId => item?.LootInstanceId;
        public QusapLootDefinition Definition => definition;
        public int Quantity => item?.Quantity ?? quantity;
        public void Configure(QusapRaidBootstrap bootstrap, QusapLootDefinition loot, int amount = 1, string origin = null)
        { raid = bootstrap; definition = loot; quantity = amount; provenance = origin; }
        private void Start()
        {
            world = raid.Session.World;
            var weapon = definition.Category == QusapLootCategory.Weapon
                ? FindAnyObjectByType<QusapWeaponMatchBootstrap>().CreateRaidLootWeapon(definition.WeaponDefinitionId) : null;
            item = world.Create(definition, string.IsNullOrEmpty(provenance) ? "Pickup: " + gameObject.name : provenance, weapon, quantity);
            world.Changed += Changed;
        }
        public bool CanReach(QusapRaidInventory inventory) => item != null && item.Location == QusapLootLocation.World
            && isActiveAndEnabled && inventory.Session.World == world
            && (inventory.transform.position - transform.position).sqrMagnitude <= pickupRadius * pickupRadius;
        private void OnTriggerStay(Collider other)
        { if (automaticPickup) { var inventory = other.GetComponentInParent<QusapRaidInventory>(); if (inventory != null) inventory.TryPickup(this); } }
        private void Changed()
        {
            if (item.Location != QusapLootLocation.World)
            { world.Changed -= Changed; gameObject.SetActive(false); Destroy(gameObject); }
        }
        private void OnDestroy() { if (world != null) world.Changed -= Changed; }
    }
}
