using System;
using System.Collections.Generic;

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
        Disarmed,
        VoluntarySwapThrow
    }

    public enum QusapWeaponReleaseType
    {
        Disarmed,
        VoluntarySwapThrow,
        InitialSpawn
    }

    public enum QusapWeaponThrowDirection
    {
        Forward,
        Up
    }

    public readonly struct QusapWeaponSwapThrowPress
    {
        public QusapWeaponSwapThrowPress(
            ulong pressId,
            double timestamp,
            QusapWeaponThrowDirection direction,
            int facingDirection)
        {
            if (pressId == 0)
            {
                throw new ArgumentOutOfRangeException(nameof(pressId));
            }

            if (double.IsNaN(timestamp) || double.IsInfinity(timestamp))
            {
                throw new ArgumentOutOfRangeException(nameof(timestamp));
            }

            if (direction != QusapWeaponThrowDirection.Forward
                && direction != QusapWeaponThrowDirection.Up)
            {
                throw new ArgumentOutOfRangeException(nameof(direction));
            }

            PressId = pressId;
            Timestamp = timestamp;
            Direction = direction;
            FacingDirection = facingDirection < 0 ? -1 : 1;
        }

        public ulong PressId { get; }
        public double Timestamp { get; }
        public QusapWeaponThrowDirection Direction { get; }
        public int FacingDirection { get; }
    }

    public sealed class QusapWeaponSwapThrowInputBuffer
    {
        public const float UpThreshold = 0.5f;

        private ulong lastPressId;
        private bool held;
        private readonly Queue<QusapWeaponSwapThrowPress> pendingPresses = new();

        public bool IsHeld => held;
        public bool HasPendingPress => pendingPresses.Count > 0;
        public int PendingCount => pendingPresses.Count;

        public bool SetButtonState(
            bool pressed,
            float capturedVertical,
            int capturedFacingDirection,
            double timestamp,
            out QusapWeaponSwapThrowPress createdPress)
        {
            createdPress = default;
            if (!pressed)
            {
                held = false;
                return false;
            }

            if (held
                || !float.IsFinite(capturedVertical)
                || double.IsNaN(timestamp)
                || double.IsInfinity(timestamp)
                || lastPressId == ulong.MaxValue)
            {
                return false;
            }

            held = true;
            createdPress = new QusapWeaponSwapThrowPress(
                ++lastPressId,
                timestamp,
                capturedVertical >= UpThreshold
                    ? QusapWeaponThrowDirection.Up
                    : QusapWeaponThrowDirection.Forward,
                capturedFacingDirection);
            pendingPresses.Enqueue(createdPress);
            return true;
        }

        public bool TryConsume(out QusapWeaponSwapThrowPress press)
        {
            if (pendingPresses.Count == 0)
            {
                press = default;
                return false;
            }

            press = pendingPresses.Dequeue();
            return true;
        }

        public void Clear()
        {
            held = false;
            pendingPresses.Clear();
        }
    }

    public readonly struct QusapWeaponThrowTrajectoryProfile
    {
        public const float MaximumDistance = 20f;
        public const float MaximumHeight = 20f;
        public const float MaximumDuration = 5f;

        public QusapWeaponThrowTrajectoryProfile(
            float horizontalDistance,
            float arcHeight,
            float duration,
            float defaultHorizontalDistance,
            float defaultArcHeight,
            float defaultDuration)
        {
            HorizontalDistance = Normalize(
                horizontalDistance,
                defaultHorizontalDistance,
                MaximumDistance);
            ArcHeight = Normalize(arcHeight, defaultArcHeight, MaximumHeight);
            Duration = Normalize(duration, defaultDuration, MaximumDuration);
        }

        public float HorizontalDistance { get; }
        public float ArcHeight { get; }
        public float Duration { get; }

        private static float Normalize(float value, float fallback, float maximum)
        {
            return !float.IsFinite(value) || value <= 0f
                ? fallback
                : Math.Min(value, maximum);
        }
    }

    public readonly struct QusapWeaponSwapTransition
    {
        public QusapWeaponSwapTransition(
            QusapWeaponInstance releasedWeapon,
            QusapWeaponInstance equippedWeapon,
            QusapWeaponTransition releaseTransition,
            QusapWeaponTransition equipTransition)
        {
            ReleasedWeapon = releasedWeapon
                ?? throw new ArgumentNullException(nameof(releasedWeapon));
            EquippedWeapon = equippedWeapon
                ?? throw new ArgumentNullException(nameof(equippedWeapon));
            ReleaseTransition = releaseTransition;
            EquipTransition = equipTransition;
        }

        public QusapWeaponInstance ReleasedWeapon { get; }
        public QusapWeaponInstance EquippedWeapon { get; }
        public QusapWeaponTransition ReleaseTransition { get; }
        public QusapWeaponTransition EquipTransition { get; }
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
