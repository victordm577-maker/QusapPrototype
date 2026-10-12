using System.Collections.Generic;
using UnityEngine;

namespace Qusap
{
    [DisallowMultipleComponent]
    public sealed class QusapDeathLootContainer : MonoBehaviour
    {
        [SerializeField, Min(0.1f)] private float lootRadius = 2f;
        private QusapLootWorld world;
        private bool keepDepletedVisible;
        public QusapDeathLootRecord Record { get; private set; }
        public string ContainerId => Record?.ContainerId;
        public void Initialize(QusapLootWorld ledger, QusapDeathLootRecord record)
        { world = ledger; Record = record; world.Changed += Changed; Changed(); }
        public void KeepDepletedVisible() { keepDepletedVisible = true; gameObject.SetActive(true); }
        public IReadOnlyList<QusapLootSnapshot> ReadSnapshot() => world.ReadContainer(ContainerId);
        public bool CanReach(QusapRaidInventory inventory) => Record != null && isActiveAndEnabled
            && inventory.Session.World == world && (inventory.transform.position - transform.position).sqrMagnitude <= lootRadius * lootRadius;
        private void Changed() { if (!keepDepletedVisible && Record != null && ReadSnapshot().Count == 0) gameObject.SetActive(false); }
        private void OnDestroy() { if (world != null) world.Changed -= Changed; }
    }
}
