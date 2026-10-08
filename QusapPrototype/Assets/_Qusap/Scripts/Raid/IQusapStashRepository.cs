using System.Collections.Generic;

namespace Qusap
{
    public interface IQusapStashRepository
    {
        // All-or-nothing. False/exception must leave the repository unchanged.
        // A repeated key succeeds only with the same profile and exact item identities.
        bool TryCommitSettlement(string settlementId, string profileId, IReadOnlyList<QusapLootInstance> items);
        IReadOnlyList<QusapLootSnapshot> ReadStashSnapshot(string profileId);
    }
}
