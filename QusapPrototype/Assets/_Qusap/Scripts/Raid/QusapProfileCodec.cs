using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using UnityEngine;

namespace Qusap
{
    [Serializable] public sealed class QusapProfileItem
    {
        public string InstanceId;
        public string DefinitionId;
        public string RaidId;
        public string Provenance;
        public ulong NativeWeaponInstanceId;
        public string NativeWeaponDefinitionId;

        public static QusapProfileItem From(QusapLootSnapshot item) => new()
        {
            InstanceId = item.LootInstanceId, DefinitionId = item.DefinitionId,
            RaidId = item.RaidId, Provenance = item.Provenance,
            NativeWeaponInstanceId = item.Weapon?.InstanceId ?? 0,
            NativeWeaponDefinitionId = item.Weapon?.Definition.Id ?? string.Empty
        };
    }

    [Serializable] public sealed class QusapProfileReceipt
    { public string SettlementId; public string Fingerprint; }

    [Serializable] public sealed class QusapProfileContent
    {
        public int SchemaVersion = 1;
        public string ProfileId;
        public long Revision;
        public string SavedAtUtc;
        public ulong NextItemInstanceId = 1;
        public ulong NextNativeWeaponInstanceId = 1;
        public QusapProfileItem[] Stash = Array.Empty<QusapProfileItem>();
        public QusapProfileReceipt[] Settlements = Array.Empty<QusapProfileReceipt>();
    }

    [Serializable] public sealed class QusapProfileDocument
    { public QusapProfileContent Content; public string Checksum; }

    // Only primitive DTOs cross the disk boundary. Unity objects are resolved by exact definition ID.
    public sealed class QusapProfileCodec
    {
        private readonly Dictionary<string, QusapLootDefinition> definitions;
        public QusapProfileCodec(IReadOnlyList<QusapLootDefinition> catalog)
        {
            if (catalog == null || catalog.Any(d => d == null || string.IsNullOrWhiteSpace(d.DefinitionId))
                || catalog.Select(d => d.DefinitionId).Distinct(StringComparer.Ordinal).Count() != catalog.Count)
                throw new ArgumentException("Invalid definition catalog.");
            definitions = catalog.ToDictionary(d => d.DefinitionId, StringComparer.Ordinal);
        }

        public static string Sha256(string content)
        {
            using var hash = SHA256.Create();
            return BitConverter.ToString(hash.ComputeHash(Encoding.UTF8.GetBytes(content))).Replace("-", "").ToLowerInvariant();
        }

        public static QusapProfileDocument Seal(QusapProfileContent content)
        {
            content.Stash = content.Stash.OrderBy(i => i.InstanceId, StringComparer.Ordinal).ToArray();
            content.Settlements = content.Settlements.OrderBy(r => r.SettlementId, StringComparer.Ordinal).ToArray();
            return new QusapProfileDocument { Content = content, Checksum = Sha256(JsonUtility.ToJson(content)) };
        }
        public static string Encode(QusapProfileDocument document) => JsonUtility.ToJson(document);

        public bool TryDecode(string json, out QusapProfileDocument document, out string error, out bool future)
        {
            document = null; error = "Invalid JSON"; future = false;
            try
            {
                document = JsonUtility.FromJson<QusapProfileDocument>(json);
                if (document?.Content == null) return false;
                future = document.Content.SchemaVersion > 1;
                if (document.Content.SchemaVersion != 1) { error = "Incompatible SchemaVersion: " + document.Content.SchemaVersion; return false; }
                // Reject omitted/unknown/duplicate fields and noncanonical payloads rather than hashing only a subset.
                if (!string.Equals(json, Encode(document), StringComparison.Ordinal)) { error = "Noncanonical or incomplete JSON"; return false; }
                return Validate(document, out error);
            }
            catch (Exception exception) { document = null; error = "Invalid JSON: " + exception.Message; return false; }
        }

