using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace Qusap
{
    [DefaultExecutionOrder(500)]
    [DisallowMultipleComponent]
    public sealed class QusapRaidBootstrap : MonoBehaviour
    {
        [SerializeField] private QusapRaidInventory[] participants;
        [SerializeField] private QusapLootDefinition[] definitions;
        [SerializeField] private QusapDeathLootContainer deathContainerPrefab;
        private readonly List<QusapDeathLootContainer> views = new();
        public QusapRaidSession Session { get; private set; }
        public IReadOnlyList<QusapRaidInventory> Participants => participants;
        public IReadOnlyList<QusapDeathLootContainer> ContainerViews => views;
        public IReadOnlyList<QusapLootDefinition> Definitions => definitions;
        public void Configure(QusapRaidInventory[] players, QusapLootDefinition[] loot, QusapDeathLootContainer prefab)
        { participants = players; definitions = loot; deathContainerPrefab = prefab; }
        private void Awake()
        { Session = new QusapRaidSession(Guid.NewGuid().ToString("N")); Session.DeathSettled += SpawnContainer; }
        private void Start()
        {
            foreach (var player in participants) player.Initialize(Session, definitions);
            Session.World.Notify();
        }
        private void SpawnContainer(QusapDeathLootRecord record)
        {
            var owner = participants.Single(p => p.ParticipantId == record.ParticipantId);
            var view = Instantiate(deathContainerPrefab, owner.transform.position + Vector3.up * 0.25f, Quaternion.identity, transform);
            view.name = "Contenedor_" + record.ParticipantId;
            view.Initialize(Session.World, record); views.Add(view);
        }
        private void OnDestroy() { Session?.Dispose(); }
    }
}
