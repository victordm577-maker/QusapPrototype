using System;
using System.Globalization;

namespace Qusap
{
    public static class QusapDeathLootIdentity
    {
        public static string Format(string raidId, string playerId, ulong eliminationOrdinal)
        {
            if (string.IsNullOrWhiteSpace(raidId) || string.IsNullOrWhiteSpace(playerId) || eliminationOrdinal == 0)
                throw new ArgumentException("An authoritative raid, victim and positive elimination ordinal are required.");
            return "death/" + Uri.EscapeDataString(raidId) + "/" + Uri.EscapeDataString(playerId) + "/"
                + eliminationOrdinal.ToString(CultureInfo.InvariantCulture);
        }
    }
}
