using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace Qusap.Tests
{
    public sealed class QusapDeathLootContainerEditModeTests
    {
        private QusapRaidLootCatalog catalog;
        private QusapRaidSession raid;
        private QusapRaidInventoryState p1, p2;
        private QusapLocalWorldLootAuthority owner;
        private readonly List<GameObject> objects = new();
        private QusapRaidWeaponAdapter adapter;
        private QusapWeaponEquipment equipment;
        private int factories;
        private ulong nativeId = 100;
        [SetUp] public void Setup()
        {
            adapter = null; equipment = null; factories = 0; nativeId = 100;
            catalog = AssetDatabase.LoadAssetAtPath<QusapRaidLootCatalog>("Assets/_Qusap/Settings/RaidLootCatalog/RaidLootCatalog.asset");
            raid = new("death-test-raid"); p1 = raid.Register("P1", "isolated/P1"); p2 = raid.Register("P2", "isolated/P2", 2);
            owner = new(raid, catalog, 123, d => { factories++; return new QusapWeaponInstance(++nativeId, d.WeaponDefinition); });
        }
        [TearDown] public void Cleanup()
        { raid.Dispose(); adapter?.Dispose(); foreach (var go in objects) UnityEngine.Object.DestroyImmediate(go); objects.Clear(); }
        private QusapLootDefinition Def(string id) { Assert.That(catalog.TryResolve(id, out var d), Is.True); return d; }
        private QusapLootInstance Bag(string id = "raid_fragment", int quantity = 1, QusapRaidInventoryState player = null)
        {
            var definition = Def(id); var item = raid.World.Create(definition, "death fixture", definition.Category == QusapLootCategory.Weapon ? new QusapWeaponInstance(++nativeId, definition.WeaponDefinition) : null, quantity);
            Assert.That((player ?? p1).TryPickup(item.LootInstanceId), Is.EqualTo(QusapLootResult.Success)); return item;
        }
        private QusapLootInstance Equipped()
        {
            var go = new GameObject("IsolatedDeathEquipment"); go.SetActive(false); objects.Add(go);
            var combat = go.AddComponent<QusapCombatController>(); equipment = go.AddComponent<QusapWeaponEquipment>(); Assert.That(equipment.TryInitialize(combat), Is.True);
            adapter = new(raid.World, p1, equipment, catalog.Definitions);
            var item = raid.World.Create(Def("raid_purple_sword"), "persistent fixture", new QusapWeaponInstance(++nativeId, Def("raid_purple_sword").WeaponDefinition));
            Assert.That(equipment.TryEquip(item.Weapon, out _), Is.EqualTo(QusapWeaponOperationResult.Success)); return item;
        }
        private QusapDeathLootRecord Death(QusapRaidInventoryState player = null)
        {
            player ??= p1; player.BlockOperations(); Assert.That(raid.SettleDeath(player, player == p1 ? adapter : null), Is.EqualTo(QusapLootResult.Success));
            return raid.Containers.Single(c => c.ParticipantId == player.ParticipantId);
        }
        private QusapDeathLootRecord Open()
        {
            var record = Death(); Assert.That(owner.RegisterDeathContainer(record), Is.EqualTo(QusapWorldLootResult.Success));
            Assert.That(owner.RequestOpen(new("P2", record.ContainerId)).Confirmed, Is.True); return record;
        }
        private QusapWorldLootTakeRequest Take(QusapDeathLootRecord record, string id, int quantity = 1, string player = "P2")
            => new(player, record.ContainerId, id, quantity, owner.ReadContainer(record.ContainerId).Version);
        [Test] public void DeterministicIdentityEscapesContextAndDifferentOrdinalsDiffer()
        {
            Assert.That(QusapDeathLootIdentity.Format("raid", "P1", 1), Is.EqualTo("death/raid/P1/1"));
            Assert.That(QusapDeathLootIdentity.Format("raid", "P1", 1), Is.EqualTo(QusapDeathLootIdentity.Format("raid", "P1", 1)));
            Assert.That(QusapDeathLootIdentity.Format("raid", "P1", 2), Is.Not.EqualTo(QusapDeathLootIdentity.Format("raid", "P1", 1)));
            Assert.That(QusapDeathLootIdentity.Format("a/b", "c", 1), Is.Not.EqualTo(QusapDeathLootIdentity.Format("a", "b/c", 1)));
            Assert.Throws<ArgumentException>(() => QusapDeathLootIdentity.Format("raid", "P1", 0));
        }
        [Test] public void DuplicateDeathAndSourceRegistrationResolveExactlyOneRecordAndEvent()
        {
            Bag(); int events = 0; raid.DeathSettled += _ => events++; var record = Death();
            Assert.That(raid.SettleDeath(p1), Is.EqualTo(QusapLootResult.Success)); Assert.That(raid.Containers.Single(), Is.SameAs(record));
            Assert.That(events, Is.EqualTo(1)); Assert.That(record.EliminationOrdinal, Is.EqualTo(1));
            Assert.That(owner.RegisterDeathContainer(record), Is.EqualTo(QusapWorldLootResult.Success));
            var before = owner.ReadContainer(record.ContainerId); Assert.That(owner.RegisterDeathContainer(record), Is.EqualTo(QusapWorldLootResult.Success));
            Assert.That(owner.ReadContainer(record.ContainerId).Version, Is.EqualTo(before.Version));
        }
        [Test] public void AuthoritativeOrdinalsAdvanceOnlyForDifferentAcceptedEliminations()
        {
            Bag(); Bag(player: p2); var first = Death(); raid.SettleDeath(p1); var second = Death(p2);
            Assert.That(first.EliminationOrdinal, Is.EqualTo(1)); Assert.That(second.EliminationOrdinal, Is.EqualTo(2));
            Assert.That(second.ContainerId, Is.EqualTo(QusapDeathLootIdentity.Format(raid.World.RaidId, "P2", 2)));
            Assert.That(raid.Containers.Select(c => c.ContainerId).Distinct().Count(), Is.EqualTo(2));
        }
        [Test] public void BackpackEquippedAndRemainingQuantitiesMoveOnceButConsumedAndPocketDoNot()
        {
            var sword = Equipped(); var material = Bag(quantity: 5); var heal = Bag("raid_minor_heal", 2); var consumed = Bag("raid_minor_heal");
            var pocket = Bag("raid_rare_relic"); p1.TryMoveToSecurePocket(pocket.LootInstanceId);
            p1.TryConsume(heal.LootInstanceId, _ => 1); p1.TryConsume(consumed.LootInstanceId, _ => 1);
            var record = Death(); var items = raid.World.ReadContainer(record.ContainerId);
            Assert.That(items.Select(i => i.LootInstanceId), Is.EquivalentTo(new[] { sword.LootInstanceId, material.LootInstanceId, heal.LootInstanceId }));
            Assert.That(items.Single(i => i.LootInstanceId == heal.LootInstanceId).Quantity, Is.EqualTo(1));
            Assert.That(items.Single(i => i.LootInstanceId == material.LootInstanceId).Quantity, Is.EqualTo(5));
            Assert.That(items.Count(i => ReferenceEquals(i.Weapon, sword.Weapon)), Is.EqualTo(1));
            Assert.That(equipment.HasWeapon, Is.False); Assert.That(sword.Weapon.IsFree, Is.True);
            Assert.That(p1.ReadInventorySnapshot().Backpack.All(i => !i.HasValue), Is.True); Assert.That(p1.ReadInventorySnapshot().SecurePocket, Is.Null);
            Assert.That(consumed.Quantity, Is.Zero); Assert.That(consumed.Location, Is.EqualTo(QusapLootLocation.Consumed));
            Assert.That(pocket.Location, Is.EqualTo(QusapLootLocation.Stash));
            Assert.That(raid.Stash.ReadStashSnapshot("isolated/P1").Single().LootInstanceId, Is.EqualTo(pocket.LootInstanceId));
        }
        [Test] public void SecurePocketReturnsSameStackOnceAndEmptyPocketCreatesNoItem()
        {
            var pocket = Bag(quantity: 3); p1.TryMoveToSecurePocket(pocket.LootInstanceId); Death();
            raid.SettleDeath(p1); raid.FinishSession(); raid.FinishSession();
            var returned = raid.Stash.ReadStashSnapshot("isolated/P1").Single(); Assert.That(returned.LootInstanceId, Is.EqualTo(pocket.LootInstanceId)); Assert.That(returned.Quantity, Is.EqualTo(3));
            Death(p2); Assert.That(raid.Stash.ReadStashSnapshot("isolated/P2"), Is.Empty); Assert.That(raid.World.Instances.Count, Is.EqualTo(1));
        }
        [Test] public void LoanerIsDestroyedAtDeathAndCannotBeLootedEquippedPersistedOrRecovered()
        {
            var definition = Def("raid_basic_sword"); var loaner = raid.World.CreateLoaner(definition, new QusapWeaponInstance(++nativeId, definition.WeaponDefinition));
            p1.TryPickup(loaner.LootInstanceId); var record = Death();
            Assert.That(loaner.Location, Is.EqualTo(QusapLootLocation.Destroyed)); Assert.That(loaner.HolderId, Is.Null); Assert.That(loaner.Weapon.IsRetired, Is.True);
            Assert.That(record.InitialTransferredSnapshot, Is.Empty); Assert.That(raid.World.ReadContainer(record.ContainerId), Is.Empty);
            Assert.That(p2.TryTakeFromDeathContainer(record.ContainerId, loaner.LootInstanceId), Is.Not.EqualTo(QusapLootResult.Success));
            Assert.That(raid.Stash.TryCommitSettlement("bad-loaner", "isolated/P1", new[] { loaner }), Is.False);
            Assert.Throws<InvalidOperationException>(() => QusapProfileItem.From(new QusapLootSnapshot(loaner)));
            raid.FinishSession(); raid.FinishSession(); Assert.That(raid.World.LoanersDestroyed, Is.EqualTo(1)); Assert.That(loaner.Weapon.IsRetired, Is.True);
        }
        [Test] public void PredefinedSourceNeverCallsFactoryRandomOrTableAndNeverRegenerates()
        {
            var item = Bag(); var random = UnityEngine.Random.state; ulong allocator = raid.World.NextItemInstanceId;
            var record = Open(); owner.RequestOpen(new("P2", record.ContainerId)); var snapshot = owner.ReadContainer(record.ContainerId);
            Assert.That(snapshot.TableId, Is.Empty); Assert.That(snapshot.GenerationCount, Is.Zero); Assert.That(snapshot.Seed, Is.Zero);
            Assert.That(factories, Is.Zero); Assert.That(UnityEngine.Random.state, Is.EqualTo(random)); Assert.That(raid.World.NextItemInstanceId, Is.EqualTo(allocator));
            Assert.That(owner.RequestTake(Take(record, item.LootInstanceId)).Confirmed, Is.True); var version = owner.ReadContainer(record.ContainerId).Version;
            Assert.That(owner.RequestOpen(new("P2", record.ContainerId)).Confirmed, Is.False);
            Assert.That(owner.ReadContainer(record.ContainerId).State, Is.EqualTo(QusapWorldLootState.Depleted)); Assert.That(owner.ReadContainer(record.ContainerId).Version, Is.EqualTo(version));
        }
        private QusapLootInstance DisarmedLoaner()
        {
            Equipped(); Assert.That(adapter.TryStoreEquippedWeapon(), Is.EqualTo(QusapLootResult.Success));
            var definition = Def("raid_basic_sword"); var loaner = raid.World.CreateLoaner(definition, new QusapWeaponInstance(++nativeId, definition.WeaponDefinition));
            Assert.That(p1.TryPickup(loaner.LootInstanceId), Is.EqualTo(QusapLootResult.Success));
            Assert.That(adapter.TryEquipWeaponFromBackpack(loaner.LootInstanceId), Is.EqualTo(QusapLootResult.Success));
            Assert.That(equipment.TryDisarm(equipment.OwnerEntityId + 1, out _, out _), Is.EqualTo(QusapWeaponOperationResult.Success));
            Assert.That(loaner.Location, Is.EqualTo(QusapLootLocation.World)); return loaner;
        }
        [Test] public void PreviouslyDiscardedLoanerIsRetiredWhenItsLastCarrierDies()
        {
            var loaner = DisarmedLoaner(); var record = Death();
            Assert.That(loaner.Location, Is.EqualTo(QusapLootLocation.Destroyed)); Assert.That(loaner.Weapon.IsRetired, Is.True);
            Assert.That(record.InitialTransferredSnapshot.All(i => !i.RaidLoaner), Is.True); Assert.That(raid.World.LoanersDestroyed, Is.EqualTo(1));
        }
        [Test] public void RivalAcquiredLoanerIsNotDestroyedByItsPreviousCarriersDeath()
        {
            var loaner = DisarmedLoaner(); Assert.That(p2.TryPickup(loaner.LootInstanceId), Is.EqualTo(QusapLootResult.Success)); Death();
            Assert.That(loaner.Location, Is.EqualTo(QusapLootLocation.Backpack)); Assert.That(loaner.HolderId, Is.EqualTo("P2")); Assert.That(loaner.Weapon.IsRetired, Is.False);
            Death(p2); Assert.That(loaner.Location, Is.EqualTo(QusapLootLocation.Destroyed)); Assert.That(raid.World.LoanersDestroyed, Is.EqualTo(1));
        }
        [Test] public void FullBackpackRejectsAtomicallyAndFreedSlotReceivesOriginalSword()
        {
            var sword = Equipped(); var heal = Bag("raid_minor_heal", player: p2); Bag(player: p2); var record = Open(); var request = Take(record, sword.LootInstanceId);
            Assert.That(owner.RequestTake(request).Result, Is.EqualTo(QusapWorldLootResult.Full)); Assert.That(sword.Location, Is.EqualTo(QusapLootLocation.DeathContainer));
            Assert.That(owner.ReadContainer(record.ContainerId).Version, Is.EqualTo(request.ExpectedVersion));
            p2.TryConsume(heal.LootInstanceId, _ => 1); Assert.That(owner.RequestTake(request).Confirmed, Is.True);
            Assert.That(raid.World.Get(sword.LootInstanceId), Is.SameAs(sword)); Assert.That(sword.Location, Is.EqualTo(QusapLootLocation.Backpack)); Assert.That(sword.HolderId, Is.EqualTo("P2"));
            Assert.That(owner.RequestTake(request).Result, Is.EqualTo(QusapWorldLootResult.StaleRequest)); Assert.That(owner.DuplicateInstances, Is.Zero);
        }
        [Test] public void ConcurrentSameVersionConfirmsOneAndLeavesLoserInventoryIntact()
        {
            var sword = Equipped(); var p3 = raid.Register("P3", "isolated/P3"); var record = Open();
            var a = Take(record, sword.LootInstanceId); var b = Take(record, sword.LootInstanceId, player: "P3");
            QusapWorldLootReply r1 = null, r2 = null; Parallel.Invoke(() => r1 = owner.RequestTake(a), () => r2 = owner.RequestTake(b));
            Assert.That(new[] { r1.Result, r2.Result }, Is.EquivalentTo(new[] { QusapWorldLootResult.Success, QusapWorldLootResult.StaleRequest }));
            Assert.That((r1.Confirmed ? p3 : p2).ReadInventorySnapshot().Backpack.All(i => !i.HasValue), Is.True);
            Assert.That(owner.ConfirmedTransfers, Is.EqualTo(1)); Assert.That(owner.DuplicateInstances, Is.Zero);
        }
        [Test] public void LegacyLedgerTransferInvalidatesVersionBeforeAnotherRequest()
        {
            var first = Bag(); var second = Bag("raid_rare_relic"); var record = Open(); var request = Take(record, second.LootInstanceId);
            Assert.That(p2.TryTakeFromDeathContainer(record.ContainerId, first.LootInstanceId), Is.EqualTo(QusapLootResult.Success));
            Assert.That(owner.RequestTake(request).Result, Is.EqualTo(QusapWorldLootResult.StaleRequest)); Assert.That(second.Location, Is.EqualTo(QusapLootLocation.DeathContainer));
        }
        [Test] public void RivalExtractionRoutesSwordOnlyToRivalsMemoryProfile()
        {
            var sword = Equipped(); var pocket = Bag("raid_rare_relic"); p1.TryMoveToSecurePocket(pocket.LootInstanceId); var abandoned = Bag(quantity: 4); var record = Open();
            Assert.That(owner.RequestTake(Take(record, sword.LootInstanceId)).Confirmed, Is.True); Assert.That(raid.SettleExtraction(p2), Is.EqualTo(QusapLootResult.Success));
            raid.FinishSession(); raid.FinishSession();
            Assert.That(raid.Stash.ReadStashSnapshot("isolated/P2").Single().LootInstanceId, Is.EqualTo(sword.LootInstanceId));
            Assert.That(raid.Stash.ReadStashSnapshot("isolated/P1").Single().LootInstanceId, Is.EqualTo(pocket.LootInstanceId));
            Assert.That(abandoned.Location, Is.EqualTo(QusapLootLocation.Destroyed)); Assert.That(raid.World.DeathLootDestroyed, Is.EqualTo(1));
            Assert.That(raid.SettleExtraction(p2), Is.Not.EqualTo(QusapLootResult.Success)); Assert.That(owner.DuplicateInstances, Is.Zero);
        }
        [Test] public void RivalDeathTransfersSameSwordToItsOwnDeathContainerWithoutVictimRestoration()
        {
            var sword = Equipped(); var record = Open(); owner.RequestTake(Take(record, sword.LootInstanceId)); var second = Death(p2);
            Assert.That(sword.HolderId, Is.EqualTo(second.ContainerId)); Assert.That(sword.Location, Is.EqualTo(QusapLootLocation.DeathContainer));
            Assert.That(second.InitialTransferredSnapshot.Single().LootInstanceId, Is.EqualTo(sword.LootInstanceId)); Assert.That(second.EliminationOrdinal, Is.EqualTo(2));
            Assert.That(raid.World.ReadContainer(record.ContainerId), Is.Empty); Assert.That(raid.Stash.ReadStashSnapshot("isolated/P1"), Is.Empty);
            raid.FinishSession(); Assert.That(sword.Location, Is.EqualTo(QusapLootLocation.Destroyed)); Assert.That(sword.Weapon.IsRetired, Is.True);
        }
        [Test] public void EliminatedParticipantAndCancelledRequestCannotOpenOrTake()
        {
            var item = Bag(); var record = Open(); Assert.That(owner.RequestOpen(new("P1", record.ContainerId)).Result, Is.EqualTo(QusapWorldLootResult.Blocked));
            Assert.That(owner.RequestTake(Take(record, item.LootInstanceId, player: "P1")).Result, Is.EqualTo(QusapWorldLootResult.Blocked));
            using var cancel = new CancellationTokenSource(); cancel.Cancel(); Assert.That(owner.RequestTake(Take(record, item.LootInstanceId), cancel.Token).Result, Is.EqualTo(QusapWorldLootResult.Cancelled));
            Assert.That(item.Location, Is.EqualTo(QusapLootLocation.DeathContainer)); Assert.That(p1.TryMoveToBackpack(item.LootInstanceId), Is.EqualTo(QusapLootResult.Blocked));
        }
        [Test] public void UnitPartsPreserveSourceAndNestedIdentityValidatesForPersistence()
        {
            var stack = Bag(quantity: 5); var record = Open(); var reply = owner.RequestTake(Take(record, stack.LootInstanceId, 2));
            Assert.That(reply.Confirmed, Is.True); Assert.That(stack.Quantity, Is.EqualTo(3)); Assert.That(stack.Location, Is.EqualTo(QusapLootLocation.DeathContainer));
            var child = raid.World.Get(reply.DestinationInstanceId); Assert.That(child.Quantity, Is.EqualTo(2)); Assert.That(child.LootInstanceId, Is.EqualTo(stack.LootInstanceId + "/part/1"));
            Assert.That(QusapRaidInstanceIdentity.TryValidate(stack.RaidId, child.LootInstanceId, out var originalCounter), Is.True); Assert.That(originalCounter, Is.EqualTo(1));
            var second = Death(p2); var p3 = raid.Register("P3", "isolated/P3"); owner.RegisterDeathContainer(second); owner.RequestOpen(new("P3", second.ContainerId));
            var nestedReply = owner.RequestTake(Take(second, child.LootInstanceId, 1, "P3")); var nested = raid.World.Get(nestedReply.DestinationInstanceId);
            Assert.That(nestedReply.Confirmed, Is.True); Assert.That(QusapRaidInstanceIdentity.TryValidate(nested.RaidId, nested.LootInstanceId, out originalCounter), Is.True);
            Assert.That(raid.World.Instances.Where(i => i.DefinitionId == stack.DefinitionId).Sum(i => i.Quantity), Is.EqualTo(5));
            Assert.That(raid.SettleExtraction(p3), Is.EqualTo(QusapLootResult.Success)); Assert.That(raid.Stash.ReadStashSnapshot("isolated/P3").Single().LootInstanceId, Is.EqualTo(nested.LootInstanceId));
        }
        [TestCase("death-test-raid/loot/1/part/0")]
        [TestCase("death-test-raid/loot/1/part/01")]
        [TestCase("death-test-raid/loot/1/part")]
        [TestCase("death-test-raid/loot/1/evil/1")]
        public void InvalidPartIdentitiesRemainRejected(string id) => Assert.That(QusapRaidInstanceIdentity.TryValidate(raid.World.RaidId, id, out _), Is.False);
        [Test] public void GeometryProjectionAndSolidFallbackAreSafeWithoutChangingSourcePosition()
        {
            GameObject Primitive(string name, Vector3 position, Vector3 scale)
            { var go = GameObject.CreatePrimitive(PrimitiveType.Cube); objects.Add(go); go.name = name; go.layer = 30; go.transform.position = position; go.transform.localScale = scale; return go; }
            Primitive("DeathTestFloor", new(0,-.5f,0), new(30,1,4)); Primitive("DeathTestWall", new(4,1,0), new(1,2,2)); Physics.SyncTransforms();
            Vector3 safe = new(0,1.05f,0), invalid = new(4,1,0); int mask = 1 << 30;
            Assert.That(QusapDeathLootPlacementResolver.TryResolve(safe, null, mask, mask, .35f, 0, out var projected), Is.True);
            Assert.That(projected.Kind, Is.EqualTo(QusapDeathLootPlacementKind.GroundProjection)); Assert.That(projected.Position.y, Is.EqualTo(.37f).Within(.001f));
            Assert.That(QusapDeathLootPlacementResolver.TryResolve(invalid, safe, mask, mask, .35f, 0, out var fallback), Is.True);
            Assert.That(fallback.Kind, Is.EqualTo(QusapDeathLootPlacementKind.LastSafePosition)); Assert.That(fallback.Position.x, Is.EqualTo(safe.x)); Assert.That(fallback.Position.z, Is.Zero);
            Assert.That(invalid, Is.EqualTo(new Vector3(4,1,0))); Assert.That(QusapDeathLootPlacementResolver.TryResolve(invalid, null, mask, mask, .35f, 0, out _), Is.False);
        }
        private static string Hash(string path) { using var sha = SHA256.Create(); return BitConverter.ToString(sha.ComputeHash(File.ReadAllBytes(path))).Replace("-", ""); }
        [Test] public void ProtectedAssetsAndBuildSettingsMatchApprovedBytesAndEverySceneGuidResolves()
        {
            Assert.That(Hash("Assets/_Qusap/Settings/QusapControls.inputactions"), Is.EqualTo("928669F8FA54A36F792F1E6A4B367759CE1E004EA3E3670C39F5C8BA9651D912"));
            Assert.That(Hash("Assets/_Qusap/Scripts/Input/QusapInputReader.cs"), Is.EqualTo("77FD97B48684C854E1A2D8AC55CCBC99AAFDFA1EA2D40FD44D6C7DFB435B4B47"));
            Assert.That(Hash("Assets/_Qusap/Prefabs/QusapCombatPlayer.prefab"), Is.EqualTo("FB72228431EC4F8620D8F5A6FD8240ECABCFC0D5AB046AF3C8BF1984A53D53D5"));
            const string scene = "Assets/_Qusap/Scenes/RaidDeathLootContainerPlayground.unity";
            Assert.That(EditorBuildSettings.scenes.Any(s => s.path == scene), Is.False);
            foreach (Match match in Regex.Matches(File.ReadAllText(scene), @"guid: ([0-9a-f]{32})"))
                if (!match.Groups[1].Value.StartsWith("0000000000000000")) Assert.That(AssetDatabase.GUIDToAssetPath(match.Groups[1].Value), Is.Not.Empty, match.Value);
        }
        [Test] public void ProfileDtoContainsOnlyExtractedItemsAndNoPhysicalContainerSerialization()
        {
            var sword = Equipped(); var record = Open(); owner.RequestTake(Take(record, sword.LootInstanceId)); raid.SettleExtraction(p2);
            var content = new QusapProfileContent { ProfileId = "isolated/P2", Revision = 1, SavedAtUtc = "2026-10-11T00:00:00.0000000Z",
                NextItemInstanceId = raid.World.NextItemInstanceId, NextNativeWeaponInstanceId = nativeId + 1,
                Stash = raid.Stash.ReadStashSnapshot("isolated/P2").Select(QusapProfileItem.From).ToArray(),
                Settlements = new[] { new QusapProfileReceipt { SettlementId = raid.World.RaidId + "/extraction/P2", Fingerprint = "isolated/P2\n" + sword.LootInstanceId } } };
            var json = QusapProfileCodec.Encode(QusapProfileCodec.Seal(content)); var codec = new QusapProfileCodec(catalog.Definitions);
            Assert.That(codec.TryDecode(json, out var decoded, out var error, out _), Is.True, error);
            Assert.That(decoded.Content.Stash.Single().InstanceId, Is.EqualTo(sword.LootInstanceId));
            foreach (var forbidden in new[] { record.ContainerId, "DeathContainer", "GameObject", "Rigidbody", "RaidLoaner" }) StringAssert.DoesNotContain(forbidden, json);
            string hud = File.ReadAllText("Assets/_Qusap/Scripts/UI/QusapRaidDeathLootHud.cs");
            foreach (var forbidden in new[] { "File.Write", "TryCommitSettlement", "PersistentStashRepository" }) StringAssert.DoesNotContain(forbidden, hud);
        }
    }
}
