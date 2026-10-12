using System;
using UnityEngine;

namespace Qusap
{
    public enum QusapDeathLootPlacementKind { EliminationPosition, GroundProjection, LastSafePosition, Unavailable }
    public readonly struct QusapDeathLootPlacement
    {
        public QusapDeathLootPlacement(Vector3 source, Vector3 position, QusapDeathLootPlacementKind kind, bool projected)
        { EliminationPosition = source; Position = position; Kind = kind; ProjectedToGround = projected; }
        public Vector3 EliminationPosition { get; }
        public Vector3 Position { get; }
        public QusapDeathLootPlacementKind Kind { get; }
        public bool ProjectedToGround { get; }
    }
    // Geometry queries only. Never writes the player's position, Rigidbody, Collider or physics settings.
    public static class QusapDeathLootPlacementResolver
    {
        private static bool Finite(Vector3 point) => float.IsFinite(point.x) && float.IsFinite(point.y) && float.IsFinite(point.z);
        private static bool Geometry(Collider collider) => collider != null && !collider.isTrigger
            && collider.GetComponentInParent<QusapHitReceiver>() == null
            && collider.GetComponentInParent<QusapDeathLootContainer>() == null;
        private static bool Clear(Vector3 center, float radius, int mask)
        {
            foreach (var collider in Physics.OverlapSphere(center, radius, mask, QueryTriggerInteraction.Ignore))
                if (Geometry(collider)) return false;
            return true;
        }
        private static bool Project(Vector3 source, int groundMask, int solidMask, float radius, float planeZ, out Vector3 result)
        {
            result = default;
            if (!Finite(source) || !Clear(source, radius, solidMask)) return false;
            source.z = planeZ;
            float closest = float.PositiveInfinity;
            foreach (var hit in Physics.RaycastAll(source + Vector3.up * 2f, Vector3.down, 22f, groundMask, QueryTriggerInteraction.Ignore))
            {
                if (!Geometry(hit.collider) || hit.normal.y < .6f || hit.point.y > source.y + .05f || hit.distance >= closest) continue;
                var point = hit.point + Vector3.up * (radius + .02f); point.z = planeZ;
                if (!Clear(point, radius, solidMask)) continue;
                closest = hit.distance; result = point;
            }
            return float.IsFinite(closest);
        }
        public static bool TryResolve(Vector3 finalPosition, Vector3? lastSafePosition, int groundMask, int solidMask,
            float radius, float planeZ, out QusapDeathLootPlacement placement)
        {
            if (!float.IsFinite(radius) || radius <= 0 || !float.IsFinite(planeZ)) throw new ArgumentOutOfRangeException(nameof(radius));
            if (Project(finalPosition, groundMask, solidMask, radius, planeZ, out var projected))
            {
                bool moved = (projected - finalPosition).sqrMagnitude > .000001f;
                placement = new(finalPosition, projected, moved ? QusapDeathLootPlacementKind.GroundProjection : QusapDeathLootPlacementKind.EliminationPosition, moved);
                return true;
            }
            if (lastSafePosition.HasValue && Project(lastSafePosition.Value, groundMask, solidMask, radius, planeZ, out projected))
            {
                placement = new(finalPosition, projected, QusapDeathLootPlacementKind.LastSafePosition, true); return true;
            }
            placement = new(finalPosition, default, QusapDeathLootPlacementKind.Unavailable, false); return false;
        }
    }
}
