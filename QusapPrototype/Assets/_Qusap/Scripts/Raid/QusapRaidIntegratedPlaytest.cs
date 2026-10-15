using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEngine;
using UnityEngine.InputSystem;

namespace Qusap
{
    // Scene composition only: ownership, time, combat, settlements and victory stay in the approved authorities.
    [DefaultExecutionOrder(1100)]
    public sealed class QusapRaidIntegratedPlaytest : MonoBehaviour
    {
        [SerializeField] private GameObject roundTemplate;
        [SerializeField] private QusapRaidLootCatalog catalog;
        [SerializeField] private bool reducedPreset;
        [SerializeField] private ulong lootSeed = 606;
        private GameObject round;
        private QusapWeaponMatchBootstrap native;
        private ulong nextNativeId = 1;
        private readonly string[] weapons = new string[2];
        private readonly HashSet<string>[] consumables = { new(), new() };
        private readonly bool[] ready = new bool[2];
        private readonly int[] selection = new int[2];
        private readonly int[] inventorySelection = new int[2];
        private int inventoryStickDirection;
        private readonly List<QusapWorldLootContainerView> views = new();
        public QusapPersistentStashRepository[] Profiles { get; private set; }
        public QusapSharedRaidContext Context { get; private set; }
        public QusapRaidBootstrap Raid { get; private set; }
        public QusapRaidHazard Hazard => Raid != null ? Raid.GetComponent<QusapRaidHazardDriver>().Hazard : null;
        public QusapLocalWorldLootAuthority LootOwner { get; private set; }
        public QusapLootInteractionAuthority LootAuthority { get; private set; }
        public QusapLootInteractor[] Interactors { get; private set; } = Array.Empty<QusapLootInteractor>();
        public IReadOnlyList<QusapWorldLootContainerView> Containers => views.AsReadOnly();
        public int RaidNumber { get; private set; }
        public int DiagnosticSeedGrants { get; private set; }
        public bool DetailsVisible { get; set; }
        public QusapRaidIntegratedParryDiagnostic ParryDiagnostic { get; private set; }
        public int DetailPlayer { get; set; }
        public string Message { get; private set; } = "Preparación diagnóstica · perfiles sólo en memoria";
        public string LastResults { get; private set; } = "Todavía no hay resultados.";
        public bool ReducedPreset => reducedPreset;
        public bool Preparing => Context == null;
        public bool Running => Context != null && !Context.Session.IsFinished;
        public bool CanPrepareNext => Context != null && Context.Session.IsFinished
            && Context.Status == QusapDeploymentStatus.Resolved && Profiles.All(p => !p.HasActiveDeployment);
        public QusapRaidTimingSettings TimingSettings => reducedPreset ? new(120, 20) : new();
        public QusapRaidHazardSettings HazardSettings => reducedPreset
            ? new(new(-12, -.5f, 12, 11), new[] {
                new QusapRaidHazardStage(60,new(-10,-.5f,10,10),10,5),
                new QusapRaidHazardStage(75,new(-7,-.5f,7,9),12,5),
                new QusapRaidHazardStage(90,new(-4,-.5f,4,8),15,5),
                new QusapRaidHazardStage(105,new(-2.5f,-.5f,2.5f,6),20,5)},1)
            : QusapRaidHazardSettings.Normal();

