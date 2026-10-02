using System;
using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEditor.Animations;
using UnityEditor.SceneManagement;
using UnityEngine;
using Qusap;

public static class QusapHandSwitchV5Builder
{
    public const string Base="Assets/_Qusap/Tests/NewQusap75KAnimationTest/WeaponGripV1";
    public const string Root=Base+"/HandSwitchV5";
    public const string Output=@"C:\Dev\Qusap_ArtSource_Recovered\Qusap75K_WeaponGrip_v1\HandSwitchV5";
    public const string ModelPath=Root+"/Models/Qusap75K_WeaponGrip_v5.fbx";
    public const string PrefabPath=Root+"/Prefab/Qusap75K_WeaponGripV5_HandSwitch_Test.prefab";
    public const string ScenePath=Root+"/Scene/Qusap75K_WeaponGripV5_HandSwitch_Test.unity";
    public static void ImportV5()
    {
        Directory.CreateDirectory(Output);
        foreach(string sub in new[]{"Models","Animation","Controller","Prefab","Scene"})Directory.CreateDirectory(Root+"/"+sub);
        File.Copy(@"C:\Dev\Qusap_ArtSource_Recovered\Qusap75K_WeaponGrip_v1\Qusap75K_WeaponGrip_v5.fbx",ModelPath,true);
        AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
        var old=(ModelImporter)AssetImporter.GetAtPath(Base+"/Models/Qusap75K_WeaponGrip_v4.fbx");
        var next=(ModelImporter)AssetImporter.GetAtPath(ModelPath);
        var originalAvatar=AssetDatabase.LoadAllAssetsAtPath(old.assetPath).OfType<Avatar>().Single();
        // Reuse the existing Avatar. No regeneration or edits to its original mapping.
        next.animationType=ModelImporterAnimationType.Human;
        next.avatarSetup=ModelImporterAvatarSetup.CopyFromOther;
        next.sourceAvatar=originalAvatar; next.humanDescription=old.humanDescription;
        next.optimizeGameObjects=false;next.isReadable=true;next.importAnimation=false;
        next.globalScale=old.globalScale;next.useFileScale=old.useFileScale;
        next.skinWeights=ModelImporterSkinWeights.Standard;next.SaveAndReimport();
        var a=AssetDatabase.LoadAssetAtPath<GameObject>(old.assetPath);
        var b=AssetDatabase.LoadAssetAtPath<GameObject>(ModelPath);
        var avatar=next.sourceAvatar;
        var mapOld=old.humanDescription.human;var mapNew=next.humanDescription.human;
        var log=new StringBuilder();
        log.AppendLine($"v4_avatar_valid={originalAvatar.isValid}\nv4_avatar_human={originalAvatar.isHuman}\nv5_avatar_valid={avatar&&avatar.isValid}\nv5_avatar_human={avatar&&avatar.isHuman}\nreuses_original_avatar={avatar==originalAvatar}\nv4_humanoid_assignments={mapOld.Length}\nv5_humanoid_assignments={mapNew.Length}\noptimizeGameObjects={next.optimizeGameObjects}");
        bool mapping=mapOld.Length==22&&mapNew.Length==22&&mapOld.Zip(mapNew,(x,y)=>x.boneName==y.boneName&&x.humanName==y.humanName).All(x=>x);
        log.AppendLine("mapping_identical="+mapping);
        foreach(var h in mapOld)log.AppendLine(h.humanName+"="+h.boneName);
        var ta=a.GetComponentsInChildren<Transform>(true);var tb=b.GetComponentsInChildren<Transform>(true);
        float p=0,r=0,s=0;
        foreach(var t in ta.Where(t=>t!=a.transform))
        {
            var n=tb.SingleOrDefault(n=>n.name==t.name);if(!n)throw new InvalidOperationException("STOP: Missing existing bone/object "+t.name);
            p=Mathf.Max(p,Vector3.Distance(t.localPosition,n.localPosition));r=Mathf.Max(r,Quaternion.Angle(t.localRotation,n.localRotation));s=Mathf.Max(s,Vector3.Distance(t.localScale,n.localScale));
        }
        int custom=tb.Count(t=>t.name.StartsWith("claw_r_")||t.name.StartsWith("claw_l_"));
        var sa=a.GetComponentInChildren<SkinnedMeshRenderer>();var sb=b.GetComponentInChildren<SkinnedMeshRenderer>();
        log.AppendLine($"custom_claw_bones={custom}\nmax_existing_transform_position_delta_m={p:R}\nmax_existing_transform_rotation_delta_deg={r:R}\nmax_existing_transform_scale_delta={s:R}\nv4_bounds={sa.sharedMesh.bounds}\nv5_bounds={sb.sharedMesh.bounds}\nroot_position_identical={a.transform.localPosition==b.transform.localPosition}\nroot_scale_identical={a.transform.localScale==b.transform.localScale}");
        File.WriteAllText(Path.Combine(Output,"Avatar_v4_v5.txt"),log.ToString());
        if(!avatar||!avatar.isValid||!avatar.isHuman||!mapping||custom!=12||p>.00001f||s>.00001f)throw new InvalidOperationException("STOP: v5 Avatar/body comparison failed. v4 remains intact.");
        Debug.Log("V5 Avatar audit passed; original Avatar reused, identical 22 assignments, 12 custom claw bones.");
    }
    public static void Build()
    {
        if(!File.Exists(Path.Combine(Output,"Avatar_v4_v5.txt")))throw new InvalidOperationException("Run ImportV5 first.");
        var oldPrefab=Base+"/Prefab/Qusap75K_WeaponGripV1_Test.prefab";
        var controllerPath=Root+"/Controller/Qusap75K_HandSwitchV5_Test.controller";
        if(!AssetDatabase.LoadMainAssetAtPath(controllerPath))AssetDatabase.CopyAsset(Base+"/Controller/Qusap75K_WeaponGripV1_Test.controller",controllerPath);
        var controller=AssetDatabase.LoadAssetAtPath<AnimatorController>(controllerPath);
        var player=PrefabUtility.LoadPrefabContents(oldPrefab);
        try
        {
            var animator=player.GetComponentsInChildren<Animator>(true).First(a=>a.name=="Qusap75K_Visual");
            var oldVisual=animator.transform.Find("Visual");var oldHand=Find(animator,"hand_r");
            var oldSocket=oldHand.Find("WeaponGripSocket");
            var savedSocket=UnityEngine.Object.Instantiate(oldSocket.gameObject);savedSocket.name="WeaponGripSocket_R";
            var rightClip=oldHand.GetComponent<QusapWeaponGripPosePlayer>().GripClip;
            var material=oldVisual.GetComponentInChildren<SkinnedMeshRenderer>().sharedMaterials;
            UnityEngine.Object.DestroyImmediate(oldVisual.gameObject);
            var visual=(GameObject)PrefabUtility.InstantiatePrefab(AssetDatabase.LoadAssetAtPath<GameObject>(ModelPath),animator.transform);
            visual.name="Visual";visual.transform.localPosition=Vector3.zero;visual.transform.localRotation=Quaternion.identity;visual.transform.localScale=Vector3.one;
            visual.GetComponentInChildren<SkinnedMeshRenderer>().sharedMaterials=material;
            // The active outer Animator retains the existing Avatar and approved animations.
            var nestedAnimator=visual.GetComponent<Animator>();if(nestedAnimator)nestedAnimator.enabled=false;
            animator.runtimeAnimatorController=controller;animator.applyRootMotion=false;
            var right=Find(animator,"hand_r");var left=Find(animator,"hand_l");
            var sr=savedSocket.transform;sr.SetParent(right,false);
            right.gameObject.AddComponent<QusapWeaponGripPosePlayer>().GripClip=rightClip;
            var sl=new GameObject("WeaponGripSocket_L").transform;sl.SetParent(left,false);sl.localScale=Vector3.one;
            MirrorSocketAndGrip(animator,sr,sl,rightClip);
            var leftCarry=AssetDatabase.LoadAssetAtPath<AnimationClip>(Root+"/Animation/SwordCarryUpperBody_L.anim");
            if(!leftCarry){AssetDatabase.CopyAsset(Base+"/PresentationMirror/AlternativeHandSwitch/Animation/Qusap75K_SwordCarryLeftUpperBody.anim",Root+"/Animation/SwordCarryUpperBody_L.anim");leftCarry=AssetDatabase.LoadAssetAtPath<AnimationClip>(Root+"/Animation/SwordCarryUpperBody_L.anim");}
            var carryMask=Mask(animator,new[]{Find(animator,"clavicle_l"),Find(animator,"upperarm_l"),Find(animator,"lowerarm_l"),left},AvatarMaskBodyPart.LeftArm);
            Save(carryMask,Root+"/Animation/SwordCarryUpperBody_L.mask");AddLayer(controller,"SwordCarryUpperBody_L",leftCarry,AssetDatabase.LoadAssetAtPath<AvatarMask>(Root+"/Animation/SwordCarryUpperBody_L.mask"),0);
            var leftGrip=AssetDatabase.LoadAssetAtPath<AnimationClip>(Root+"/Animation/SwordGrip_L.anim");
            left.gameObject.AddComponent<QusapWeaponGripPosePlayer>().GripClip=leftGrip;
            var layerClip=new AnimationClip{name="SwordGrip_L_Layer",frameRate=30};var prefix=AnimationUtility.CalculateTransformPath(left,animator.transform);
            foreach(var binding in AnimationUtility.GetCurveBindings(leftGrip)){var b=binding;b.path=prefix+"/"+b.path;AnimationUtility.SetEditorCurve(layerClip,b,AnimationUtility.GetEditorCurve(leftGrip,binding));}
            Save(layerClip,Root+"/Animation/SwordGrip_L_Layer.anim");
            Save(Mask(animator,left.GetComponentsInChildren<Transform>().Where(t=>t.name.StartsWith("claw_l_")).ToArray(),null),Root+"/Animation/SwordGrip_L.mask");
            AddLayer(controller,"SwordGrip_L",AssetDatabase.LoadAssetAtPath<AnimationClip>(Root+"/Animation/SwordGrip_L_Layer.anim"),AssetDatabase.LoadAssetAtPath<AvatarMask>(Root+"/Animation/SwordGrip_L.mask"),0);
            var oldFacing=player.GetComponent<QusapModularFacingPresenter>().OrientationPivot;
            var pivot=new GameObject("WeaponPresentationPivotV5").transform;pivot.SetParent(oldFacing.parent,false);
            var basis=new GameObject("WeaponFacingBasis").transform;basis.SetParent(pivot,false);basis.localRotation=oldFacing.localRotation;
            animator.transform.SetParent(basis,false);
            var presentation=pivot.gameObject.AddComponent<QusapSwordHandSwitchV5>();presentation.Configure(player.GetComponent<QusapCombatController>(),animator,basis,sr,sl,sr.Find("SwordVisual"));
            var existingPoseData=AssetDatabase.LoadAssetAtPath<QusapSwordCarryLeftV5Data>(Root+"/Animation/SwordCarryUpperBody_L_PoseData.asset");
            if(existingPoseData)
            {
                var savedMask=AssetDatabase.LoadAssetAtPath<AvatarMask>(Root+"/Animation/SwordCarryUpperBody_L.mask");for(int i=0;i<(int)AvatarMaskBodyPart.LastBodyPart;i++)savedMask.SetHumanoidBodyPartActive((AvatarMaskBodyPart)i,false);EditorUtility.SetDirty(savedMask);
                animator.gameObject.AddComponent<QusapSwordCarryLeftPoseV5>().Configure(animator,presentation,new[]{"clavicle_l","upperarm_l","lowerarm_l","hand_l"}.Select(n=>Find(animator,n)).ToArray(),existingPoseData);
            }
            player.name="Qusap75K_WeaponGripV5_HandSwitch_Test";
            PrefabUtility.SaveAsPrefabAsset(player,PrefabPath);EditorUtility.SetDirty(controller);AssetDatabase.SaveAssets();
        }
        finally{PrefabUtility.UnloadPrefabContents(player);}
        var scene=EditorSceneManager.OpenScene(Base+"/Scene/Qusap75K_WeaponGripV1_Test.unity");
        var original=UnityEngine.Object.FindObjectsByType<GameObject>(FindObjectsInactive.Include).First(g=>g.name=="Player1_Qusap75K_WeaponGripV1_Test");
        var replacement=(GameObject)PrefabUtility.InstantiatePrefab(AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath),scene);
        replacement.name="Player1_Qusap75K_HandSwitchV5_Test";replacement.transform.SetPositionAndRotation(original.transform.position,original.transform.rotation);replacement.transform.localScale=original.transform.localScale;UnityEngine.Object.DestroyImmediate(original);
        foreach(var old in UnityEngine.Object.FindObjectsByType<QusapWeaponGripV1RuntimeValidation>())UnityEngine.Object.DestroyImmediate(old.gameObject);
        new GameObject("HandSwitchV5Validation").AddComponent<QusapHandSwitchV5Validation>();EditorSceneManager.SaveScene(scene,ScenePath);AssetDatabase.SaveAssets();
    }
    private static void MirrorSocketAndGrip(Animator a,Transform sr,Transform sl,AnimationClip rightClip)
    {
        var skin=a.GetComponentInChildren<SkinnedMeshRenderer>();var bones=skin.bones;var bind=skin.sharedMesh.bindposes;
        var mirror=Matrix4x4.Scale(new Vector3(-1,1,1));var modelFromMesh=a.transform.worldToLocalMatrix*skin.transform.localToWorldMatrix;
        Matrix4x4 Rest(Transform t)=>Matrix4x4.Rotate((modelFromMesh*bind[Array.IndexOf(bones,t)].inverse).rotation);
        var handR=sr.parent;var handL=sl.parent;var parity=Rest(handR).inverse*mirror*Rest(handL);
        sl.localPosition=parity.inverse.MultiplyPoint3x4(sr.localPosition);
        sl.localRotation=(parity.inverse*Matrix4x4.Rotate(sr.localRotation)*mirror).rotation;
        var grip=new AnimationClip{name="SwordGrip_L",frameRate=30};var report=new StringBuilder();
        foreach(var r in handR.GetComponentsInChildren<Transform>().Where(t=>t.name.StartsWith("claw_r_")))
        {
            var l=handL.GetComponentsInChildren<Transform>().Single(t=>t.name==r.name.Replace("claw_r_","claw_l_"));
            var localParity=Rest(r).inverse*mirror*Rest(l);var parentParity=Rest(r.parent).inverse*mirror*Rest(l.parent);
            var q=(parentParity.inverse*Matrix4x4.Rotate(ReadQuat(rightClip,AnimationUtility.CalculateTransformPath(r,handR)))*localParity).rotation;
            SetQuat(grip,AnimationUtility.CalculateTransformPath(l,handL),q);report.AppendLine(l.name+"="+q.ToString("F9"));
        }
        Save(grip,Root+"/Animation/SwordGrip_L.anim");
        File.WriteAllText(Path.Combine(Output,"left_grip_rotations.txt"),report.ToString());
        File.WriteAllText(Path.Combine(Output,"left_socket_pose.txt"),"position="+sl.localPosition.ToString("F9")+"\nrotation="+sl.localRotation.ToString("F9")+"\nscale="+sl.localScale.ToString("F9"));
    }
    public static Quaternion ReadQuat(AnimationClip c,string p){float V(string axis)=>AnimationUtility.GetEditorCurve(c,EditorCurveBinding.FloatCurve(p,typeof(Transform),"m_LocalRotation."+axis)).Evaluate(0);return new Quaternion(V("x"),V("y"),V("z"),V("w"));}
    public static void SetQuat(AnimationClip c,string p,Quaternion q){foreach(var v in new[]{("x",q.x),("y",q.y),("z",q.z),("w",q.w)})AnimationUtility.SetEditorCurve(c,EditorCurveBinding.FloatCurve(p,typeof(Transform),"m_LocalRotation."+v.Item1),AnimationCurve.Constant(0,1,v.Item2));}
    public static AvatarMask Mask(Animator a,Transform[] bones,AvatarMaskBodyPart? part){var m=new AvatarMask();for(int i=0;i<(int)AvatarMaskBodyPart.LastBodyPart;i++)m.SetHumanoidBodyPartActive((AvatarMaskBodyPart)i,part.HasValue&&i==(int)part.Value);var all=a.GetComponentsInChildren<Transform>(true);m.transformCount=all.Length;var paths=bones.Select(b=>AnimationUtility.CalculateTransformPath(b,a.transform)).ToArray();for(int i=0;i<all.Length;i++){var p=AnimationUtility.CalculateTransformPath(all[i],a.transform);m.SetTransformPath(i,p);m.SetTransformActive(i,paths.Contains(p));}return m;}
    public static void Save(UnityEngine.Object o,string p){var old=AssetDatabase.LoadMainAssetAtPath(p);if(old){EditorUtility.CopySerialized(o,old);UnityEngine.Object.DestroyImmediate(o);}else AssetDatabase.CreateAsset(o,p);}
    public static void AddLayer(AnimatorController c,string n,AnimationClip clip,AvatarMask mask,float w){var layers=c.layers;int i=Array.FindIndex(layers,l=>l.name==n);if(i<0){c.AddLayer(n);layers=c.layers;i=layers.Length-1;}layers[i].avatarMask=mask;layers[i].defaultWeight=w;layers[i].blendingMode=AnimatorLayerBlendingMode.Override;var state=layers[i].stateMachine.states.FirstOrDefault().state;if(!state)state=layers[i].stateMachine.AddState(n);state.writeDefaultValues=false;state.motion=clip;layers[i].stateMachine.defaultState=state;c.layers=layers;}
    public static void Author(){EditorSceneManager.OpenScene(ScenePath);UnityEngine.Object.FindAnyObjectByType<QusapHandSwitchV5Validation>().author=true;EditorApplication.EnterPlaymode();}
    public static void Validate(){EditorSceneManager.OpenScene(ScenePath);EditorApplication.EnterPlaymode();}
    public static void Probe(){EditorSceneManager.OpenScene(ScenePath);UnityEngine.Object.FindAnyObjectByType<QusapHandSwitchV5Validation>().probe=true;EditorApplication.EnterPlaymode();}
    public static void PrepareEvidence()
    {
        var scene=EditorSceneManager.OpenScene(ScenePath);
        // The validation animates a visual clone. Keep serialized gameplay components
        // intact and inactive during this isolated evidence session.
        foreach(var root in scene.GetRootGameObjects().Where(g=>g.name.StartsWith("Player")))root.SetActive(false);
        EditorSceneManager.SaveScene(scene,ScenePath);
    }
    public static void AuditFinal()
    {
        var prefab=AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath);
        var animator=prefab.GetComponentsInChildren<Animator>(true).First(a=>a.name=="Qusap75K_Visual");
        var model=(ModelImporter)AssetImporter.GetAtPath(ModelPath);
        var custom=animator.GetComponentsInChildren<Transform>(true).Where(t=>t.name.StartsWith("claw_l_")||t.name.StartsWith("claw_r_")).ToArray();
        if(!animator.avatar.isValid||!animator.avatar.isHuman||model.humanDescription.human.Length!=22||custom.Length!=12||animator.applyRootMotion||model.optimizeGameObjects)throw new InvalidOperationException("Final Avatar/rig invariant failed");
        var report=new StringBuilder("compiled=True\nAvatar valid=True\nAvatar Human=True\nHumanoid assignments=22\ncustom claw bones=12\nRoot Motion=False\nOptimize Game Objects=False\n");
        foreach(var pair in new[]{("SwordGrip_L",6),("SwordCarryUpperBody_L",4)})
        {
            var mask=AssetDatabase.LoadAssetAtPath<AvatarMask>(Root+"/Animation/"+pair.Item1+".mask");
            var paths=Enumerable.Range(0,mask.transformCount).Where(i=>mask.GetTransformActive(i)).Select(i=>mask.GetTransformPath(i)).ToArray();
            if(paths.Length!=pair.Item2)throw new InvalidOperationException("Mask scope failed "+pair.Item1);
            if(Enumerable.Range(0,(int)AvatarMaskBodyPart.LastBodyPart).Any(i=>mask.GetHumanoidBodyPartActive((AvatarMaskBodyPart)i)))throw new InvalidOperationException("Left mask Humanoid scope is not sparse");
            report.AppendLine(pair.Item1+" active transforms="+paths.Length);foreach(var path in paths)report.AppendLine(path);
        }
        var presentation=prefab.GetComponentInChildren<QusapSwordHandSwitchV5>(true);
        if(presentation.GetComponentsInChildren<Collider>(true).Length!=0||presentation.GetComponentsInChildren<Rigidbody>(true).Length!=0||presentation.GetComponentsInChildren<Transform>(true).Count(t=>t.name=="SwordVisual")!=1)throw new InvalidOperationException("Final presentation/weapon scope failed");
        report.AppendLine("SwordVisual instances in prefab=1\npresentation Rigidbody count=0\npresentation Collider count=0");
        File.WriteAllText(Path.Combine(Output,"Final_Asset_Audit.txt"),report.ToString());
    }
    private static Transform Find(Animator a,string n)=>a.GetComponentsInChildren<Transform>(true).First(t=>t.name==n);
}
