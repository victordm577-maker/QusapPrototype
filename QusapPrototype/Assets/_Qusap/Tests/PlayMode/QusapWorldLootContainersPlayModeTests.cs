#if UNITY_EDITOR
using System;
using System.Collections;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

namespace Qusap.Tests
{
    public sealed class QusapWorldLootContainersPlayModeTests
    {
        private const string ScenePath = "Assets/_Qusap/Scenes/RaidWorldLootPlayground.unity";
        private string directory;
        private InputTestFixture devices;
        private object runtime;
        private QusapRaidWorldLootPlayground scene;
        private QusapRaidInventory player;
        private float scale, fixedStep;
        private void Clock() { runtime.GetType().GetProperty("currentTimeOffsetToRealtimeSinceStartup").SetValue(runtime, 0d); runtime.GetType().GetProperty("currentTimeForFixedUpdate").SetValue(runtime, devices.currentTime); }
        private IEnumerator Tick(float duration = .12f) { float end = Time.time + duration; while (Time.time < end) { devices.currentTime += Time.deltaTime; Clock(); InputSystem.Update(); yield return null; } }
        [UnitySetUp] public IEnumerator Setup()
        {
            directory = Path.Combine(Path.GetTempPath(), "QusapWorldLootPlayTests", Guid.NewGuid().ToString("N"));
            QusapLocalProfilePersistence.IsolatedStorageDirectory = directory; scale = Time.timeScale; fixedStep = Time.fixedDeltaTime;
            devices = new InputTestFixture(); devices.Setup(); InputSystem.AddDevice<Keyboard>(); InputSystem.AddDevice<Gamepad>();
            runtime = typeof(InputTestFixture).GetProperty("runtime", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(devices); runtime.GetType().GetProperty("advanceTimeEachDynamicUpdate").SetValue(runtime, 0d);
            var load = EditorSceneManager.LoadSceneAsyncInPlayMode(ScenePath, new LoadSceneParameters(LoadSceneMode.Single)); while (!load.isDone) yield return null;
            new GameObject("WorldLootTestClock").AddComponent<QusapVitalityTestClock>().Synchronize = Clock; yield return Tick(.3f);
            scene = UnityEngine.Object.FindAnyObjectByType<QusapRaidWorldLootPlayground>(); player = scene.LocalPlayer;
        }
        [UnityTearDown] public IEnumerator Cleanup()
        {
            foreach (var root in SceneManager.GetActiveScene().GetRootGameObjects()) { root.SetActive(false); UnityEngine.Object.Destroy(root); } yield return null; yield return null;
            devices.TearDown(); QusapLocalProfilePersistence.IsolatedStorageDirectory = null;
            Assert.That(Time.timeScale, Is.EqualTo(scale)); Assert.That(Time.fixedDeltaTime, Is.EqualTo(fixedStep));
            string parent = Path.GetFullPath(Path.Combine(Path.GetTempPath(), "QusapWorldLootPlayTests")) + Path.DirectorySeparatorChar;
            if (!Path.GetFullPath(directory).StartsWith(parent, StringComparison.OrdinalIgnoreCase) || !Guid.TryParseExact(Path.GetFileName(directory), "N", out _)) throw new InvalidOperationException("Unsafe isolated cleanup");
            if (Directory.Exists(directory)) Directory.Delete(directory, true);
        }
        private void Place(Vector3 position) { var body = player.GetComponent<Rigidbody>(); body.position = position; body.linearVelocity = Vector3.zero; Physics.SyncTransforms(); }
        private void At(int cache) { scene.Select(cache); Place(scene.Containers[cache].transform.position + Vector3.up * .51f); }
        private void SeedFor(Func<QusapLootRoll[][], bool> condition)
        {
            for (ulong seed = 0; seed < 10000; seed++)
            {
                var rolls = scene.Containers.Select(v => v.Table.Resolve(scene.Loadout.Catalog, scene.Authority.RaidId, v.SpawnPointId, seed)).ToArray();
                if (condition(rolls)) { Assert.That(scene.ConfigureSeedBeforeOpen(seed), Is.True); return; }
            }
            Assert.Fail("No deterministic diagnostic seed found within bounded search");
        }
        [UnityTest] public IEnumerator IsolatedSceneKeepsApprovedDeploymentCatalogPocketCapacityBuildAndGuidReferences()
        {
            Assert.That(scene.Authority, Is.Not.Null); Assert.That(scene.Loadout.Deployment.Status, Is.EqualTo(QusapDeploymentStatus.Running));
            Assert.That(scene.Loadout.Profile.Storage.DirectoryPath, Is.EqualTo(directory)); Assert.That(player.ReadInventorySnapshot().Backpack.Count, Is.EqualTo(6)); Assert.That(player.ReadInventorySnapshot().SecurePocket, Is.Null);
            Assert.That(scene.Containers.Count, Is.EqualTo(3)); Assert.That(scene.Containers.Select(v => v.SpawnPointId).Distinct().Count(), Is.EqualTo(3));
            Assert.That(scene.Containers.All(v => v.Snapshot.State == QusapWorldLootState.Unopened), Is.True); Assert.That(scene.Loadout.Catalog.Definitions.Count, Is.EqualTo(9)); Assert.That(scene.Loadout.Catalog.Aliases.Count, Is.EqualTo(5));
            Assert.That(EditorBuildSettings.scenes.All(s => s.path != ScenePath), Is.True); Assert.That(UnityEngine.Object.FindObjectsByType<Animator>().All(a => !a.applyRootMotion), Is.True);
            Assert.That(UnityEngine.Object.FindAnyObjectByType<QusapRaidWorldLootHud>().Canvas, Is.Not.Null);
            foreach (var root in SceneManager.GetActiveScene().GetRootGameObjects()) foreach (var t in root.GetComponentsInChildren<Transform>(true)) Assert.That(GameObjectUtility.GetMonoBehavioursWithMissingScriptCount(t.gameObject), Is.Zero);
            foreach (System.Text.RegularExpressions.Match match in System.Text.RegularExpressions.Regex.Matches(File.ReadAllText(ScenePath), "guid: ([a-f0-9]{32})")) if (!match.Groups[1].Value.StartsWith("0000000000000000")) Assert.That(AssetDatabase.GUIDToAssetPath(match.Groups[1].Value), Is.Not.Empty);
            yield return null;
        }
        [UnityTest] public IEnumerator CommonCacheSupportsPartialIdentityThenStackingAndRepeatedOpenWithoutWrites()
        {
            SeedFor(r => r[0].Any(i => i.Definition.Category == QusapLootCategory.Material && i.Quantity >= 2)); At(0); Assert.That(scene.OpenSelected().Confirmed, Is.True);
            scene.SelectedItem = scene.ReadSelected().Items.ToList().FindIndex(i => i.Category == QusapLootCategory.Material && i.Quantity >= 2);
            var request = scene.CreateTakeRequest(1); var first = scene.Submit(request); var target = scene.Loadout.Deployment.Session.World.Get(first.DestinationInstanceId);
            Assert.That(first.Confirmed, Is.True); Assert.That(first.DestinationInstanceId, Is.EqualTo(request.InstanceId + "/part/1"));
            Assert.That(scene.TakeSelected(1).Confirmed, Is.True); Assert.That(target.Quantity, Is.EqualTo(2)); Assert.That(player.ReadInventorySnapshot().Backpack.Count(i => i.HasValue), Is.EqualTo(1));
            var before = scene.ReadSelected(); Assert.That(scene.OpenSelected().Confirmed, Is.True); Assert.That(scene.ReadSelected().Version, Is.EqualTo(before.Version)); Assert.That(scene.ReadSelected().GenerationCount, Is.EqualTo(1)); Assert.That(player.ReadInventorySnapshot().SecurePocket, Is.Null);
            Assert.That(scene.Authority.DuplicateInstances, Is.Zero); yield return null;
        }
        [UnityTest] public IEnumerator FullBackpackRetainsOriginAndCancelledRequestDoesNotChangeEitherOwner()
        {
            At(0); scene.OpenSelected(); var definition = scene.Loadout.Catalog.Definitions.Single(d => d.DefinitionId == "raid_major_heal");
            while (player.ReadInventorySnapshot().Backpack.Any(i => !i.HasValue)) { var fixture = scene.Loadout.Deployment.Session.World.Create(definition, "isolated capacity fixture"); Assert.That(player.State.TryPickup(fixture.LootInstanceId), Is.EqualTo(QusapLootResult.Success)); }
            var before = scene.ReadSelected(); var request = scene.CreateTakeRequest(); Assert.That(scene.Submit(request, new CancellationToken(true)).Result, Is.EqualTo(QusapWorldLootResult.Cancelled));
            Assert.That(scene.Submit(request).Result, Is.EqualTo(QusapWorldLootResult.Full)); Assert.That(scene.ReadSelected().Version, Is.EqualTo(before.Version)); Assert.That(scene.ReadSelected().Items.Select(i => i.LootInstanceId), Is.EqualTo(before.Items.Select(i => i.LootInstanceId)));
            Assert.That(player.ReadInventorySnapshot().Backpack.Count(i => i.HasValue), Is.EqualTo(6)); yield return null;
        }
        [UnityTest] public IEnumerator MedicalCacheUsesExistingHealthConsumptionAndNeverAutomaticallyFillsPocket()
        {
            SeedFor(r => r[1].Any(i => i.Definition.Category == QusapLootCategory.Consumable)); At(1); scene.OpenSelected(); scene.SelectedItem = scene.ReadSelected().Items.ToList().FindIndex(i => i.Category == QusapLootCategory.Consumable);
            var taken = scene.TakeSelected(1); Assert.That(taken.Confirmed, Is.True); Assert.That(player.ReadInventorySnapshot().SecurePocket, Is.Null);
            scene.DiagnosticDamage(); float before = player.Receiver.CurrentHealth; Assert.That(scene.Consume(), Is.EqualTo(QusapLootResult.Success)); Assert.That(player.Receiver.CurrentHealth, Is.GreaterThan(before)); Assert.That(scene.Loadout.Deployment.Session.World.Get(taken.DestinationInstanceId).Quantity, Is.Zero);
            yield return null;
        }
        [UnityTest] public IEnumerator HighValueManualPocketAndPhysicalExtractionPersistTheExactCanonicalInstanceOnly()
        {
            SeedFor(r => r[2].Any(i => i.Definition.CanEnterSecurePocket)); At(2); long revision = scene.Loadout.Profile.Revision; int writes = scene.Loadout.Profile.Storage.Writes;
            string disk = File.ReadAllText(scene.Loadout.Profile.Storage.MainPath); scene.OpenSelected(); scene.SelectedItem = scene.ReadSelected().Items.ToList().FindIndex(i => i.Category != QusapLootCategory.Weapon); var item = scene.ReadSelected().Items[scene.SelectedItem];
            var taken = scene.TakeSelected(); Assert.That(taken.Confirmed, Is.True); Assert.That(taken.DestinationInstanceId, Is.EqualTo(item.LootInstanceId)); Assert.That(player.ReadInventorySnapshot().SecurePocket, Is.Null);
            Assert.That(File.ReadAllText(scene.Loadout.Profile.Storage.MainPath), Is.EqualTo(disk)); Assert.That(scene.Loadout.Profile.Revision, Is.EqualTo(revision)); Assert.That(scene.Loadout.Profile.Storage.Writes, Is.EqualTo(writes));
            Assert.That(scene.MoveLastToPocket(), Is.EqualTo(QusapLootResult.Success)); Assert.That(player.ReadInventorySnapshot().SecurePocket.Value.LootInstanceId, Is.EqualTo(item.LootInstanceId));
            var extraction = UnityEngine.Object.FindAnyObjectByType<QusapExtractionVolume>();
            Place(new Vector3(extraction.transform.position.x, 1.01f, extraction.transform.position.z)); extraction.Configure(.25f); yield return Tick(.6f);
            Assert.That(player.GetComponent<QusapRaidExtraction>().Status, Is.EqualTo(QusapExtractionStatus.Extracted)); Assert.That(scene.Loadout.Profile.Revision, Is.EqualTo(revision + 1));
            var saved = scene.Loadout.Profile.ReadStashSnapshot(scene.Loadout.Profile.ProfileId).Single(i => i.LootInstanceId == item.LootInstanceId); Assert.That(saved.DefinitionId, Is.EqualTo(item.DefinitionId)); Assert.That(saved.Quantity, Is.EqualTo(item.Quantity)); Assert.That(saved.RaidLoaner, Is.False);
            Assert.That(scene.Loadout.Profile.ReadActiveDeploymentSnapshot(), Is.Null); Assert.That(scene.Loadout.Deployment.LoanersDestroyed, Is.EqualTo(1));
            var reload = new QusapPersistentStashRepository(directory, scene.Loadout.Catalog.Definitions); Assert.That(reload.ReadStashSnapshot(reload.ProfileId).Any(i => i.LootInstanceId == item.LootInstanceId), Is.True); Assert.That(reload.Storage.Writes, Is.Zero);
        }
        [UnityTest] public IEnumerator RealEliminationProtectsManualPocketAndDoesNotPersistUntakenContainerLoot()
        {
            SeedFor(r => r[2].Any(i => i.Definition.CanEnterSecurePocket)); At(2); scene.OpenSelected(); scene.SelectedItem = scene.ReadSelected().Items.ToList().FindIndex(i => i.Category != QusapLootCategory.Weapon);
            var item = scene.TakeSelected(); scene.MoveLastToPocket(); var untaken = scene.ReadSelected().Items.Select(i => i.LootInstanceId).ToArray();
            Assert.That(player.Receiver.TryReceiveEnvironmentDamage(1000, scene), Is.True); yield return Tick(.2f);
            Assert.That(player.Receiver.IsEliminated, Is.True); Assert.That(scene.Loadout.Profile.ReadStashSnapshot(scene.Loadout.Profile.ProfileId).Any(i => i.LootInstanceId == item.DestinationInstanceId), Is.True);
            Assert.That(untaken.Any(id => scene.Loadout.Profile.ReadStashSnapshot(scene.Loadout.Profile.ProfileId).Any(i => i.LootInstanceId == id)), Is.False); Assert.That(scene.TakeSelected().Result, Is.EqualTo(QusapWorldLootResult.Blocked));
        }
        [UnityTest] public IEnumerator DoubleRequestHasOneConfirmationOneRejectionAndAllThreeCachesKeepUniqueIds()
        {
            At(0); scene.OpenSelected(); var request = scene.CreateTakeRequest(); var first = scene.Submit(request); var second = scene.Submit(request);
            Assert.That(first.Confirmed, Is.True); Assert.That(second.Result, Is.EqualTo(QusapWorldLootResult.StaleRequest)); Assert.That(scene.Authority.ConfirmedTransfers, Is.EqualTo(1)); Assert.That(scene.Authority.RejectedRequests, Is.EqualTo(1));
            At(1); scene.OpenSelected(); At(2); scene.OpenSelected(); var world = scene.Loadout.Deployment.Session.World;
            Assert.That(world.Instances.Select(i => i.LootInstanceId).Distinct().Count(), Is.EqualTo(world.Instances.Count)); Assert.That(world.Instances.All(i => i.Quantity >= 0), Is.True);
            Assert.That(world.Instances.Where(i => i.Location == QusapLootLocation.WorldContainer).All(i => !i.RaidLoaner), Is.True); Assert.That(scene.Authority.DuplicateInstances, Is.Zero); yield return null;
        }
        [UnityTest] public IEnumerator UnopenedOutOfReachAndWeaponPocketRequestsAreExplicitlyRejected()
        {
            SeedFor(r => r[0].Any(i => i.Definition.Category == QusapLootCategory.Weapon)); scene.Select(0);
            Assert.That(scene.TakeSelected().Result, Is.EqualTo(QusapWorldLootResult.NotOpened)); At(0); scene.OpenSelected(); scene.SelectedItem = scene.ReadSelected().Items.ToList().FindIndex(i => i.Category == QusapLootCategory.Weapon);
            var weapon = scene.TakeSelected(); Assert.That(weapon.Confirmed, Is.True); Assert.That(scene.MoveLastToPocket(), Is.EqualTo(QusapLootResult.Ineligible));
            Assert.That(player.TryStoreEquippedWeapon(), Is.EqualTo(QusapLootResult.Success)); var loaner = player.ReadInventorySnapshot().Backpack.First(i => i.HasValue && i.Value.RaidLoaner).Value;
            Assert.That(player.TryMoveToSecurePocket(loaner.LootInstanceId), Is.EqualTo(QusapLootResult.Ineligible));
            Place(new Vector3(50, 1.01f, 0)); Assert.That(scene.OpenSelected().Result, Is.EqualTo(QusapWorldLootResult.OutOfReach)); yield return null;
        }
    }
}
#endif