        public bool Validate(QusapProfileDocument document, out string error)
        {
            error = "Invalid profile metadata";
            var c = document?.Content;
            if (c == null || c.SchemaVersion != 1 || string.IsNullOrWhiteSpace(c.ProfileId)
                || c.ProfileId.Contains('\n') || c.Revision < 1 || c.Stash == null || c.Settlements == null
                || !DateTime.TryParseExact(c.SavedAtUtc, "O", CultureInfo.InvariantCulture,
                    DateTimeStyles.RoundtripKind, out var date) || date.Kind != DateTimeKind.Utc
                || c.NextItemInstanceId == 0 || c.NextNativeWeaponInstanceId == 0) return false;
            if (!string.Equals(document.Checksum, Sha256(JsonUtility.ToJson(c)), StringComparison.Ordinal))
            { error = "Checksum mismatch"; return false; }
            var ids = new HashSet<string>(StringComparer.Ordinal);
            var nativeIds = new HashSet<ulong>();
            string previous = null;
            foreach (var item in c.Stash)
            {
                error = "Invalid or duplicate InstanceId";
                if (item == null || string.IsNullOrWhiteSpace(item.InstanceId) || item.InstanceId.Contains('\n')
                    || !ids.Add(item.InstanceId) || string.IsNullOrWhiteSpace(item.RaidId) || item.Provenance == null
                    || !item.InstanceId.StartsWith(item.RaidId + "/loot/", StringComparison.Ordinal)
                    || !ulong.TryParse(item.InstanceId.Substring(item.RaidId.Length + 6), NumberStyles.None,
                        CultureInfo.InvariantCulture, out ulong suffix) || suffix == 0 || suffix >= c.NextItemInstanceId
                    || (previous != null && StringComparer.Ordinal.Compare(previous, item.InstanceId) >= 0)) return false;
                previous = item.InstanceId;
                error = "Unknown DefinitionId: " + item.DefinitionId;
                if (item.DefinitionId == null || !definitions.TryGetValue(item.DefinitionId, out var definition)) return false;
                error = "Invalid native weapon identity";
                if (definition.Category == QusapLootCategory.Weapon)
                {
                    if (item.NativeWeaponInstanceId == 0 || item.NativeWeaponInstanceId >= c.NextNativeWeaponInstanceId
                        || !nativeIds.Add(item.NativeWeaponInstanceId) || definition.WeaponDefinition == null
                        || definition.WeaponDefinition.Id != item.NativeWeaponDefinitionId) return false;
                }
                else if (item.NativeWeaponInstanceId != 0 || item.NativeWeaponDefinitionId != string.Empty) return false;
            }
            var receiptIds = new HashSet<string>(StringComparer.Ordinal);
            var receiptedItems = new HashSet<string>(StringComparer.Ordinal);
            previous = null;
            foreach (var receipt in c.Settlements)
            {
                error = "Invalid settlement receipt";
                if (receipt == null || string.IsNullOrWhiteSpace(receipt.SettlementId) || receipt.Fingerprint == null
                    || !receiptIds.Add(receipt.SettlementId)
                    || (previous != null && StringComparer.Ordinal.Compare(previous, receipt.SettlementId) >= 0)) return false;
                previous = receipt.SettlementId;
                var parts = receipt.Fingerprint.Split('\n');
                if (parts.Length < 2 || parts[0] != c.ProfileId) return false;
                var receiptItems = parts.Skip(1).ToArray();
                if (!receiptItems.SequenceEqual(receiptItems.OrderBy(id => id, StringComparer.Ordinal))) return false;
                foreach (string id in receiptItems) if (!ids.Contains(id) || !receiptedItems.Add(id)) return false;
            }
            if (!ids.SetEquals(receiptedItems)) { error = "Unreceipted stash objects"; return false; }
            error = "Valid"; return true;
        }

        public QusapLootInstance[] Restore(QusapProfileContent content)
        {
            return content.Stash.Select(item =>
            {
                var definition = definitions[item.DefinitionId];
                var weapon = item.NativeWeaponInstanceId == 0 ? null
                    : new QusapWeaponInstance(item.NativeWeaponInstanceId, definition.WeaponDefinition);
                return new QusapLootInstance(item.InstanceId, item.RaidId, definition, item.Provenance, weapon)
                { Location = QusapLootLocation.Stash, HolderId = content.ProfileId, SlotIndex = -1 };
            }).ToArray();
        }
    }
}
