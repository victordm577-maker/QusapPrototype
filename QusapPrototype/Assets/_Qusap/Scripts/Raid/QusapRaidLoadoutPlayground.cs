using System;
using System.IO;
using System.Linq;
using UnityEngine;

namespace Qusap
{
    // Diagnostic scene only. Normal Play also receives an isolated temporary directory in every build.
    [DefaultExecutionOrder(400)]
    public sealed class QusapRaidLoadoutPlayground : MonoBehaviour
    {
        [SerializeField] private QusapRaidBootstrap raid;
        [SerializeField] private QusapRaidLootCatalog catalog;
        private string ownedDirectory;
        public QusapRaidBootstrap Raid => raid;
        public QusapRaidLootCatalog Catalog => catalog;
        public QusapPersistentStashRepository Profile => raid.GetComponent<QusapLocalProfilePersistence>().Repository;
        public QusapRaidDeploymentAuthority Deployment { get; private set; }
        public string Action { get; set; } = "1 equipo propio | 2 loaner | P preparar | C confirmar | ENTER Running | X cancelar | H consumir";
        public string WeaponSelection { get; private set; }
        public QusapLoadoutStack[] ConsumableSelection { get; private set; } = Array.Empty<QusapLoadoutStack>();
        public void Configure(QusapRaidBootstrap bootstrap, QusapRaidLootCatalog definitions) { raid = bootstrap; catalog = definitions; }
        private void Awake()
        {
            // Injection is scoped to this playground and works without editor compilation symbols.
            if (string.IsNullOrWhiteSpace(raid.GetComponent<QusapLocalProfilePersistence>().IsolatedPlaygroundDirectory))
            {
                string[] args = Environment.GetCommandLineArgs(); int index = Array.IndexOf(args, "-qusapProfileDirectory");
                ownedDirectory = index >= 0 && index + 1 < args.Length ? args[index + 1]
                    : Path.Combine(Path.GetTempPath(), "QusapRaidLoadoutPlayground", Guid.NewGuid().ToString("N"));
#if UNITY_EDITOR
                if (!string.IsNullOrWhiteSpace(QusapLocalProfilePersistence.IsolatedStorageDirectory)) ownedDirectory = QusapLocalProfilePersistence.IsolatedStorageDirectory;
#endif
                raid.GetComponent<QusapLocalProfilePersistence>().ConfigureIsolatedPlayground(ownedDirectory);
            }
        }
        private void Start()
        {
            var native = FindAnyObjectByType<QusapWeaponMatchBootstrap>();
            native.TryInitialize();
            foreach (var player in raid.Participants) player.GetComponent<QusapInputReader>()?.SetExternalGameplayInputBlocked(true);
            if (Profile.ReadStashSnapshot(Profile.ProfileId).Count == 0 && !Profile.HasActiveDeployment)
            {
                // Reproducible seed data for this diagnostic scene, never for a production profile.
                using var seed = new QusapRaidSession("diagnostic-seed-" + Guid.NewGuid().ToString("N"), Profile);
                var state = seed.Register("seed", Profile.ProfileId);
                foreach (string id in new[] { "raid_purple_sword", "raid_minor_heal", "raid_major_heal" })
                {
                    if (!catalog.TryResolve(id, out var definition)) throw new InvalidOperationException("Approved catalog missing seed item");
                    var weapon = definition.Category == QusapLootCategory.Weapon ? native.CreateRaidLootWeapon(definition.WeaponDefinitionId) : null;
                    var item = seed.World.Create(definition, "isolated diagnostic seed", weapon, definition.MaxStack);
                    state.TryPickup(item.LootInstanceId);
                }
                if (seed.SettleExtraction(state) != QusapLootResult.Success) throw new InvalidOperationException("Isolated seed save failed");
            }
            Deployment = new QusapRaidDeploymentAuthority(Profile, catalog, raid.Participants[0].ParticipantId,
                raid.Participants[0].GetComponent<QusapWeaponEquipment>(), d => native.CreateRaidLootWeapon(d.WeaponDefinitionId));
            SelectOwned();
        }
        public void SelectOwned()
        {
            if (Deployment != null && Deployment.Status != QusapDeploymentStatus.None) { Action = "Loadout locked after Commit"; return; }
            var stash = Profile.ReadStashSnapshot(Profile.ProfileId);
            WeaponSelection = stash.FirstOrDefault(i => i.DefinitionId == "raid_qusap_sword_purple").LootInstanceId;
            ConsumableSelection = stash.Where(i => i.DefinitionId == "raid_healing").Take(1).Select(i => new QusapLoadoutStack(i.LootInstanceId, i.Quantity)).ToArray();
        }
        public void SelectLoaner()
        {
            if (Deployment != null && Deployment.Status != QusapDeploymentStatus.None) { Action = "Loadout locked after Commit"; return; }
            WeaponSelection = null; ConsumableSelection = Array.Empty<QusapLoadoutStack>();
        }
        public void ToggleConsumable(string definitionId)
        {
            if (Deployment.Status != QusapDeploymentStatus.None) { Action = "Loadout locked after Commit"; return; }
            var item = Profile.ReadStashSnapshot(Profile.ProfileId).FirstOrDefault(i => i.DefinitionId == definitionId && i.Category == QusapLootCategory.Consumable);
            if (item.LootInstanceId == null) return;
            ConsumableSelection = ConsumableSelection.Any(i => i.InstanceId == item.LootInstanceId)
                ? ConsumableSelection.Where(i => i.InstanceId != item.LootInstanceId).ToArray()
                : ConsumableSelection.Append(new QusapLoadoutStack(item.LootInstanceId, item.Quantity)).Take(2).ToArray();
        }
        public bool Prepare() { bool ok = Deployment.PrepareLoadout(WeaponSelection, ConsumableSelection); Action = Deployment.LastResult; return ok; }
        public bool Commit()
        {
            bool ok = Deployment.CommitDeployment(); if (ok) raid.BeginDeploymentSession(Deployment);
            Action = Deployment.LastResult; return ok;
        }
        public bool Run()
        {
            bool ok = Deployment.StartRunning();
            if (ok) ok = raid.GetComponent<QusapRaidSessionObserver>().StartSession();
            if (ok) foreach (var player in raid.Participants) player.GetComponent<QusapInputReader>()?.SetExternalGameplayInputBlocked(false);
            Action = Deployment.LastResult; return ok;
        }
        private void OnDestroy() { Deployment?.Dispose(); }
    }
}
