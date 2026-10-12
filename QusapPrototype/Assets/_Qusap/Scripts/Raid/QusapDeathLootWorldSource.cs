using System;
using System.Linq;

namespace Qusap
{
    // A predefined source in the approved WorldLoot authority, backed by the original DeathContainer location.
    public sealed partial class QusapLocalWorldLootAuthority
    {
        public QusapWorldLootResult RegisterDeathContainer(QusapDeathLootRecord record, bool startsOpened = false)
        {
            lock (session.World.Gate)
            {
                if (record == null || !session.Containers.Any(r => ReferenceEquals(r, record))) return QusapWorldLootResult.InvalidSpawn;
                if (spawns.TryGetValue(record.ContainerId, out var existing))
                    return ReferenceEquals(existing.Death, record) ? QusapWorldLootResult.Success : QusapWorldLootResult.DuplicateSpawn;
                if (session.IsFinished) return QusapWorldLootResult.Blocked;
                if (!session.World.RegisterWorldSpawn(record.ContainerId)) return QusapWorldLootResult.DuplicateSpawn;
                var source = new Spawn { Id = record.ContainerId, Death = record, Opened = startsOpened };
                source.PreviousDeathContents = ReadSourceContents(source);
                spawns.Add(record.ContainerId, source);
                return QusapWorldLootResult.Success;
            }
        }
        private QusapLootSnapshot[] ReadSourceContents(Spawn source) => session.World.Find(source.Location, source.Id)
            .Where(i => i.Quantity > 0 && !i.RaidLoaner).Select(i => new QusapLootSnapshot(i)).ToArray();
        private void RefreshDeathVersion(Spawn source)
        {
            if (source.Death == null) return;
            var current = ReadSourceContents(source);
            if (!current.Select(i => (i.LootInstanceId, i.Quantity)).SequenceEqual(source.PreviousDeathContents.Select(i => (i.LootInstanceId, i.Quantity))))
            { source.Version++; source.PreviousDeathContents = current; }
        }
    }
}
