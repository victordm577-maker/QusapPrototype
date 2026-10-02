using System;using UnityEngine;
[DefaultExecutionOrder(130)]
public sealed class QusapGripMotionClearance:MonoBehaviour {
 [Serializable]public class Correction{public string side,bone;public Quaternion base_rotation,target_rotation;public string[] states;}
 [Serializable]public class Data{public Correction[] corrections;}
 [SerializeField]Animator animator;[SerializeField]QusapSwordHandSwitchV5 facing;[SerializeField]Correction[] corrections;[SerializeField]Transform[] targets;
 public void Configure(Animator a,QusapSwordHandSwitchV5 f,Correction[] c,Transform[] t){animator=a;facing=f;corrections=c;targets=t;}
 public void Evaluate(){if(!animator||!facing||corrections==null)return;var current=animator.GetCurrentAnimatorStateInfo(0);bool transition=animator.IsInTransition(0);var next=animator.GetNextAnimatorStateInfo(0);float t=transition?Mathf.Clamp01(animator.GetAnimatorTransitionInfo(0).normalizedTime):0;
  for(int i=0;i<corrections.Length;i++){var c=corrections[i];bool active=(c.side=="R")== (facing.FacingDirection>0);float a=0,b=0;if(active)foreach(var state in c.states){if(current.IsName(state))a=1;if(transition&&next.IsName(state))b=1;}float w=transition?Mathf.Lerp(a,b,t):a;targets[i].localRotation=w==0?c.base_rotation:w==1?c.target_rotation:Quaternion.Slerp(c.base_rotation,c.target_rotation,w);}
 }
 void LateUpdate()=>Evaluate();
}
