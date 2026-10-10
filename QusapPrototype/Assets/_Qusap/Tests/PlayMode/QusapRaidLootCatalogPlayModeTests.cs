#if UNITY_EDITOR
using System;
using System.Collections;
using System.IO;
using System.Linq;
using System.Reflection;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

namespace Qusap.Tests
{
    public sealed class QusapRaidLootCatalogPlayModeTests
    {
        private const string ScenePath = "Assets/_Qusap/Scenes/RaidLootCatalogPlayground.unity";
        private string directory;
        private QusapRaidLootCatalogPlayground playground;
        private QusapRaidInventory p1;
        private QusapRaidBootstrap raid;
        private QusapPersistentStashRepository repository;
        private InputTestFixture devices;
        private object runtime;
        private float scale, step;
        private void Clock()
        {
            runtime.GetType().GetProperty("currentTimeOffsetToRealtimeSinceStartup").SetValue(runtime, 0d);
            runtime.GetType().GetProperty("currentTimeForFixedUpdate").SetValue(runtime, devices.currentTime);
        }
        private IEnumerator Tick(float seconds = .12f)
        { float until = Time.time + seconds; while (Time.time < until) { devices.currentTime += Time.deltaTime; Clock(); InputSystem.Update(); yield return null; } }
        [UnitySetUp] public IEnumerator Setup()
        {
            scale = Time.timeScale; step = Time.fixedDeltaTime;
            directory = Path.Combine(Path.GetTempPath(), "QusapRaidLootPlayTests", Guid.NewGuid().ToString("N")); Directory.CreateDirectory(directory);
            QusapLocalProfilePersistence.IsolatedStorageDirectory = directory;
            devices = new InputTestFixture(); devices.Setup(); InputSystem.AddDevice<Keyboard>(); InputSystem.AddDevice<Gamepad>();
            runtime = typeof(InputTestFixture).GetProperty("runtime", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(devices);
            runtime.GetType().GetProperty("advanceTimeEachDynamicUpdate").SetValue(runtime, 0d);
            var load = EditorSceneManager.LoadSceneAsyncInPlayMode(ScenePath, new LoadSceneParameters(LoadSceneMode.Single)); while (!load.isDone) yield return null;
            new GameObject("RaidLoot_TestClock").AddComponent<QusapVitalityTestClock>().Synchronize = Clock;
            foreach (var pickup in UnityEngine.Object.FindObjectsByType<QusapLootPickup>()) pickup.GetComponent<Collider>().enabled = false;
            yield return Tick(.3f);
            playground = UnityEngine.Object.FindAnyObjectByType<QusapRaidLootCatalogPlayground>(); raid = playground.Raid; p1 = raid.Participants[0];
            repository = raid.GetComponent<QusapLocalProfilePersistence>().Repository;
            Assert.That(UnityEngine.Object.FindAnyObjectByType<QusapRaidSessionObserver>().StartSession(), Is.True);
        }
        [UnityTearDown] public IEnumerator Cleanup()
        {
            foreach (var root in SceneManager.GetActiveScene().GetRootGameObjects()) { root.SetActive(false); UnityEngine.Object.Destroy(root); }
            yield return null; yield return null; devices.TearDown(); QusapLocalProfilePersistence.IsolatedStorageDirectory = null;
            Assert.That(Time.timeScale, Is.EqualTo(scale)); Assert.That(Time.fixedDeltaTime, Is.EqualTo(step));
            string parent = Path.GetFullPath(Path.Combine(Path.GetTempPath(), "QusapRaidLootPlayTests")) + Path.DirectorySeparatorChar;
            string exact = Path.GetFullPath(directory);
            if (!exact.StartsWith(parent, StringComparison.OrdinalIgnoreCase) || !Guid.TryParseExact(Path.GetFileName(exact), "N", out _)) throw new InvalidOperationException("Unsafe cleanup target.");
            if (Directory.Exists(exact)) Directory.Delete(exact, true);
        }
        private QusapLootInstance Bag(string alias, int quantity = 1)
        {
            Assert.That(playground.Catalog.TryResolve(alias, out var definition), Is.True);
            var native = definition.Category == QusapLootCategory.Weapon ? UnityEngine.Object.FindAnyObjectByType<QusapWeaponMatchBootstrap>().CreateRaidLootWeapon(definition.WeaponDefinitionId) : null;
            var item = raid.Session.World.Create(definition, "isolated play provenance", native, quantity);
            Assert.That(p1.State.TryPickup(item.LootInstanceId), Is.EqualTo(QusapLootResult.Success)); return item;
        }
        [UnityTest] public IEnumerator ValidatedSceneUsesOneCatalogAndIsolatedProfileWithoutBuildOrRootMotionChanges()
        {
            Assert.That(playground.ValidationErrors, Is.Empty); Assert.That(raid.Definitions, Is.EqualTo(playground.Catalog.Definitions));
            Assert.That(repository.Storage.DirectoryPath, Is.EqualTo(directory)); Assert.That(repository.Storage.MainExists, Is.False);
            Assert.That(EditorBuildSettings.scenes.All(s => s.path != ScenePath), Is.True);
            Assert.That(raid.Participants.SelectMany(p => p.GetComponentsInChildren<Animator>(true)).All(a => !a.applyRootMotion), Is.True);
            Assert.That(playground.Catalog.Definitions.Single(d => d.WeaponDefinitionId == "qusap_sword_purple").DefinitionId, Is.EqualTo("raid_qusap_sword_purple")); yield return null;
        }
        [UnityTest] public IEnumerator RealWorldPickupsMergeMaterialStacksBeforeUsingSixSlots()
        {
            var shells = UnityEngine.Object.FindObjectsByType<QusapLootPickup>().Where(p => p.Definition.DefinitionId == "raid_fragment").OrderBy(p => p.Quantity).ToArray();
            var shellDefinition = shells[0].Definition;
            var body = p1.GetComponent<Rigidbody>(); body.position = new Vector3(-5.6f, 1.01f, 0); body.linearVelocity = Vector3.zero; Physics.SyncTransforms();
            foreach (var pickup in shells) { pickup.GetComponent<Collider>().enabled = true; yield return Tick(.15f); }
            var stacks = p1.ReadInventorySnapshot().Backpack.Where(i => i.HasValue).Select(i => i.Value).ToArray();
            Assert.That(stacks.Select(i => i.Quantity), Is.EqualTo(new[] { 10, 3 })); Assert.That(stacks.Select(i => i.LootInstanceId).Distinct().Count(), Is.EqualTo(2));
            Bag("raid_ancient_scale",3); Bag("raid_resonant_crystal",2); Bag("raid_minor_heal"); Bag("raid_rare_relic");
            Assert.That(p1.ReadInventorySnapshot().Backpack.Count(i => i.HasValue), Is.EqualTo(6));
            var excess = raid.Session.World.Create(shellDefinition, "different persistent origin"); Assert.That(p1.State.TryPickup(excess.LootInstanceId), Is.EqualTo(QusapLootResult.Full));
        }
        [UnityTest] public IEnumerator ExplicitConsumableHealing25And50CapsAtMaxAndConsumesOneUnit()
        {
            var minor = Bag("raid_minor_heal",2); var major = Bag("raid_major_heal");
            Assert.That(p1.TryConsume(minor.LootInstanceId), Is.EqualTo(QusapLootResult.NoEffect)); Assert.That(minor.Quantity, Is.EqualTo(2));
            Assert.That(p1.Receiver.TryReceiveEnvironmentDamage(80, playground), Is.True); float before = p1.Receiver.CurrentHealth;
            Assert.That(p1.TryConsume(minor.LootInstanceId), Is.EqualTo(QusapLootResult.Success)); Assert.That(p1.Receiver.CurrentHealth, Is.EqualTo(before+25)); Assert.That(minor.Quantity, Is.EqualTo(1));
            before = p1.Receiver.CurrentHealth; Assert.That(p1.TryConsume(major.LootInstanceId), Is.EqualTo(QusapLootResult.Success)); Assert.That(p1.Receiver.CurrentHealth, Is.EqualTo(before+50)); Assert.That(major.Quantity, Is.Zero);
            Assert.That(p1.TryConsume(minor.LootInstanceId), Is.EqualTo(QusapLootResult.Success)); Assert.That(p1.Receiver.CurrentHealth, Is.EqualTo(p1.Receiver.MaxHealth)); Assert.That(minor.Quantity, Is.Zero); yield return null;
        }
        [UnityTest] public IEnumerator WholePocketStackPreservesIdentityAndQuantityAndWeaponIsRejectedAndEquipsNatively()
        {
            var material = Bag("raid_ancient_scale",3); p1.TryMoveToSecurePocket(material.LootInstanceId); p1.TryMoveToBackpack(material.LootInstanceId);
            Assert.That(material.Quantity, Is.EqualTo(3)); Assert.That(p1.ReadInventorySnapshot().Backpack.First(i => i.HasValue).Value.LootInstanceId, Is.EqualTo(material.LootInstanceId));
            var relic = Bag("raid_rare_relic"); Assert.That(p1.TryMoveToSecurePocket(relic.LootInstanceId), Is.EqualTo(QusapLootResult.Success));
            var sword = Bag("raid_basic_sword"); Assert.That(sword.Quantity, Is.EqualTo(1)); Assert.That(relic.Quantity, Is.EqualTo(1));
            Assert.That(p1.TryMoveToSecurePocket(sword.LootInstanceId), Is.EqualTo(QusapLootResult.Ineligible));
            Assert.That(p1.TryStoreEquippedWeapon(), Is.EqualTo(QusapLootResult.Success)); Assert.That(p1.WeaponAdapter.TryEquipWeaponFromBackpack(sword.LootInstanceId), Is.EqualTo(QusapLootResult.Success));
            Assert.That(p1.WeaponAdapter.CurrentWeapon, Is.SameAs(sword.Weapon)); Assert.That(sword.Weapon.Definition.Id, Is.EqualTo("qusap_sword_blue")); Assert.That(sword.Location, Is.EqualTo(QusapLootLocation.Equipped)); yield return null;
        }
        [UnityTest] public IEnumerator RealExtractionPersistsStacksAndLogicalReloadResolvesCatalogWithoutWritingOrDuplicates()
        {
            var shell = Bag("raid_shell_fragment",7); var crystal = Bag("raid_resonant_crystal",2); var relic = Bag("raid_rare_relic"); p1.TryMoveToSecurePocket(relic.LootInstanceId);
            var sword = raid.Session.World.FindWeapon(p1.WeaponAdapter.CurrentWeapon);
            var expected = new[] { shell, crystal, relic, sword }.ToDictionary(i => i.LootInstanceId, i => i.Quantity);
            var zone = UnityEngine.Object.FindAnyObjectByType<QusapExtractionVolume>(); zone.Configure(.25f);
            var body = p1.GetComponent<Rigidbody>(); body.position = new Vector3(-2,1.01f,0); body.linearVelocity = Vector3.zero; Physics.SyncTransforms(); yield return Tick(.6f);
            Assert.That(p1.GetComponent<QusapRaidExtraction>().Status, Is.EqualTo(QusapExtractionStatus.Extracted)); Assert.That(repository.Revision, Is.EqualTo(1)); Assert.That(repository.Storage.Writes, Is.EqualTo(1));
            string original = File.ReadAllText(repository.Storage.MainPath); playground.ReloadEvidenceProfile(); var loaded = playground.Reloaded;
            var items = loaded.ReadStashSnapshot(loaded.ProfileId); Assert.That(items.Count, Is.EqualTo(4));
            foreach (var item in items) { Assert.That(item.Quantity, Is.EqualTo(expected[item.LootInstanceId])); var resolved = playground.Catalog.Definitions.Single(d => d.DefinitionId == item.DefinitionId); Assert.That(item.Rarity, Is.EqualTo(resolved.Rarity)); Assert.That(item.DisplayName, Is.EqualTo(resolved.DisplayName)); }
            Assert.That(items.Single(i => i.DefinitionId == crystal.DefinitionId).Rarity, Is.EqualTo(QusapLootRarity.Rare));
            Assert.That(items.Select(i => i.LootInstanceId).Distinct().Count(), Is.EqualTo(items.Count)); Assert.That(loaded.SchemaVersion, Is.EqualTo(1)); Assert.That(loaded.Storage.Writes, Is.Zero); Assert.That(File.ReadAllText(repository.Storage.MainPath), Is.EqualTo(original));
        }
        [UnityTest] public IEnumerator PhysicalDeathDropsWholeMaterialStackAndPersistsProtectedRelicOnlyOnce()
        {
            var cargo = Bag("raid_ancient_scale",4); var relic = Bag("raid_rare_relic"); p1.TryMoveToSecurePocket(relic.LootInstanceId);
            p1.Receiver.TryReceiveEnvironmentDamage(1000, playground); yield return Tick(.2f);
            Assert.That(cargo.Location, Is.EqualTo(QusapLootLocation.DeathContainer)); Assert.That(cargo.Quantity, Is.EqualTo(4));
            Assert.That(repository.ReadStashSnapshot(repository.ProfileId).Single().LootInstanceId, Is.EqualTo(relic.LootInstanceId)); Assert.That(repository.Storage.Writes, Is.EqualTo(1));
            p1.RetryDeathSettlement(); Assert.That(repository.Storage.Writes, Is.EqualTo(1));
        }
        [UnityTest] public IEnumerator SeededWorldGenerationHasStableDefinitionsQuantitiesAndFreshIds()
        {
            var a = playground.GenerateWorldLoot(4107); var b = playground.GenerateWorldLoot(4107); var c = playground.GenerateWorldLoot(4108);
            foreach (var pickup in a.Concat(b).Concat(c)) pickup.GetComponent<Collider>().enabled = false; yield return Tick(.15f);
            var first = a.Select(p => p.Definition.DefinitionId+"/"+p.Quantity).ToArray(); Assert.That(b.Select(p => p.Definition.DefinitionId+"/"+p.Quantity), Is.EqualTo(first)); Assert.That(c.Select(p => p.Definition.DefinitionId+"/"+p.Quantity), Is.Not.EqualTo(first));
            Assert.That(a.Concat(b).Concat(c).Select(p => p.InstanceId).Distinct().Count(), Is.EqualTo(24)); Assert.That(playground.SameSeedMatches && playground.DifferentSeedDiffers, Is.True);
        }
    }
}
#endif
