using System;
using System.IO;
using System.Linq;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace Qusap.Tests
{
    public sealed class QusapRaidLoadoutDeploymentEditModeTests
    {
        private string directory;
        private QusapRaidLootCatalog catalog;
        private QusapPersistentStashRepository repo;
        private QusapRaidDeploymentAuthority deployment;
        private GameObject root;
        private QusapWeaponEquipment equipment;
        private ulong nativeSequence;
        private const string Profile = "anonymous-loadout-fixture";
        private string Main => Path.Combine(directory, QusapProfileFileStore.FileName);
        [SetUp] public void Setup()
        {
            directory = Path.Combine(Path.GetTempPath(), "QusapLoadoutTests", Guid.NewGuid().ToString("N"));
            catalog = AssetDatabase.LoadAssetAtPath<QusapRaidLootCatalog>("Assets/_Qusap/Settings/RaidLootCatalog/RaidLootCatalog.asset");
            repo = new QusapPersistentStashRepository(directory, catalog.Definitions, Profile);
            root = new GameObject("LoadoutTestOwner"); root.SetActive(false);
            var combat = root.AddComponent<QusapCombatController>(); equipment = root.AddComponent<QusapWeaponEquipment>();
            Assert.That(equipment.TryInitialize(combat), Is.True); Authority();
        }
        private void Authority(QusapLoadoutLimits limits = null)
        { deployment = new QusapRaidDeploymentAuthority(repo, catalog, "P1", equipment, d => new QusapWeaponInstance(++nativeSequence, d.WeaponDefinition), limits); }
        private QusapLootInstance Seed(string id, int quantity = 1)
        {
            Assert.That(catalog.TryResolve(id, out var definition), Is.True);
            using var session = new QusapRaidSession("seed-" + Guid.NewGuid().ToString("N"), repo); var inventory = session.Register("seed", Profile);
            var native = definition.Category == QusapLootCategory.Weapon ? new QusapWeaponInstance(++nativeSequence, definition.WeaponDefinition) : null;
            var item = session.World.Create(definition, "anonymous test origin", native, quantity); inventory.TryPickup(item.LootInstanceId);
            Assert.That(session.SettleExtraction(inventory), Is.EqualTo(QusapLootResult.Success)); return item;
        }
        private void Deploy(QusapLootInstance weapon = null, params QusapLootInstance[] stacks)
        {
            Assert.That(deployment.PrepareLoadout(weapon?.LootInstanceId, stacks.Select(i => new QusapLoadoutStack(i.LootInstanceId, i.Quantity)).ToArray()), Is.True);
            Assert.That(deployment.CommitDeployment(), Is.True, deployment.LastResult);
        }
        [TearDown] public void Cleanup()
        {
            deployment.Dispose(); UnityEngine.Object.DestroyImmediate(root);
            string parent = Path.GetFullPath(Path.Combine(Path.GetTempPath(), "QusapLoadoutTests")) + Path.DirectorySeparatorChar;
            if (!Path.GetFullPath(directory).StartsWith(parent, StringComparison.OrdinalIgnoreCase) || !Guid.TryParseExact(Path.GetFileName(directory), "N", out _)) throw new InvalidOperationException("Unsafe cleanup");
            if (Directory.Exists(directory)) Directory.Delete(directory, true);
        }
        [Test] public void PrepareIsReadOnlyAndOptionalWeaponIsValid()
        {
            var heal = Seed("raid_minor_heal", 2); string bytes = File.ReadAllText(Main); long rev = repo.Revision; int writes = repo.Storage.Writes;
            Assert.That(deployment.PrepareLoadout(null, new QusapLoadoutStack(heal.LootInstanceId,2)), Is.True);
            Assert.That(repo.Revision, Is.EqualTo(rev)); Assert.That(repo.Storage.Writes, Is.EqualTo(writes)); Assert.That(File.ReadAllText(Main), Is.EqualTo(bytes)); Assert.That(repo.ReadActiveDeploymentSnapshot(), Is.Null);
        }
        [Test] public void WholeWeaponAndTwoStacksMoveOnlyAfterAtomicSaveAndKeepIdentity()
        {
            var sword = Seed("raid_purple_sword"); var heal = Seed("raid_minor_heal",2); var major = Seed("raid_major_heal"); long revision = repo.Revision;
            Deploy(sword,heal,major);
            Assert.That(repo.Revision, Is.EqualTo(revision+1)); Assert.That(repo.ReadStashSnapshot(Profile), Is.Empty);
            Assert.That(deployment.Session.World.Get(sword.LootInstanceId), Is.SameAs(sword)); Assert.That(equipment.EquippedWeapon, Is.SameAs(sword.Weapon));
            Assert.That(deployment.Inventory.ReadInventorySnapshot().Backpack.Where(i=>i.HasValue).Select(i=>i.Value.Quantity), Is.EqualTo(new[]{2,1}));
            Assert.That(deployment.Inventory.ReadInventorySnapshot().Backpack.Count, Is.EqualTo(6)); Assert.That(deployment.Inventory.ReadInventorySnapshot().SecurePocket, Is.Null);
            Assert.That(repo.ReadActiveDeploymentSnapshot().State, Is.EqualTo(QusapDeploymentStatus.Prepared)); Assert.That(deployment.LoanersCreated, Is.Zero);
        }
        [Test] public void CancellationBeforeCommitLeavesRevisionWritesBytesAndInstancesUnchanged()
        {
            var sword = Seed("raid_purple_sword"); var heal = Seed("raid_minor_heal", 2);
            string bytes = File.ReadAllText(Main); long revision = repo.Revision; int writes = repo.Storage.Writes;
            Assert.That(deployment.PrepareLoadout(sword.LootInstanceId, new QusapLoadoutStack(heal.LootInstanceId, 2)), Is.True);
            Assert.That(deployment.CancelDeployment(), Is.True); Assert.That(deployment.CancelDeployment(), Is.True);
            Assert.That(repo.Revision, Is.EqualTo(revision)); Assert.That(repo.Storage.Writes, Is.EqualTo(writes));
            Assert.That(File.ReadAllText(Main), Is.EqualTo(bytes)); Assert.That(repo.ReadActiveDeploymentSnapshot(), Is.Null);
            Assert.That(repo.ReadStashSnapshot(Profile).Select(i => i.LootInstanceId), Is.EquivalentTo(new[] { sword.LootInstanceId, heal.LootInstanceId }));
            Assert.That(sword.Location, Is.EqualTo(QusapLootLocation.Stash)); Assert.That(heal.Quantity, Is.EqualTo(2));
            Assert.That(deployment.Session, Is.Null); Assert.That(equipment.HasWeapon, Is.False);
        }
        [Test] public void CatalogAliasesNormalizeImmediatelyAndPreserveInstanceIdentityThroughDeploymentAndReload()
        {
            var sword = Seed("raid_basic_sword"); var heal = Seed("raid_minor_heal", 2);
            Assert.That(sword.DefinitionId, Is.EqualTo("raid_qusap_sword_blue")); Assert.That(heal.DefinitionId, Is.EqualTo("raid_healing"));
            Deploy(sword, heal);
            var manifest = repo.ReadActiveDeploymentSnapshot();
            Assert.That(manifest.Items.Single(i => i.InstanceId == sword.LootInstanceId).DefinitionId, Is.EqualTo(sword.DefinitionId));
            Assert.That(manifest.Items.Single(i => i.InstanceId == heal.LootInstanceId).DefinitionId, Is.EqualTo(heal.DefinitionId));
            Assert.That(deployment.Session.World.Get(sword.LootInstanceId), Is.SameAs(sword));
            Assert.That(deployment.Session.World.Get(heal.LootInstanceId), Is.SameAs(heal));
            Assert.That(deployment.StartRunning(), Is.True);
            Assert.That(deployment.Session.SettleExtraction(deployment.Inventory, deployment.WeaponAdapter), Is.EqualTo(QusapLootResult.Success));
            var loaded = new QusapPersistentStashRepository(directory, catalog.Definitions);
            Assert.That(loaded.ReadStashSnapshot(Profile).Single(i => i.LootInstanceId == sword.LootInstanceId).DefinitionId, Is.EqualTo(sword.DefinitionId));
            Assert.That(loaded.ReadStashSnapshot(Profile).Single(i => i.LootInstanceId == heal.LootInstanceId).DefinitionId, Is.EqualTo(heal.DefinitionId));
            Assert.That(loaded.Storage.Writes, Is.Zero);
        }
        [TestCase("raid_fragment")] [TestCase("raid_rare_relic")] [TestCase("raid_basic_sword")]
        public void ForbiddenConsumableCategoriesAreRejected(string definition)
        { var item=Seed(definition); Assert.That(deployment.PrepareLoadout(null,new QusapLoadoutStack(item.LootInstanceId,1)),Is.False); Assert.That(repo.ReadStashSnapshot(Profile).Count,Is.EqualTo(1)); }
        [TestCase("raid_fragment")] [TestCase("raid_minor_heal")] [TestCase("raid_rare_relic")]
        public void OnlyWeaponCategoryCanOccupyWeaponSelection(string definition)
        { var item=Seed(definition); Assert.That(deployment.PrepareLoadout(item.LootInstanceId),Is.False); }
        [Test] public void MoreThanTwoStacksAndDuplicateIdsAreRejected()
        {
            var a=Seed("raid_minor_heal");var b=Seed("raid_minor_heal");var c=Seed("raid_major_heal");
            Assert.That(deployment.PrepareLoadout(null,new QusapLoadoutStack(a.LootInstanceId,1),new QusapLoadoutStack(b.LootInstanceId,1),new QusapLoadoutStack(c.LootInstanceId,1)),Is.False);
            Assert.That(deployment.PrepareLoadout(null,new QusapLoadoutStack(a.LootInstanceId,1),new QusapLoadoutStack(a.LootInstanceId,1)),Is.False);
        }
        [TestCase(0)] [TestCase(1)] [TestCase(3)] public void PartialZeroAndExcessQuantityAreRejected(int quantity)
        {var item=Seed("raid_minor_heal",2);Assert.That(deployment.PrepareLoadout(null,new QusapLoadoutStack(item.LootInstanceId,quantity)),Is.False);}
        [Test] public void UnknownInstanceCannotDeployAndConfiguredLimitsAreApplied()
        {
            Assert.That(deployment.PrepareLoadout("not-owned"),Is.False); var sword=Seed("raid_purple_sword");var heal=Seed("raid_minor_heal"); Authority(new QusapLoadoutLimits(0,0));
            Assert.That(deployment.PrepareLoadout(sword.LootInstanceId),Is.False);Assert.That(deployment.PrepareLoadout(null,new QusapLoadoutStack(heal.LootInstanceId,1)),Is.False);
        }
        [Test] public void UnknownDefinitionIsRejectedWithoutChangingCatalog()
        {
            var unknown=ScriptableObject.CreateInstance<QusapLootDefinition>();unknown.Configure("anonymous-unknown", "Unknown", QusapLootCategory.Consumable,true,QusapConsumableType.Healing,25);
            var expanded=catalog.Definitions.Concat(new[]{unknown}).ToArray();var other=new QusapPersistentStashRepository(directory,expanded,Profile);
            using var session=new QusapRaidSession("unknown",other);var inventory=session.Register("seed",Profile);var item=session.World.Create(unknown,"anonymous");inventory.TryPickup(item.LootInstanceId);session.SettleExtraction(inventory);
            using var authority=new QusapRaidDeploymentAuthority(other,catalog,"P1",equipment,d=>new QusapWeaponInstance(++nativeSequence,d.WeaponDefinition));
            Assert.That(authority.PrepareLoadout(null,new QusapLoadoutStack(item.LootInstanceId,1)),Is.False);UnityEngine.Object.DestroyImmediate(unknown);
        }
        private sealed class FailFiles : IQusapProfileFiles
        {
            public bool Fail;public Action OnWrite;private readonly QusapProfileFiles real=new();
            public bool Exists(string p)=>real.Exists(p);public string Read(string p)=>real.Read(p);public void CreateDirectory(string p)=>real.CreateDirectory(p);
            public void WriteClosedAndFlushed(string p,string contents){OnWrite?.Invoke();if(Fail)throw new IOException("Injected isolated write failure");real.WriteClosedAndFlushed(p,contents);}
            public void Replace(string t,string m,string b)=>real.Replace(t,m,b);public void Move(string t,string m)=>real.Move(t,m);
        }
        [Test] public void FailedCommitKeepsStashSelectionIdsAndBytesAndDoesNotStartRaid()
        {
            var sword=Seed("raid_purple_sword");string bytes=File.ReadAllText(Main);var files=new FailFiles{Fail=true};repo=new QusapPersistentStashRepository(directory,catalog.Definitions,Profile,files);Authority();
            deployment.PrepareLoadout(sword.LootInstanceId);Assert.That(deployment.CommitDeployment(),Is.False);Assert.That(deployment.Session,Is.Null);Assert.That(equipment.HasWeapon,Is.False);
            Assert.That(repo.ReadStashSnapshot(Profile).Single().LootInstanceId,Is.EqualTo(sword.LootInstanceId));Assert.That(deployment.SelectedWeaponId,Is.EqualTo(sword.LootInstanceId));Assert.That(File.ReadAllText(Main),Is.EqualTo(bytes));
            files.Fail=false;Assert.That(deployment.CommitDeployment(),Is.True,deployment.LastResult);
        }
        [Test] public void DiskCallbackSeesNoRaidDeliveryAndCannotReenterCommit()
        {
            var sword=Seed("raid_purple_sword");var files=new FailFiles();repo=new QusapPersistentStashRepository(directory,catalog.Definitions,Profile,files);Authority();deployment.PrepareLoadout(sword.LootInstanceId);
            files.OnWrite=()=>{Assert.That(equipment.HasWeapon,Is.False);Assert.That(deployment.Session,Is.Null);Assert.That(repo.ReadStashSnapshot(Profile).Count,Is.EqualTo(1));Assert.That(deployment.CommitDeployment(),Is.False);};
            Assert.That(deployment.CommitDeployment(),Is.True);Assert.That(repo.ReadStashSnapshot(Profile),Is.Empty);
        }
        [Test] public void CancellationReturnsExactInstancesOnceAndBlocksPreparedOperations()
        {
            var sword=Seed("raid_purple_sword");var heal=Seed("raid_minor_heal",2);Deploy(sword,heal);long revision=repo.Revision;
            Assert.That(deployment.Inventory.TryConsume(heal.LootInstanceId,_=>25),Is.EqualTo(QusapLootResult.Blocked));Assert.That(deployment.CancelDeployment(),Is.True);
            Assert.That(repo.Revision,Is.EqualTo(revision+1));Assert.That(repo.ReadActiveDeploymentSnapshot(),Is.Null);Assert.That(repo.ReadStashSnapshot(Profile).Count,Is.EqualTo(2));Assert.That(sword.Location,Is.EqualTo(QusapLootLocation.Stash));
            Assert.That(deployment.CancelDeployment(),Is.True);Assert.That(repo.Revision,Is.EqualTo(revision+1));Assert.That(equipment.HasWeapon,Is.False);
        }
        [Test] public void DoubleDeploymentAndLoadoutMutationAfterRunningAreBlocked()
        {
            Deploy();Assert.That(deployment.CommitDeployment(),Is.False);using var second=new QusapRaidDeploymentAuthority(repo,catalog,"P2",equipment,d=>new QusapWeaponInstance(++nativeSequence,d.WeaponDefinition));
            Assert.That(second.PrepareLoadout(),Is.False);Assert.That(deployment.StartRunning(),Is.True);Assert.That(deployment.PrepareLoadout(),Is.False);Assert.That(deployment.CancelDeployment(),Is.False);
        }
        [TestCase(0)] [TestCase(1)] [TestCase(2)] public void ExtractionReturnsExactWeaponAndOnlyRemainingConsumableQuantity(int consumed)
        {
            var sword=Seed("raid_purple_sword");var heal=Seed("raid_minor_heal",2);Deploy(sword,heal);deployment.StartRunning();
            for(int i=0;i<consumed;i++)Assert.That(deployment.Inventory.TryConsume(heal.LootInstanceId,_=>25),Is.EqualTo(QusapLootResult.Success));
            long revision=repo.Revision;Assert.That(deployment.Session.SettleExtraction(deployment.Inventory,deployment.WeaponAdapter),Is.EqualTo(QusapLootResult.Success));
            Assert.That(repo.Revision,Is.EqualTo(revision+1));Assert.That(repo.ReadActiveDeploymentSnapshot(),Is.Null);Assert.That(repo.ReadStashSnapshot(Profile).Single(i=>i.Category==QusapLootCategory.Weapon).LootInstanceId,Is.EqualTo(sword.LootInstanceId));
            var stacks=repo.ReadStashSnapshot(Profile).Where(i=>i.Category==QusapLootCategory.Consumable).ToArray();Assert.That(stacks.Length,Is.EqualTo(consumed==2?0:1));if(consumed<2){Assert.That(stacks[0].Quantity,Is.EqualTo(2-consumed));Assert.That(stacks[0].LootInstanceId,Is.EqualTo(heal.LootInstanceId));}
            var reloaded = new QusapPersistentStashRepository(directory, catalog.Definitions);
            var persisted = reloaded.ReadStashSnapshot(Profile).Where(i => i.Category == QusapLootCategory.Consumable).ToArray();
            Assert.That(persisted.Length, Is.EqualTo(consumed == 2 ? 0 : 1));
            if (consumed < 2) { Assert.That(persisted[0].LootInstanceId, Is.EqualTo(heal.LootInstanceId)); Assert.That(persisted[0].Quantity, Is.EqualTo(2 - consumed)); }
            Assert.That(reloaded.Storage.Writes, Is.Zero);
        }
        [Test] public void DeathDoesNotRestoreLoadoutAndRivalExtractsTheSameInstances()
        {
            var sword=Seed("raid_purple_sword");var heal=Seed("raid_minor_heal",2);Deploy(sword,heal);deployment.StartRunning();var state=deployment.Inventory;state.BlockOperations();
            Assert.That(deployment.Session.SettleDeath(state,deployment.WeaponAdapter),Is.EqualTo(QusapLootResult.Success));Assert.That(repo.ReadStashSnapshot(Profile),Is.Empty);Assert.That(repo.ReadActiveDeploymentSnapshot(),Is.Null);
            string container=deployment.Session.Containers.Single().ContainerId;var rival=deployment.Session.Register("P2","anonymous-rival");
            foreach(var item in new[]{sword,heal})Assert.That(rival.TryTakeFromDeathContainer(container,item.LootInstanceId),Is.EqualTo(QusapLootResult.Success));
            Assert.That(deployment.Session.SettleExtraction(rival),Is.EqualTo(QusapLootResult.Success));Assert.That(repo.ReadStashSnapshot("anonymous-rival").Select(i=>i.LootInstanceId),Is.EquivalentTo(new[]{sword.LootInstanceId,heal.LootInstanceId}));Assert.That(deployment.Session.World.ReadContainer(container),Is.Empty);
        }
        [Test] public void LoanerCannotPersistEnterPocketOrProduceRevisionsAndRetiresOnExtraction()
        {
            Deploy();var item=deployment.Session.World.FindWeapon(equipment.EquippedWeapon);Assert.That(item.RaidLoaner,Is.True);Assert.That(item.DefinitionId,Is.EqualTo("raid_qusap_sword_blue"));
            Assert.That(new QusapMemoryStashRepository().TryCommitSettlement("bad",Profile,new[]{item}),Is.False);Assert.Throws<InvalidOperationException>(()=>QusapProfileItem.From(new QusapLootSnapshot(item)));
            deployment.StartRunning();deployment.WeaponAdapter.TryStoreEquippedWeapon();Assert.That(deployment.Inventory.TryMoveToSecurePocket(item.LootInstanceId),Is.EqualTo(QusapLootResult.Ineligible));deployment.WeaponAdapter.TryEquipWeaponFromBackpack(item.LootInstanceId);
            Assert.That(deployment.Session.SettleExtraction(deployment.Inventory,deployment.WeaponAdapter),Is.EqualTo(QusapLootResult.Success));Assert.That(repo.Revision,Is.Zero);Assert.That(repo.Storage.Writes,Is.Zero);Assert.That(repo.ReadStashSnapshot(Profile),Is.Empty);Assert.That(item.Weapon.IsRetired,Is.True);Assert.That(deployment.LoanersDestroyed,Is.EqualTo(1));Assert.That(File.Exists(Main),Is.False);
        }
        [Test] public void LoanerCanBeDisarmedDroppedAndReequippedButCannotSurviveSessionFinish()
        {
            Deploy();deployment.StartRunning();var item=deployment.Session.World.FindWeapon(equipment.EquippedWeapon);
            Assert.That(equipment.TryDisarm(equipment.OwnerEntityId+1,out var native,out _),Is.EqualTo(QusapWeaponOperationResult.Success));Assert.That(native,Is.SameAs(item.Weapon));Assert.That(item.Location,Is.EqualTo(QusapLootLocation.World));
            Assert.That(deployment.Inventory.TryPickup(item.LootInstanceId),Is.EqualTo(QusapLootResult.Success));Assert.That(deployment.WeaponAdapter.TryEquipWeaponFromBackpack(item.LootInstanceId),Is.EqualTo(QusapLootResult.Success));
            deployment.Session.FinishSession();Assert.That(item.Weapon.IsRetired,Is.True);Assert.That(equipment.HasWeapon,Is.False);Assert.That(item.Location,Is.EqualTo(QusapLootLocation.Consumed));Assert.That(equipment.TryEquip(item.Weapon,out _),Is.Not.EqualTo(QusapWeaponOperationResult.Success));
        }
        [Test] public void LoanerMayTemporarilyEnterDeathContainerAndIsDestroyedExactlyOnce()
        {
            Deploy();deployment.StartRunning();var item=deployment.Session.World.FindWeapon(equipment.EquippedWeapon);deployment.Inventory.BlockOperations();deployment.Session.SettleDeath(deployment.Inventory,deployment.WeaponAdapter);
            Assert.That(item.Location,Is.EqualTo(QusapLootLocation.DeathContainer));deployment.Session.FinishSession();deployment.Session.FinishSession();Assert.That(item.Location,Is.EqualTo(QusapLootLocation.Consumed));Assert.That(deployment.LoanersDestroyed,Is.EqualTo(1));Assert.That(repo.Revision,Is.Zero);
        }
        [Test] public void TwoRaidsCreateDistinctLoanerInstanceIdsWithoutReusingDefinitions()
        {
            Deploy();string first=deployment.Session.World.FindWeapon(equipment.EquippedWeapon).LootInstanceId;deployment.CancelDeployment();deployment.Dispose();Authority();Deploy();
            Assert.That(deployment.Session.World.FindWeapon(equipment.EquippedWeapon).LootInstanceId,Is.Not.EqualTo(first));Assert.That(catalog.Definitions.Count,Is.EqualTo(9));Assert.That(catalog.Aliases.Count,Is.EqualTo(5));Assert.That(repo.Revision,Is.Zero);
        }
        [TestCase(false)] [TestCase(true)] public void InterruptedPreparedAndRunningRecoverOnceAcrossIndependentRepositories(bool running)
        {
            var sword=Seed("raid_purple_sword");var heal=Seed("raid_minor_heal",2);Deploy(sword,heal);if(running)deployment.StartRunning();long revision=repo.Revision;
            var loaded=new QusapPersistentStashRepository(directory,catalog.Definitions);Assert.That(loaded.Revision,Is.EqualTo(revision+1));Assert.That(loaded.RecoveryReason,Is.EqualTo(running?"InterruptedLocalRaid":"PreparedNotStarted"));
            Assert.That(loaded.ReadActiveDeploymentSnapshot(),Is.Null);Assert.That(loaded.ReadStashSnapshot(Profile).Select(i=>i.LootInstanceId),Is.EquivalentTo(new[]{sword.LootInstanceId,heal.LootInstanceId}));
            string bytes=File.ReadAllText(Main);Assert.That(loaded.TryRecoverInterruptedDeployment(),Is.True);var again=new QusapPersistentStashRepository(directory,catalog.Definitions);Assert.That(again.Revision,Is.EqualTo(revision+1));Assert.That(again.Storage.Writes,Is.Zero);Assert.That(File.ReadAllText(Main),Is.EqualTo(bytes));
        }
        [Test] public void RecoveryWriteFailurePreservesManifestAndRetryRestoresOnce()
        {
            var sword=Seed("raid_purple_sword");Deploy(sword);deployment.StartRunning();long revision=repo.Revision;string bytes=File.ReadAllText(Main);var files=new FailFiles{Fail=true};
            var recovered=new QusapPersistentStashRepository(directory,catalog.Definitions,null,files);Assert.That(recovered.HasActiveDeployment,Is.True);Assert.That(recovered.ReadStashSnapshot(Profile),Is.Empty);Assert.That(File.ReadAllText(Main),Is.EqualTo(bytes));
            files.Fail=false;Assert.That(recovered.TryRecoverInterruptedDeployment(),Is.True);Assert.That(recovered.TryRecoverInterruptedDeployment(),Is.True);Assert.That(recovered.Revision,Is.EqualTo(revision+1));Assert.That(recovered.ReadStashSnapshot(Profile).Count,Is.EqualTo(1));
        }
        [Test] public void V1MigrationPreservesIdsQuantitiesAndBackupUntilValidatedV2Transaction()
        {
            var heal=Seed("raid_minor_heal",2);var codec=new QusapProfileCodec(catalog.Definitions);Assert.That(codec.TryDecode(File.ReadAllText(Main),out var document,out _,out _),Is.True);
            document.Content.SchemaVersion=1;string v1=QusapProfileCodec.Encode(QusapProfileCodec.Seal(document.Content));File.WriteAllText(Main,v1);File.Copy(Main,Main+".bak");
            repo=new QusapPersistentStashRepository(directory,catalog.Definitions);Authority();Assert.That(repo.SchemaVersion,Is.EqualTo(2));Assert.That(repo.Storage.Writes,Is.Zero);Assert.That(File.ReadAllText(Main),Is.EqualTo(v1));Assert.That(repo.ReadStashSnapshot(Profile).Single().Quantity,Is.EqualTo(2));
            Deploy(null,repo.ReadStashSnapshot(Profile).Select(i=>heal).ToArray());Assert.That(File.ReadAllText(Main+".bak"),Is.EqualTo(v1));Assert.That(codec.TryDecode(File.ReadAllText(Main),out var v2,out _,out _),Is.True);Assert.That(v2.Content.SchemaVersion,Is.EqualTo(2));Assert.That(v2.Content.ActiveDeploymentManifest.Items.Single().InstanceId,Is.EqualTo(heal.LootInstanceId));
        }
        [Test] public void CancellationSaveFailurePreservesPreparedOwnershipAndCanRetryOnce()
        {
            var sword=Seed("raid_purple_sword");var files=new FailFiles();repo=new QusapPersistentStashRepository(directory,catalog.Definitions,Profile,files);Authority();Deploy(sword);long revision=repo.Revision;string bytes=File.ReadAllText(Main);files.Fail=true;
            Assert.That(deployment.CancelDeployment(),Is.False);Assert.That(equipment.EquippedWeapon,Is.Not.Null);Assert.That(deployment.Status,Is.EqualTo(QusapDeploymentStatus.Prepared));Assert.That(File.ReadAllText(Main),Is.EqualTo(bytes));Assert.That(repo.Revision,Is.EqualTo(revision));
            files.Fail=false;Assert.That(deployment.CancelDeployment(),Is.True);Assert.That(deployment.CancelDeployment(),Is.True);Assert.That(repo.Revision,Is.EqualTo(revision+1));Assert.That(repo.ReadStashSnapshot(Profile).Count,Is.EqualTo(1));
        }
        [Test] public void RunningSaveFailureKeepsPreparedAndBlocksConsumableCommands()
        {
            var heal=Seed("raid_minor_heal",2);var files=new FailFiles();repo=new QusapPersistentStashRepository(directory,catalog.Definitions,Profile,files);Authority();Deploy(null,heal);long revision=repo.Revision;files.Fail=true;
            Assert.That(deployment.StartRunning(),Is.False);Assert.That(deployment.Status,Is.EqualTo(QusapDeploymentStatus.Prepared));Assert.That(deployment.Inventory.TryConsume(heal.LootInstanceId,_=>25),Is.EqualTo(QusapLootResult.Blocked));Assert.That(repo.Revision,Is.EqualTo(revision));
            files.Fail=false;Assert.That(deployment.StartRunning(),Is.True);
        }
        [Test] public void DeathProtectsOnlyNewPocketLootAndExtractionReturnsNewRaidLoot()
        {
            var sword=Seed("raid_purple_sword");Deploy(sword);deployment.StartRunning();catalog.TryResolve("raid_rare_relic",out var relic);
            var item=deployment.Session.World.Create(relic,"acquired during raid");deployment.Inventory.TryPickup(item.LootInstanceId);deployment.Inventory.TryMoveToSecurePocket(item.LootInstanceId);deployment.Inventory.BlockOperations();
            Assert.That(deployment.Session.SettleDeath(deployment.Inventory,deployment.WeaponAdapter),Is.EqualTo(QusapLootResult.Success));Assert.That(repo.ReadStashSnapshot(Profile).Single().LootInstanceId,Is.EqualTo(item.LootInstanceId));Assert.That(sword.Location,Is.EqualTo(QusapLootLocation.DeathContainer));
        }
        [Test] public void LoanerRaidExtractionPersistsOnlyAcquiredLootWithOneRevision()
        {
            Deploy();deployment.StartRunning();catalog.TryResolve("raid_fragment",out var definition);var loot=deployment.Session.World.Create(definition,"new loot");deployment.Inventory.TryPickup(loot.LootInstanceId);
            Assert.That(deployment.Session.SettleExtraction(deployment.Inventory,deployment.WeaponAdapter),Is.EqualTo(QusapLootResult.Success));Assert.That(repo.Revision,Is.EqualTo(1));Assert.That(repo.ReadStashSnapshot(Profile).Single().LootInstanceId,Is.EqualTo(loot.LootInstanceId));Assert.That(deployment.LoanersDestroyed,Is.EqualTo(1));
        }
        [Test] public void ConsumedLoanerIsOnlyAMemoryTombstoneAndCannotBeRestoredOrPersisted()
        {
            Deploy(); Assert.That(deployment.StartRunning(), Is.True);
            var loaner = deployment.Session.World.FindWeapon(equipment.EquippedWeapon);
            catalog.TryResolve("raid_fragment", out var definition);
            var loot = deployment.Session.World.Create(definition, "isolated terminal-state fixture");
            Assert.That(deployment.Inventory.TryPickup(loot.LootInstanceId), Is.EqualTo(QusapLootResult.Success));
            Assert.That(deployment.Session.SettleExtraction(deployment.Inventory, deployment.WeaponAdapter), Is.EqualTo(QusapLootResult.Success));
            Assert.That(loaner.Location, Is.EqualTo(QusapLootLocation.Consumed)); Assert.That(loaner.HolderId, Is.Null);
            Assert.That(loaner.Weapon.IsRetired, Is.True); Assert.That(equipment.HasWeapon, Is.False);
            var inventory = deployment.Inventory.ReadInventorySnapshot();
            Assert.That(inventory.Backpack.All(i => !i.HasValue), Is.True); Assert.That(inventory.SecurePocket, Is.Null);
            var other = deployment.Session.Register("late-participant", "anonymous-late-participant");
            Assert.That(other.TryPickup(loaner.LootInstanceId), Is.Not.EqualTo(QusapLootResult.Success));
            Assert.That(other.TryMoveToSecurePocket(loaner.LootInstanceId), Is.Not.EqualTo(QusapLootResult.Success));
            Assert.That(deployment.WeaponAdapter.TryEquipWeaponFromBackpack(loaner.LootInstanceId), Is.Not.EqualTo(QusapLootResult.Success));
            Assert.That(equipment.TryEquip(loaner.Weapon, out _), Is.Not.EqualTo(QusapWeaponOperationResult.Success));
            Assert.That(repo.TryCommitSettlement("forbidden-loaner", Profile, new[] { loaner }), Is.False);
            Assert.Throws<InvalidOperationException>(() => QusapProfileItem.From(new QusapLootSnapshot(loaner)));
            string bytes = File.ReadAllText(Main); long revision = repo.Revision; int writes = repo.Storage.Writes;
            Assert.That(bytes, Does.Not.Contain(loaner.LootInstanceId)); Assert.That(bytes, Does.Not.Contain("RaidLoaner"));
            deployment.Dispose();
            var loaded = new QusapPersistentStashRepository(directory, catalog.Definitions);
            Assert.That(loaded.ReadStashSnapshot(Profile).Single().LootInstanceId, Is.EqualTo(loot.LootInstanceId));
            Assert.That(loaded.ReadActiveDeploymentSnapshot(), Is.Null); Assert.That(loaded.Storage.Writes, Is.Zero);
            Assert.That(repo.Revision, Is.EqualTo(revision)); Assert.That(repo.Storage.Writes, Is.EqualTo(writes));
            Assert.That(File.ReadAllText(Main), Is.EqualTo(bytes));
        }
        [Test] public void OldSettlementCannotReinsertAnInstanceCurrentlyOwnedByDeployment()
        {
            var sword=Seed("raid_purple_sword");var heal=Seed("raid_minor_heal",2);Deploy(sword,heal);deployment.StartRunning();long revision=repo.Revision;
            Assert.That(repo.TryCommitSettlement(sword.RaidId+"/extraction/seed",Profile,new[]{sword}),Is.False);
            Assert.That(repo.TryCommitSettlement(heal.RaidId+"/extraction/seed",Profile,new[]{heal}),Is.False);
            Assert.That(repo.ReadStashSnapshot(Profile),Is.Empty);Assert.That(repo.Revision,Is.EqualTo(revision));
        }
    }
}
