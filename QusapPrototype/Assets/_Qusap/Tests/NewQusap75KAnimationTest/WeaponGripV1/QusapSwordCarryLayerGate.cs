using UnityEngine;

// A presentation-only allowlist: every other full-body state owns its arm, including Roll.
[DefaultExecutionOrder(100)]
[RequireComponent(typeof(Animator))]
public sealed class QusapSwordCarryLayerGate : MonoBehaviour
{
    [SerializeField] private bool swordEquipped = true;
    [SerializeField] private bool fullBodyOwnsRightArm;
    private Animator animator;
    private int carryLayer;
    private Transform swordVisual;
    public bool SwordEquipped { get=>swordEquipped; set=>swordEquipped=value; }
    public bool FullBodyOwnsRightArm { get=>fullBodyOwnsRightArm; set=>fullBodyOwnsRightArm=value; }
    private void Awake()
    {
        animator=GetComponent<Animator>();carryLayer=animator.GetLayerIndex("SwordCarryUpperBody");
        var hand=animator.GetBoneTransform(HumanBodyBones.RightHand);
        swordVisual=hand?hand.Find("WeaponGripSocket/SwordVisual"):null;
    }
    private static bool Locomotion(AnimatorStateInfo state)=>state.IsName("CombatIdle_B1")||state.IsName("WalkForward")||state.IsName("WalkBackward")||state.IsName("RunForward")||state.IsName("RunBackward");
    private void Update()
    {
        if(carryLayer<0)return;
        var current=animator.GetCurrentAnimatorStateInfo(0);
        // Crossfade weight follows the incoming locomotion. Roll weight reaches exactly zero
        // when the base transition finishes; no carry pose survives inside the full Roll.
        float weight=Locomotion(current)?1:0;
        if(animator.IsInTransition(0))
        {
            var next=animator.GetNextAnimatorStateInfo(0);
            weight=Mathf.Lerp(weight,Locomotion(next)?1:0,Mathf.Clamp01(animator.GetAnimatorTransitionInfo(0).normalizedTime));
        }
        animator.SetLayerWeight(carryLayer,swordEquipped&&!fullBodyOwnsRightArm&&swordVisual&&swordVisual.gameObject.activeInHierarchy?weight:0);
    }
    private void OnDisable(){if(animator&&carryLayer>=0)animator.SetLayerWeight(carryLayer,0);}
}
