using System;
using System.Collections.Generic;
using System.Linq;

namespace Qusap
{
    // One ledger per raid. Reservations also protect against synchronous callback reentry.
    public sealed class QusapLootWorld
    {
        internal readonly object Gate = new();
        private readonly Dictionary<string, QusapLootInstance> items = new(StringComparer.Ordinal);
        private ulong sequence;
        public QusapLootWorld(string raidId, ulong nextItemInstanceId = 1)
        {
            RaidId = !string.IsNullOrWhiteSpace(raidId) ? raidId : throw new ArgumentException(nameof(raidId));
            if (nextItemInstanceId == 0) throw new ArgumentOutOfRangeException(nameof(nextItemInstanceId));
            sequence = nextItemInstanceId - 1;
        }
        public string RaidId { get; }
        public ulong NextItemInstanceId { get { lock (Gate) return checked(sequence + 1); } }
        public event Action Changed;
        public IReadOnlyList<QusapLootInstance> Instances { get { lock (Gate) return items.Values.ToArray(); } }

        public QusapLootInstance Create(QusapLootDefinition definition, string provenance,
            QusapWeaponInstance weapon = null)
        {
            lock (Gate)
            {
                if (definition == null || string.IsNullOrWhiteSpace(definition.DefinitionId))
                    throw new ArgumentException("Missing definition.");
                if ((definition.Category == QusapLootCategory.Weapon) != (weapon != null)
                    || (weapon != null && (definition.WeaponDefinition == null
                        || definition.WeaponDefinition.Id != weapon.Definition.Id)))
                    throw new ArgumentException("A weapon loot record must reference an existing compatible weapon.");
                if (weapon != null && items.Values.Any(i => i.Weapon != null
                    && (ReferenceEquals(i.Weapon, weapon) || i.Weapon.InstanceId == weapon.InstanceId)))
                    throw new InvalidOperationException("Duplicate native weapon identity.");
                var item = new QusapLootInstance(RaidId + "/loot/" + checked(++sequence), RaidId,
                    definition, provenance ?? string.Empty, weapon);
                item.Location = QusapLootLocation.World; item.SlotIndex = -1;
                items.Add(item.LootInstanceId, item);
                return item;
            }
        }

        public QusapLootInstance Get(string id) { lock (Gate) return id != null && items.TryGetValue(id, out var item) ? item : null; }
        public QusapLootInstance FindWeapon(QusapWeaponInstance weapon)
        { lock (Gate) return items.Values.FirstOrDefault(i => ReferenceEquals(i.Weapon, weapon) && weapon != null); }
        internal QusapLootInstance[] Find(QusapLootLocation location, string holder)
        { lock (Gate) return items.Values.Where(i => i.Location == location && i.HolderId == holder).OrderBy(i => i.SlotIndex).ThenBy(i => i.LootInstanceId, StringComparer.Ordinal).ToArray(); }
        public IReadOnlyList<QusapLootSnapshot> ReadContainer(string id)
        { lock (Gate) return Array.AsReadOnly(Find(QusapLootLocation.DeathContainer, id).Select(i => new QusapLootSnapshot(i)).ToArray()); }
        internal void Notify() => Changed?.Invoke();

        internal bool Reserve(IEnumerable<QusapLootInstance> requested)
        {
            var list = requested.ToArray();
            if (list.Any(i => i == null || i.Reserved) || list.Distinct().Count() != list.Length) return false;
            foreach (var item in list) item.Reserved = true;
            return true;
        }
        internal void Release(IEnumerable<QusapLootInstance> requested)
        { foreach (var item in requested) item.Reserved = false; }
        internal void Move(QusapLootInstance item, QusapLootLocation location, string holder = null, int slot = -1)
        { item.Location = location; item.HolderId = holder; item.SlotIndex = slot; }

        internal QusapLootResult Transfer(QusapRaidInventoryState destination, string id,
            QusapLootLocation source, string sourceHolder, QusapLootLocation target)
        {
            lock (Gate)
            {
                if (!destination.CanOperate) return QusapLootResult.Blocked;
                var item = Get(id);
                if (item == null) return QusapLootResult.Missing;
                if (item.Location != source || item.HolderId != sourceHolder) return QusapLootResult.InvalidSource;
                if (item.Reserved) return QusapLootResult.Reserved;
                if (item.Weapon != null && item.Weapon.IsEquipped) return QusapLootResult.InvalidSource;
                int slot;
                if (target == QusapLootLocation.SecurePocket)
                {
                    if (!item.Definition.CanEnterSecurePocket) return QusapLootResult.Ineligible;
                    if (Find(target, destination.ParticipantId).Length != 0) return QusapLootResult.Occupied;
                    slot = 0;
                }
                else if (target == QusapLootLocation.Backpack)
                {
                    slot = destination.FindEmptyBackpackSlot();
                    if (slot < 0) return QusapLootResult.Full;
                }
                else return QusapLootResult.InvalidDestination;
                if (!Reserve(new[] { item })) return QusapLootResult.Reserved;
                Move(item, target, destination.ParticipantId, slot);
                Release(new[] { item });
            }
            Notify();
            return QusapLootResult.Success;
        }

        internal void ObserveWeapon(QusapLootInstance item, QusapWeaponInstance equipped, string participant)
        {
            lock (Gate)
            {
                if (item.Reserved) return;
                if (ReferenceEquals(item.Weapon, equipped)) Move(item, QusapLootLocation.Equipped, participant);
                else if (item.Location == QusapLootLocation.Equipped) Move(item, QusapLootLocation.World);
                else return;
            }
            Notify();
        }
    }
}
