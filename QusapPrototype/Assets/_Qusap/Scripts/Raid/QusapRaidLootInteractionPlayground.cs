using System;
using System.Linq;
using UnityEngine;

namespace Qusap
{
    [DefaultExecutionOrder(1100)]
    public sealed class QusapRaidLootInteractionPlayground : MonoBehaviour
    {
        [SerializeField] private QusapRaidBootstrap raid;
        [SerializeField] private QusapWorldLootContainerView[] containers;
        [SerializeField, Min(.01f)] private float interactionRange = 2.75f;
        [SerializeField, Min(0)] private float rangeHysteresis = .2f;
        [SerializeField, Min(.01f)] private float holdDuration = .45f;
        [SerializeField] private LayerMask solidGeometryMask = 1;
        [SerializeField] private ulong seed = 606;
        public QusapRaidBootstrap Raid => raid;
        public QusapRaidSessionAuthority Session => raid.GetComponent<QusapRaidSessionObserver>().Authority;
        public QusapLocalWorldLootAuthority Owner { get; private set; }
        public QusapLootInteractionAuthority Authority { get; private set; }
        public QusapLootInteractor[] Interactors { get; private set; } = Array.Empty<QusapLootInteractor>();
        public System.Collections.Generic.IReadOnlyList<QusapWorldLootContainerView> Containers => containers;
        public int OpeningCount => containers.Sum(c => c != null ? c.Snapshot?.GenerationCount ?? 0 : 0);
        public int Rejections => (Owner?.RejectedRequests ?? 0) + (Authority?.BoundaryRejections ?? 0);
        public void Configure(QusapRaidBootstrap bootstrap, QusapWorldLootContainerView[] views) { raid = bootstrap; containers = views; }
        private void Start()
        {
            // This diagnostic scene has no profile, deployment, extraction or persistence component.
            if (raid.GetComponent<QusapLocalProfilePersistence>() != null) throw new InvalidOperationException("Interaction playground must be memory-only");
            var native = FindAnyObjectByType<QusapWeaponMatchBootstrap>(); native.TryInitialize();
            raid.GetComponent<QusapRaidSessionObserver>().StartSession();
            Owner = new(raid.Session, raid.LootCatalog, seed, d => native.CreateRaidLootWeapon(d.WeaponDefinitionId),
                (participant, spawn) => ValidateAccess(participant, spawn) == QusapLootInteractionRejection.None);
            Authority = new(Owner, ValidateAccess);
            foreach (var view in containers)
            {
                if (Authority.RegisterSpawn(view.SpawnPointId, view.Table) != QusapWorldLootResult.Success) throw new InvalidOperationException("Invalid isolated spawn");
                view.Bind(Authority);
            }
            Interactors = raid.Participants.Select(player =>
            {
                var sensor = player.gameObject.AddComponent<QusapLootProximitySensor>(); sensor.Configure(containers, interactionRange, rangeHysteresis, solidGeometryMask);
                var input = player.gameObject.AddComponent<QusapLootInteractionInputBridge>(); input.Configure(player.GetComponent<QusapInputReader>().LocalPlayerSlot);
                var interactor = player.gameObject.AddComponent<QusapLootInteractor>();
                interactor.Configure(player.ParticipantId, "loot/" + player.ParticipantId, sensor, input, Authority, Session, holdDuration); return interactor;
            }).ToArray();
        }
        private QusapLootInteractionRejection ValidateAccess(string participant, string spawn)
        {
            var interactor = Interactors.FirstOrDefault(i => i != null && i.PlayerId == participant);
            return interactor != null ? interactor.Validate(containers.FirstOrDefault(c => c != null && c.SpawnPointId == spawn)) : QusapLootInteractionRejection.Incapacitated;
        }
        private void Update()
        {
            foreach (var player in Interactors) if (player != null) player.PrepareFrame(Time.deltaTime);
            foreach (var player in Interactors) if (player != null) player.SubmitPrepared();
            foreach (var player in Interactors) if (player != null) player.Observe();
        }
    }
}
