using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using NUnit.Framework;
using UnityEngine;

namespace Qusap.Tests
{
    public sealed class QusapLocalProfileEditModeTests
    {
        private string directory;
        private QusapLootDefinition fragment, relic, weaponLoot;
        private QusapWeaponVisualCatalog weaponCatalog;
        private QusapLootDefinition[] definitions;
        private QusapPersistentStashRepository repository;
        private QusapRaidSession raid;
        private QusapRaidInventoryState player;
        private readonly List<QusapLootDefinition> owned = new();
        private const string Profile = "isolated-test-profile";
        private string Main => Path.Combine(directory, QusapProfileFileStore.FileName);

        [SetUp] public void Setup()
        {
            directory = Path.Combine(Path.GetTempPath(), "QusapProfileTests", Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(directory);
            fragment = Def("fragment", QusapLootCategory.Fragment); relic = Def("relic", QusapLootCategory.Relic);
            weaponCatalog = UnityEditor.AssetDatabase.LoadAssetAtPath<QusapWeaponVisualCatalog>(
                "Assets/_Qusap/Settings/Weapons/QusapWeaponVisualCatalog.asset");
            if (weaponCatalog == null)
                weaponCatalog = UnityEditor.AssetDatabase.FindAssets("t:QusapWeaponVisualCatalog")
                    .Select(g => UnityEditor.AssetDatabase.LoadAssetAtPath<QusapWeaponVisualCatalog>(UnityEditor.AssetDatabase.GUIDToAssetPath(g))).First();
            weaponLoot = Def("sword", QusapLootCategory.Weapon);
            weaponLoot.Configure("sword", "Identifiable sword", QusapLootCategory.Weapon, false,
                catalog: weaponCatalog, weaponId: QusapWeaponVisualCatalog.BlueDefinitionId);
            definitions = new[] { fragment, relic, weaponLoot };
            Open();
        }
        private QusapLootDefinition Def(string id, QusapLootCategory category)
        {
            var definition = ScriptableObject.CreateInstance<QusapLootDefinition>();
            definition.Configure(id, id, category, category != QusapLootCategory.Weapon); owned.Add(definition); return definition;
        }
        private void Open()
        {
            raid?.Dispose(); repository = new QusapPersistentStashRepository(directory, definitions, Profile);
            raid = new QusapRaidSession("isolated-raid-" + Guid.NewGuid().ToString("N"), repository); player = raid.Register("P1", repository.ProfileId);
        }
        [TearDown] public void Cleanup()
        {
            raid?.Dispose(); foreach (var definition in owned) UnityEngine.Object.DestroyImmediate(definition); owned.Clear();
            // This exact GUID directory was created by this test. Never delete a parent or wildcard.
            string expectedParent = Path.GetFullPath(Path.Combine(Path.GetTempPath(), "QusapProfileTests")) + Path.DirectorySeparatorChar;
            string exact = Path.GetFullPath(directory);
            if (!exact.StartsWith(expectedParent, StringComparison.OrdinalIgnoreCase)
                || !Guid.TryParseExact(Path.GetFileName(exact), "N", out _)) throw new InvalidOperationException("Unsafe test cleanup target.");
            if (Directory.Exists(exact)) Directory.Delete(exact, true);
        }
        private QusapLootInstance Bag(QusapLootDefinition definition = null, ulong nativeId = 0)
        {
            var d = definition ?? fragment;
            var weapon = nativeId == 0 ? null : new QusapWeaponInstance(nativeId, d.WeaponDefinition);
            var item = raid.World.Create(d, "original provenance", weapon);
            Assert.That(player.TryPickup(item.LootInstanceId), Is.EqualTo(QusapLootResult.Success)); return item;
        }
        private QusapLootInstance SaveFirst()
        { var item = Bag(); Assert.That(raid.SettleExtraction(player), Is.EqualTo(QusapLootResult.Success)); return item; }
        private QusapProfileDocument Document()
        { Assert.That(new QusapProfileCodec(definitions).TryDecode(File.ReadAllText(Main), out var document, out _, out _), Is.True); return document; }
        private void Rewrite(Action<QusapProfileContent> mutate)
        { var document = Document(); mutate(document.Content); File.WriteAllText(Main, QusapProfileCodec.Encode(QusapProfileCodec.Seal(document.Content))); }

        [Test] public void NewEmptyProfileDoesNotWriteAndUsesOnlyInjectedDirectory()
        {
            Assert.That(repository.Revision, Is.Zero); Assert.That(repository.Storage.Source, Is.EqualTo(QusapProfileLoadSource.New));
            Assert.That(repository.ReadStashSnapshot(Profile), Is.Empty); Assert.That(repository.Storage.Writes, Is.Zero);
            Assert.That(File.Exists(Main), Is.False); Assert.That(repository.Storage.DirectoryPath, Is.EqualTo(directory));
            Assert.That(Path.GetFullPath(directory), Is.Not.EqualTo(Path.GetFullPath(QusapLocalProfilePersistence.ProductionDirectory)));
        }
        [Test] public void CanonicalSerializationIsDeterministicAcrossRepeatedEncodingAndInputOrder()
        {
            Bag(); Bag(relic); SaveFirst(); var document = Document(); string expected = File.ReadAllText(Main);
            Array.Reverse(document.Content.Stash); Array.Reverse(document.Content.Settlements);
            Assert.That(QusapProfileCodec.Encode(QusapProfileCodec.Seal(document.Content)), Is.EqualTo(expected));
            Assert.That(QusapProfileCodec.Encode(QusapProfileCodec.Seal(document.Content)), Is.EqualTo(expected));
        }
        [Test] public void ExactRoundTripRetainsLootDefinitionProvenanceAndNativeWeaponIds()
        {
            var a = Bag(); var b = Bag(relic); var sword = Bag(weaponLoot, 76);
            var ids = new[] { a.LootInstanceId, b.LootInstanceId, sword.LootInstanceId };
            Assert.That(raid.SettleExtraction(player), Is.EqualTo(QusapLootResult.Success)); string profileId = repository.ProfileId;
            var loaded = new QusapPersistentStashRepository(directory, definitions);
            var stash = loaded.ReadStashSnapshot(profileId);
            CollectionAssert.AreEquivalent(ids, stash.Select(i => i.LootInstanceId));
            CollectionAssert.AreEquivalent(new[] { "fragment", "relic", "sword" }, stash.Select(i => i.DefinitionId));
            Assert.That(stash.All(i => i.Provenance == "original provenance" && i.Location == QusapLootLocation.Stash), Is.True);
            var restoredSword = stash.Single(i => i.DefinitionId == "sword").Weapon;
            Assert.That(restoredSword.InstanceId, Is.EqualTo(76)); Assert.That(restoredSword.Definition.Id, Is.EqualTo(sword.Weapon.Definition.Id));
            Assert.That(restoredSword.OwnerEntityId, Is.Null); Assert.That(loaded.ProfileId, Is.EqualTo(profileId));
            Assert.That(loaded.Revision, Is.EqualTo(1)); Assert.That(loaded.NextNativeWeaponInstanceId, Is.GreaterThan(76));
            Assert.That(loaded.Storage.Source, Is.EqualTo(QusapProfileLoadSource.Main));
            Assert.That(Document().Content.Stash.Length, Is.EqualTo(3), "No quantity model exists; every item persists separately.");
        }
        [Test] public void RestoredAllocatorContinuesBeyondEveryIssuedAndRestoredId()
        {
            SaveFirst(); var loaded = new QusapPersistentStashRepository(directory, definitions);
            using var nextRaid = new QusapRaidSession(raid.World.RaidId, loaded);
            var next = nextRaid.World.Create(fragment, "new process");
            Assert.That(next.LootInstanceId, Is.EqualTo(raid.World.RaidId + "/loot/2"));
            Assert.That(loaded.ReadStashSnapshot(Profile).Any(i => i.LootInstanceId == next.LootInstanceId), Is.False);
            var generator = new QusapWeaponIdGenerator(); generator.ReserveThrough(100); Assert.That(generator.Next(), Is.EqualTo(101));
        }
        [Test] public void RevisionIsMonotonicAndPreviousValidMainBecomesBackup()
        {
            SaveFirst(); string first = File.ReadAllText(Main); Open(); Bag(); Assert.That(raid.SettleExtraction(player), Is.EqualTo(QusapLootResult.Success));
            Assert.That(repository.Revision, Is.EqualTo(2)); Assert.That(repository.Storage.Writes, Is.EqualTo(1));
            Assert.That(File.ReadAllText(Main + ".bak"), Is.EqualTo(first)); Assert.That(File.Exists(Main + ".tmp"), Is.False);
            Open(); Assert.That(repository.Revision, Is.EqualTo(2)); Assert.That(repository.ReadStashSnapshot(Profile).Count, Is.EqualTo(2));
        }
        [Test] public void DuplicateSignalsAndNormalClosureProduceNoAdditionalWritesOrRevision()
        {
            var item = SaveFirst(); string bytes = File.ReadAllText(Main);
            Assert.That(repository.TryCommitSettlement(raid.World.RaidId + "/extraction/P1", Profile, new[] { item }), Is.True);
            Assert.That(raid.SettleExtraction(player), Is.EqualTo(QusapLootResult.Blocked)); repository.ConfirmUnchanged(); repository.ConfirmUnchanged();
            Assert.That(repository.Revision, Is.EqualTo(1)); Assert.That(repository.Storage.Writes, Is.EqualTo(1)); Assert.That(File.ReadAllText(Main), Is.EqualTo(bytes));
        }
        [Test] public void SettlementReceiptSurvivesReloadAndRejectsConflictingPayload()
        {
            var item = SaveFirst(); var loaded = new QusapPersistentStashRepository(directory, definitions);
            Assert.That(loaded.TryCommitSettlement(raid.World.RaidId + "/extraction/P1", Profile, new[] { item }), Is.True);
            Assert.That(loaded.TryCommitSettlement(raid.World.RaidId + "/extraction/P1", Profile, Array.Empty<QusapLootInstance>()), Is.False);
            Assert.That(loaded.Revision, Is.EqualTo(1)); Assert.That(loaded.Storage.Writes, Is.Zero);
        }
        [Test] public void EmptyTransferDoesNotPersistOrIncrementRevision()
        { Assert.That(raid.SettleExtraction(player), Is.EqualTo(QusapLootResult.Success)); Assert.That(repository.Revision, Is.Zero); Assert.That(File.Exists(Main), Is.False); }
        [Test] public void EmptyReceiptDoesNotInvalidateLaterNonemptySettlement()
        {
            raid.SettleExtraction(player); var second = raid.Register("P2", Profile); var item = raid.World.Create(fragment, "second"); second.TryPickup(item.LootInstanceId);
            Assert.That(raid.SettleExtraction(second), Is.EqualTo(QusapLootResult.Success)); Assert.That(repository.Revision, Is.EqualTo(1)); Open(); Assert.That(repository.Revision, Is.EqualTo(1));
        }
        [Test] public void DeathPersistsOnlyPocketOnceAndKeepsDeathCargoOutOfSave()
        {
            var bag = Bag(); var safe = Bag(relic); player.TryMoveToSecurePocket(safe.LootInstanceId); player.BlockOperations();
            Assert.That(raid.SettleDeath(player), Is.EqualTo(QusapLootResult.Success)); Assert.That(raid.SettleDeath(player), Is.EqualTo(QusapLootResult.Success));
            Assert.That(repository.Storage.Writes, Is.EqualTo(1)); Assert.That(repository.Revision, Is.EqualTo(1));
            Assert.That(bag.Location, Is.EqualTo(QusapLootLocation.DeathContainer)); Assert.That(safe.Location, Is.EqualTo(QusapLootLocation.Stash));
            Assert.That(Document().Content.Stash.Single().InstanceId, Is.EqualTo(safe.LootInstanceId)); Open(); Assert.That(repository.ReadStashSnapshot(Profile).Count, Is.EqualTo(1));
        }
        [Test] public void OtherLocalTestParticipantKeepsIndependentMemoryStash()
        {
            var other = raid.Register("P2", "different-profile"); var item = raid.World.Create(fragment, "other"); other.TryPickup(item.LootInstanceId); raid.SettleExtraction(other);
            Assert.That(repository.ReadStashSnapshot("different-profile").Count, Is.EqualTo(1)); Assert.That(repository.ReadStashSnapshot(Profile), Is.Empty);
            Assert.That(repository.Revision, Is.Zero); Assert.That(File.Exists(Main), Is.False);
        }
        [Test] public void IncompleteTemporaryIsIgnoredAndRetainedWithoutOverwriting()
        {
            SaveFirst(); File.WriteAllText(Main + ".tmp", "{incomplete"); Open();
            Assert.That(repository.Revision, Is.EqualTo(1)); Bag(); Assert.That(raid.SettleExtraction(player), Is.EqualTo(QusapLootResult.StashUnavailable));
            Assert.That(File.ReadAllText(Main + ".tmp"), Is.EqualTo("{incomplete")); Assert.That(repository.Revision, Is.EqualTo(1)); Assert.That(player.CanOperate, Is.True);
        }
        [Test] public void BackupOnlyLoadsAndCanPublishNewMainWithoutLosingBackup()
        {
            SaveFirst(); string first = File.ReadAllText(Main); File.Move(Main, Main + ".bak"); Open();
            Assert.That(repository.Storage.Source, Is.EqualTo(QusapProfileLoadSource.Backup)); Assert.That(repository.Revision, Is.EqualTo(1));
            Bag(); Assert.That(raid.SettleExtraction(player), Is.EqualTo(QusapLootResult.Success)); Assert.That(repository.Revision, Is.EqualTo(2)); Assert.That(File.ReadAllText(Main + ".bak"), Is.EqualTo(first));
        }
        [Test] public void CorruptMainRecoversBackupWithoutDuplicatesAndPreservesCorruptMain()
        {
            SaveFirst(); string valid = File.ReadAllText(Main); File.WriteAllText(Main + ".bak", valid); File.WriteAllText(Main, "CORRUPT"); Open();
            Assert.That(repository.Storage.Source, Is.EqualTo(QusapProfileLoadSource.Backup)); Assert.That(repository.ReadStashSnapshot(Profile).Count, Is.EqualTo(1));
            Assert.That(repository.Storage.CanWrite, Is.False); Assert.That(File.ReadAllText(Main), Is.EqualTo("CORRUPT"));
            Bag(); Assert.That(raid.SettleExtraction(player), Is.EqualTo(QusapLootResult.StashUnavailable)); Assert.That(File.ReadAllText(Main), Is.EqualTo("CORRUPT"));
        }
        [Test] public void BothCorruptFilesStartSafeEmptyProfileWithoutDeletingOrThrowing()
        {
            File.WriteAllText(Main, "BAD MAIN"); File.WriteAllText(Main + ".bak", "BAD BACKUP"); Assert.DoesNotThrow(Open);
            Assert.That(repository.Storage.Source, Is.EqualTo(QusapProfileLoadSource.Recovery)); Assert.That(repository.Revision, Is.Zero);
            Assert.That(repository.ReadStashSnapshot(Profile), Is.Empty); Assert.That(repository.Storage.CanWrite, Is.False);
            Assert.That(File.ReadAllText(Main), Is.EqualTo("BAD MAIN")); Assert.That(File.ReadAllText(Main + ".bak"), Is.EqualTo("BAD BACKUP"));
        }
        [Test] public void WrongChecksumIsRejectedDeterministically()
        { SaveFirst(); var document = Document(); document.Checksum = new string('0', 64); File.WriteAllText(Main, QusapProfileCodec.Encode(document)); Open(); Assert.That(repository.Storage.Source, Is.EqualTo(QusapProfileLoadSource.Recovery)); Assert.That(repository.LastResult, Does.Contain("Checksum")); }
        [Test] public void DuplicateLootInstanceInvalidatesEntireLoad()
        { SaveFirst(); Rewrite(c => c.Stash = c.Stash.Concat(c.Stash).ToArray()); Open(); Assert.That(repository.ReadStashSnapshot(Profile), Is.Empty); Assert.That(repository.Storage.CanWrite, Is.False); }
        [Test] public void UnknownDefinitionRejectsObjectWithoutSubstitutingAnotherDefinition()
        { SaveFirst(); Rewrite(c => c.Stash[0].DefinitionId = "unknown-definition"); Open(); Assert.That(repository.ReadStashSnapshot(Profile), Is.Empty); Assert.That(repository.LastResult, Does.Contain("unknown-definition")); }
        [Test] public void InvalidNextItemCounterRejectsLoad()
        { SaveFirst(); Rewrite(c => c.NextItemInstanceId = 1); Open(); Assert.That(repository.ReadStashSnapshot(Profile), Is.Empty); Assert.That(repository.Storage.CanWrite, Is.False); }
        [Test] public void FutureMainIsPreservedAndNeverReplacedByValidOldBackup()
        {
            SaveFirst(); File.Copy(Main, Main + ".bak"); Rewrite(c => c.SchemaVersion = 2); string future = File.ReadAllText(Main); Open();
            Assert.That(repository.Storage.CanWrite, Is.False); Assert.That(repository.LastResult, Does.Contain("Incompatible future"));
            Bag(); Assert.That(raid.SettleExtraction(player), Is.EqualTo(QusapLootResult.StashUnavailable)); Assert.That(File.ReadAllText(Main), Is.EqualTo(future));
        }
        [Test] public void FutureBackupIsPreservedEvenWhenMainIsValid()
        {
            SaveFirst(); var future = Document(); future.Content.SchemaVersion = 2;
            string futureBytes = QusapProfileCodec.Encode(QusapProfileCodec.Seal(future.Content)); File.WriteAllText(Main + ".bak", futureBytes); Open();
            Assert.That(repository.Revision, Is.EqualTo(1)); Assert.That(repository.Storage.CanWrite, Is.False); Assert.That(File.ReadAllText(Main + ".bak"), Is.EqualTo(futureBytes));
        }
        [Test] public void UnknownOrOmittedJsonFieldsAreRejectedRatherThanExcludedFromChecksum()
        { SaveFirst(); string original = File.ReadAllText(Main); File.WriteAllText(Main, original.Insert(1, "\"Unknown\":3,")); Open(); Assert.That(repository.Storage.CanWrite, Is.False); Assert.That(repository.ReadStashSnapshot(Profile), Is.Empty); }
        [Test] public void StaleRepositoryCannotOverwriteAnExternallyCommittedRevision()
        {
            var stale = new QusapPersistentStashRepository(directory, definitions, Profile); SaveFirst();
            using var staleRaid = new QusapRaidSession("stale", stale); var state = staleRaid.Register("P", Profile);
            var item = staleRaid.World.Create(fragment, "stale"); state.TryPickup(item.LootInstanceId);
            string bytes = File.ReadAllText(Main); Assert.That(staleRaid.SettleExtraction(state), Is.EqualTo(QusapLootResult.StashUnavailable));
            Assert.That(File.ReadAllText(Main), Is.EqualTo(bytes)); Assert.That(stale.Revision, Is.Zero);
        }

        private sealed class FailureFiles : IQusapProfileFiles
        {
            public readonly QusapProfileFiles Real = new();
            public bool FailWrite, FailReplace, Truncate;
            public int Replacements;
            public bool Exists(string path) => Real.Exists(path);
            public string Read(string path) => Real.Read(path);
            public void CreateDirectory(string path) => Real.CreateDirectory(path);
            public void WriteClosedAndFlushed(string path, string contents)
            { if (FailWrite) throw new IOException("injected write failure"); Real.WriteClosedAndFlushed(path, Truncate ? "{" : contents); }
            public void Replace(string temporary, string main, string backup)
            { Replacements++; if (FailReplace) throw new IOException("injected atomic replacement failure"); Real.Replace(temporary, main, backup); }
            public void Move(string temporary, string main) => Real.Move(temporary, main);
        }
        [Test] public void ControlledWriteExceptionLeavesInventoryAndRevisionUnchangedAndCanRetry()
        {
            var files = new FailureFiles { FailWrite = true }; var store = new QusapPersistentStashRepository(directory, definitions, Profile, files);
            using var model = new QusapRaidSession("write-failure", store); var p = model.Register("P1", Profile);
            var item = model.World.Create(fragment, "failure"); p.TryPickup(item.LootInstanceId);
            Assert.That(model.SettleExtraction(p), Is.EqualTo(QusapLootResult.StashUnavailable)); Assert.That(p.CanOperate, Is.True);
            Assert.That(item.Location, Is.EqualTo(QusapLootLocation.Backpack)); Assert.That(store.Revision, Is.Zero); Assert.That(File.Exists(Main), Is.False);
            files.FailWrite = false; Assert.That(model.SettleExtraction(p), Is.EqualTo(QusapLootResult.Success)); Assert.That(store.Revision, Is.EqualTo(1));
        }
        [Test] public void AtomicReplaceFailureRetainsOnlyValidMainAndDoesNotCommitMemory()
        {
            SaveFirst(); string valid = File.ReadAllText(Main); var files = new FailureFiles { FailReplace = true };
            var store = new QusapPersistentStashRepository(directory, definitions, Profile, files);
            using var model = new QusapRaidSession("replace-failure", store); var p = model.Register("P", Profile); var item = model.World.Create(fragment, "failure"); p.TryPickup(item.LootInstanceId);
            Assert.That(model.SettleExtraction(p), Is.EqualTo(QusapLootResult.StashUnavailable)); Assert.That(store.Revision, Is.EqualTo(1));
            Assert.That(File.ReadAllText(Main), Is.EqualTo(valid)); Assert.That(store.ReadStashSnapshot(Profile).Count, Is.EqualTo(1)); Assert.That(files.Replacements, Is.EqualTo(1));
            Assert.That(File.Exists(Main + ".tmp"), Is.True); Assert.That(item.Location, Is.EqualTo(QusapLootLocation.Backpack));
        }
        [Test] public void TruncatedTemporaryNeverReplacesOnlyValidMain()
        {
            SaveFirst(); string valid = File.ReadAllText(Main); var files = new FailureFiles { Truncate = true };
            var store = new QusapPersistentStashRepository(directory, definitions, Profile, files);
            using var model = new QusapRaidSession("truncate", store); var p = model.Register("P", Profile); var item = model.World.Create(fragment, "failure"); p.TryPickup(item.LootInstanceId);
            Assert.That(model.SettleExtraction(p), Is.EqualTo(QusapLootResult.StashUnavailable)); Assert.That(File.ReadAllText(Main), Is.EqualTo(valid)); Assert.That(files.Replacements, Is.Zero);
        }
        [Test] public void SavedDtoExcludesTransientRaidAndUnityState()
        {
            SaveFirst(); string json = File.ReadAllText(Main);
            foreach (string field in new[] { "GameObject", "MonoBehaviour", "ScriptableObject", "OwnerEntityId", "CurrentHealth", "Velocity", "Animator", "Hitstun", "Position", "Extracted", "Eliminated", "SlotIndex", "HolderId", "Rarity", "MaxStack", "BaseValue", "HealAmount", "Tags" })
                Assert.That(json, Does.Not.Contain(field));
            Assert.That(json, Does.Contain("\"Quantity\":1")); // Quantity is now persistent primitive data, not runtime state.
        }
    }
}
