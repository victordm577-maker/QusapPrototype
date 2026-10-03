#if UNITY_EDITOR
using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using Qusap;
using UnityEditor;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;
using UnityEngine.Rendering;

[DefaultExecutionOrder(250)]
public sealed class QusapDoubleLAirEvidence:MonoBehaviour
{
    public bool preview;
    const string Output=@"C:\Dev\Qusap_ArtSource_Recovered\DoubleL_AirLocomotion";
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
    static void Devices()
    {
        if(!Environment.GetCommandLineArgs().Any(a=>a=="QusapDoubleLAirBuilder.Preview"||a=="QusapDoubleLAirBuilder.Gameplay"))return;
        var settings=Instantiate(InputSystem.settings);settings.hideFlags=HideFlags.HideAndDontSave;
        settings.editorInputBehaviorInPlayMode=InputSettings.EditorInputBehaviorInPlayMode.AllDeviceInputAlwaysGoesToGameView;
        settings.backgroundBehavior=InputSettings.BackgroundBehavior.IgnoreFocus;InputSystem.settings=settings;
        if(Keyboard.current==null)InputSystem.AddDevice<Keyboard>();if(Gamepad.all.Count==0)InputSystem.AddDevice<Gamepad>();
    }
    [Serializable]class Candidate{public string name,path,state;public float length;public bool loop,human;}
    [Serializable]class Inventory{public Candidate[] clips;}
    StreamWriter console;int errors,warnings;Camera camera;RenderTexture rt;Texture2D pixels;
    QusapDoubleLGroundAttackPresenter player;string imagePath;bool imageReady;
    Rigidbody body;QusapGroundSensor ground;QusapCombatController combat;QusapDashMotor dash;
    Gamepad pad;float move,started;bool heldJump,heldDash,heldY,heldB,recording;
    QusapContinuousAvi video;StreamWriter frames;string action="Idle";UnityEngine.UI.Text label;
    float fullHeight,shortHeight,startHeight,maxHeight;uint lastLanding;int softLandings,hardLandings,dashBlocked;bool trackHeight;
    readonly HashSet<string> observedStates=new HashSet<string>();
    [Serializable]class Frame{public int frame,facing;public float time,x,y,vx,vy,impact;public string action,state;public bool grounded,dash,rootMotion;}
    [Serializable]class Result{public int errors,warnings,frames,softLandings,hardLandings,dashBlocked;public float seconds,fullJumpHeight,shortJumpHeight;public string scene,prefab;}
    void Awake(){Application.runInBackground=true;Directory.CreateDirectory(Output);console=new StreamWriter(Output+(preview?"/Console_Preview_Runtime.txt":"/Console_Gameplay_Runtime.txt")){AutoFlush=true};Application.logMessageReceived+=Log;}
    void Log(string message,string stack,LogType type){console.WriteLine($"[{type}] {message}\n{stack}");if(type==LogType.Error||type==LogType.Exception||type==LogType.Assert){errors++;EditorApplication.delayCall+=()=>EditorApplication.Exit(2);}if(type==LogType.Warning)warnings++;}
    IEnumerator Start()
    {
        yield return new WaitForSeconds(.5f);
        player=FindObjectsByType<QusapDoubleLGroundAttackPresenter>().Single(p=>p.GetComponent<QusapInputReader>().LocalPlayerSlot==QusapLocalPlayerSlot.Player2Gamepad);
        camera=Camera.main;int width=preview?640:1280,height=preview?640:720;rt=new RenderTexture(width,height,24);rt.Create();camera.targetTexture=rt;pixels=new Texture2D(width,height,TextureFormat.RGB24,false);
        RenderPipelineManager.endCameraRendering+=Rendered;
        if(preview)yield return Preview();
        else yield return Gameplay();
        Debug.Log((preview?"AIR_PREVIEW_PASS":"AIR_GAMEPLAY_PASS")+" errors="+errors+" warnings="+warnings);
        EditorApplication.delayCall+=()=>EditorApplication.Exit(errors==0?0:2);
    }
    void Check(bool condition,string message){if(!condition)throw new InvalidOperationException("AIR_EVIDENCE_STOP: "+message);}
    void Queue()
    {
        var state=new GamepadState{leftStick=new Vector2(move,0)};
        if(heldJump)state=state.WithButton(GamepadButton.South);
        if(heldDash)state=state.WithButton(GamepadButton.RightShoulder);
        if(heldY)state=state.WithButton(GamepadButton.North);
        if(heldB)state=state.WithButton(GamepadButton.East);
        InputSystem.QueueStateEvent(pad,state);
    }
    void Move(float value){move=value;Queue();}
    IEnumerator Face(int side){Move(side);yield return new WaitForSeconds(.06f);Move(0);yield return new WaitForSeconds(.15f);}
    IEnumerator Grounded()
    {
        float deadline=Time.time+2.5f;yield return new WaitForSeconds(.06f);
        while((!ground.IsGrounded||body.linearVelocity.y>0)&&Time.time<deadline)yield return null;
        Check(ground.IsGrounded&&body.linearVelocity.y<=0,"native landing did not occur");
        yield return new WaitForSeconds(.40f);
    }
    IEnumerator Jump(float hold,float moveFor=0)
    {
        startHeight=body.position.y;maxHeight=startHeight;trackHeight=true;
        heldJump=true;Queue();
        if(moveFor>0){yield return new WaitForSeconds(moveFor);Move(0);hold-=moveFor;}
        yield return new WaitForSeconds(Mathf.Max(.01f,hold));heldJump=false;Queue();
        yield return Grounded();trackHeight=false;
    }
    IEnumerator Gameplay()
    {
        body=player.GetComponent<Rigidbody>();ground=player.GetComponent<QusapGroundSensor>();combat=player.GetComponent<QusapCombatController>();dash=player.GetComponent<QusapDashMotor>();pad=Gamepad.all[0];
        foreach(var b in camera.GetComponents<MonoBehaviour>())if(b is QusapSharedCombatCamera||b is QusapCameraFollow)b.enabled=false;
        camera.orthographic=true;camera.orthographicSize=3.05f;
        var canvasObject=new GameObject("Evidence_Label",typeof(Canvas),typeof(UnityEngine.UI.CanvasScaler));
        var canvas=canvasObject.GetComponent<Canvas>();canvas.renderMode=RenderMode.ScreenSpaceCamera;canvas.worldCamera=camera;canvas.planeDistance=.5f;
        var scaler=canvasObject.GetComponent<UnityEngine.UI.CanvasScaler>();scaler.uiScaleMode=UnityEngine.UI.CanvasScaler.ScaleMode.ScaleWithScreenSize;scaler.referenceResolution=new Vector2(1280,720);
        var textObject=new GameObject("Action",typeof(RectTransform),typeof(UnityEngine.UI.Text));textObject.transform.SetParent(canvasObject.transform,false);
        label=textObject.GetComponent<UnityEngine.UI.Text>();label.font=Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");label.fontSize=23;label.color=Color.white;label.alignment=TextAnchor.UpperCenter;
        var rect=label.rectTransform;rect.anchorMin=new Vector2(0,1);rect.anchorMax=new Vector2(1,1);rect.pivot=new Vector2(.5f,1);rect.anchoredPosition=new Vector2(0,-14);rect.sizeDelta=new Vector2(0,60);
        yield return Grounded();
        // The real gamepad spawn is under the right platform. Walk out using
        // its existing input binding before recording the stationary jumps.
        Move(-1);float approachDeadline=Time.time+2;
        while(body.position.x>2.4f&&Time.time<approachDeadline)yield return null;
        Move(0);yield return Grounded();
        QualitySettings.vSyncCount=0;Application.targetFrameRate=90;started=Time.time;
        video=new QusapContinuousAvi(Output+"/Gameplay_Air_Raw.avi",1280,720,60);frames=new StreamWriter(Output+"/Gameplay_Frames.jsonl"){AutoFlush=true};recording=true;
        action="Salto quieto / derecha";yield return Face(1);yield return Jump(.50f);fullHeight=maxHeight-startHeight;
        action="Salto quieto / izquierda";yield return Face(-1);yield return Jump(.50f);
        action="Salto corto / jump cut / A pulsado y liberado";yield return Jump(.06f);shortHeight=maxHeight-startHeight;
        Check(fullHeight>2.1f&&shortHeight<fullHeight*.7f,"jump cut did not reduce the native jump height");
        action="Salto en movimiento / derecha / plataforma";Move(1);yield return new WaitForSeconds(.08f);yield return Jump(.50f,.17f);
        Check(body.position.y>2.8f,"native jump did not reach right platform");
        action="Salto con reversa / izquierda / aterrizaje fuerte";Move(-1);yield return new WaitForSeconds(.02f);yield return Jump(.50f,.11f);
        action="Subida por salto real a plataforma";Move(1);yield return new WaitForSeconds(.03f);yield return Jump(.50f,.18f);
        Check(body.position.y>2.8f,"second native platform climb failed");
        action="Caida desde plataforma / aterrizaje suave";Move(-1);
        float deadline=Time.time+1.2f;while(body.position.x>2.75f&&Time.time<deadline)yield return null;
        Move(0);yield return Grounded();
        action="Dash aereo / Y y B bloqueados";yield return Face(1);heldJump=true;Queue();yield return new WaitForSeconds(.15f);
        Move(1);heldDash=true;Queue();yield return new WaitForSeconds(.04f);heldDash=false;Queue();
        Check(dash.IsDashing&&!ground.IsGrounded,"existing air dash did not start");
        heldY=true;Queue();yield return new WaitForSeconds(.025f);heldY=false;Queue();
        Check(!combat.IsAttacking&&dash.IsDashing,"Y started an attack during dash");dashBlocked++;
        heldB=true;Queue();yield return new WaitForSeconds(.025f);heldB=false;Queue();
        Check(!combat.IsAttacking,"B started an attack during dash");dashBlocked++;
        yield return new WaitForSeconds(.20f);heldJump=false;Move(0);yield return Grounded();
        action="Interrupcion a Run / Idle";Move(-1);yield return new WaitForSeconds(.20f);Move(0);yield return new WaitForSeconds(.30f);
        recording=false;video.Dispose();video=null;frames.Dispose();frames=null;
        var result=new Result{errors=errors,warnings=warnings,frames=frameCount,seconds=Time.time-started,softLandings=softLandings,hardLandings=hardLandings,dashBlocked=dashBlocked,fullJumpHeight=fullHeight,shortJumpHeight=shortHeight,scene=gameObject.scene.path,prefab="Assets/_Qusap/Prefabs/QusapCombatPlayer.prefab"};
        File.WriteAllText(Output+"/Gameplay_Result.json",JsonUtility.ToJson(result,true));
        Check(result.seconds<=30&&softLandings>0&&hardLandings>0&&dashBlocked==2&&errors==0,"mandatory gameplay case missing or capture exceeds 30s");
        foreach(string state in new[]{"JumpTakeoff","JumpRise","Fall","LandSoft","LandHard","RunForward","CombatIdle_B1"})
            Check(observedStates.Contains(state),"required visible state missing: "+state);
    }
    int frameCount;
    void LateUpdate()
    {
        if(preview||!body||!camera)return;
        camera.transform.position=new Vector3(body.position.x,Mathf.Max(2.1f,body.position.y+1.0f),-14);camera.transform.rotation=Quaternion.identity;
        if(trackHeight)maxHeight=Mathf.Max(maxHeight,body.position.y);
        if(label)label.text=action+"\n"+player.PresentationState;
        if(player.LandingSequence!=lastLanding){lastLanding=player.LandingSequence;if(player.LandingImpactSpeed>=QusapDoubleLAirLocomotion.HardLandingSpeed)hardLandings++;else softLandings++;}
    }
    IEnumerator Preview()
    {
        Directory.CreateDirectory(Output+"/Pose_Frames");
        var others=FindObjectsByType<QusapDoubleLGroundAttackPresenter>();
        foreach(var p in others)if(p!=player)p.gameObject.SetActive(false);
        foreach(var b in player.GetComponents<MonoBehaviour>())b.enabled=false;
        player.GetComponent<Rigidbody>().isKinematic=true;
        player.transform.position=new Vector3(0,1,0);
        var animator=player.Animator;
        foreach(var b in camera.GetComponents<MonoBehaviour>())if(b is QusapSharedCombatCamera||b is QusapCameraFollow)b.enabled=false;
        camera.orthographic=true;camera.orthographicSize=1.35f;camera.transform.position=new Vector3(0,1.1f,-14);camera.transform.rotation=Quaternion.identity;
        var controller=(UnityEditor.Animations.AnimatorController)animator.runtimeAnimatorController;
        var states=controller.layers[0].stateMachine.states;
        var candidates=states.Select(s=>new{state=s.state,clip=s.state.motion as AnimationClip})
            .Where(s=>s.clip&&s.clip.name.StartsWith("1Hand_Base_Jump_")&&!s.clip.name.Contains("Attack")&&
                (s.clip.name.EndsWith("InPlace")||s.clip.name.EndsWith("Air_Loop")))
            .GroupBy(s=>s.clip.name).Select(g=>g.First()).OrderBy(s=>s.clip.name).ToArray();
        File.WriteAllText(Output+"/Inventory.json",JsonUtility.ToJson(new Inventory{clips=candidates.Select(s=>new Candidate{name=s.clip.name,path=AssetDatabase.GetAssetPath(s.clip),state=s.state.name,length=s.clip.length,loop=s.clip.isLooping,human=s.clip.humanMotion}).ToArray()},true));
        var pose=typeof(QusapDoubleLGroundAttackPresenter).GetMethod("ApplyApprovedPose",BindingFlags.Instance|BindingFlags.NonPublic);
        foreach(var candidate in candidates)for(int sample=0;sample<9;sample++)
        {
            animator.Play(candidate.state.name,0,Mathf.Min(.9999f,sample/8f));animator.Update(0);
            pose.Invoke(player,null);
            imagePath=Output+"/Pose_Frames/"+candidate.clip.name+"_"+sample+".png";imageReady=false;
            while(!imageReady)yield return null;
            imagePath=null;
        }
    }
    void Rendered(ScriptableRenderContext context,Camera c)
    {
        if(c!=camera)return;
        if(preview)
        {
            if(imagePath==null||imageReady)return;
            var old=RenderTexture.active;RenderTexture.active=rt;pixels.ReadPixels(new Rect(0,0,640,640),0,0);pixels.Apply();RenderTexture.active=old;
            File.WriteAllBytes(imagePath,pixels.EncodeToPNG());imageReady=true;
        }
        else if(recording)
        {
            observedStates.Add(player.PresentationState);
            Check(!player.Animator.applyRootMotion,"Root Motion enabled");
            Check(player.Visual.GetComponentsInChildren<MeshFilter>(false).Length==1,"more than one visible sword in main presentation");
            var old=RenderTexture.active;RenderTexture.active=rt;pixels.ReadPixels(new Rect(0,0,1280,720),0,0);pixels.Apply();RenderTexture.active=old;
            video.Add(pixels.EncodeToJPG(88));
            frames.WriteLine(JsonUtility.ToJson(new Frame{frame=frameCount++,time=Time.time-started,x=body.position.x,y=body.position.y,vx=body.linearVelocity.x,vy=body.linearVelocity.y,impact=player.LandingImpactSpeed,action=action,state=player.PresentationState,facing=combat.FacingDirection,grounded=ground.IsGrounded,dash=dash.IsDashing,rootMotion=player.Animator.applyRootMotion}));
        }
    }
    void OnDestroy(){RenderPipelineManager.endCameraRendering-=Rendered;video?.Dispose();frames?.Dispose();Application.logMessageReceived-=Log;console?.Dispose();}
}
#endif
