using System;
using System.Collections.Generic;
using UnityEngine;

namespace Qusap
{
    public readonly struct QusapWeaponPickupPlayerSnapshot
    {
        public QusapWeaponPickupPlayerSnapshot(
            ulong entityId,
            bool isActiveAndRegistered,
            bool hasWeapon,
            Vector3 position)
        {
            EntityId = entityId;
            IsActiveAndRegistered = isActiveAndRegistered;
            HasWeapon = hasWeapon;
            Position = position;
        }

        public ulong EntityId { get; }
        public bool IsActiveAndRegistered { get; }
        public bool HasWeapon { get; }
        public Vector3 Position { get; }
    }

    public readonly struct QusapDroppedWeaponSnapshot
    {
        public QusapDroppedWeaponSnapshot(
            ulong instanceId,
            bool isFree,
            bool isSettled,
            bool isClaimed,
            ulong? previousOwnerEntityId,
            double droppedAt,
            Vector3 position)
        {
            InstanceId = instanceId;
            IsFree = isFree;
            IsSettled = isSettled;
            IsClaimed = isClaimed;
            PreviousOwnerEntityId = previousOwnerEntityId;
            DroppedAt = droppedAt;
            Position = position;
        }

        public ulong InstanceId { get; }
        public bool IsFree { get; }
        public bool IsSettled { get; }
        public bool IsClaimed { get; }
        public ulong? PreviousOwnerEntityId { get; }
        public double DroppedAt { get; }
        public Vector3 Position { get; }
    }

    public readonly struct QusapWeaponPickupCandidate
    {
        public QusapWeaponPickupCandidate(
            ulong playerEntityId,
            ulong weaponInstanceId,
            float squaredDistance)
        {
            PlayerEntityId = playerEntityId;
            WeaponInstanceId = weaponInstanceId;
            SquaredDistance = squaredDistance;
        }

        public ulong PlayerEntityId { get; }
        public ulong WeaponInstanceId { get; }
        public float SquaredDistance { get; }
    }

    /// <summary>
    /// Pure deterministic policy for automatic weapon pickup. Unity supplies
    /// snapshots and a timestamp, while this type owns validation and ordering.
    /// </summary>
    public sealed class QusapWeaponPickupResolver
    {
        public const float DefaultPickupRadius = 0.85f;
        public const double DefaultPreviousOwnerLockout = 1d;
        public const float MaximumPickupRadius = 10f;
        public const double MaximumPreviousOwnerLockout = 60d;

        private bool hasAcceptedTimestamp;
        private double lastAcceptedTimestamp;

        public QusapWeaponPickupResolver(
            float pickupRadius = DefaultPickupRadius,
            double previousOwnerLockout = DefaultPreviousOwnerLockout)
        {
            PickupRadius = NormalizePickupRadius(pickupRadius);
            PreviousOwnerLockout = NormalizePreviousOwnerLockout(
                previousOwnerLockout);
        }

        public float PickupRadius { get; }
        public double PreviousOwnerLockout { get; }
        public double LastAcceptedTimestamp => lastAcceptedTimestamp;
        public bool HasAcceptedTimestamp => hasAcceptedTimestamp;

        public bool TryCreateCandidate(
            QusapWeaponPickupPlayerSnapshot player,
            QusapDroppedWeaponSnapshot dropped,
            double currentTimestamp,
            out QusapWeaponPickupCandidate candidate)
        {
            candidate = default;
            if (!TryAcceptTimestamp(currentTimestamp)
                || player.EntityId == 0
                || !player.IsActiveAndRegistered
                || player.HasWeapon
                || !IsFinite(player.Position)
                || dropped.InstanceId == 0
                || !dropped.IsFree
                || !dropped.IsSettled
                || dropped.IsClaimed
                || !IsFinite(dropped.Position)
                || !IsFinite(dropped.DroppedAt)
                || currentTimestamp < dropped.DroppedAt)
            {
                return false;
            }

            if (dropped.PreviousOwnerEntityId == player.EntityId
                && currentTimestamp < dropped.DroppedAt + PreviousOwnerLockout)
            {
                return false;
            }

            float squaredDistance = (player.Position - dropped.Position).sqrMagnitude;
            float squaredRadius = PickupRadius * PickupRadius;
            if (!float.IsFinite(squaredDistance) || squaredDistance > squaredRadius)
            {
                return false;
            }

            candidate = new QusapWeaponPickupCandidate(
                player.EntityId,
                dropped.InstanceId,
                squaredDistance);
            return true;
        }

        public bool TrySelectBest(
            IReadOnlyList<QusapWeaponPickupPlayerSnapshot> players,
            IReadOnlyList<QusapDroppedWeaponSnapshot> droppedWeapons,
            double currentTimestamp,
            out QusapWeaponPickupCandidate selected)
        {
            selected = default;
            if (players == null
                || droppedWeapons == null
                || !TryAcceptTimestamp(currentTimestamp))
            {
                return false;
            }

            bool hasSelected = false;
            for (int playerIndex = 0; playerIndex < players.Count; playerIndex++)
            {
                for (int weaponIndex = 0;
                    weaponIndex < droppedWeapons.Count;
                    weaponIndex++)
                {
                    if (!TryCreateCandidate(
                            players[playerIndex],
                            droppedWeapons[weaponIndex],
                            currentTimestamp,
                            out QusapWeaponPickupCandidate candidate)
                        || (hasSelected && !IsPreferred(candidate, selected)))
                    {
                        continue;
                    }

                    selected = candidate;
                    hasSelected = true;
                }
            }

            return hasSelected;
        }

        public bool TryAcceptTimestamp(double currentTimestamp)
        {
            if (!IsFinite(currentTimestamp)
                || (hasAcceptedTimestamp && currentTimestamp < lastAcceptedTimestamp))
            {
                return false;
            }

            hasAcceptedTimestamp = true;
            lastAcceptedTimestamp = currentTimestamp;
            return true;
        }

        public void ResetTime()
        {
            hasAcceptedTimestamp = false;
            lastAcceptedTimestamp = 0d;
        }

        public static bool IsPreferred(
            QusapWeaponPickupCandidate candidate,
            QusapWeaponPickupCandidate current)
        {
            int distanceComparison = candidate.SquaredDistance.CompareTo(
                current.SquaredDistance);
            if (distanceComparison != 0)
            {
                return distanceComparison < 0;
            }

            int playerComparison = candidate.PlayerEntityId.CompareTo(
                current.PlayerEntityId);
            return playerComparison != 0
                ? playerComparison < 0
                : candidate.WeaponInstanceId < current.WeaponInstanceId;
        }

        public static float NormalizePickupRadius(float value)
        {
            if (!float.IsFinite(value) || value < 0f)
            {
                return DefaultPickupRadius;
            }

            return Mathf.Min(value, MaximumPickupRadius);
        }

        public static double NormalizePreviousOwnerLockout(double value)
        {
            if (!IsFinite(value) || value < 0d)
            {
                return DefaultPreviousOwnerLockout;
            }

            return Math.Min(value, MaximumPreviousOwnerLockout);
        }

        private static bool IsFinite(double value)
        {
            return !double.IsNaN(value) && !double.IsInfinity(value);
        }

        private static bool IsFinite(Vector3 value)
        {
            return float.IsFinite(value.x)
                && float.IsFinite(value.y)
                && float.IsFinite(value.z);
        }
    }
}
