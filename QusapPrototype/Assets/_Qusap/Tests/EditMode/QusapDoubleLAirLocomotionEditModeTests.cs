using System;
using System.Linq;
using NUnit.Framework;

namespace Qusap.Tests
{
    public sealed class QusapDoubleLAirLocomotionEditModeTests
    {
        object observer;
        [SetUp]public void SetUp()
        {
            var type=AppDomain.CurrentDomain.GetAssemblies().Select(a=>a.GetType("QusapDoubleLAirLocomotion")).First(t=>t!=null);
            observer=Activator.CreateInstance(type);
            Observe(true,0,0);
        }
        void Observe(bool grounded,float x,float y,bool interrupted=false,float delta=.02f)=>observer.GetType().GetMethod("Observe").Invoke(observer,new object[]{delta,grounded,x,y,interrupted});
        T Read<T>(string name)=>(T)observer.GetType().GetProperty(name).GetValue(observer);
        [Test]public void PositiveJumpVelocityOverridesAStillGroundedSensor()
        {
            Observe(true,0,11);Assert.That(Read<string>("State"),Is.EqualTo("JumpTakeoff"));
            Observe(false,0,7,false,.13f);Assert.That(Read<string>("State"),Is.EqualTo("JumpRise"));
        }
        [Test]public void WalkingOffALedgeNeverInventsATakeoff()
        {
            Observe(false,8,-1);Assert.That(Read<string>("State"),Is.EqualTo("Fall"));
        }
        [Test]public void EarlyJumpCutCanEnterFallBeforeTakeoffRecoveryFinishes()
        {
            Observe(true,0,11);Observe(false,0,-.1f);
            Assert.That(Read<string>("State"),Is.EqualTo("Fall"));
        }
        [TestCase(-8f,"LandSoft")][TestCase(-17f,"LandHard")]
        public void LandingUsesTheDescentVelocityEvenWhenTheMotorHasAlreadyStopped(float velocity,string state)
        {
            Observe(false,0,velocity);Observe(true,0,0);
            Assert.That(Read<string>("State"),Is.EqualTo(state));
            Assert.That(Read<float>("LastImpactSpeed"),Is.EqualTo(-velocity));
            Assert.That(Read<uint>("LandingSequence"),Is.EqualTo(1));
            Observe(true,0,0);Assert.That(Read<uint>("LandingSequence"),Is.EqualTo(1));
        }
        [TestCase(false,8f)][TestCase(true,0f)]
        public void MovementOrExistingGameplayInterruptionEndsLandingWithoutADelayedRecovery(bool interrupted,float speed)
        {
            Observe(false,0,-17);Observe(true,0,0);
            Assert.That(Read<string>("State"),Is.EqualTo("LandHard"));
            Observe(true,speed,0,interrupted);Assert.That(Read<string>("State"),Is.Null);
            Observe(true,0,0);Assert.That(Read<string>("State"),Is.Null);
        }
        [Test]public void AnotherJumpInterruptsLandingImmediately()
        {
            Observe(false,0,-8);Observe(true,0,0);Observe(true,0,11);
            Assert.That(Read<string>("State"),Is.EqualTo("JumpTakeoff"));
        }
    }
}
