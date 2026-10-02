using System;using UnityEngine;
public sealed class QusapFiveAttackCorrection:MonoBehaviour {
 [Serializable]public class Key{public float phase;public Quaternion rotation;}
 [Serializable]public class BoneCurve{public string name;public Key[] keys;}
 [Serializable]public class ClipCorrection{public int clip,direction;public BoneCurve[] bones;}
 [Serializable]public class Data{public ClipCorrection[] corrections;}
 [SerializeField]ClipCorrection[] corrections;[SerializeField]Transform[] targets;
 public void Configure(ClipCorrection[] c,Transform[] t){corrections=c;targets=t;}
 public void Evaluate(int clip,int direction,float phase,float weight){if(corrections==null||weight==0)return;foreach(var c in corrections){if(c.clip!=clip||c.direction!=direction)continue;foreach(var b in c.bones){var target=Array.Find(targets,t=>t.name==b.name);if(!target||b.keys.Length==0)continue;var a=b.keys[0];var z=b.keys[b.keys.Length-1];for(int i=0;i<b.keys.Length-1;i++)if(phase>=b.keys[i].phase&&phase<=b.keys[i+1].phase){a=b.keys[i];z=b.keys[i+1];break;}var q=Quaternion.Slerp(a.rotation,z.rotation,a.phase==z.phase?0:Mathf.Clamp01((phase-a.phase)/(z.phase-a.phase)));target.localRotation=target.localRotation*Quaternion.Slerp(Quaternion.identity,q,weight);}}}
}
