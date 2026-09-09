using System;

namespace Qusap
{
    public sealed class QusapWeaponEquipmentState
    {
        public QusapWeaponEquipmentState(ulong ownerEntityId)
        {
            if (ownerEntityId == 0)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(ownerEntityId),
                    "An equipment slot requires a non-zero owner identifier.");
            }

            OwnerEntityId = ownerEntityId;
        }

        public ulong OwnerEntityId { get; }
        public bool HasWeapon => EquippedWeapon != null;
        public QusapWeaponInstance EquippedWeapon { get; private set; }
        public ulong Revision { get; private set; }
        public QusapWeaponTransition? LastTransition { get; private set; }

        public QusapWeaponOperationResult TryEquip(
            QusapWeaponInstance weapon,
            out QusapWeaponTransition transition)
        {
            transition = default;
            if (!IsConsistent())
            {
                return QusapWeaponOperationResult.InconsistentState;
            }

            if (weapon == null || weapon.InstanceId == 0)
            {
                return QusapWeaponOperationResult.InvalidWeapon;
            }

            if (HasWeapon)
            {
                return QusapWeaponOperationResult.SlotOccupied;
            }

            if (weapon.OwnerEntityId.HasValue)
            {
                return QusapWeaponOperationResult.WeaponAlreadyOwned;
            }

            ulong nextRevision = GetNextRevision();
            if (!weapon.TryAssignOwner(OwnerEntityId))
            {
                return QusapWeaponOperationResult.InconsistentState;
            }

            EquippedWeapon = weapon;
            Revision = nextRevision;
            transition = new QusapWeaponTransition(
                QusapWeaponTransitionType.Equipped,
                weapon,
                null,
                OwnerEntityId,
                OwnerEntityId,
                Revision);
            LastTransition = transition;
            return QusapWeaponOperationResult.Success;
        }

        public QusapWeaponOperationResult TryDrop(
            out QusapWeaponInstance weapon,
            out QusapWeaponTransition transition)
        {
            weapon = null;
            transition = default;
            if (!HasWeapon)
            {
                return QusapWeaponOperationResult.SlotEmpty;
            }

            QusapWeaponOperationResult consistency = GetOccupiedSlotConsistency();
            if (consistency != QusapWeaponOperationResult.Success)
            {
                return consistency;
            }

            QusapWeaponInstance releasedWeapon = EquippedWeapon;
            ulong nextRevision = GetNextRevision();
            if (!releasedWeapon.TryReleaseOwner(OwnerEntityId))
            {
                return QusapWeaponOperationResult.InconsistentState;
            }

            EquippedWeapon = null;
            Revision = nextRevision;
            weapon = releasedWeapon;
            transition = new QusapWeaponTransition(
                QusapWeaponTransitionType.Dropped,
                releasedWeapon,
                OwnerEntityId,
                null,
                OwnerEntityId,
                Revision);
            LastTransition = transition;
            return QusapWeaponOperationResult.Success;
        }

        public QusapWeaponOperationResult TryDisarm(
            ulong sourceEntityId,
            out QusapWeaponInstance weapon,
            out QusapWeaponTransition transition)
        {
            weapon = null;
            transition = default;
            if (sourceEntityId == 0)
            {
                return QusapWeaponOperationResult.InvalidOwner;
            }

            if (sourceEntityId == OwnerEntityId)
            {
                return QusapWeaponOperationResult.SelfDisarmRejected;
            }

            if (!HasWeapon)
            {
                return QusapWeaponOperationResult.SlotEmpty;
            }

            QusapWeaponOperationResult consistency = GetOccupiedSlotConsistency();
            if (consistency != QusapWeaponOperationResult.Success)
            {
                return consistency;
            }

            QusapWeaponInstance releasedWeapon = EquippedWeapon;
            ulong nextRevision = GetNextRevision();
            if (!releasedWeapon.TryReleaseOwner(OwnerEntityId))
            {
                return QusapWeaponOperationResult.InconsistentState;
            }

            EquippedWeapon = null;
            Revision = nextRevision;
            weapon = releasedWeapon;
            transition = new QusapWeaponTransition(
                QusapWeaponTransitionType.Disarmed,
                releasedWeapon,
                OwnerEntityId,
                null,
                sourceEntityId,
                Revision);
            LastTransition = transition;
            return QusapWeaponOperationResult.Success;
        }

        private bool IsConsistent()
        {
            return EquippedWeapon == null
                || EquippedWeapon.OwnerEntityId == OwnerEntityId;
        }

        private QusapWeaponOperationResult GetOccupiedSlotConsistency()
        {
            if (!EquippedWeapon.OwnerEntityId.HasValue)
            {
                return QusapWeaponOperationResult.InconsistentState;
            }

            return EquippedWeapon.OwnerEntityId.Value == OwnerEntityId
                ? QusapWeaponOperationResult.Success
                : QusapWeaponOperationResult.NotWeaponOwner;
        }

        private ulong GetNextRevision()
        {
            return checked(Revision + 1);
        }
    }
}
