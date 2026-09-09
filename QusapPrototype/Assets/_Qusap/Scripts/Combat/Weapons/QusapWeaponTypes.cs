using System;

namespace Qusap
{
    public enum QusapWeaponOperationResult
    {
        Success,
        InvalidWeapon,
        InvalidOwner,
        SlotOccupied,
        SlotEmpty,
        WeaponAlreadyOwned,
        NotWeaponOwner,
        SelfDisarmRejected,
        InconsistentState
    }

    public enum QusapWeaponTransitionType
    {
        None,
        Equipped,
        Dropped,
        Disarmed
    }

    public readonly struct QusapWeaponTransition
    {
        public QusapWeaponTransition(
            QusapWeaponTransitionType type,
            QusapWeaponInstance weapon,
            ulong? previousOwnerEntityId,
            ulong? newOwnerEntityId,
            ulong? sourceEntityId,
            ulong resultingRevision)
        {
            Type = type;
            Weapon = weapon ?? throw new ArgumentNullException(nameof(weapon));
            PreviousOwnerEntityId = previousOwnerEntityId;
            NewOwnerEntityId = newOwnerEntityId;
            SourceEntityId = sourceEntityId;
            ResultingRevision = resultingRevision;
        }

        public QusapWeaponTransitionType Type { get; }
        public QusapWeaponInstance Weapon { get; }
        public ulong? PreviousOwnerEntityId { get; }
        public ulong? NewOwnerEntityId { get; }
        public ulong? SourceEntityId { get; }
        public ulong ResultingRevision { get; }
    }

    public sealed class QusapWeaponIdGenerator
    {
        private ulong lastIssuedId;

        public QusapWeaponIdGenerator(ulong lastIssuedId = 0)
        {
            this.lastIssuedId = lastIssuedId;
        }

        public ulong Next()
        {
            if (lastIssuedId == ulong.MaxValue)
            {
                throw new OverflowException("No more weapon instance identifiers are available.");
            }

            lastIssuedId++;
            return lastIssuedId;
        }

        public void Reset()
        {
            // Instance identifiers are authoritative and must never be reused.
        }
    }
}
