using System;

namespace Qusap
{
    public sealed class QusapWeaponInstance
    {
        public QusapWeaponInstance(ulong instanceId, QusapWeaponDefinition definition)
        {
            if (instanceId == 0)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(instanceId),
                    "A weapon instance identifier must be non-zero.");
            }

            InstanceId = instanceId;
            Definition = definition ?? throw new ArgumentNullException(nameof(definition));
        }

        public ulong InstanceId { get; }
        public QusapWeaponDefinition Definition { get; }
        public ulong? OwnerEntityId { get; private set; }
        public bool IsEquipped => OwnerEntityId.HasValue;
        public bool IsFree => !OwnerEntityId.HasValue;
        public bool IsRetired { get; private set; }
        internal void RetireFromRaid() { if (IsEquipped) throw new InvalidOperationException("Release before retiring."); IsRetired = true; }

        internal bool TryAssignOwner(ulong ownerEntityId)
        {
            if (IsRetired || ownerEntityId == 0 || OwnerEntityId.HasValue)
            {
                return false;
            }

            OwnerEntityId = ownerEntityId;
            return true;
        }

        internal bool TryReleaseOwner(ulong ownerEntityId)
        {
            if (!OwnerEntityId.HasValue || OwnerEntityId.Value != ownerEntityId)
            {
                return false;
            }

            OwnerEntityId = null;
            return true;
        }
    }
}
