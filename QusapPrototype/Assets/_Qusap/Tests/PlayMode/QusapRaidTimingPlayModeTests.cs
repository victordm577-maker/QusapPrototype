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
    public sealed class QusapRaidTimingPlayModeTests
    {
        private InputTestFixture devices;
        private object runtime;
        private QusapRaidTimingPlayground scene;
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
            var load=EditorSceneManager.LoadSceneAsyncInPlayMode("Assets/_Qusap/Scenes/RaidTimingPlayground.unity",new LoadSceneParameters(LoadSceneMode.Single));
            while(!load.isDone) yield return null;
            new GameObject("TimingInputTestClock").AddComponent<QusapVitalityTestClock>().Synchronize=InputClock;yield return Tick(.2f);
            scene=Object.FindAnyObjectByType<QusapRaidTimingPlayground>();Assert.That(scene,Is.Not.Null);
        }
        private IEnumerator Start(double duration=30,double opening=2,bool manual=true)
        {
            scene.ConfigureTimes(new QusapRaidTimingSettings(duration,opening));
            Assert.That(scene.BeginRaid(),Is.True);yield return Tick(.15f);
            if(manual) scene.Raid.GetComponent<QusapRaidTimingDriver>().enabled=false;
        }
        [UnityTearDown] public IEnumerator Cleanup()
        {
            foreach(var root in SceneManager.GetActiveScene().GetRootGameObjects()){root.SetActive(false);Object.Destroy(root);}
            yield return null;yield return null;devices.TearDown();
            Assert.That(Time.timeScale,Is.EqualTo(scale));Assert.That(Time.fixedDeltaTime,Is.EqualTo(step));
        }
        private static void Place(QusapRaidInventory player,float x)
        {var body=player.GetComponent<Rigidbody>();body.position=new(x,1.05f,0);if(!body.isKinematic)body.linearVelocity=Vector3.zero;Physics.SyncTransforms();}
        private void AdvanceTo(double target)=>Assert.That(scene.Context.Session.AdvanceTime(target-ClockState.ElapsedSeconds),Is.True);
        [UnityTest] public IEnumerator OccupancyAndApiAreBlockedUntilExactOpeningThenFiveSecondsStartFromZero()
        {
            yield return Start();Place(P1,5.9f);yield return Tick(.12f);
            var extraction=P1.GetComponent<QusapRaidExtraction>();var volume=Object.FindAnyObjectByType<QusapExtractionVolume>();
            var winnerExtraction=P2.GetComponent<QusapRaidExtraction>();
            var diagnostics=scene.Canvas.GetComponentsInChildren<UnityEngine.UI.Text>(true)
                .Single(t=>t.transform.parent.name=="TimingDiagnostics");
            var session=scene.Context.Session;int p1Settled=0,p2Settled=0;
            session.ExtractionSettled+=state=>{if(state==P1.State)p1Settled++;if(state==P2.State)p2Settled++;};
            long winnerRevision=scene.Profiles[1].Revision;
            var winnerInventory=P2.State.ReadInventorySnapshot();
            var winnerLoot=winnerInventory.Backpack.Concat(new[]{winnerInventory.SecurePocket})
                .Where(i=>i.HasValue).Select(i=>i.Value.LootInstanceId).ToArray();
            Assert.That(volume.Contains(winnerExtraction),Is.False);
            Assert.That(winnerExtraction.Countdown.Elapsed,Is.Zero);
            Assert.That(volume.Contains(extraction),Is.True);Assert.That(extraction.Countdown.Elapsed,Is.Zero);
            Assert.That(extraction.TryBegin(volume),Is.False);
            Assert.That(scene.Context.Session.SettleExtraction(P1.State,P1.WeaponAdapter),Is.EqualTo(QusapLootResult.Blocked));
            AdvanceTo(1.999);yield return Tick(.05f);Assert.That(extraction.Status,Is.EqualTo(QusapExtractionStatus.Active));
            AdvanceTo(2);yield return Tick(.05f);
            Assert.That(extraction.Status,Is.EqualTo(QusapExtractionStatus.Extracting));Assert.That(extraction.Countdown.Elapsed,Is.Zero);
            AdvanceTo(6.9);yield return Tick(.05f);Assert.That(extraction.Countdown.Elapsed,Is.EqualTo(4.9f).Within(.0001));
            Assert.That(P1.State.Status,Is.EqualTo(QusapRaidInventoryStatus.Active));
            Assert.That(diagnostics.text,Does.Contain("P1: Active · cuenta "+extraction.Countdown.Elapsed.ToString("F2")+"/5 s"));
            Assert.That(diagnostics.text,Does.Contain("P2: Active · cuenta "+0f.ToString("F2")+"/5 s"));
            AdvanceTo(7);yield return Tick(.1f);
            Assert.That(P1.State.Status,Is.EqualTo(QusapRaidInventoryStatus.Extracted));
            Assert.That(scene.Context.Session.Competition.WinnerId,Is.EqualTo("P2"));
            Assert.That(scene.Profiles[0].HasTriumphHorn,Is.False);Assert.That(scene.Profiles[1].HasTriumphHorn,Is.True);
            Assert.That(diagnostics.text,Does.Contain("P1: Extracted · cuenta "+5f.ToString("F2")+"/5 s"));
            Assert.That(diagnostics.text,Does.Contain("P2: Winner · No aplica · Winner"));
            Assert.That(diagnostics.text,Does.Not.Contain("P2: Winner · cuenta"));
            // Complete() is the existing shared retirement signal, not another physical extraction.
            Assert.That(winnerExtraction.Countdown.Elapsed,Is.EqualTo(5));
            Assert.That(p1Settled,Is.EqualTo(1));Assert.That(p2Settled,Is.EqualTo(1));
            Assert.That(session.ExtractionTransfersResolved,Is.EqualTo(2));
            Assert.That(scene.Profiles[1].Revision,Is.EqualTo(winnerRevision+1));
            Assert.That(scene.Profiles[1].TriumphHornGrantsPerformed,Is.EqualTo(1));
            var receipts=scene.Profiles[1].Storage.Load().Content.Settlements;
            Assert.That(receipts.Count(r=>r.SettlementId==scene.Context.RaidId+"/winner/P2"),Is.EqualTo(1));
            Assert.That(receipts.Any(r=>r.SettlementId==scene.Context.RaidId+"/extraction/P2"),Is.False);
            var stash=scene.Profiles[1].ReadStashSnapshot(scene.Profiles[1].ProfileId);
            Assert.That(stash.Select(i=>i.LootInstanceId),Is.EquivalentTo(winnerLoot));
            Assert.That(stash.Select(i=>i.LootInstanceId).Distinct().Count(),Is.EqualTo(stash.Count));
            int extractions=0;
            Assert.That(winnerExtraction.Countdown.Advance(100,()=>{extractions++;return QusapLootResult.Success;}),Is.False);
            Assert.That(winnerExtraction.TryBegin(volume),Is.False);
            Assert.That(session.SettleExtraction(P2.State,P2.WeaponAdapter),Is.EqualTo(QusapLootResult.Blocked));
            Assert.That(session.Competition.EvaluateVictory(),Is.False);yield return Tick(.2f);
            Assert.That(extractions,Is.Zero);Assert.That(p2Settled,Is.EqualTo(1));
            Assert.That(session.ExtractionTransfersResolved,Is.EqualTo(2));
            Assert.That(scene.Profiles[1].Revision,Is.EqualTo(winnerRevision+1));
            Assert.That(scene.Profiles[1].TriumphHornGrantsPerformed,Is.EqualTo(1));
        }
        [UnityTest] public IEnumerator DamageInterruptsCountdownAndStayingInsideDoesNotAutoRestart()
        {
            yield return Start();AdvanceTo(2);Place(P1,5.9f);yield return Tick(.12f);AdvanceTo(3);yield return Tick(.05f);
            var extraction=P1.GetComponent<QusapRaidExtraction>();Assert.That(extraction.Countdown.Elapsed,Is.EqualTo(1).Within(.0001));
            Assert.That(P1.Receiver.TryReceiveEnvironmentDamage(1,scene),Is.True);yield return Tick(.1f);
            Assert.That(extraction.Status,Is.EqualTo(QusapExtractionStatus.Active));
            Assert.That(extraction.Countdown.Elapsed,Is.Zero);Assert.That(extraction.Countdown.Cancellation,Is.EqualTo(QusapExtractionCancellation.Damage));
            Place(P1,4.4f);yield return Tick(.1f);Place(P1,5.9f);yield return Tick(.1f);
            Assert.That(extraction.Status,Is.EqualTo(QusapExtractionStatus.Extracting));Assert.That(extraction.Countdown.Elapsed,Is.Zero);
        }
        [UnityTest] public IEnumerator LeavingAndDisabledVolumeKeepExistingInterruptionRules()
        {
            yield return Start();AdvanceTo(2);Place(P1,5.9f);yield return Tick(.1f);AdvanceTo(3);yield return Tick(.05f);
            var extraction=P1.GetComponent<QusapRaidExtraction>();Place(P1,4.4f);yield return Tick(.12f);
            Assert.That(extraction.Countdown.Cancellation,Is.EqualTo(QusapExtractionCancellation.LeftVolume));Assert.That(extraction.Countdown.Elapsed,Is.Zero);
            Place(P1,5.9f);yield return Tick(.1f);Object.FindAnyObjectByType<QusapExtractionVolume>().enabled=false;yield return Tick(.05f);
            Assert.That(extraction.Countdown.Cancellation,Is.EqualTo(QusapExtractionCancellation.ZoneDisabled));Assert.That(extraction.Countdown.Elapsed,Is.Zero);
        }
        [UnityTest] public IEnumerator DeadlineWinsOverCountdownCompletionAndBothActorsAreEliminated()
        {
            yield return Start(7,2);AdvanceTo(2);Place(P1,5.9f);yield return Tick(.1f);AdvanceTo(6.999);yield return Tick(.05f);
            Assert.That(P1.GetComponent<QusapRaidExtraction>().Countdown.Elapsed,Is.LessThan(5));
            AdvanceTo(7);yield return Tick(.1f);
            Assert.That(scene.Context.Session.Competition.WinnerId,Is.Null);
            Assert.That(scene.Context.Session.Competition.FinalResult.Reason,Is.EqualTo(QusapRaidSessionFinishReason.Expired));
            Assert.That(P1.Receiver.IsEliminated&&P2.Receiver.IsEliminated,Is.True);
            Assert.That(scene.Profiles.All(p=>!p.HasTriumphHorn),Is.True);
            Assert.That(scene.Profiles.All(p=>p.ReadStashSnapshot(p.ProfileId).Count==1),Is.True);
            Assert.That(scene.Context.Session.World.LoanersDestroyed,Is.EqualTo(2));
            Assert.That(scene.Raid.Participants.All(p=>!p.CanOperate),Is.True);
        }
        [UnityTest] public IEnumerator NativeClockFinishesCollectivelyWithoutPreparedHealthOrTimeScaleChanges()
        {
            yield return Start(1,.25,false);
            Assert.That(P1.Receiver.CurrentHealth,Is.EqualTo(P1.Receiver.MaxHealth));Assert.That(P2.Receiver.CurrentHealth,Is.EqualTo(P2.Receiver.MaxHealth));
            yield return Tick(1.1f);
            Assert.That(scene.Context.Session.IsFinished,Is.True);Assert.That(scene.LastSummary,Does.Contain("Expired").And.Contain("Sin ganador"));
            Assert.That(scene.Context.Session.Competition.EliminatedCount,Is.EqualTo(2));Assert.That(ClockState.ElapsedSeconds,Is.EqualTo(1));
            Assert.That(scene.Raid.Participants.SelectMany(p=>p.GetComponentsInChildren<Animator>(true)).All(a=>!a.applyRootMotion),Is.True);
        }
        [UnityTest] public IEnumerator NormalSettingsAreRetainedAndNoClockRunsBeforeDeployment()
        {
            Assert.That(scene.Context,Is.Null);yield return Tick(.2f);Assert.That(scene.Context,Is.Null);
            yield return Start(480,180,false);
            Assert.That(scene.ReducedDiagnosticTimes,Is.False);Assert.That(ClockState.Settings.DurationSeconds,Is.EqualTo(480));
            Assert.That(ClockState.Settings.ExtractionOpeningSeconds,Is.EqualTo(180));Assert.That(ClockState.ElapsedSeconds,Is.GreaterThan(0));
            Assert.That(scene.Raid.Participants.All(p=>p.GetComponent<QusapRaidExtraction>().Countdown.Duration==5),Is.True);
            Assert.That(scene.Context.Session.CanExtract,Is.False);
        }
    }
}
#endif
