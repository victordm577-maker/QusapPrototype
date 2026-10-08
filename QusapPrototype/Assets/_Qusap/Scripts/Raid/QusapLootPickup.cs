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
        private QusapLootWorld world;
        private QusapLootInstance item;
        public string InstanceId => item?.LootInstanceId;
        public QusapLootDefinition Definition => definition;
        public void Configure(QusapRaidBootstrap bootstrap, QusapLootDefinition loot) { raid = bootstrap; definition = loot; }
        private void Start()
        {
            world = raid.Session.World; item = world.Create(definition, "Pickup: " + gameObject.name);
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
