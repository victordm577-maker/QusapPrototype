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

public sealed partial class QusapDoubleLWallDashEvidence
{
    Rigidbody body;QusapGroundSensor ground;QusapWallSensor wall;QusapVerticalMotor vertical;QusapDashMotor dash;QusapCombatController combat;
    QusapDoubleLGroundAttackPresenter[] allPlayers;Vector3[] scales,swordScales;
    Gamepad pad;float move,started;bool heldJump,heldDash,heldY,heldB,recording;
    QusapContinuousAvi video;StreamWriter frames;UnityEngine.UI.Text label;string action="Idle";int frameCount;
    readonly Dictionary<int,string> stateNames=new Dictionary<int,string>();
    readonly HashSet<string> observedStates=new HashSet<string>();
    readonly List<Case> cases=new List<Case>();
    int rejectedAttacks,wallRecharges;float dashPreVertical;
    [Serializable]class Frame
    {
        public int frame,wallSide,slideSide,visualDirection,inputDirection;public float time,x,y,vx,vy,input;
        public string action,requestedState,animator,wall;public bool grounded,sliding,dashing,gravity,airDash,rootMotion;
    }
    [Serializable]class Case{public string name,state;public int side;public float x,y,vx,vy,elapsed,distance;}
    [Serializable]class Result{public float seconds;public int frames,errors,warnings,rejectedAttacks,wallRecharges;public string[] states;public Case[] cases;}
    T MotorField<T>(string name)=>(T)typeof(QusapDashMotor).GetField(name,BindingFlags.Instance|BindingFlags.NonPublic).GetValue(dash);
    void Queue()
    {
        var state=new GamepadState{leftStick=new Vector2(move,0)};
        if(heldJump)state=state.WithButton(GamepadButton.South);if(heldDash)state=state.WithButton(GamepadButton.RightShoulder);
        if(heldY)state=state.WithButton(GamepadButton.North);if(heldB)state=state.WithButton(GamepadButton.East);
        InputSystem.QueueStateEvent(pad,state);
    }
    void Move(float value){move=value;Queue();}
    void Jump(bool held){heldJump=held;Queue();}
    void Mark(string name,int side,float elapsed=0,float distance=0)
    {cases.Add(new Case{name=name,side=side,state=player.PresentationState,x=body.position.x,y=body.position.y,vx=body.linearVelocity.x,vy=body.linearVelocity.y,elapsed=elapsed,distance=distance});}
    IEnumerator Grounded()
    {
        float deadline=Time.time+3;yield return new WaitForSeconds(.04f);
        while((!ground.IsGrounded||body.linearVelocity.y>0)&&Time.time<deadline)yield return null;
        Check(ground.IsGrounded&&body.linearVelocity.y<=0,"native landing did not occur");yield return new WaitForSeconds(.22f);
    }
    IEnumerator Ready()
    {
        float deadline=Time.time+1.2f;
        while(MotorField<float>("cooldownRemaining")>0&&Time.time<deadline)yield return null;
        Check(MotorField<float>("cooldownRemaining")<=0,"native cooldown never completed");
    }
    IEnumerator Approach(int side)
    {
        Move(side);float deadline=Time.time+4;
        while(side*body.position.x<9.2f&&Time.time<deadline)yield return null;
        Move(0);yield return new WaitForSeconds(.16f);
        Check(ground.IsGrounded&&side*body.position.x<11.45f,"ground approach must stop before wall contact");
    }
    IEnumerator Burst(string name,int side,bool expectGround)
    {
        dashPreVertical=body.linearVelocity.y;float startX=body.position.x,begin=Time.time;
        heldDash=true;Queue();float deadline=Time.time+.12f;
        while(!dash.IsDashing&&Time.time<deadline)yield return null;
        Check(dash.IsDashing,"real RB binding did not start dash: "+name);
        heldDash=false;Queue();
        yield return new WaitForSeconds(.025f);
        Check(!body.useGravity&&Mathf.Abs(body.linearVelocity.y)<.001f,"native dash suspension changed");
        Check(MotorField<float>("dashDirection")==side,"native dash direction differs: "+name);
        Check(Mathf.Abs(body.linearVelocity.x)<.001f||Mathf.Sign(body.linearVelocity.x)==side,"dash velocity reversed: "+name);
        Check(player.PresentationDirection==side,"visual dash direction differs from velocity");
        Check(player.PresentationState==(expectGround?"RunForward":"DashAir"),"wrong dash presentation: "+player.PresentationState);
        heldY=true;Queue();yield return new WaitForSeconds(.025f);heldY=false;Queue();
        Check(dash.IsDashing&&!combat.IsAttacking,"A_1 started during "+name);rejectedAttacks++;
        heldB=true;Queue();yield return new WaitForSeconds(.025f);heldB=false;Queue();
        Check(dash.IsDashing&&!combat.IsAttacking,"A_4 started during "+name);rejectedAttacks++;
        while(dash.IsDashing&&Time.time<begin+.4f)yield return null;
        Check(!dash.IsDashing&&body.useGravity,"native gravity recovery changed");
        Mark(name,side,Time.time-begin,Mathf.Abs(body.position.x-startX));
    }
    IEnumerator WallSequence(int side)
    {
        action="Dash aereo antes de pared / gastar carga / Y+B bloqueados";
        Jump(true);yield return new WaitForSeconds(.35f);Move(side);
        yield return Burst("WallApproachDash",side,false);Jump(false);
        float deadline=Time.time+1;
        while((!vertical.IsWallSliding||!MotorField<bool>("hasAirDash"))&&Time.time<deadline)yield return null;
        Check(vertical.IsWallSliding&&vertical.WallSide==side&&wall.WallSide==side,"no real wall slide");
        Check(!ground.IsGrounded&&MotorField<bool>("hasAirDash"),"first valid wall did not recharge air dash");wallRecharges++;Mark("WallRecharge",side);
        action="Wall Slide / pared "+(side>0?"derecha":"izquierda");
        yield return new WaitForSeconds(.10f);
        Check(player.PresentationDirection==side&&player.PresentationState=="WallSlide","slide did not use real WallSide");Mark("WallSlide",side);
        uint sequence=vertical.WallJumpSequence;
        action="Wall Jump / impulso real y cambio de direccion";
        Move(-side);Jump(true);yield return new WaitForSeconds(.06f);
        Check(vertical.WallJumpSequence>sequence&&body.linearVelocity.x*side<0,"native wall jump did not leave wall");
        Check(player.PresentationState=="WallJump"&&player.PresentationDirection==-side,"wall jump did not face its impulse");Mark("WallJump",side);
        yield return new WaitForSeconds(.08f);Move(side);yield return new WaitForSeconds(.10f);Mark("WallJumpDirectionChange",side);
        yield return Ready();
        Check(!ground.IsGrounded,"wall-jump dash setup already landed");
        action="Wall Jump + dash aereo / direccion real / Y+B bloqueados";
        // A valid wall contact forces the native dash away even with input into
        // it. If the actor is still away from the wall, use the real away input.
        Move(wall.IsTouchingWall?side:-side);yield return Burst("WallJumpDash",-side,false);Jump(false);
        Check(!MotorField<bool>("hasAirDash"),"air dash was not consumed after wall recharge");
        Move(-side);yield return new WaitForSeconds(.05f);Move(0);yield return Grounded();
    }
    IEnumerator GroundDash(int side,bool running)
    {
        action="Dash terrestre / "+(side>0?"derecha":"izquierda")+" / "+(running?"Run":"Idle")+" / Y+B bloqueados";
        yield return Ready();Move(side);yield return new WaitForSeconds(running?.12f:.06f);
        if(!running)
        {
            Move(0);yield return new WaitForSeconds(.20f);float idleDeadline=Time.time+.65f;
            while(player.PresentationState!="CombatIdle_B1"&&Time.time<idleDeadline)yield return null;
            Check(player.PresentationState=="CombatIdle_B1","dash idle setup not idle");
        }
        else Check(player.PresentationState=="RunForward","dash run setup not running");
        yield return Burst(running?"GroundRunDash":"GroundIdleDash",side,true);
        if(running){yield return new WaitForSeconds(.08f);Check(player.PresentationState=="RunForward","dash did not return to Run");}
        Move(0);yield return new WaitForSeconds(.25f);Check(player.PresentationState=="CombatIdle_B1","dash did not return to Idle");
    }
    IEnumerator AirDash(int side,bool descending)
    {
        action="Dash aereo / "+(side>0?"derecha":"izquierda")+" / "+(descending?"caida + aterrizaje":"subida")+" / Y+B bloqueados";
        // Use the real open floor between the left platform and boundary.
        // Approach/braking are ordinary input, not actor repositioning.
        Move(body.position.x>-9?-1:1);float approachDeadline=Time.time+2;
        while(Mathf.Abs(body.position.x+9)>.45f&&Time.time<approachDeadline)yield return null;
        Move(0);yield return new WaitForSeconds(.18f);Check(ground.IsGrounded,"air dash staging must remain on real floor");
        yield return Ready();Move(side);yield return new WaitForSeconds(.05f);Move(0);yield return new WaitForSeconds(.17f);
        float launchY=body.position.y;Jump(true);
        if(descending)
        {
            float deadline=Time.time+1.5f;
            while((body.linearVelocity.y>=-2||body.position.y>launchY+.45f)&&Time.time<deadline&&!ground.IsGrounded)yield return null;
            // First leave the initial contact before testing the descending phase.
            if(body.linearVelocity.y>=0){yield return new WaitForSeconds(.05f);while((body.linearVelocity.y>=-2||body.position.y>launchY+.45f)&&Time.time<deadline&&!ground.IsGrounded)yield return null;}
            Check(!ground.IsGrounded&&body.linearVelocity.y<0,"descending dash setup already landed");
        }
        else{yield return new WaitForSeconds(.14f);Check(body.linearVelocity.y>0&&!ground.IsGrounded,"ascending dash setup not rising");}
        Move(side);yield return Burst(descending?"AirFallDash":"AirRiseDash",side,false);Jump(false);
        Check(!MotorField<bool>("hasAirDash")||ground.IsGrounded,"air dash limit changed");
        Move(-side);yield return new WaitForSeconds(.10f);Move(0);
        yield return Grounded();Check(MotorField<bool>("hasAirDash"),"ground did not recharge air dash");
    }
    IEnumerator Gameplay()
    {
        body=player.GetComponent<Rigidbody>();ground=player.GetComponent<QusapGroundSensor>();wall=player.GetComponent<QusapWallSensor>();vertical=player.GetComponent<QusapVerticalMotor>();dash=player.GetComponent<QusapDashMotor>();combat=player.GetComponent<QusapCombatController>();pad=Gamepad.all[0];
        allPlayers=FindObjectsByType<QusapDoubleLGroundAttackPresenter>();scales=allPlayers.Select(p=>p.Visual.transform.localScale).ToArray();swordScales=allPlayers.Select(p=>p.Facing.SwordVisual.localScale).ToArray();
        foreach(var s in ((UnityEditor.Animations.AnimatorController)player.Animator.runtimeAnimatorController).layers[0].stateMachine.states)stateNames[Animator.StringToHash(s.state.name)]=s.state.name;
        foreach(var b in camera.GetComponents<MonoBehaviour>())if(b is QusapSharedCombatCamera||b is QusapCameraFollow)b.enabled=false;
        camera.orthographic=true;camera.orthographicSize=3.05f;
        var canvasObject=new GameObject("Evidence_Label",typeof(Canvas),typeof(UnityEngine.UI.CanvasScaler));var canvas=canvasObject.GetComponent<Canvas>();canvas.renderMode=RenderMode.ScreenSpaceCamera;canvas.worldCamera=camera;canvas.planeDistance=.5f;
        var scaler=canvasObject.GetComponent<UnityEngine.UI.CanvasScaler>();scaler.uiScaleMode=UnityEngine.UI.CanvasScaler.ScaleMode.ScaleWithScreenSize;scaler.referenceResolution=new Vector2(1280,720);
        var textObject=new GameObject("State",typeof(RectTransform),typeof(UnityEngine.UI.Text));textObject.transform.SetParent(canvasObject.transform,false);label=textObject.GetComponent<UnityEngine.UI.Text>();label.font=Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");label.fontSize=22;label.color=Color.white;label.alignment=TextAnchor.UpperCenter;
        var rect=label.rectTransform;rect.anchorMin=new Vector2(0,1);rect.anchorMax=new Vector2(1,1);rect.pivot=new Vector2(.5f,1);rect.anchoredPosition=new Vector2(0,-12);rect.sizeDelta=new Vector2(0,100);
        yield return Grounded();yield return Approach(1);
        QualitySettings.vSyncCount=0;Application.targetFrameRate=90;started=Time.time;
        video=new QusapContinuousAvi(Output+"/Gameplay_WallDash_Raw.avi",1280,720,60);frames=new StreamWriter(Output+"/Gameplay_Frames.jsonl"){AutoFlush=true};recording=true;
        yield return WallSequence(1);
        action="Movimiento real a la pared izquierda";yield return Approach(-1);yield return WallSequence(-1);
        yield return GroundDash(1,false);yield return GroundDash(-1,false);yield return GroundDash(1,true);yield return GroundDash(-1,true);
        yield return AirDash(1,false);yield return AirDash(-1,false);yield return AirDash(1,true);yield return AirDash(-1,true);
        action="Idle / una espada / Root Motion OFF";yield return new WaitForSeconds(.15f);
        recording=false;DisposeCapture();
        foreach(string state in new[]{"WallSlide","WallJump","DashAir","JumpRise","Fall","LandSoft","RunForward","CombatIdle_B1"})Check(observedStates.Contains(state),"required visible state missing: "+state);
        Check(errors==0&&warnings==0&&rejectedAttacks==24&&wallRecharges==2,"mandatory checks incomplete");
        var result=new Result{seconds=Time.time-started,frames=frameCount,errors=errors,warnings=warnings,rejectedAttacks=rejectedAttacks,wallRecharges=wallRecharges,states=observedStates.OrderBy(s=>s).ToArray(),cases=cases.ToArray()};
        File.WriteAllText(Output+"/Gameplay_Result.json",JsonUtility.ToJson(result,true));Check(result.seconds<=25,"continuous gameplay exceeds 25 seconds");
    }
    string AnimatorName()
    {
        string Name(AnimatorStateInfo s)=>stateNames.TryGetValue(s.shortNameHash,out var n)?n:s.shortNameHash.ToString();
        string current=Name(player.Animator.GetCurrentAnimatorStateInfo(0));
        return player.Animator.IsInTransition(0)?current+" -> "+Name(player.Animator.GetNextAnimatorStateInfo(0)):current;
    }
    void LateUpdate()
    {
        if(preview||!body||!camera)return;
        camera.transform.position=new Vector3(body.position.x,Mathf.Max(2.15f,body.position.y+1.0f),-14);camera.transform.rotation=Quaternion.identity;
        if(label)label.text=action+"\nAnimator: "+AnimatorName()+"\nPared real: "+wall.WallSide+" | vx: "+body.linearVelocity.x.ToString("F2")+" | visual: "+player.PresentationDirection+" | RB: "+dash.IsDashing;
    }
    void CaptureFrame()
    {
        if(!recording)return;observedStates.Add(player.PresentationState);
        for(int i=0;i<allPlayers.Length;i++)
        {
            var p=allPlayers[i];Check(!p.Animator.applyRootMotion,"Root Motion enabled");
            Check(p.Visual.GetComponentsInChildren<MeshFilter>(false).Length==1,"duplicate active sword");
            Check(p.Visual.transform.localScale==scales[i]&&p.Facing.SwordVisual.localScale==swordScales[i],"presentation scale changed");
            Check(p.Facing.SwordVisual.localPosition==Vector3.zero,"socket-pivot not zero");
        }
        if(wall.CurrentWallCollider)
        {
            var oneWay=wall.CurrentWallCollider.GetComponentInChildren<QusapOneWayPlatform>(true);
            Check(!oneWay||oneWay.SolidCollider!=wall.CurrentWallCollider,"one-way platform counted as wall");
        }
        var old=RenderTexture.active;RenderTexture.active=rt;pixels.ReadPixels(new Rect(0,0,1280,720),0,0);pixels.Apply();RenderTexture.active=old;video.Add(pixels.EncodeToJPG(88));
        frames.WriteLine(JsonUtility.ToJson(new Frame{frame=frameCount++,time=Time.time-started,x=body.position.x,y=body.position.y,vx=body.linearVelocity.x,vy=body.linearVelocity.y,input=move,inputDirection=Math.Sign(move),wallSide=wall.WallSide,slideSide=vertical.WallSide,visualDirection=player.PresentationDirection,action=action,requestedState=player.PresentationState,animator=AnimatorName(),wall=wall.CurrentWallCollider?wall.CurrentWallCollider.name:"",grounded=ground.IsGrounded,sliding=vertical.IsWallSliding,dashing=dash.IsDashing,gravity=body.useGravity,airDash=MotorField<bool>("hasAirDash"),rootMotion=player.Animator.applyRootMotion}));
    }
    void DisposeCapture(){video?.Dispose();video=null;frames?.Dispose();frames=null;}
}
#endif
