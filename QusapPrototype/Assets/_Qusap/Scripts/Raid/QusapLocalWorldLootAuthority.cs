using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;

namespace Qusap
{
    public sealed class QusapLocalWorldLootAuthority : IQusapWorldLootAuthority
    {
        private sealed class Spawn
        { public string Id; public QusapWorldLootRecipe Recipe; public bool Opened, Generating; public long Version; public int Generations; }
        private readonly QusapRaidSession session;
        private readonly QusapRaidLootCatalog catalog;
        private readonly Func<QusapLootDefinition, QusapWeaponInstance> weapons;
        private readonly Func<string, string, bool> reachable;
        private readonly Dictionary<string, Spawn> spawns = new(StringComparer.Ordinal);
        public QusapLocalWorldLootAuthority(QusapRaidSession raid, QusapRaidLootCatalog definitions, ulong seed,
            Func<QusapLootDefinition, QusapWeaponInstance> weaponFactory = null, Func<string, string, bool> canReach = null)
        { session = raid ?? throw new ArgumentNullException(nameof(raid)); catalog = definitions ?? throw new ArgumentNullException(nameof(definitions)); Seed = seed; weapons = weaponFactory; reachable = canReach; }
        public ulong Seed { get; private set; }
        public string RaidId => session.World.RaidId;
        public int RejectedRequests { get; private set; }
        public int ConfirmedTransfers { get; private set; }
        public int DuplicateInstances => session.World.Instances.GroupBy(i => i.LootInstanceId, StringComparer.Ordinal).Sum(g => g.Count() - 1);
        public bool TrySetSeedBeforeOpening(ulong seed)
        { lock (session.World.Gate) { if (spawns.Values.Any(s => s.Opened || s.Generating) || session.IsFinished) return false; Seed = seed; return true; } }
        private QusapWorldLootReply Reject(QusapWorldLootResult reason, string id = null)
        { RejectedRequests++; return new QusapWorldLootReply(reason, id); }
        public QusapWorldLootResult RegisterSpawn(string spawnPointId, QusapWorldLootTable table)
        {
            lock (session.World.Gate)
            {
                if (session.IsFinished || !QusapRaidInstanceIdentity.ValidSpawnPoint(spawnPointId) || table == null) return QusapWorldLootResult.InvalidSpawn;
                QusapWorldLootRecipe recipe;
                try { recipe = table.Freeze(catalog); } catch { return QusapWorldLootResult.InvalidSpawn; }
                if (!session.World.RegisterWorldSpawn(spawnPointId)) return QusapWorldLootResult.DuplicateSpawn;
                spawns.Add(spawnPointId, new Spawn { Id = spawnPointId, Recipe = recipe }); return QusapWorldLootResult.Success;
            }
        }
        private QusapWorldLootResult Validate(string participant, string spawn, out QusapRaidInventoryState inventory, out Spawn source)
        {
            inventory = session.FindParticipant(participant); source = null;
            if (inventory == null) return QusapWorldLootResult.InvalidParticipant;
            if (session.IsFinished || !inventory.CanOperate) return QusapWorldLootResult.Blocked;
            if (spawn == null || !spawns.TryGetValue(spawn, out source)) return QusapWorldLootResult.MissingContainer;
            if (reachable != null && !reachable(participant, spawn)) return QusapWorldLootResult.OutOfReach;
            return QusapWorldLootResult.Success;
        }
        public QusapWorldLootReply RequestOpen(QusapWorldLootOpenRequest request, CancellationToken cancellation = default)
        {
            lock (session.World.Gate)
            {
                if (cancellation.IsCancellationRequested) return Reject(QusapWorldLootResult.Cancelled);
                var valid = Validate(request.ParticipantId, request.SpawnPointId, out _, out var source);
                if (valid != QusapWorldLootResult.Success) return Reject(valid);
                if (source.Opened) return new QusapWorldLootReply(QusapWorldLootResult.Success);
                if (source.Generating) return Reject(QusapWorldLootResult.Reserved);
                source.Generating = true;
                try
                {
                    var rolls = source.Recipe.Resolve(RaidId, source.Id, Seed);
                    if (!session.World.CreateWorldContainerItems(source.Id, rolls, weapons, cancellation))
                        return Reject(cancellation.IsCancellationRequested ? QusapWorldLootResult.Cancelled : QusapWorldLootResult.GenerationRejected);
                    source.Opened = true; source.Generations++; source.Version++;
                }
                catch { return Reject(QusapWorldLootResult.GenerationRejected); }
                finally { source.Generating = false; }
            }
            session.World.Notify(); return new QusapWorldLootReply(QusapWorldLootResult.Success);
        }
        public QusapWorldLootReply RequestTake(QusapWorldLootTakeRequest request, CancellationToken cancellation = default)
        {
            QusapWorldLootReply reply;
            lock (session.World.Gate)
            {
                if (cancellation.IsCancellationRequested) return Reject(QusapWorldLootResult.Cancelled, request.InstanceId);
                var valid = Validate(request.ParticipantId, request.SpawnPointId, out var inventory, out var source);
                if (valid != QusapWorldLootResult.Success) return Reject(valid, request.InstanceId);
                if (!source.Opened) return Reject(QusapWorldLootResult.NotOpened, request.InstanceId);
                if (request.ExpectedVersion != source.Version) return Reject(QusapWorldLootResult.StaleRequest, request.InstanceId);
                reply = session.World.TransferWorldContainer(inventory, source.Id, request.InstanceId, request.Quantity, cancellation);
                if (!reply.Confirmed) return Reject(reply.Result, request.InstanceId);
                source.Version++; ConfirmedTransfers++;
            }
            session.World.Notify(); return reply;
        }
        public QusapWorldLootContainerSnapshot ReadContainer(string spawnPointId)
        {
            lock (session.World.Gate)
            {
                if (spawnPointId == null || !spawns.TryGetValue(spawnPointId, out var spawn)) return null;
                var items = session.World.Find(QusapLootLocation.WorldContainer, spawnPointId).Select(i => new QusapLootSnapshot(i)).ToArray();
                var state = !spawn.Opened ? QusapWorldLootState.Unopened : items.Length == 0 ? QusapWorldLootState.Depleted : QusapWorldLootState.Opened;
                return new QusapWorldLootContainerSnapshot(RaidId, Seed, spawn.Id, spawn.Recipe.TableId, state, spawn.Version, spawn.Generations, items);
            }
        }
    }
}
