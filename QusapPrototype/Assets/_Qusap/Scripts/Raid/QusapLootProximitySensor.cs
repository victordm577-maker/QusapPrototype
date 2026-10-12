using System;
using System.Collections.Generic;
using UnityEngine;

namespace Qusap
{
    public readonly struct QusapLootCandidate
    {
        public QusapLootCandidate(QusapWorldLootContainerView view, float distance, bool visible)
        { View = view; Distance = distance; Visible = visible; }
        public QusapWorldLootContainerView View { get; }
        public float Distance { get; }
        public bool Visible { get; }
    }

    public static class QusapLootTargetSelector
    {
        public static QusapWorldLootContainerView Select(IReadOnlyList<QusapLootCandidate> candidates,
            QusapWorldLootContainerView previous, float range, float hysteresis)
        {
            QusapWorldLootContainerView best = null; float distance = float.PositiveInfinity;
            foreach (var candidate in candidates)
            {
                if (candidate.View == null || !candidate.View.isActiveAndEnabled || !candidate.Visible) continue;
                float limit = candidate.View == previous ? range + hysteresis : range;
                if (candidate.Distance > limit) continue;
                if (candidate.Distance < distance || (candidate.Distance == distance &&
                    string.CompareOrdinal(candidate.View.SpawnPointId, best?.SpawnPointId) < 0))
                { best = candidate.View; distance = candidate.Distance; }
            }
            return best;
        }
    }

    [DisallowMultipleComponent]
    public sealed class QusapLootProximitySensor : MonoBehaviour
    {
        [SerializeField, Min(.01f)] private float range = 2.75f;
        [SerializeField, Min(0)] private float hysteresis = .2f;
        [SerializeField] private LayerMask solidGeometryMask = 1;
        private Rigidbody body;
        private Collider[] playerColliders;
        private IReadOnlyList<QusapWorldLootContainerView> views = Array.Empty<QusapWorldLootContainerView>();
        private readonly List<QusapLootCandidate> candidates = new();
        public float Range => range;
        public float Hysteresis => hysteresis;
        public LayerMask SolidGeometryMask => solidGeometryMask;
        public Vector3 Position => body != null ? body.position : transform.position;
        public QusapWorldLootContainerView Target { get; private set; }
        public QusapWorldLootContainerView NearestDiagnostic { get; private set; }
        public float Distance => DistanceTo(Target != null ? Target : NearestDiagnostic);
        public bool LineOfSight => Visible(Target != null ? Target : NearestDiagnostic);
        private void Awake() { body = GetComponent<Rigidbody>(); playerColliders = GetComponentsInChildren<Collider>(); }
        public void Configure(IReadOnlyList<QusapWorldLootContainerView> containers, float radius = 2.75f, float margin = .2f, int mask = 1)
        { views = containers ?? throw new ArgumentNullException(nameof(containers)); range = Mathf.Max(.01f, radius); hysteresis = Mathf.Max(0, margin); solidGeometryMask = mask; Target = null; }
        public float DistanceTo(QusapWorldLootContainerView view) => view != null ? Vector3.Distance(Position, view.transform.position) : float.PositiveInfinity;
        public bool Visible(QusapWorldLootContainerView view)
        {
            if (view == null || !view.isActiveAndEnabled) return false;
            var start = Position; var end = view.transform.position;
            var direction = end - start; float distance = direction.magnitude;
            if (distance < .0001f) return true;
            // Detect an enclosing wall too; raycasts alone omit a collider containing their origin.
            foreach (var collider in Physics.OverlapSphere(start, .01f, solidGeometryMask, QueryTriggerInteraction.Ignore))
                if (Blocks(collider, view)) return false;
            foreach (var hit in Physics.RaycastAll(start, direction / distance, distance, solidGeometryMask, QueryTriggerInteraction.Ignore))
                if (Blocks(hit.collider, view)) return false;
            return true;
        }
        private bool Blocks(Collider collider, QusapWorldLootContainerView view)
        {
            if (collider == null || collider.isTrigger || collider.transform.IsChildOf(transform)
                || collider.transform.IsChildOf(view.transform) || collider.GetComponentInParent<QusapHitReceiver>() != null) return false;
            var platform = collider.GetComponentInChildren<QusapOneWayPlatform>(true) ?? collider.GetComponentInParent<QusapOneWayPlatform>();
            if (platform == null) return true;
            // Match current physical pass-through configuration, including per-player IgnoreCollision.
            if (!platform.isActiveAndEnabled || platform.SolidCollider != collider) return false;
            foreach (var own in playerColliders) if (own != null && !own.isTrigger
                && !Physics.GetIgnoreLayerCollision(own.gameObject.layer, collider.gameObject.layer) && !Physics.GetIgnoreCollision(own, collider)) return true;
            return false;
        }
        public QusapLootInteractionRejection Validate(QusapWorldLootContainerView view)
        {
            if (view == null || !view.isActiveAndEnabled || view.Snapshot == null) return QusapLootInteractionRejection.InvalidTarget;
            if (DistanceTo(view) > range) return QusapLootInteractionRejection.OutOfRange;
            return Visible(view) ? QusapLootInteractionRejection.None : QusapLootInteractionRejection.NoLineOfSight;
        }
        public void Scan()
        {
            candidates.Clear(); NearestDiagnostic = null; float nearest = float.PositiveInfinity;
            foreach (var view in views)
            {
                if (view == null || !view.isActiveAndEnabled || view.Snapshot == null) continue;
                float distance = DistanceTo(view);
                if (distance < nearest || (distance == nearest && string.CompareOrdinal(view.SpawnPointId, NearestDiagnostic?.SpawnPointId) < 0))
                { nearest = distance; NearestDiagnostic = view; }
                if (distance <= range + hysteresis) candidates.Add(new(view, distance, Visible(view)));
            }
            Target = QusapLootTargetSelector.Select(candidates, Target, range, hysteresis);
        }
    }
}
