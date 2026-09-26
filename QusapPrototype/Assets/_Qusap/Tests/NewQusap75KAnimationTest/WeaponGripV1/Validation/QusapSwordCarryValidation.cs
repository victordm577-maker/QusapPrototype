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

public sealed class QusapSwordCarryValidation : MonoBehaviour
{
    public bool final;
    public bool preview;
    public bool author;
    public const string Output = @"C:\Dev\Qusap_ArtSource_Recovered\Qusap75K_WeaponGrip_v1\SwordCarry";
    private Animator animator;
    private Transform actor, hand, socket;
    private Camera camera;
    private RenderTexture target;
    private Texture2D pixels;
    private string capture;
    private readonly StringBuilder metrics = new StringBuilder("candidate,facing,phase,blade_angle_deg,socket_y,tip_y,blade_screen_length,blade_x,blade_z,tip_screen_x,tip_screen_y,socket_screen_x,socket_screen_y\n");
    private QusapContinuousAvi video;
    private StreamWriter frameLog;
    private string segment;
    private float recordStart;
    private SkinnedMeshRenderer skin;
    private int errors,warnings;
    private readonly StringBuilder console=new StringBuilder();
    private Transform[] claws;
    private Quaternion[] approvedGrip;
    private Vector3 approvedSocketPosition;
    private Quaternion approvedSocketRotation;
    private float maxGripAngle,maxSocketPositionDelta,maxSocketAngle;

