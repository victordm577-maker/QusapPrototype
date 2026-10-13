using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEngine;
using UnityEngine.UI;

namespace Qusap
{
    // Isolated diagnostic scene. Outcomes and rewards are decided exclusively by Session.Competition.
    [DefaultExecutionOrder(1100)]
    public sealed class QusapRaidLastSurvivorPlayground : MonoBehaviour
    {
        [SerializeField] private GameObject roundTemplate;
        [SerializeField] private QusapRaidLootCatalog catalog;
        private GameObject round;
        private QusapWeaponMatchBootstrap native;
        private ulong nativeHighWater;
        private Text header, diagnostics, summary;
        private Button begin;
        private readonly List<string> history = new();
        public QusapPersistentStashRepository[] Profiles { get; private set; }
        public QusapSharedRaidContext Context { get; private set; }
        public QusapRaidBootstrap Raid { get; private set; }
        public QusapRaidSessionAuthority Session => Context?.Session?.Competition;
        public Canvas Canvas { get; private set; }
        public int RaidNumber { get; private set; }
        public int Duplicates { get; private set; }
        public string LastSummary { get; private set; } = "Caso A: P2 elimina a P1. Caso B: P1 extrae y P2 vuelve a ganar.";
        public string EvidenceAction { get; set; } = "P1 teclado · P2 gamepad · dos perfiles temporales independientes";
        public IReadOnlyList<string> RaidHistory => history.AsReadOnly();
        public void Configure(GameObject template, QusapRaidLootCatalog definitions) { roundTemplate = template; catalog = definitions; }
        private void Start()
        {
            Profiles = Enumerable.Range(0, 2).Select(i => new QusapPersistentStashRepository(
                Path.Combine(Path.GetTempPath(), "QusapVictoryMemoryOnly", Guid.NewGuid().ToString("N")), catalog.Definitions,
                "victory/P" + (i + 1) + "/" + Guid.NewGuid().ToString("N"), new QusapMemoryProfileFiles())).ToArray();
            BuildHud(); Refresh();
        }
        public bool BeginRaid()
        {
            if (Session != null && Session.Status != QusapRaidSessionStatus.Finished) return false;
            if (round != null)
            {
                nativeHighWater = Math.Max(nativeHighWater, native.NextWeaponInstanceId - 1);
                Context.Dispose(); round.SetActive(false); Destroy(round);
            }
            round = Instantiate(roundTemplate); round.name = "VictoryRound_" + (RaidNumber + 1);
            if (RaidNumber >= 1)
            {
                // Case B has its own initial spawn near extraction; movement still uses native controls.
                var actors = round.GetComponentInChildren<QusapRaidBootstrap>(true).Participants;
                var starts = new[] { new Vector3(4.4f, 1.05f, 0), new Vector3(.85f, 1.05f, 0) };
                for (int i = 0; i < 2; i++) actors[i].transform.position = starts[i];
                round.GetComponentInChildren<QusapCombatArenaController>(true).Configure(actors[0].GetComponent<QusapInputReader>(), actors[1].GetComponent<QusapInputReader>(), starts[0], starts[1], true);
            }
            round.SetActive(true);
            Raid = round.GetComponentInChildren<QusapRaidBootstrap>(); native = round.GetComponentInChildren<QusapWeaponMatchBootstrap>();
            native.ReserveWeaponInstanceIdsThrough(Math.Max(nativeHighWater, Profiles.Max(p => p.NextNativeWeaponInstanceId) - 1)); native.TryInitialize();
            var context = new QusapSharedRaidContext("P1", "P2");
            for (int i = 0; i < 2; i++)
            {
                var player = Raid.Participants[i]; player.Configure(player.ParticipantId, Profiles[i].ProfileId);
                Profiles[i].ObserveNativeAllocator(() => native.NextWeaponInstanceId);
                // Redeploy the previous winner's exact sword and consumables on the second raid.
                var stash = Profiles[i].ReadStashSnapshot(Profiles[i].ProfileId);
                string sword = stash.FirstOrDefault(s => s.Category == QusapLootCategory.Weapon).LootInstanceId;
                var stacks = stash.Where(s => s.Category == QusapLootCategory.Consumable).Take(2).Select(s => new QusapLoadoutStack(s.LootInstanceId, s.Quantity)).ToArray();
                var deployment = new QusapRaidDeploymentAuthority(Profiles[i], catalog, player.ParticipantId,
                    player.GetComponent<QusapWeaponEquipment>(), d => native.CreateRaidLootWeapon(d.WeaponDefinitionId));
                if (!deployment.PrepareLoadout(sword, stacks) || !context.Add(deployment)) throw new InvalidOperationException("Isolated loadout rejected");
            }
            if (!context.CommitDeployment() || !context.StartRunning()) throw new InvalidOperationException(context.LastResult);
            Context = context; Raid.BeginSharedDeploymentSession(context); RaidNumber++; history.Add(context.RaidId);
            Session.SessionFinished += Completed;
            Give(0, "raid_fragment", 2);
            Give(1, "raid_fragment", 3);
            var pocket = Give(1, "raid_rare_relic");
            if (Raid.Participants[1].TryMoveToSecurePocket(pocket.LootInstanceId) != QusapLootResult.Success) throw new InvalidOperationException("Pocket fixture rejected");
            if (RaidNumber == 1)
            {
                Give(1, "raid_minor_heal", 2); Give(1, "raid_purple_sword");
                // Existing health API makes a real BodyAttack lethal; no combat settings change.
                var victim = Raid.Participants[0].Receiver; victim.TryReceiveEnvironmentDamage(victim.CurrentHealth - 1, this);
            }
            begin.gameObject.SetActive(false); Refresh(); return true;
        }
        private QusapLootInstance Give(int index, string id, int quantity = 1)
        {
            if (!catalog.TryResolve(id, out var definition)) throw new InvalidOperationException(id);
            var weapon = definition.Category == QusapLootCategory.Weapon ? native.CreateRaidLootWeapon(definition.WeaponDefinitionId) : null;
            var item = Raid.Session.World.Create(definition, "Victory diagnostic P" + (index + 1), weapon, quantity);
            if (Raid.Participants[index].State.TryPickup(item.LootInstanceId) != QusapLootResult.Success) throw new InvalidOperationException("Real pickup rejected");
            return item;
        }
        private void Completed(QusapRaidSessionResult result)
        {
            LastSummary = $"CASO {(RaidNumber == 1 ? "A" : "B")} FINALIZADO · Ganador: {result.WinnerId ?? "Sin ganador"}\n"
                + string.Join(" · ", result.Participants.Select(p => p.ParticipantId + ": " + p.Outcome))
                + $"\nLoot conservado P2: {Profiles[1].ReadStashSnapshot(Profiles[1].ProfileId).Count} objetos · Cuerno: {(Profiles[1].HasTriumphHorn ? "desbloqueado" : "bloqueado")}"
                + $" · Concesiones: {Profiles[1].TriumphHornGrantsPerformed} · Finalizaciones: {Session.SessionFinishedEvents}";
            Debug.Log("RAID_LAST_SURVIVOR_FINISHED " + result.SessionId + " " + LastSummary);
            begin.gameObject.SetActive(true); begin.GetComponentInChildren<Text>().text = "Nueva raid · caso B · mismo perfil ganador"; Refresh();
        }
        private void Update()
        {
            if (Raid?.Session != null)
                foreach (var player in Raid.Participants)
                    if (player.Receiver.IsEliminated && player.State.Status == QusapRaidInventoryStatus.Settled) player.Receiver.RetireFromRaid();
            Refresh();
        }
        private void Refresh()
        {
            if (Profiles == null) return;
            header.text = "ULTIMO SUPERVIVIENTE · CUERNO DEL TRIUNFO\n" + EvidenceAction;
            summary.text = LastSummary;
            if (Session == null) { diagnostics.text = "Deployment pendiente · roster abierto · sin evaluación de victoria\nCuerno P1: bloqueado · P2: bloqueado · Concesiones: 0"; return; }
            var live = Raid.Session.World.Instances.Where(i => i.Location != QusapLootLocation.Stash && i.Location != QusapLootLocation.Destroyed
                && i.Location != QusapLootLocation.Consumed && i.Location != QusapLootLocation.Merged).Select(i => i.LootInstanceId);
            var ids = live.Concat(Profiles.SelectMany(p => p.ReadStashSnapshot(p.ProfileId)).Select(i => i.LootInstanceId)).ToArray();
            Duplicates = ids.Length - ids.Distinct(StringComparer.Ordinal).Count();
            diagnostics.text = $"RaidId: {Context.RaidId}\nRoster cerrado: {Session.RosterClosed} · Despliegue completo: {Session.SharedDeploymentCompleted} · {Session.Status}\n"
                + string.Join(" · ", Session.ReadParticipantsSnapshot().Select(p => p.ParticipantId + ": " + p.Outcome))
                + $" · Active: {Session.ActiveCount} · Ganador: {Session.WinnerId ?? "Sin ganador"}\n"
                + $"Resoluciones de loot: {Raid.Session.ExtractionTransfersResolved} · Death Loot: {Raid.Session.Containers.Count} · RaidLoaner destruidos: {Raid.Session.World.LoanersDestroyed}\n"
                + string.Join("\n", Profiles.Select((p, i) => $"P{i + 1} · Revision {p.Revision} · Stash {p.ReadStashSnapshot(p.ProfileId).Count} · Cuerno: {(p.HasTriumphHorn ? "desbloqueado" : "bloqueado")} · Concesiones efectuadas: {p.TriumphHornGrantsPerformed}"))
                + $"\nDuplicados detectados: {Duplicates}";
        }
        private void BuildHud()
        {
            var root = new GameObject("VictoryCanvas", typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster)); root.transform.SetParent(transform, false);
            Canvas = root.GetComponent<Canvas>(); Canvas.renderMode = RenderMode.ScreenSpaceOverlay; Canvas.sortingOrder = 20;
            var scaler = root.GetComponent<CanvasScaler>(); scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize; scaler.referenceResolution = new(1280, 720);
            header = Label(root.transform, "VictoryHeader", 25, 12, 1230, 64, 22);
            diagnostics = Label(root.transform, "VictoryDiagnostics", 25, 82, 1230, 205, 19);
            summary = Label(root.transform, "VictorySummary", 25, 578, 1230, 127, 19);
            var button = new GameObject("IniciarRaid", typeof(RectTransform), typeof(Image), typeof(Button)); button.transform.SetParent(root.transform, false);
            var rect = button.GetComponent<RectTransform>(); rect.anchorMin = rect.anchorMax = new(.5f, 0); rect.pivot = new(.5f, 0); rect.anchoredPosition = new(0, 148); rect.sizeDelta = new(500, 42);
            button.GetComponent<Image>().color = new(.12f, .32f, .24f); begin = button.GetComponent<Button>(); begin.onClick.AddListener(() => BeginRaid());
            var label = Label(button.transform, "BeginLabel", 0, 0, 500, 42, 20); label.alignment = TextAnchor.MiddleCenter; label.text = "Iniciar caso A · dos jugadores desplegados";
        }
        private static Text Label(Transform parent, string name, float x, float y, float w, float h, int size)
        {
            var root = new GameObject(name, typeof(RectTransform), typeof(Image)); root.transform.SetParent(parent, false);
            var rect = root.GetComponent<RectTransform>(); rect.anchorMin = rect.anchorMax = new(0, 1); rect.pivot = new(0, 1); rect.anchoredPosition = new(x, -y); rect.sizeDelta = new(w, h);
            root.GetComponent<Image>().color = new(.025f, .04f, .065f, .9f);
            var go = new GameObject("Text", typeof(RectTransform), typeof(Text)); go.transform.SetParent(root.transform, false);
            var text = go.GetComponent<Text>(); text.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf"); text.fontSize = size; text.color = Color.white; text.raycastTarget = false;
            text.rectTransform.anchorMin = Vector2.zero; text.rectTransform.anchorMax = Vector2.one; text.rectTransform.offsetMin = new(10, 3); text.rectTransform.offsetMax = new(-10, -3); return text;
        }
        private void OnDestroy() => Context?.Dispose();
    }
}
