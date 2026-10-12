using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace Qusap
{
    [DefaultExecutionOrder(1100)]
    public sealed class QusapRaidDeathLootContainerPlayground : MonoBehaviour
    {
        [SerializeField] private QusapRaidBootstrap raid;
        [SerializeField] private LayerMask groundMask = 1;
        [SerializeField] private LayerMask solidMask = 1;
        [SerializeField] private bool startsOpened;
        private readonly List<QusapWorldLootContainerView> targets = new();
        private readonly Dictionary<string, string> lastOwner = new(StringComparer.Ordinal);
        private readonly Dictionary<string, List<string>> custody = new(StringComparer.Ordinal);
        public QusapRaidBootstrap Raid => raid;
        public QusapRaidSessionAuthority Session => raid.GetComponent<QusapRaidSessionObserver>().Authority;
        public QusapLocalWorldLootAuthority Owner { get; private set; }
        public QusapLootInteractionAuthority Authority { get; private set; }
        public QusapDeathLootPresentation Presentation { get; private set; }
        public QusapLootInteractor[] Interactors { get; private set; } = Array.Empty<QusapLootInteractor>();
        public QusapRaidInventorySnapshot InitialVictim { get; private set; }
        public QusapLootInstance Sword { get; private set; }
        public QusapLootInstance Pocket { get; private set; }
        public QusapLootInstance Loaner { get; private set; }
        public QusapLootInstance RemainingHeal { get; private set; }
        public QusapLootInstance Consumed { get; private set; }
        public QusapLootInstance RivalHeal { get; private set; }
        public string EvidenceAction { get; set; } = "Diagnóstico: P2 elimina a P1; hold D-pad arriba 0,45 s; saquear; extraer";
        public int Rejections => (Owner?.RejectedRequests ?? 0) + (Authority?.BoundaryRejections ?? 0);
        public void Configure(QusapRaidBootstrap bootstrap) => raid = bootstrap;
        private void Start()
        {
            if (raid.GetComponent<QusapLocalProfilePersistence>() != null) throw new InvalidOperationException("Death playground must use isolated memory owners.");
            raid.GetComponent<QusapRaidSessionObserver>().StartSession();
            var native = FindAnyObjectByType<QusapWeaponMatchBootstrap>(); native.TryInitialize();
            Owner = new(raid.Session, raid.LootCatalog, 0, canReach: (player, id) => Access(player, id) == QusapLootInteractionRejection.None);
            Authority = new(Owner, Access);
            Interactors = raid.Participants.Select(player =>
            {
                player.gameObject.AddComponent<QusapDeathLootSafePosition>().Configure(groundMask, solidMask, .35f, 0);
                var sensor = player.gameObject.AddComponent<QusapLootProximitySensor>(); sensor.Configure(targets, mask: solidMask);
                var commands = player.gameObject.AddComponent<QusapLootInteractionInputBridge>(); commands.Configure(player.GetComponent<QusapInputReader>().LocalPlayerSlot);
                var interactor = player.gameObject.AddComponent<QusapLootInteractor>();
                interactor.Configure(player.ParticipantId, "death-loot/" + player.ParticipantId, sensor, commands, Authority, Session); return interactor;
            }).ToArray();
            Presentation = raid.gameObject.AddComponent<QusapDeathLootPresentation>();
            Presentation.Configure(raid, Owner, Authority, view => targets.Add(view), groundMask, solidMask, opened: startsOpened);
            Seed(native);
            raid.Session.World.Changed += ObserveCustody; ObserveCustody();
        }
        private QusapLootDefinition Definition(string id)
        { if (!raid.LootCatalog.TryResolve(id, out var value)) throw new InvalidOperationException("Approved catalog missing " + id); return value; }
        private QusapLootInstance Give(QusapRaidInventory player, string definition, int quantity = 1)
        {
            var item = raid.Session.World.Create(Definition(definition), "Death diagnostic " + player.ParticipantId, quantity: quantity);
            if (player.State.TryPickup(item.LootInstanceId) != QusapLootResult.Success) throw new InvalidOperationException("Diagnostic capacity mismatch");
            return item;
        }
        private void Seed(QusapWeaponMatchBootstrap native)
        {
            var p1 = raid.Participants.Single(p => p.ParticipantId == "P1"); var p2 = raid.Participants.Single(p => p.ParticipantId == "P2");
            var sword = native.CreateRaidLootWeapon(Definition("raid_purple_sword").WeaponDefinitionId);
            if (p1.GetComponent<QusapWeaponEquipment>().TryEquip(sword, out _) != QusapWeaponOperationResult.Success) throw new InvalidOperationException("Expected empty diagnostic equipment");
            Sword = raid.Session.World.FindWeapon(sword);
            Give(p1, "raid_fragment", 5); RemainingHeal = Give(p1, "raid_minor_heal", 2);
            Pocket = Give(p1, "raid_rare_relic"); p1.TryMoveToSecurePocket(Pocket.LootInstanceId);
            var loanerDefinition = Definition("raid_basic_sword");
            Loaner = raid.Session.World.CreateLoaner(loanerDefinition, native.CreateRaidLootWeapon(loanerDefinition.WeaponDefinitionId));
            if (p1.State.TryPickup(Loaner.LootInstanceId) != QusapLootResult.Success) throw new InvalidOperationException("Loaner fixture capacity mismatch");
            Consumed = Give(p1, "raid_minor_heal");
            p1.Receiver.TryReceiveEnvironmentDamage(40, this); p1.TryConsume(RemainingHeal.LootInstanceId); p1.TryConsume(Consumed.LootInstanceId);
            // Existing authoritative health API: make the next real BodyAttack lethal without changing its damage.
            p1.Receiver.TryReceiveEnvironmentDamage(Mathf.Max(0, p1.Receiver.CurrentHealth - 1), this);
            RivalHeal = Give(p2, "raid_minor_heal"); Give(p2, "raid_fragment", 1); p2.Receiver.TryReceiveEnvironmentDamage(5, this);
            InitialVictim = p1.ReadInventorySnapshot(); raid.Session.World.Notify();
        }
        private QusapLootInteractionRejection Access(string participant, string id)
        {
            var player = Interactors.FirstOrDefault(p => p.PlayerId == participant);
            return player != null ? player.Validate(targets.FirstOrDefault(t => t.SpawnPointId == id)) : QusapLootInteractionRejection.Incapacitated;
        }
        private void Update()
        {
            foreach (var player in Interactors)
            {
                var inventory = raid.Participants.Single(i => i.ParticipantId == player.PlayerId);
                if (!inventory.CanOperate || Session.Status != QusapRaidSessionStatus.Running)
                {
                    // Retire only after native equipment release and the existing death settlement succeed.
                    if (inventory.Receiver.IsEliminated && inventory.State.Status == QusapRaidInventoryStatus.Settled)
                        inventory.Receiver.RetireFromRaid();
                    if (player.enabled)
                    {
                        player.enabled = false; player.GetComponent<QusapLootInteractionInputBridge>().enabled = false;
                        player.Sensor.Configure(Array.Empty<QusapWorldLootContainerView>()); player.Sensor.Scan();
                    }
                    continue;
                }
                player.PrepareFrame(Time.deltaTime);
            }
            foreach (var player in Interactors) if (player.isActiveAndEnabled) player.SubmitPrepared();
            foreach (var player in Interactors) if (player.isActiveAndEnabled) player.Observe();
        }
        public QusapLootResult ConsumeRivalHealing() => raid.Participants.Single(p => p.ParticipantId == "P2").TryConsume(RivalHeal.LootInstanceId);
        private void ObserveCustody()
        {
            foreach (var item in raid.Session.World.Instances)
            {
                string owner = item.Location + (item.HolderId == null ? "" : "/" + item.HolderId);
                if (lastOwner.TryGetValue(item.LootInstanceId, out var before) && before == owner) continue;
                lastOwner[item.LootInstanceId] = owner;
                if (!custody.TryGetValue(item.LootInstanceId, out var chain)) custody.Add(item.LootInstanceId, chain = new());
                chain.Add(owner);
            }
        }
        public IReadOnlyList<string> Custody(string id) => custody.TryGetValue(id, out var chain) ? chain.AsReadOnly() : Array.Empty<string>();
        private void OnDestroy() { if (raid?.Session != null) raid.Session.World.Changed -= ObserveCustody; }
    }
}
