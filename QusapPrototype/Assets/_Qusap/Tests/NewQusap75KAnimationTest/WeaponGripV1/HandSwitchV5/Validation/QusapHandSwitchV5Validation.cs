#if UNITY_EDITOR
using System;
using System.Collections;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using UnityEngine;
using UnityEngine.Rendering;
#if UNITY_EDITOR
using UnityEditor;
#endif

// Evidence runner in the additional isolated scene; never edits the approved prefab or animations.
public sealed class QusapHandSwitchV5Validation : MonoBehaviour
{
    public bool author;
    public bool probe;
    public const string Output=@"C:\Dev\Qusap_ArtSource_Recovered\Qusap75K_WeaponGrip_v1\HandSwitchV5";
    private Transform pivot,hand,socket,sword;
    private Transform rightSocket,leftSocket;
    private Vector3 leftSocketPosition;
    private Quaternion leftSocketRotation;
    private Transform[] leftClaws;
    private Quaternion[] approvedLeftGrip;
    private float maxLeftGripDelta;
    private int maxSwords,minSwords=100; private string weaponId;
    private Animator animator;
    private QusapSwordHandSwitchV5 facing;
    private SkinnedMeshRenderer skin;
    private MeshFilter swordMesh;
    private Camera camera;
    private RenderTexture target;
    private Texture2D pixels;
    private string capture,segment;
    private QusapContinuousAvi video;
    private StreamWriter frameLog;
    private StringBuilder console=new StringBuilder();
    private int errors,warnings;
    private float recordStart,maxGripDelta,maxSocketPositionDelta,maxSocketRotationDelta,maxSwordScaleDelta,maxSwordRotationDelta,maxPivotMagnitudeDelta,maxPlayerPositionDelta;
    private Vector3 socketPosition,swordScale,playerPosition,cameraPosition;
    private Quaternion socketRotation,swordRotation,cameraRotation;
    private Transform player;
    private Transform[] claws;
    private Quaternion[] approvedGrip;
    private void Awake()
    {
        Directory.CreateDirectory(Output);
        System.Threading.Thread.CurrentThread.CurrentCulture=CultureInfo.InvariantCulture;
        Application.logMessageReceived+=Log;
    }
    private IEnumerator Start()
    {
        player=UnityEngine.Object.FindObjectsByType<GameObject>(FindObjectsInactive.Include).First(g=>g.name=="Player1_Qusap75K_HandSwitchV5_Test").transform;
        var original=player.GetComponentsInChildren<QusapSwordHandSwitchV5>(true).Single();
        var clone=Instantiate(original.gameObject);pivot=clone.transform;
        pivot.SetPositionAndRotation(original.transform.position,original.transform.rotation);pivot.localScale=Vector3.one;
        playerPosition=player.position;player.gameObject.SetActive(false);
        facing=clone.GetComponent<QusapSwordHandSwitchV5>();facing.SetPreviewFacing(1);
        foreach(var t in clone.GetComponentsInChildren<Transform>(true))t.gameObject.layer=30;
        if(clone.GetComponentsInChildren<Rigidbody>(true).Length!=0||clone.GetComponentsInChildren<Collider>(true).Length!=0)throw new InvalidOperationException("Visual clone contains physics.");
        animator=clone.GetComponentsInChildren<Animator>(true).First(a=>a.name=="Qusap75K_Visual");
        foreach(var a in clone.GetComponentsInChildren<Animator>(true))if(a!=animator)a.enabled=false;
        animator.cullingMode=AnimatorCullingMode.AlwaysAnimate;
        if(animator.applyRootMotion)throw new InvalidOperationException("Root Motion is enabled.");
        hand=animator.GetBoneTransform(HumanBodyBones.RightHand);rightSocket=hand.Find("WeaponGripSocket_R");leftSocket=animator.GetBoneTransform(HumanBodyBones.LeftHand).Find("WeaponGripSocket_L");
        leftSocketPosition=leftSocket.localPosition;leftSocketRotation=leftSocket.localRotation;socket=rightSocket;sword=facing.SwordVisual;
        socketPosition=socket.localPosition;socketRotation=socket.localRotation;swordScale=sword.localScale;swordRotation=sword.localRotation;
        weaponId=sword.GetEntityId().ToString();swordMesh=sword.GetComponentInChildren<MeshFilter>();skin=clone.GetComponentInChildren<SkinnedMeshRenderer>();skin.updateWhenOffscreen=true;
        var weightsLog=new StringBuilder("vertex,bone,weight\n");var sourceWeights=skin.sharedMesh.boneWeights;
        for(int i=0;i<sourceWeights.Length;i++){var w=sourceWeights[i];foreach(var pair in new[]{(w.boneIndex0,w.weight0),(w.boneIndex1,w.weight1),(w.boneIndex2,w.weight2),(w.boneIndex3,w.weight3)})if(pair.Item2>0&&skin.bones[pair.Item1].name.StartsWith("claw_"))weightsLog.AppendLine($"{i},{skin.bones[pair.Item1].name},{pair.Item2:R}");}
        File.WriteAllText(Path.Combine(Output,"unity_claw_vertex_weights.csv"),weightsLog.ToString());
        var swordTopology=new StringBuilder("index,a,b,c\n");var swordTris=swordMesh.sharedMesh.triangles;var swordVerts=swordMesh.sharedMesh.vertices;
        for(int i=0;i<swordTris.Length;i+=3)if(new[]{swordTris[i],swordTris[i+1],swordTris[i+2]}.All(j=>Mathf.Abs(swordVerts[j].y)<.076f))swordTopology.AppendLine($"{i/3},{swordTris[i]},{swordTris[i+1]},{swordTris[i+2]}");
        File.WriteAllText(Path.Combine(Output,"handle_triangles.csv"),swordTopology.ToString());
        claws=hand.GetComponentsInChildren<Transform>(true).Where(t=>t.name.StartsWith("claw_r_")).OrderBy(t=>t.name).ToArray();
#if UNITY_EDITOR
        var grip=AssetDatabase.LoadAssetAtPath<AnimationClip>("Assets/_Qusap/Tests/NewQusap75KAnimationTest/WeaponGripV1/Animation/Qusap75K_SwordGrip.anim");
        approvedGrip=claws.Select(t=>{
            var path=AnimationUtility.CalculateTransformPath(t,hand);
            float Value(string axis)=>AnimationUtility.GetEditorCurve(grip,EditorCurveBinding.FloatCurve(path,typeof(Transform),"m_LocalRotation."+axis)).Evaluate(0);
            return new Quaternion(Value("x"),Value("y"),Value("z"),Value("w"));
        }).ToArray();
        var leftHand=animator.GetBoneTransform(HumanBodyBones.LeftHand);
        leftClaws=leftHand.GetComponentsInChildren<Transform>(true).Where(t=>t.name.StartsWith("claw_l_")).OrderBy(t=>t.name).ToArray();
        var leftSampler=leftHand.GetComponent<QusapWeaponGripPosePlayer>();var leftGrip=leftSampler?leftSampler.GripClip:null;
        approvedLeftGrip=leftClaws.Select(t=>{
            var path=AnimationUtility.CalculateTransformPath(t,leftHand);
            float Value(string axis)=>AnimationUtility.GetEditorCurve(leftGrip,EditorCurveBinding.FloatCurve(path,typeof(Transform),"m_LocalRotation."+axis)).Evaluate(0);
            return new Quaternion(Value("x"),Value("y"),Value("z"),Value("w"));
        }).ToArray();
#endif
        camera=new GameObject("FixedSideMirrorEvidenceCamera").AddComponent<Camera>();
        camera.orthographic=true;camera.orthographicSize=2;camera.cullingMask=1<<30;camera.clearFlags=CameraClearFlags.SolidColor;camera.backgroundColor=new Color(.12f,.13f,.14f);
        camera.transform.SetPositionAndRotation(pivot.position+new Vector3(0,1.25f,-10),Quaternion.identity);
        cameraPosition=camera.transform.position;cameraRotation=camera.transform.rotation;
        target=new RenderTexture(1280,720,24);target.Create();camera.targetTexture=target;pixels=new Texture2D(1280,720,TextureFormat.RGB24,false);
        RenderPipelineManager.endCameraRendering+=Rendered;Time.captureFramerate=30;
        // Ground reference outside the reflected hierarchy. No collider or material override on the actor.
        var line=GameObject.CreatePrimitive(PrimitiveType.Cube);line.name="GroundReference_visual_only";line.layer=30;
        Destroy(line.GetComponent<Collider>());line.transform.position=pivot.position+new Vector3(0,-.02f,.7f);line.transform.localScale=new Vector3(6,.025f,1);
        if(author)
        {
            animator.Play("CombatIdle_B1",0,.5f);yield return new WaitForSeconds(.5f);
            System.Type.GetType("QusapHandSwitchV5Author, Assembly-CSharp-Editor").GetMethod("AuthorLeftPose").Invoke(null,new object[]{animator});
            ReadActiveSide();capture="AUTHOR_left";yield return new WaitForSeconds(.3f);
            facing.SetPreviewFacing(1);capture="AUTHOR_right";yield return new WaitForSeconds(.3f);
#if UNITY_EDITOR
            EditorApplication.Exit(0);
#endif
            yield break;
        }
        yield return VerifyPositiveScaleAndRoll();
        if(probe)
        {
            animator.speed=0;animator.Play("CombatIdle_B1",0,.5f);
            foreach(int side in new[]{1,-1}){facing.SetPreviewFacing(side);animator.Update(0);yield return null;yield return null;ReadActiveSide();camera.orthographicSize=.30f;camera.transform.position=new Vector3(socket.position.x,socket.position.y,cameraPosition.z);capture="PROBE_GRIP_"+(side>0?"right":"left");ExportGeometry(capture);yield return null;}
            File.WriteAllText(Path.Combine(Output,"Probe_Console.txt"),console+"\nerrors="+errors+"\nwarnings="+warnings);
#if UNITY_EDITOR
            EditorApplication.Exit(errors==0?0:2);
#endif
            yield break;
        }
        facing.SetPreviewFacing(1);animator.speed=1;animator.Play("CombatIdle_B1",0,0);yield return new WaitForSeconds(.5f);
        video=new QusapContinuousAvi(Path.Combine(Output,"Qusap_HandSwitchV5_CONTINUOUS.avi"),1280,720,30);
        frameLog=new StreamWriter(Path.Combine(Output,"continuous_frame_log.csv"));
        frameLog.WriteLine("video_frame,unity_frame,time_s,segment,facing,phase,carry_weight,left_carry_weight,apparent_blade_angle_deg,guard_y,tip_y,sword_min_y,socket_pivot_m,pivot_sx,pivot_sy,pivot_sz,sword_basis_x_m,sword_basis_y_m,sword_basis_z_m,player_position_delta_m,grip_delta_deg,side_camera_fixed,active_swords,weapon_instance_id,parent_is_selected_socket,left_grip_delta_deg");recordStart=Time.time;
        segment="Idle derecha";capture="SIDE_Idle_right";yield return new WaitForSeconds(2);
        segment="Run derecha";animator.CrossFadeInFixedTime("RunForward",.18f,0);yield return new WaitForSeconds(1);capture="SIDE_Run_right";yield return new WaitForSeconds(1);
        segment="Cambio derecha a izquierda";facing.SetPreviewFacing(-1);yield return new WaitForSeconds(.5f);
        segment="Idle izquierda";animator.CrossFadeInFixedTime("CombatIdle_B1",.18f,0);yield return new WaitForSeconds(1);capture="SIDE_Idle_left";yield return new WaitForSeconds(1);
        segment="Run izquierda";animator.CrossFadeInFixedTime("RunForward",.18f,0);yield return new WaitForSeconds(1);capture="SIDE_Run_left";yield return new WaitForSeconds(1);
        segment="Cambio izquierda a derecha";facing.SetPreviewFacing(1);yield return new WaitForSeconds(.5f);
        // Ten consecutive atomic transfers; positive unit scale.
        for(int i=1;i<=10;i++){segment="Cambio rapido "+i+" de 10";facing.SetPreviewFacing(-facing.FacingDirection);yield return new WaitForSeconds(.3f);}
        float rollLength=animator.runtimeAnimatorController.animationClips.First(c=>c.name=="RM_Roll_front").length;
        segment="Preparacion Roll derecha";facing.SetPreviewFacing(1);animator.CrossFadeInFixedTime("CombatIdle_B1",.18f,0);yield return new WaitForSeconds(.5f);
        segment="Roll derecha";animator.CrossFadeInFixedTime("RollRMFrontInPlace",.22f,0);yield return new WaitForSeconds(rollLength);
        segment="Regreso Idle derecha";animator.CrossFadeInFixedTime("CombatIdle_B1",.22f,0);yield return new WaitForSeconds(.7f);
        segment="Preparacion Roll izquierda";facing.SetPreviewFacing(-1);yield return new WaitForSeconds(.5f);
        segment="Roll izquierda";animator.CrossFadeInFixedTime("RollRMFrontInPlace",.22f,0);yield return new WaitForSeconds(rollLength);
        segment="Regreso Idle izquierda";animator.CrossFadeInFixedTime("CombatIdle_B1",.22f,0);yield return new WaitForSeconds(.7f);
        segment="Roll derecha 0.25x preparacion";facing.SetPreviewFacing(1);yield return new WaitForSeconds(.3f);
        animator.speed=.25f;segment="Roll derecha 0.25x";animator.CrossFadeInFixedTime("RollRMFrontInPlace",.22f,0);yield return new WaitForSeconds(rollLength*4);
        animator.speed=1;segment="Regreso Carry derecha";animator.CrossFadeInFixedTime("CombatIdle_B1",.22f,0);yield return new WaitForSeconds(.7f);
        facing.SetPreviewFacing(-1);segment="Roll izquierda 0.25x preparacion";yield return new WaitForSeconds(.3f);
        animator.speed=.25f;segment="Roll izquierda 0.25x";animator.CrossFadeInFixedTime("RollRMFrontInPlace",.22f,0);yield return new WaitForSeconds(rollLength*4);
        animator.speed=1;segment="Regreso Carry izquierda";animator.CrossFadeInFixedTime("CombatIdle_B1",.22f,0);yield return new WaitForSeconds(.7f);
        segment="Acercamiento agarre derecha";facing.SetPreviewFacing(1);yield return new WaitForSeconds(.3f);
        camera.orthographicSize=.36f;camera.transform.position=new Vector3(socket.position.x,socket.position.y,cameraPosition.z);capture="GRIP_right";yield return new WaitForSeconds(2);
        segment="Acercamiento agarre izquierda";facing.SetPreviewFacing(-1);yield return new WaitForSeconds(.1f);
        camera.transform.position=new Vector3(socket.position.x,socket.position.y,cameraPosition.z);capture="GRIP_left";yield return new WaitForSeconds(2);
        console.AppendLine($"max_active_swords={maxSwords}\nmin_active_swords={minSwords}\nweapon_instance_id={weaponId}\nmax_left_grip_delta_deg={maxLeftGripDelta:F6}");
        int frames=video.Frames;video.Dispose();video=null;frameLog.Dispose();frameLog=null;
        RenderPipelineManager.endCameraRendering-=Rendered;Time.captureFramerate=0;
        console.AppendLine($"errors={errors}\nwarnings={warnings}\nrecorded_frames={frames}\nfps=30\nduration_seconds={frames/30f:F3}\napplyRootMotion={animator.applyRootMotion}");
        console.AppendLine($"max_grip_delta_deg={maxGripDelta:F6}\nleft_articulated_claw_count={leftClaws.Length}\nmax_socket_position_delta_m={maxSocketPositionDelta:F8}\nmax_socket_rotation_delta_deg={maxSocketRotationDelta:F6}\nmax_sword_local_rotation_delta_deg={maxSwordRotationDelta:F6}\nmax_sword_local_scale_delta={maxSwordScaleDelta:F8}\nmax_pivot_scale_magnitude_delta={maxPivotMagnitudeDelta:F8}\nmax_player_position_delta_m={maxPlayerPositionDelta:F8}");
        console.AppendLine("Evidence visual clone world position="+pivot.localPosition.ToString("F7")+" rotation="+pivot.localRotation.ToString("F7")+" scale="+pivot.localScale.ToString("F7"));
        File.WriteAllText(Path.Combine(Output,"Console.txt"),console.ToString());Application.logMessageReceived-=Log;
#if UNITY_EDITOR
        if(Application.isBatchMode)EditorApplication.Exit(errors==0?0:2);else EditorApplication.ExitPlaymode();
#endif
    }
    private IEnumerator VerifyPositiveScaleAndRoll()
    {
        var reference=Instantiate(pivot.gameObject);reference.name="SynchronizedPositiveScaleReference";
        foreach(var r in reference.GetComponentsInChildren<Renderer>())r.enabled=false;
        reference.transform.SetPositionAndRotation(pivot.position,pivot.rotation);
        var refFacing=reference.GetComponent<QusapSwordHandSwitchV5>();
        var refAnimator=reference.GetComponentsInChildren<Animator>(true).First(a=>a.name=="Qusap75K_Visual");
        var refSkin=reference.GetComponentInChildren<SkinnedMeshRenderer>();
        var originalPrefab=AssetDatabase.LoadAssetAtPath<GameObject>("Assets/_Qusap/Tests/NewQusap75KAnimationTest/WeaponGripV1/Prefab/Qusap75K_WeaponGripV1_Test.prefab");
        var originalAnimator=originalPrefab.GetComponentsInChildren<Animator>(true).First(a=>a.name=="Qusap75K_Visual");
        var originalBasis=new GameObject("OriginalV4RollReferenceBasis").transform;originalBasis.SetPositionAndRotation(pivot.position,pivot.rotation);
        var originalRig=Instantiate(originalAnimator.gameObject,originalBasis,false);var originalV4=originalRig.GetComponent<Animator>();originalV4.speed=0;originalV4.cullingMode=AnimatorCullingMode.AlwaysAnimate;
        foreach(var a in originalRig.GetComponentsInChildren<Animator>(true))if(a!=originalV4)a.enabled=false;
        foreach(var r in originalRig.GetComponentsInChildren<Renderer>(true))r.enabled=false;
        var checks=new StringBuilder("state,facing,phase,body_vertex_reference_delta_m,body_normal_reference_delta,max_roll_bone_delta_m,max_roll_rotation_delta_deg,right_carry_weight,left_carry_weight,guard_y,tip_y,apparent_angle_deg,blade_length_m,v4_roll_bone_delta_m,v4_roll_rotation_delta_deg\n");
        var baked=new Mesh();var refBaked=new Mesh();animator.speed=0;refAnimator.speed=0;
        foreach(var state in new[]{"CombatIdle_B1","RunForward","RollRMFrontInPlace"})
        foreach(int direction in new[]{1,-1})for(int phase=0;phase<120;phase++)
        {
            facing.SetPreviewFacing(direction);refFacing.SetPreviewFacing(direction);
            originalBasis.localRotation=Quaternion.Euler(0,direction>0?60:-60,0);
            float time=phase/120f;animator.Play(state,0,time);refAnimator.Play(state,0,time);
            originalV4.Play(state,0,time);originalV4.SetLayerWeight(originalV4.GetLayerIndex("SwordCarryUpperBody"),state=="RollRMFrontInPlace"?0:1);originalV4.Update(0);
            animator.SetLayerWeight(animator.GetLayerIndex("SwordCarryUpperBody"),state=="RollRMFrontInPlace"||direction<0?0:1);
            animator.SetLayerWeight(animator.GetLayerIndex("SwordCarryUpperBody_L"),state=="RollRMFrontInPlace"||direction>0?0:1);
            refAnimator.SetLayerWeight(refAnimator.GetLayerIndex("SwordCarryUpperBody"),state=="RollRMFrontInPlace"||direction<0?0:1);
            refAnimator.SetLayerWeight(refAnimator.GetLayerIndex("SwordCarryUpperBody_L"),state=="RollRMFrontInPlace"||direction>0?0:1);
            animator.Update(0);refAnimator.Update(0);yield return null;yield return null;
            ReadActiveSide();skin.BakeMesh(baked,true);refSkin.BakeMesh(refBaked,true);
            var points=baked.vertices;var refPoints=refBaked.vertices;var normals=baked.normals;var refNormals=refBaked.normals;
            var matrix=skin.transform.localToWorldMatrix;var refMatrix=refSkin.transform.localToWorldMatrix;
            var normalMatrix=matrix.inverse.transpose;var refNormalMatrix=refMatrix.inverse.transpose;
            float pointDelta=0,normalDelta=0,boneDelta=0,rotationDelta=0,originalDelta=0,originalAngle=0;
            for(int i=0;i<points.Length;i++)
            {
                pointDelta=Mathf.Max(pointDelta,Vector3.Distance(matrix.MultiplyPoint3x4(points[i]),refMatrix.MultiplyPoint3x4(refPoints[i])));
                normalDelta=Mathf.Max(normalDelta,Vector3.Distance(normalMatrix.MultiplyVector(normals[i]).normalized,refNormalMatrix.MultiplyVector(refNormals[i]).normalized));
            }
            if(state=="RollRMFrontInPlace")for(int i=0;i<(int)HumanBodyBones.LastBone;i++)
            {
                var a=animator.GetBoneTransform((HumanBodyBones)i);var b=refAnimator.GetBoneTransform((HumanBodyBones)i);if(!a||!b)continue;
                boneDelta=Mathf.Max(boneDelta,Vector3.Distance(a.position,b.position));rotationDelta=Mathf.Max(rotationDelta,Quaternion.Angle(a.localRotation,b.localRotation));
                var originalBone=originalV4.GetBoneTransform((HumanBodyBones)i);if(originalBone){originalDelta=Mathf.Max(originalDelta,Vector3.Distance(a.position,originalBone.position));originalAngle=Mathf.Max(originalAngle,Quaternion.Angle(a.localRotation,originalBone.localRotation));}
            }
            var tip=swordMesh.transform.TransformPoint(swordMesh.sharedMesh.vertices.OrderByDescending(v=>v.y).First());var blade=tip-socket.position;
            checks.AppendLine($"{state},{direction},{time:F6},{pointDelta:F8},{normalDelta:F8},{boneDelta:F8},{rotationDelta:F6},{animator.GetLayerWeight(animator.GetLayerIndex("SwordCarryUpperBody")):F6},{animator.GetLayerWeight(animator.GetLayerIndex("SwordCarryUpperBody_L")):F6},{socket.position.y:F6},{tip.y:F6},{Mathf.Atan2(blade.y,Mathf.Abs(blade.x))*Mathf.Rad2Deg:F4},{blade.magnitude:F8},{originalDelta:F8},{originalAngle:F6}");
            if(pointDelta>.0001f||normalDelta>.001f||boneDelta>.0001f)Debug.LogError("Positive scale/reference pose differs: "+state);
            if(phase==60){capture="SYNC_"+state+"_"+(direction>0?"right":"left");ExportGeometry(capture);}
        }
        File.WriteAllText(Path.Combine(Output,"synchronized_positive_scale_checks.csv"),checks.ToString());
        Destroy(baked);Destroy(refBaked);Destroy(reference);Destroy(originalBasis.gameObject);animator.speed=1;
    }
    private void ReadActiveSide()
    {
        hand=animator.GetBoneTransform(facing.FacingDirection>0?HumanBodyBones.RightHand:HumanBodyBones.LeftHand);
        socket=facing.FacingDirection>0?rightSocket:leftSocket;sword=facing.SwordVisual;swordMesh=sword.GetComponentInChildren<MeshFilter>();
    }
    private void Rendered(ScriptableRenderContext context,Camera rendered)
    {
        if(rendered!=camera||(capture==null&&video==null))return;
        ReadActiveSide();
        var previous=RenderTexture.active;RenderTexture.active=target;pixels.ReadPixels(new Rect(0,0,1280,720),0,0);pixels.Apply();RenderTexture.active=previous;
        if(capture!=null){File.WriteAllBytes(Path.Combine(Output,capture+".png"),pixels.EncodeToPNG());capture=null;}
        if(video==null)return;
        float gripDelta=0;if(facing.FacingDirection>0)for(int i=0;i<claws.Length;i++)gripDelta=Mathf.Max(gripDelta,Quaternion.Angle(claws[i].localRotation,approvedGrip[i]));
        maxGripDelta=Mathf.Max(maxGripDelta,gripDelta);
        float leftDelta=0;if(facing.FacingDirection<0)for(int i=0;i<leftClaws.Length;i++)leftDelta=Mathf.Max(leftDelta,Quaternion.Angle(leftClaws[i].localRotation,approvedLeftGrip[i]));maxLeftGripDelta=Mathf.Max(maxLeftGripDelta,leftDelta);
        int swordCount=pivot.GetComponentsInChildren<Transform>().Count(t=>t.name=="SwordVisual"&&t.gameObject.activeInHierarchy);maxSwords=Mathf.Max(maxSwords,swordCount);minSwords=Mathf.Min(minSwords,swordCount);
        if(swordCount!=1||sword.GetEntityId().ToString()!=weaponId||sword.parent!=socket)Debug.LogError("Weapon transfer invariant failed");
        maxSocketPositionDelta=Mathf.Max(maxSocketPositionDelta,Mathf.Max(Vector3.Distance(rightSocket.localPosition,socketPosition),Vector3.Distance(leftSocket.localPosition,leftSocketPosition)));
        maxSocketRotationDelta=Mathf.Max(maxSocketRotationDelta,Mathf.Max(Quaternion.Angle(rightSocket.localRotation,socketRotation),Quaternion.Angle(leftSocket.localRotation,leftSocketRotation)));
        maxSwordRotationDelta=Mathf.Max(maxSwordRotationDelta,Quaternion.Angle(sword.localRotation,swordRotation));
        maxSwordScaleDelta=Mathf.Max(maxSwordScaleDelta,Vector3.Distance(sword.localScale,swordScale));
        maxPivotMagnitudeDelta=Mathf.Max(maxPivotMagnitudeDelta,Vector3.Distance(new Vector3(Mathf.Abs(pivot.localScale.x),Mathf.Abs(pivot.localScale.y),Mathf.Abs(pivot.localScale.z)),Vector3.one));
        maxPlayerPositionDelta=Mathf.Max(maxPlayerPositionDelta,Vector3.Distance(player.position,playerPosition));
        video.Add(pixels.EncodeToJPG(90));
        var vertices=swordMesh.sharedMesh.vertices;var tip=swordMesh.transform.TransformPoint(vertices.OrderByDescending(v=>v.y).First());
        var vector=tip-socket.position;
        float apparent=Mathf.Atan2(vector.y,Mathf.Abs(vector.x))*Mathf.Rad2Deg;
        var matrix=swordMesh.transform.localToWorldMatrix;
        var basis=new Vector3(((Vector3)matrix.GetColumn(0)).magnitude,((Vector3)matrix.GetColumn(1)).magnitude,((Vector3)matrix.GetColumn(2)).magnitude);
        bool fixedCamera=camera.transform.position==cameraPosition&&camera.transform.rotation==cameraRotation;
        frameLog.WriteLine($"{video.Frames-1},{Time.frameCount},{Time.time-recordStart:F6},{segment},{facing.FacingDirection},{animator.GetCurrentAnimatorStateInfo(0).normalizedTime:F6},{animator.GetLayerWeight(animator.GetLayerIndex("SwordCarryUpperBody")):F6},{animator.GetLayerWeight(animator.GetLayerIndex("SwordCarryUpperBody_L")):F6},{apparent:F4},{socket.position.y:F6},{tip.y:F6},{sword.GetComponentsInChildren<MeshRenderer>().Min(r=>r.bounds.min.y):F6},{Vector3.Distance(socket.position,sword.position):F8},{pivot.localScale.x:F3},{pivot.localScale.y:F3},{pivot.localScale.z:F3},{basis.x:F8},{basis.y:F8},{basis.z:F8},{Vector3.Distance(player.position,playerPosition):F8},{gripDelta:F6},{fixedCamera},{swordCount},{sword.GetEntityId().ToString()},{sword.parent==socket},{leftDelta:F6}");
        // Actual geometry for every recorded frame, with inherited scale compensated once.
        ExportGeometry("FRAME_"+video.Frames.ToString("D4"));
    }
    private void ExportGeometry(string name)
    {
        var folder=Path.Combine(Output,"collision_geometry");Directory.CreateDirectory(folder);
        var baked=new Mesh();skin.BakeMesh(baked,true);var positions=baked.vertices;var weights=skin.sharedMesh.boneWeights;var bones=skin.bones;
        bool IsHand(int i)=>bones[i].name=="hand_r"||bones[i].name=="hand_l"||bones[i].name.StartsWith("claw_r_")||bones[i].name.StartsWith("claw_l_");
        var handVertices=weights.Select(w=>(IsHand(w.boneIndex0)?w.weight0:0)+(IsHand(w.boneIndex1)?w.weight1:0)+(IsHand(w.boneIndex2)?w.weight2:0)+(IsHand(w.boneIndex3)?w.weight3:0)>.45f).ToArray();
        var triangles=skin.sharedMesh.triangles;
        var body=Enumerable.Range(0,triangles.Length/3).Where(i=>!handVertices[triangles[i*3]]&&!handVertices[triangles[i*3+1]]&&!handVertices[triangles[i*3+2]]).SelectMany(i=>new[]{triangles[i*3],triangles[i*3+1],triangles[i*3+2]}).ToArray();
        var verts=swordMesh.sharedMesh.vertices;var tris=swordMesh.sharedMesh.triangles;
        var blade=Enumerable.Range(0,tris.Length/3).Where(i=>verts[tris[i*3]].y>.13f&&verts[tris[i*3+1]].y>.13f&&verts[tris[i*3+2]].y>.13f).SelectMany(i=>new[]{tris[i*3],tris[i*3+1],tris[i*3+2]}).ToArray();
        var bodyMatrix=skin.transform.localToWorldMatrix;var swordMatrix=swordMesh.transform.localToWorldMatrix;
        using(var writer=new BinaryWriter(File.Create(Path.Combine(folder,name+".bin"))))
        {
            writer.Write(positions.Length);foreach(var v in positions){var p=bodyMatrix.MultiplyPoint3x4(v);writer.Write(p.x);writer.Write(p.y);writer.Write(p.z);}
            writer.Write(body.Length);foreach(var i in body)writer.Write(i);
            writer.Write(verts.Length);foreach(var v in verts){var p=swordMatrix.MultiplyPoint3x4(v);writer.Write(p.x);writer.Write(p.y);writer.Write(p.z);}
            writer.Write(blade.Length);foreach(var i in blade)writer.Write(i);
        }
        Destroy(baked);
    }
    private void Log(string message,string stack,LogType type)
    {
        if(type==LogType.Error||type==LogType.Exception||type==LogType.Assert){errors++;console.AppendLine(type+": "+message+"\n"+stack);}
        if(type==LogType.Warning){warnings++;console.AppendLine("Warning: "+message);}
    }
}

#endif
