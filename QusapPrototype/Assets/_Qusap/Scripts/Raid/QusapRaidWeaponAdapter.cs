using System;
using System.Collections.Generic;
using System.Linq;

namespace Qusap
{
    // Only QusapWeaponEquipment owns an equipped sword. This adapter wraps its exact
    // instance and never stores an equipped slot or copies OwnerEntityId.
    public sealed class QusapRaidWeaponAdapter : IDisposable
    {
        private readonly QusapLootWorld world;
        private readonly QusapRaidInventoryState inventory;
        private readonly QusapWeaponEquipment equipment;
        private readonly IReadOnlyList<QusapLootDefinition> definitions;
        private bool controlled;
        public QusapRaidWeaponAdapter(QusapLootWorld world, QusapRaidInventoryState inventory,
            QusapWeaponEquipment equipment, IReadOnlyList<QusapLootDefinition> definitions)
        {
            this.world = world; this.inventory = inventory; this.equipment = equipment; this.definitions = definitions;
            inventory.BindEquipment(() => equipment.EquippedWeapon);
            equipment.WeaponTransitioned += Transitioned;
            world.RegisterAdapter(this);
            TrackEquipped();
        }
        public QusapWeaponInstance CurrentWeapon => equipment.EquippedWeapon;
        internal bool BelongsTo(QusapLootWorld ledger, QusapRaidInventoryState state)
            => ReferenceEquals(world, ledger) && ReferenceEquals(inventory, state);
        public QusapLootInstance TrackEquipped()
        {
            lock (world.Gate)
            {
                var weapon = equipment.EquippedWeapon;
                if (weapon == null) return null;
                var item = world.FindWeapon(weapon);
                if (item == null)
                {
                    var definition = definitions.FirstOrDefault(d => d.Category == QusapLootCategory.Weapon
                        && d.WeaponDefinition != null && d.WeaponDefinition.Id == weapon.Definition.Id);
                    if (definition == null) throw new InvalidOperationException("Unregistered native weapon definition.");
                    item = world.Create(definition, "Native equipment", weapon);
                }
                if (!item.Reserved) world.Move(item, QusapLootLocation.Equipped, inventory.ParticipantId);
                return item;
            }
        }
        private void Transitioned(QusapWeaponTransition transition)
        {
            if (controlled) return;
            lock (world.Gate)
            {
                var released = world.FindWeapon(transition.Weapon);
                if (released != null && !released.Reserved && released.Location == QusapLootLocation.Equipped
                    && !ReferenceEquals(transition.Weapon, equipment.EquippedWeapon))
                    world.Move(released, QusapLootLocation.World);
                TrackEquipped(); // Native swap is already completely committed when either event fires.
            }
            world.Notify();
        }
        internal bool TryRelease(QusapWeaponInstance expected)
        {
            if (!ReferenceEquals(equipment.EquippedWeapon, expected)) return false;
            controlled = true;
            try { return equipment.TryDrop(out var released, out _) == QusapWeaponOperationResult.Success && ReferenceEquals(released, expected); }
            finally { controlled = false; }
        }
        internal void Restore(QusapWeaponInstance weapon)
        {
            controlled = true;
            try
            {
                if (equipment.TryEquip(weapon, out _) != QusapWeaponOperationResult.Success)
                    throw new InvalidOperationException("Unable to restore reserved native weapon after rejected settlement.");
            }
            finally { controlled = false; }
        }
        public QusapLootResult TryStoreEquippedWeapon()
        {
            lock (world.Gate)
            {
                if (!inventory.CanOperate) return QusapLootResult.Blocked;
                int slot = inventory.FindEmptyBackpackSlot();
                if (slot < 0) return QusapLootResult.Full;
                var item = TrackEquipped();
                if (item == null) return QusapLootResult.Missing;
                if (!item.Definition.CanEnterBackpack) return QusapLootResult.Ineligible;
                if (!world.Reserve(new[] { item })) return QusapLootResult.Reserved;
                try
                {
                    if (!TryRelease(item.Weapon)) return QusapLootResult.EquipmentRejected;
                    world.Move(item, QusapLootLocation.Backpack, inventory.ParticipantId, slot);
                }
                finally { world.Release(new[] { item }); }
            }
            world.Notify(); return QusapLootResult.Success;
        }
        public QusapLootResult TryEquipWeaponFromContainer(string containerId, string id)
            => TryEquipExisting(QusapLootLocation.DeathContainer, containerId, id);
        public QusapLootResult TryEquipWeaponFromBackpack(string id)
            => TryEquipExisting(QusapLootLocation.Backpack, inventory.ParticipantId, id);
        private QusapLootResult TryEquipExisting(QusapLootLocation source, string holder, string id)
        {
            lock (world.Gate)
            {
                if (!inventory.CanOperate) return QusapLootResult.Blocked;
                if (equipment.HasWeapon) return QusapLootResult.Occupied;
                var item = world.Get(id);
                if (item == null) return QusapLootResult.Missing;
                if (item.Location != source || item.HolderId != holder) return QusapLootResult.InvalidSource;
                if (item.Weapon == null || !item.Weapon.IsFree) return QusapLootResult.Ineligible;
                if (!world.Reserve(new[] { item })) return QusapLootResult.Reserved;
                controlled = true;
                try
                {
                    if (equipment.TryEquip(item.Weapon, out _) != QusapWeaponOperationResult.Success)
                        return QusapLootResult.EquipmentRejected;
                    world.Move(item, QusapLootLocation.Equipped, inventory.ParticipantId);
                }
                finally { controlled = false; world.Release(new[] { item }); }
            }
            world.Notify(); return QusapLootResult.Success;
        }
        public void Dispose()
        {
            var current = equipment != null ? CurrentWeapon : null;
            if (current != null && world.FindWeapon(current)?.RaidLoaner == true) TryRelease(current);
            world.RemoveAdapter(this); if (equipment != null) equipment.WeaponTransitioned -= Transitioned;
        }
    }
}