    private IEnumerator Start()
    {
        Directory.CreateDirectory(Output);
        Application.logMessageReceived+=Log;
        System.Threading.Thread.CurrentThread.CurrentCulture = CultureInfo.InvariantCulture;
        var player = Object.FindObjectsByType<GameObject>().First(o=>o.name.Contains("Player1_Qusap75K_WeaponGripV1_Test"));
        var original = player.GetComponentsInChildren<Animator>(true).First(a=>a.name=="Qusap75K_Visual");
        var clone = Instantiate(original.gameObject); actor=clone.transform;
        actor.SetPositionAndRotation(original.transform.position, original.transform.rotation);actor.localScale=original.transform.lossyScale;
        player.SetActive(false);
        foreach(var t in clone.GetComponentsInChildren<Transform>(true))t.gameObject.layer=30;
        animator=clone.GetComponent<Animator>();animator.applyRootMotion=false;animator.cullingMode=AnimatorCullingMode.AlwaysAnimate;
        if(!final&&!preview&&!author)
        {
            var gate=animator.GetComponent<QusapSwordCarryLayerGate>();if(gate)gate.enabled=false;
            int layer=animator.GetLayerIndex("SwordCarryUpperBody");if(layer>=0)animator.SetLayerWeight(layer,0);
        }
        foreach(var a in clone.GetComponentsInChildren<Animator>(true))if(a!=animator)a.enabled=false;
        hand=animator.GetBoneTransform(HumanBodyBones.RightHand);socket=hand.Find("WeaponGripSocket");
        approvedSocketPosition=socket.localPosition;approvedSocketRotation=socket.localRotation;
        claws=hand.GetComponentsInChildren<Transform>(true).Where(t=>t.name.StartsWith("claw_r_")).OrderBy(t=>t.name).ToArray();
#if UNITY_EDITOR
        var sourceGrip=AssetDatabase.LoadAssetAtPath<AnimationClip>("Assets/_Qusap/Tests/NewQusap75KAnimationTest/WeaponGripV1/Animation/Qusap75K_SwordGrip.anim");
        approvedGrip=claws.Select(t=>{
            var path=AnimationUtility.CalculateTransformPath(t,hand);
            float Value(string axis)=>AnimationUtility.GetEditorCurve(sourceGrip,EditorCurveBinding.FloatCurve(path,typeof(Transform),"m_LocalRotation."+axis)).Evaluate(0);
            return new Quaternion(Value("x"),Value("y"),Value("z"),Value("w"));
        }).ToArray();
#endif
        skin=clone.GetComponentInChildren<SkinnedMeshRenderer>();skin.updateWhenOffscreen=true;
        camera=new GameObject("FixedGameSideCamera").AddComponent<Camera>();camera.orthographic=true;camera.orthographicSize=2;
        camera.cullingMask=1<<30;camera.clearFlags=CameraClearFlags.SolidColor;camera.backgroundColor=new Color(.12f,.13f,.14f);
        camera.transform.SetPositionAndRotation(actor.position+new Vector3(0,1.05f,-10),Quaternion.identity);
        target=new RenderTexture(1280,720,24);target.Create();camera.targetTexture=target;pixels=new Texture2D(1280,720,TextureFormat.RGB24,false);
        RenderPipelineManager.endCameraRendering+=Rendered;Time.captureFramerate=30;
#if UNITY_EDITOR
        if(author)
        {
            actor.rotation=Quaternion.Euler(0,60,0);animator.Play("CombatIdle_B1",0,0);yield return new WaitForSeconds(.5f);
            System.Type.GetType("QusapSwordCarryBuilder, Assembly-CSharp-Editor").GetMethod("AuthorPose").Invoke(null,new object[]{animator});
            preview=true;
        }
#endif
        if(preview)
        {
            foreach(var left in new[]{false,true})foreach(var state in new[]{"CombatIdle_B1","RunForward"})
            {
                actor.rotation=Quaternion.Euler(0,left?-60:60,0);animator.Play(state,0,0);yield return new WaitForSeconds(.5f);
                capture="PREVIEW_"+state+"_"+(left?"left":"right");Measure(state,left);ExportGeometry(capture);yield return null;
            }
            File.WriteAllText(Path.Combine(Output,"preview_metrics.csv"),metrics.ToString());
#if UNITY_EDITOR
            EditorApplication.Exit(0);
#endif
            yield break;
        }
        if(final){yield return Final();yield break;}
#if UNITY_EDITOR
        var controller=animator.runtimeAnimatorController;
        var idle=controller.animationClips.First(c=>c.name=="1Hand_Base_Stand_Idle_B_1");
        var paths=AssetDatabase.FindAssets("t:Model",new[]{"Assets/_Qusap/Tests/NewQusap75KAnimationTest/Imported/DoubleL"}).Select(AssetDatabase.GUIDToAssetPath)
            .Where(p=>p.Contains("/Movement/")&&!p.Contains("Turn_")).OrderBy(p=>p).ToArray();
        var sources=new StringBuilder();
        foreach(var path in paths)
        {
            var clip=AssetDatabase.LoadAllAssetsAtPath(path).OfType<AnimationClip>().First(c=>!c.name.StartsWith("__"));
            sources.AppendLine(clip.name+" | "+path);
            var replacement=new AnimatorOverrideController(controller);replacement[idle]=clip;animator.runtimeAnimatorController=replacement;
            foreach(var left in new[]{false,true})
            {
                actor.rotation=Quaternion.Euler(0,left?-60:60,0);animator.Play("CombatIdle_B1",0,0);
                for(int phase=0;phase<4;phase++)
                {
                    yield return new WaitForSeconds(clip.length/4);
                    capture="PROBE_"+clip.name+"_"+(left?"left":"right")+"_"+phase;
                    Measure(clip.name,left);yield return null;
                }
            }
        }
        File.WriteAllText(Path.Combine(Output,"DoubleL_available.txt"),sources.ToString());
#endif
        File.WriteAllText(Path.Combine(Output,"DoubleL_probe.csv"),metrics.ToString());
        RenderPipelineManager.endCameraRendering-=Rendered;Time.captureFramerate=0;
#if UNITY_EDITOR
        EditorApplication.Exit(0);
#endif
    }
    private void Measure(string candidate,bool left)
    {
        var filter=socket.GetComponentInChildren<MeshFilter>();
        var tip=filter.transform.TransformPoint(filter.sharedMesh.vertices.OrderByDescending(v=>v.y).First());
        var direction=(tip-socket.position).normalized;
        var screenA=camera.WorldToViewportPoint(socket.position);var screenB=camera.WorldToViewportPoint(tip);
        metrics.AppendLine($"{candidate},{(left?"left":"right")},{animator.GetCurrentAnimatorStateInfo(0).normalizedTime:F6},{Mathf.Asin(direction.y)*Mathf.Rad2Deg:F3},{socket.position.y:F4},{tip.y:F4},{Vector2.Distance(screenA,screenB):F4},{direction.x:F4},{direction.z:F4},{screenB.x:F4},{screenB.y:F4},{screenA.x:F4},{screenA.y:F4}");
    }
    private void Rendered(ScriptableRenderContext context,Camera rendered)
    {
        if(rendered!=camera||(capture==null&&video==null))return;
        var previous=RenderTexture.active;RenderTexture.active=target;pixels.ReadPixels(new Rect(0,0,1280,720),0,0);pixels.Apply();RenderTexture.active=previous;
        if(capture!=null){File.WriteAllBytes(Path.Combine(Output,capture+".png"),pixels.EncodeToPNG());capture=null;}
        if(video!=null)
        {
            if(approvedGrip!=null)for(int i=0;i<claws.Length;i++)maxGripAngle=Mathf.Max(maxGripAngle,Quaternion.Angle(claws[i].localRotation,approvedGrip[i]));
            maxSocketPositionDelta=Mathf.Max(maxSocketPositionDelta,Vector3.Distance(socket.localPosition,approvedSocketPosition));
            maxSocketAngle=Mathf.Max(maxSocketAngle,Quaternion.Angle(socket.localRotation,approvedSocketRotation));
            video.Add(pixels.EncodeToJPG(90));
            var filter=socket.GetComponentInChildren<MeshFilter>();var verts=filter.sharedMesh.vertices;
            var tip=filter.transform.TransformPoint(verts.OrderByDescending(v=>v.y).First());
            var direction=(tip-socket.position).normalized;
            float angle=Mathf.Asin(direction.y)*Mathf.Rad2Deg;
            frameLog.WriteLine($"{video.Frames-1},{Time.frameCount},{Time.time-recordStart:F6},{segment},{animator.GetCurrentAnimatorStateInfo(0).normalizedTime:F6},{animator.GetLayerWeight(animator.GetLayerIndex("SwordCarryUpperBody")):F6},{angle:F3},{socket.position.y:F6},{tip.y:F6},{socket.GetComponentsInChildren<MeshRenderer>().Min(r=>r.bounds.min.y):F6},{Vector3.Distance(socket.position,socket.Find("SwordVisual").position):F6},{hand.localRotation.x:F6},{hand.localRotation.y:F6},{hand.localRotation.z:F6},{hand.localRotation.w:F6}");
            ExportGeometry("FRAME_"+video.Frames.ToString("D4"));
        }
    }
    private IEnumerator Final()
    {
        actor.rotation=Quaternion.Euler(0,60,0);animator.Play("CombatIdle_B1",0,0);yield return new WaitForSeconds(.5f);
        // Independent complete Walk cycles, and direct carry-off comparison across the full Roll.
        var checks=new StringBuilder("state,facing,phase,carry_weight,max_roll_bone_delta_m,max_roll_rotation_delta_deg\n");
        var reference=Instantiate(actor.gameObject);reference.name="CarryOffReference";
        foreach(var renderer in reference.GetComponentsInChildren<Renderer>())renderer.enabled=false;
        var referenceAnimator=reference.GetComponent<Animator>();referenceAnimator.GetComponent<QusapSwordCarryLayerGate>().enabled=false;
        referenceAnimator.SetLayerWeight(referenceAnimator.GetLayerIndex("SwordCarryUpperBody"),0);
        var humanoidBones=new[]{HumanBodyBones.Hips,HumanBodyBones.RightShoulder,HumanBodyBones.RightUpperArm,HumanBodyBones.RightLowerArm,HumanBodyBones.RightHand,HumanBodyBones.LeftUpperLeg,HumanBodyBones.RightUpperLeg};
        foreach(var state in new[]{"WalkForward","WalkBackward","RunBackward","RollRMFrontInPlace"})
        foreach(var left in new[]{false,true})
        {
            actor.rotation=Quaternion.Euler(0,left?-60:60,0);reference.transform.SetPositionAndRotation(actor.position,actor.rotation);
            // Verification only, before recording: evaluate the initial Roll pose on both
            // Animators rather than comparing transforms left over from the previous state.
            if(state=="RollRMFrontInPlace")animator.SetLayerWeight(animator.GetLayerIndex("SwordCarryUpperBody"),0);
            animator.Play(state,0,0);referenceAnimator.Play(state,0,0);
            if(state=="RollRMFrontInPlace"){animator.Update(0);referenceAnimator.Update(0);}
            yield return null;yield return null;
            float length=animator.GetCurrentAnimatorClipInfo(0)[0].clip.length;
            int count=Mathf.CeilToInt(length*30);
            for(int i=0;i<count;i++)
            {
                float position=0,rotation=0;
                if(state=="RollRMFrontInPlace")foreach(var bone in humanoidBones)
                {
                    var a=animator.GetBoneTransform(bone);var b=referenceAnimator.GetBoneTransform(bone);
                    position=Mathf.Max(position,Vector3.Distance(a.position,b.position));rotation=Mathf.Max(rotation,Quaternion.Angle(a.rotation,b.rotation));
                }
                checks.AppendLine($"{state},{(left?"left":"right")},{animator.GetCurrentAnimatorStateInfo(0).normalizedTime:F6},{animator.GetLayerWeight(animator.GetLayerIndex("SwordCarryUpperBody")):F6},{position:F8},{rotation:F6}");
                if(i==count/2){capture="CHECK_"+state+"_"+(left?"left":"right");ExportGeometry(capture);}
                yield return null;
            }
        }
        Destroy(reference);File.WriteAllText(Path.Combine(Output,"complete_state_checks.csv"),checks.ToString());
        actor.rotation=Quaternion.Euler(0,60,0);animator.Play("CombatIdle_B1",0,0);yield return new WaitForSeconds(.5f);
        video=new QusapContinuousAvi(Path.Combine(Output,"Qusap_SwordCarry_CONTINUOUS.avi"),1280,720,30);
        frameLog=new StreamWriter(Path.Combine(Output,"continuous_frame_log.csv"));
        frameLog.WriteLine("video_frame,unity_frame,time_s,segment,phase,carry_weight,blade_angle_deg,guard_y,tip_y,sword_min_y,socket_pivot_m,hand_qx,hand_qy,hand_qz,hand_qw");
        recordStart=Time.time;
        segment="Idle derecha";capture="SIDE_Idle_right";yield return new WaitForSeconds(2);
        segment="Run derecha";animator.CrossFadeInFixedTime("RunForward",.18f,0);yield return new WaitForSeconds(1);capture="SIDE_Run_right";yield return new WaitForSeconds(1);
        segment="Idle izquierda";actor.rotation=Quaternion.Euler(0,-60,0);animator.CrossFadeInFixedTime("CombatIdle_B1",.18f,0);yield return new WaitForSeconds(1);capture="SIDE_Idle_left";yield return new WaitForSeconds(1);
        segment="Run izquierda";animator.CrossFadeInFixedTime("RunForward",.18f,0);yield return new WaitForSeconds(1);capture="SIDE_Run_left";yield return new WaitForSeconds(1);
        float rollLength=animator.runtimeAnimatorController.animationClips.First(c=>c.name=="RM_Roll_front").length;
        segment="Transicion Run a Roll";animator.CrossFadeInFixedTime("RollRMFrontInPlace",.22f,0);yield return new WaitForSeconds(.22f);
        segment="Roll normal";yield return new WaitForSeconds(rollLength-.22f);
        segment="Regreso Idle";animator.CrossFadeInFixedTime("CombatIdle_B1",.22f,0);yield return new WaitForSeconds(1);
        segment="Transicion Idle a Roll 0.25x";animator.speed=.25f;animator.CrossFadeInFixedTime("RollRMFrontInPlace",.12f,0);yield return new WaitForSeconds(.12f*4);
        segment="Roll 0.25x";yield return new WaitForSeconds((rollLength-.12f)*4);
        segment="Regreso a Idle despues del Roll";animator.speed=1;animator.CrossFadeInFixedTime("CombatIdle_B1",.22f,0);yield return new WaitForSeconds(2);
        segment="Acercamiento frontal del agarre";
        actor.rotation=Quaternion.Euler(0,60,0);yield return new WaitForSeconds(.3f);
        camera.orthographicSize=.35f;camera.transform.position=socket.position+actor.forward*5;camera.transform.rotation=Quaternion.LookRotation(-actor.forward,Vector3.up);
        capture="GRIP_front";yield return new WaitForSeconds(2);
        segment="Acercamiento perfil del agarre";camera.transform.position=socket.position+actor.right*5;camera.transform.rotation=Quaternion.LookRotation(-actor.right,Vector3.up);
        capture="GRIP_profile";yield return new WaitForSeconds(2);
        int countFrames=video.Frames;video.Dispose();video=null;frameLog.Dispose();
        RenderPipelineManager.endCameraRendering-=Rendered;Time.captureFramerate=0;
        console.AppendLine($"errors={errors}\nwarnings={warnings}\nrecorded_frames={countFrames}\nfps=30\nduration_seconds={countFrames/30f:F3}");
        console.AppendLine($"applyRootMotion={animator.applyRootMotion}\nmax_grip_rotation_delta_deg={maxGripAngle:F6}\nmax_socket_local_position_delta_m={maxSocketPositionDelta:F8}\nmax_socket_rotation_delta_deg={maxSocketAngle:F6}");
        File.WriteAllText(Path.Combine(Output,"Console.txt"),console.ToString());
        Application.logMessageReceived-=Log;
#if UNITY_EDITOR
        if(Application.isBatchMode)EditorApplication.Exit(errors==0?0:2);else EditorApplication.ExitPlaymode();
#endif
    }
    private void Log(string message,string stack,LogType type)
    {
        if(type==LogType.Error||type==LogType.Exception||type==LogType.Assert){errors++;console.AppendLine(type+": "+message+"\n"+stack);}
        if(type==LogType.Warning){warnings++;console.AppendLine("Warning: "+message);}
    }
    private void ExportGeometry(string name)
    {
        var folder=Path.Combine(Output,"collision_geometry");Directory.CreateDirectory(folder);
        var baked=new Mesh();skin.BakeMesh(baked,true);var positions=baked.vertices;var weights=skin.sharedMesh.boneWeights;
        var bones=skin.bones;
        bool IsHand(int i)=>bones[i].name=="hand_r"||bones[i].name.StartsWith("claw_r_");
        bool[] handVertices=weights.Select(w=>(IsHand(w.boneIndex0)?w.weight0:0)+(IsHand(w.boneIndex1)?w.weight1:0)+(IsHand(w.boneIndex2)?w.weight2:0)+(IsHand(w.boneIndex3)?w.weight3:0)>.45f).ToArray();
        var triangles=skin.sharedMesh.triangles;
        var body=Enumerable.Range(0,triangles.Length/3).Where(i=>!handVertices[triangles[i*3]]&&!handVertices[triangles[i*3+1]]&&!handVertices[triangles[i*3+2]]).SelectMany(i=>new[]{triangles[i*3],triangles[i*3+1],triangles[i*3+2]}).ToArray();
        var filter=socket.GetComponentInChildren<MeshFilter>();var vertices=filter.sharedMesh.vertices;var tris=filter.sharedMesh.triangles;
        var skinMatrix=skin.transform.localToWorldMatrix;var swordMatrix=filter.transform.localToWorldMatrix;
        var blade=Enumerable.Range(0,tris.Length/3).Where(i=>vertices[tris[i*3]].y>.13f&&vertices[tris[i*3+1]].y>.13f&&vertices[tris[i*3+2]].y>.13f).SelectMany(i=>new[]{tris[i*3],tris[i*3+1],tris[i*3+2]}).ToArray();
        using(var writer=new BinaryWriter(File.Create(Path.Combine(folder,name+".bin"))))
        {
            writer.Write(positions.Length);foreach(var v in positions){var p=skinMatrix.MultiplyPoint3x4(v);writer.Write(p.x);writer.Write(p.y);writer.Write(p.z);}
            writer.Write(body.Length);foreach(var i in body)writer.Write(i);
            writer.Write(vertices.Length);foreach(var v in vertices){var p=swordMatrix.MultiplyPoint3x4(v);writer.Write(p.x);writer.Write(p.y);writer.Write(p.z);}
            writer.Write(blade.Length);foreach(var i in blade)writer.Write(i);
        }
        Destroy(baked);
    }
}
