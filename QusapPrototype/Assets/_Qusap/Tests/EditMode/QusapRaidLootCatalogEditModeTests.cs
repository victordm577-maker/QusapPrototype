using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace Qusap.Tests
{
    public sealed class QusapRaidLootCatalogEditModeTests
    {
        private readonly List<UnityEngine.Object> owned = new();
        private string directory;
        private QusapLootDefinition material;
        private QusapRaidSession raid;
        private QusapRaidInventoryState player;
        [SetUp] public void Setup()
        {
            directory = Path.Combine(Path.GetTempPath(), "QusapRaidLootCatalogTests", Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(directory);
            material = Definition("test_material", QusapLootCategory.Material, 10);
            raid = new QusapRaidSession("isolated-raid"); player = raid.Register("P1", "isolated-profile");
        }
        [TearDown] public void Cleanup()
        {
            raid.Dispose(); foreach (var obj in owned) UnityEngine.Object.DestroyImmediate(obj); owned.Clear();
            string parent = Path.GetFullPath(Path.Combine(Path.GetTempPath(), "QusapRaidLootCatalogTests")) + Path.DirectorySeparatorChar;
            string exact = Path.GetFullPath(directory);
            if (!exact.StartsWith(parent, StringComparison.OrdinalIgnoreCase) || !Guid.TryParseExact(Path.GetFileName(exact), "N", out _)) throw new InvalidOperationException("Unsafe fixture cleanup");
            Directory.Delete(exact, true);
        }
        private T Own<T>() where T : ScriptableObject { var asset = ScriptableObject.CreateInstance<T>(); owned.Add(asset); return asset; }
        private QusapLootDefinition Definition(string id, QusapLootCategory category, int stack = 1, float heal = 25,
            QusapLootRarity rarity = QusapLootRarity.Common)
        {
            var d = Own<QusapLootDefinition>();
            var native = category == QusapLootCategory.Weapon ? AssetDatabase.LoadAssetAtPath<QusapWeaponVisualCatalog>("Assets/_Qusap/Settings/Combat/Weapons/QusapWeaponVisualCatalog.asset") : null;
            d.Configure(id, id, category, category != QusapLootCategory.Weapon && category != QusapLootCategory.Armor,
                category == QusapLootCategory.Consumable ? QusapConsumableType.Healing : QusapConsumableType.None,
                category == QusapLootCategory.Consumable ? heal : 0, native, native != null ? QusapWeaponVisualCatalog.BlueDefinitionId : null);
            d.ConfigureMetadata(rarity, stack, 0, "raid_pickup_common", itemTags: new[] { "test", "raid" }); return d;
        }
        private QusapRaidLootCatalog Catalog(params QusapLootDefinition[] definitions) { var c = Own<QusapRaidLootCatalog>(); c.Configure(definitions); return c; }
        private QusapRaidLootTable Table(params QusapLootTableEntry[] entries) { var t = Own<QusapRaidLootTable>(); t.Configure("test_table", entries); return t; }
        private QusapLootInstance Cargo(int quantity, string provenance = "same durable properties") => raid.World.Create(material, provenance, quantity: quantity);
        private void Pickup(QusapLootInstance item) => Assert.That(player.TryPickup(item.LootInstanceId), Is.EqualTo(QusapLootResult.Success));

        [Test] public void DefinitionIdsAreImmutableAndDuplicateDefinitionsAreRejected()
        {
            Assert.Throws<InvalidOperationException>(() => material.Configure("renamed", "name", QusapLootCategory.Material, true));
            Assert.That(Catalog(material, material).Validate(), Has.Some.Contains("Duplicate"));
        }
        [TestCase(QusapLootCategory.Material, 10)] [TestCase(QusapLootCategory.Consumable, 2)]
        [TestCase(QusapLootCategory.Weapon, 1)] [TestCase(QusapLootCategory.Armor, 1)] [TestCase(QusapLootCategory.Relic, 1)]
        public void FiveCategoriesHaveValidExplicitData(QusapLootCategory category, int stack)
        { Assert.That(Catalog(Definition("category_test", category, stack)).Validate(), Is.Empty); }
        [TestCase(QusapLootRarity.Common)] [TestCase(QusapLootRarity.Uncommon)] [TestCase(QusapLootRarity.Rare)]
        [TestCase(QusapLootRarity.Epic)] [TestCase(QusapLootRarity.Legendary)]
        public void RarityDoesNotChangeExplicitHealing(QusapLootRarity tier)
        { var d = Definition("heal", QusapLootCategory.Consumable, 2, 25, tier); Assert.That(d.HealAmount, Is.EqualTo(25)); Assert.That(Catalog(d).Validate(), Is.Empty); }
        [Test] public void InvalidCategoryRarityStackValueAndDuplicateTagsAreReported()
        {
            var d = Definition("invalid", (QusapLootCategory)999);
            d.ConfigureMetadata((QusapLootRarity)999, 0, -1, "", itemTags: new[] { "same", "same" });
            var errors = Catalog(d).Validate();
            foreach (var field in new[] { "Category", "Rarity", "MaxStack", "BaseValue", "Tags", "Presentation" }) Assert.That(errors, Has.Some.Contains(field));
        }
        [TestCase(QusapLootCategory.Weapon)] [TestCase(QusapLootCategory.Armor)] [TestCase(QusapLootCategory.Relic)]
        public void NonStackableCategoriesRejectQuantityAboveOne(QusapLootCategory category)
        {
            var d = Definition("single", category, 2); Assert.That(Catalog(d).Validate(), Has.Some.Contains("MaxStack"));
            d.ConfigureMetadata(QusapLootRarity.Common, 1, 0, "raid_pickup_common");
            var weapon = category == QusapLootCategory.Weapon ? new QusapWeaponInstance(100, d.WeaponDefinition) : null;
            Assert.Throws<ArgumentOutOfRangeException>(() => raid.World.Create(d, "test", weapon, 2));
            Assert.That(raid.World.Create(d, "test", weapon).Quantity, Is.EqualTo(1));
        }
        [Test] public void AliasesResolveTheSameDefinitionWithoutAddingDuplicateWeapons()
        {
            var weapon = Definition("existing_weapon", QusapLootCategory.Weapon); var c = Catalog(material, weapon);
            c.Configure(new[] { material, weapon }, new QusapLootAlias("new_name", material.DefinitionId));
            Assert.That(c.Validate(), Is.Empty); c.TryResolve("new_name", out var resolved); Assert.That(resolved, Is.SameAs(material));
            c.Configure(new[] { material, weapon }, new QusapLootAlias("bad", "missing")); Assert.That(c.Validate(), Is.Not.Empty);
            Assert.That(Catalog(weapon, Definition("second_for_same_native", QusapLootCategory.Weapon)).Validate(), Has.Some.Contains("one native weapon"));
        }
        [Test] public void DeterministicTagsAreSortedAndLegacyCategoryValuesAreUnchanged()
        {
            Assert.That(material.Tags, Is.EqualTo(new[] { "raid", "test" }));
            Assert.That((int)QusapLootCategory.Material, Is.Zero); Assert.That(QusapLootCategory.Fragment, Is.EqualTo(QusapLootCategory.Material));
            Assert.That((int)QusapLootCategory.Relic, Is.EqualTo(1)); Assert.That((int)QusapLootCategory.Weapon, Is.EqualTo(3));
        }
        [Test] public void PartialStackIsFilledBeforeAnotherSlotAndIdsAreNeverReused()
        {
            var first = Cargo(6); var incoming = Cargo(7); Pickup(first); Pickup(incoming);
            Assert.That(first.Quantity, Is.EqualTo(10)); Assert.That(incoming.Quantity, Is.EqualTo(3));
            Assert.That(first.SlotIndex, Is.Zero); Assert.That(incoming.SlotIndex, Is.EqualTo(1));
            var third = Cargo(4); Pickup(third); Assert.That(incoming.Quantity, Is.EqualTo(7));
            Assert.That(third.Location, Is.EqualTo(QusapLootLocation.Merged)); Assert.That(third.Quantity, Is.Zero);
            var next = Cargo(1); Assert.That(next.LootInstanceId, Is.EqualTo("isolated-raid/loot/4"));
            Assert.That(raid.World.Instances.Select(i => i.LootInstanceId).Distinct().Count(), Is.EqualTo(4));
        }
        [Test] public void FullBackpackRejectsWholeAcquisitionWithoutPartiallyMutatingStacks()
        {
            using var one = raid.Register("limited", "limited-profile", 1); var first = Cargo(6); var incoming = Cargo(7);
            Assert.That(one.TryPickup(first.LootInstanceId), Is.EqualTo(QusapLootResult.Success));
            Assert.That(one.TryPickup(incoming.LootInstanceId), Is.EqualTo(QusapLootResult.Full));
            Assert.That(first.Quantity, Is.EqualTo(6)); Assert.That(incoming.Quantity, Is.EqualTo(7)); Assert.That(incoming.Location, Is.EqualTo(QusapLootLocation.World));
        }
        [Test] public void FullSixSlotsCanAcceptQuantityThatFitsAnExistingPartial()
        {
            var partial = Cargo(6); Pickup(partial);
            for (int i = 0; i < 5; i++) Pickup(Cargo(10, "different provenance " + i));
            var extra = Cargo(3); Pickup(extra); Assert.That(partial.Quantity, Is.EqualTo(9));
            Assert.That(player.ReadInventorySnapshot().Backpack.Count(i => i.HasValue), Is.EqualTo(6));
        }
        [Test] public void DifferentPersistentPropertiesPreventMerging()
        { var first = Cargo(6, "origin A"); var second = Cargo(7, "origin B"); Pickup(first); Pickup(second); Assert.That(first.Quantity, Is.EqualTo(6)); Assert.That(second.Quantity, Is.EqualTo(7)); Assert.That(second.SlotIndex, Is.EqualTo(1)); }
        [Test] public void WholePocketTransfersPreserveIdentityAndQuantityWithoutImplicitFusion()
        {
            var first = Cargo(6); Pickup(first); Assert.That(player.TryMoveToSecurePocket(first.LootInstanceId), Is.EqualTo(QusapLootResult.Success));
            var second = Cargo(3); Pickup(second); Assert.That(player.TryMoveToBackpack(first.LootInstanceId), Is.EqualTo(QusapLootResult.Success));
            Assert.That(first.Quantity, Is.EqualTo(6)); Assert.That(second.Quantity, Is.EqualTo(3)); Assert.That(player.ReadInventorySnapshot().Backpack.Count(i => i.HasValue), Is.EqualTo(2));
        }
        [Test] public void BackpackAndPocketRulesAreAuthoritative()
        {
            material.ConfigureMetadata(QusapLootRarity.Common, 10, 1, "raid_pickup_common", backpack: false);
            Assert.That(player.TryPickup(Cargo(1).LootInstanceId), Is.EqualTo(QusapLootResult.Ineligible));
            var relic = raid.World.Create(Definition("relic", QusapLootCategory.Relic), "relic"); Pickup(relic);
            Assert.That(player.TryMoveToSecurePocket(relic.LootInstanceId), Is.EqualTo(QusapLootResult.Success));
            var armor = raid.World.Create(Definition("armor", QusapLootCategory.Armor), "armor"); Pickup(armor);
            Assert.That(player.TryMoveToSecurePocket(armor.LootInstanceId), Is.EqualTo(QusapLootResult.Ineligible));
        }
        [Test] public void ConsumingOneUnitPreservesRemainingStackAndConsumeOnUseIsExplicit()
        {
            var d = Definition("heal", QusapLootCategory.Consumable, 2); var item = raid.World.Create(d, "test", quantity: 2); Pickup(item);
            float requested = 0; Assert.That(player.TryConsume(item.LootInstanceId, n => { requested = n; return n; }), Is.EqualTo(QusapLootResult.Success));
            Assert.That(requested, Is.EqualTo(25)); Assert.That(item.Quantity, Is.EqualTo(1)); Assert.That(item.Location, Is.EqualTo(QusapLootLocation.Backpack));
            d.ConfigureMetadata(QusapLootRarity.Legendary, 2, 0, "raid_pickup_legendary", consume: false);
            player.TryConsume(item.LootInstanceId, n => n); Assert.That(item.Quantity, Is.EqualTo(1));
            Assert.That(player.TryConsume(item.LootInstanceId, _ => 0), Is.EqualTo(QusapLootResult.NoEffect)); Assert.That(item.Quantity, Is.EqualTo(1));
        }
        [TestCase(0)] [TestCase(-1)] public void NonpositiveWeightsInvalidateTable(int weight)
        { Assert.That(Table(new QusapLootTableEntry(material.DefinitionId, weight)).Validate(Catalog(material)), Has.Some.Contains("Weight")); }
        [Test] public void UnknownDefinitionAndInvalidMinMaxInvalidateTable()
        {
            var c = Catalog(material);
            Assert.That(Table(new QusapLootTableEntry("missing", 1)).Validate(c), Has.Some.Contains("Unknown"));
            foreach (var pair in new[] { (0, 1), (3, 2), (1, 11) }) Assert.That(Table(new QusapLootTableEntry(material.DefinitionId, 1, pair.Item1, pair.Item2)).Validate(c), Has.Some.Contains("min/max"));
        }
        [Test] public void MinimumRarityAndSingleInstanceTableRulesAreValidated()
        {
            Assert.That(Table(new QusapLootTableEntry(material.DefinitionId, 1, tier: QusapLootRarity.Rare)).Validate(Catalog(material)), Has.Some.Contains("rarity"));
            var relic = Definition("relic", QusapLootCategory.Relic);
            Assert.That(Table(new QusapLootTableEntry(relic.DefinitionId, 1, 1, 2)).Validate(Catalog(relic)), Has.Some.Contains("min/max"));
        }
        [Test] public void SameSeedIsDeterministicDifferentSeedChangesSequenceAndGlobalRandomIsUntouched()
        {
            var other = Definition("other", QusapLootCategory.Material, 5, rarity: QusapLootRarity.Uncommon);
            var c = Catalog(material, other); var t = Table(new QusapLootTableEntry(material.DefinitionId, 45, 1, 10), new QusapLootTableEntry(other.DefinitionId, 25, 1, 5));
            var state = UnityEngine.Random.state;
            var a = t.Roll(c, 4107, 100).Select(r => r.ToString()).ToArray();
            Assert.That(t.Roll(c, 4107, 100).Select(r => r.ToString()), Is.EqualTo(a));
            Assert.That(t.Roll(c, 4108, 100).Select(r => r.ToString()), Is.Not.EqualTo(a)); Assert.That(UnityEngine.Random.state, Is.EqualTo(state));
        }
        [Test] public void ProvisionalWeightedTableHasExactWeightsAndExpectedDistribution()
        {
            var c = AssetDatabase.LoadAssetAtPath<QusapRaidLootCatalog>("Assets/_Qusap/Settings/RaidLootCatalog/RaidLootCatalog.asset");
            var t = AssetDatabase.LoadAssetAtPath<QusapRaidLootTable>("Assets/_Qusap/Settings/RaidLootCatalog/RaidTestRouteLoot.asset");
            Assert.That(c.Validate(), Is.Empty); Assert.That(t.Validate(c), Is.Empty);
            Assert.That(t.Entries.Select(e => e.Weight), Is.EqualTo(new[] { 45, 25, 15, 8, 4, 2, 1 }));
            var draws = t.Roll(c, 4107, 10000);
            foreach (var entry in t.Entries) { c.TryResolve(entry.DefinitionId, out var d); int actual = draws.Count(r => r.DefinitionId == d.DefinitionId); Assert.That(actual, Is.InRange(entry.Weight * 100 - 250, entry.Weight * 100 + 250)); }
            Assert.That(draws.All(r => r.Quantity >= 1 && r.Quantity <= r.Definition.MaxStack), Is.True);
        }
        [Test] public void PersistentStacksRoundTripAndResolveCurrentMetadataWithoutStoringRarity()
        {
            var repo = new QusapPersistentStashRepository(directory, new[] { material }, "anonymous-profile");
            using var session = new QusapRaidSession("persisted-raid", repo); var p = session.Register("local", repo.ProfileId);
            var item = session.World.Create(material, "same", quantity: 7); p.TryPickup(item.LootInstanceId); session.SettleExtraction(p);
            var before = File.ReadAllText(repo.Storage.MainPath);
            material.ConfigureMetadata(QusapLootRarity.Rare, 10, 8, "raid_pickup_rare");
            var loaded = new QusapPersistentStashRepository(directory, new[] { material }); var snapshot = loaded.ReadStashSnapshot(loaded.ProfileId).Single();
            Assert.That(snapshot.LootInstanceId, Is.EqualTo(item.LootInstanceId)); Assert.That(snapshot.Quantity, Is.EqualTo(7)); Assert.That(snapshot.Rarity, Is.EqualTo(QusapLootRarity.Rare));
            Assert.That(loaded.SchemaVersion, Is.EqualTo(1)); Assert.That(loaded.Storage.Writes, Is.Zero); Assert.That(File.ReadAllText(repo.Storage.MainPath), Is.EqualTo(before)); Assert.That(before, Does.Not.Contain("Rarity"));
        }
        [Test] public void ApprovedLegacyV1WithoutQuantityLoadsExactlyWithoutWriting()
        {
            var fragment = AssetDatabase.LoadAssetAtPath<QusapLootDefinition>("Assets/_Qusap/Settings/RaidInventory/Fragment.asset");
            const string content = "{\"SchemaVersion\":1,\"ProfileId\":\"anonymous-legacy-profile\",\"Revision\":1,\"SavedAtUtc\":\"2026-10-10T00:00:00.0000000Z\",\"NextItemInstanceId\":8,\"NextNativeWeaponInstanceId\":4,\"Stash\":[{\"InstanceId\":\"legacy-raid/loot/7\",\"DefinitionId\":\"raid_fragment\",\"RaidId\":\"legacy-raid\",\"Provenance\":\"anonymous legacy origin\",\"NativeWeaponInstanceId\":0,\"NativeWeaponDefinitionId\":\"\"}],\"Settlements\":[{\"SettlementId\":\"legacy-raid/extraction/P1\",\"Fingerprint\":\"anonymous-legacy-profile\\nlegacy-raid/loot/7\"}]}";
            string json = "{\"Content\":" + content + ",\"Checksum\":\"" + QusapProfileCodec.Sha256(content) + "\"}";
            var path = Path.Combine(directory, QusapProfileFileStore.FileName); File.WriteAllText(path, json);
            var codec = new QusapProfileCodec(new[] { fragment }); Assert.That(codec.TryDecode(json, out var decoded, out var error, out _), Is.True, error);
            Assert.That(QusapProfileCodec.Encode(decoded), Is.EqualTo(json));
            var repo = new QusapPersistentStashRepository(directory, new[] { fragment }); var item = repo.ReadStashSnapshot(repo.ProfileId).Single();
            Assert.That(item.LootInstanceId, Is.EqualTo("legacy-raid/loot/7")); Assert.That(item.DefinitionId, Is.EqualTo("raid_fragment")); Assert.That(item.Quantity, Is.EqualTo(1));
            Assert.That(repo.Storage.Source, Is.EqualTo(QusapProfileLoadSource.Main)); Assert.That(repo.SchemaVersion, Is.EqualTo(1)); Assert.That(repo.Storage.Writes, Is.Zero); Assert.That(File.ReadAllText(path), Is.EqualTo(json));
        }
        [Test] public void DeathAndExtractionApplyExplicitDropAndStashRulesToWholeStacks()
        {
            var safe = raid.World.Create(Definition("relic", QusapLootCategory.Relic), "relic"); Pickup(safe); player.TryMoveToSecurePocket(safe.LootInstanceId);
            var cargo = Cargo(7); Pickup(cargo); player.BlockOperations(); Assert.That(raid.SettleDeath(player), Is.EqualTo(QusapLootResult.Success));
            Assert.That(raid.Stash.ReadStashSnapshot(player.ProfileId).Single().LootInstanceId, Is.EqualTo(safe.LootInstanceId));
            Assert.That(raid.World.ReadContainer(raid.Containers.Single().ContainerId).Single().Quantity, Is.EqualTo(7));
            var noDrop = Definition("discard", QusapLootCategory.Material, 3); noDrop.ConfigureMetadata(QusapLootRarity.Common, 3, 0, "raid_pickup_common", dropOnDeath: false, persist: false);
            using var s = new QusapRaidSession("discard-raid"); var p = s.Register("other", "other-profile"); var item = s.World.Create(noDrop, "test", quantity: 3); p.TryPickup(item.LootInstanceId); p.BlockOperations(); s.SettleDeath(p);
            Assert.That(s.World.ReadContainer(s.Containers.Single().ContainerId), Is.Empty); Assert.That(item.Location, Is.EqualTo(QusapLootLocation.Consumed));
        }
    }
}
