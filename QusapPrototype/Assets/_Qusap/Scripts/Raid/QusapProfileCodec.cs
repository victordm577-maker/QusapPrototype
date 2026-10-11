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
        public int Quantity = 1;

        public static QusapProfileItem From(QusapLootSnapshot item) => item.RaidLoaner
            ? throw new InvalidOperationException("RaidLoaner cannot cross the persistence boundary.") : new()
        {
            InstanceId = item.LootInstanceId, DefinitionId = item.DefinitionId,
            RaidId = item.RaidId, Provenance = item.Provenance,
            NativeWeaponInstanceId = item.Weapon?.InstanceId ?? 0,
            NativeWeaponDefinitionId = item.Weapon?.Definition.Id ?? string.Empty, Quantity = item.Quantity
        };
    }

    [Serializable] public sealed class QusapProfileReceipt
    { public string SettlementId; public string Fingerprint; }

    [Serializable] public sealed class QusapProfileContent
    {
        public int SchemaVersion = 2;
        public string ProfileId;
        public long Revision;
        public string SavedAtUtc;
        public ulong NextItemInstanceId = 1;
        public ulong NextNativeWeaponInstanceId = 1;
        public QusapProfileItem[] Stash = Array.Empty<QusapProfileItem>();
        public QusapProfileReceipt[] Settlements = Array.Empty<QusapProfileReceipt>();
        public QusapActiveDeploymentManifest ActiveDeploymentManifest;
        public string RecoveryReason = string.Empty;
    }

    [Serializable] public sealed class QusapProfileDocument
    {
        public QusapProfileContent Content; public string Checksum;
        [NonSerialized] internal bool LegacyWithoutQuantity;
    }

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
            if (content.ActiveDeploymentManifest != null) content.ActiveDeploymentManifest.Items = content.ActiveDeploymentManifest.Items.OrderBy(i => i.InstanceId, StringComparer.Ordinal).ToArray();
            return new QusapProfileDocument { Content = content, Checksum = Sha256(WireContent(content)) };
        }
        private static string WireContent(QusapProfileContent c) => c.SchemaVersion == 1
            ? JsonUtility.ToJson(QusapV1ProfileContent.From(c)) : NullableManifest(JsonUtility.ToJson(c), c);
        // Unity serializes a null inline class as an empty object. Preserve an actual nullable journal on disk.
        private static string NullableManifest(string json, QusapProfileContent c) => c.ActiveDeploymentManifest != null ? json
            : json.Replace("\"ActiveDeploymentManifest\":" + JsonUtility.ToJson(new QusapActiveDeploymentManifest()), "\"ActiveDeploymentManifest\":null");
        public static string Encode(QusapProfileDocument document) => document.LegacyWithoutQuantity
            ? JsonUtility.ToJson(QusapLegacyProfileDocument.From(document)) : document.Content.SchemaVersion == 1
            ? JsonUtility.ToJson(new QusapV1ProfileDocument { Content = QusapV1ProfileContent.From(document.Content), Checksum = document.Checksum })
            : NullableManifest(JsonUtility.ToJson(document), document.Content);

        public bool TryDecode(string json, out QusapProfileDocument document, out string error, out bool future)
        {
            document = null; error = "Invalid JSON"; future = false;
            try
            {
                document = JsonUtility.FromJson<QusapProfileDocument>(json);
                if (document?.Content == null) return false;
                if (document.Content.SchemaVersion == 1 || json.Contains("\"ActiveDeploymentManifest\":null")) document.Content.ActiveDeploymentManifest = null;
                future = document.Content.SchemaVersion > 2;
                if (document.Content.SchemaVersion != 1 && document.Content.SchemaVersion != 2) { error = "Incompatible SchemaVersion: " + document.Content.SchemaVersion; return false; }
                // Reject omitted/unknown/duplicate fields and noncanonical payloads rather than hashing only a subset.
                if (!string.Equals(json, Encode(document), StringComparison.Ordinal))
                {
                    // Exact approved v1 wire format: its original Content bytes remain checksum-authoritative.
                    if (document.Content.SchemaVersion != 1) { error = "Noncanonical or incomplete JSON"; return false; }
                    var legacy = JsonUtility.FromJson<QusapLegacyProfileDocument>(json);
                    if (legacy?.Content == null || !string.Equals(json, JsonUtility.ToJson(legacy), StringComparison.Ordinal))
                    { error = "Noncanonical or incomplete JSON"; return false; }
                    document = legacy.RestoreQuantityOne();
                }
                return Validate(document, out error);
            }
            catch (Exception exception) { document = null; error = "Invalid JSON: " + exception.Message; return false; }
        }

        public bool Validate(QusapProfileDocument document, out string error)
        {
            error = "Invalid profile metadata";
            var c = document?.Content;
            if (c == null || (c.SchemaVersion != 1 && c.SchemaVersion != 2) || string.IsNullOrWhiteSpace(c.ProfileId)
                || c.ProfileId.Contains('\n') || c.Revision < 1 || c.Stash == null || c.Settlements == null
                || !DateTime.TryParseExact(c.SavedAtUtc, "O", CultureInfo.InvariantCulture,
                    DateTimeStyles.RoundtripKind, out var date) || date.Kind != DateTimeKind.Utc
                || c.NextItemInstanceId == 0 || c.NextNativeWeaponInstanceId == 0) return false;
            string checksumContent = document.LegacyWithoutQuantity
                ? JsonUtility.ToJson(QusapLegacyProfileContent.From(c)) : WireContent(c);
            if (!string.Equals(document.Checksum, Sha256(checksumContent), StringComparison.Ordinal))
            { error = "Checksum mismatch"; return false; }
            var ids = new HashSet<string>(StringComparer.Ordinal);
            var stashIds = new HashSet<string>(c.Stash.Select(i => i?.InstanceId), StringComparer.Ordinal);
            var manifest = c.ActiveDeploymentManifest;
            if (c.SchemaVersion == 1 && manifest != null) { error = "Manifest requires schema 2"; return false; }
            if (manifest != null && !manifest.ValidMetadata()) { error = "Invalid deployment manifest"; return false; }
            foreach (var list in new[] { c.Stash, manifest?.Items ?? Array.Empty<QusapProfileItem>() })
                if (!list.Select(i => i?.InstanceId).SequenceEqual(list.Select(i => i?.InstanceId).OrderBy(id => id, StringComparer.Ordinal)))
                { error = "Noncanonical item order"; return false; }
            var owned = c.Stash.Concat(manifest?.Items ?? Array.Empty<QusapProfileItem>()).OrderBy(i => i?.InstanceId, StringComparer.Ordinal);
            var nativeIds = new HashSet<ulong>();
            string previous = null;
            foreach (var item in owned)
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
                error = "Invalid Quantity or stash rule";
                if (item.Provenance == "RaidLoaner" || !definition.CanPersistInStash || item.Quantity < 1 || item.Quantity > definition.MaxStack
                    || (!definition.IsStackable && item.Quantity != 1) || (document.LegacyWithoutQuantity && item.Quantity != 1)) return false;
                error = "Invalid native weapon identity";
                if (definition.Category == QusapLootCategory.Weapon)
                {
                    if (item.NativeWeaponInstanceId == 0 || item.NativeWeaponInstanceId >= c.NextNativeWeaponInstanceId
                        || !nativeIds.Add(item.NativeWeaponInstanceId) || definition.WeaponDefinition == null
                        || definition.WeaponDefinition.Id != item.NativeWeaponDefinitionId) return false;
                }
                else if (item.NativeWeaponInstanceId != 0 || item.NativeWeaponDefinitionId != string.Empty) return false;
                if (manifest != null && manifest.Items.Contains(item) && definition.Category != QusapLootCategory.Weapon
                    && definition.Category != QusapLootCategory.Consumable) { error = "Invalid manifest category"; return false; }
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
                foreach (string id in receiptItems) if (!stashIds.Contains(id) || !receiptedItems.Add(id)) return false;
            }
            if (!stashIds.SetEquals(receiptedItems)) { error = "Unreceipted stash objects"; return false; }
            error = "Valid"; return true;
        }

        public QusapLootInstance[] Restore(QusapProfileContent content)
            => RestoreItems(content.Stash, content.ProfileId);
        internal QusapLootInstance[] RestoreItems(QusapProfileItem[] items, string profileId)
        {
            return items.Select(item =>
            {
                var definition = definitions[item.DefinitionId];
                var weapon = item.NativeWeaponInstanceId == 0 ? null
                    : new QusapWeaponInstance(item.NativeWeaponInstanceId, definition.WeaponDefinition);
                return new QusapLootInstance(item.InstanceId, item.RaidId, definition, item.Provenance, weapon, item.Quantity)
                { Location = QusapLootLocation.Stash, HolderId = profileId, SlotIndex = -1 };
            }).ToArray();
        }
    }

    // Compatibility DTOs describe the already approved v1 shape, not a second stash or profile authority.
    [Serializable] internal sealed class QusapLegacyProfileItem
    {
        public string InstanceId; public string DefinitionId; public string RaidId; public string Provenance;
        public ulong NativeWeaponInstanceId; public string NativeWeaponDefinitionId;
        public static QusapLegacyProfileItem From(QusapProfileItem item) => new() {
            InstanceId = item.InstanceId, DefinitionId = item.DefinitionId, RaidId = item.RaidId, Provenance = item.Provenance,
            NativeWeaponInstanceId = item.NativeWeaponInstanceId, NativeWeaponDefinitionId = item.NativeWeaponDefinitionId };
        public QusapProfileItem Restore() => new() { InstanceId = InstanceId, DefinitionId = DefinitionId, RaidId = RaidId,
            Provenance = Provenance, NativeWeaponInstanceId = NativeWeaponInstanceId, NativeWeaponDefinitionId = NativeWeaponDefinitionId, Quantity = 1 };
    }
    [Serializable] internal sealed class QusapLegacyProfileContent
    {
        public int SchemaVersion = 1; public string ProfileId; public long Revision; public string SavedAtUtc;
        public ulong NextItemInstanceId = 1; public ulong NextNativeWeaponInstanceId = 1;
        public QusapLegacyProfileItem[] Stash = Array.Empty<QusapLegacyProfileItem>();
        public QusapProfileReceipt[] Settlements = Array.Empty<QusapProfileReceipt>();
        public static QusapLegacyProfileContent From(QusapProfileContent c) => new() {
            SchemaVersion = c.SchemaVersion, ProfileId = c.ProfileId, Revision = c.Revision, SavedAtUtc = c.SavedAtUtc,
            NextItemInstanceId = c.NextItemInstanceId, NextNativeWeaponInstanceId = c.NextNativeWeaponInstanceId,
            Stash = c.Stash.Select(QusapLegacyProfileItem.From).ToArray(), Settlements = c.Settlements };
        public QusapProfileContent Restore() => new() { SchemaVersion = SchemaVersion, ProfileId = ProfileId,
            Revision = Revision, SavedAtUtc = SavedAtUtc, NextItemInstanceId = NextItemInstanceId,
            NextNativeWeaponInstanceId = NextNativeWeaponInstanceId, Stash = Stash?.Select(i => i?.Restore()).ToArray(), Settlements = Settlements };
    }
    [Serializable] internal sealed class QusapLegacyProfileDocument
    {
        public QusapLegacyProfileContent Content; public string Checksum;
        public static QusapLegacyProfileDocument From(QusapProfileDocument d) => new() { Content = QusapLegacyProfileContent.From(d.Content), Checksum = d.Checksum };
        public QusapProfileDocument RestoreQuantityOne() => new() { Content = Content.Restore(), Checksum = Checksum, LegacyWithoutQuantity = true };
    }
}
