using System;
using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;

// Author only the left arm. Sampling the exact base phase compensates locomotion
// torso asymmetry without changing the approved right carry or either socket.
public static class QusapHandSwitchV5Author
{
    public static void AuthorLeftPose(Animator animator)
    {
        var facing=animator.GetComponentInParent<QusapSwordHandSwitchV5>();
        var right=animator.GetBoneTransform(HumanBodyBones.RightHand).Find("WeaponGripSocket_R");
        var left=animator.GetBoneTransform(HumanBodyBones.LeftHand).Find("WeaponGripSocket_L");
        var mesh=facing.SwordVisual.GetComponentInChildren<MeshFilter>();
        var tipLocal=mesh.sharedMesh.vertices.OrderByDescending(v=>v.y).First();
        var carry=AssetDatabase.LoadAssetAtPath<AnimationClip>(QusapHandSwitchV5Builder.Root+"/Animation/SwordCarryUpperBody_L.anim");
        var bindings=AnimationUtility.GetCurveBindings(carry);var muscleIndices=bindings.Select(b=>Array.IndexOf(HumanTrait.MuscleName,b.propertyName)).ToArray();
        if(muscleIndices.Any(i=>i<0))throw new InvalidOperationException("Missing muscle mapping");
        var initial=bindings.Select(b=>AnimationUtility.GetEditorCurve(carry,b).Evaluate(0)).ToArray();
        var controller=(AnimatorController)animator.runtimeAnimatorController;
        int layer=animator.GetLayerIndex("SwordCarryUpperBody_L");
        var overrides=new AnimatorOverrideController(controller);
        var temporary=new AnimationClip{name="V5AuthorLeftMusclesOnly",frameRate=60};
        var mask=AssetDatabase.LoadAssetAtPath<AvatarMask>(QusapHandSwitchV5Builder.Root+"/Animation/SwordCarryUpperBody_L.mask");
        mask.SetHumanoidBodyPartActive(AvatarMaskBodyPart.LeftArm,true);
        var report=new StringBuilder("state,phase,loss,tip_height_delta_m,guard_delta_m,blade_angle_delta_deg\n");
        var clips=new System.Collections.Generic.Dictionary<string,AnimationClip>();
        var arm= new[]{"clavicle_l","upperarm_l","lowerarm_l","hand_l"}.Select(n=>animator.GetComponentsInChildren<Transform>(true).Single(t=>t.name==n)).ToArray();
        Vector3 Reflect(Vector3 p)=>new Vector3(2*facing.transform.position.x-p.x,p.y,p.z);
        const int Samples=60;
        foreach(var name in new[]{"CombatIdle_B1","WalkForward","WalkBackward","RunForward","RunBackward"})
        {
            var source=controller.layers[0].stateMachine.states.Single(s=>s.state.name==name).state.motion as AnimationClip;
            var state=controller.layers[0].stateMachine.states.Single(s=>s.state.name==name).state;
            var overrideSource=controller.layers[layer].GetOverrideMotion(state) as AnimationClip;
            if(!overrideSource)overrideSource=carry;
            var clip=new AnimationClip{name="SwordCarryUpperBody_L_"+name,frameRate=60};
            var keys=bindings.Select(b=>new System.Collections.Generic.List<Keyframe>()).ToArray();
            var sparse=arm.Select(t=>new System.Collections.Generic.List<(float time,Vector3 p,Quaternion q)>()).ToArray();
            var values=(float[])initial.Clone();float[] first=null;
            for(int frame=0;frame<Samples;frame++)
            {
                float phase=frame/(float)Samples;
                animator.runtimeAnimatorController=controller;animator.Rebind();facing.SetPreviewFacing(1);animator.SetLayerWeight(layer,0);animator.Play(name,0,phase);animator.Update(.000001f);
                var guard=Reflect(right.position);var tip=Reflect(mesh.transform.TransformPoint(tipLocal));var blade=tip-guard;
                float Loss()
                {
                    for(int i=0;i<values.Length;i++)AnimationUtility.SetEditorCurve(temporary,bindings[i],AnimationCurve.Constant(0,source.length,values[i]));
                    overrides[overrideSource]=null;overrides[overrideSource]=temporary;
                    animator.runtimeAnimatorController=overrides;animator.Rebind();facing.SetPreviewFacing(-1);animator.SetLayerWeight(layer,1);animator.Play(name,0,phase);animator.Update(.000001f);
                    var actualTip=mesh.transform.TransformPoint(tipLocal);var actualBlade=actualTip-left.position;
                    var screenActual=new Vector2(Mathf.Abs(actualBlade.x),actualBlade.y).normalized;var screenTarget=new Vector2(Mathf.Abs(blade.x),blade.y).normalized;
                    return (left.position-guard).sqrMagnitude*15+(actualBlade.normalized-blade.normalized).sqrMagnitude*3+(screenActual-screenTarget).sqrMagnitude*25+Mathf.Pow(actualTip.y-tip.y,2)*200+(actualTip-tip).sqrMagnitude*5;
                }
                // Continuous solution seeded from the previous phase. Refine all nine
                // shoulder, elbow and wrist muscles, keeping the anatomical socket fixed.
                for(float step=.16f;step>.00025f;step*=.5f)
                for(int sweep=0;sweep<6;sweep++)for(int i=0;i<values.Length;i++)
                {
                    float start=values[i],choice=start,best=Loss();
                    foreach(float sign in new[]{-1f,1f}){values[i]=Mathf.Clamp(start+sign*step,-1,1);float trial=Loss();if(trial<best){best=trial;choice=values[i];}}
                    values[i]=choice;
                }
                float loss=Loss();var finalTip=mesh.transform.TransformPoint(tipLocal);var finalBlade=finalTip-left.position;
                float angle=Mathf.Atan2(finalBlade.y,Mathf.Abs(finalBlade.x))*Mathf.Rad2Deg-Mathf.Atan2(blade.y,Mathf.Abs(blade.x))*Mathf.Rad2Deg;
                report.AppendLine($"{name},{phase:F7},{loss:F9},{Mathf.Abs(finalTip.y-tip.y):F9},{Vector3.Distance(left.position,guard):F9},{angle:F6}");
                for(int i=0;i<values.Length;i++)keys[i].Add(new Keyframe(phase*source.length,values[i]));
                for(int i=0;i<arm.Length;i++)sparse[i].Add((phase*source.length,arm[i].localPosition,arm[i].localRotation));
                if(frame==0)first=(float[])values.Clone();
            }
            for(int i=0;i<arm.Length;i++)
            {
                sparse[i].Add((source.length,sparse[i][0].p,sparse[i][0].q));string bonePath=AnimationUtility.CalculateTransformPath(arm[i],animator.transform);
                for(int axis=0;axis<7;axis++)
                {
                    int component=axis%4;
                    var curve=new AnimationCurve(sparse[i].Select(v=>new Keyframe(v.time,axis<4?v.q[component]:v.p[axis-4])).ToArray());
                    for(int k=0;k<curve.length;k++){AnimationUtility.SetKeyLeftTangentMode(curve,k,AnimationUtility.TangentMode.ClampedAuto);AnimationUtility.SetKeyRightTangentMode(curve,k,AnimationUtility.TangentMode.ClampedAuto);}
                    string property=axis<4?"m_LocalRotation."+"xyzw"[axis]:"m_LocalPosition."+"xyz"[axis-4];
                    AnimationUtility.SetEditorCurve(clip,EditorCurveBinding.FloatCurve(bonePath,typeof(Transform),property),curve);
                }
            }
            clip.EnsureQuaternionContinuity();
            var settings=AnimationUtility.GetAnimationClipSettings(clip);settings.loopTime=true;settings.loopBlend=true;AnimationUtility.SetAnimationClipSettings(clip,settings);
            string path=QusapHandSwitchV5Builder.Root+"/Animation/"+clip.name+".anim";
            QusapHandSwitchV5Builder.Save(clip,path);clips[name]=AssetDatabase.LoadAssetAtPath<AnimationClip>(path);
            Debug.Log("V5 authored left carry "+name);
        }
        var layers=controller.layers;layers[layer].syncedLayerIndex=0;layers[layer].syncedLayerAffectsTiming=false;
        foreach(var state in layers[0].stateMachine.states)if(clips.TryGetValue(state.state.name,out var clip))layers[layer].SetOverrideMotion(state.state,clip);
        controller.layers=layers;EditorUtility.SetDirty(controller);AssetDatabase.SaveAssets();
        for(int i=0;i<(int)AvatarMaskBodyPart.LastBodyPart;i++)mask.SetHumanoidBodyPartActive((AvatarMaskBodyPart)i,false);EditorUtility.SetDirty(mask);
        var player=PrefabUtility.LoadPrefabContents(QusapHandSwitchV5Builder.PrefabPath);
        try
        {
            var a=player.GetComponentsInChildren<Animator>(true).First(t=>t.name=="Qusap75K_Visual");
            var presenter=player.GetComponentInChildren<QusapSwordHandSwitchV5>(true);
            var sampler=a.GetComponent<QusapSwordCarryLeftPoseV5>();if(!sampler)sampler=a.gameObject.AddComponent<QusapSwordCarryLeftPoseV5>();
            sampler.Configure(a,presenter,new[]{"clavicle_l","upperarm_l","lowerarm_l","hand_l"}.Select(n=>a.GetComponentsInChildren<Transform>(true).Single(t=>t.name==n)).ToArray(),BakeData(a));
            PrefabUtility.SaveAsPrefabAsset(player,QusapHandSwitchV5Builder.PrefabPath);
        }
        finally{PrefabUtility.UnloadPrefabContents(player);}
        AssetDatabase.SaveAssets();
        File.WriteAllText(Path.Combine(QusapHandSwitchV5Builder.Output,"left_carry_authoring.csv"),report.ToString());
        File.WriteAllText(Path.Combine(QusapHandSwitchV5Builder.Output,"left_carry_authoring.txt"),"Left arm clips synchronized to base locomotion normalized time. Nine left-arm muscles only. Five locomotion states authored at 60 equivalent phases per loop. Right carry, right grip, both sockets and Roll untouched. Left carry has weight zero throughout full Roll. No scale reflection.\n");
        animator.runtimeAnimatorController=controller;UnityEngine.Object.DestroyImmediate(overrides);UnityEngine.Object.DestroyImmediate(temporary);animator.Rebind();facing.SetPreviewFacing(-1);animator.Play("CombatIdle_B1",0,.5f);animator.Update(0);
    }
    public static QusapSwordCarryLeftV5Data BakeData(Animator animator)
    {
        var names=new[]{"CombatIdle_B1","WalkForward","WalkBackward","RunForward","RunBackward"};
        var bones=new[]{"clavicle_l","upperarm_l","lowerarm_l","hand_l"}.Select(n=>animator.GetComponentsInChildren<Transform>(true).Single(t=>t.name==n)).ToArray();
        var data=ScriptableObject.CreateInstance<QusapSwordCarryLeftV5Data>();data.name="SwordCarryUpperBody_L_PoseData";data.states=new QusapSwordCarryLeftV5Data.State[names.Length];
        for(int s=0;s<names.Length;s++)
        {
            var clip=AssetDatabase.LoadAssetAtPath<AnimationClip>(QusapHandSwitchV5Builder.Root+"/Animation/SwordCarryUpperBody_L_"+names[s]+".anim");
            var frames=new QusapSwordCarryLeftV5Data.Frame[60];
            for(int frame=0;frame<60;frame++)
            {
                frames[frame]=new QusapSwordCarryLeftV5Data.Frame{positions=new Vector3[4],rotations=new Quaternion[4]};
                for(int i=0;i<4;i++)
                {
                    string path=AnimationUtility.CalculateTransformPath(bones[i],animator.transform);float time=clip.length*frame/60;
                    float V(string property)=>AnimationUtility.GetEditorCurve(clip,EditorCurveBinding.FloatCurve(path,typeof(Transform),property)).Evaluate(time);
                    frames[frame].positions[i]=new Vector3(V("m_LocalPosition.x"),V("m_LocalPosition.y"),V("m_LocalPosition.z"));
                    frames[frame].rotations[i]=new Quaternion(V("m_LocalRotation.x"),V("m_LocalRotation.y"),V("m_LocalRotation.z"),V("m_LocalRotation.w")).normalized;
                }
            }
            data.states[s]=new QusapSwordCarryLeftV5Data.State{name=names[s],frames=frames};
        }
        string asset=QusapHandSwitchV5Builder.Root+"/Animation/SwordCarryUpperBody_L_PoseData.asset";QusapHandSwitchV5Builder.Save(data,asset);return AssetDatabase.LoadAssetAtPath<QusapSwordCarryLeftV5Data>(asset);
    }
    public static void BakePoseData()
    {
        var player=PrefabUtility.LoadPrefabContents(QusapHandSwitchV5Builder.PrefabPath);
        try{var a=player.GetComponentsInChildren<Animator>(true).First(t=>t.name=="Qusap75K_Visual");var sampler=a.GetComponent<QusapSwordCarryLeftPoseV5>();sampler.Configure(a,player.GetComponentInChildren<QusapSwordHandSwitchV5>(true),new[]{"clavicle_l","upperarm_l","lowerarm_l","hand_l"}.Select(n=>a.GetComponentsInChildren<Transform>(true).Single(t=>t.name==n)).ToArray(),BakeData(a));PrefabUtility.SaveAsPrefabAsset(player,QusapHandSwitchV5Builder.PrefabPath);AssetDatabase.SaveAssets();}
        finally{PrefabUtility.UnloadPrefabContents(player);}
    }
}
