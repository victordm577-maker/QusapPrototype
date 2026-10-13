using System;

namespace Qusap
{
    // Exact schema 2 layout keeps old checksums and canonical bytes authoritative on load.
    [Serializable] internal sealed class QusapV2ProfileContent
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
        public static QusapV2ProfileContent From(QusapProfileContent c) => new()
        {
            SchemaVersion = c.SchemaVersion, ProfileId = c.ProfileId, Revision = c.Revision, SavedAtUtc = c.SavedAtUtc,
            NextItemInstanceId = c.NextItemInstanceId, NextNativeWeaponInstanceId = c.NextNativeWeaponInstanceId,
            Stash = c.Stash, Settlements = c.Settlements, ActiveDeploymentManifest = c.ActiveDeploymentManifest,
            RecoveryReason = c.RecoveryReason
        };
    }
    [Serializable] internal sealed class QusapV2ProfileDocument
    { public QusapV2ProfileContent Content; public string Checksum; }
}