        public void Configure(GameObject template, QusapRaidLootCatalog definitions)
        { roundTemplate = template; catalog = definitions; }
        private void Start() { PrepareNext(); }
        public bool SetPreset(bool reduced)
        { if (!Preparing || ready.Any(r => r)) return false; reducedPreset = reduced; return true; }
        public bool IsReady(int player) => player >= 0 && player < 2 && ready[player];
        public string WeaponSelection(int player) => weapons[player];
        public bool ConsumableSelected(int player, string id) => consumables[player].Contains(id);
        public int PreparationSelection(int player) => selection[player];
        public int InventorySelection(int player) => inventorySelection[player];
        public QusapLootSnapshot[] Stash(int player) => Profiles[player].ReadStashSnapshot(Profiles[player].ProfileId).ToArray();
        public QusapLootSnapshot[] LoadoutChoices(int player) => Stash(player)
            .Where(i => i.Category == QusapLootCategory.Weapon || i.Category == QusapLootCategory.Consumable).ToArray();
        public bool ToggleLoadout(int player, string instanceId)
        {
            if (!Preparing || player < 0 || player > 1 || ready[player]) return false;
            if (instanceId == null) { weapons[player] = null; return true; }
            var item = LoadoutChoices(player).FirstOrDefault(i => i.LootInstanceId == instanceId);
            if (item.LootInstanceId == null) return false;
            if (item.Category == QusapLootCategory.Weapon)
                weapons[player] = weapons[player] == instanceId ? null : instanceId;
            else if (!consumables[player].Remove(instanceId))
            { if (consumables[player].Count >= 2) return false; consumables[player].Add(instanceId); }
            return true;
        }
        public bool SetReady(int player, bool value)
        { if (!Preparing || player < 0 || player > 1) return false; ready[player] = value; return true; }

