using System;
using System.Threading;

namespace Qusap
{
    // Boundary validation surrounds the approved generation/transfer authority without owning loot.
    public sealed class QusapLootInteractionAuthority : IQusapWorldLootAuthority
    {
        private readonly IQusapWorldLootAuthority owner;
        private readonly Func<string, string, QusapLootInteractionRejection> validate;
        public QusapLootInteractionAuthority(IQusapWorldLootAuthority authority, Func<string, string, QusapLootInteractionRejection> access)
        { owner = authority ?? throw new ArgumentNullException(nameof(authority)); validate = access ?? throw new ArgumentNullException(nameof(access)); }
        public int BoundaryRejections { get; private set; }
        public QusapWorldLootResult RegisterSpawn(string id, QusapWorldLootTable table) => owner.RegisterSpawn(id, table);
        public QusapWorldLootContainerSnapshot ReadContainer(string id) => owner.ReadContainer(id);
        private QusapWorldLootReply Reject(QusapLootInteractionRejection reason)
        {
            BoundaryRejections++;
            return new(reason == QusapLootInteractionRejection.OutOfRange || reason == QusapLootInteractionRejection.NoLineOfSight
                ? QusapWorldLootResult.OutOfReach : reason == QusapLootInteractionRejection.InvalidTarget ? QusapWorldLootResult.MissingContainer : QusapWorldLootResult.Blocked);
        }
        public QusapWorldLootReply RequestOpen(QusapWorldLootOpenRequest request, CancellationToken cancellation = default)
        { var reason = validate(request.ParticipantId, request.SpawnPointId); return reason == QusapLootInteractionRejection.None ? owner.RequestOpen(request, cancellation) : Reject(reason); }
        public QusapWorldLootReply RequestTake(QusapWorldLootTakeRequest request, CancellationToken cancellation = default)
        { var reason = validate(request.ParticipantId, request.SpawnPointId); return reason == QusapLootInteractionRejection.None ? owner.RequestTake(request, cancellation) : Reject(reason); }
    }
}
