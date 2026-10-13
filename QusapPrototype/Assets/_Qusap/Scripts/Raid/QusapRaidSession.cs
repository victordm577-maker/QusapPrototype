using System;
using System.Collections.Generic;
using System.Linq;

namespace Qusap
{
    public sealed class QusapRaidSession : IDisposable
    {
        private readonly Dictionary<string, QusapRaidInventoryState> inventories = new(StringComparer.Ordinal);
        private readonly Dictionary<string, QusapDeathLootRecord> containers = new(StringComparer.Ordinal);
        private readonly HashSet<string> settling = new(StringComparer.Ordinal);
        private ulong eliminationOrdinal;
        public QusapRaidSession(string raidId, IQusapStashRepository stash = null)
        {
            var persistent = stash as QusapPersistentStashRepository;
            World = new QusapLootWorld(raidId, persistent?.NextItemInstanceId ?? 1);
            Stash = stash ?? new QusapMemoryStashRepository();
            persistent?.ObserveAllocator(World);
        }
        public QusapLootWorld World { get; }
        public bool IsFinished { get; private set; }
        public int ParticipantCount { get { lock (World.Gate) return inventories.Count; } }
        internal QusapRaidInventoryState FindParticipant(string id)
        { lock (World.Gate) return id != null && inventories.TryGetValue(id, out var state) ? state : null; }
        public IQusapStashRepository Stash { get; }
        public IReadOnlyList<QusapDeathLootRecord> Containers { get { lock (World.Gate) return containers.Values.ToArray(); } }
        public event Action<QusapDeathLootRecord> DeathSettled;
        public event Action<QusapRaidInventoryState> ExtractionSettled;
        public int ExtractionTransfersResolved { get; private set; }
        public QusapRaidInventoryState Register(string participant, string profile, int capacity = 6)
        {
            lock (World.Gate)
            {
                if (inventories.ContainsKey(participant)) throw new InvalidOperationException("Participant already registered.");
                var state = new QusapRaidInventoryState(World, participant, profile, capacity);
                inventories.Add(participant, state); return state;
            }
        }

