using System;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using UnityEngine;

namespace Qusap.Tests
{
    public sealed class QusapRaidExtractionEditModeTests
    {
        private QusapRaidSession raid;
        private QusapRaidInventoryState p1, p2;
        private QusapLootDefinition definition;
        [SetUp] public void Setup()
        {
            raid = new QusapRaidSession("extraction-test"); p1 = raid.Register("P1", "profile1"); p2 = raid.Register("P2", "profile2");
            definition = ScriptableObject.CreateInstance<QusapLootDefinition>();
            definition.Configure("fragment", "Fragment", QusapLootCategory.Fragment, true);
        }
        [TearDown] public void Cleanup() { raid.Dispose(); UnityEngine.Object.DestroyImmediate(definition); }
        private QusapLootInstance Bag(QusapRaidInventoryState state)
        { var item = raid.World.Create(definition, "test"); Assert.That(state.TryPickup(item.LootInstanceId), Is.EqualTo(QusapLootResult.Success)); return item; }
        [Test] public void CountdownStartsAutomaticallyAndUsesConfiguredSeconds()
        { var t = new QusapExtractionCountdown(); Assert.That(t.Begin(5, true), Is.True); t.Advance(2, () => throw new Exception()); Assert.That(t.Status, Is.EqualTo(QusapExtractionStatus.Extracting)); Assert.That(t.Remaining, Is.EqualTo(3)); }
        [TestCase(QusapExtractionCancellation.LeftVolume)] [TestCase(QusapExtractionCancellation.Damage)]
        [TestCase(QusapExtractionCancellation.TechnicalRecovery)] [TestCase(QusapExtractionCancellation.ZoneDisabled)]
        public void CancellationResetsAndReentryStartsAtZero(QusapExtractionCancellation reason)
        { var t = new QusapExtractionCountdown(); t.Begin(5, true); t.Advance(3, () => throw new Exception()); t.Cancel(reason); Assert.That(t.Elapsed, Is.Zero); Assert.That(t.Cancellation, Is.EqualTo(reason)); Assert.That(t.Begin(5, true), Is.True); Assert.That(t.Remaining, Is.EqualTo(5)); }
        [Test] public void EliminationCancelsAndPermanentlyRejectsCountdown()
        { var t = new QusapExtractionCountdown(); t.Begin(5, true); t.Advance(1, () => throw new Exception()); t.Eliminate(); Assert.That(t.Elapsed, Is.Zero); Assert.That(t.Cancellation, Is.EqualTo(QusapExtractionCancellation.Eliminated)); Assert.That(t.Begin(5, true), Is.False); Assert.That(t.Advance(100, () => throw new Exception()), Is.False); }
        [Test] public void SuccessfulTimerResolvesExactlyOnce()
        { var t = new QusapExtractionCountdown(); int calls = 0; t.Begin(5, true); Assert.That(t.Advance(5, () => { calls++; return QusapLootResult.Success; }), Is.True); Assert.That(t.Begin(5, true), Is.False); t.Advance(5, () => { calls++; return QusapLootResult.Success; }); Assert.That(calls, Is.EqualTo(1)); Assert.That(t.TransfersResolved, Is.EqualTo(1)); }
        [Test] public void FullBackpackAndPocketKeepExactInstancesIdsAndProvenanceWithoutContainer()
        {
            var safe = Bag(p1); p1.TryMoveToSecurePocket(safe.LootInstanceId);
            var bag = Enumerable.Range(0, 6).Select(_ => Bag(p1)).ToArray(); var all = bag.Append(safe).ToArray();
            int deaths = 0; raid.DeathSettled += _ => deaths++;
            Assert.That(raid.SettleExtraction(p1), Is.EqualTo(QusapLootResult.Success));
            CollectionAssert.AreEquivalent(all.Select(i => i.LootInstanceId), raid.Stash.ReadStashSnapshot("profile1").Select(i => i.LootInstanceId));
            foreach (var item in all) { Assert.That(raid.World.Get(item.LootInstanceId), Is.SameAs(item)); Assert.That(item.Provenance, Is.EqualTo("test")); Assert.That(item.HolderId, Is.EqualTo("profile1")); }
            Assert.That(p1.ReadInventorySnapshot().Backpack.All(x => !x.HasValue), Is.True); Assert.That(p1.ReadInventorySnapshot().SecurePocket, Is.Null);
            Assert.That(raid.Containers, Is.Empty); Assert.That(deaths, Is.Zero); Assert.That(raid.World.Instances.Count, Is.EqualTo(7));
        }
        [Test] public void ExtractionBlocksDeathRepeatPickupAndConsumptionWithoutDuplicates()
        {
            var item = Bag(p1); raid.SettleExtraction(p1);
            Assert.That(raid.SettleExtraction(p1), Is.EqualTo(QusapLootResult.Blocked));
            Assert.That(raid.SettleDeath(p1), Is.EqualTo(QusapLootResult.Blocked));
            Assert.That(p1.TryPickup(Bag(p2).LootInstanceId), Is.EqualTo(QusapLootResult.Blocked));
            Assert.That(p1.TryConsume(item.LootInstanceId, _ => throw new Exception()), Is.EqualTo(QusapLootResult.Blocked));
            Assert.That(raid.ExtractionTransfersResolved, Is.EqualTo(1)); Assert.That(raid.Stash.ReadStashSnapshot("profile1").Count, Is.EqualTo(1));
        }
        [Test] public void TwoExtractionsKeepExclusiveOwnershipAndOtherPlayerStaysActive()
        {
            var a = Bag(p1); var b = Bag(p2); raid.SettleExtraction(p1); Assert.That(p2.CanOperate, Is.True);
            var c = Bag(p2); Assert.That(raid.SettleExtraction(p2), Is.EqualTo(QusapLootResult.Success));
            Assert.That(raid.Stash.ReadStashSnapshot("profile1").Single().LootInstanceId, Is.EqualTo(a.LootInstanceId));
            CollectionAssert.AreEquivalent(new[] { b.LootInstanceId, c.LootInstanceId }, raid.Stash.ReadStashSnapshot("profile2").Select(x => x.LootInstanceId));
            Assert.That(raid.ExtractionTransfersResolved, Is.EqualTo(2));
        }
        [Test] public void EliminatedInventoryCannotExtractAndDeathPocketRegressionIsUnchanged()
        {
            var bag = Bag(p1); var safe = Bag(p1); p1.TryMoveToSecurePocket(safe.LootInstanceId); p1.BlockOperations();
            Assert.That(raid.SettleExtraction(p1), Is.EqualTo(QusapLootResult.Blocked)); raid.SettleDeath(p1);
            Assert.That(bag.Location, Is.EqualTo(QusapLootLocation.DeathContainer)); Assert.That(safe.Location, Is.EqualTo(QusapLootLocation.Stash));
            Assert.That(raid.SettleExtraction(p1), Is.EqualTo(QusapLootResult.Blocked)); Assert.That(raid.ExtractionTransfersResolved, Is.Zero);
        }
        private sealed class CallbackStash : IQusapStashRepository
        {
            public Func<bool> Callback;
            private readonly QusapMemoryStashRepository stash = new();
            public bool TryCommitSettlement(string key, string profile, IReadOnlyList<QusapLootInstance> items) => Callback() && stash.TryCommitSettlement(key, profile, items);
            public IReadOnlyList<QusapLootSnapshot> ReadStashSnapshot(string profile) => stash.ReadStashSnapshot(profile);
        }
        [Test] public void StashReentryAndFailureCannotLoseOrDuplicateCargo()
        {
            var stash = new CallbackStash(); using var r = new QusapRaidSession("callback", stash); var p = r.Register("P", "profile");
            var item = r.World.Create(definition, "origin"); p.TryPickup(item.LootInstanceId);
            stash.Callback = () => { Assert.That(r.SettleExtraction(p), Is.EqualTo(QusapLootResult.Blocked)); Assert.That(r.SettleDeath(p), Is.EqualTo(QusapLootResult.Blocked)); return false; };
            Assert.That(r.SettleExtraction(p), Is.EqualTo(QusapLootResult.StashUnavailable)); Assert.That(p.CanOperate, Is.True); Assert.That(item.Location, Is.EqualTo(QusapLootLocation.Backpack));
            stash.Callback = () => true; Assert.That(r.SettleExtraction(p), Is.EqualTo(QusapLootResult.Success)); Assert.That(r.World.Get(item.LootInstanceId), Is.SameAs(item));
        }
        [Test] public void ForeignSessionStateIsRejected()
        { using var other = new QusapRaidSession("foreign"); Assert.That(other.SettleExtraction(p1), Is.EqualTo(QusapLootResult.InvalidSource)); }
    }
}
