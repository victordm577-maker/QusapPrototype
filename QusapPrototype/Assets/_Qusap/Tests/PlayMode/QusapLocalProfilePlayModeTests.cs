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
    public sealed class QusapLocalProfilePlayModeTests
    {
        private const string ScenePath = "Assets/_Qusap/Scenes/LocalProfilePersistencePlayground.unity";
        private string directory;
        private QusapRaidBootstrap raid;
        private QusapRaidInventory p1;
        private QusapPersistentStashRepository repository;
        private QusapExtractionVolume zone;
        private InputTestFixture devices;
        private object runtime;
        private float scale, step;
        private void Clock()
        {
            runtime.GetType().GetProperty("currentTimeOffsetToRealtimeSinceStartup").SetValue(runtime, 0d);
            runtime.GetType().GetProperty("currentTimeForFixedUpdate").SetValue(runtime, devices.currentTime);
        }
        private IEnumerator Tick(float seconds = 0.12f)
        { float until = Time.time + seconds; while (Time.time < until) { devices.currentTime += Time.deltaTime; Clock(); InputSystem.Update(); yield return null; } }
        [UnitySetUp] public IEnumerator Setup()
        {
            scale = Time.timeScale; step = Time.fixedDeltaTime;
            directory = Path.Combine(Path.GetTempPath(), "QusapProfilePlayTests", Guid.NewGuid().ToString("N")); Directory.CreateDirectory(directory);
            QusapLocalProfilePersistence.IsolatedStorageDirectory = directory;
            devices = new InputTestFixture(); devices.Setup(); InputSystem.AddDevice<Keyboard>(); InputSystem.AddDevice<Gamepad>();
            runtime = typeof(InputTestFixture).GetProperty("runtime", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(devices);
            runtime.GetType().GetProperty("advanceTimeEachDynamicUpdate").SetValue(runtime, 0d);
            var load = EditorSceneManager.LoadSceneAsyncInPlayMode(ScenePath, new LoadSceneParameters(LoadSceneMode.Single));
            while (!load.isDone) yield return null;
            new GameObject("LocalProfile_TestClock").AddComponent<QusapVitalityTestClock>().Synchronize = Clock;
            foreach (var pickup in UnityEngine.Object.FindObjectsByType<QusapLootPickup>()) pickup.GetComponent<Collider>().enabled = false;
            yield return Tick(0.3f);
            raid = UnityEngine.Object.FindAnyObjectByType<QusapRaidBootstrap>(); p1 = raid.Participants[0];
            repository = raid.GetComponent<QusapLocalProfilePersistence>().Repository;
            zone = UnityEngine.Object.FindAnyObjectByType<QusapExtractionVolume>();
            Assert.That(UnityEngine.Object.FindAnyObjectByType<QusapRaidSessionObserver>().StartSession(), Is.True);
        }
        [UnityTearDown] public IEnumerator Cleanup()
        {
            foreach (var root in SceneManager.GetActiveScene().GetRootGameObjects()) { root.SetActive(false); UnityEngine.Object.Destroy(root); }
            yield return null; yield return null; devices.TearDown(); QusapLocalProfilePersistence.IsolatedStorageDirectory = null;
            Assert.That(Time.timeScale, Is.EqualTo(scale)); Assert.That(Time.fixedDeltaTime, Is.EqualTo(step));
            string parent = Path.GetFullPath(Path.Combine(Path.GetTempPath(), "QusapProfilePlayTests")) + Path.DirectorySeparatorChar;
            string exact = Path.GetFullPath(directory);
            if (!exact.StartsWith(parent, StringComparison.OrdinalIgnoreCase) || !Guid.TryParseExact(Path.GetFileName(exact), "N", out _)) throw new InvalidOperationException("Unsafe cleanup target.");
            if (Directory.Exists(exact)) Directory.Delete(exact, true);
        }
        private QusapLootInstance Bag(QusapLootCategory category = QusapLootCategory.Fragment)
        {
            var item = raid.Session.World.Create(raid.Definitions.First(d => d.Category == category), "play test provenance");
            Assert.That(p1.State.TryPickup(item.LootInstanceId), Is.EqualTo(QusapLootResult.Success)); return item;
        }
        private void PlaceInZone()
        { var body = p1.GetComponent<Rigidbody>(); body.position = new Vector3(-2, 1.01f, 0); body.linearVelocity = Vector3.zero; Physics.SyncTransforms(); }
        [UnityTest] public IEnumerator NewProfileIsIsolatedEmptyAndPlaygroundIsOutsideBuildSettings()
        {
            Assert.That(repository.Revision, Is.Zero); Assert.That(repository.Storage.MainExists, Is.False);
            Assert.That(repository.Storage.DirectoryPath, Is.EqualTo(directory)); Assert.That(EditorBuildSettings.scenes.All(s => s.path != ScenePath), Is.True);
            Assert.That(raid.Participants.SelectMany(p => p.GetComponentsInChildren<Animator>(true)).All(a => !a.applyRootMotion), Is.True); yield return null;
        }
        [UnityTest] public IEnumerator RealExtractionPersistsCompleteCargoAndNativeWeaponExactlyOnce()
        {
            var fragment = Bag(); var relic = Bag(QusapLootCategory.Relic); p1.TryMoveToSecurePocket(relic.LootInstanceId);
            var native = p1.WeaponAdapter.CurrentWeapon; var weaponId = raid.Session.World.FindWeapon(native).LootInstanceId;
            zone.Configure(0.25f); PlaceInZone(); yield return Tick(0.5f);
            Assert.That(p1.GetComponent<QusapRaidExtraction>().Status, Is.EqualTo(QusapExtractionStatus.Extracted));
            Assert.That(repository.Revision, Is.EqualTo(1)); Assert.That(repository.Storage.Writes, Is.EqualTo(1));
            CollectionAssert.AreEquivalent(new[] { fragment.LootInstanceId, relic.LootInstanceId, weaponId }, repository.ReadStashSnapshot(repository.ProfileId).Select(i => i.LootInstanceId));
            Assert.That(raid.Session.SettleExtraction(p1.State, p1.WeaponAdapter), Is.EqualTo(QusapLootResult.Blocked));
            repository.ConfirmUnchanged(); Assert.That(repository.Storage.Writes, Is.EqualTo(1));
            var loaded = new QusapPersistentStashRepository(directory, raid.Definitions);
            Assert.That(loaded.ReadStashSnapshot(repository.ProfileId).Single(i => i.Weapon != null).Weapon.InstanceId, Is.EqualTo(native.InstanceId));
        }
        [UnityTest] public IEnumerator PhysicalEliminationPersistsOnlySecurePocketAndNoDuplicateDeathWrites()
        {
            var cargo = Bag(); var relic = Bag(QusapLootCategory.Relic); p1.TryMoveToSecurePocket(relic.LootInstanceId);
            Assert.That(p1.Receiver.TryReceiveEnvironmentDamage(1000, zone), Is.True); yield return Tick(0.2f);
            Assert.That(p1.Receiver.IsEliminated, Is.True); Assert.That(repository.Revision, Is.EqualTo(1));
            Assert.That(repository.ReadStashSnapshot(repository.ProfileId).Single().LootInstanceId, Is.EqualTo(relic.LootInstanceId));
            Assert.That(cargo.Location, Is.EqualTo(QusapLootLocation.DeathContainer)); Assert.That(raid.Session.Containers.Count, Is.EqualTo(1));
            Assert.That(p1.RetryDeathSettlement(), Is.EqualTo(QusapLootResult.Success)); Assert.That(repository.Storage.Writes, Is.EqualTo(1));
        }
        [UnityTest] public IEnumerator OrphanTemporaryRejectsExtractionWithoutLosingCargoOrRetiringPlayer()
        {
            var cargo = Bag(); File.WriteAllText(repository.Storage.TemporaryPath, "incomplete");
            zone.Configure(0.1f); PlaceInZone(); yield return Tick(0.4f);
            Assert.That(repository.Revision, Is.Zero); Assert.That(repository.Storage.Writes, Is.Zero);
            Assert.That(cargo.Location, Is.EqualTo(QusapLootLocation.Backpack)); Assert.That(p1.Receiver.IsGameplayRetired, Is.False);
            Assert.That(File.ReadAllText(repository.Storage.TemporaryPath), Is.EqualTo("incomplete"));
        }
        [UnityTest] public IEnumerator LoadedRepositoryCanContinueRevisionAndNeverReusesLootOrNativeIds()
        {
            Bag(); raid.Session.SettleExtraction(p1.State, p1.WeaponAdapter); var old = repository.ReadStashSnapshot(repository.ProfileId);
            var loaded = new QusapPersistentStashRepository(directory, raid.Definitions);
            using var model = new QusapRaidSession(raid.Session.World.RaidId, loaded); var p = model.Register("next-P1", loaded.ProfileId);
            var item = model.World.Create(raid.Definitions.First(d => d.Category == QusapLootCategory.Fragment), "new execution");
            Assert.That(old.All(i => i.LootInstanceId != item.LootInstanceId), Is.True); p.TryPickup(item.LootInstanceId);
            Assert.That(model.SettleExtraction(p), Is.EqualTo(QusapLootResult.Success)); Assert.That(loaded.Revision, Is.EqualTo(2));
            var again = new QusapPersistentStashRepository(directory, raid.Definitions); Assert.That(again.Revision, Is.EqualTo(2));
            var stash = again.ReadStashSnapshot(again.ProfileId); Assert.That(stash.Count, Is.EqualTo(stash.Select(i => i.LootInstanceId).Distinct().Count())); yield return null;
        }
    }
}
#endif
