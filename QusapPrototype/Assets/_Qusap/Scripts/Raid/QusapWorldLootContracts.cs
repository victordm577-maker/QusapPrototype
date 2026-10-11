using System;
using System.Collections.Generic;
using System.Threading;

namespace Qusap
{
    public enum QusapWorldLootState { Unopened, Opened, Depleted }
    public enum QusapWorldLootResult
    { Success, DuplicateSpawn, InvalidSpawn, MissingContainer, InvalidParticipant, Blocked, OutOfReach, NotOpened, InvalidQuantity, StaleRequest, Cancelled, MissingInstance, InvalidSource, Full, Reserved, Ineligible, GenerationRejected }

    public readonly struct QusapWorldLootOpenRequest
    {
        public QusapWorldLootOpenRequest(string participantId, string spawnPointId)
        { ParticipantId = participantId; SpawnPointId = spawnPointId; }
        public string ParticipantId { get; }
        public string SpawnPointId { get; }
    }
    // Constructing or abandoning a request never reserves or moves an instance.
    public readonly struct QusapWorldLootTakeRequest
    {
        public QusapWorldLootTakeRequest(string participantId, string spawnPointId, string instanceId, int quantity, long expectedVersion)
        { ParticipantId = participantId; SpawnPointId = spawnPointId; InstanceId = instanceId; Quantity = quantity; ExpectedVersion = expectedVersion; }
        public string ParticipantId { get; }
        public string SpawnPointId { get; }
        public string InstanceId { get; }
        public int Quantity { get; }
        public long ExpectedVersion { get; }
    }
    public sealed class QusapWorldLootReply
    {
        internal QusapWorldLootReply(QusapWorldLootResult result, string source = null, int quantity = 0,
            string destination = null, string[] mergedInto = null)
        { Result = result; SourceInstanceId = source; ConfirmedQuantity = quantity; DestinationInstanceId = destination; MergedInto = Array.AsReadOnly(mergedInto ?? Array.Empty<string>()); }
        public QusapWorldLootResult Result { get; }
        public bool Confirmed => Result == QusapWorldLootResult.Success;
        public string SourceInstanceId { get; }
        public int ConfirmedQuantity { get; }
        public string DestinationInstanceId { get; }
        public IReadOnlyList<string> MergedInto { get; }
    }
    public sealed class QusapWorldLootContainerSnapshot
    {
        internal QusapWorldLootContainerSnapshot(string raid, ulong seed, string spawn, string table, QusapWorldLootState state,
            long version, int generations, QusapLootSnapshot[] items)
        { RaidId = raid; Seed = seed; SpawnPointId = spawn; TableId = table; State = state; Version = version; GenerationCount = generations; Items = Array.AsReadOnly(items); }
        public string RaidId { get; }
        public ulong Seed { get; }
        public string SpawnPointId { get; }
        public string TableId { get; }
        public QusapWorldLootState State { get; }
        public long Version { get; }
        public int GenerationCount { get; }
        public IReadOnlyList<QusapLootSnapshot> Items { get; }
    }
    // Local implementation accepts synchronous requests; a future adapter can expose these contracts over a real transport.
    public interface IQusapWorldLootAuthority
    {
        QusapWorldLootResult RegisterSpawn(string spawnPointId, QusapWorldLootTable table);
        QusapWorldLootReply RequestOpen(QusapWorldLootOpenRequest request, CancellationToken cancellation = default);
        QusapWorldLootReply RequestTake(QusapWorldLootTakeRequest request, CancellationToken cancellation = default);
        QusapWorldLootContainerSnapshot ReadContainer(string spawnPointId);
    }
}
