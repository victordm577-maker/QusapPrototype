using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace Qusap
{
    public sealed class QusapDeathLootProjection
    {
        internal QusapDeathLootProjection(QusapDeathLootRecord record, QusapWorldLootContainerView view, QusapDeathLootPlacement placement)
        { Record = record; View = view; Placement = placement; }
        public QusapDeathLootRecord Record { get; }
        public QusapWorldLootContainerView View { get; }
        public QusapDeathLootPlacement Placement { get; internal set; }
    }
    // Binds the single existing Bootstrap death view. Does not settle, create or transfer any item.
    public sealed class QusapDeathLootPresentation : MonoBehaviour
    {
        private QusapRaidBootstrap raid;
        private QusapLocalWorldLootAuthority owner;
        private IQusapWorldLootAuthority access;
        private Action<QusapWorldLootContainerView> addTarget;
        private int groundMask, solidMask;
        private float clearance, planeZ;
        public bool StartsOpened { get; set; }
        private readonly Dictionary<string, QusapDeathLootProjection> projections = new(StringComparer.Ordinal);
        public IReadOnlyList<QusapDeathLootProjection> Projections => projections.Values.OrderBy(p => p.Record.EliminationOrdinal).ToArray();
        public void Configure(QusapRaidBootstrap bootstrap, QusapLocalWorldLootAuthority authority, IQusapWorldLootAuthority interaction,
            Action<QusapWorldLootContainerView> add, int ground = 1, int solids = 1, float radius = .35f, float plane = 0, bool opened = false)
        {
            if (raid != null) throw new InvalidOperationException("Presentation already configured.");
            raid = bootstrap; owner = authority; access = interaction; addTarget = add;
            groundMask = ground; solidMask = solids; clearance = radius; planeZ = plane; StartsOpened = opened;
            raid.Session.DeathSettled += Bind;
            foreach (var record in raid.Session.Containers) Bind(record);
        }
        public void Bind(QusapDeathLootRecord record)
        {
            if (record == null || projections.ContainsKey(record.ContainerId)) return;
            var legacy = raid.ContainerViews.Single(v => ReferenceEquals(v.Record, record));
            var result = owner.RegisterDeathContainer(record, StartsOpened);
            if (result != QusapWorldLootResult.Success)
            {
                if (raid.Session.IsFinished) { legacy.gameObject.SetActive(false); return; }
                throw new InvalidOperationException("Authoritative death source rejected: " + result);
            }
            var view = legacy.gameObject.AddComponent<QusapWorldLootContainerView>();
            view.Configure(record.ContainerId, null); view.RequireOpeningHold(true); view.Bind(access);
            legacy.KeepDepletedVisible();
            legacy.name = "DIAGNOSTIC_DeathLoot_" + record.ParticipantId;
            legacy.transform.localScale = new(.6f, .45f, .5f);
            var projection = new QusapDeathLootProjection(record, view, default);
            projections.Add(record.ContainerId, projection); ResolvePosition(projection); addTarget(view);
        }
        private void ResolvePosition(QusapDeathLootProjection projection)
        {
            var victim = raid.Participants.Single(p => p.ParticipantId == projection.Record.ParticipantId);
            var body = victim.GetComponent<Rigidbody>();
            var lastSafe = victim.GetComponent<QusapDeathLootSafePosition>()?.LastSafePosition;
            bool resolved = QusapDeathLootPlacementResolver.TryResolve(body.position, lastSafe, groundMask, solidMask, clearance, planeZ, out var placement);
            projection.Placement = placement;
            // If no safe surface exists, retain the original logical record and defer this same projection.
            projection.View.gameObject.SetActive(resolved);
            if (resolved) projection.View.transform.position = placement.Position;
        }
        private void Update()
        { foreach (var projection in projections.Values) if (projection.Placement.Kind == QusapDeathLootPlacementKind.Unavailable) ResolvePosition(projection); }
        private void OnDestroy() { if (raid?.Session != null) raid.Session.DeathSettled -= Bind; }
    }
}
