using UnityEngine;

// A clip configuration adapter for the isolated test. No negative scale, bone
// edits, socket offsets, grip changes or replacement of the equipped weapon.
[DefaultExecutionOrder(115)]
public sealed class QusapPurchasedAnimationDirection : MonoBehaviour
{
    [SerializeField] private Animator animator;
    [SerializeField] private QusapSwordHandSwitchV5 facing;
    public void Configure(Animator a,QusapSwordHandSwitchV5 f){animator=a;facing=f;}
    private void Update(){if(animator&&facing)animator.SetBool("PurchasedLeftPose",facing.FacingDirection<0);}
    public void SetDirection(int direction){facing.SetPreviewFacing(direction);animator.SetBool("PurchasedLeftPose",direction<0);}
}
