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
        public QusapRaidSession(string raidId, IQusapStashRepository stash = null)
        { World = new QusapLootWorld(raidId); Stash = stash ?? new QusapMemoryStashRepository(); }
        public QusapLootWorld World { get; }
        public IQusapStashRepository Stash { get; }
        public IReadOnlyList<QusapDeathLootRecord> Containers { get { lock (World.Gate) return containers.Values.ToArray(); } }
        public event Action<QusapDeathLootRecord> DeathSettled;
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
                    || registered != state) return QusapLootResult.InvalidSource;
                string key = World.RaidId + "/death/" + state.ParticipantId;
                if (containers.ContainsKey(key)) return QusapLootResult.Success;
                if (state.CanOperate) return QusapLootResult.Blocked;
                if (settling.Contains(key)) return QusapLootResult.Reserved;
                state.MarkPending();
                var equipped = weaponAdapter?.TrackEquipped();
                var cargo = World.Find(QusapLootLocation.Backpack, state.ParticipantId).ToList();
                if (equipped != null) cargo.Add(equipped);
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
                    try { accepted = Stash.TryCommitSettlement(key, state.ProfileId, Array.AsReadOnly(pocket)); }
                    catch { accepted = false; }
                    if (!accepted)
                    {
                        if (released) weaponAdapter.Restore(equipped.Weapon);
                        return QusapLootResult.StashUnavailable;
                    }
                    foreach (var item in cargo) World.Move(item, QusapLootLocation.DeathContainer, key);
                    foreach (var item in pocket) World.Move(item, QusapLootLocation.Stash, state.ProfileId);
                    container = new QusapDeathLootRecord(key, state.ParticipantId, cargo.Select(i => new QusapLootSnapshot(i)).ToArray());
                    containers.Add(key, container); state.MarkSettled();
                }
                finally { World.Release(all); settling.Remove(key); }
            }
            World.Notify(); DeathSettled?.Invoke(container);
            return QusapLootResult.Success;
        }
        public void Dispose()
        { foreach (var state in inventories.Values) state.Dispose(); DeathSettled = null; }
    }
}