        public bool PrepareNext()
        {
            if (Context != null && !CanPrepareNext) return false;
            if (native != null) nextNativeId = Math.Max(nextNativeId, native.NextWeaponInstanceId);
            Context?.Dispose(); Context = null; LootOwner = null; LootAuthority = null; Interactors = Array.Empty<QusapLootInteractor>();
            if (round != null) { round.SetActive(false); Destroy(round); }
            round = Instantiate(roundTemplate); round.name = "IntegratedRound_" + (RaidNumber + 1);
            Raid = round.GetComponentInChildren<QusapRaidBootstrap>(true);
            var positions = new[] { new Vector3(-10.5f, 1.05f, 0), new Vector3(10.5f, 1.05f, 0) };
            for (int i = 0; i < 2; i++) Raid.Participants[i].transform.position = positions[i];
            round.GetComponentInChildren<QusapCombatArenaController>(true).Configure(
                Raid.Participants[0].GetComponent<QusapInputReader>(), Raid.Participants[1].GetComponent<QusapInputReader>(), positions[0], positions[1], true);
            native = round.GetComponentInChildren<QusapWeaponMatchBootstrap>(true);
            ulong reserved = Profiles == null ? nextNativeId : Math.Max(nextNativeId, Profiles.Max(p => p.NextNativeWeaponInstanceId));
            native.ReserveWeaponInstanceIdsThrough(reserved - 1);
            round.SetActive(true);
            foreach (var actor in Raid.Participants) actor.GetComponent<QusapInputReader>().SetExternalGameplayInputBlocked(true);
            if (!native.TryInitialize()) throw new InvalidOperationException("Native weapon bootstrap rejected");
            native.SetPickupRequestFilter(equipment => Running &&
                (!DetailsVisible || DetailPlayer != (equipment == Raid.Participants[0].GetComponent<QusapWeaponEquipment>() ? 0 : 1)) &&
                QusapIntegratedLootInput.InteractHeld(equipment.GetComponent<QusapInputReader>().LocalPlayerSlot));
            if (Profiles == null) CreateProfilesOnce();
            foreach (var profile in Profiles) profile.ObserveNativeAllocator(() => native.NextWeaponInstanceId);
            views.Clear(); views.AddRange(round.GetComponentsInChildren<QusapWorldLootContainerView>());
            for (int i = 0; i < 2; i++)
            { weapons[i] = null; consumables[i].Clear(); ready[i] = false; selection[i] = 0; inventorySelection[i] = 0; }
            Message = "Preparación: elige espada propia o préstamo y hasta dos consumibles. Ambos deben confirmar.";
            if (ParryDiagnostic == null) ParryDiagnostic = gameObject.AddComponent<QusapRaidIntegratedParryDiagnostic>();
            ParryDiagnostic.Bind(Raid.Participants.ToArray());
            return true;
        }
        private void CreateProfilesOnce()
        {
            Profiles = Enumerable.Range(0, 2).Select(i => new QusapPersistentStashRepository(
                Path.Combine(Path.GetTempPath(), "QusapIntegratedMemoryOnly", Guid.NewGuid().ToString("N")),
                catalog.Definitions, "playtest/P" + (i + 1) + "/" + Guid.NewGuid().ToString("N"), new QusapMemoryProfileFiles())).ToArray();
            foreach (var profile in Profiles)
            {
                profile.ObserveNativeAllocator(() => native.NextWeaponInstanceId);
                using var seed = new QusapRaidSession("diagnostic-seed-" + Guid.NewGuid().ToString("N"), profile);
                var inventory = seed.Register("diagnostic-seed", profile.ProfileId);
                foreach (var id in new[] { "raid_basic_sword", "raid_minor_heal", "raid_major_heal" })
                {
                    if (!catalog.TryResolve(id, out var definition)) throw new InvalidOperationException("Missing approved seed definition " + id);
                    var weapon = definition.Category == QusapLootCategory.Weapon ? native.CreateRaidLootWeapon(definition.WeaponDefinitionId) : null;
                    var item = seed.World.Create(definition, "Preparación diagnóstica única por perfil", weapon,
                        definition.Category == QusapLootCategory.Consumable ? Math.Min(2, definition.MaxStack) : 1);
                    if (inventory.TryPickup(item.LootInstanceId) != QusapLootResult.Success) throw new InvalidOperationException("Seed pickup rejected");
                }
                if (seed.SettleExtraction(inventory) != QusapLootResult.Success) throw new InvalidOperationException("Seed settlement rejected");
                DiagnosticSeedGrants++;
            }
        }
        public bool BeginRaid()
        {
            if (!Preparing || ready.Any(r => !r)) return false;
            var context = new QusapSharedRaidContext(TimingSettings, "P1", "P2");
            for (int i = 0; i < 2; i++)
            {
                var actor = Raid.Participants[i]; actor.Configure("P" + (i + 1), Profiles[i].ProfileId);
                var deployment = new QusapRaidDeploymentAuthority(Profiles[i], catalog, actor.ParticipantId,
                    actor.GetComponent<QusapWeaponEquipment>(), d => native.CreateRaidLootWeapon(d.WeaponDefinitionId));
                var selected = Stash(i).Where(item => consumables[i].Contains(item.LootInstanceId))
                    .Select(item => new QusapLoadoutStack(item.LootInstanceId, item.Quantity)).ToArray();
                if (!deployment.PrepareLoadout(weapons[i], selected) || !context.Add(deployment))
                { Message = deployment.LastResult + " / " + context.LastResult; deployment.Dispose(); context.Dispose(); Array.Fill(ready, false); return false; }
            }
            if (!context.CommitDeployment() || !context.StartRunning())
            { Message = context.LastResult; context.Dispose(); Array.Fill(ready, false); return false; }
            Context = context; Raid.BeginSharedDeploymentSession(context); RaidNumber++;
            Raid.GetComponent<QusapRaidHazardDriver>().Bind(HazardSettings);
            BindLoot();
            context.Session.DeathSettled += RegisterDeathView;
            context.Session.Competition.SessionFinished += result =>
            {
                LastResults = "Incursión " + RaidNumber + " · " + context.RaidId + "\n" + result.Reason + " · Winner: " + (result.WinnerId ?? "ninguno")
                    + "\n" + string.Join("\n", result.Participants.Select(p => p.ParticipantId + ": " + p.Outcome))
                    + "\n" + string.Join("\n", Profiles.Select((p, i) => "P" + (i + 1) + " · stash " + Stash(i).Length + " · Cuerno " + (p.HasTriumphHorn ? "desbloqueado" : "bloqueado")));
                DetailsVisible = true; Message = "Incursión terminada. Revisa resultados y stash antes de preparar la siguiente.";
                Debug.Log("INTEGRATED_RAID_FINISHED " + LastResults);
            };
            foreach (var actor in Raid.Participants) actor.GetComponent<QusapInputReader>().SetExternalGameplayInputBlocked(false);
            DetailsVisible = false; Message = "Incursión " + RaidNumber + " · recogida y bolsillo manuales · sin reposición inicial";
            return true;
        }
        private QusapLootInteractionRejection ValidateAccess(string participant, string spawn)
            => Interactors.FirstOrDefault(i => i.PlayerId == participant)?.Validate(views.FirstOrDefault(v => v != null && v.SpawnPointId == spawn))
                ?? QusapLootInteractionRejection.Incapacitated;
        private void BindLoot()
        {
            LootOwner = new(Context.Session, catalog, lootSeed, d => native.CreateRaidLootWeapon(d.WeaponDefinitionId),
                (participant, spawn) => ValidateAccess(participant, spawn) == QusapLootInteractionRejection.None);
            LootAuthority = new(LootOwner, ValidateAccess);
            foreach (var view in views)
            {
                if (LootAuthority.RegisterSpawn(view.SpawnPointId, view.Table) != QusapWorldLootResult.Success)
                    throw new InvalidOperationException("Invalid integrated loot spawn " + view.SpawnPointId);
                view.Bind(LootAuthority);
            }
            Interactors = Raid.Participants.Select(player =>
            {
                var sensor = player.gameObject.AddComponent<QusapLootProximitySensor>(); sensor.Configure(views);
                var commands = player.gameObject.AddComponent<QusapIntegratedLootInput>(); commands.Configure(player.GetComponent<QusapInputReader>().LocalPlayerSlot);
                int index = player == Raid.Participants[0] ? 0 : 1;
                commands.SetAvailable(() => !DetailsVisible || DetailPlayer != index);
                var interactor = player.gameObject.AddComponent<QusapLootInteractor>();
                interactor.Configure(player.ParticipantId, "integrated/" + player.ParticipantId, sensor, commands, LootAuthority, Context.Session.Competition);
                return interactor;
            }).ToArray();
        }
        private void RegisterDeathView(QusapDeathLootRecord record)
        {
            // The bootstrap has already created its approved physical death-container projection.
            if (LootOwner.RegisterDeathContainer(record) != QusapWorldLootResult.Success) return;
            var source = Raid.ContainerViews.Last();
            var view = source.gameObject.AddComponent<QusapWorldLootContainerView>();
            view.Configure(record.ContainerId, null); view.RequireOpeningHold(true); view.Bind(LootAuthority); views.Add(view);
        }
        public QusapLootResult InventoryAction(int player, string action, string id = null)
        {
            if (!Running || player < 0 || player > 1) return QusapLootResult.Blocked;
            var actor = Raid.Participants[player];
            var result = action switch {
                "pocket" => actor.TryMoveToSecurePocket(id), "backpack" => actor.TryMoveToBackpack(id),
                "consume" => actor.TryConsume(id), "equip" => actor.WeaponAdapter.TryEquipWeaponFromBackpack(id),
                "store" => actor.TryStoreEquippedWeapon(), _ => QusapLootResult.Blocked };
            Message = actor.ParticipantId + " · " + action + ": " + result; return result;
        }
        private void QuickAction(int player, string action)
        {
            if (!Running) return;
            var inventory = Raid.Participants[player].ReadInventorySnapshot();
            var candidates = inventory.Backpack.Where(i => i.HasValue).Select(i => i.Value);
            var item = action == "consume" ? candidates.FirstOrDefault(i => i.Category == QusapLootCategory.Consumable)
                : candidates.FirstOrDefault(i => !i.RaidLoaner && catalog.TryResolve(i.DefinitionId, out var definition) && definition.CanEnterSecurePocket);
            InventoryAction(player, action, item.LootInstanceId);
        }
        private void PreparationInput(int player, int delta, bool toggle, bool confirm)
        {
            var choices = LoadoutChoices(player);
            if (delta != 0) selection[player] = ((selection[player] + delta) % (choices.Length + 1) + choices.Length + 1) % (choices.Length + 1);
            if (toggle) ToggleLoadout(player, selection[player] == 0 ? null : choices[selection[player] - 1].LootInstanceId);
            if (confirm) SetReady(player, !ready[player]);
        }
        private void DetailInput(Keyboard key, Gamepad pad)
        {
            if (!DetailsVisible || !Running) { inventoryStickDirection = 0; return; }
            int player = DetailPlayer; Interactors[player].Close();
            var backpack = Raid.Participants[player].ReadInventorySnapshot().Backpack;
            int direction = player == 1 && pad != null ? (pad.rightStick.x.ReadValue() > .6f ? 1 : pad.rightStick.x.ReadValue() < -.6f ? -1 : 0) : 0;
            int delta = player == 0 && key != null ? (key.downArrowKey.wasPressedThisFrame ? 1 : 0) - (key.upArrowKey.wasPressedThisFrame ? 1 : 0)
                : direction != inventoryStickDirection ? direction : 0;
            inventoryStickDirection = direction;
            inventorySelection[player] = ((inventorySelection[player] + delta) % backpack.Count + backpack.Count) % backpack.Count;
            var item = backpack[inventorySelection[player]];
            bool use = player == 0 ? key?.enterKey.wasPressedThisFrame == true : pad?.rightStickButton.wasPressedThisFrame == true;
            if (use && item.HasValue) InventoryAction(player, item.Value.Category == QusapLootCategory.Weapon ? "equip"
                : item.Value.Category == QusapLootCategory.Consumable ? "consume" : "pocket", item.Value.LootInstanceId);
            bool store = player == 0 ? key?.zKey.wasPressedThisFrame == true : pad?.rightStick.up.wasPressedThisFrame == true;
            if (store) InventoryAction(player, "store");
            bool unpack = player == 0 ? key?.xKey.wasPressedThisFrame == true : pad?.rightStick.down.wasPressedThisFrame == true;
            if (unpack) InventoryAction(player, "backpack", Raid.Participants[player].ReadInventorySnapshot().SecurePocket?.LootInstanceId);
        }
        private void Update()
        {
            var key = Keyboard.current; var pad = Gamepad.all.Count > 0 ? Gamepad.all[0] : null;
            if (Profiles == null) return;
            if (Preparing)
            {
                if (key?.rKey.wasPressedThisFrame == true || pad?.selectButton.wasPressedThisFrame == true) SetPreset(!reducedPreset);
                if (key != null) PreparationInput(0, (key.downArrowKey.wasPressedThisFrame ? 1 : 0) - (key.upArrowKey.wasPressedThisFrame ? 1 : 0), key.enterKey.wasPressedThisFrame, key.pKey.wasPressedThisFrame);
                if (pad != null) PreparationInput(1, (pad.dpad.down.wasPressedThisFrame ? 1 : 0) - (pad.dpad.up.wasPressedThisFrame ? 1 : 0), pad.buttonSouth.wasPressedThisFrame, pad.startButton.wasPressedThisFrame);
                if (ready.All(r => r)) BeginRaid();
                return;
            }
            if (key?.tabKey.wasPressedThisFrame == true) { DetailsVisible = !DetailsVisible; DetailPlayer = 0; }
            if (pad?.startButton.wasPressedThisFrame == true) { DetailsVisible = !DetailsVisible; DetailPlayer = 1; }
            if (!Running)
            {
                if (CanPrepareNext && (key?.nKey.wasPressedThisFrame == true || pad?.buttonSouth.wasPressedThisFrame == true)) PrepareNext();
                return;
            }
            DetailInput(key, pad);
            if (key?.bKey.wasPressedThisFrame == true) QuickAction(0, "pocket");
            if (key?.hKey.wasPressedThisFrame == true) QuickAction(0, "consume");
            if (pad?.leftStickButton.wasPressedThisFrame == true) QuickAction(1, "pocket");
            if (pad?.selectButton.wasPressedThisFrame == true) QuickAction(1, "consume");
            foreach (var interactor in Interactors) interactor.PrepareFrame(Time.deltaTime);
            foreach (var interactor in Interactors) interactor.SubmitPrepared();
            foreach (var interactor in Interactors) interactor.Observe();
        }
        private void OnDestroy() { Context?.Dispose(); }
    }
}
