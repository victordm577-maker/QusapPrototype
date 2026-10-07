using System;
using System.Linq;
using Qusap;
using UnityEngine;

// Main-player presentation, promoted from the approved isolated adapter.
// Combat remains the sole authority for phases, parry, cancellation and damage.
// The grip, arm mapping, carry and corrections below reuse the approved review.
[DefaultExecutionOrder(150)]
public sealed class QusapDoubleLGroundAttackPresenter : MonoBehaviour
{
    [Serializable] public sealed class BonePose { public string name; public Quaternion rotation; }
    [Serializable] public sealed class Pose { public string name; public Vector3 socketLocalPosition; public Quaternion socketLocalRotation; public BonePose[] bones; }
    [Serializable] public sealed class Grip { public Pose[] poses; }
    [SerializeField] QusapCombatController combat;
    [SerializeField] Rigidbody body;
    [SerializeField] QusapGroundSensor ground;
    [SerializeField] GameObject visual;
    [SerializeField] Animator animator;
    [SerializeField] TextAsset approvedGrip;
    // H2 contains sparse local rotations, not Humanoid muscles. Keep the
    // approved clip intact; these serialized curves are copied from that asset.
    [Serializable] public sealed class HeadbuttRotation
    {
        public string path;
        public AnimationCurve x, y, z, w;
        [NonSerialized] public Transform bone;
    }
    [SerializeField] AnimationClip headbuttClip;
    [SerializeField] HeadbuttRotation[] headbuttRotations;
    AnimationClip bodyAttackClip, launchFinisherClip;
    const int BodyAttackClip = 5, HeadbuttClip = 6;
    public const int LaunchFinisherClip = 7;
    public const string LaunchFinisherState = "LaunchFinisherE2";
    public const float LaunchImpactSource = .5f;
    // Approved E2 contact at source .50 s follows the existing C# deadline.
    // This conversion is presentation only; no Animator timer resolves combat.
    public static float LaunchSourceAt(double now, double started, double closed) =>
        Mathf.Lerp(0, LaunchImpactSource, Mathf.Clamp01((float)((now-started)/(closed-started))));
    bool disarmImpactPending;
    QusapDashMotor dash;
    QusapVerticalMotor vertical;
    QusapHitstunController hitstun;
    QusapHitReceiver hitReceiver;
    bool parryAttemptPending, observingParrySuccess;
    static readonly string[] reactionStates = { "AttackerParried", "HitstunLight", "HitstunHeavy" };
    static readonly string[] parryStates = { "ParryAttempt", "ParrySuccess" };
    QusapInputReader input;
    QusapEquippedWeaponPresenter legacyWeaponPresentation;
    QusapWeaponEquipment equipment;
    Renderer[] swordRenderers;
    uint wallJumpSequence;
    float rollRemaining, landingRemaining, wallJumpRemaining;
    bool wasGrounded;
    bool wasDashing, dashStartedGrounded;
    int wallJumpDirection=1, dashVisualDirection=1;
    readonly QusapDoubleLAirLocomotion airLocomotion=new QusapDoubleLAirLocomotion();
    public string AirPresentationState => airLocomotion.State;
    public float LandingImpactSpeed => airLocomotion.LastImpactSpeed;
    public uint LandingSequence => airLocomotion.LandingSequence;
    public string PresentationState { get; private set; } = "CombatIdle_B1";
    public bool IsRolling => rollRemaining > 0;
    QusapSwordHandSwitchV5 facing;
    QusapSwordCarryLeftPoseV5 carry;
    QusapGripMotionClearance clearance;
    QusapFiveAttackCorrection correction;
    Transform[] all, rightArm, leftArm;
    Matrix4x4[] mapA, mapB;
    Grip grip;
    int clip = -1, direction = 1;
    ulong execution;
    float sourceTime, tailRemaining;
    int finisherImpactFrame=-1;
    bool finisher;
    double finisherStarted, finisherOpened, finisherClosed;
    SpriteRenderer finisherCue;
    Sprite cueSprite;
    public bool FinisherCueVisible => finisherCue != null && finisherCue.enabled;
    public double FinisherStartedAt => finisherStarted;
    public double FinisherPresentationElapsed { get; private set; }
    public const float FinisherCueSource = 1f/3f, FinisherOpenSource = .5f, FinisherCloseSource = 111f/120f;
    // Preserve the original .10 s telegraph and .35 s acceptance window.
    // Only B_2's presentation clock changes; combat still owns cancellation/damage.
    public static float FinisherSourceAt(double now,double started,double opened,double closed)
    {
        if(now<=opened) return Mathf.Lerp(0,FinisherOpenSource,Mathf.Clamp01((float)((now-started)/(opened-started))));
        return Mathf.Lerp(FinisherOpenSource,FinisherCloseSource,Mathf.Clamp01((float)((now-opened)/(closed-opened))));
    }
    const float SourceLength = 2f, VisualTail = .22f;
    bool GroundedForPresentation => ground.IsGrounded && body.linearVelocity.y<=0f;
    public int ClipIndex => clip;
    public float SourceTime => sourceTime;
    public int PresentationDirection => direction;
    public GameObject Visual => visual;
    public Animator Animator => animator;
    public QusapSwordHandSwitchV5 Facing => facing;
    public static float ReferenceImpact(int index) => index == 0 ? .5f : index == 1 ? 23f/30f : 28f/30f;
    // The approved 0.25x review validated source poses at 120 Hz. Keep the
    // gameplay clock on that validated source grid instead of introducing new
    // between-sample poses into the frozen procedural correction curves.
    static float ValidatedSourceTime(float value) => Mathf.Floor(value*120f+.0001f)/120f;
    public void Configure(QusapCombatController c, Rigidbody rb, QusapGroundSensor g, GameObject v, Animator a, TextAsset frozen)
    { combat=c; body=rb; ground=g; visual=v; animator=a; approvedGrip=frozen; }
    static Matrix4x4 Ortho(Matrix4x4 m)
    { for(int i=0;i<3;i++) m.SetColumn(i,((Vector3)m.GetColumn(i)).normalized); m.SetColumn(3,new Vector4(0,0,0,1)); return m; }
    void Awake()
    {
        dash=GetComponent<QusapDashMotor>(); vertical=GetComponent<QusapVerticalMotor>();
        hitstun=GetComponent<QusapHitstunController>(); input=GetComponent<QusapInputReader>();
        hitReceiver=GetComponent<QusapHitReceiver>();
        legacyWeaponPresentation=GetComponent<QusapEquippedWeaponPresenter>();
        equipment=GetComponent<QusapWeaponEquipment>();
        wallJumpSequence=vertical.WallJumpSequence; wasGrounded=ground.IsGrounded;
        animator.applyRootMotion=false; animator.cullingMode=AnimatorCullingMode.AlwaysAnimate;
        animator.keepAnimatorStateOnDisable=true;
        // Explicit sampling lets the existing phase clock drive source time without
        // seeking/rebinding the skeleton on every frame. Physics advances normally.
        animator.enabled=false;
        all=visual.GetComponentsInChildren<Transform>(true);
        bodyAttackClip=animator.runtimeAnimatorController.animationClips.Distinct().Single(c=>c.name=="1Hand_Base_Skill_7_InPlace");
        launchFinisherClip=animator.runtimeAnimatorController.animationClips.Distinct().Single(c=>c.name=="1Hand_Base_Attack_E_2_InPlace");
        foreach(var track in headbuttRotations) track.bone=animator.transform.Find(track.path);
        facing=visual.GetComponent<QusapSwordHandSwitchV5>();
        swordRenderers=facing.SwordVisual.GetComponentsInChildren<Renderer>(true);
        carry=visual.GetComponentInChildren<QusapSwordCarryLeftPoseV5>(true);
        clearance=visual.GetComponent<QusapGripMotionClearance>(); correction=visual.GetComponent<QusapFiveAttackCorrection>();
        grip=JsonUtility.FromJson<Grip>(approvedGrip.text);
        foreach(var component in visual.GetComponentsInChildren<MonoBehaviour>(true)) component.enabled=false;
        var skin=visual.GetComponentInChildren<SkinnedMeshRenderer>(); skin.updateWhenOffscreen=true;
        var bones=skin.bones; var binds=skin.sharedMesh.bindposes;
        var labels=new[]{"clavicle","upperarm","lowerarm","hand"};
        rightArm=labels.Select(n=>all.Single(t=>t.name==n+"_r")).ToArray(); leftArm=labels.Select(n=>all.Single(t=>t.name==n+"_l")).ToArray();
        var reflection=Matrix4x4.Scale(new Vector3(-1,1,1)); mapA=new Matrix4x4[4]; mapB=new Matrix4x4[4];
        for(int j=0;j<4;j++)
        {
            var R=Ortho(binds[Array.IndexOf(bones,rightArm[j])].inverse); var L=Ortho(binds[Array.IndexOf(bones,leftArm[j])].inverse);
            mapB[j]=R.inverse*reflection*L;
            mapA[j]=Ortho(binds[Array.IndexOf(bones,rightArm[j].parent)].inverse).inverse*reflection*Ortho(binds[Array.IndexOf(bones,leftArm[j].parent)].inverse);
        }
        animator.SetBool("PurchasedLeftPose",false); animator.Play("CombatIdle_B1",0,0); animator.Update(0); ApplyApprovedPose();
        // Scene presentation only: a warning at sword lift, separate from the
        // original red damage feedback emitted after a successful hit.
        var cueObject=new GameObject("B2_AnticipationCue"); cueObject.transform.SetParent(transform,false);
        cueObject.transform.localPosition=new Vector3(0,1.25f,-.5f);
        cueObject.transform.localRotation=Quaternion.Euler(0,0,45);
        cueObject.transform.localScale=Vector3.one*.3f;
        finisherCue=cueObject.AddComponent<SpriteRenderer>();
        var tex=Texture2D.whiteTexture;
        cueSprite=Sprite.Create(tex,new Rect(0,0,tex.width,tex.height),Vector2.one*.5f,tex.width);
        finisherCue.sprite=cueSprite; finisherCue.color=new Color(1,.08f,.08f,1); finisherCue.sortingOrder=1001; finisherCue.enabled=false;
    }
    void OnEnable()
    {
        combat.FinisherArmed+=Armed; combat.CombatVisualExecutionEnded+=Ended; combat.AttackVariantStarted+=Started; combat.AttackPhaseChanged+=Phased; combat.FinisherParryWindowOpened+=WindowOpened; combat.FinisherReadyToResolve+=WindowClosed;
        combat.ParryAttemptAccepted+=ParryAttemptAccepted; combat.ParryAttemptFinished+=ParryAttemptFinished; combat.ParryFailed+=ParryFailed; combat.ParrySucceeded+=ParrySucceeded; combat.FinisherParried+=AttackerParried;
        hitReceiver.HitReceived+=HitReceived; hitReceiver.FinisherReceived+=FinisherReceived;
        hitstun.HitstunStarted+=HitstunStarted; hitstun.HitstunEnded+=HitstunEnded;
        animator.SetBool("IsHitstunned",hitstun.IsInHitstun);
        animator.SetBool("ParryAttemptActive",false);
        animator.SetBool("ParrySuccessActive",false);
    }
    void OnDisable()
    {
        combat.FinisherArmed-=Armed; combat.CombatVisualExecutionEnded-=Ended; combat.AttackVariantStarted-=Started; combat.AttackPhaseChanged-=Phased; combat.FinisherParryWindowOpened-=WindowOpened; combat.FinisherReadyToResolve-=WindowClosed;
        combat.ParryAttemptAccepted-=ParryAttemptAccepted; combat.ParryAttemptFinished-=ParryAttemptFinished; combat.ParryFailed-=ParryFailed; combat.ParrySucceeded-=ParrySucceeded; combat.FinisherParried-=AttackerParried;
        hitReceiver.HitReceived-=HitReceived; hitReceiver.FinisherReceived-=FinisherReceived;
        hitstun.HitstunStarted-=HitstunStarted; hitstun.HitstunEnded-=HitstunEnded;
        CancelParryPresentation(); animator.SetBool("IsHitstunned",false);
        animator.ResetTrigger("ParrySuccess"); foreach(string name in reactionStates)animator.ResetTrigger(name);
        animator.enabled=false;
        if(finisherCue)finisherCue.enabled=false;
    }
    void CancelParryAttempt()
    { parryAttemptPending=false; animator.SetBool("ParryAttemptActive",false); }
    void CancelParryPresentation()
    {
        CancelParryAttempt(); observingParrySuccess=false;
        animator.SetBool("ParrySuccessActive",false); animator.ResetTrigger("ParrySuccess");
    }
    void ParryAttemptAccepted(QusapCombatCommandPress press)
    {
        CancelParryPresentation(); parryAttemptPending=true;
        animator.SetBool("ParryAttemptActive",true);
    }
    void ParryAttemptFinished(QusapParryAttemptFeedback feedback)
    {
        CancelParryAttempt();
        // Rejected presses neither accept an attempt nor extend its visual life.
        if(feedback.Outcome==QusapParryAttemptOutcome.OnRecovery || feedback.Outcome==QusapParryAttemptOutcome.AlreadyAttempted ||
           feedback.Outcome==QusapParryAttemptOutcome.DuplicateOrStalePressIgnored || feedback.Outcome==QusapParryAttemptOutcome.InvalidTimestampIgnored)return;
        if(feedback.Outcome!=QusapParryAttemptOutcome.Success)CancelParryPresentation();
    }
    void ParryFailed(QusapParryAttemptOutcome outcome) => CancelParryPresentation();
    void ParrySucceeded(QusapCombatController attacker,QusapComboId combo)
    {
        CancelParryPresentation();
        if(hitstun.IsInHitstun)return;
        observingParrySuccess=combat.IsParryAttemptRecovering;
        animator.SetBool("ParrySuccessActive",observingParrySuccess);
        animator.SetTrigger("ParrySuccess");
    }
    void AttackerParried(QusapComboId combo,QusapHitReceiver defender) => TriggerReaction("AttackerParried");
    void HitstunStarted()
    { CancelParryPresentation(); if(clip==BodyAttackClip||clip==HeadbuttClip||clip==LaunchFinisherClip)ReturnToLocomotion(); animator.SetBool("IsHitstunned",true); }
    void HitstunEnded()
    {
        animator.SetBool("IsHitstunned",false);
        foreach(string name in reactionStates)animator.ResetTrigger(name);
    }
    void TriggerReaction(string name)
    {
        // Consume the newest authoritative impact; superseded triggers must not
        // remain queued and appear after a later recovery.
        CancelParryPresentation();
        foreach(string other in reactionStates)animator.ResetTrigger(other);
        animator.SetTrigger(name);
    }
    void HitReceived(QusapHitInfo info)
    {
        // Both weapon variants expose the legacy StrongKick attack type.
        // Their existing variant is the authoritative light/heavy distinction.
        bool heavy=info.AttackVariant==QusapAttackVariant.WeaponStrong ||
            (info.AttackVariant!=QusapAttackVariant.WeaponLight&&info.AttackType==QusapAttackType.StrongKick);
        TriggerReaction(heavy?"HitstunHeavy":"HitstunLight");
    }
    void FinisherReceived(QusapFinisherHitInfo info) => TriggerReaction("HitstunHeavy");
    bool SampleCombatReaction(float delta)
    {
        bool attemptActive=parryAttemptPending;
        bool successActive=observingParrySuccess && combat.IsParryAttemptRecovering;
        if(!successActive)observingParrySuccess=false;
        animator.SetBool("ParryAttemptActive",attemptActive);
        animator.SetBool("ParrySuccessActive",successActive);
        animator.SetBool("IsMoving",Mathf.Abs(body.linearVelocity.x)>=.1f);
        // Reactions use Animator's normal visual clock. Keep manual evaluation
        // for initial entry/new signals only; evaluating again every LateUpdate
        // would replace its normal progress. Native bools alone end reactions.
        var previous=animator.GetCurrentAnimatorStateInfo(0);
        var previousNext=animator.IsInTransition(0)?animator.GetNextAnimatorStateInfo(0):previous;
        bool controlled=reactionStates.Concat(parryStates).Any(n=>previous.IsName(n)||previousNext.IsName(n));
        bool pending=animator.GetBool("ParrySuccess")||reactionStates.Any(n=>animator.GetBool(n));
        if(!attemptActive&&!hitstun.IsInHitstun&&!controlled&&!pending){animator.enabled=false;return false;}
        bool startingVisualClock=!animator.enabled;
        animator.enabled=true;
        // A legacy scripted locomotion crossfade is not an Animator transition
        // and cannot be interrupted by the new AnyState impact transitions.
        // Cancel that visual blend at its current sample, then let the trigger
        // select the reaction. No combat phase or source clock is changed.
        if(pending&&!controlled&&animator.IsInTransition(0))
        { animator.Play(previous.fullPathHash,0,previous.normalizedTime);animator.Update(0); }
        if(startingVisualClock||pending)animator.Update(delta);
        var current=animator.GetCurrentAnimatorStateInfo(0);
        var next=animator.IsInTransition(0)?animator.GetNextAnimatorStateInfo(0):current;
        string state=reactionStates.FirstOrDefault(n=>next.IsName(n))??parryStates.FirstOrDefault(n=>next.IsName(n));
        if(state==null&&hitstun.IsInHitstun&&next.IsName("Hitstun"))state="Hitstun";
        // Do not let the legacy locomotion CrossFade overwrite an impact which
        // the graph is entering. The valid native hitstun still owns this frame.
        if(state==null&&hitstun.IsInHitstun&&pending)state="Hitstun";
        if(state==null){animator.enabled=false;return false;}
        clip=-1; tailRemaining=0; finisher=false; disarmImpactPending=false; finisherCue.enabled=false;
        direction=combat.FacingDirection<0?-1:1;
        PresentationState=state;
        return true;
    }
    void OnDestroy() { if(cueSprite)Destroy(cueSprite); }
    void WindowOpened(QusapComboId combo,QusapHitReceiver target)
    { if(finisher && clip==2) { SampleFinisher(Mathf.Max(sourceTime,FinisherOpenSource)); finisherCue.enabled=true; } }
    void WindowClosed(QusapComboId combo,QusapHitReceiver target)
    { if(finisher && clip==2) { SampleFinisher(FinisherCloseSource); finisherCue.enabled=false; } }
    void SampleFinisher(float target)
    { animator.Update(Mathf.Max(0,target-sourceTime)); sourceTime=target; ApplyApprovedPose(); }
    void Started(QusapAttackVariant variant)
    {
        CancelParryPresentation();
        if(variant==QusapAttackVariant.WeakKickGround || variant==QusapAttackVariant.WeakKickAir ||
           variant==QusapAttackVariant.HeadbuttGround || variant==QusapAttackVariant.DiveHeadbuttAir)
        {
            finisher=false;
            BeginBodyAttack(variant==QusapAttackVariant.WeakKickGround||variant==QusapAttackVariant.WeakKickAir?BodyAttackClip:HeadbuttClip,
                combat.AttackDirection,combat.CurrentAttackExecutionId);
            return;
        }
        if(finisher || !GroundedForPresentation) return;
        if(variant==QusapAttackVariant.WeaponLight || variant==QusapAttackVariant.WeaponStrong)
            Begin(variant==QusapAttackVariant.WeaponLight?0:1,combat.AttackDirection,combat.CurrentAttackExecutionId);
    }
    void Phased(QusapAttackVariant variant,QusapAttackPhase phase)
    {
        if(finisher || clip<0 || clip>1 || execution!=combat.CurrentAttackExecutionId) return;
        float target=phase==QusapAttackPhase.Active?ReferenceImpact(clip):
            phase==QusapAttackPhase.Recovery?ReferenceImpact(clip)+(clip==0?.08f:.1f):sourceTime;
        target=ValidatedSourceTime(target);
        animator.Update(Mathf.Max(0,target-sourceTime)); sourceTime=target;
    }
    void Armed(QusapComboId combo,QusapHitReceiver target)
    {
        CancelParryPresentation();
        if(combo!=QusapComboId.Disarm && !GroundedForPresentation) return;
        finisher=true; finisherStarted=UnityEngine.InputSystem.LowLevel.InputState.currentTime;
        FinisherPresentationElapsed=0;
        finisherOpened=combat.ParryWindowOpensAt; finisherClosed=combat.ParryWindowClosesAt;
        if(combo==QusapComboId.Disarm)BeginBodyAttack(HeadbuttClip,combat.FacingDirection,combat.CurrentAttackExecutionId);
        else if(combo==QusapComboId.Launch)BeginLaunch(combat.FacingDirection,combat.CurrentAttackExecutionId);
        else Begin(2,combat.FacingDirection,combat.CurrentAttackExecutionId);
    }
    void Ended(QusapCombatVisualContext context)
    {
        if(context.CancellationReason!=QusapCombatVisualCancellationReason.Completed)CancelParryPresentation();
        if(context.AttackExecutionId!=execution || clip<0) return;
        if(clip==LaunchFinisherClip)
        {
            finisher=false;
            if(context.IsFinisher && context.ComboId==QusapComboId.Launch &&
               context.CancellationReason==QusapCombatVisualCancellationReason.Completed)
            {
                FinisherPresentationElapsed=finisherClosed-finisherStarted;
                SampleLaunch(LaunchImpactSource);
                tailRemaining=VisualTail; finisherImpactFrame=Time.frameCount;
            }
            else ReturnToLocomotion();
            return;
        }
        if(clip==BodyAttackClip || clip==HeadbuttClip)
        {
            if(context.IsFinisher && context.ComboId==QusapComboId.Disarm &&
               context.CancellationReason==QusapCombatVisualCancellationReason.Completed)
            {
                // C# resolved at the existing .45 s deadline. Show its impact
                // once, just like B_2, without adding a recovery or gameplay lock.
                sourceTime=(float)(finisherClosed-finisherStarted); finisher=false;
                disarmImpactPending=true; finisherImpactFrame=Time.frameCount;
                SampleBodyAttack();
            }
            else ReturnToLocomotion();
            return;
        }
        if(context.IsFinisher)
        {
            finisherCue.enabled=false;
            finisher=false;
            if(context.CancellationReason==QusapCombatVisualCancellationReason.Completed)
            { FinisherPresentationElapsed=finisherClosed-finisherStarted; SampleFinisher(ReferenceImpact(2)); tailRemaining=VisualTail; finisherImpactFrame=Time.frameCount; }
            else ReturnToLocomotion();
        }
        else if(context.CancellationReason!=QusapCombatVisualCancellationReason.Completed) ReturnToLocomotion();
    }
    void Begin(int index,int side,ulong id)
    {
        animator.enabled=false;
        disarmImpactPending=false;
        clip=index; execution=id; direction=side<0?-1:1; sourceTime=0; tailRemaining=0; rollRemaining=0; landingRemaining=0; PresentationState="FiveAttack_"+index;
        facing.SetPreviewFacing(direction); animator.CrossFadeInFixedTime("FiveAttack_"+clip,.12f,0,0); animator.Update(0);
    }
    void BeginLaunch(int side,ulong id)
    {
        animator.enabled=false; clip=LaunchFinisherClip; execution=id; direction=side<0?-1:1;
        sourceTime=0; tailRemaining=0; rollRemaining=0; landingRemaining=0;
        disarmImpactPending=false; finisherCue.enabled=false;
        PresentationState=LaunchFinisherState;
        // Play at zero restarts even when a new execution interrupts E2's tail.
        // The existing presenter selects the state directly, without parameters.
        SampleLaunch(0);
    }
    void SampleLaunch(float target)
    {
        sourceTime=target;
        animator.Play(LaunchFinisherState,0,Mathf.Min(.99999f,target/launchFinisherClip.length));
        animator.Update(0); ApplyApprovedPose();
    }
    void BeginBodyAttack(int index,int side,ulong id)
    {
        animator.enabled=false; clip=index; execution=id; direction=side<0?-1:1;
        sourceTime=0; tailRemaining=0; rollRemaining=0; landingRemaining=0;
        disarmImpactPending=false; finisherCue.enabled=false;
        PresentationState=index==BodyAttackClip?"BodyAttack":"Headbutt";
        SampleBodyAttack();
    }
    float BodySourceAt(QusapCombatVisualContext context)
    {
        var variant=combat.CurrentAttackVariant;
        var air=combat.GetAirAttackData(variant);
        IQusapAttackDefinition data=air??(IQusapAttackDefinition)combat.GetAttackData(
            clip==BodyAttackClip?QusapAttackType.WeakKick:QusapAttackType.Headbutt);
        float p=context.NormalizedProgress;
        if(variant==QusapAttackVariant.DiveHeadbuttAir)
        {
            // Native dive landing/contact can shorten Active and change Recovery.
            // Its phase progress drives anticipation/impact/recovery, never exit.
            return context.Phase==QusapAttackPhase.Startup?Mathf.Lerp(0,.28f,p):
                context.Phase==QusapAttackPhase.Active?Mathf.Lerp(.28f,.45f,p):
                Mathf.Lerp(.45f,headbuttClip.length,p);
        }
        return context.Phase==QusapAttackPhase.Startup?data.StartupTime*p:
            context.Phase==QusapAttackPhase.Active?data.StartupTime+data.ActiveDuration*p:
            data.StartupTime+data.ActiveDuration+data.RecoveryTime*p;
    }
    void SampleBodyAttack()
    {
        if(clip==BodyAttackClip)
        {
            animator.Play("BodyAttack",0,Mathf.Clamp01(sourceTime/bodyAttackClip.length)); animator.Update(0);
            return;
        }
        // Humanoid Idle provides the approved planted base. Evaluate only H2's
        // eight transform rotations so unbound pelvis/feet are never reset.
        animator.Play("Headbutt",0,0); animator.Update(0);
        facing.SetPreviewFacing(direction); facing.Apply(); animator.Update(0);
        float t=Mathf.Clamp(sourceTime,0,headbuttClip.length);
        foreach(var track in headbuttRotations)
            track.bone.localRotation=new Quaternion(track.x.Evaluate(t),track.y.Evaluate(t),track.z.Evaluate(t),track.w.Evaluate(t)).normalized;
    }
    // These states observe the existing providers. They never move the root,
    // consume gameplay actions, change gravity, or create attack eligibility.
    string Locomotion()
    {
        if(hitstun.IsInHitstun) return "Hitstun";
        if(dash.IsDashing) return dashStartedGrounded?"RunForward":"DashAir";
        if(wallJumpRemaining>0) return "WallJump";
        if(vertical.IsWallSliding) return "WallSlide";
        if(!ground.IsGrounded || body.linearVelocity.y>0) return airLocomotion.State??(body.linearVelocity.y>0?"JumpRise":"Fall");
        if(rollRemaining>0) return "RollRMFrontInPlace";
        if(airLocomotion.State!=null) return airLocomotion.State;
        if(Mathf.Abs(body.linearVelocity.x)<.1f) return "CombatIdle_B1";
        return body.linearVelocity.x*combat.FacingDirection>=0?"RunForward":"RunBackward";
    }
    public bool RequestRoll()
    {
        if(!GroundedForPresentation || dash.IsDashing || hitstun.IsInHitstun || combat.IsAttacking || combat.HasArmedFinisher) return false;
        rollRemaining=animator.runtimeAnimatorController.animationClips.Single(c=>c.name=="RM_Roll_front").length;
        animator.CrossFadeInFixedTime("RollRMFrontInPlace",.12f,0,0);
        return true;
    }
    void Update()
    {
        // Retain the approved review's R / D-pad-up presentation shortcut and
        // scope it to the player's already configured device slot.
        bool pressed=input.LocalPlayerSlot==QusapLocalPlayerSlot.Player1Keyboard
            ? UnityEngine.InputSystem.Keyboard.current!=null && UnityEngine.InputSystem.Keyboard.current.rKey.wasPressedThisFrame
            : UnityEngine.InputSystem.Gamepad.all.Count>0 && UnityEngine.InputSystem.Gamepad.all[0].dpad.up.wasPressedThisFrame;
        if(pressed)RequestRoll();
    }
    void ObserveLocomotion(float delta)
    {
        rollRemaining=Mathf.Max(0,rollRemaining-delta); landingRemaining=Mathf.Max(0,landingRemaining-delta);wallJumpRemaining=Mathf.Max(0,wallJumpRemaining-delta);
        if(!wasGrounded && ground.IsGrounded && body.linearVelocity.y<=0)landingRemaining=.12f;
        wasGrounded=ground.IsGrounded;
        if(vertical.WallJumpSequence!=wallJumpSequence)
        {
            wallJumpSequence=vertical.WallJumpSequence;wallJumpRemaining=.14f;
            wallJumpDirection=body.linearVelocity.x<0?-1:1;
        }
        // Classify once at the native dash start. Crossing an edge or landing
        // during the burst must not restart it as a different visual action.
        if(dash.IsDashing&&!wasDashing){dashStartedGrounded=ground.IsGrounded;dashVisualDirection=direction;}
        if(dash.IsDashing&&Mathf.Abs(body.linearVelocity.x)>.001f)dashVisualDirection=body.linearVelocity.x<0?-1:1;
        wasDashing=dash.IsDashing;
        if(dash.IsDashing || hitstun.IsInHitstun || !GroundedForPresentation)rollRemaining=0;
        airLocomotion.Observe(delta,ground.IsGrounded,body.linearVelocity.x,body.linearVelocity.y,
            dash.IsDashing||hitstun.IsInHitstun||combat.IsAttacking||combat.HasArmedFinisher||rollRemaining>0);
    }
    void ReturnToLocomotion()
    { clip=-1; tailRemaining=0; finisher=false; disarmImpactPending=false; finisherCue.enabled=false; animator.CrossFadeInFixedTime(Locomotion(),.12f,0); }
    void LateUpdate()
    {
        // Preserve the native drop/throw presentation reference's facing even
        // while its legacy socket is hidden behind the approved visible sword.
        legacyWeaponPresentation.ApplyFacing(combat.FacingDirection);
        float delta=Time.deltaTime; ObserveLocomotion(delta);
        if(SampleCombatReaction(delta)){ApplyApprovedPose();return;}
        if(clip==LaunchFinisherClip)
        {
            if(finisher)
            {
                double now=UnityEngine.InputSystem.LowLevel.InputState.currentTime;
                FinisherPresentationElapsed=now-finisherStarted;
                // Hold the preceding approved sample until C# actually resolves
                // on its next physics tick; contact is sampled by Ended above.
                SampleLaunch(Mathf.Min(LaunchImpactSource-1f/120f,
                    LaunchSourceAt(now,finisherStarted,finisherClosed)));
                return;
            }
            if(tailRemaining>0 && !combat.IsAttacking && !dash.IsDashing && GroundedForPresentation)
            {
                if(finisherImpactFrame!=Time.frameCount)
                {
                    tailRemaining=Mathf.Max(0,tailRemaining-delta);
                    FinisherPresentationElapsed=finisherClosed-finisherStarted+VisualTail-tailRemaining;
                    SampleLaunch(Mathf.Lerp(LaunchImpactSource,launchFinisherClip.length,1-tailRemaining/VisualTail));
                }
                if(tailRemaining>0)return;
            }
            ReturnToLocomotion();
        }
        if(disarmImpactPending)
        {
            if(finisherImpactFrame==Time.frameCount){SampleBodyAttack();ApplyApprovedPose();return;}
            ReturnToLocomotion();
        }
        if(clip==BodyAttackClip || clip==HeadbuttClip)
        {
            // Only an accepted start initializes these states. A paused or
            // interrupted execution cannot reactivate itself from its context.
            if(combat.TryGetCombatVisualContext(out var bodyContext) && bodyContext.AttackExecutionId==execution)
            {
                sourceTime=finisher?(float)(UnityEngine.InputSystem.LowLevel.InputState.currentTime-finisherStarted):
                    Mathf.Max(sourceTime,BodySourceAt(bodyContext));
                SampleBodyAttack();ApplyApprovedPose();return;
            }
            ReturnToLocomotion();
        }
        if(finisher)
        {
            // Existing finisher damage resolves at the CLOSED parry window.
            double now=UnityEngine.InputSystem.LowLevel.InputState.currentTime;
            FinisherPresentationElapsed=now-finisherStarted;
            float target=ValidatedSourceTime(FinisherSourceAt(now,finisherStarted,finisherOpened,finisherClosed));
            animator.Update(Mathf.Max(0,target-sourceTime)); sourceTime=target;
            // FixedUpdate owns closing the window. Retain the cue until its
            // close/cancellation event, even if rendering passes the deadline.
            finisherCue.enabled=sourceTime>=FinisherCueSource;
        }
        // Damage sampled the impact pose in FixedUpdate. Render that same pose
        // once before advancing B_2's recovery, preserving visible hit alignment.
        else if(tailRemaining>0 && finisherImpactFrame==Time.frameCount) { }
        else if(tailRemaining>0)
        {
            if(combat.IsAttacking || !GroundedForPresentation) ReturnToLocomotion();
            else
            {
                tailRemaining=Mathf.Max(0,tailRemaining-delta);
                FinisherPresentationElapsed=finisherClosed-finisherStarted+VisualTail-tailRemaining;
                float target=ValidatedSourceTime(Mathf.Lerp(ReferenceImpact(2),SourceLength,1-tailRemaining/VisualTail));
                animator.Update(Mathf.Max(0,target-sourceTime)); sourceTime=target;
                if(tailRemaining==0) ReturnToLocomotion();
            }
        }
        else if(GroundedForPresentation && combat.TryGetCombatVisualContext(out var ctx) && !ctx.IsFinisher &&
            (ctx.Command==QusapCombatCommand.WeaponLight || ctx.Command==QusapCombatCommand.WeaponStrong))
        {
            int index=ctx.Command==QusapCombatCommand.WeaponLight?0:1;
            if(clip!=index || execution!=ctx.AttackExecutionId) Begin(index,ctx.CapturedFacing,ctx.AttackExecutionId);
            float impact=ReferenceImpact(index), activeEnd=impact+(index==0?.08f:.10f);
            float target=ctx.Phase==QusapAttackPhase.Startup?Mathf.Lerp(0,impact,ctx.NormalizedProgress):
                ctx.Phase==QusapAttackPhase.Active?Mathf.Lerp(impact,activeEnd,ctx.NormalizedProgress):
                Mathf.Lerp(activeEnd,SourceLength,ctx.NormalizedProgress);
            target=Mathf.Max(sourceTime,ValidatedSourceTime(target)); animator.Update(Mathf.Max(0,target-sourceTime)); sourceTime=target;
        }
        else
        {
            if(clip>=0) { animator.Update(Mathf.Max(0,SourceLength-sourceTime)); ReturnToLocomotion(); }
            // WallSide is published by the real slide branch. Dash and the
            // wall-jump impulse may move away from a wall despite input into it.
            direction=dash.IsDashing?dashVisualDirection:
                wallJumpRemaining>0?wallJumpDirection:
                vertical.IsWallSliding&&vertical.WallSide!=0?vertical.WallSide:
                combat.FacingDirection<0?-1:1;
            string state=Locomotion(); PresentationState=state;
            if(!animator.GetCurrentAnimatorStateInfo(0).IsName(state) &&
                (!animator.IsInTransition(0)||!animator.GetNextAnimatorStateInfo(0).IsName(state)))
                animator.CrossFadeInFixedTime(state,dash.IsDashing?.035f:airLocomotion.State!=null?.06f:.12f,0);
            animator.Update(delta);
        }
        ApplyApprovedPose();
    }
    float AttackWeight()
    {
        var cur=animator.GetCurrentAnimatorStateInfo(0); float a=cur.IsName(LaunchFinisherState)||cur.IsName("BodyAttack")||cur.IsName("Headbutt")||Enumerable.Range(0,5).Any(i=>cur.IsName("FiveAttack_"+i))?1:0;
        if(!animator.IsInTransition(0)) return a;
        var next=animator.GetNextAnimatorStateInfo(0); float b=next.IsName(LaunchFinisherState)||next.IsName("BodyAttack")||next.IsName("Headbutt")||Enumerable.Range(0,5).Any(i=>next.IsName("FiveAttack_"+i))?1:0;
        return Mathf.Lerp(a,b,Mathf.Clamp01(animator.GetAnimatorTransitionInfo(0).normalizedTime));
    }
    void ApplyApprovedPose()
    {
        facing.SetPreviewFacing(direction); animator.SetBool("PurchasedLeftPose",false); facing.Apply();
        // The native inventory already owns disarm/drop/equip. Its result must
        // also retire the approved equipped visual, without changing the sword.
        foreach(var renderer in swordRenderers)renderer.enabled=equipment==null||equipment.HasWeapon;
        float weight=AttackWeight(); var rq=rightArm.Select(t=>t.localRotation).ToArray();
        carry?.SendMessage("LateUpdate",SendMessageOptions.DontRequireReceiver);
        if(direction<0 && weight>0) for(int j=0;j<4;j++)
        { var mapped=mapA[j].inverse*Matrix4x4.Rotate(rq[j])*mapB[j]; var q=Quaternion.LookRotation(mapped.GetColumn(2),mapped.GetColumn(1)); leftArm[j].localRotation=Quaternion.Slerp(leftArm[j].localRotation,q,weight); }
        foreach(var p in grip.poses)
        {
            var socket=all.Single(t=>t.name=="WeaponGripSocket_"+p.name);
            if(!socket.localPosition.Equals(p.socketLocalPosition)||!socket.localRotation.Equals(p.socketLocalRotation))
            { enabled=false; throw new InvalidOperationException("STOP: approved P1 socket changed."); }
            foreach(var bone in p.bones) all.Single(t=>t.name==bone.name).localRotation=bone.rotation;
        }
        clearance?.Evaluate(); if(clip>=0&&clip<5) correction?.Evaluate(clip,direction,sourceTime/SourceLength,weight);
        // These components are evaluated only above, once after Animator sampling.
        foreach(var component in visual.GetComponentsInChildren<MonoBehaviour>(true)) component.enabled=false;
    }
}
