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
        private QusapRaidDeploymentAuthority[] deployments;
        private bool advancing;
        public QusapRaidSession(string raidId, IQusapStashRepository stash = null, QusapRaidTimingSettings timing = null)
        {
            var persistent = stash as QusapPersistentStashRepository;
            World = new QusapLootWorld(raidId, persistent?.NextItemInstanceId ?? 1);
            Stash = stash ?? new QusapMemoryStashRepository();
            if (timing != null) Timing = new QusapRaidTiming(timing);
            persistent?.ObserveAllocator(World);
        }
        public QusapLootWorld World { get; }
        public bool IsFinished { get; private set; }
        public QusapRaidSessionAuthority Competition { get; private set; }
        public QusapRaidTiming Timing { get; }
        public bool CanExtract => !IsFinished && (Timing == null || Timing.ExtractionsOpen);
        public event Action<IReadOnlyList<QusapRaidInventoryState>> ExpirationStarted;
        public bool AdvanceTime(double seconds)
        {
            lock (World.Gate)
            {
                if (advancing || Competition?.ResolutionInProgress == true || Timing == null || !Timing.IsRunning || IsFinished
                    || !double.IsFinite(seconds) || seconds < 0 || World.Instances.Any(i => i.Reserved)) return false;
                advancing = true;
                try
                {
                    Timing.Advance(seconds);
                    if (Timing.HasExpired) ResolveExpiration();
                    return true;
                }
                finally { advancing = false; }
            }
        }
        // Per-profile atomic settlements; accepted profiles stay accepted on a later rejection.
        // The whole roster is fenced before any callback, and Winner stays suppressed on retries.
        private void ResolveExpiration()
        {
            if (!Competition.BeginExpiration()) return;
            var active = Competition.ReadParticipantsSnapshot().Where(p => p.Outcome == QusapRaidParticipantOutcome.Active)
                .Select(p => inventories[p.ParticipantId]).ToArray();
            bool first = !World.GameplayBlocked;
            World.GameplayBlocked = true;
            foreach (var state in active) state.BlockForExpiration();
            if (first) ExpirationStarted?.Invoke(Array.AsReadOnly(active));
            foreach (var state in active)
            {
                var adapter = deployments.Single(d => d.Inventory == state).WeaponAdapter;
                if (SettleDeath(state, adapter) == QusapLootResult.Success
                    && Competition.TryGetOutcome(state.ParticipantId, out var outcome) && outcome == QusapRaidParticipantOutcome.Active)
                    Competition.TryResolve(state.ParticipantId, QusapRaidParticipantOutcome.Eliminated);
            }
            Competition.EvaluateVictory();
        }
        internal void StartLastSurvivorCompetition(IReadOnlyList<QusapRaidDeploymentAuthority> deployments)
        {
            lock (World.Gate)
            {
                if (Competition != null || IsFinished || deployments.Any(d => d.Session != this
                    || d.Status != QusapDeploymentStatus.Running || !d.Inventory.CanOperate))
                    throw new InvalidOperationException("All shared deployments must be Running");
                var authority = new QusapRaidSessionAuthority(World.RaidId);
                foreach (var state in inventories.Values) authority.Register(state.ParticipantId);
                authority.BindResolutionValidator((id, outcome) => inventories.TryGetValue(id, out var participant)
                    && (outcome == QusapRaidParticipantOutcome.Eliminated ? participant.Status == QusapRaidInventoryStatus.Settled
                        : participant.Status == QusapRaidInventoryStatus.Extracted));
                if (!authority.CompleteSharedDeployment(deployments.Select(d => d.ParticipantId).ToArray(), id =>
                {
                    var player = deployments.Single(d => d.ParticipantId == id);
                    return SettleExtractionCore(player.Inventory, player.WeaponAdapter, true) == QusapLootResult.Success;
                }) || !authority.StartSession()) throw new InvalidOperationException("Shared roster could not be sealed");
                Competition = authority;
                this.deployments = deployments.ToArray();
                Timing?.Start();
                authority.SessionFinished += _ => FinishSession();
            }
        }
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
                if (IsFinished || Competition?.RosterClosed == true || inventories.Count >= 8 || inventories.ContainsKey(participant))
                    throw new InvalidOperationException("Participant roster unavailable.");
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
                if (state.CanOperate || state.Status == QusapRaidInventoryStatus.Winner || state.Status == QusapRaidInventoryStatus.Extracted
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
            Competition?.TryResolve(state.ParticipantId, QusapRaidParticipantOutcome.Eliminated);
            return QusapLootResult.Success;
        }
        // Same ledger, reservation and stash transaction as death; extraction creates no container.
        public QusapLootResult SettleExtraction(QusapRaidInventoryState state, QusapRaidWeaponAdapter weaponAdapter = null)
            => SettleExtractionCore(state, weaponAdapter, false);
        private QusapLootResult SettleExtractionCore(QusapRaidInventoryState state, QusapRaidWeaponAdapter weaponAdapter, bool winner)
        {
            lock (World.Gate)
            {
                if (state == null || !inventories.TryGetValue(state.ParticipantId, out var registered)
                    || registered != state || (weaponAdapter != null && !weaponAdapter.BelongsTo(World, state)))
                    return QusapLootResult.InvalidSource;
                if (!state.CanOperate) return QusapLootResult.Blocked;
                if (!winner && !CanExtract) return QusapLootResult.Blocked;
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
                    try { accepted = winner
                        ? Stash is IQusapVictoryRepository progression && progression.TryCommitVictory(World.RaidId + "/winner/" + state.ParticipantId,
                            state.ProfileId, Array.AsReadOnly(persistent))
                        : Stash.TryCommitSettlement(World.RaidId + "/extraction/" + state.ParticipantId,
                            state.ProfileId, Array.AsReadOnly(persistent)); }
                    catch { accepted = false; }
                    if (!accepted)
                    {
                        if (released) weaponAdapter.Restore(equipped.Weapon);
                        return QusapLootResult.StashUnavailable;
                    }
                    foreach (var item in all)
                    {
                        if (winner && item.RaidLoaner) World.DestroyDeathLoaner(item);
                        else World.Move(item, item.CanPersist ? QusapLootLocation.Stash : QusapLootLocation.Consumed,
                            item.CanPersist ? state.ProfileId : null);
                    }
                    if (winner) state.MarkWinner(); else state.MarkExtracted();
                    ExtractionTransfersResolved++; committed = true;
                }
                finally { World.Release(all); if (!committed) state.RejectExtraction(); }
            }
            World.DiscardLoaners(true);
            ExtractionSettled?.Invoke(state); // Retire the owner before publishing the changed ledger.
            World.Notify();
            if (!winner) Competition?.TryResolve(state.ParticipantId, QusapRaidParticipantOutcome.Extracted);
            return QusapLootResult.Success;
        }
        public void FinishSession()
        {
            if (Competition != null && Competition.Status != QusapRaidSessionStatus.Finished)
            { Competition.EvaluateVictory(); return; }
            FinishSession(true);
        }
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
                changed = !IsFinished; IsFinished = true; Timing?.Stop();
                changed |= World.DestroyRemainingDeathLoot() > 0;
                World.DiscardLoaners();
            }
            if (changed && notify) World.Notify();
        }
        public void Dispose()
        { FinishSession(false); foreach (var state in inventories.Values) state.Dispose(); DeathSettled = null; ExtractionSettled = null; ExpirationStarted = null; }
    }
}
