using System.Collections.Generic;

namespace Qusap
{
    // Progression commits with the existing settlement, never as a loot instance.
    public interface IQusapVictoryRepository
    {
        bool TryCommitVictory(string settlementId, string profileId, IReadOnlyList<QusapLootInstance> items);
    }
}
