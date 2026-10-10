using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace Qusap
{
    public sealed class QusapPersistentStashRepository : IQusapStashRepository
    {
        private readonly object gate = new();
        private readonly QusapMemoryStashRepository authority = new();
        private readonly List<QusapLootWorld> allocators = new();
        private Func<ulong> nativeAllocator;
        private QusapProfileContent committed;
        public QusapProfileFileStore Storage { get; }
        public string ProfileId => committed.ProfileId;
        public int SchemaVersion => committed.SchemaVersion;
        public long Revision => committed.Revision;
        public ulong NextItemInstanceId => committed.NextItemInstanceId;
        public ulong NextNativeWeaponInstanceId => committed.NextNativeWeaponInstanceId;
        public string Checksum { get; private set; }
        public string LastResult { get; private set; }

        public QusapPersistentStashRepository(string directory, IReadOnlyList<QusapLootDefinition> definitions,
            string newProfileId = null, IQusapProfileFiles files = null)
        {
            var codec = new QusapProfileCodec(definitions);
            Storage = new QusapProfileFileStore(directory, codec, files);
            var document = Storage.Load();
            committed = document?.Content ?? new QusapProfileContent
            { ProfileId = newProfileId ?? Guid.NewGuid().ToString("N"), SavedAtUtc = DateTime.UtcNow.ToString("O", CultureInfo.InvariantCulture) };
            Checksum = document?.Checksum ?? QusapProfileCodec.Seal(committed).Checksum;
            if (document != null) authority.Restore(ProfileId, codec.Restore(committed), committed.Settlements);
            LastResult = Storage.LastResult;
        }

        internal void ObserveAllocator(QusapLootWorld world) { lock (gate) allocators.Add(world); }
        public void ObserveNativeAllocator(Func<ulong> readNext) { lock (gate) nativeAllocator = readNext; }
        public IReadOnlyList<QusapLootSnapshot> ReadStashSnapshot(string profileId) => authority.ReadStashSnapshot(profileId);

        public bool TryCommitSettlement(string settlementId, string profileId, IReadOnlyList<QusapLootInstance> items)
        {
            lock (gate)
            {
                // Diagnostic multiplayer partners retain the approved in-memory behavior.
                if (profileId != ProfileId) return authority.TryCommitSettlement(settlementId, profileId, items);
                bool prepared = false;
                bool accepted = authority.TryCommitSettlement(settlementId, profileId, items, () =>
                {
                    if (items.Count == 0) return true;
                    prepared = true;
                    var stash = authority.ReadStashSnapshot(profileId).Select(QusapProfileItem.From)
                        .Concat(items.Select(item => QusapProfileItem.From(new QusapLootSnapshot(item)))).ToArray();
                    ulong nextItem = Math.Max(committed.NextItemInstanceId,
                        allocators.Select(a => a.NextItemInstanceId).DefaultIfEmpty(1UL).Max());
                    ulong maxItem = stash.Select(item => ulong.Parse(item.InstanceId.Substring(item.RaidId.Length + 6),
                        NumberStyles.None, CultureInfo.InvariantCulture)).DefaultIfEmpty(0UL).Max();
                    nextItem = Math.Max(nextItem, checked(maxItem + 1));
                    ulong maxNative = stash.Select(item => item.NativeWeaponInstanceId).DefaultIfEmpty(0UL).Max();
                    var receipts = authority.ReadReceipts(profileId)
                        .Where(r => r.Fingerprint.Split('\n').Length > 1 && r.Fingerprint.Split('\n')[1].Length > 0)
                        .Append(new QusapProfileReceipt
                        {
                            SettlementId = settlementId,
                            Fingerprint = profileId + "\n" + string.Join("\n", items.Select(i => i.LootInstanceId).OrderBy(id => id, StringComparer.Ordinal))
                        }).ToArray();
                    var candidate = new QusapProfileContent
                    {
                        ProfileId = ProfileId, Revision = checked(Revision + 1),
                        SavedAtUtc = DateTime.UtcNow.ToString("O", CultureInfo.InvariantCulture),
                        NextItemInstanceId = nextItem,
                        NextNativeWeaponInstanceId = Math.Max(Math.Max(committed.NextNativeWeaponInstanceId,
                            nativeAllocator?.Invoke() ?? 1), checked(maxNative + 1)),
                        Stash = stash, Settlements = receipts
                    };
                    var document = QusapProfileCodec.Seal(candidate);
                    if (!Storage.TryWrite(document)) { LastResult = Storage.LastResult; return false; }
                    committed = candidate; Checksum = document.Checksum; LastResult = Storage.LastResult;
                    return true;
                });
                if (!prepared) LastResult = accepted ? "No change; no additional revision or disk write" : "Settlement rejected; no disk write";
                return accepted;
            }
        }

        // Transactions are already durable; normal closure never invents an extra revision.
        public bool ConfirmUnchanged()
        { lock (gate) { LastResult = "Confirmed unchanged; no additional revision or disk write"; return true; } }
    }
}
