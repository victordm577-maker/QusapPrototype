using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace Qusap
{
    public readonly struct QusapLoadoutStack
    {
        public QusapLoadoutStack(string id, int quantity) { InstanceId = id; Quantity = quantity; }
        public string InstanceId { get; }
        public int Quantity { get; }
    }
    public sealed class QusapLoadoutLimits
    {
        public int MaximumWeapons { get; }
        public int MaximumConsumableStacks { get; }
        public QusapLoadoutLimits(int maximumWeapons = 1, int maximumConsumableStacks = 2)
        {
            if (maximumWeapons < 0 || maximumWeapons > 1 || maximumConsumableStacks < 0 || maximumConsumableStacks > 6)
                throw new ArgumentOutOfRangeException(nameof(maximumConsumableStacks));
            MaximumWeapons = maximumWeapons; MaximumConsumableStacks = maximumConsumableStacks;
        }
    }

    // One deployment authority coordinates the existing stash, loot ledger and native equipment.
    // Selection stores IDs only. The manifest stores recovery DTOs, never another live inventory.
    public sealed class QusapRaidDeploymentAuthority : IDisposable
    {
        private readonly QusapPersistentStashRepository stash;
        private readonly QusapRaidLootCatalog catalog;
        private readonly QusapWeaponEquipment equipment;
        private readonly Func<QusapLootDefinition, QusapWeaponInstance> createNative;
        private readonly string participant;
        private readonly QusapLoadoutLimits limits;
        private QusapLoadoutStack[] consumables = Array.Empty<QusapLoadoutStack>();
        private bool selected, busy;
        public string SelectedWeaponId { get; private set; }
        public IReadOnlyList<QusapLoadoutStack> SelectedConsumables => Array.AsReadOnly(consumables);
        public string DeploymentId { get; private set; }
        public string RaidId => Session?.World.RaidId;
        public QusapDeploymentStatus Status { get; private set; }
        public QusapRaidSession Session { get; private set; }
        public QusapRaidInventoryState Inventory { get; private set; }
        public QusapRaidWeaponAdapter WeaponAdapter { get; private set; }
        public int Rejections { get; private set; }
        public string LastResult { get; private set; } = "None";
        public int LoanersCreated => Session?.World.LoanersCreated ?? 0;
        public int LoanersDestroyed => Session?.World.LoanersDestroyed ?? 0;
        public QusapRaidDeploymentAuthority(QusapPersistentStashRepository repository, QusapRaidLootCatalog definitions,
            string participantId, QusapWeaponEquipment nativeEquipment,
            Func<QusapLootDefinition, QusapWeaponInstance> nativeFactory, QusapLoadoutLimits configuration = null)
        {
            stash = repository ?? throw new ArgumentNullException(nameof(repository));
            catalog = definitions ?? throw new ArgumentNullException(nameof(definitions));
            if (catalog.Validate().Count != 0 || string.IsNullOrWhiteSpace(participantId)) throw new ArgumentException("Invalid deployment configuration");
            participant = participantId; equipment = nativeEquipment ?? throw new ArgumentNullException(nameof(nativeEquipment));
            createNative = nativeFactory ?? throw new ArgumentNullException(nameof(nativeFactory)); limits = configuration ?? new QusapLoadoutLimits();
        }
        private bool Reject(string reason) { Rejections++; LastResult = reason; return false; }
        private bool Validate(string weapon, QusapLoadoutStack[] stacks, out QusapLootInstance[] items)
        {
            items = Array.Empty<QusapLootInstance>();
            if (stacks == null || stacks.Length > limits.MaximumConsumableStacks || (!string.IsNullOrEmpty(weapon) && limits.MaximumWeapons == 0)) return Reject("Loadout limit");
            var ids = stacks.Select(s => s.InstanceId).Concat(string.IsNullOrEmpty(weapon) ? Array.Empty<string>() : new[] { weapon }).ToArray();
            if (ids.Any(string.IsNullOrWhiteSpace) || ids.Distinct(StringComparer.Ordinal).Count() != ids.Length) return Reject("Duplicate or invalid selection");
            var owned = stash.ReadOwnedInstances();
            foreach (string id in ids)
            {
                var item = owned.SingleOrDefault(i => i.LootInstanceId == id);
                if (item == null || item.Location != QusapLootLocation.Stash || item.HolderId != stash.ProfileId || !item.CanPersist)
                    return Reject("Instance is not owned by this stash");
                if (!catalog.TryResolve(item.DefinitionId, out var definition) || !ReferenceEquals(definition, item.Definition)) return Reject("Unknown DefinitionId");
                if (id == weapon ? definition.Category != QusapLootCategory.Weapon : definition.Category != QusapLootCategory.Consumable)
                    return Reject("Forbidden category");
                // This milestone transfers whole stacks. Splitting would require a new instance, outside its scope.
                if (id != weapon && stacks.Single(s => s.InstanceId == id).Quantity != item.Quantity) return Reject("Select the complete existing stack quantity");
                if (item.Weapon != null && (!item.Weapon.IsFree || item.Weapon.IsRetired)) return Reject("Native weapon unavailable");
            }
            items = ids.Select(id => owned.Single(i => i.LootInstanceId == id)).ToArray(); return true;
        }
        public bool PrepareLoadout(string weaponId = null, params QusapLoadoutStack[] stacks)
        {
            if (busy || Status != QusapDeploymentStatus.None || stash.HasActiveDeployment) return Reject("Deployment already active; loadout locked");
            if (!Validate(weaponId, stacks, out _)) return false;
            SelectedWeaponId = weaponId; consumables = (QusapLoadoutStack[])stacks.Clone(); selected = true;
            LastResult = "Prepared selection; stash and disk unchanged"; return true;
        }
        public bool CommitDeployment()
        {
            if (busy || !selected || Status != QusapDeploymentStatus.None || stash.HasActiveDeployment) return Reject("Deployment unavailable");
            busy = true;
            try
            {
                if (!Validate(SelectedWeaponId, consumables, out var items)) return false;
                if (!equipment.TryInitialize() || equipment.HasWeapon) return Reject("Equipment must be initialized and empty");
                var receiver = equipment.GetComponent<QusapHitReceiver>();
                if (receiver != null && (receiver.IsHealthDepleted || receiver.IsGameplayRetired)) return Reject("Participant unavailable");
                string deployment = Guid.NewGuid().ToString("N"), raidId = Guid.NewGuid().ToString("N");
                var manifest = new QusapActiveDeploymentManifest { DeploymentId = deployment, RaidId = raidId, State = QusapDeploymentStatus.Prepared,
                    Items = items.Select(i => QusapProfileItem.From(new QusapLootSnapshot(i))).ToArray(), WeaponInstanceId = SelectedWeaponId,
                    CreatedAtUtc = DateTime.UtcNow.ToString("O", CultureInfo.InvariantCulture) };
                // Allocate a loaner before committing, so a rejecting native factory cannot strand a durable withdrawal.
                QusapWeaponInstance loaner = null; QusapLootDefinition basic = null;
                if (string.IsNullOrEmpty(SelectedWeaponId))
                {
                    if (!catalog.TryResolve("raid_basic_sword", out basic)) return Reject("Missing canonical basic sword");
                    loaner = createNative(basic);
                    if (loaner == null || !loaner.IsFree || loaner.IsRetired || loaner.Definition.Id != basic.WeaponDefinitionId) return Reject("Invalid native loaner");
                }
                var session = new QusapRaidSession(raidId, stash);
                if (!session.World.CanImport(items)) { session.Dispose(); return Reject("Ledger import rejected"); }
                if (!stash.TryCommitDeployment(manifest, items.Select(i => i.LootInstanceId).ToArray(), out var withdrawn))
                { session.Dispose(); return Reject("Save rejected; stash and selection intact: " + stash.LastResult); }
                DeploymentId = deployment; Session = session; Inventory = session.Register(participant, stash.ProfileId);
                Inventory.DeploymentLocked = true; WeaponAdapter = new QusapRaidWeaponAdapter(session.World, Inventory, equipment, catalog.Definitions);
                int slot = 0;
                lock (session.World.Gate)
                {
                    foreach (var item in withdrawn)
                    {
                        session.World.Import(item);
                        session.World.Move(item, QusapLootLocation.Backpack, participant, slot++);
                    }
                    var sword = string.IsNullOrEmpty(SelectedWeaponId) ? session.World.CreateLoaner(basic, loaner) : session.World.Get(SelectedWeaponId);
                    WeaponAdapter.Restore(sword.Weapon);
                    session.World.Move(sword, QusapLootLocation.Equipped, participant);
                    // Compact consumable slots without merging either selected stack.
                    slot = 0; foreach (var item in withdrawn.Where(i => i.Weapon == null)) session.World.Move(item, QusapLootLocation.Backpack, participant, slot++);
                }
                Session.ExtractionSettled += Extracted; Session.DeathSettled += Eliminated;
                Status = QusapDeploymentStatus.Prepared; LastResult = "Committed atomically; exact instances delivered after disk commit";
                session.World.Notify(); return true;
            }
            finally { busy = false; }
        }
        public bool StartRunning()
        {
            if (busy || Status != QusapDeploymentStatus.Prepared) return Reject("Not Prepared");
            busy = true;
            try
            {
                if (!stash.TryMarkDeploymentRunning(DeploymentId)) return Reject("Running save rejected");
                Status = QusapDeploymentStatus.Running; Inventory.DeploymentLocked = false; LastResult = "Running; loadout locked"; return true;
            }
            finally { busy = false; }
        }
        public bool CancelDeployment()
        {
            if (busy) return Reject("Transaction in progress");
            if (Status == QusapDeploymentStatus.Resolved) return true;
            if (Status == QusapDeploymentStatus.None) { selected = false; consumables = Array.Empty<QusapLoadoutStack>(); SelectedWeaponId = null; return true; }
            if (Status != QusapDeploymentStatus.Prepared) return Reject("Cannot cancel Running");
            busy = true;
            try
            {
                lock (Session.World.Gate)
                {
                    var all = Session.World.Instances.Where(i => i.HolderId == participant && (i.Location == QusapLootLocation.Backpack || i.Location == QusapLootLocation.Equipped)).ToArray();
                    var sword = all.Single(i => i.Weapon != null);
                    if (!Session.World.Reserve(all)) return Reject("Reserved");
                    try
                    {
                        if (!WeaponAdapter.TryRelease(sword.Weapon)) return Reject("Equipment rejected cancellation");
                        if (!stash.TryCommitSettlement(RaidId + "/cancel/" + DeploymentId, stash.ProfileId, all.Where(i => i.CanPersist).ToArray()))
                        { WeaponAdapter.Restore(sword.Weapon); return Reject("Cancel save rejected; exact instances retained"); }
                        foreach (var item in all) Session.World.Move(item, item.CanPersist ? QusapLootLocation.Stash : QusapLootLocation.Consumed, item.CanPersist ? stash.ProfileId : null);
                        Inventory.MarkSettled(); Resolve();
                    }
                    finally { Session.World.Release(all); }
                }
                Session.World.Notify(); return true;
            }
            finally { busy = false; }
        }
        private void Extracted(QusapRaidInventoryState inventory) { if (inventory == Inventory) Resolve(); }
        private void Eliminated(QusapDeathLootRecord record) { if (record.ParticipantId == participant) Resolve(false); }
        private void Resolve(bool discardLoaners = true)
        {
            Status = QusapDeploymentStatus.Resolved; stash.ReleaseVolatileDeployment(DeploymentId);
            if (discardLoaners) Session.World.DiscardLoaners(true);
            LastResult = "Resolved; manifest cleared transactionally";
        }
        public void Dispose()
        {
            if (Session != null) { Session.ExtractionSettled -= Extracted; Session.DeathSettled -= Eliminated; }
            Session?.Dispose(); WeaponAdapter?.Dispose();
        }
    }
}
