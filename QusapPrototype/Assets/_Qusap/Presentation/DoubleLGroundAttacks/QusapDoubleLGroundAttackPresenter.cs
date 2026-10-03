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
    QusapDashMotor dash;
    QusapVerticalMotor vertical;
    QusapHitstunController hitstun;
    QusapInputReader input;
    QusapEquippedWeaponPresenter legacyWeaponPresentation;
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
        legacyWeaponPresentation=GetComponent<QusapEquippedWeaponPresenter>();
        wallJumpSequence=vertical.WallJumpSequence; wasGrounded=ground.IsGrounded;
        animator.applyRootMotion=false; animator.cullingMode=AnimatorCullingMode.AlwaysAnimate;
        // Explicit sampling lets the existing phase clock drive source time without
        // seeking/rebinding the skeleton on every frame. Physics advances normally.
        animator.enabled=false;
        all=visual.GetComponentsInChildren<Transform>(true);
        facing=visual.GetComponent<QusapSwordHandSwitchV5>();
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
    void OnEnable() { combat.FinisherArmed+=Armed; combat.CombatVisualExecutionEnded+=Ended; combat.AttackVariantStarted+=Started; combat.AttackPhaseChanged+=Phased; combat.FinisherParryWindowOpened+=WindowOpened; combat.FinisherReadyToResolve+=WindowClosed; }
    void OnDisable() { combat.FinisherArmed-=Armed; combat.CombatVisualExecutionEnded-=Ended; combat.AttackVariantStarted-=Started; combat.AttackPhaseChanged-=Phased; combat.FinisherParryWindowOpened-=WindowOpened; combat.FinisherReadyToResolve-=WindowClosed; if(finisherCue)finisherCue.enabled=false; }
    void OnDestroy() { if(cueSprite)Destroy(cueSprite); }
    void WindowOpened(QusapComboId combo,QusapHitReceiver target)
    { if(finisher) { SampleFinisher(Mathf.Max(sourceTime,FinisherOpenSource)); finisherCue.enabled=true; } }
    void WindowClosed(QusapComboId combo,QusapHitReceiver target)
    { if(finisher) { SampleFinisher(FinisherCloseSource); finisherCue.enabled=false; } }
    void SampleFinisher(float target)
    { animator.Update(Mathf.Max(0,target-sourceTime)); sourceTime=target; ApplyApprovedPose(); }
    void Started(QusapAttackVariant variant)
    {
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
        if(!GroundedForPresentation) return;
        finisher=true; finisherStarted=UnityEngine.InputSystem.LowLevel.InputState.currentTime;
        FinisherPresentationElapsed=0;
        finisherOpened=combat.ParryWindowOpensAt; finisherClosed=combat.ParryWindowClosesAt;
        Begin(2,combat.FacingDirection,combat.CurrentAttackExecutionId);
    }
    void Ended(QusapCombatVisualContext context)
    {
        if(context.AttackExecutionId!=execution || clip<0) return;
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
        clip=index; execution=id; direction=side<0?-1:1; sourceTime=0; tailRemaining=0; rollRemaining=0; landingRemaining=0; PresentationState="FiveAttack_"+index;
        facing.SetPreviewFacing(direction); animator.CrossFadeInFixedTime("FiveAttack_"+clip,.12f,0,0); animator.Update(0);
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
    { clip=-1; tailRemaining=0; finisher=false; finisherCue.enabled=false; animator.CrossFadeInFixedTime(Locomotion(),.12f,0); }
    void LateUpdate()
    {
        // Preserve the native drop/throw presentation reference's facing even
        // while its legacy socket is hidden behind the approved visible sword.
        legacyWeaponPresentation.ApplyFacing(combat.FacingDirection);
        float delta=Time.deltaTime; ObserveLocomotion(delta);
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
        var cur=animator.GetCurrentAnimatorStateInfo(0); float a=Enumerable.Range(0,5).Any(i=>cur.IsName("FiveAttack_"+i))?1:0;
        if(!animator.IsInTransition(0)) return a;
        var next=animator.GetNextAnimatorStateInfo(0); float b=Enumerable.Range(0,5).Any(i=>next.IsName("FiveAttack_"+i))?1:0;
        return Mathf.Lerp(a,b,Mathf.Clamp01(animator.GetAnimatorTransitionInfo(0).normalizedTime));
    }
    void ApplyApprovedPose()
    {
        facing.SetPreviewFacing(direction); animator.SetBool("PurchasedLeftPose",false); facing.Apply();
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
        clearance?.Evaluate(); if(clip>=0) correction?.Evaluate(clip,direction,sourceTime/SourceLength,weight);
        // These components are evaluated only above, once after Animator sampling.
        foreach(var component in visual.GetComponentsInChildren<MonoBehaviour>(true)) component.enabled=false;
    }
}
