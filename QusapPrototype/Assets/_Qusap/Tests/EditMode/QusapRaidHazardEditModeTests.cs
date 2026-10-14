using System;
using System.IO;
using System.Linq;
using System.Reflection;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace Qusap.Tests
{
    public sealed class QusapRaidHazardEditModeTests
    {
        private sealed class FaultFiles : QusapMemoryProfileFiles
        {
            public bool Fail,AfterStage;
            public Action DuringWrite;
            public override void WriteClosedAndFlushed(string path,string content)
            {
                DuringWrite?.Invoke();
                if(Fail && !AfterStage) throw new IOException("Hazard rejection before staging");
                base.WriteClosedAndFlushed(path,content);
                if(Fail) throw new IOException("Hazard rejection after staging");
            }
            public void RecoverTemporary(string path)
            {
                var data=(System.Collections.Generic.Dictionary<string,string>)typeof(QusapMemoryProfileFiles)
                    .GetField("contents",BindingFlags.Instance|BindingFlags.NonPublic).GetValue(this);
                Assert.That(data.Remove(path),Is.True);
            }
        }
        private QusapSharedRaidContext context;
        private QusapRaidInventory[] actors;
        private QusapPersistentStashRepository[] profiles;
        private FaultFiles[] files;
        private QusapRaidHazard hazard;
        private ulong native;
        private static void Invoke(object target,string method) => target.GetType().GetMethod(method,BindingFlags.Instance|BindingFlags.NonPublic).Invoke(target,null);
        [SetUp] public void Setup() => Create();
        private void Create(float damage=10,int count=2)
        {
            Cleanup();
            var catalog=AssetDatabase.LoadAssetAtPath<QusapRaidLootCatalog>("Assets/_Qusap/Settings/RaidLootCatalog/RaidLootCatalog.asset");
            files=Enumerable.Range(0,count).Select(_=>new FaultFiles()).ToArray();
            profiles=Enumerable.Range(0,count).Select(i=>new QusapPersistentStashRepository(
                Path.Combine(Path.GetTempPath(),"QusapHazardMemoryTests",Guid.NewGuid().ToString("N")),catalog.Definitions,"hazard-profile-"+i,files[i])).ToArray();
            context=new QusapSharedRaidContext(new QusapRaidTimingSettings(30,2),Enumerable.Range(1,count).Select(i=>"P"+i).ToArray());
            actors=Enumerable.Range(1,count).Select(i=>new GameObject("HazardActor"+i,typeof(Rigidbody),typeof(QusapHitReceiver),typeof(QusapRaidInventory)).GetComponent<QusapRaidInventory>()).ToArray();
            for(int i=0;i<count;i++)
            {
                var actor=actors[i];actor.Configure("P"+(i+1),profiles[i].ProfileId);
                Invoke(actor,"Awake");Invoke(actor,"OnEnable");
                var combat=actor.gameObject.AddComponent<QusapCombatController>();
                var equipment=actor.gameObject.AddComponent<QusapWeaponEquipment>();
                Assert.That(equipment.TryInitialize(combat),Is.True);
                var deployment=new QusapRaidDeploymentAuthority(profiles[i],catalog,actor.ParticipantId,equipment,d=>new QusapWeaponInstance(++native,d.WeaponDefinition));
                Assert.That(deployment.PrepareLoadout() && context.Add(deployment),Is.True);
            }
            Assert.That(context.CommitDeployment() && context.StartRunning(),Is.True);
            foreach(var actor in actors)
            {
                var d=context.Deployments.Single(p=>p.ParticipantId==actor.ParticipantId);
                typeof(QusapRaidInventory).GetMethod("BindDeployment",BindingFlags.Instance|BindingFlags.NonPublic)
                    .Invoke(actor,new object[]{context.Session,d.Inventory,d.WeaponAdapter});
                actor.GetComponent<Rigidbody>().position=new(0,1,0);
            }
            hazard=new QusapRaidHazard(context.Session,new QusapRaidHazardSettings(new(-10,0,10,10),
                new[]{new QusapRaidHazardStage(3,new(-1,0,1,2),damage,1)}),actors,actors[0]);
        }
        [TearDown] public void Cleanup()
        {
            context?.Dispose();context=null;
            if(actors!=null) foreach(var actor in actors) UnityEngine.Object.DestroyImmediate(actor.gameObject);
            actors=null;
        }
        private void At(double time) { Assert.That(context.Session.AdvanceTime(time-context.Session.Timing.ElapsedSeconds),Is.True);hazard.Process(); }
        private void Expose(int i) => actors[i].GetComponent<Rigidbody>().position=new(5,1,0);
        private void Eliminate(int i) => Invoke(actors[i].Receiver,"LateUpdate");
        private void GivePockets()
        {
            var catalog=AssetDatabase.LoadAssetAtPath<QusapRaidLootCatalog>("Assets/_Qusap/Settings/RaidLootCatalog/RaidLootCatalog.asset");
            Assert.That(catalog.TryResolve("raid_rare_relic",out var definition),Is.True);
            foreach(var actor in actors)
            {
                var item=context.Session.World.Create(definition,"Hazard persistence pocket");
                Assert.That(actor.State.TryPickup(item.LootInstanceId),Is.EqualTo(QusapLootResult.Success));
                Assert.That(actor.State.TryMoveToSecurePocket(item.LootInstanceId),Is.EqualTo(QusapLootResult.Success));
            }
        }
        [Test] public void NormalStagesAre240300360420NestedWithConfigurableDamageAndWarnings()
        {
            var s=QusapRaidHazardSettings.Normal();
            Assert.That(s.Stages.Select(p=>p.StartsAt),Is.EqualTo(new double[]{240,300,360,420}));
            Assert.That(s.Stages.Select(p=>p.Damage),Is.EqualTo(new float[]{10,12,15,20}));
            Assert.That(s.TickSeconds,Is.EqualTo(1));Assert.That(s.Stages.All(p=>p.WarningSeconds==15),Is.True);
            var previous=s.InitialArea;foreach(var stage in s.Stages){Assert.That(previous.Contains(stage.Area),Is.True);previous=stage.Area;}
            Assert.That(previous.Contains(new Vector2(-2.3f,2.2f)) && previous.Contains(new Vector2(2.3f,2.2f)) && previous.Contains(new Vector2(0,5.3f)),Is.True);
        }
        [TestCase(1f)] [TestCase(float.NaN)] [TestCase(float.PositiveInfinity)]
        public void InvalidAreaIsRejected(float value) => Assert.Throws<ArgumentException>(()=>new QusapRaidSafeArea(value,0,1,2));
        [Test] public void UnorderedOrUnnestedOrUnchangedStagesAreRejected()
        {
            var a=new QusapRaidSafeArea(-2,0,2,4);var small=new QusapRaidSafeArea(-1,0,1,3);
            Assert.Throws<ArgumentException>(()=>new QusapRaidHazardSettings(a,new[]{new QusapRaidHazardStage(3,small,10),new QusapRaidHazardStage(2,new(-.5f,0,.5f,2),10)}));
            Assert.Throws<ArgumentException>(()=>new QusapRaidHazardSettings(a,new[]{new QusapRaidHazardStage(3,new(-3,0,1,2),10)}));
            Assert.Throws<ArgumentException>(()=>new QusapRaidHazardSettings(a,new[]{new QusapRaidHazardStage(3,a,10)}));
            Assert.Throws<ArgumentException>(()=>new QusapRaidHazardSettings(a,new[]{new QusapRaidHazardStage(3,small,10)},0));
        }
        [Test] public void BoundsAreInclusiveXYAndIgnoreDepth()
        {
            Expose(0);At(3);Assert.That(hazard.IsExposed(actors[0]),Is.True);
            actors[0].GetComponent<Rigidbody>().position=new(1,2,123);Assert.That(hazard.IsExposed(actors[0]),Is.False);
            actors[0].GetComponent<Rigidbody>().position=new(0,2.001f,0);Assert.That(hazard.IsExposed(actors[0]),Is.True);
        }
        [Test] public void NoDamageBeforeFirstStageAndWarningHasNoDamageAuthority()
        {
            Expose(0);At(1.999);Assert.That(hazard.WarningActive,Is.False);
            At(2);Assert.That(hazard.WarningActive,Is.True);At(3.999);
            Assert.That(actors[0].Receiver.CurrentHealth,Is.EqualTo(100));At(4);Assert.That(actors[0].Receiver.CurrentHealth,Is.EqualTo(90));
        }
        [Test] public void ReentryStopsFutureDamageAndLeavingResumesOnlyAtNextScheduledTick()
        {
            Expose(0);At(4);Assert.That(actors[0].Receiver.CurrentHealth,Is.EqualTo(90));
            actors[0].GetComponent<Rigidbody>().position=new(0,1,0);At(7);Assert.That(actors[0].Receiver.CurrentHealth,Is.EqualTo(90));
            Expose(0);At(7.99);Assert.That(actors[0].Receiver.CurrentHealth,Is.EqualTo(90));At(8);Assert.That(actors[0].Receiver.CurrentHealth,Is.EqualTo(80));
            Assert.That(actors[1].Receiver.CurrentHealth,Is.EqualTo(100));
        }
        [TestCase(30)] [TestCase(60)] [TestCase(144)]
        public void SameSharedTimeGivesSameDamageAcrossFrameRates(int fps)
        {
            Expose(0);for(int frame=1;frame<=6*fps;frame++) At((double)frame/fps);
            Assert.That(actors[0].Receiver.CurrentHealth,Is.EqualTo(70));Assert.That(hazard.DamageApplications,Is.EqualTo(3));
            hazard.Process();Assert.That(hazard.DamageApplications,Is.EqualTo(3));
        }
        [Test] public void CatchupUsesStageAtTickAndCurrentSampledPosition()
        {
            hazard=new QusapRaidHazard(context.Session,new QusapRaidHazardSettings(new(-10,0,10,10),new[]{
                new QusapRaidHazardStage(3,new(-6,0,6,9),10),new QusapRaidHazardStage(5,new(-1,0,1,2),20)}),actors,actors[0]);
            Expose(0);At(6);Assert.That(actors[0].Receiver.CurrentHealth,Is.EqualTo(60));Assert.That(hazard.TicksProcessed,Is.EqualTo(3));
        }
        [TestCase(false)] [TestCase(true)]
        public void SimultaneousLethalTickNeverGrantsWinnerRegardlessOfEliminationOrder(bool reverse)
        {
            Create(50);Expose(0);Expose(1);At(5);
            Assert.That(context.Session.Competition.EnvironmentalDeathsPending,Is.EqualTo(2));
            Assert.That(context.Session.Competition.EvaluateVictory(),Is.False);Assert.That(context.Session.Competition.ExpirationStarted,Is.False);
            Eliminate(reverse?1:0);Assert.That(context.Session.Competition.WinnerId,Is.Null);Assert.That(context.Session.IsFinished,Is.False);
            Eliminate(reverse?0:1);Assert.That(context.Session.IsFinished,Is.True);Assert.That(context.Session.Competition.EliminatedCount,Is.EqualTo(2));
            Assert.That(context.Session.Competition.WinnerId,Is.Null);Assert.That(profiles.All(p=>!p.HasTriumphHorn),Is.True);
            Assert.That(context.Session.Competition.FinalResult.Reason,Is.EqualTo(QusapRaidSessionFinishReason.AllParticipantsResolved));
        }
        [Test] public void EnvironmentalDeathUsesExistingWinnerReceiptAndOnlyOneHorn()
        {
            Create(50);GivePockets();Expose(0);var revisions=profiles.Select(p=>p.Revision).ToArray();At(5);Eliminate(0);
            Assert.That(context.Session.Competition.WinnerId,Is.EqualTo("P2"));Assert.That(profiles[1].TriumphHornGrantsPerformed,Is.EqualTo(1));
            Assert.That(profiles[1].Storage.Load().Content.Settlements.Count(s=>s.SettlementId==context.RaidId+"/winner/P2"),Is.EqualTo(1));
            Assert.That(profiles.Select(p=>p.Revision),Is.EqualTo(revisions.Select(r=>r+1)));hazard.Process();
            Assert.That(hazard.Enabled,Is.False);Assert.That(actors[1].Receiver.CurrentHealth,Is.EqualTo(100));
        }
        [TestCase(false)] [TestCase(true)]
        public void PendingDeathStaysFencedAcrossPerProfileRejectionAndSafeRetry(bool afterStage)
        {
            Create(50);GivePockets();Expose(0);Expose(1);files[1].Fail=true;files[1].AfterStage=afterStage;
            var revisions=profiles.Select(p=>p.Revision).ToArray();At(5);Eliminate(0);Eliminate(1);
            Assert.That(actors[0].State.Status,Is.EqualTo(QusapRaidInventoryStatus.Settled));
            Assert.That(actors[1].State.Status,Is.EqualTo(QusapRaidInventoryStatus.SettlementPending));
            Assert.That(profiles[0].Revision,Is.EqualTo(revisions[0]+1));Assert.That(profiles[1].Revision,Is.EqualTo(revisions[1]));
            Assert.That(context.Session.Competition.EnvironmentalDeathsPending,Is.EqualTo(1));Assert.That(context.Session.Competition.EvaluateVictory(),Is.False);
            At(6);Assert.That(hazard.DamageApplications,Is.EqualTo(4));Assert.That(context.Session.Competition.WinnerId,Is.Null);
            files[1].Fail=false;
            if(afterStage){Assert.That(actors[1].RetryDeathSettlement(),Is.EqualTo(QusapLootResult.StashUnavailable));files[1].RecoverTemporary(profiles[1].Storage.TemporaryPath);}
            Assert.That(actors[1].RetryDeathSettlement(),Is.EqualTo(QusapLootResult.Success));
            Assert.That(context.Session.IsFinished,Is.True);Assert.That(profiles.Select(p=>p.Revision),Is.EqualTo(revisions.Select(r=>r+1)));
            Assert.That(profiles.All(p=>!p.HasTriumphHorn),Is.True);Assert.That(context.Session.Competition.SessionFinishedEvents,Is.EqualTo(1));
        }
        [Test] public void ExpirationDiscardsOverdueHazardTicksAndUsesExpiredResult()
        {
            Expose(0);Expose(1);At(30);Assert.That(hazard.DamageApplications,Is.Zero);Assert.That(hazard.Enabled,Is.False);
            Assert.That(context.Session.Competition.FinalResult.Reason,Is.EqualTo(QusapRaidSessionFinishReason.Expired));
            Assert.That(profiles.All(p=>!p.HasTriumphHorn),Is.True);
        }
        [TestCase(2)] [TestCase(3)] [TestCase(4)] [TestCase(5)] [TestCase(6)] [TestCase(7)] [TestCase(8)]
        public void SameTickCanKillEntireRosterWithoutOrderDependentHorn(int count)
        {
            Create(50,count);GivePockets();for(int i=0;i<count;i++)Expose(i);At(5);
            for(int i=count-1;i>=0;i--)Eliminate(i);
            Assert.That(context.Session.Competition.EliminatedCount,Is.EqualTo(count));Assert.That(context.Session.IsFinished,Is.True);
            Assert.That(profiles.All(p=>!p.HasTriumphHorn && p.ReadStashSnapshot(p.ProfileId).Count==1),Is.True);
            Assert.That(context.Session.Competition.SessionFinishedEvents,Is.EqualTo(1));
        }
        [Test] public void TwoEnvironmentalDeathsOnlyPublishThirdParticipantWinnerAfterBothConfirm()
        {
            Create(50,3);GivePockets();Expose(0);Expose(1);At(5);Eliminate(0);
            Assert.That(context.Session.Competition.WinnerId,Is.Null);Eliminate(1);
            Assert.That(context.Session.Competition.WinnerId,Is.EqualTo("P3"));Assert.That(profiles[2].TriumphHornGrantsPerformed,Is.EqualTo(1));
        }
        [Test] public void EnvironmentalSettlementFailureCanReachExpirationAndRetryWithoutWinner()
        {
            Create(50);GivePockets();Expose(0);files[0].Fail=true;At(5);Eliminate(0);
            Assert.That(context.Session.Competition.EnvironmentalDeathsPending,Is.EqualTo(1));
            At(30);Assert.That(context.Session.IsFinished,Is.False);Assert.That(context.Session.Competition.ExpirationStarted,Is.True);
            Assert.That(context.Session.Competition.WinnerId,Is.Null);files[0].Fail=false;
            Assert.That(context.Session.AdvanceTime(0),Is.True);Assert.That(context.Session.IsFinished,Is.True);
            Assert.That(context.Session.Competition.FinalResult.Reason,Is.EqualTo(QusapRaidSessionFinishReason.Expired));
            Assert.That(profiles.All(p=>!p.HasTriumphHorn),Is.True);
        }
        [Test] public void EnvironmentalCallbacksCannotReenterClockOrEvaluateWinnerOrExtract()
        {
            Expose(0);int callbacks=0;
            actors[0].Receiver.HealthChanged+=_=>{callbacks++;Assert.That(context.Session.AdvanceTime(30),Is.False);
                Assert.That(context.Session.Competition.EvaluateVictory(),Is.False);
                Assert.That(context.Session.SettleExtraction(actors[1].State,actors[1].WeaponAdapter),Is.EqualTo(QusapLootResult.Blocked));hazard.Process();};
            At(4);Assert.That(callbacks,Is.EqualTo(1));Assert.That(context.Session.Timing.ElapsedSeconds,Is.EqualTo(4));
        }
        [Test] public void ReservedPerProfileSettlementCannotReenterEnvironmentalDamage()
        {
            Create(50,3);GivePockets();Expose(0);At(5);int calls=0;
            files[0].DuringWrite=()=>{calls++;Assert.That(context.Session.ApplyEnvironmentalDamageBatch(new[]{actors[1]},10,actors[0]),Is.False);
                Assert.That(context.Session.AdvanceTime(1),Is.False);};
            Eliminate(0);Assert.That(calls,Is.EqualTo(1));Assert.That(actors[1].Receiver.CurrentHealth,Is.EqualTo(100));
        }
    }
}
