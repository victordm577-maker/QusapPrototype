using System;
using System.Globalization;
using System.Linq;

namespace Qusap
{
    public enum QusapDeploymentStatus { None, Prepared, Running, Resolved }

    // Recovery journal, not a second live owner or inventory. Only persistent selected items enter it.
    [Serializable] public sealed class QusapActiveDeploymentManifest
    {
        public string DeploymentId;
        public string RaidId;
        public QusapDeploymentStatus State;
        public QusapProfileItem[] Items = Array.Empty<QusapProfileItem>();
        public string WeaponInstanceId;
        public string CreatedAtUtc;
        internal bool ValidMetadata() => !string.IsNullOrWhiteSpace(DeploymentId) && !string.IsNullOrWhiteSpace(RaidId)
            && (State == QusapDeploymentStatus.Prepared || State == QusapDeploymentStatus.Running)
            && Items != null && Items.Length > 0 && Items.All(i => i != null)
            && (string.IsNullOrEmpty(WeaponInstanceId) || Items.Count(i => i.InstanceId == WeaponInstanceId && i.NativeWeaponInstanceId != 0) == 1)
            && Items.Count(i => i.NativeWeaponInstanceId != 0) == (string.IsNullOrEmpty(WeaponInstanceId) ? 0 : 1)
            && DateTime.TryParseExact(CreatedAtUtc, "O", CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var utc) && utc.Kind == DateTimeKind.Utc;
    }

    // Exact approved v1 wire layout (with Quantity). The earlier layout without Quantity remains in the codec.
    [Serializable] internal sealed class QusapV1ProfileContent
    {
        public int SchemaVersion = 1;
        public string ProfileId;
        public long Revision;
        public string SavedAtUtc;
        public ulong NextItemInstanceId = 1;
        public ulong NextNativeWeaponInstanceId = 1;
        public QusapProfileItem[] Stash = Array.Empty<QusapProfileItem>();
        public QusapProfileReceipt[] Settlements = Array.Empty<QusapProfileReceipt>();
        public static QusapV1ProfileContent From(QusapProfileContent c) => new() { SchemaVersion = c.SchemaVersion,
            ProfileId = c.ProfileId, Revision = c.Revision, SavedAtUtc = c.SavedAtUtc, NextItemInstanceId = c.NextItemInstanceId,
            NextNativeWeaponInstanceId = c.NextNativeWeaponInstanceId, Stash = c.Stash, Settlements = c.Settlements };
    }
    [Serializable] internal sealed class QusapV1ProfileDocument
    { public QusapV1ProfileContent Content; public string Checksum; }
}