        public QusapLootResult SettleDeath(QusapRaidInventoryState state, QusapRaidWeaponAdapter weaponAdapter = null)
        {
            QusapDeathLootRecord container;
            lock (World.Gate)
            {
                if (state == null || !inventories.TryGetValue(state.ParticipantId, out var registered)
                    || registered != state || (weaponAdapter != null && !weaponAdapter.BelongsTo(World, state))) return QusapLootResult.InvalidSource;
                string key = World.RaidId + "/death/" + state.ParticipantId;
                if (containers.ContainsKey(key)) return QusapLootResult.Success;
                if (state.CanOperate || state.Status == QusapRaidInventoryStatus.Extracted
                    || state.Status == QusapRaidInventoryStatus.ExtractionPending) return QusapLootResult.Blocked;
                if (settling.Contains(key)) return QusapLootResult.Reserved;
                // The durable settlement key remains compatible with ActiveDeploymentManifest recovery.
                // Only a committed elimination advances this raid's authoritative ordinal.
                ulong ordinal = checked(eliminationOrdinal + 1);
                string containerId = QusapDeathLootIdentity.Format(World.RaidId, state.ParticipantId, ordinal);
                state.MarkPending();
                var equipped = weaponAdapter?.TrackEquipped();
                var cargo = World.Find(QusapLootLocation.Backpack, state.ParticipantId).Where(i => i.Quantity > 0).ToList();
                if (equipped != null && equipped.Quantity > 0 && !cargo.Contains(equipped)) cargo.Add(equipped);
                // A discarded loaner is not recoverable value after its last carrier dies.
                // A rival's already-acquired loaner belongs to that rival and is not touched here.
                cargo.AddRange(World.Instances.Where(i => i.RaidLoaner && i.LoanerLastCarrierId == state.ParticipantId
                    && i.Location == QusapLootLocation.World && i.Weapon.IsFree && !i.Weapon.IsRetired && i.Quantity > 0));
                var pocket = World.Find(QusapLootLocation.SecurePocket, state.ParticipantId);
                var all = cargo.Concat(pocket).ToArray();
                if (!World.Reserve(all)) return QusapLootResult.Reserved;
                settling.Add(key);
                bool released = false;
                try
                {
                    if (equipped != null)
                    {
                        released = weaponAdapter.TryRelease(equipped.Weapon);
                        if (!released) return QusapLootResult.EquipmentRejected;
                    }
                    bool accepted;
                    var protectedItems = pocket.Where(i => i.CanPersist).ToArray();
                    try { accepted = Stash.TryCommitSettlement(key, state.ProfileId, Array.AsReadOnly(protectedItems)); }
                    catch { accepted = false; }
                    if (!accepted)
                    {
                        if (released) weaponAdapter.Restore(equipped.Weapon);
                        return QusapLootResult.StashUnavailable;
                    }
                    foreach (var item in cargo)
                    {
                        if (item.RaidLoaner) { World.DestroyDeathLoaner(item); continue; }
                        World.Move(item, item.Definition.CanDropOnDeath ? QusapLootLocation.DeathContainer : QusapLootLocation.Consumed,
                            item.Definition.CanDropOnDeath ? containerId : null);
                    }
                    foreach (var item in pocket) World.Move(item, item.CanPersist ? QusapLootLocation.Stash : QusapLootLocation.Consumed,
                        item.CanPersist ? state.ProfileId : null);
                    container = new QusapDeathLootRecord(containerId, state.ParticipantId,
                        cargo.Where(i => !i.RaidLoaner && i.Definition.CanDropOnDeath).Select(i => new QusapLootSnapshot(i)).ToArray(), ordinal);
                    containers.Add(key, container); eliminationOrdinal = ordinal; state.MarkSettled();
                    // A delayed accepted retry after session end cannot revive abandoned raid cargo.
                    if (IsFinished) World.DestroyRemainingDeathLoot();
                }
                finally { World.Release(all); settling.Remove(key); }
            }
            World.Notify(); DeathSettled?.Invoke(container);
            return QusapLootResult.Success;
        }
        // Same ledger, reservation and stash transaction as death; extraction creates no container.
        public QusapLootResult SettleExtraction(QusapRaidInventoryState state, QusapRaidWeaponAdapter weaponAdapter = null)
        {
            lock (World.Gate)
            {
                if (state == null || !inventories.TryGetValue(state.ParticipantId, out var registered)
                    || registered != state || (weaponAdapter != null && !weaponAdapter.BelongsTo(World, state)))
                    return QusapLootResult.InvalidSource;
                if (!state.CanOperate) return QusapLootResult.Blocked;
                var equipped = weaponAdapter?.TrackEquipped();
                var all = World.Find(QusapLootLocation.Backpack, state.ParticipantId)
                    .Concat(World.Find(QusapLootLocation.SecurePocket, state.ParticipantId))
                    .Concat(World.Find(QusapLootLocation.Equipped, state.ParticipantId)).ToArray();
                if (all.Any(i => i.Location == QusapLootLocation.Equipped && !ReferenceEquals(i, equipped)))
                    return QusapLootResult.EquipmentRejected;
                if (!World.Reserve(all)) return QusapLootResult.Reserved;
                state.MarkExtractionPending();
                bool released = false, committed = false;
                try
                {
                    if (equipped != null)
                    {
                        released = weaponAdapter.TryRelease(equipped.Weapon);
                        if (!released) return QusapLootResult.EquipmentRejected;
                    }
                    bool accepted;
                    var persistent = all.Where(i => i.CanPersist).ToArray();
                    try { accepted = Stash.TryCommitSettlement(World.RaidId + "/extraction/" + state.ParticipantId,
                        state.ProfileId, Array.AsReadOnly(persistent)); }
                    catch { accepted = false; }
                    if (!accepted)
                    {
                        if (released) weaponAdapter.Restore(equipped.Weapon);
                        return QusapLootResult.StashUnavailable;
                    }
                    foreach (var item in all) World.Move(item, item.CanPersist ? QusapLootLocation.Stash : QusapLootLocation.Consumed,
                        item.CanPersist ? state.ProfileId : null);
                    state.MarkExtracted(); ExtractionTransfersResolved++; committed = true;
                }
                finally { World.Release(all); if (!committed) state.RejectExtraction(); }
            }
            World.DiscardLoaners(true);
            ExtractionSettled?.Invoke(state); // Retire the owner before publishing the changed ledger.
            World.Notify();
            return QusapLootResult.Success;
        }
        public void FinishSession() => FinishSession(true);
        internal void AbortPreparedDeployment()
        {
            lock (World.Gate)
            {
                if (containers.Count != 0 || ExtractionTransfersResolved != 0) throw new InvalidOperationException("Cannot roll back gameplay");
                foreach (var state in inventories.Values) state.Dispose();
                inventories.Clear(); World.AbortPreparedDeployment(); IsFinished = true;
                DeathSettled = null; ExtractionSettled = null;
            }
        }
        private void FinishSession(bool notify)
        {
            bool changed;
            lock (World.Gate)
            {
                changed = !IsFinished; IsFinished = true;
                changed |= World.DestroyRemainingDeathLoot() > 0;
                World.DiscardLoaners();
            }
            if (changed && notify) World.Notify();
        }
        public void Dispose()
        { FinishSession(false); foreach (var state in inventories.Values) state.Dispose(); DeathSettled = null; ExtractionSettled = null; }
    }
}
