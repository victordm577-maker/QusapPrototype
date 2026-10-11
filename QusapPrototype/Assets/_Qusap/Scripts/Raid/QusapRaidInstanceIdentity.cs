using System;
using System.Globalization;
using System.Linq;

namespace Qusap
{
    // Legacy allocator identities stay byte-identical. World identities use a separate raid/spawn namespace.
    public static class QusapRaidInstanceIdentity
    {
        public static bool ValidSpawnPoint(string value) => !string.IsNullOrWhiteSpace(value)
            && value == value.Trim() && value.Length <= 128 && !value.Any(char.IsControl);
        public static bool TryValidate(string raidId, string instanceId, out ulong legacyCounter)
        {
            legacyCounter = 0;
            if (string.IsNullOrWhiteSpace(raidId) || string.IsNullOrWhiteSpace(instanceId) || instanceId.Contains('\n')) return false;
            string legacy = raidId + "/loot/";
            if (instanceId.StartsWith(legacy, StringComparison.Ordinal))
                return ulong.TryParse(instanceId.Substring(legacy.Length), NumberStyles.None, CultureInfo.InvariantCulture, out legacyCounter) && legacyCounter > 0;
            string prefix = Uri.EscapeDataString(raidId) + "/world/";
            if (!instanceId.StartsWith(prefix, StringComparison.Ordinal)) return false;
            var parts = instanceId.Substring(prefix.Length).Split('/');
            if ((parts.Length != 3 && parts.Length != 5) || parts[1] != "roll"
                || !int.TryParse(parts[2], NumberStyles.None, CultureInfo.InvariantCulture, out int index) || index < 0
                || parts[2] != index.ToString(CultureInfo.InvariantCulture)) return false;
            string spawn;
            try { spawn = Uri.UnescapeDataString(parts[0]); } catch { return false; }
            if (!ValidSpawnPoint(spawn) || parts[0] != Uri.EscapeDataString(spawn)) return false;
            if (parts.Length == 5 && (parts[3] != "part" || !ulong.TryParse(parts[4], NumberStyles.None, CultureInfo.InvariantCulture, out ulong split)
                || split == 0 || parts[4] != split.ToString(CultureInfo.InvariantCulture))) return false;
            return true;
        }
    }
}
