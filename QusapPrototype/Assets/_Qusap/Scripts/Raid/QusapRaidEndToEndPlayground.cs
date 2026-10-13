using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using UnityEngine;
using UnityEngine.UI;

namespace Qusap
{
    // Isolated scene orchestration. All ownership, combat and settlements remain production operations.
    [DefaultExecutionOrder(1100)]
    public sealed class QusapRaidEndToEndPlayground : MonoBehaviour
    {
        [SerializeField] private GameObject roundTemplate;
        [SerializeField] private QusapRaidLootCatalog catalog;
        private GameObject round;
        private QusapWeaponMatchBootstrap native;
        private readonly List<QusapWorldLootContainerView> targets = new();
        private readonly HashSet<string> protectedIds = new(StringComparer.Ordinal);
        private readonly List<string> history = new();
        private ulong nativeHighWater;
        private long[] before;
        private Text heading, summary;
        private Text[] lootPanels;
        private Button begin;
        private Button[] selectionButtons;
        private readonly string[] selectedWeapons = new string[2];
        private readonly QusapLoadoutStack[][] selectedStacks = new QusapLoadoutStack[2][];
        private bool finished;
        public QusapPersistentStashRepository[] Profiles { get; private set; }
        public QusapSharedRaidContext Context { get; private set; }
        public QusapRaidBootstrap Raid { get; private set; }
        public QusapRaidSessionAuthority Session => Raid?.GetComponent<QusapRaidSessionObserver>()?.Authority;
        public QusapLocalWorldLootAuthority Owner { get; private set; }
        public QusapLootInteractionAuthority Interaction { get; private set; }
        public QusapDeathLootPresentation DeathPresentation { get; private set; }
        public QusapLootInteractor[] Interactors { get; private set; } = Array.Empty<QusapLootInteractor>();
        public QusapRaidInventoryHud InventoryHud { get; private set; }
        public Canvas Canvas { get; private set; }
        public int RaidNumber { get; private set; }
        public ulong Seed { get; private set; }
        public int Duplicates { get; private set; }
        public int LostItems { get; private set; }
        public int SecuredItems { get; private set; }
        public int ExtractedItems { get; private set; }
        public string LastSummary { get; private set; } = "Dos perfiles temporales independientes. Selección inicial vacía: RaidLoaner por jugador.";
        public string EvidenceAction { get; set; } = "P1 teclado · F | P2 gamepad · D-pad arriba | E: WeaponSwapThrow";
        public IReadOnlyList<string> RaidHistory => history.AsReadOnly();
        public IReadOnlyList<QusapWorldLootContainerView> Targets => targets;
        public void Configure(GameObject template, QusapRaidLootCatalog definitions) { roundTemplate = template; catalog = definitions; }
        private void Start()
        {
            Profiles = Enumerable.Range(0, 2).Select(i => new QusapPersistentStashRepository(
                Path.Combine(Path.GetTempPath(), "QusapEndToEndMemory", Guid.NewGuid().ToString("N")), catalog.Definitions,
                "playground/P" + (i + 1) + "/" + Guid.NewGuid().ToString("N"), new QusapMemoryProfileFiles())).ToArray();
            BuildHud(); CreateRound(); Refresh();
        }
        private void SelectOwnedLoadout(int index)
        {
            var stash = Profiles[index].ReadStashSnapshot(Profiles[index].ProfileId);
            selectedWeapons[index] = stash.FirstOrDefault(s => s.Category == QusapLootCategory.Weapon).LootInstanceId;
            selectedStacks[index] = stash.Where(s => s.Category == QusapLootCategory.Consumable).Take(2).Select(s => new QusapLoadoutStack(s.LootInstanceId, s.Quantity)).ToArray();
        }
        private void ChooseWeapon(int index)
        {
            if (Context != null && !finished) return;
            var options = new string[] { null }.Concat(Profiles[index].ReadStashSnapshot(Profiles[index].ProfileId).Where(i => i.Category == QusapLootCategory.Weapon).Select(i => i.LootInstanceId)).ToArray();
            selectedWeapons[index] = options[(Array.IndexOf(options, selectedWeapons[index]) + 1) % options.Length];
        }
        private void ChooseConsumables(int index)
        {
            if (Context != null && !finished) return;
            selectedStacks[index] = selectedStacks[index].Length > 0 ? Array.Empty<QusapLoadoutStack>()
                : Profiles[index].ReadStashSnapshot(Profiles[index].ProfileId).Where(s => s.Category == QusapLootCategory.Consumable).Take(2).Select(s => new QusapLoadoutStack(s.LootInstanceId, s.Quantity)).ToArray();
        }
        private void CreateRound(bool selectDefaults = true)
        {
            if (round != null)
            {
                nativeHighWater = Math.Max(nativeHighWater, native.NextWeaponInstanceId - 1);
                Context?.Dispose(); round.SetActive(false); Destroy(round);
            }
            Context = null; Owner = null; Interaction = null; InventoryHud = null; DeathPresentation = null;
            targets.Clear(); Interactors = Array.Empty<QusapLootInteractor>(); protectedIds.Clear(); finished = false;
            round = Instantiate(roundTemplate); round.name = "RaidRound_" + (RaidNumber + 1); round.SetActive(true);
            Raid = round.GetComponentInChildren<QusapRaidBootstrap>(); native = round.GetComponentInChildren<QusapWeaponMatchBootstrap>();
            native.ReserveWeaponInstanceIdsThrough(Math.Max(nativeHighWater, Profiles.Max(p => p.NextNativeWeaponInstanceId) - 1));
            native.TryInitialize();
            for (int i = 0; i < Raid.Participants.Count; i++)
            {
                var player = Raid.Participants[i]; player.Configure(player.ParticipantId, Profiles[i].ProfileId);
                player.GetComponent<QusapInputReader>().SetExternalGameplayInputBlocked(true);
                Profiles[i].ObserveNativeAllocator(() => native.NextWeaponInstanceId);
                if (selectDefaults) SelectOwnedLoadout(i);
            }
        }
        public bool BeginRaid()
        {
            if (Session != null && Session.Status != QusapRaidSessionStatus.Finished) return false;
            if (finished) CreateRound(false);
            if (Context != null) return false;
            before = Profiles.Select(p => p.Revision).ToArray();
            var context = new QusapSharedRaidContext("P1", "P2");
            for (int i = 0; i < 2; i++)
            {
                var deployment = new QusapRaidDeploymentAuthority(Profiles[i], catalog, Raid.Participants[i].ParticipantId,
                    Raid.Participants[i].GetComponent<QusapWeaponEquipment>(), d => native.CreateRaidLootWeapon(d.WeaponDefinitionId));
                if (!deployment.PrepareLoadout(selectedWeapons[i], selectedStacks[i]) || !context.Add(deployment))
                { deployment.Dispose(); context.Dispose(); throw new InvalidOperationException("Loadout rejected before shared start"); }
            }
            if (!context.CommitDeployment() || !context.StartRunning())
            { string message = context.LastResult; context.Dispose(); throw new InvalidOperationException("Shared start rejected and rolled back: " + message); }
            Context = context; Raid.BeginSharedDeploymentSession(context);
            if (!Raid.GetComponent<QusapRaidSessionObserver>().StartSession()) throw new InvalidOperationException("Session start rejected");
            RaidNumber++; history.Add(context.RaidId); SetupPhysicalLoot();
            var hudRoot = new GameObject("AuthorizedInventoryHud"); hudRoot.transform.SetParent(round.transform);
            InventoryHud = hudRoot.AddComponent<QusapRaidInventoryHud>(); InventoryHud.Configure(Raid);
            Session.SessionFinished += Completed;
            Raid.Session.World.Changed += Observe;
            foreach (var player in Raid.Participants) player.GetComponent<QusapInputReader>().SetExternalGameplayInputBlocked(false);
            begin.gameObject.SetActive(false); Observe(); Refresh(); return true;
        }
        private void SetupPhysicalLoot()
        {
            var caches = round.GetComponentsInChildren<QusapWorldLootContainerView>().OrderBy(v => v.SpawnPointId, StringComparer.Ordinal).ToArray();
            // Select a reproducible demonstration seed from the approved table, before opening/generation.
            var valuable = caches.Single(v => v.Table.LootTable.LootTableId == "HighValueCache");
            for (Seed = 0; Seed < 100000; Seed++)
            {
                var rolls = valuable.Table.Resolve(catalog, Context.RaidId, valuable.SpawnPointId, Seed);
                if (rolls.Any(r => r.Definition.Category == QusapLootCategory.Relic)
                    && rolls.Any(r => r.Definition.Category == QusapLootCategory.Material)) break;
            }
            if (Seed == 100000) throw new InvalidOperationException("No demonstration seed found in approved table");
            Owner = new QusapLocalWorldLootAuthority(Raid.Session, catalog, Seed, d => native.CreateRaidLootWeapon(d.WeaponDefinitionId),
                (participant, spawn) => Access(participant, spawn) == QusapLootInteractionRejection.None);
            Interaction = new QusapLootInteractionAuthority(Owner, Access);
            foreach (var cache in caches)
            { if (Interaction.RegisterSpawn(cache.SpawnPointId, cache.Table) != QusapWorldLootResult.Success) throw new InvalidOperationException("Cache registration failed"); cache.RequireOpeningHold(true); cache.Bind(Interaction); targets.Add(cache); }
            Interactors = Raid.Participants.Select(player =>
            {
                player.gameObject.AddComponent<QusapDeathLootSafePosition>().Configure(1, 1, .35f, 0);
                var sensor = player.gameObject.AddComponent<QusapLootProximitySensor>(); sensor.Configure(targets);
                var input = player.gameObject.AddComponent<QusapLootInteractionInputBridge>(); input.Configure(player.GetComponent<QusapInputReader>().LocalPlayerSlot);
                var interactor = player.gameObject.AddComponent<QusapLootInteractor>();
                interactor.Configure(player.ParticipantId, "end-to-end/" + player.ParticipantId, sensor, input, Interaction, Session); return interactor;
            }).ToArray();
            DeathPresentation = Raid.gameObject.AddComponent<QusapDeathLootPresentation>();
            DeathPresentation.Configure(Raid, Owner, Interaction, AddDeathTarget);
        }
        private void AddDeathTarget(QusapWorldLootContainerView target)
        {
            targets.Add(target);
            // Scene-only marker for the existing single view, so the corpse does not hide its appearance.
            var label = new GameObject("DeathContainerMarker"); label.transform.SetParent(target.transform, false);
            label.transform.position = target.transform.position + Vector3.up * 2.05f;
            var text = label.AddComponent<TextMesh>(); text.text = "▼ DEATH LOOT"; text.color = new(.9f, .55f, 1);
            text.anchor = TextAnchor.MiddleCenter; text.characterSize = .24f; text.fontSize = 36;
        }
        private QusapLootInteractionRejection Access(string participant, string id)
        {
            var player = Interactors.FirstOrDefault(p => p.PlayerId == participant);
            return player == null ? QusapLootInteractionRejection.Incapacitated : player.Validate(targets.FirstOrDefault(t => t != null && t.SpawnPointId == id));
        }
        private void Observe()
        {
            foreach (var item in Raid.Session.World.Instances.Where(i => i.Location == QusapLootLocation.SecurePocket)) protectedIds.Add(item.LootInstanceId);
            var live = Raid.Session.World.Instances.Where(i => i.Location != QusapLootLocation.Stash && i.Location != QusapLootLocation.Consumed
                && i.Location != QusapLootLocation.Merged && i.Location != QusapLootLocation.Destroyed).Select(i => i.LootInstanceId);
            var ids = live.Concat(Profiles.SelectMany(p => p.ReadStashSnapshot(p.ProfileId)).Select(i => i.LootInstanceId)).ToArray();
            Duplicates = ids.Length - ids.Distinct(StringComparer.Ordinal).Count();
        }
        private void Completed(QusapRaidSessionResult result)
        {
            // Observer subscribed first and has already finalized the existing ledger.
            Context.FinishSession(); Observe(); finished = true;
            var items = Raid.Session.World.Instances;
            SecuredItems = items.Count(i => protectedIds.Contains(i.LootInstanceId) && i.Location == QusapLootLocation.Stash);
            ExtractedItems = items.Count(i => i.Location == QusapLootLocation.Stash && !protectedIds.Contains(i.LootInstanceId));
            LostItems = items.Count(i => !i.RaidLoaner && (i.Location == QusapLootLocation.World || i.Location == QusapLootLocation.WorldContainer
                || i.Location == QusapLootLocation.DeathContainer || i.Location == QusapLootLocation.Destroyed));
            LastSummary = $"RAID {RaidNumber} FINALIZADA · {result.SessionId}\n"
                + string.Join(" · ", result.Participants.Select(p => p.ParticipantId + ": " + p.Outcome))
                + $"\nExtraídos {result.ExtractedCount} · Eliminados {result.EliminatedCount} · Asegurados {SecuredItems} · Objetos extraídos {ExtractedItems}"
                + $"\nPerdidos {LostItems} · RaidLoaner destruidos {Raid.Session.World.LoanersDestroyed} · Duplicados {Duplicates} · Finalizaciones {Session.SessionFinishedEvents}"
                + $"\nRevision P1 {before[0]} → {Profiles[0].Revision} · P2 {before[1]} → {Profiles[1].Revision}"
                + "\nIDs conservados: " + string.Join(" · ", Profiles.SelectMany((p, index) => p.ReadStashSnapshot(p.ProfileId).Select(item => $"P{index + 1} {ShortId(item.LootInstanceId)}")));
            Debug.Log("RAID_END_TO_END_FINISHED " + LastSummary);
            begin.gameObject.SetActive(true); begin.GetComponentInChildren<Text>().text = "Nueva raid desde el stash resultante";
            for (int i = 0; i < 2; i++) SelectOwnedLoadout(i);
            Refresh();
        }
        private void Update()
        {
            foreach (var interactor in Interactors)
            {
                var inventory = interactor.GetComponent<QusapRaidInventory>();
                if (!inventory.CanOperate || Session.Status != QusapRaidSessionStatus.Running)
                {
                    if (inventory.Receiver.IsEliminated && inventory.State.Status == QusapRaidInventoryStatus.Settled) inventory.Receiver.RetireFromRaid();
                    if (interactor.enabled)
                    { interactor.Close(); interactor.enabled = false; interactor.GetComponent<QusapLootInteractionInputBridge>().enabled = false;
                        interactor.Sensor.Configure(Array.Empty<QusapWorldLootContainerView>()); interactor.Sensor.Scan(); }
                    continue;
                }
                interactor.PrepareFrame(Time.deltaTime);
            }
            foreach (var interactor in Interactors) if (interactor.isActiveAndEnabled) interactor.SubmitPrepared();
            foreach (var interactor in Interactors) if (interactor.isActiveAndEnabled) interactor.Observe();
            CompactInventoryHud(); Refresh();
        }
        private void CompactInventoryHud()
        {
            if (InventoryHud?.Canvas == null || InventoryHud.Canvas.name == "EndToEnd_Inventory") return;
            InventoryHud.Canvas.name = "EndToEnd_Inventory";
            foreach (Transform panel in InventoryHud.Canvas.transform) panel.localScale = new(.62f, .62f, 1);
            foreach (var button in InventoryHud.GetComponentsInChildren<Button>())
                if (button.name == "Saquear" || button.name == "Equipar espada" || button.name == "Contenedor / siguiente objeto") button.gameObject.SetActive(false);
            foreach (var text in InventoryHud.GetComponentsInChildren<Text>()) if (text.name == "Container") text.gameObject.SetActive(false);
        }
        private void BuildHud()
        {
            var root = new GameObject("EndToEndCanvas", typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster)); root.transform.SetParent(transform, false);
            Canvas = root.GetComponent<Canvas>(); Canvas.renderMode = RenderMode.ScreenSpaceOverlay; Canvas.sortingOrder = 20;
            var scaler = root.GetComponent<CanvasScaler>(); scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize; scaler.referenceResolution = new(1280, 720);
            heading = Label(root.transform, "RaidHeader", 220, 5, 840, 55, 18);
            summary = Label(root.transform, "RaidSummary", 210, 560, 860, 145, 16);
            lootPanels = new Text[2];
            selectionButtons = new Button[4];
            for (int p = 0; p < 2; p++)
            {
                int index = p; lootPanels[p] = Label(root.transform, "Loot_P" + (p + 1), 220 + p * 420, 65, 410, 115, 14);
                MakeButton(root.transform, "Seleccionar_P" + (p + 1), "Siguiente", 220 + p * 420, 182, 90, () => Queue(index, new(false, 1)));
                MakeButton(root.transform, "Tomar_P" + (p + 1), "Tomar stack", 315 + p * 420, 182, 140, () => Queue(index, new(false, takeStack: true)));
                MakeButton(root.transform, "Cerrar_P" + (p + 1), "Cerrar", 460 + p * 420, 182, 85, () => Queue(index, new(false, close: true)));
                selectionButtons[p * 2] = MakeButton(root.transform, "Arma_P" + (p + 1), "Arma / préstamo", 220 + p * 420, 217, 190, () => ChooseWeapon(index));
                selectionButtons[p * 2 + 1] = MakeButton(root.transform, "Consumibles_P" + (p + 1), "Consumibles: elegir", 415 + p * 420, 217, 200, () => ChooseConsumables(index));
            }
            begin = MakeButton(root.transform, "IniciarRaid", "Iniciar raid · loadout del stash", 450, 517, 380, () => BeginRaid());
        }
        private void Queue(int index, QusapLootInteractionCommand command)
        { if (index < Interactors.Length) Interactors[index].QueueCommand(command); }
        private static Text Label(Transform parent, string name, float x, float y, float w, float h, int size)
        {
            var root = new GameObject(name, typeof(RectTransform), typeof(Image)); root.transform.SetParent(parent, false);
            var rect = root.GetComponent<RectTransform>(); rect.anchorMin = rect.anchorMax = new(0, 1); rect.pivot = new(0, 1); rect.anchoredPosition = new(x, -y); rect.sizeDelta = new(w, h);
            root.GetComponent<Image>().color = new(.025f, .04f, .065f, .90f);
            var go = new GameObject("Text", typeof(RectTransform), typeof(Text)); go.transform.SetParent(root.transform, false);
            var text = go.GetComponent<Text>(); text.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf"); text.fontSize = size; text.color = Color.white; text.raycastTarget = false;
            text.rectTransform.anchorMin = Vector2.zero; text.rectTransform.anchorMax = Vector2.one; text.rectTransform.offsetMin = new(6, 3); text.rectTransform.offsetMax = new(-6, -3); return text;
        }
        private static Button MakeButton(Transform parent, string name, string caption, float x, float y, float w, UnityEngine.Events.UnityAction action)
        {
            var text = Label(parent, name, x, y, w, 30, 15); text.text = caption; text.alignment = TextAnchor.MiddleCenter;
            var button = text.transform.parent.gameObject.AddComponent<Button>(); button.onClick.AddListener(action); return button;
        }
        private void Refresh()
        {
            if (heading == null) return;
            heading.text = $"RAID END-TO-END · Partida {RaidNumber} · {Session?.Status.ToString() ?? "Despliegue pendiente"}\n{Context?.RaidId ?? "Dos perfiles en memoria"} · {EvidenceAction}";
            summary.text = LastSummary + (RaidNumber > 0 && !finished ? $"\nStash actual P1 {Profiles[0].ReadStashSnapshot(Profiles[0].ProfileId).Count} · P2 {Profiles[1].ReadStashSnapshot(Profiles[1].ProfileId).Count} · Duplicados {Duplicates}" : "");
            for (int i = 0; i < lootPanels.Length; i++)
            {
                selectionButtons[i * 2].interactable = selectionButtons[i * 2 + 1].interactable = Context == null || finished;
                selectionButtons[i * 2].GetComponentInChildren<Text>().text = selectedWeapons[i] == null ? "Arma: RaidLoaner" : "Arma: propia del stash";
                selectionButtons[i * 2 + 1].GetComponentInChildren<Text>().text = "Consumibles: " + selectedStacks[i].Length + " stacks";
                if (i >= Interactors.Length) { lootPanels[i].text = $"P{i + 1}: mochila 6 slots · bolsillo 1\nArma propia del stash o RaidLoaner.\nObjetos conservados: {Profiles[i].ReadStashSnapshot(Profiles[i].ProfileId).Count}"; continue; }
                var interactor = Interactors[i]; var target = interactor.Sensor.Target; var snapshot = interactor.Observed ?? target?.Snapshot;
                var extraction = Raid.Participants[i].GetComponent<QusapRaidExtraction>();
                lootPanels[i].text = $"P{i + 1} · {interactor.BindingLabel}\n{target?.SpawnPointId ?? "Acercarse a un contenedor"} · hold {interactor.Progress.Elapsed:0.00}/0.45 s\n"
                    + (snapshot == null ? "" : string.Join("\n", snapshot.Items.Select(item => (interactor.SelectedInstanceId == item.LootInstanceId ? "> " : "  ") + item.DisplayName + " x" + item.Quantity)))
                    + (interactor.SelectedInstanceId == null ? "" : "\nID: " + ShortId(interactor.SelectedInstanceId))
                    + $"\nExtracción {extraction.Status} · {extraction.Countdown.Remaining:0.0}s · {interactor.LastReply?.Result.ToString() ?? "--"}";
            }
        }
        private static string ShortId(string id)
        { int boundary = id.IndexOf('/'); return (boundary > 8 ? id.Substring(0, 8) + "…" + id.Substring(boundary) : id).Replace("/world/", "/").Replace("/roll/", "/r"); }
        private void OnDestroy() { Context?.Dispose(); }
    }
}
