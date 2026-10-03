using UnityEditor;
using UnityEditor.Animations;
using UnityEditor.SceneManagement;
using UnityEngine;
using System.Linq;
using Qusap;

public static class QusapDoubleLWallDashBuilder
{
    public static void Preview()=>Open(true);
    public static void Gameplay()=>Open(false);
    public static void Integrate()
    {
        var controller=AssetDatabase.LoadAssetAtPath<AnimatorController>("Assets/_Qusap/Presentation/DoubleLGroundAttacks/DoubleLGroundAttacks.controller");
        var machine=controller.layers[0].stateMachine;
        AnimationClip Clip(string name)=>controller.animationClips.First(c=>c.name==name&&AssetDatabase.GetAssetPath(c).Contains("PurchasedAnimationsV111/"));
        void Assign(string name,AnimationClip clip,float speed)
        {
            var state=machine.states.Where(s=>s.state.name==name).Select(s=>s.state).SingleOrDefault()??machine.AddState(name);
            state.motion=clip;state.speed=speed;
        }
        var prefab=AssetDatabase.LoadAssetAtPath<GameObject>("Assets/_Qusap/Prefabs/QusapCombatPlayer.prefab");
        float duration=new SerializedObject(prefab.GetComponent<QusapDashMotor>()).FindProperty("dashDuration").floatValue;
        var air=Clip("1Hand_Base_Jump_Air_Loop");
        // Sprint's left pose reaches the floor with the frozen sword. Keep the
        // approved RunForward state and its existing left-arm presentation.
        var discarded=machine.states.Where(s=>s.state.name=="DashGround").Select(s=>s.state).SingleOrDefault();
        if(discarded)machine.RemoveState(discarded);
        Assign("DashAir",air,1);
        // WallSlide already uses this neutral purchased loop. Keep its state.
        var wallJump=machine.states.Single(s=>s.state.name=="WallJump").state;wallJump.motion=air;
        EditorUtility.SetDirty(controller);AssetDatabase.SaveAssets();
        Debug.Log("WALL_DASH_INTEGRATION_PASS: native dash duration="+duration+" ground dash preserves RunForward; air/wall jump use the purchased neutral loop");
        EditorApplication.Exit(0);
    }
    static void Open(bool preview)
    {
        EditorSceneManager.OpenScene("Assets/_Qusap/Scenes/CombatPlayground.unity");
        new GameObject("WallDash_Evidence").AddComponent<QusapDoubleLWallDashEvidence>().preview=preview;
        int frames=0;
        void Play()
        {
            if(EditorApplication.isCompiling||EditorApplication.isUpdating||++frames<8)return;
            EditorApplication.update-=Play;EditorApplication.isPaused=false;
            var game=typeof(EditorWindow).Assembly.GetType("UnityEditor.GameView");
            EditorWindow.GetWindow(game).Show();EditorWindow.GetWindow(game).Focus();EditorApplication.EnterPlaymode();
        }
        EditorApplication.update+=Play;
    }
}
