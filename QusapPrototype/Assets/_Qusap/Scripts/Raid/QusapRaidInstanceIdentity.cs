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
            {
                var legacyParts = instanceId.Substring(legacy.Length).Split('/');
                return ulong.TryParse(legacyParts[0], NumberStyles.None, CultureInfo.InvariantCulture, out legacyCounter)
                    && legacyCounter > 0 && ValidParts(legacyParts, 1);
            }
            string prefix = Uri.EscapeDataString(raidId) + "/world/";
            if (!instanceId.StartsWith(prefix, StringComparison.Ordinal)) return false;
            var parts = instanceId.Substring(prefix.Length).Split('/');
            if (parts.Length < 3 || parts[1] != "roll"
                || !int.TryParse(parts[2], NumberStyles.None, CultureInfo.InvariantCulture, out int index) || index < 0
                || parts[2] != index.ToString(CultureInfo.InvariantCulture)) return false;
            string spawn;
            try { spawn = Uri.UnescapeDataString(parts[0]); } catch { return false; }
            if (!ValidSpawnPoint(spawn) || parts[0] != Uri.EscapeDataString(spawn)) return false;
            return ValidParts(parts, 3);
        }
        // Taking units from a previously looted stack may create another deterministic part.
        // Preserve the original allocator counter and every existing identity byte.
        private static bool ValidParts(string[] parts, int start)
        {
            if ((parts.Length - start) % 2 != 0) return false;
            for (int index = start; index < parts.Length; index += 2)
                if (parts[index] != "part" || !ulong.TryParse(parts[index + 1], NumberStyles.None, CultureInfo.InvariantCulture, out ulong split)
                    || split == 0 || parts[index + 1] != split.ToString(CultureInfo.InvariantCulture)) return false;
            return true;
        }
    }
}
