using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace Qusap.Tests
{
    public sealed class QusapWorldLootContainersEditModeTests
    {
        private QusapRaidLootCatalog catalog;
        private QusapRaidSession session;
        private QusapLocalWorldLootAuthority authority;
        private QusapRaidInventoryState p1, p2;
        private readonly List<ScriptableObject> temporary = new();
        private ulong native;
        private string directory;
        private QusapLootDefinition Def(string id) { Assert.That(catalog.TryResolve(id, out var d), Is.True); return d; }
        private QusapWorldLootTable Table(string id = "raid_fragment", int quantity = 3, int rolls = 1)
        {
            var weighted = ScriptableObject.CreateInstance<QusapRaidLootTable>(); temporary.Add(weighted);
            weighted.Configure("anonymous-table-" + temporary.Count, new QusapLootTableEntry(id, 100, quantity, quantity));
            var table = ScriptableObject.CreateInstance<QusapWorldLootTable>(); temporary.Add(table); table.Configure(weighted, rolls, rolls); return table;
        }
        private QusapLocalWorldLootAuthority Authority(QusapRaidSession raid, ulong seed = 42)
            => new(raid, catalog, seed, d => new QusapWeaponInstance(++native, d.WeaponDefinition));
        private void Spawn(string id = "cache-a", QusapWorldLootTable table = null)
        { Assert.That(authority.RegisterSpawn(id, table ?? Table()), Is.EqualTo(QusapWorldLootResult.Success)); }
        private QusapWorldLootContainerSnapshot Open(string spawn = "cache-a")
        { Assert.That(authority.RequestOpen(new("P1", spawn)).Confirmed, Is.True); return authority.ReadContainer(spawn); }
        private QusapWorldLootTakeRequest Request(string spawn = "cache-a", int quantity = -1, string player = "P1")
        { var snap = authority.ReadContainer(spawn); var item = snap.Items[0]; return new(player, spawn, item.LootInstanceId, quantity < 0 ? item.Quantity : quantity, snap.Version); }
        private QusapLootInstance Bag(string id = "raid_major_heal", string origin = "anonymous capacity", int quantity = 1)
        { var item = session.World.Create(Def(id), origin, null, quantity); Assert.That(p1.TryPickup(item.LootInstanceId), Is.EqualTo(QusapLootResult.Success)); return item; }
        private void Fill() { while (p1.ReadInventorySnapshot().Backpack.Any(i => !i.HasValue)) Bag(); }
        private static string Signature(QusapWorldLootContainerSnapshot snapshot)
            => snapshot.State + ":" + snapshot.Version + ":" + string.Join(";", snapshot.Items.Select(i => i.LootInstanceId + ":" + i.DefinitionId + ":" + i.Quantity));
        [SetUp] public void Setup()
        {
            catalog = AssetDatabase.LoadAssetAtPath<QusapRaidLootCatalog>("Assets/_Qusap/Settings/RaidLootCatalog/RaidLootCatalog.asset");
            session = new QusapRaidSession("anonymous-raid"); p1 = session.Register("P1", "anonymous-profile"); p2 = session.Register("P2", "anonymous-rival"); authority = Authority(session);
            directory = Path.Combine(Path.GetTempPath(), "QusapWorldLootTests", Guid.NewGuid().ToString("N"));
        }
        [TearDown] public void Cleanup()
        {
            session.Dispose(); foreach (var asset in temporary) UnityEngine.Object.DestroyImmediate(asset); temporary.Clear();
            string parent = Path.GetFullPath(Path.Combine(Path.GetTempPath(), "QusapWorldLootTests")) + Path.DirectorySeparatorChar;
            if (!Path.GetFullPath(directory).StartsWith(parent, StringComparison.OrdinalIgnoreCase) || !Guid.TryParseExact(Path.GetFileName(directory), "N", out _)) throw new InvalidOperationException("Unsafe isolated cleanup");
            if (Directory.Exists(directory)) Directory.Delete(directory, true);
        }
        [Test] public void SameRaidSeedAndSpawnReproduceIdentitiesContentQuantitiesAndRollCount()
        {
            var table = Table(rolls: 3); Spawn(table: table); var first = Open();
            using var other = new QusapRaidSession(session.World.RaidId); other.Register("P1", "anonymous-profile"); var replay = Authority(other);
            replay.RegisterSpawn("cache-a", table); replay.RequestOpen(new("P1", "cache-a"));
            Assert.That(Signature(replay.ReadContainer("cache-a")), Is.EqualTo(Signature(first)));
        }
        [Test] public void DifferentRaidOrSpawnProducesDisjointIdsAndEscapedComponentsAvoidAmbiguity()
        {
            var table = Table(); Spawn(table: table); var a = Open();
            using var other = new QusapRaidSession("different-raid"); other.Register("P1", "anonymous"); var second = Authority(other);
            second.RegisterSpawn("cache-a", table); second.RequestOpen(new("P1", "cache-a"));
            Spawn("cache-b", table); var b = Open("cache-b");
            Assert.That(a.Items[0].LootInstanceId, Is.Not.EqualTo(b.Items[0].LootInstanceId));
            Assert.That(a.Items[0].LootInstanceId, Is.Not.EqualTo(second.ReadContainer("cache-a").Items[0].LootInstanceId));
            Assert.That(QusapWorldLootTable.InstanceId("a/world/b", "c", 0), Is.Not.EqualTo(QusapWorldLootTable.InstanceId("a", "b/world/c", 0)));
        }
        [Test] public void DuplicateSpawnIsRejectedEvenThroughAnotherAuthorityOnTheSameLedger()
        { Spawn(); Assert.That(authority.RegisterSpawn("cache-a", Table()), Is.EqualTo(QusapWorldLootResult.DuplicateSpawn)); Assert.That(Authority(session).RegisterSpawn("cache-a", Table()), Is.EqualTo(QusapWorldLootResult.DuplicateSpawn)); }
        [Test] public void OpeningTwiceOrAfterDepletionNeverRerolls()
        {
            Spawn(); string before = Signature(Open()); Assert.That(authority.RequestOpen(new("P2", "cache-a")).Confirmed, Is.True); Assert.That(Signature(authority.ReadContainer("cache-a")), Is.EqualTo(before));
            Assert.That(authority.RequestTake(Request()).Confirmed, Is.True); var depleted = authority.ReadContainer("cache-a"); Assert.That(depleted.State, Is.EqualTo(QusapWorldLootState.Depleted));
            authority.RequestOpen(new("P1", "cache-a")); Assert.That(Signature(authority.ReadContainer("cache-a")), Is.EqualTo(Signature(depleted))); Assert.That(depleted.GenerationCount, Is.EqualTo(1));
        }
        [TestCase("raid_shell_fragment", "raid_fragment")]
        [TestCase("raid_minor_heal", "raid_healing")]
        [TestCase("raid_basic_sword", "raid_qusap_sword_blue")]
        [TestCase("raid_purple_sword", "raid_qusap_sword_purple")]
        [TestCase("raid_rare_relic", "raid_rarerelic")]
        public void AliasInputsGenerateOnlyCanonicalNonLoanerInstances(string alias, string canonical)
        { Spawn(table: Table(alias, 1)); var item = Open().Items.Single(); Assert.That(item.DefinitionId, Is.EqualTo(canonical)); Assert.That(item.RaidLoaner, Is.False); Assert.That(session.World.LoanersCreated, Is.Zero); }
        [TestCase("raid_basic_sword")] [TestCase("raid_qusap_sword_white")] [TestCase("raid_purple_sword")] [TestCase("raid_rare_relic")]
        public void WeaponsAndRelicsAlwaysHaveOneUnitAndRejectLargerTableQuantities(string id)
        { Spawn(table: Table(id, 1)); Assert.That(Open().Items.Single().Quantity, Is.EqualTo(1)); Assert.That(authority.RegisterSpawn("invalid", Table(id, 2)), Is.EqualTo(QusapWorldLootResult.InvalidSpawn)); }
        [Test] public void WholeTransferMovesTheSameObjectAndRemovesItFromContainer()
        {
            Spawn(); var snap = Open(); var item = session.World.Get(snap.Items[0].LootInstanceId); var reply = authority.RequestTake(Request());
            Assert.That(reply.Confirmed, Is.True); Assert.That(reply.DestinationInstanceId, Is.EqualTo(item.LootInstanceId)); Assert.That(session.World.Get(item.LootInstanceId), Is.SameAs(item));
            Assert.That(item.Location, Is.EqualTo(QusapLootLocation.Backpack)); Assert.That(authority.ReadContainer("cache-a").Items, Is.Empty); Assert.That(p1.ReadInventorySnapshot().SecurePocket, Is.Null);
        }
        [Test] public void FullBackpackLeavesContainerAndInventoryExactlyUnchanged()
        {
            Spawn(); Open(); Fill(); var request = Request(); string before = Signature(authority.ReadContainer("cache-a")); var ids = p1.ReadInventorySnapshot().Backpack.Select(i => i.Value.LootInstanceId).ToArray();
            Assert.That(authority.RequestTake(request).Result, Is.EqualTo(QusapWorldLootResult.Full)); Assert.That(Signature(authority.ReadContainer("cache-a")), Is.EqualTo(before));
            Assert.That(p1.ReadInventorySnapshot().Backpack.Select(i => i.Value.LootInstanceId), Is.EqualTo(ids));
        }
        [Test] public void CompatibleStackIsUsedBeforeEmptySlotAndCanAcceptWhenAllSlotsAreOccupied()
        {
            var existing = Bag("raid_fragment", "World loot"); Spawn(); Open(); Fill();
            var reply = authority.RequestTake(Request()); Assert.That(reply.Confirmed, Is.True); Assert.That(reply.MergedInto, Is.EqualTo(new[] { existing.LootInstanceId }));
            Assert.That(existing.Quantity, Is.EqualTo(4)); Assert.That(p1.ReadInventorySnapshot().Backpack.Count(i => i.HasValue), Is.EqualTo(6)); Assert.That(authority.ReadContainer("cache-a").State, Is.EqualTo(QusapWorldLootState.Depleted));
        }
        [Test] public void ExactQuantityIsAtomicWhenStackRoomIsInsufficientAndNoSlotRemains()
        {
            var stack = Bag("raid_fragment", "World loot", Def("raid_fragment").MaxStack - 1); Spawn(); Open(); Fill(); string before = Signature(authority.ReadContainer("cache-a"));
            Assert.That(authority.RequestTake(Request(quantity: 2)).Result, Is.EqualTo(QusapWorldLootResult.Full)); Assert.That(stack.Quantity, Is.EqualTo(stack.Definition.MaxStack - 1)); Assert.That(Signature(authority.ReadContainer("cache-a")), Is.EqualTo(before));
            Assert.That(authority.RequestTake(Request(quantity: 1)).Confirmed, Is.True); Assert.That(stack.Quantity, Is.EqualTo(stack.Definition.MaxStack)); Assert.That(authority.ReadContainer("cache-a").Items.Single().Quantity, Is.EqualTo(2));
        }
        [Test] public void PartialTransferCreatesDerivedIdentityKeepsOriginAndThenMergesWithoutExtraSlot()
        {
            Spawn(); var initial = Open().Items.Single(); var first = authority.RequestTake(Request(quantity: 1));
            Assert.That(first.Confirmed, Is.True); Assert.That(first.DestinationInstanceId, Is.EqualTo(initial.LootInstanceId + "/part/1")); Assert.That(authority.ReadContainer("cache-a").Items.Single().Quantity, Is.EqualTo(2));
            var second = authority.RequestTake(Request(quantity: 2)); Assert.That(second.Confirmed, Is.True); Assert.That(second.DestinationInstanceId, Is.EqualTo(first.DestinationInstanceId));
            Assert.That(p1.ReadInventorySnapshot().Backpack.Count(i => i.HasValue), Is.EqualTo(1)); Assert.That(session.World.Get(first.DestinationInstanceId).Quantity, Is.EqualTo(3));
            Assert.That(session.World.Get(initial.LootInstanceId).Location, Is.EqualTo(QusapLootLocation.Merged)); Assert.That(authority.DuplicateInstances, Is.Zero);
        }
        [Test] public void TwoPlayersSharingTheSameVersionCanConfirmOnlyOnePartialTransfer()
        {
            Spawn(); Open(); var first = Request(quantity: 1); var second = new QusapWorldLootTakeRequest("P2", first.SpawnPointId, first.InstanceId, first.Quantity, first.ExpectedVersion);
            var replies = new QusapWorldLootReply[2]; using var ready = new Barrier(3);
            var a = Task.Run(() => { ready.SignalAndWait(); replies[0] = authority.RequestTake(first); });
            var b = Task.Run(() => { ready.SignalAndWait(); replies[1] = authority.RequestTake(second); }); ready.SignalAndWait(); Task.WaitAll(a, b);
            Assert.That(replies.Count(r => r.Confirmed), Is.EqualTo(1)); Assert.That(replies.Single(r => !r.Confirmed).Result, Is.EqualTo(QusapWorldLootResult.StaleRequest));
            Assert.That(authority.ReadContainer("cache-a").Items.Single().Quantity, Is.EqualTo(2)); Assert.That(session.World.Instances.Where(i => i.Location == QusapLootLocation.Backpack).Sum(i => i.Quantity), Is.EqualTo(1));
        }
        [Test] public void DuplicateWholeRequestCannotCreateAnotherInstanceOrNegativeQuantity()
        {
            Spawn(); Open(); var request = Request(); Assert.That(authority.RequestTake(request).Confirmed, Is.True); Assert.That(authority.RequestTake(request).Result, Is.EqualTo(QusapWorldLootResult.StaleRequest));
            Assert.That(authority.ConfirmedTransfers, Is.EqualTo(1)); Assert.That(authority.RejectedRequests, Is.EqualTo(1)); Assert.That(session.World.Instances.All(i => i.Quantity >= 0), Is.True);
        }
        [TestCase(0)] [TestCase(-1)] [TestCase(4)] [TestCase(int.MaxValue)]
        public void InvalidQuantitiesDoNotMutateEitherOwner(int quantity)
        { Spawn(); Open(); string before = Signature(authority.ReadContainer("cache-a")); Assert.That(authority.RequestTake(Request(quantity: 0).WithQuantity(quantity)).Result, Is.EqualTo(QusapWorldLootResult.InvalidQuantity)); Assert.That(Signature(authority.ReadContainer("cache-a")), Is.EqualTo(before)); Assert.That(p1.ReadInventorySnapshot().Backpack.All(i => !i.HasValue), Is.True); }
        [Test] public void CancelledOrAbandonedRequestsDoNotModifyOriginDestinationVersionOrStash()
        {
            Spawn(); Open(); var abandoned = Request(); string before = Signature(authority.ReadContainer("cache-a"));
            Assert.That(abandoned.Quantity, Is.EqualTo(3)); Assert.That(Signature(authority.ReadContainer("cache-a")), Is.EqualTo(before));
            Assert.That(authority.RequestTake(abandoned, new CancellationToken(true)).Result, Is.EqualTo(QusapWorldLootResult.Cancelled));
            Assert.That(Signature(authority.ReadContainer("cache-a")), Is.EqualTo(before)); Assert.That(p1.ReadInventorySnapshot().Backpack.All(i => !i.HasValue), Is.True); Assert.That(session.Stash.ReadStashSnapshot(p1.ProfileId), Is.Empty);
        }
        [Test] public void CancelledOpenAndTakeBeforeOpenDoNotGenerateAnything()
        { Spawn(); Assert.That(authority.RequestOpen(new("P1", "cache-a"), new CancellationToken(true)).Result, Is.EqualTo(QusapWorldLootResult.Cancelled)); Assert.That(authority.RequestTake(new("P1", "cache-a", "missing", 1, 0)).Result, Is.EqualTo(QusapWorldLootResult.NotOpened)); Assert.That(authority.ReadContainer("cache-a").GenerationCount, Is.Zero); Assert.That(session.World.Instances, Is.Empty); }
        [Test] public void UnextractedContainerAndBackpackLootNeverReachPersistentProfile()
        {
            session.Dispose(); var repo = new QusapPersistentStashRepository(directory, catalog.Definitions, "anonymous-world-profile"); session = new QusapRaidSession("persistent-world-raid", repo); p1 = session.Register("P1", repo.ProfileId); authority = Authority(session);
            Spawn(table: Table(rolls: 2)); Open(); authority.RequestTake(Request());
            Assert.That(repo.ReadStashSnapshot(repo.ProfileId), Is.Empty); Assert.That(repo.Storage.Writes, Is.Zero); Assert.That(repo.Revision, Is.Zero); Assert.That(File.Exists(repo.Storage.MainPath), Is.False);
            session.Dispose(); var reloaded = new QusapPersistentStashRepository(directory, catalog.Definitions, repo.ProfileId); Assert.That(reloaded.ReadStashSnapshot(repo.ProfileId), Is.Empty); Assert.That(reloaded.ReadActiveDeploymentSnapshot(), Is.Null);
        }
        [Test] public void ExistingExtractionPersistsOnlyTakenCanonicalInstanceWithSameId()
        {
            session.Dispose(); var repo = new QusapPersistentStashRepository(directory, catalog.Definitions, "anonymous-world-profile"); session = new QusapRaidSession("persistent-world-raid", repo); p1 = session.Register("P1", repo.ProfileId); authority = Authority(session);
            Spawn(table: Table("raid_shell_fragment", 2, 2)); var all = Open().Items.ToArray(); authority.RequestTake(Request());
            Assert.That(session.SettleExtraction(p1), Is.EqualTo(QusapLootResult.Success), repo.LastResult); Assert.That(repo.Storage.Writes, Is.EqualTo(1));
            Assert.That(repo.ReadStashSnapshot(repo.ProfileId).Single().LootInstanceId, Is.EqualTo(all[0].LootInstanceId)); Assert.That(repo.ReadStashSnapshot(repo.ProfileId).Single().DefinitionId, Is.EqualTo("raid_fragment"));
            string bytes = File.ReadAllText(repo.Storage.MainPath); Assert.That(bytes, Does.Not.Contain(all[1].LootInstanceId)); Assert.That(bytes, Does.Not.Contain("WorldContainer"));
            var reloaded = new QusapPersistentStashRepository(directory, catalog.Definitions); Assert.That(reloaded.ReadStashSnapshot(repo.ProfileId).Single().LootInstanceId, Is.EqualTo(all[0].LootInstanceId)); Assert.That(reloaded.Storage.Writes, Is.Zero);
        }
        [TestCase("raid_minor_heal", 2)] [TestCase("raid_basic_sword", 1)] [TestCase("raid_purple_sword", 1)] [TestCase("raid_rare_relic", 1)]
        public void ExtractedWorldIdentitiesRoundTripWithoutChangingInstanceOrDefinition(string id, int quantity)
        {
            session.Dispose(); var repo = new QusapPersistentStashRepository(directory, catalog.Definitions, "anonymous-world-profile"); session = new QusapRaidSession("roundtrip-world-raid", repo); p1 = session.Register("P1", repo.ProfileId); authority = Authority(session);
            Spawn(table: Table(id, quantity)); var item = Open().Items.Single(); authority.RequestTake(Request());
            Assert.That(session.SettleExtraction(p1), Is.EqualTo(QusapLootResult.Success), repo.LastResult);
            var reload = new QusapPersistentStashRepository(directory, catalog.Definitions); var restored = reload.ReadStashSnapshot(repo.ProfileId).Single();
            Assert.That(restored.LootInstanceId, Is.EqualTo(item.LootInstanceId)); Assert.That(restored.DefinitionId, Is.EqualTo(item.DefinitionId)); Assert.That(restored.Quantity, Is.EqualTo(quantity)); Assert.That(reload.Storage.Writes, Is.Zero);
        }
        [Test] public void SplitIdentityPersistsWhileItsUntransferredOriginDoesNot()
        {
            session.Dispose(); var repo = new QusapPersistentStashRepository(directory, catalog.Definitions, "anonymous-world-profile"); session = new QusapRaidSession("split-world-raid", repo); p1 = session.Register("P1", repo.ProfileId); authority = Authority(session);
            Spawn(); var source = Open().Items.Single(); var reply = authority.RequestTake(Request(quantity: 1));
            Assert.That(session.SettleExtraction(p1), Is.EqualTo(QusapLootResult.Success), repo.LastResult);
            var reload = new QusapPersistentStashRepository(directory, catalog.Definitions); var saved = reload.ReadStashSnapshot(repo.ProfileId).Single();
            Assert.That(saved.LootInstanceId, Is.EqualTo(reply.DestinationInstanceId)); Assert.That(saved.Quantity, Is.EqualTo(1)); Assert.That(saved.LootInstanceId, Is.Not.EqualTo(source.LootInstanceId)); Assert.That(authority.ReadContainer("cache-a").Items.Single().Quantity, Is.EqualTo(2));
        }
        [Test] public void PreviouslyExtractedWorldWeaponCanDeployAndReturnWithoutIdMigration()
        {
            session.Dispose(); var repo = new QusapPersistentStashRepository(directory, catalog.Definitions, "anonymous-world-profile"); session = new QusapRaidSession("world-to-loadout-raid", repo); p1 = session.Register("P1", repo.ProfileId); authority = Authority(session);
            Spawn(table: Table("raid_basic_sword", 1)); var item = Open().Items.Single(); authority.RequestTake(Request()); Assert.That(session.SettleExtraction(p1), Is.EqualTo(QusapLootResult.Success), repo.LastResult);
            var reloaded = new QusapPersistentStashRepository(directory, catalog.Definitions); var root = new GameObject("AnonymousWorldWeaponOwner"); root.SetActive(false);
            try
            {
                var combat = root.AddComponent<QusapCombatController>(); var equipment = root.AddComponent<QusapWeaponEquipment>(); Assert.That(equipment.TryInitialize(combat), Is.True);
                using var deployment = new QusapRaidDeploymentAuthority(reloaded, catalog, "P1", equipment, d => new QusapWeaponInstance(++native, d.WeaponDefinition));
                Assert.That(deployment.PrepareLoadout(item.LootInstanceId), Is.True); Assert.That(deployment.CommitDeployment(), Is.True, deployment.LastResult); Assert.That(reloaded.ReadActiveDeploymentSnapshot().Items.Single().InstanceId, Is.EqualTo(item.LootInstanceId));
                Assert.That(deployment.StartRunning(), Is.True); Assert.That(deployment.Session.SettleExtraction(deployment.Inventory, deployment.WeaponAdapter), Is.EqualTo(QusapLootResult.Success), reloaded.LastResult);
                Assert.That(reloaded.ReadStashSnapshot(reloaded.ProfileId).Single().LootInstanceId, Is.EqualTo(item.LootInstanceId));
            }
            finally { UnityEngine.Object.DestroyImmediate(root); }
        }
        [TestCase("anonymous-raid/world/cache/roll/-1")]
        [TestCase("anonymous-raid/world/cache/roll/00")]
        [TestCase("anonymous-raid/world/cache/roll/0/part/0")]
        [TestCase("anonymous-raid/world/%63ache/roll/0")]
        [TestCase("wrong-raid/world/cache/roll/0")]
        public void MalformedOrNoncanonicalWorldIdentityIsRejected(string identity)
        { Assert.That(QusapRaidInstanceIdentity.TryValidate("anonymous-raid", identity, out _), Is.False); }
        [Test] public void ManualPocketLootSurvivesDeathWhileBackpackLootEntersDeathContainer()
        {
            Spawn(table: Table("raid_rare_relic", 1, 2)); Open(); var first = authority.RequestTake(Request()); Assert.That(p1.ReadInventorySnapshot().SecurePocket, Is.Null);
            Assert.That(p1.TryMoveToSecurePocket(first.DestinationInstanceId), Is.EqualTo(QusapLootResult.Success)); var second = authority.RequestTake(Request());
            p1.BlockOperations(); Assert.That(session.SettleDeath(p1), Is.EqualTo(QusapLootResult.Success));
            Assert.That(session.Stash.ReadStashSnapshot(p1.ProfileId).Single().LootInstanceId, Is.EqualTo(first.DestinationInstanceId)); Assert.That(session.World.Get(second.DestinationInstanceId).Location, Is.EqualTo(QusapLootLocation.DeathContainer));
        }
        [Test] public void WeaponAndLoanerAreIneligibleForPocketAndContainerGenerationCreatesNoLoaners()
        {
            Spawn(table: Table("raid_basic_sword", 1)); Open(); var sword = authority.RequestTake(Request()); Assert.That(p1.TryMoveToSecurePocket(sword.DestinationInstanceId), Is.EqualTo(QusapLootResult.Ineligible));
            var definition = Def("raid_basic_sword"); var loaner = session.World.CreateLoaner(definition, new QusapWeaponInstance(++native, definition.WeaponDefinition)); p1.TryPickup(loaner.LootInstanceId);
            Assert.That(p1.TryMoveToSecurePocket(loaner.LootInstanceId), Is.EqualTo(QusapLootResult.Ineligible)); Assert.That(authority.ReadContainer("cache-a").GenerationCount, Is.EqualTo(1)); Assert.That(session.World.Get(sword.DestinationInstanceId).RaidLoaner, Is.False);
        }
        [Test] public void SpawnRecipeIsFrozenAndSeedCannotChangeAfterOpening()
        { var table = Table(); Spawn(table: table); table.LootTable.Configure(table.LootTable.LootTableId, new QusapLootTableEntry("raid_major_heal", 100)); Assert.That(Open().Items.Single().DefinitionId, Is.EqualTo("raid_fragment")); Assert.That(authority.TrySetSeedBeforeOpening(100), Is.False); }
        [Test] public void ActorRangeActiveSessionAndContainerOriginAreValidated()
        {
            authority = new QusapLocalWorldLootAuthority(session, catalog, 42, canReach: (actor, spawn) => false); Spawn(); Assert.That(authority.RequestOpen(new("P1", "cache-a")).Result, Is.EqualTo(QusapWorldLootResult.OutOfReach));
            Assert.That(authority.RequestOpen(new("unknown", "cache-a")).Result, Is.EqualTo(QusapWorldLootResult.InvalidParticipant)); session.FinishSession(); Assert.That(authority.RequestOpen(new("P1", "cache-a")).Result, Is.EqualTo(QusapWorldLootResult.Blocked));
        }
        [Test] public void WrongContainerCannotTransferAnInstanceFromAnotherOrigin()
        { Spawn(); Spawn("cache-b"); var a = Open(); var b = Open("cache-b"); Assert.That(authority.RequestTake(new("P1", "cache-b", a.Items[0].LootInstanceId, 1, b.Version)).Result, Is.EqualTo(QusapWorldLootResult.InvalidSource)); Assert.That(authority.ReadContainer("cache-a").Items.Single().Quantity, Is.EqualTo(3)); }
        [Test] public void ThreeIndependentConfiguredTablesAreNonemptyBoundedAndHaveDisjointIds()
        {
            foreach (string name in new[] { "CommonCache", "MedicalCache", "HighValueCache" })
            {
                var table = AssetDatabase.LoadAssetAtPath<QusapWorldLootTable>("Assets/_Qusap/Settings/WorldLootContainers/" + name + ".asset"); Assert.That(table, Is.Not.Null); Assert.That(table.Validate(catalog), Is.Empty); Spawn(name, table);
                for (ulong seed = 0; seed < 100; seed++) { var rolls = table.Resolve(catalog, session.World.RaidId, name, seed); Assert.That(rolls.Length, Is.InRange(table.MinimumRolls, table.MaximumRolls)); Assert.That(rolls.All(r => r.Quantity > 0 && r.Quantity <= r.Definition.MaxStack), Is.True); }
                Open(name);
            }
            var ids = session.World.Instances.Select(i => i.LootInstanceId).ToArray(); Assert.That(ids.Distinct().Count(), Is.EqualTo(ids.Length)); Assert.That(session.World.Instances.All(i => !i.RaidLoaner), Is.True); Assert.That(catalog.Definitions.Count, Is.EqualTo(9)); Assert.That(catalog.Aliases.Count, Is.EqualTo(5));
        }
        [Test] public void GenerationFailureAndReentrantOpenPublishNoPartialContainer()
        {
            var table = Table("raid_basic_sword", 1, 2); var nativeWeapon = new QusapWeaponInstance(++native, Def("raid_basic_sword").WeaponDefinition);
            QusapWorldLootResult nested = QusapWorldLootResult.Success;
            authority = new QusapLocalWorldLootAuthority(session, catalog, 42, d => { nested = authority.RequestOpen(new("P1", "cache-a")).Result; return nativeWeapon; }); Spawn(table: table);
            Assert.That(authority.RequestOpen(new("P1", "cache-a")).Result, Is.EqualTo(QusapWorldLootResult.GenerationRejected)); Assert.That(nested, Is.EqualTo(QusapWorldLootResult.Reserved)); Assert.That(session.World.Instances, Is.Empty); Assert.That(authority.ReadContainer("cache-a").State, Is.EqualTo(QusapWorldLootState.Unopened));
        }
    }
    internal static class QusapWorldLootRequestTestExtensions
    {
        internal static QusapWorldLootTakeRequest WithQuantity(this QusapWorldLootTakeRequest r, int quantity)
            => new(r.ParticipantId, r.SpawnPointId, r.InstanceId, quantity, r.ExpectedVersion);
    }
}
