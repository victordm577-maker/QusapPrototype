#if UNITY_EDITOR
using System.Collections;
using System.Linq;
using System.Reflection;
using NUnit.Framework;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

namespace Qusap.Tests
{
    public sealed class QusapRaidHazardPlayModeTests
    {
        private InputTestFixture devices;
        private object runtime;
        private QusapRaidHazardPlayground scene;
        private float scale,step;
        private QusapRaidInventory P1=>scene.Raid.Participants[0];
        private QusapRaidInventory P2=>scene.Raid.Participants[1];
        private QusapRaidTiming ClockState=>scene.Context.Session.Timing;
        private void InputClock()
        {
            runtime.GetType().GetProperty("currentTimeOffsetToRealtimeSinceStartup").SetValue(runtime,0d);
            runtime.GetType().GetProperty("currentTimeForFixedUpdate").SetValue(runtime,devices.currentTime);
        }
        private IEnumerator Tick(float seconds=.1f)
        {float end=Time.time+seconds;while(Time.time<end){devices.currentTime+=Time.deltaTime;InputClock();InputSystem.Update();yield return null;}}
        [UnitySetUp] public IEnumerator Setup()
        {
            scale=Time.timeScale;step=Time.fixedDeltaTime;devices=new InputTestFixture();devices.Setup();
            InputSystem.AddDevice<Keyboard>();InputSystem.AddDevice<Gamepad>();
            runtime=typeof(InputTestFixture).GetProperty("runtime",BindingFlags.Instance|BindingFlags.NonPublic).GetValue(devices);
            runtime.GetType().GetProperty("advanceTimeEachDynamicUpdate").SetValue(runtime,0d);
            var load=EditorSceneManager.LoadSceneAsyncInPlayMode("Assets/_Qusap/Scenes/RaidHazardPlayground.unity",new LoadSceneParameters(LoadSceneMode.Single));
            while(!load.isDone) yield return null;
            new GameObject("TimingInputTestClock").AddComponent<QusapVitalityTestClock>().Synchronize=InputClock;yield return Tick(.2f);
            scene=Object.FindAnyObjectByType<QusapRaidHazardPlayground>();Assert.That(scene,Is.Not.Null);
        }
        private IEnumerator Start(double duration=36,double opening=2,bool manual=true,QusapRaidHazardSettings settings=null)
        {
            scene.ConfigureTimes(new QusapRaidTimingSettings(duration,opening),settings);
            Assert.That(scene.BeginRaid(),Is.True);yield return Tick(.15f);
            if(manual) { scene.Raid.GetComponent<QusapRaidTimingDriver>().enabled=false;scene.Raid.GetComponent<QusapRaidHazardDriver>().enabled=false; }
        }
        [UnityTearDown] public IEnumerator Cleanup()
        {
            foreach(var root in SceneManager.GetActiveScene().GetRootGameObjects()){root.SetActive(false);Object.Destroy(root);}
            yield return null;yield return null;devices.TearDown();
            Assert.That(Time.timeScale,Is.EqualTo(scale));Assert.That(Time.fixedDeltaTime,Is.EqualTo(step));
        }
        private static void Place(QusapRaidInventory player,float x)
        {var body=player.GetComponent<Rigidbody>();body.position=new(x,1.05f,0);if(!body.isKinematic)body.linearVelocity=Vector3.zero;Physics.SyncTransforms();}
        private void AdvanceTo(double target) { Assert.That(scene.Context.Session.AdvanceTime(target-ClockState.ElapsedSeconds),Is.True); scene.Hazard.Process(); }
        private static QusapRaidHazardSettings Fast(float damage=10) => new(new(-12,-.5f,12,11),
            new[]{new QusapRaidHazardStage(3,new(-4,-.5f,4,6),damage,1)},1);
        [UnityTest] public IEnumerator DeploymentAndFirstStageAreHarmlessAndNormalTimesAreRetained()
        {
            Assert.That(scene.Context,Is.Null);yield return Tick(.2f);Assert.That(scene.Context,Is.Null);
            yield return Start(480,180,settings:QusapRaidHazardSettings.Normal());
            Assert.That(P1.Receiver.CurrentHealth,Is.EqualTo(100));AdvanceTo(239.999);yield return Tick(.05f);
            Assert.That(scene.Hazard.StageIndex,Is.EqualTo(-1));Assert.That(scene.Hazard.WarningActive,Is.True);
            Assert.That(P1.Receiver.CurrentHealth,Is.EqualTo(100));AdvanceTo(240);yield return Tick(.05f);
            Assert.That(scene.Hazard.StageIndex,Is.Zero);Assert.That(scene.Hazard.IsExposed(P1),Is.True);
            Assert.That(P1.Receiver.CurrentHealth,Is.EqualTo(100));AdvanceTo(241);yield return Tick(.05f);
            Assert.That(P1.Receiver.CurrentHealth,Is.EqualTo(90));
        }
        [UnityTest] public IEnumerator WarningStagesAndVerticalBoundsFollowSharedClock()
        {
            yield return Start();Place(P1,0);Place(P2,1);AdvanceTo(3);yield return Tick(.05f);
            Assert.That(scene.Hazard.WarningActive,Is.True);AdvanceTo(6);yield return Tick(.05f);
            Assert.That(scene.Hazard.CurrentArea.MaxX,Is.EqualTo(10));AdvanceTo(12);AdvanceTo(18);AdvanceTo(24);yield return Tick(.05f);
            Assert.That(scene.Hazard.StageIndex,Is.EqualTo(3));Assert.That(scene.Hazard.NextStage,Is.Null);
            Assert.That(P1.Receiver.CurrentHealth,Is.EqualTo(100));Assert.That(P2.Receiver.CurrentHealth,Is.EqualTo(100));
            var body=P1.GetComponent<Rigidbody>();body.position=new(0,6.01f,0);Assert.That(scene.Hazard.IsExposed(P1),Is.True);
            body.position=new(0,5.3f,0);Assert.That(scene.Hazard.IsExposed(P1),Is.False);
        }
        [UnityTest] public IEnumerator RealDamageStopsInsideAndResumesOutsideWithoutMotorOrColliderChanges()
        {
            yield return Start(settings:Fast());Place(P1,5);Place(P2,0);
            var colliders=scene.Raid.GetComponentsInChildren<Collider>(true);var enabled=colliders.Select(c=>c.enabled).ToArray();
            var body=P1.GetComponent<Rigidbody>();var constraints=body.constraints;
            AdvanceTo(4);yield return Tick(.05f);Assert.That(P1.Receiver.CurrentHealth,Is.EqualTo(90));
            Place(P1,0);AdvanceTo(7);yield return Tick(.05f);Assert.That(P1.Receiver.CurrentHealth,Is.EqualTo(90));
            Place(P1,5);AdvanceTo(8);yield return Tick(.05f);Assert.That(P1.Receiver.CurrentHealth,Is.EqualTo(80));
            Assert.That(body.constraints,Is.EqualTo(constraints));Assert.That(colliders.Select(c=>c.enabled),Is.EqualTo(enabled));
            Assert.That(scene.Raid.Participants.SelectMany(p=>p.GetComponentsInChildren<Animator>(true)).All(a=>!a.applyRootMotion),Is.True);
        }
        [UnityTest] public IEnumerator ExtractionReceivesHazardDamageCancelsAndRequiresVolumeReentry()
        {
            yield return Start(settings:Fast());Place(P2,0);AdvanceTo(2);Place(P1,8.8f);yield return Tick(.15f);
            var extraction=P1.GetComponent<QusapRaidExtraction>();
            Assert.That(extraction.Status,Is.EqualTo(QusapExtractionStatus.Extracting));
            AdvanceTo(3.5);yield return Tick(.05f);Assert.That(extraction.Countdown.Elapsed,Is.EqualTo(1.5f).Within(.001));
            AdvanceTo(4);yield return Tick(.05f);Assert.That(P1.Receiver.CurrentHealth,Is.EqualTo(90));
            Assert.That(extraction.Countdown.Cancellation,Is.EqualTo(QusapExtractionCancellation.Damage));Assert.That(extraction.Countdown.Elapsed,Is.Zero);
            AdvanceTo(4.5);yield return Tick(.05f);Assert.That(extraction.Status,Is.EqualTo(QusapExtractionStatus.Active));
            Place(P1,0);yield return Tick(.12f);Place(P1,8.8f);yield return Tick(.12f);
            Assert.That(extraction.Status,Is.EqualTo(QusapExtractionStatus.Extracting));Assert.That(extraction.Countdown.Elapsed,Is.Zero);
            AdvanceTo(5);yield return Tick(.05f);Assert.That(P1.Receiver.CurrentHealth,Is.EqualTo(80));
            Assert.That(extraction.Countdown.Cancellation,Is.EqualTo(QusapExtractionCancellation.Damage));
        }
        [UnityTest] public IEnumerator SimultaneousNormalHealthDeathsAreFencedUntilBothLateUpdatesWithoutWinner()
        {
            yield return Start(settings:Fast(50));Place(P1,5);Place(P2,-5);
            AdvanceTo(5);var authority=scene.Context.Session.Competition;
            Assert.That(P1.Receiver.CurrentHealth,Is.Zero);Assert.That(P2.Receiver.CurrentHealth,Is.Zero);
            Assert.That(authority.EnvironmentalDeathsPending,Is.EqualTo(2));Assert.That(authority.ExpirationStarted,Is.False);
            Assert.That(authority.EvaluateVictory(),Is.False);yield return Tick(.15f);
            Assert.That(authority.FinalResult.Reason,Is.EqualTo(QusapRaidSessionFinishReason.AllParticipantsResolved));
            Assert.That(authority.EliminatedCount,Is.EqualTo(2));Assert.That(authority.WinnerId,Is.Null);
            Assert.That(scene.Profiles.All(p=>!p.HasTriumphHorn),Is.True);Assert.That(scene.Profiles.All(p=>p.ReadStashSnapshot(p.ProfileId).Count==1),Is.True);
            Assert.That(authority.SessionFinishedEvents,Is.EqualTo(1));Assert.That(scene.Context.Session.World.LoanersDestroyed,Is.EqualTo(2));
        }
        [UnityTest] public IEnumerator EnvironmentalSurvivorGetsOneWinnerAndHazardStopsAfterFinish()
        {
            yield return Start(settings:Fast(50));Place(P1,5);Place(P2,0);AdvanceTo(5);yield return Tick(.15f);
            Assert.That(P1.Receiver.IsEliminated,Is.True);Assert.That(scene.Context.Session.Competition.WinnerId,Is.EqualTo("P2"));
            Assert.That(scene.Profiles[1].TriumphHornGrantsPerformed,Is.EqualTo(1));Assert.That(scene.Profiles[1].ReadStashSnapshot(scene.Profiles[1].ProfileId).Count,Is.EqualTo(2));
            var ticks=scene.Hazard.TicksProcessed;scene.Hazard.Process();yield return Tick(.2f);
            Assert.That(scene.Hazard.TicksProcessed,Is.EqualTo(ticks));Assert.That(P2.Receiver.CurrentHealth,Is.EqualTo(100));
            Assert.That(scene.LastSummary,Does.Contain("LastSurvivor").And.Contain("P2"));
        }
        [UnityTest] public IEnumerator ExactDeadlineBeatsAnOverdueLethalHazardTick()
        {
            yield return Start(8,2,settings:Fast(50));Place(P1,5);Place(P2,-5);
            var before=scene.Hazard.DamageApplications;AdvanceTo(8);yield return Tick(.15f);
            Assert.That(scene.Hazard.DamageApplications,Is.EqualTo(before));
            Assert.That(scene.Context.Session.Competition.FinalResult.Reason,Is.EqualTo(QusapRaidSessionFinishReason.Expired));
            Assert.That(scene.Context.Session.Competition.WinnerId,Is.Null);Assert.That(scene.Profiles.All(p=>!p.HasTriumphHorn),Is.True);
        }
        [UnityTest] public IEnumerator ExtractionAndTerminalParticipantsReceiveNoLaterHazardOperations()
        {
            yield return Start(settings:Fast());AdvanceTo(2);Place(P1,8.8f);Place(P2,0);yield return Tick(.1f);
            Assert.That(scene.Context.Session.SettleExtraction(P1.State,P1.WeaponAdapter),Is.EqualTo(QusapLootResult.Success));
            float health=P1.Receiver.CurrentHealth;scene.Hazard.Process();yield return Tick(.1f);
            Assert.That(P1.State.Status,Is.EqualTo(QusapRaidInventoryStatus.Extracted));Assert.That(P1.Receiver.CurrentHealth,Is.EqualTo(health));
            Assert.That(scene.Hazard.IsExposed(P1),Is.False);Assert.That(scene.Hazard.Enabled,Is.False);
        }
        [UnityTest] public IEnumerator NativeKeyboardMovementEntersSafeAreaAndStopsDamageWithoutTeleport()
        {
            yield return Start(settings:Fast());Place(P1,5);Place(P2,0);AdvanceTo(4);yield return Tick(.05f);
            Assert.That(P1.Receiver.CurrentHealth,Is.EqualTo(90));var keyboard=Keyboard.current;
            devices.Press(keyboard.aKey);yield return Tick(.5f);devices.Release(keyboard.aKey);yield return Tick(.1f);
            Assert.That(P1.GetComponent<Rigidbody>().position.x,Is.LessThan(4));AdvanceTo(5);yield return Tick(.05f);
            Assert.That(P1.Receiver.CurrentHealth,Is.EqualTo(90));Assert.That(scene.Hazard.IsExposed(P1),Is.False);
        }
        [UnityTest] public IEnumerator EnvironmentalTickBypassesNativeParryAttemptAndDoesNotChangeVelocity()
        {
            yield return Start(settings:Fast());Place(P1,5);Place(P2,0);devices.Press(Keyboard.current.eKey);yield return Tick(.05f);
            var velocity=P1.GetComponent<Rigidbody>().linearVelocity;
            AdvanceTo(4);Assert.That(P1.Receiver.CurrentHealth,Is.EqualTo(90));
            Assert.That(P1.GetComponent<Rigidbody>().linearVelocity,Is.EqualTo(velocity));devices.Release(Keyboard.current.eKey);yield return Tick(.05f);
        }
        [UnityTest] public IEnumerator ConnectedPlatformsSupportNativeJumpRouteInsideFinalArea()
        {
            yield return Start();Place(P1,3.4f);Place(P2,-3.4f);yield return Tick(.15f);
            var keyboard=Keyboard.current;var body=P1.GetComponent<Rigidbody>();
            devices.Press(keyboard.spaceKey);yield return Tick(.3f);devices.Press(keyboard.aKey);
            int budget=25;while(body.position.x>2 && budget-->0)yield return Tick(.02f);
            devices.Release(keyboard.aKey);yield return Tick(.7f);devices.Release(keyboard.spaceKey);yield return Tick(.1f);
            Assert.That(body.position.y,Is.EqualTo(3.2f).Within(.15f),"First connected platform must be reached by native jump");
            devices.Press(keyboard.spaceKey);yield return Tick(.3f);devices.Press(keyboard.aKey);
            budget=25;while(body.position.x>.4f && budget-->0)yield return Tick(.02f);
            devices.Release(keyboard.aKey);yield return Tick(.7f);devices.Release(keyboard.spaceKey);yield return Tick(.1f);
            Assert.That(body.position.y,Is.EqualTo(5.2f).Within(.15f),"Upper platform must remain reachable without altering the motor");
            Assert.That(scene.Hazard.Settings.Stages.Last().Area.Contains((Vector2)body.position),Is.True);
            Assert.That(body.position.z,Is.EqualTo(0).Within(.001));
        }
    }
}
#endif
