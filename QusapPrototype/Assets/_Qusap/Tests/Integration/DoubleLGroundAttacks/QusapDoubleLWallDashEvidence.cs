#if UNITY_EDITOR
using System;
using System.Collections;
using System.IO;
using System.Linq;
using System.Reflection;
using Qusap;
using UnityEditor;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.Rendering;

[DefaultExecutionOrder(250)]
public sealed partial class QusapDoubleLWallDashEvidence:MonoBehaviour
{
    public bool preview;
    const string Output=@"C:\Dev\Qusap_ArtSource_Recovered\DoubleL_WallDash";
    StreamWriter console;int errors,warnings;Camera camera;RenderTexture rt;Texture2D pixels;
    QusapDoubleLGroundAttackPresenter player;string imagePath;bool imageReady;
    [Serializable]class Candidate{public string name,path,state;public float length;public bool loop,human;}
    [Serializable]class Inventory{public Candidate[] clips;}
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
    static void Devices()
    {
        if(!Environment.GetCommandLineArgs().Any(a=>a=="QusapDoubleLWallDashBuilder.Preview"||a=="QusapDoubleLWallDashBuilder.Gameplay"))return;
        var settings=Instantiate(InputSystem.settings);settings.hideFlags=HideFlags.HideAndDontSave;
        settings.editorInputBehaviorInPlayMode=InputSettings.EditorInputBehaviorInPlayMode.AllDeviceInputAlwaysGoesToGameView;
        settings.backgroundBehavior=InputSettings.BackgroundBehavior.IgnoreFocus;InputSystem.settings=settings;
        if(Keyboard.current==null)InputSystem.AddDevice<Keyboard>();if(Gamepad.all.Count==0)InputSystem.AddDevice<Gamepad>();
    }
    void Awake(){Application.runInBackground=true;Directory.CreateDirectory(Output);console=new StreamWriter(Output+(preview?"/Console_Preview_Runtime.txt":"/Console_Gameplay_Runtime.txt")){AutoFlush=true};Application.logMessageReceived+=Log;}
    void Log(string message,string stack,LogType type)
    {
        console.WriteLine($"[{type}] {message}\n{stack}");
        if(type==LogType.Error||type==LogType.Exception||type==LogType.Assert){errors++;EditorApplication.delayCall+=()=>EditorApplication.Exit(2);}
        if(type==LogType.Warning)warnings++;
    }
    void Check(bool condition,string message){if(!condition)throw new InvalidOperationException("WALL_DASH_EVIDENCE_STOP: "+message);}
    IEnumerator Start()
    {
        yield return new WaitForSeconds(.5f);
        player=FindObjectsByType<QusapDoubleLGroundAttackPresenter>().Single(p=>p.GetComponent<QusapInputReader>().LocalPlayerSlot==QusapLocalPlayerSlot.Player2Gamepad);
        camera=Camera.main;int w=preview?640:1280,h=preview?640:720;
        rt=new RenderTexture(w,h,24);rt.Create();camera.targetTexture=rt;pixels=new Texture2D(w,h,TextureFormat.RGB24,false);
        RenderPipelineManager.endCameraRendering+=Rendered;
        if(preview)yield return Preview();else yield return Gameplay();
        Debug.Log((preview?"WALL_DASH_PREVIEW_PASS":"WALL_DASH_GAMEPLAY_PASS")+" errors="+errors+" warnings="+warnings);
        EditorApplication.delayCall+=()=>EditorApplication.Exit(errors==0&&warnings==0?0:2);
    }
    IEnumerator Preview()
    {
        Directory.CreateDirectory(Output+"/Pose_Frames");
        foreach(var p in FindObjectsByType<QusapDoubleLGroundAttackPresenter>())if(p!=player)p.gameObject.SetActive(false);
        foreach(var b in player.GetComponents<MonoBehaviour>())b.enabled=false;
        player.GetComponent<Rigidbody>().isKinematic=true;player.transform.position=new Vector3(0,1,0);
        foreach(var b in camera.GetComponents<MonoBehaviour>())if(b is QusapSharedCombatCamera||b is QusapCameraFollow)b.enabled=false;
        camera.orthographic=true;camera.orthographicSize=1.35f;camera.transform.position=new Vector3(0,1.1f,-14);camera.transform.rotation=Quaternion.identity;
        var animator=player.Animator;
        var controller=(UnityEditor.Animations.AnimatorController)animator.runtimeAnimatorController;
        var names=new[]{"1Hand_Base_Jump_Air_Loop","1Hand_Base_Jump_Start_InPlace","1Hand_Base_Jump_Start_F_InPlace","1Hand_Base_Jump_B_InPlace","1Hand_Base_Sprint_A_F_InPlace","1Hand_Base_Sprint_A_Idle_To_F_InPlace","1Hand_Base_Sprint_B_F_InPlace","1Hand_Base_Sprint_B_Idle_To_F_InPlace","1Hand_Base_Attack_Dash_1_InPlace","1Hand_Base_Jump_Attack_1_Air_Loop","1Hand_Base_Jump_Attack_2_Air_Loop"};
        var candidates=controller.layers[0].stateMachine.states.Select(s=>new{state=s.state,clip=s.state.motion as AnimationClip})
            .Where(s=>s.clip&&names.Contains(s.clip.name)&&AssetDatabase.GetAssetPath(s.clip).Contains("PurchasedAnimationsV111/"))
            .GroupBy(s=>s.clip.name).Select(g=>g.First()).OrderBy(s=>s.clip.name).ToArray();
        Check(candidates.Length==names.Length,"a purchased candidate is missing");
        File.WriteAllText(Output+"/Inventory_Reviewed.json",JsonUtility.ToJson(new Inventory{clips=candidates.Select(s=>new Candidate{name=s.clip.name,path=AssetDatabase.GetAssetPath(s.clip),state=s.state.name,length=s.clip.length,loop=s.clip.isLooping,human=s.clip.humanMotion}).ToArray()},true));
        var pose=typeof(QusapDoubleLGroundAttackPresenter).GetMethod("ApplyApprovedPose",BindingFlags.Instance|BindingFlags.NonPublic);
        var facingField=typeof(QusapDoubleLGroundAttackPresenter).GetField("direction",BindingFlags.Instance|BindingFlags.NonPublic);
        foreach(var candidate in candidates)foreach(int side in new[]{1,-1})for(int sample=0;sample<=30;sample++)
        {
            if(Environment.GetCommandLineArgs().Contains("-wallDashSprintBOnly")&&!candidate.clip.name.StartsWith("1Hand_Base_Sprint_B_"))continue;
            facingField.SetValue(player,side);animator.Play(candidate.state.name,0,Mathf.Min(.9999f,sample/30f));animator.Update(0);pose.Invoke(player,null);
            imagePath=Output+"/Pose_Frames/"+candidate.clip.name+"_"+(side>0?"R":"L")+"_"+sample.ToString("D2")+".png";imageReady=false;
            while(!imageReady)yield return null;imagePath=null;
        }
    }
    void Rendered(ScriptableRenderContext context,Camera c)
    {
        if(c!=camera)return;
        if(preview&&imagePath!=null&&!imageReady)
        {
            var old=RenderTexture.active;RenderTexture.active=rt;pixels.ReadPixels(new Rect(0,0,640,640),0,0);pixels.Apply();RenderTexture.active=old;
            File.WriteAllBytes(imagePath,pixels.EncodeToPNG());imageReady=true;
        }
        else if(!preview)CaptureFrame();
    }
    void OnDestroy(){RenderPipelineManager.endCameraRendering-=Rendered;DisposeCapture();Application.logMessageReceived-=Log;console?.Dispose();}
}
#endif
