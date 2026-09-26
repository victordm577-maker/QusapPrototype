using System.Linq;
using UnityEngine;
using Qusap;

// Isolated presentation only. The equipped object reference and positive scale never change.
[DefaultExecutionOrder(110)]
public sealed class QusapSwordHandSwitchV5 : MonoBehaviour
{
    [SerializeField] private QusapCombatController facingSource;
    [SerializeField] private Animator visualAnimator;
    [SerializeField] private Transform facingBasis,rightSocket,leftSocket,swordVisual;
    [SerializeField] private bool followCombatFacing=true;
    [SerializeField] private int direction=1;
    private QusapWeaponGripPosePlayer rightGrip,leftGrip;
    private QusapSwordCarryLayerGate equipmentGate;
    private int rightCarry,leftCarry,rightLayer,leftLayer,lastDirection;
    private Transform[] rightClaws,leftClaws;
    private Quaternion[] rightRest,leftRest;
    public int FacingDirection=>direction;
    public Transform SwordVisual=>swordVisual;
    public Transform FacingBasis=>facingBasis;
    public void Configure(QusapCombatController source,Animator animator,Transform basis,Transform r,Transform l,Transform sword){facingSource=source;visualAnimator=animator;facingBasis=basis;rightSocket=r;leftSocket=l;swordVisual=sword;Initialize();Apply();}
    private void Awake(){Initialize();}
    private void Initialize()
    {
        if(!visualAnimator)return;
        equipmentGate=visualAnimator.GetComponent<QusapSwordCarryLayerGate>();
        rightCarry=visualAnimator.GetLayerIndex("SwordCarryUpperBody");leftCarry=visualAnimator.GetLayerIndex("SwordCarryUpperBody_L");
        rightLayer=visualAnimator.GetLayerIndex("SwordGrip");leftLayer=visualAnimator.GetLayerIndex("SwordGrip_L");
        rightGrip=rightSocket.parent.GetComponent<QusapWeaponGripPosePlayer>();leftGrip=leftSocket.parent.GetComponent<QusapWeaponGripPosePlayer>();
        rightClaws=rightSocket.parent.GetComponentsInChildren<Transform>().Where(t=>t.name.StartsWith("claw_r_")).ToArray();leftClaws=leftSocket.parent.GetComponentsInChildren<Transform>().Where(t=>t.name.StartsWith("claw_l_")).ToArray();
        rightRest=rightClaws.Select(t=>t.localRotation).ToArray();leftRest=leftClaws.Select(t=>t.localRotation).ToArray();
    }
    public void SetPreviewFacing(int value){followCombatFacing=false;direction=value<0?-1:1;Apply();}
    private void Update(){if(followCombatFacing&&facingSource)direction=facingSource.FacingDirection<0?-1:1;Apply();}
    private static bool Locomotion(AnimatorStateInfo s)=>s.IsName("CombatIdle_B1")||s.IsName("WalkForward")||s.IsName("WalkBackward")||s.IsName("RunForward")||s.IsName("RunBackward");
    public void Apply()
    {
        if(!visualAnimator||!facingBasis||!swordVisual)return;
        facingBasis.localRotation=Quaternion.Euler(0,direction>0?60:-60,0);
        var target=direction>0?rightSocket:leftSocket;
        // SetParent directly to the destination in the same update; no root parent,
        // destruction, duplicate, activation toggle, or inventory-reference replacement.
        if(swordVisual.parent!=target){swordVisual.SetParent(target,false);swordVisual.SetLocalPositionAndRotation(Vector3.zero,Quaternion.identity);swordVisual.localScale=Vector3.one;}
        rightGrip.enabled=direction>0;leftGrip.enabled=direction<0;
        if(lastDirection!=direction){var bones=direction>0?leftClaws:rightClaws;var rest=direction>0?leftRest:rightRest;for(int i=0;i<bones.Length;i++)bones[i].localRotation=rest[i];}
        lastDirection=direction;
        if(rightLayer>=0)visualAnimator.SetLayerWeight(rightLayer,direction>0?1:0);
        if(leftLayer>=0)visualAnimator.SetLayerWeight(leftLayer,direction<0?1:0);
        float weight=Locomotion(visualAnimator.GetCurrentAnimatorStateInfo(0))?1:0;
        if(visualAnimator.IsInTransition(0))weight=Mathf.Lerp(weight,Locomotion(visualAnimator.GetNextAnimatorStateInfo(0))?1:0,Mathf.Clamp01(visualAnimator.GetAnimatorTransitionInfo(0).normalizedTime));
        if(equipmentGate&&(!equipmentGate.SwordEquipped||equipmentGate.FullBodyOwnsRightArm))weight=0;
        if(rightCarry>=0)visualAnimator.SetLayerWeight(rightCarry,direction>0?weight:0);
        if(leftCarry>=0)visualAnimator.SetLayerWeight(leftCarry,direction<0?weight:0);
    }
}
