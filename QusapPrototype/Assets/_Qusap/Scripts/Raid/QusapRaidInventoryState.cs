using System;
using System.Linq;

namespace Qusap
{
    public sealed class QusapRaidInventoryState : IDisposable
    {
        private readonly QusapLootWorld world;
        private Func<QusapWeaponInstance> readEquipped;
        public QusapRaidInventoryState(QusapLootWorld world, string participantId, string profileId, int capacity = 6)
        {
            this.world = world ?? throw new ArgumentNullException(nameof(world));
            if (string.IsNullOrWhiteSpace(participantId) || string.IsNullOrWhiteSpace(profileId) || capacity < 1)
                throw new ArgumentException("Invalid participant or capacity.");
            ParticipantId = participantId; ProfileId = profileId; Capacity = capacity;
            world.Changed += OnChanged;
        }
        public string ParticipantId { get; }
        public string ProfileId { get; }
        public int Capacity { get; }
        public QusapRaidInventoryStatus Status { get; private set; }
        public bool CanOperate => Status == QusapRaidInventoryStatus.Active;
        public event Action InventoryChanged;
        internal void BindEquipment(Func<QusapWeaponInstance> read) => readEquipped = read;
        public void BlockOperations() { if (CanOperate) { Status = QusapRaidInventoryStatus.Blocked; OnChanged(); } }
        internal void MarkPending() { Status = QusapRaidInventoryStatus.SettlementPending; }
        internal void MarkSettled() { Status = QusapRaidInventoryStatus.Settled; }
        internal void MarkExtractionPending() { Status = QusapRaidInventoryStatus.ExtractionPending; }
        internal void MarkExtracted() { Status = QusapRaidInventoryStatus.Extracted; }
        internal void RejectExtraction() { if (Status == QusapRaidInventoryStatus.ExtractionPending) Status = QusapRaidInventoryStatus.Active; }
        private void OnChanged() => InventoryChanged?.Invoke();
        public void Dispose() { world.Changed -= OnChanged; InventoryChanged = null; }
        internal int FindEmptyBackpackSlot()
        {
            var occupied = world.Find(QusapLootLocation.Backpack, ParticipantId);
            for (int i = 0; i < Capacity; i++) if (!occupied.Any(item => item.SlotIndex == i)) return i;
            return -1;
        }
        public QusapLootResult TryPickup(string id) => world.Transfer(this, id, QusapLootLocation.World, null, QusapLootLocation.Backpack);
        public QusapLootResult TryMoveToSecurePocket(string id) => world.Transfer(this, id, QusapLootLocation.Backpack, ParticipantId, QusapLootLocation.SecurePocket);
        public QusapLootResult TryMoveToBackpack(string id) => world.Transfer(this, id, QusapLootLocation.SecurePocket, ParticipantId, QusapLootLocation.Backpack);
        public QusapLootResult TryTakeFromDeathContainer(string container, string id) => world.Transfer(this, id, QusapLootLocation.DeathContainer, container, QusapLootLocation.Backpack);

        public QusapLootResult TryConsume(string id, Func<float, float> heal)
        {
            QusapLootInstance item;
            lock (world.Gate)
            {
                if (!CanOperate) return QusapLootResult.Blocked;
                item = world.Get(id);
                if (item == null) return QusapLootResult.Missing;
                if (item.HolderId != ParticipantId || (item.Location != QusapLootLocation.Backpack
                    && item.Location != QusapLootLocation.SecurePocket)) return QusapLootResult.InvalidSource;
                if (heal == null || item.Definition.ConsumableType != QusapConsumableType.Healing
                    || item.Definition.Category != QusapLootCategory.Consumable) return QusapLootResult.Ineligible;
                if (!world.Reserve(new[] { item })) return QusapLootResult.Reserved;
            }
            float applied;
            try { applied = heal(item.Definition.HealAmount); }
            catch { lock (world.Gate) world.Release(new[] { item }); throw; }
            lock (world.Gate)
            {
                if (!float.IsFinite(applied) || applied <= 0f) { world.Release(new[] { item }); return QusapLootResult.NoEffect; }
                world.Move(item, QusapLootLocation.Consumed);
                world.Release(new[] { item });
            }
            world.Notify();
            return QusapLootResult.Success;
        }

        public QusapRaidInventorySnapshot ReadInventorySnapshot()
        {
            lock (world.Gate)
            {
                var slots = new QusapLootSnapshot?[Capacity];
                foreach (var item in world.Find(QusapLootLocation.Backpack, ParticipantId)) slots[item.SlotIndex] = new QusapLootSnapshot(item);
                var pocket = world.Find(QusapLootLocation.SecurePocket, ParticipantId).FirstOrDefault();
                var weapon = world.FindWeapon(readEquipped?.Invoke());
                return new QusapRaidInventorySnapshot(ParticipantId, Status, slots,
                    pocket != null ? new QusapLootSnapshot(pocket) : null,
                    weapon != null ? new QusapLootSnapshot(weapon) : null);
            }
        }
    }
}
