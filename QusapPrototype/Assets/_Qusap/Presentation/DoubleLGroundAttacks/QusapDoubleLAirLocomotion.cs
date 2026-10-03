using UnityEngine;

// Reads the existing motor results. This observer never moves a Transform,
// writes velocity, consumes input, or changes attack eligibility.
public sealed class QusapDoubleLAirLocomotion
{
    public const float TakeoffDuration=.12f, SoftLandingDuration=.22f, HardLandingDuration=.34f;
    // Native full jumps land near 12.4 m/s; falls from a platform after a
    // full jump reach about 17 m/s. This affects presentation alone.
    public const float HardLandingSpeed=14f;
    bool airborne, initialized;
    float takeoffRemaining, landingRemaining, mostNegativeVelocity;
    public string State { get; private set; }
    public float LastImpactSpeed { get; private set; }
    public bool HardLanding { get; private set; }
    public uint LandingSequence { get; private set; }
    public void Observe(float delta,bool sensorGrounded,float horizontalVelocity,float verticalVelocity,bool interrupted)
    {
        bool inAir=!sensorGrounded||verticalVelocity>0;
        takeoffRemaining=Mathf.Max(0,takeoffRemaining-delta);
        landingRemaining=Mathf.Max(0,landingRemaining-delta);
        if(!initialized){initialized=true;airborne=inAir;mostNegativeVelocity=Mathf.Min(0,verticalVelocity);}
        if(inAir)
        {
            if(!airborne)
            {
                takeoffRemaining=verticalVelocity>0?TakeoffDuration:0;
                mostNegativeVelocity=Mathf.Min(0,verticalVelocity);
            }
            mostNegativeVelocity=Mathf.Min(mostNegativeVelocity,verticalVelocity);
            landingRemaining=0;
            State=verticalVelocity>0?(takeoffRemaining>0?"JumpTakeoff":"JumpRise"):"Fall";
        }
        else
        {
            if(airborne)
            {
                LastImpactSpeed=-mostNegativeVelocity;
                HardLanding=LastImpactSpeed>=HardLandingSpeed;
                LandingSequence++;
                landingRemaining=HardLanding?HardLandingDuration:SoftLandingDuration;
            }
            // Existing movement, attacks, hitstun, dash and roll may interrupt
            // recovery immediately. No visual state adds a gameplay lock.
            if(interrupted||Mathf.Abs(horizontalVelocity)>.1f)landingRemaining=0;
            State=landingRemaining>0?(HardLanding?"LandHard":"LandSoft"):null;
        }
        airborne=inAir;
    }
}
