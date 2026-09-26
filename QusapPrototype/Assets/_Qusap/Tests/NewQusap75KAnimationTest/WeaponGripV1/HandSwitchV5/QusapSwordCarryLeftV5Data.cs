using System;
using UnityEngine;

public sealed class QusapSwordCarryLeftV5Data : ScriptableObject
{
    [Serializable] public sealed class Frame{public Vector3[] positions;public Quaternion[] rotations;}
    [Serializable] public sealed class State{public string name;public Frame[] frames;}
    public State[] states;
    public void Evaluate(int state,float phase,int bone,out Vector3 p,out Quaternion q)
    {
        var frames=states[state].frames;float t=Mathf.Repeat(phase,1)*frames.Length;int i=Mathf.FloorToInt(t);var a=frames[i];var b=frames[(i+1)%frames.Length];
        p=Vector3.Lerp(a.positions[bone],b.positions[bone],t-i);q=Quaternion.Slerp(a.rotations[bone],b.rotations[bone],t-i);
    }
}
