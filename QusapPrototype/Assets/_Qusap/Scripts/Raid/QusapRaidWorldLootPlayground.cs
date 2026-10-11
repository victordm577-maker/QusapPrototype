using System;
using System.Linq;
using System.Threading;
using UnityEngine;

namespace Qusap
{
    [DefaultExecutionOrder(600)]
    public sealed class QusapRaidWorldLootPlayground : MonoBehaviour
    {
        [SerializeField] private QusapRaidLoadoutPlayground loadout;
        [SerializeField] private QusapWorldLootContainerView[] containers = Array.Empty<QusapWorldLootContainerView>();
        [SerializeField] private ulong seed = 606;
        public QusapRaidLoadoutPlayground Loadout => loadout;
        public QusapRaidInventory LocalPlayer => loadout.Raid.Participants[0];
        public QusapLocalWorldLootAuthority Authority { get; private set; }
        public System.Collections.Generic.IReadOnlyList<QusapWorldLootContainerView> Containers => Array.AsReadOnly(containers);
        public int SelectedContainer { get; private set; }
        public int SelectedItem { get; set; }
        public ulong Seed => Authority?.Seed ?? seed;
        public string LastTakenInstanceId { get; private set; }
        public string Action { get; set; } = "1/2/3 cache | O abrir | T tomar stack | Y tomar 1 | J/K/L roll | B bolsillo | H curar | V daño diagnóstico";
        public void Configure(QusapRaidLoadoutPlayground deployment, QusapWorldLootContainerView[] views, ulong initialSeed)
        { loadout = deployment; containers = (QusapWorldLootContainerView[])views.Clone(); seed = initialSeed; }
        private void Start()
        {
            loadout.SelectLoaner();
            if (!loadout.Prepare() || !loadout.Commit() || !loadout.Run()) throw new InvalidOperationException("Isolated world-loot deployment rejected: " + loadout.Action);
            var native = FindAnyObjectByType<QusapWeaponMatchBootstrap>();
            Authority = new QusapLocalWorldLootAuthority(loadout.Deployment.Session, loadout.Catalog, seed,
                d => native.CreateRaidLootWeapon(d.WeaponDefinitionId), (participant, spawn) => containers.FirstOrDefault(c => c.SpawnPointId == spawn)
                    ?.CanReach(loadout.Raid.Participants.FirstOrDefault(p => p.ParticipantId == participant)) == true);
            foreach (var view in containers)
            {
                if (Authority.RegisterSpawn(view.SpawnPointId, view.Table) != QusapWorldLootResult.Success) throw new InvalidOperationException("Invalid or duplicate isolated world spawn: " + view.SpawnPointId);
                view.Bind(Authority);
            }
        }
        public bool ConfigureSeedBeforeOpen(ulong value) => Authority != null && Authority.TrySetSeedBeforeOpening(value);
        public void Select(int index) { if (index < 0 || index >= containers.Length) throw new ArgumentOutOfRangeException(nameof(index)); SelectedContainer = index; SelectedItem = 0; }
        public QusapWorldLootContainerSnapshot ReadSelected() => containers[SelectedContainer].Snapshot;
        public QusapWorldLootReply OpenSelected()
        {
            var reply = Authority.RequestOpen(new(LocalPlayer.ParticipantId, containers[SelectedContainer].SpawnPointId));
            Action = "OPEN " + containers[SelectedContainer].SpawnPointId + " : " + reply.Result; return reply;
        }
        public QusapWorldLootTakeRequest CreateTakeRequest(int quantity = -1)
        {
            var snapshot = ReadSelected(); var item = snapshot.Items.Count > 0 ? snapshot.Items[Math.Clamp(SelectedItem, 0, snapshot.Items.Count - 1)] : default;
            return new(LocalPlayer.ParticipantId, snapshot.SpawnPointId, item.LootInstanceId, quantity < 0 ? item.Quantity : quantity, snapshot.Version);
        }
        public QusapWorldLootReply Submit(QusapWorldLootTakeRequest request, CancellationToken cancellation = default)
        {
            var reply = Authority.RequestTake(request, cancellation);
            if (reply.Confirmed) LastTakenInstanceId = reply.DestinationInstanceId;
            Action = "TAKE " + reply.Result + " | confirmadas " + reply.ConfirmedQuantity + " | " + request.SpawnPointId;
            return reply;
        }
        public QusapWorldLootReply TakeSelected(int quantity = -1) => Submit(CreateTakeRequest(quantity));
        public QusapLootResult MoveLastToPocket()
        { var result = LocalPlayer.TryMoveToSecurePocket(LastTakenInstanceId); Action = "BOLSILLO manual: " + result; return result; }
        public QusapLootResult Consume()
        {
            var item = LocalPlayer.ReadInventorySnapshot().Backpack.FirstOrDefault(i => i.HasValue && i.Value.Category == QusapLootCategory.Consumable);
            var result = LocalPlayer.TryConsume(item?.LootInstanceId); Action = "CONSUMO: " + result; return result;
        }
        public void DiagnosticDamage() { LocalPlayer.Receiver.TryReceiveEnvironmentDamage(25, this); Action = "DIAGNÓSTICO: daño 25 mediante la API existente, para comprobar curación"; }
    }
}
