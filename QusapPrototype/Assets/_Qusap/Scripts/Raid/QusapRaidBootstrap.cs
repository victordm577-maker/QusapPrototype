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
        [SerializeField] private QusapRaidLootCatalog lootCatalog;
        [SerializeField] private QusapDeathLootContainer deathContainerPrefab;
        [SerializeField] private bool deferForDeployment;
        private readonly List<QusapDeathLootContainer> views = new();
        public QusapRaidSession Session { get; private set; }
        public IReadOnlyList<QusapRaidInventory> Participants => participants;
        public IReadOnlyList<QusapDeathLootContainer> ContainerViews => views;
        public IReadOnlyList<QusapLootDefinition> Definitions => definitions;
        public QusapRaidLootCatalog LootCatalog => lootCatalog;
        public void Configure(QusapRaidInventory[] players, QusapLootDefinition[] loot, QusapDeathLootContainer prefab, QusapRaidLootCatalog catalog = null)
        { participants = players; definitions = loot; deathContainerPrefab = prefab; lootCatalog = catalog; }
        private void Awake()
        {
            if (lootCatalog != null)
            {
                var errors = lootCatalog.Validate();
                if (errors.Count != 0) throw new InvalidOperationException("Invalid authoritative raid catalog: " + string.Join("; ", errors));
                definitions = lootCatalog.Definitions.ToArray();
            }
            var profile = GetComponent<QusapLocalProfilePersistence>();
            profile?.Initialize(this);
            if (deferForDeployment) return;
            Session = new QusapRaidSession(Guid.NewGuid().ToString("N"), profile?.Repository);
            Session.DeathSettled += SpawnContainer;
        }
        private void Start()
        {
            if (Session == null) return;
            foreach (var player in participants) player.Initialize(Session, definitions);
            Session.World.Notify();
        }
        public void BeginDeploymentSession(QusapRaidDeploymentAuthority deployment)
        {
            if (!deferForDeployment || Session != null || deployment.Status != QusapDeploymentStatus.Prepared)
                throw new InvalidOperationException("Expected the first committed deployment");
            Session = deployment.Session; Session.DeathSettled += SpawnContainer;
            var local = participants.Single(p => p.ParticipantId == deployment.Inventory.ParticipantId);
            local.BindDeployment(Session, deployment.Inventory, deployment.WeaponAdapter);
            foreach (var player in participants) if (player != local) player.Initialize(Session, definitions);
            foreach (var player in participants) player.GetComponent<QusapRaidExtraction>()?.BindSession();
            GetComponent<QusapRaidSessionObserver>()?.InitializeSession();
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
