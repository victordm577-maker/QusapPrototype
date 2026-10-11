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
    public sealed class QusapRaidLoadoutDeploymentPlayModeTests
    {
        private const string ScenePath="Assets/_Qusap/Scenes/RaidLoadoutDeploymentPlayground.unity";
        private string directory;private InputTestFixture devices;private object runtime;
        private QusapRaidLoadoutPlayground scene;private QusapRaidInventory p1;private float scale,step;
        private void Clock(){runtime.GetType().GetProperty("currentTimeOffsetToRealtimeSinceStartup").SetValue(runtime,0d);runtime.GetType().GetProperty("currentTimeForFixedUpdate").SetValue(runtime,devices.currentTime);}
        private IEnumerator Tick(float seconds=.12f){float until=Time.time+seconds;while(Time.time<until){devices.currentTime+=Time.deltaTime;Clock();InputSystem.Update();yield return null;}}
        [UnitySetUp]public IEnumerator Setup()
        {
            scale=Time.timeScale;step=Time.fixedDeltaTime;directory=Path.Combine(Path.GetTempPath(),"QusapLoadoutPlayTests",Guid.NewGuid().ToString("N"));
            QusapLocalProfilePersistence.IsolatedStorageDirectory=directory;devices=new InputTestFixture();devices.Setup();InputSystem.AddDevice<Keyboard>();InputSystem.AddDevice<Gamepad>();
            runtime=typeof(InputTestFixture).GetProperty("runtime",BindingFlags.Instance|BindingFlags.NonPublic).GetValue(devices);runtime.GetType().GetProperty("advanceTimeEachDynamicUpdate").SetValue(runtime,0d);
            var load=EditorSceneManager.LoadSceneAsyncInPlayMode(ScenePath,new LoadSceneParameters(LoadSceneMode.Single));while(!load.isDone)yield return null;
            new GameObject("LoadoutTestClock").AddComponent<QusapVitalityTestClock>().Synchronize=Clock;yield return Tick(.3f);
            scene=UnityEngine.Object.FindAnyObjectByType<QusapRaidLoadoutPlayground>();p1=scene.Raid.Participants[0];
        }
        [UnityTearDown]public IEnumerator Cleanup()
        {
            foreach(var root in SceneManager.GetActiveScene().GetRootGameObjects()){root.SetActive(false);UnityEngine.Object.Destroy(root);}yield return null;yield return null;
            devices.TearDown();QusapLocalProfilePersistence.IsolatedStorageDirectory=null;Assert.That(Time.timeScale,Is.EqualTo(scale));Assert.That(Time.fixedDeltaTime,Is.EqualTo(step));
            string parent=Path.GetFullPath(Path.Combine(Path.GetTempPath(),"QusapLoadoutPlayTests"))+Path.DirectorySeparatorChar;
            if(!Path.GetFullPath(directory).StartsWith(parent,StringComparison.OrdinalIgnoreCase)||!Guid.TryParseExact(Path.GetFileName(directory),"N",out _))throw new InvalidOperationException("Unsafe cleanup");if(Directory.Exists(directory))Directory.Delete(directory,true);
        }
        private void Own(){scene.SelectOwned();Assert.That(scene.Prepare(),Is.True);Assert.That(scene.Commit(),Is.True,scene.Deployment.LastResult);Assert.That(scene.Run(),Is.True);}
        private void Place(QusapRaidInventory player){var body=player.GetComponent<Rigidbody>();body.position=new Vector3(-2,1.01f,0);body.linearVelocity=Vector3.zero;Physics.SyncTransforms();UnityEngine.Object.FindAnyObjectByType<QusapExtractionVolume>().Configure(.25f);}
        [UnityTest]public IEnumerator IsolatedSceneWaitsForDeploymentAndKeepsBuildCatalogAndRootMotion()
        {
            Assert.That(scene.Profile.Storage.DirectoryPath,Is.EqualTo(directory));Assert.That(scene.Profile.SchemaVersion,Is.EqualTo(2));Assert.That(scene.Profile.ReadStashSnapshot(scene.Profile.ProfileId).Count,Is.EqualTo(3));
            Assert.That(p1.State,Is.Null);Assert.That(p1.GetComponent<QusapWeaponEquipment>().HasWeapon,Is.False);Assert.That(scene.Catalog.Definitions.Count,Is.EqualTo(9));Assert.That(scene.Catalog.Aliases.Count,Is.EqualTo(5));
            Assert.That(EditorBuildSettings.scenes.All(s=>s.path!=ScenePath),Is.True);Assert.That(scene.Raid.Participants.SelectMany(p=>p.GetComponentsInChildren<Animator>(true)).All(a=>!a.applyRootMotion),Is.True);yield return null;
        }
        [UnityTest]public IEnumerator OwnSwordUsesSameNativePresentationAndPhysicalExtractionReturnsPartialStack()
        {
            string sword=scene.WeaponSelection,heal=scene.ConsumableSelection.Single().InstanceId;string bytes=File.ReadAllText(scene.Profile.Storage.MainPath);long rev=scene.Profile.Revision;
            Assert.That(scene.Prepare(),Is.True);Assert.That(File.ReadAllText(scene.Profile.Storage.MainPath),Is.EqualTo(bytes));Assert.That(scene.Commit(),Is.True);Assert.That(scene.Run(),Is.True);
            Assert.That(p1.ReadInventorySnapshot().EquippedWeapon.Value.LootInstanceId,Is.EqualTo(sword));Assert.That(p1.GetComponent<QusapEquippedWeaponPresenter>().DisplayedWeapon,Is.SameAs(p1.WeaponAdapter.CurrentWeapon));
            Assert.That(p1.Receiver.TryReceiveEnvironmentDamage(25,scene),Is.True);Assert.That(p1.TryConsume(heal),Is.EqualTo(QusapLootResult.Success));Assert.That(scene.Deployment.Session.World.Get(heal).Quantity,Is.EqualTo(1));
            Place(p1);yield return Tick(.6f);Assert.That(p1.GetComponent<QusapRaidExtraction>().Status,Is.EqualTo(QusapExtractionStatus.Extracted));
            Assert.That(scene.Profile.Revision,Is.EqualTo(rev+3));Assert.That(scene.Profile.ReadActiveDeploymentSnapshot(),Is.Null);Assert.That(scene.Profile.ReadStashSnapshot(scene.Profile.ProfileId).Single(i=>i.LootInstanceId==heal).Quantity,Is.EqualTo(1));
            Assert.That(scene.Profile.ReadStashSnapshot(scene.Profile.ProfileId).Single(i=>i.LootInstanceId==sword).DefinitionId,Is.EqualTo("raid_qusap_sword_purple"));Assert.That(scene.Deployment.Status,Is.EqualTo(QusapDeploymentStatus.Resolved));
        }
        [UnityTest]public IEnumerator RealEliminationContainerAndRivalEquipKeepSameLogicalSword()
        {
            Own();var sword=scene.Deployment.Session.World.FindWeapon(p1.WeaponAdapter.CurrentWeapon);string heal=scene.ConsumableSelection.Single().InstanceId;
            Assert.That(p1.Receiver.TryReceiveEnvironmentDamage(1000,scene),Is.True);yield return Tick(.2f);var p2=scene.Raid.Participants[1];string container=scene.Deployment.Session.Containers.Single().ContainerId;
            Assert.That(sword.Location,Is.EqualTo(QusapLootLocation.DeathContainer));Assert.That(p2.WeaponAdapter.TryEquipWeaponFromContainer(container,sword.LootInstanceId),Is.EqualTo(QusapLootResult.Success));
            Assert.That(p2.WeaponAdapter.CurrentWeapon,Is.SameAs(sword.Weapon));Assert.That(p2.State.TryTakeFromDeathContainer(container,heal),Is.EqualTo(QusapLootResult.Success));
            Place(p2);yield return Tick(.6f);Assert.That(scene.Profile.ReadStashSnapshot(p2.ProfileId).Select(i=>i.LootInstanceId),Is.EquivalentTo(new[]{sword.LootInstanceId,heal}));Assert.That(scene.Profile.ReadActiveDeploymentSnapshot(),Is.Null);
        }
        [UnityTest]public IEnumerator NativeLoanerPresentationAndPhysicalExtractionNeverWriteItToStash()
        {
            scene.SelectLoaner();long rev=scene.Profile.Revision;int writes=scene.Profile.Storage.Writes;var before=scene.Profile.ReadStashSnapshot(scene.Profile.ProfileId).Select(i=>i.LootInstanceId).ToArray();
            Assert.That(scene.Prepare(),Is.True);Assert.That(scene.Commit(),Is.True);Assert.That(scene.Run(),Is.True);var loaner=scene.Deployment.Session.World.FindWeapon(p1.WeaponAdapter.CurrentWeapon);
            Assert.That(loaner.RaidLoaner,Is.True);Assert.That(loaner.Weapon.Definition.Id,Is.EqualTo("qusap_sword_blue"));Assert.That(p1.GetComponent<QusapEquippedWeaponPresenter>().DisplayedWeapon,Is.SameAs(loaner.Weapon));
            p1.GetComponent<QusapInputReader>().EnqueueCombatCommand(QusapCombatCommand.WeaponLight,UnityEngine.InputSystem.LowLevel.InputState.currentTime);yield return Tick(.1f);
            Assert.That(p1.GetComponent<QusapCombatController>().IsAttacking,Is.True);
            Place(p1);yield return Tick(.6f);Assert.That(loaner.Weapon.IsRetired,Is.True);Assert.That(scene.Deployment.LoanersDestroyed,Is.EqualTo(1));Assert.That(scene.Profile.Revision,Is.EqualTo(rev));Assert.That(scene.Profile.Storage.Writes,Is.EqualTo(writes));Assert.That(scene.Profile.ReadStashSnapshot(scene.Profile.ProfileId).Select(i=>i.LootInstanceId),Is.EquivalentTo(before));
        }
        [UnityTest]public IEnumerator LoanerNativeDisarmViewIsRemovedAtSessionFinish()
        {
            scene.SelectLoaner();scene.Prepare();scene.Commit();scene.Run();var native=UnityEngine.Object.FindAnyObjectByType<QusapWeaponMatchBootstrap>();var sword=p1.WeaponAdapter.CurrentWeapon;
            Assert.That(p1.GetComponent<QusapWeaponEquipment>().TryDisarm(scene.Raid.Participants[1].GetComponent<QusapWeaponEquipment>().OwnerEntityId,out _,out _),Is.EqualTo(QusapWeaponOperationResult.Success));yield return Tick(.1f);
            Assert.That(native.DroppedWeapons.Any(v=>ReferenceEquals(v.Weapon,sword)),Is.True);scene.Deployment.Session.FinishSession();yield return Tick(.1f);
            Assert.That(sword.IsRetired,Is.True);Assert.That(native.DroppedWeapons.Any(v=>ReferenceEquals(v.Weapon,sword)),Is.False);
        }
        [UnityTest]public IEnumerator PreparedCancellationUsesOneRevisionAndNoDuplicateOwners()
        {
            string sword=scene.WeaponSelection;scene.Prepare();scene.Commit();long rev=scene.Profile.Revision;Assert.That(scene.Deployment.CancelDeployment(),Is.True);Assert.That(scene.Deployment.CancelDeployment(),Is.True);
            Assert.That(scene.Profile.Revision,Is.EqualTo(rev+1));Assert.That(scene.Profile.ReadStashSnapshot(scene.Profile.ProfileId).Count(i=>i.LootInstanceId==sword),Is.EqualTo(1));Assert.That(p1.WeaponAdapter.CurrentWeapon,Is.Null);Assert.That(scene.Profile.ReadActiveDeploymentSnapshot(),Is.Null);yield return null;
        }
    }
}
#endif
