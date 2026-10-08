using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace Qusap.Tests
{
    public sealed class QusapRaidInventoryEditModeTests
    {
        private QusapRaidSession raid;
        private QusapRaidInventoryState p1, p2;
        private readonly List<QusapLootDefinition> definitions = new();
        private QusapLootDefinition fragment, healing;
        private QusapLootDefinition Def(QusapLootCategory category, bool eligible = true)
        {
            var d = ScriptableObject.CreateInstance<QusapLootDefinition>();
            d.Configure("test_" + definitions.Count, category.ToString(), category, eligible,
                category == QusapLootCategory.Consumable ? QusapConsumableType.Healing : QusapConsumableType.None, 25);
            definitions.Add(d); return d;
        }
        private QusapLootInstance Item(QusapLootDefinition d = null) => raid.World.Create(d ?? fragment, "test origin");
        private QusapLootInstance Bag(QusapLootDefinition d = null)
        { var item = Item(d); Assert.That(p1.TryPickup(item.LootInstanceId), Is.EqualTo(QusapLootResult.Success)); return item; }
        private void Fill(QusapRaidInventoryState state)
        { while (state.ReadInventorySnapshot().Backpack.Any(i => !i.HasValue)) Assert.That(state.TryPickup(Item().LootInstanceId), Is.EqualTo(QusapLootResult.Success)); }
        [SetUp] public void SetUp()
        { raid = new QusapRaidSession("unit-raid"); p1 = raid.Register("P1", "profile1"); p2 = raid.Register("P2", "profile2"); fragment = Def(QusapLootCategory.Fragment); healing = Def(QusapLootCategory.Consumable); }
        [TearDown] public void TearDown()
        { raid.Dispose(); foreach (var d in definitions) UnityEngine.Object.DestroyImmediate(d); definitions.Clear(); }

        [Test] public void IdsAreUniqueAndCarryRaidDefinitionAndProvenance()
        {
            var all = Enumerable.Range(0, 100).Select(_ => Item()).ToArray();
            Assert.That(all.Select(i => i.LootInstanceId).Distinct().Count(), Is.EqualTo(100));
            Assert.That(all.All(i => i.RaidId == "unit-raid" && i.DefinitionId == fragment.DefinitionId && i.Provenance == "test origin"), Is.True);
        }
        [Test] public void SnapshotIsImmutableAndEachInstanceHasOneLocation()
        {
            var item = Bag(); var before = p1.ReadInventorySnapshot();
            Assert.That(p1.TryMoveToSecurePocket(item.LootInstanceId), Is.EqualTo(QusapLootResult.Success));
            Assert.That(before.Backpack[0].Value.Location, Is.EqualTo(QusapLootLocation.Backpack));
            Assert.That(item.Location, Is.EqualTo(QusapLootLocation.SecurePocket));
            Assert.That(p1.ReadInventorySnapshot().Backpack.All(i => !i.HasValue), Is.True);
            Assert.That(raid.World.Instances.Count(i => i.LootInstanceId == item.LootInstanceId), Is.EqualTo(1));
        }
        [Test] public void DefaultSixSlotsRejectSeventhWithoutLosingWorldObject()
        {
            Fill(p1); var seventh = Item(); int changed = 0; p1.InventoryChanged += () => changed++;
            Assert.That(p1.ReadInventorySnapshot().Backpack.Count, Is.EqualTo(6));
            Assert.That(p1.TryPickup(seventh.LootInstanceId), Is.EqualTo(QusapLootResult.Full));
            Assert.That(seventh.Location, Is.EqualTo(QusapLootLocation.World)); Assert.That(changed, Is.Zero);
        }
        [Test] public void CapacityCanBeConfiguredWithoutChangingPocketCapacity()
        { var small = raid.Register("small", "small-profile", 2); Fill(small); Assert.That(small.TryPickup(Item().LootInstanceId), Is.EqualTo(QusapLootResult.Full)); }
        [Test] public void OccupiedPocketPreservesBackpackOrigin()
        {
            var a = Bag(); var b = Bag(); p1.TryMoveToSecurePocket(a.LootInstanceId);
            Assert.That(p1.TryMoveToSecurePocket(b.LootInstanceId), Is.EqualTo(QusapLootResult.Occupied));
            Assert.That(a.Location, Is.EqualTo(QusapLootLocation.SecurePocket)); Assert.That(b.Location, Is.EqualTo(QusapLootLocation.Backpack));
        }
        [Test] public void FullBackpackPreservesPocketOrigin()
        {
            var item = Bag(); p1.TryMoveToSecurePocket(item.LootInstanceId); Fill(p1);
            Assert.That(p1.TryMoveToBackpack(item.LootInstanceId), Is.EqualTo(QusapLootResult.Full)); Assert.That(item.Location, Is.EqualTo(QusapLootLocation.SecurePocket));
        }
        [TestCase(QusapLootCategory.Objective)] [TestCase(QusapLootCategory.Horn)] [TestCase(QusapLootCategory.Armor)]
        public void ExcludedCategoriesCannotEnterPocketEvenWhenEligibilityFlagIsTrue(QusapLootCategory category)
        { var item = Bag(Def(category)); Assert.That(p1.TryMoveToSecurePocket(item.LootInstanceId), Is.EqualTo(QusapLootResult.Ineligible)); Assert.That(item.Location, Is.EqualTo(QusapLootLocation.Backpack)); }
        [Test] public void WeaponsCannotEnterPocketAndCannotBeRegisteredTwice()
        {
            var catalog = AssetDatabase.FindAssets("t:QusapWeaponVisualCatalog").Select(AssetDatabase.GUIDToAssetPath)
                .Select(AssetDatabase.LoadAssetAtPath<QusapWeaponVisualCatalog>).First(c => c.Count == 3);
            var d = Def(QusapLootCategory.Weapon); d.Configure("native", "Sword", QusapLootCategory.Weapon, true, catalog: catalog, weaponId: QusapWeaponVisualCatalog.BlueDefinitionId);
            var weapon = new QusapWeaponInstance(9001, d.WeaponDefinition);
            var item = raid.World.Create(d, "native", weapon); p1.TryPickup(item.LootInstanceId);
            Assert.That(p1.TryMoveToSecurePocket(item.LootInstanceId), Is.EqualTo(QusapLootResult.Ineligible));
            Assert.Throws<InvalidOperationException>(() => raid.World.Create(d, "duplicate", weapon));
        }
        [Test] public void WrongSourceOrParticipantCannotMoveAnotherPlayersObject()
        { var item = Bag(); Assert.That(p2.TryMoveToSecurePocket(item.LootInstanceId), Is.EqualTo(QusapLootResult.InvalidSource)); Assert.That(p2.TryPickup(item.LootInstanceId), Is.EqualTo(QusapLootResult.InvalidSource)); Assert.That(item.HolderId, Is.EqualTo("P1")); }
        [Test] public void ConsumptionIsCommittedOnlyAfterEffectiveHealing()
        {
            var item = Bag(healing); int changed = 0; p1.InventoryChanged += () => changed++;
            Assert.That(p1.TryConsume(item.LootInstanceId, _ => 0), Is.EqualTo(QusapLootResult.NoEffect)); Assert.That(changed, Is.Zero);
            Assert.That(item.Location, Is.EqualTo(QusapLootLocation.Backpack));
            Assert.That(p1.TryConsume(item.LootInstanceId, amount =>
            { Assert.That(amount, Is.EqualTo(25)); Assert.That(item.Location, Is.EqualTo(QusapLootLocation.Backpack)); return 12; }), Is.EqualTo(QusapLootResult.Success));
            Assert.That(item.Location, Is.EqualTo(QusapLootLocation.Consumed)); Assert.That(changed, Is.EqualTo(1));
            Assert.That(p1.TryConsume(item.LootInstanceId, _ => 25), Is.EqualTo(QusapLootResult.InvalidSource));
        }
        [Test] public void HealingExceptionReleasesReservationWithoutConsuming()
        { var item = Bag(healing); Assert.Throws<InvalidOperationException>(() => p1.TryConsume(item.LootInstanceId, _ => throw new InvalidOperationException())); Assert.That(p1.TryConsume(item.LootInstanceId, _ => 1), Is.EqualTo(QusapLootResult.Success)); }
        [Test] public void HealingCallbackCannotTransferReservedConsumable()
        {
            var item = Bag(healing);
            p1.TryConsume(item.LootInstanceId, _ =>
            { Assert.That(p1.TryMoveToSecurePocket(item.LootInstanceId), Is.EqualTo(QusapLootResult.Reserved)); return 1; });
            Assert.That(item.Location, Is.EqualTo(QusapLootLocation.Consumed));
        }
        [Test] public void DepletedInventoryBlocksAllCommands()
        {
            var item = Bag(healing); var world = Item(); p1.BlockOperations();
            Assert.That(p1.TryPickup(world.LootInstanceId), Is.EqualTo(QusapLootResult.Blocked));
            Assert.That(p1.TryMoveToSecurePocket(item.LootInstanceId), Is.EqualTo(QusapLootResult.Blocked));
            Assert.That(p1.TryMoveToBackpack(item.LootInstanceId), Is.EqualTo(QusapLootResult.Blocked));
            Assert.That(p1.TryConsume(item.LootInstanceId, _ => throw new Exception()), Is.EqualTo(QusapLootResult.Blocked));
            Assert.That(p1.TryTakeFromDeathContainer("any", world.LootInstanceId), Is.EqualTo(QusapLootResult.Blocked));
        }
        [Test] public void SettlementIsIdempotentAndRetainsSecurePocketOnlyInMemoryStash()
        {
            var cargo = Bag(); var safe = Bag(Def(QusapLootCategory.Relic)); p1.TryMoveToSecurePocket(safe.LootInstanceId);
            int events = 0; raid.DeathSettled += _ => events++; p1.BlockOperations();
            Assert.That(raid.SettleDeath(p1), Is.EqualTo(QusapLootResult.Success)); Assert.That(raid.SettleDeath(p1), Is.EqualTo(QusapLootResult.Success));
            Assert.That(events, Is.EqualTo(1)); Assert.That(raid.Containers.Count, Is.EqualTo(1));
            Assert.That(raid.Containers[0].InitialTransferredSnapshot.Single().LootInstanceId, Is.EqualTo(cargo.LootInstanceId));
            Assert.That(safe.Location, Is.EqualTo(QusapLootLocation.Stash));
            Assert.That(raid.Stash.ReadStashSnapshot("profile1").Single().LootInstanceId, Is.EqualTo(safe.LootInstanceId));
            Assert.That(new QusapMemoryStashRepository().ReadStashSnapshot("profile1"), Is.Empty);
        }
        [Test] public void FullRivalBackpackDoesNotRemoveDeathCargo()
        {
            var item = Bag(); p1.BlockOperations(); raid.SettleDeath(p1); Fill(p2);
            Assert.That(p2.TryTakeFromDeathContainer(raid.Containers.Single().ContainerId, item.LootInstanceId), Is.EqualTo(QusapLootResult.Full));
            Assert.That(item.Location, Is.EqualTo(QusapLootLocation.DeathContainer));
        }
        [Test] public void EightIndependentInventoriesShareOneLedgerWithoutEightControllers()
        {
            for (int i = 3; i <= 8; i++) raid.Register("P" + i, "profile" + i);
            using var model = new QusapRaidSession("eight");
            var players = Enumerable.Range(1, 8).Select(i => model.Register("P" + i, "profile" + i)).ToArray();
            foreach (var p in players) { var item = model.World.Create(fragment, "eight model"); p.TryPickup(item.LootInstanceId); p.TryMoveToSecurePocket(item.LootInstanceId); }
            Assert.That(players.Select(p => p.ReadInventorySnapshot().SecurePocket.Value.LootInstanceId).Distinct().Count(), Is.EqualTo(8));
            players[0].BlockOperations(); model.SettleDeath(players[0]); Assert.That(players.Skip(1).All(p => p.CanOperate), Is.True);
        }
        [Test] public void ConcurrentClaimsProduceExactlyOneWinner()
        {
            var item = Bag(); p1.BlockOperations(); raid.SettleDeath(p1);
            var p3 = raid.Register("P3", "profile3"); using var start = new Barrier(2);
            string container = raid.Containers.Single().ContainerId;
            var tasks = new[] { p2, p3 }.Select(p => Task.Run(() => { start.SignalAndWait(); return p.TryTakeFromDeathContainer(container, item.LootInstanceId); })).ToArray();
            Assert.That(Task.WaitAll(tasks, 5000), Is.True);
            Assert.That(tasks.Count(t => t.Result == QusapLootResult.Success), Is.EqualTo(1));
            Assert.That(tasks.Count(t => t.Result == QusapLootResult.InvalidSource), Is.EqualTo(1));
            Assert.That(raid.World.ReadContainer(container), Is.Empty);
        }
        private sealed class RejectOnce : IQusapStashRepository
        {
            public bool Reject = true; private readonly QusapMemoryStashRepository store = new();
            public bool TryCommitSettlement(string key, string profile, IReadOnlyList<QusapLootInstance> items) => !Reject && store.TryCommitSettlement(key, profile, items);
            public IReadOnlyList<QusapLootSnapshot> ReadStashSnapshot(string profile) => store.ReadStashSnapshot(profile);
        }
        [Test] public void StashFailurePreservesAllOriginsAndPendingSettlementCanRetry()
        {
            var stash = new RejectOnce(); using var model = new QusapRaidSession("failure", stash); var p = model.Register("P", "profile");
            var safe = model.World.Create(fragment, "safe"); var bag = model.World.Create(fragment, "bag");
            p.TryPickup(safe.LootInstanceId); p.TryMoveToSecurePocket(safe.LootInstanceId); p.TryPickup(bag.LootInstanceId); p.BlockOperations();
            Assert.That(model.SettleDeath(p), Is.EqualTo(QusapLootResult.StashUnavailable)); Assert.That(model.Containers, Is.Empty);
            Assert.That(p.Status, Is.EqualTo(QusapRaidInventoryStatus.SettlementPending)); Assert.That(safe.Location, Is.EqualTo(QusapLootLocation.SecurePocket)); Assert.That(bag.Location, Is.EqualTo(QusapLootLocation.Backpack));
            stash.Reject = false; Assert.That(model.SettleDeath(p), Is.EqualTo(QusapLootResult.Success)); Assert.That(model.Containers.Count, Is.EqualTo(1));
        }
    }
}
