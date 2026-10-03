using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEditor.Animations;
using UnityEngine;

public static class QusapDoubleLAirBuilder
{
    public const string Output=@"C:\Dev\Qusap_ArtSource_Recovered\DoubleL_AirLocomotion";
    public const string Scene="Assets/_Qusap/Scenes/CombatPlayground.unity";
    public static void Preview(){Open(true);}
    public static void Gameplay(){Open(false);}
    public static void Integrate()
    {
        var controller=AssetDatabase.LoadAssetAtPath<AnimatorController>("Assets/_Qusap/Presentation/DoubleLGroundAttacks/DoubleLGroundAttacks.controller");
        var machine=controller.layers[0].stateMachine;
        AnimationClip Clip(string name)
        {
            string root="Assets/_Qusap/Tests/NewQusap75KAnimationTest/PurchasedAnimationsV111/Imported/FBX_Animations/One Hand Base/Jump/";
            string path=root+(name.EndsWith("InPlace")?"InPlace/":"")+name+".fbx";
            return AssetDatabase.LoadAllAssetsAtPath(path).OfType<AnimationClip>().Single(c=>c.name==name);
        }
        void Assign(string stateName,string clipName,float speed)
        {
            var state=machine.states.Where(s=>s.state.name==stateName).Select(s=>s.state).SingleOrDefault()??machine.AddState(stateName);
            state.motion=Clip(clipName);state.speed=speed;state.writeDefaultValues=true;
        }
        Assign("JumpTakeoff","1Hand_Base_Jump_Start_InPlace",Clip("1Hand_Base_Jump_Start_InPlace").length/QusapDoubleLAirLocomotion.TakeoffDuration);
        Assign("JumpRise","1Hand_Base_Jump_Air_Loop",1);
        Assign("LandSoft","1Hand_Base_Jump_End_1_InPlace",Clip("1Hand_Base_Jump_End_1_InPlace").length/QusapDoubleLAirLocomotion.SoftLandingDuration);
        Assign("LandHard","1Hand_Base_Jump_End_2_InPlace",Clip("1Hand_Base_Jump_End_2_InPlace").length/QusapDoubleLAirLocomotion.HardLandingDuration);
        EditorUtility.SetDirty(controller);AssetDatabase.SaveAssets();
        Debug.Log("DOUBLEL_AIR_INTEGRATION_PASS: presentation-only states, source clips and attack states untouched");
        EditorApplication.Exit(0);
    }
    static void Open(bool preview)
    {
        Directory.CreateDirectory(Output);
        EditorSceneManager.OpenScene(Scene);
        var fixture=new GameObject("Air_Locomotion_Evidence").AddComponent<QusapDoubleLAirEvidence>();
        fixture.preview=preview;
        int frames=0;
        void Play(){if(EditorApplication.isCompiling||EditorApplication.isUpdating||++frames<8)return;EditorApplication.update-=Play;EditorApplication.isPaused=false;var game=typeof(EditorWindow).Assembly.GetType("UnityEditor.GameView");EditorWindow.GetWindow(game).Show();EditorWindow.GetWindow(game).Focus();EditorApplication.EnterPlaymode();}
        EditorApplication.update+=Play;
    }
}
