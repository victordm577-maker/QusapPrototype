using UnityEngine;

// Sparse pose evaluation for the four left-arm transforms only. Uses the same
// base normalized time as the synchronized Animator layer. Humanoid retargeting
// continues to own the whole body, including the complete Roll.
[DefaultExecutionOrder(120)]
public sealed class QusapSwordCarryLeftPoseV5 : MonoBehaviour
{
    [SerializeField] private Animator animator;
    [SerializeField] private QusapSwordHandSwitchV5 facing;
    [SerializeField] private Transform[] bones;
    [SerializeField] private QusapSwordCarryLeftV5Data poseData;
    private readonly string[] states={"CombatIdle_B1","WalkForward","WalkBackward","RunForward","RunBackward"};
    private Vector3[] baselineP,currentP,nextP;
    private Quaternion[] baselineQ,currentQ,nextQ;
    public void Configure(Animator a,QusapSwordHandSwitchV5 f,Transform[] b,QusapSwordCarryLeftV5Data data){animator=a;facing=f;bones=b;poseData=data;Initialize();}
    private void Awake(){Initialize();}
    private void Initialize(){if(bones==null)return;baselineP=new Vector3[bones.Length];currentP=new Vector3[bones.Length];nextP=new Vector3[bones.Length];baselineQ=new Quaternion[bones.Length];currentQ=new Quaternion[bones.Length];nextQ=new Quaternion[bones.Length];}
    private int ClipIndex(AnimatorStateInfo s){for(int i=0;i<states.Length;i++)if(s.IsName(states[i]))return i;return -1;}
    private void Read(Vector3[] p,Quaternion[] q){for(int i=0;i<bones.Length;i++){p[i]=bones[i].localPosition;q[i]=bones[i].localRotation;}}
    private void Sample(int index,AnimatorStateInfo state,Vector3[] p,Quaternion[] q){for(int i=0;i<bones.Length;i++)poseData.Evaluate(index,state.normalizedTime,i,out p[i],out q[i]);}
    private void LateUpdate()
    {
        if(!animator||!facing||facing.FacingDirection>0)return;
        int layer=animator.GetLayerIndex("SwordCarryUpperBody_L");float weight=animator.GetLayerWeight(layer);if(weight<=0)return;
        var current=animator.GetCurrentAnimatorStateInfo(0);int a=ClipIndex(current);
        var next=animator.GetNextAnimatorStateInfo(0);int b=animator.IsInTransition(0)?ClipIndex(next):-1;
        if(a<0&&b<0)return;
        Read(baselineP,baselineQ);
        if(a>=0)Sample(a,current,currentP,currentQ);
        if(b>=0)Sample(b,next,nextP,nextQ);
        float blend=a>=0&&b>=0?Mathf.Clamp01(animator.GetAnimatorTransitionInfo(0).normalizedTime):b>=0?1:0;
        for(int i=0;i<bones.Length;i++)
        {
            var targetP=a<0?nextP[i]:b<0?currentP[i]:Vector3.Lerp(currentP[i],nextP[i],blend);
            var targetQ=a<0?nextQ[i]:b<0?currentQ[i]:Quaternion.Slerp(currentQ[i],nextQ[i],blend);
            bones[i].SetLocalPositionAndRotation(Vector3.Lerp(baselineP[i],targetP,weight),Quaternion.Slerp(baselineQ[i],targetQ,weight));
        }
    }
}
