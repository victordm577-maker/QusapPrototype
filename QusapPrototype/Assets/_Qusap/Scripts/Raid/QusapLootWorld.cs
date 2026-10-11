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
        private readonly List<QusapRaidWeaponAdapter> weaponAdapters = new();
        public int LoanersCreated { get; private set; }
        public int LoanersDestroyed { get; private set; }
        internal void RegisterAdapter(QusapRaidWeaponAdapter adapter) => weaponAdapters.Add(adapter);
        internal void RemoveAdapter(QusapRaidWeaponAdapter adapter) => weaponAdapters.Remove(adapter);
        public QusapLootInstance CreateLoaner(QusapLootDefinition definition, QusapWeaponInstance native)
        {
            if (definition?.DefinitionId != "raid_qusap_sword_blue") throw new ArgumentException("Use the approved basic sword canonical");
            var item = Create(definition, "RaidLoaner", native); item.RaidLoaner = true; LoanersCreated++; return item;
        }
        public void DiscardLoaners(bool onlyResolved = false)
        {
            lock (Gate)
            {
                foreach (var item in items.Values.Where(i => i.RaidLoaner && !i.Weapon.IsRetired && (!onlyResolved || i.Location == QusapLootLocation.Consumed)))
                {
                    var adapter = weaponAdapters.FirstOrDefault(a => ReferenceEquals(a.CurrentWeapon, item.Weapon));
                    if (adapter != null && !adapter.TryRelease(item.Weapon)) throw new InvalidOperationException("Loaner cleanup must release native ownership");
                    item.Weapon.RetireFromRaid(); Move(item, QusapLootLocation.Consumed); LoanersDestroyed++;
                }
            }
        }
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
            QusapWeaponInstance weapon = null, int quantity = 1)
        {
            lock (Gate)
            {
                if (definition == null || string.IsNullOrWhiteSpace(definition.DefinitionId))
                    throw new ArgumentException("Missing definition.");
                if (quantity < 1 || quantity > definition.MaxStack || (!definition.IsStackable && quantity != 1))
                    throw new ArgumentOutOfRangeException(nameof(quantity));
                if ((definition.Category == QusapLootCategory.Weapon) != (weapon != null)
                    || (weapon != null && (definition.WeaponDefinition == null
                        || definition.WeaponDefinition.Id != weapon.Definition.Id)))
                    throw new ArgumentException("A weapon loot record must reference an existing compatible weapon.");
                if (weapon != null && items.Values.Any(i => i.Weapon != null
                    && (ReferenceEquals(i.Weapon, weapon) || i.Weapon.InstanceId == weapon.InstanceId)))
                    throw new InvalidOperationException("Duplicate native weapon identity.");
                var item = new QusapLootInstance(RaidId + "/loot/" + checked(++sequence), RaidId,
                    definition, provenance ?? string.Empty, weapon, quantity);
                item.Location = QusapLootLocation.World; item.SlotIndex = -1;
                items.Add(item.LootInstanceId, item);
                return item;
            }
        }

        public QusapLootInstance Get(string id) { lock (Gate) return id != null && items.TryGetValue(id, out var item) ? item : null; }
        // Import the actual withdrawn instance; no clone, new ID, pickup merge or second inventory.
        internal bool CanImport(IEnumerable<QusapLootInstance> incoming)
        {
            var list = incoming.ToArray();
            return list.All(i => i != null && !items.ContainsKey(i.LootInstanceId) && !i.RaidLoaner
                && (i.Weapon == null || (i.Weapon.IsFree && !items.Values.Any(j => j.Weapon?.InstanceId == i.Weapon.InstanceId))))
                && list.Select(i => i.LootInstanceId).Distinct().Count() == list.Length;
        }
        internal void Import(QusapLootInstance item) { items.Add(item.LootInstanceId, item); }
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
                    if (item.RaidLoaner || !item.Definition.CanEnterSecurePocket) return QusapLootResult.Ineligible;
                    if (Find(target, destination.ParticipantId).Length != 0) return QusapLootResult.Occupied;
                    slot = 0;
                }
                else if (target == QusapLootLocation.Backpack)
                {
                    if (!item.Definition.CanEnterBackpack) return QusapLootResult.Ineligible;
                    // Acquisition may merge; explicit pocket transfers always preserve the complete instance.
                    if (item.Definition.IsStackable && (source == QusapLootLocation.World || source == QusapLootLocation.DeathContainer))
                    {
                        var partials = Find(target, destination.ParticipantId).Where(i => i.DefinitionId == item.DefinitionId
                            && i.RaidId == item.RaidId && i.Provenance == item.Provenance && i.Weapon == null
                            && i.Quantity < i.Definition.MaxStack).ToArray();
                        if (partials.Any(i => i.Reserved)) return QusapLootResult.Reserved;
                        int remaining = item.Quantity;
                        var additions = partials.Select(i => { int add = Math.Min(remaining, i.Definition.MaxStack - i.Quantity); remaining -= add; return add; }).ToArray();
                        slot = remaining == 0 ? -1 : destination.FindEmptyBackpackSlot();
                        if (remaining > 0 && slot < 0) return QusapLootResult.Full;
                        var transaction = partials.Concat(new[] { item }).ToArray();
                        if (!Reserve(transaction)) return QusapLootResult.Reserved;
                        for (int i = 0; i < partials.Length; i++) partials[i].Quantity += additions[i];
                        item.Quantity = remaining;
                        Move(item, remaining == 0 ? QusapLootLocation.Merged : target,
                            remaining == 0 ? null : destination.ParticipantId, slot);
                        Release(transaction);
                        goto Completed;
                    }
                    slot = destination.FindEmptyBackpackSlot();
                    if (slot < 0) return QusapLootResult.Full;
                }
                else return QusapLootResult.InvalidDestination;
                if (!Reserve(new[] { item })) return QusapLootResult.Reserved;
                Move(item, target, destination.ParticipantId, slot);
                Release(new[] { item });
            Completed:;
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
