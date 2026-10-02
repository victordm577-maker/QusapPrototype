using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEditor.Animations;
using UnityEngine;

public static class QusapSwordCarryBuilder
{
    public const string Root = "Assets/_Qusap/Tests/NewQusap75KAnimationTest/WeaponGripV1";
    public const string Scene = Root + "/Scene/Qusap75K_WeaponGripV1_Test.unity";
    public static void Build()
    {
        var prefab=PrefabUtility.LoadPrefabContents(Root+"/Prefab/Qusap75K_WeaponGripV1_Test.prefab");
        try
        {
            var animator=prefab.GetComponentsInChildren<Animator>(true).First(a=>a.name=="Qusap75K_Visual");
            var controller=(AnimatorController)animator.runtimeAnimatorController;
            var preserved=animator.GetComponentsInChildren<Transform>(true).Select(t=>(t,t.localPosition,t.localRotation,t.localScale)).ToArray();
            animator.Rebind();if(animator.GetLayerIndex("SwordCarryUpperBody")>=0)animator.SetLayerWeight(animator.GetLayerIndex("SwordCarryUpperBody"),0);
            animator.Play("CombatIdle_B1",0,.5f);animator.Update(0);
            var arm=animator.GetBoneTransform(HumanBodyBones.RightUpperArm);
            var forearm=animator.GetBoneTransform(HumanBodyBones.RightLowerArm);
            var hand=animator.GetBoneTransform(HumanBodyBones.RightHand);
            var shoulder=animator.GetBoneTransform(HumanBodyBones.RightShoulder);
            var socket=hand.Find("WeaponGripSocket");
            var clip=AssetDatabase.LoadAssetAtPath<AnimationClip>(Root+"/Animation/Qusap75K_SwordCarryUpperBody.anim");
            if(!clip)
            {
                clip=new AnimationClip{name="Qusap75K_SwordCarryUpperBody",frameRate=30};
                foreach(var name in HumanTrait.MuscleName.Where(n=>n.StartsWith("Right Shoulder")||n.StartsWith("Right Arm")||n.StartsWith("Right Forearm")||n.StartsWith("Right Hand")))
                    AnimationUtility.SetEditorCurve(clip,EditorCurveBinding.FloatCurve("",typeof(Animator),name),AnimationCurve.Constant(0,1,0));
                AssetDatabase.CreateAsset(clip,Root+"/Animation/Qusap75K_SwordCarryUpperBody.anim");
            }
            var mask=Mask(animator,new[]{shoulder,arm,forearm,hand},AvatarMaskBodyPart.RightArm);
            Save(mask,Root+"/Animation/SwordCarryUpperBody.mask");Layer(controller,"SwordCarryUpperBody",clip,mask,1);
            // Preserve the six existing quaternion curves exactly; only rebase their paths for an Animator layer.
            var existing=AssetDatabase.LoadAssetAtPath<AnimationClip>(Root+"/Animation/Qusap75K_SwordGrip.anim");
            var grip=new AnimationClip{name="Qusap75K_SwordGripLayer",frameRate=30};
            string handPath=AnimationUtility.CalculateTransformPath(hand,animator.transform);
            foreach(var binding in AnimationUtility.GetCurveBindings(existing))
            {
                var rebased=binding;rebased.path=handPath+"/"+binding.path;
                AnimationUtility.SetEditorCurve(grip,rebased,AnimationUtility.GetEditorCurve(existing,binding));
            }
            Save(grip,Root+"/Animation/Qusap75K_SwordGripLayer.anim");
            var claws=hand.GetComponentsInChildren<Transform>(true).Where(t=>t.name.StartsWith("claw_r_")).ToArray();
            var gripMask=Mask(animator,claws,null);Save(gripMask,Root+"/Animation/SwordGrip.mask");Layer(controller,"SwordGrip",grip,gripMask,1);
            // Keep the approved six-bone sampler. Unity's masked generic curves on a Humanoid
            // layer can omit custom distal joints; this preserves the exact approved grip.
            var gripPlayer=hand.GetComponent<QusapWeaponGripPosePlayer>();
            if(!gripPlayer)gripPlayer=hand.gameObject.AddComponent<QusapWeaponGripPosePlayer>();
            gripPlayer.GripClip=existing;
            if(!animator.GetComponent<QusapSwordCarryLayerGate>())animator.gameObject.AddComponent<QusapSwordCarryLayerGate>();
            foreach(var p in preserved){p.t.localPosition=p.localPosition;p.t.localRotation=p.localRotation;p.t.localScale=p.localScale;}
            PrefabUtility.SaveAsPrefabAsset(prefab,Root+"/Prefab/Qusap75K_WeaponGripV1_Test.prefab");
            EditorUtility.SetDirty(controller);AssetDatabase.SaveAssets();
            var audit=new System.Text.StringBuilder();
            audit.AppendLine("applyRootMotion="+animator.applyRootMotion);
            audit.AppendLine("WeaponGripSocket localPosition="+socket.localPosition.ToString("F7")+" localRotation="+socket.localRotation.ToString("F7"));
            audit.AppendLine("SwordVisual localPosition="+socket.Find("SwordVisual").localPosition.ToString("F7"));
            foreach(var layer in controller.layers)
            {
                audit.AppendLine("LAYER "+layer.name);
                if(layer.avatarMask)for(int i=0;i<layer.avatarMask.transformCount;i++)if(layer.avatarMask.GetTransformActive(i))audit.AppendLine("ACTIVE "+layer.avatarMask.GetTransformPath(i));
                foreach(var state in layer.stateMachine.states)if(state.state.motion is AnimationClip stateClip)
                {
                    audit.AppendLine("STATE "+state.state.name+" | "+stateClip.name+" | "+AssetDatabase.GetAssetPath(stateClip));
                    if(layer.name!="Base Layer")foreach(var binding in AnimationUtility.GetCurveBindings(stateClip))audit.AppendLine("CURVE "+binding.path+" | "+binding.propertyName);
                }
            }
            File.WriteAllText(Path.Combine(QusapSwordCarryValidation.Output,"Animator_audit.txt"),audit.ToString());

        }
        finally{PrefabUtility.UnloadPrefabContents(prefab);}
    }
    private static void Save(Object asset,string path)
    {
        var old=AssetDatabase.LoadMainAssetAtPath(path);
        if(old){EditorUtility.CopySerialized(asset,old);Object.DestroyImmediate(asset);}
        else AssetDatabase.CreateAsset(asset,path);
    }
    private static AvatarMask Mask(Animator animator,Transform[] bones,AvatarMaskBodyPart? human)
    {
        var mask=new AvatarMask();
        for(int i=0;i<(int)AvatarMaskBodyPart.LastBodyPart;i++)mask.SetHumanoidBodyPartActive((AvatarMaskBodyPart)i,human.HasValue&&(AvatarMaskBodyPart)i==human.Value);
        var hierarchy=animator.GetComponentsInChildren<Transform>(true);
        mask.transformCount=hierarchy.Length;
        for(int i=0;i<hierarchy.Length;i++)mask.SetTransformPath(i,AnimationUtility.CalculateTransformPath(hierarchy[i],animator.transform));
        var paths=bones.Select(b=>AnimationUtility.CalculateTransformPath(b,animator.transform)).ToArray();
        for(int i=0;i<mask.transformCount;i++)mask.SetTransformActive(i,paths.Contains(mask.GetTransformPath(i)));
        return mask;
    }
    private static void Layer(AnimatorController controller,string name,AnimationClip clip,AvatarMask mask,float weight)
    {
        var layers=controller.layers;int index=System.Array.FindIndex(layers,l=>l.name==name);
        if(index<0){controller.AddLayer(name);layers=controller.layers;index=layers.Length-1;}
        // Load saved objects, because an existing asset was updated in place.
        layers[index].avatarMask=AssetDatabase.LoadAssetAtPath<AvatarMask>(Root+"/Animation/"+name+".mask");
        layers[index].defaultWeight=weight;layers[index].blendingMode=AnimatorLayerBlendingMode.Override;
        var machine=layers[index].stateMachine;
        var state=machine.states.FirstOrDefault().state;
        if(!state)state=machine.AddState(name);
        state.writeDefaultValues=false;state.motion=AssetDatabase.LoadAssetAtPath<AnimationClip>(Root+"/Animation/Qusap75K_"+(name=="SwordGrip"?"SwordGripLayer":name)+".anim");machine.defaultState=state;
        controller.layers=layers;
    }
    public static void Validate()
    {
        EditorSceneManager.OpenScene(Scene);
        foreach(var old in Object.FindObjectsByType<QusapWeaponGripV1RuntimeValidation>())Object.DestroyImmediate(old.gameObject);
        var validation=new GameObject("SwordCarryValidation").AddComponent<QusapSwordCarryValidation>();validation.final=true;
        EditorApplication.EnterPlaymode();
    }
    public static void Preview()
    {
        EditorSceneManager.OpenScene(Scene);
        foreach(var old in Object.FindObjectsByType<QusapWeaponGripV1RuntimeValidation>())Object.DestroyImmediate(old.gameObject);
        new GameObject("SwordCarryPreview").AddComponent<QusapSwordCarryValidation>().preview=true;
        EditorApplication.EnterPlaymode();
    }
    public static void Author()
    {
        EditorSceneManager.OpenScene(Scene);
        foreach(var old in Object.FindObjectsByType<QusapWeaponGripV1RuntimeValidation>())Object.DestroyImmediate(old.gameObject);
        new GameObject("SwordCarryAuthor").AddComponent<QusapSwordCarryValidation>().author=true;
        EditorApplication.EnterPlaymode();
    }
    public static void AuthorPose(Animator animator)
    {
        var carry=AssetDatabase.LoadAssetAtPath<AnimationClip>(Root+"/Animation/Qusap75K_SwordCarryUpperBody.anim");
        var baseClips=new[]{animator.runtimeAnimatorController.animationClips.First(c=>c.name=="1Hand_Base_Stand_Idle_B_1"),animator.runtimeAnimatorController.animationClips.First(c=>c.name=="1Hand_Base_Run_B_F_InPlace")};
        var bindings=AnimationUtility.GetCurveBindings(carry);
        var values=bindings.Select(b=>AnimationUtility.GetEditorCurve(carry,b).Evaluate(0)).ToArray();
        var originalController=animator.runtimeAnimatorController;
        var overrides=new AnimatorOverrideController(originalController);
        var hand=animator.GetBoneTransform(HumanBodyBones.RightHand);var hips=animator.GetBoneTransform(HumanBodyBones.Hips);var socket=hand.Find("WeaponGripSocket");
        var filter=socket.GetComponentInChildren<MeshFilter>();var tip=filter.sharedMesh.vertices.OrderByDescending(v=>v.y).First();
        var desiredBlade=(animator.transform.forward*.681998f+Vector3.up*.731354f).normalized;
        var temporary=new AnimationClip{name="AuthoringPose",frameRate=30};
        float Loss()
        {
            for(int i=0;i<values.Length;i++)AnimationUtility.SetEditorCurve(temporary,bindings[i],AnimationCurve.Constant(0,1,values[i]));
            overrides[carry]=null;overrides[carry]=temporary;animator.runtimeAnimatorController=overrides;animator.Rebind();
            animator.SetLayerWeight(animator.GetLayerIndex("SwordCarryUpperBody"),1);animator.SetLayerWeight(animator.GetLayerIndex("SwordGrip"),1);
            float loss=0;
            foreach(var clip in baseClips)
            {
                animator.Play(clip==baseClips[0]?"CombatIdle_B1":"RunForward",0,.5f);animator.Update(0);
                var actualBlade=(filter.transform.TransformPoint(tip)-socket.position).normalized;
                var target=hips.position+animator.transform.right*.22f+animator.transform.forward*.70f-Vector3.up*.10f;
                var normal=filter.transform.TransformDirection(Vector3.forward).normalized;
                float plane=Vector3.Dot(normal,animator.transform.right);
                loss+=(socket.position-target).sqrMagnitude*10f+(actualBlade-desiredBlade).sqrMagnitude*1.2f+(1-plane*plane)*.35f;
            }
            return loss+values.Sum(v=>v*v)*.002f;
        }
        try
        {
            float bestLoss=float.MaxValue;float[] best=null;var initial=(float[])values.Clone();
            for(int seed=0;seed<4;seed++)
            {
                values=seed==0?(float[])initial.Clone():values.Select((v,i)=>Mathf.Sin(i*7.13f+seed*3.42f)*.7f).ToArray();
                for(float step=.4f;step>.006f;step*=.5f)
                for(int sweep=0;sweep<5;sweep++)
                for(int i=0;i<values.Length;i++)
                {
                    float current=values[i],value=current,loss=Loss();
                    foreach(float sign in new[]{-1f,1f}){values[i]=Mathf.Clamp(current+sign*step,-1,1);float trial=Loss();if(trial<loss){loss=trial;value=values[i];}}
                    values[i]=value;
                }
                float result=Loss();if(result<bestLoss){bestLoss=result;best=(float[])values.Clone();}
            }
            for(int i=0;i<best.Length;i++)AnimationUtility.SetEditorCurve(carry,bindings[i],AnimationCurve.Constant(0,1,best[i]));
            EditorUtility.SetDirty(carry);AssetDatabase.SaveAssets();
            File.WriteAllText(Path.Combine(QusapSwordCarryValidation.Output,"runtime_pose_authoring.txt"),"loss="+bestLoss+"\n"+string.Join("\n",bindings.Select((b,i)=>b.propertyName+"="+best[i])));
        }
        finally{animator.runtimeAnimatorController=originalController;Object.DestroyImmediate(overrides);Object.DestroyImmediate(temporary);}
        animator.Rebind();
    }
    public static void Probe()
    {
        EditorSceneManager.OpenScene(Scene);
        foreach (var old in Object.FindObjectsByType<QusapWeaponGripV1RuntimeValidation>())
            Object.DestroyImmediate(old.gameObject);
        new GameObject("SwordCarryValidation").AddComponent<QusapSwordCarryValidation>();
        EditorApplication.EnterPlaymode();
    }
}
