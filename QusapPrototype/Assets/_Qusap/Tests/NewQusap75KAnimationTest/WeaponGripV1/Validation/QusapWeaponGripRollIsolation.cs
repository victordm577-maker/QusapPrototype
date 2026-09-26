using System;
using System.Collections;
using System.IO;
using System.Linq;
using System.Text;
using UnityEngine;
#if UNITY_EDITOR
using UnityEditor;
#endif

public static class QusapWeaponGripRollIsolation
{
    public const string Output = @"C:\Dev\Qusap_ArtSource_Recovered\Qusap75K_WeaponGrip_v1\Revision_RollGrip";
    public static IEnumerator Run()
    {
        Directory.CreateDirectory(Output);
        System.Threading.Thread.CurrentThread.CurrentCulture=System.Globalization.CultureInfo.InvariantCulture;
        yield return null;
        var player = UnityEngine.Object.FindObjectsByType<GameObject>()
            .First(o => o.name.Contains("Player1_Qusap75K_WeaponGripV1_Test"));
        var original = player.GetComponentsInChildren<Animator>(true).First(a=>a.name=="Qusap75K_Visual");
        var clone = UnityEngine.Object.Instantiate(original.gameObject);
        clone.name="RollIsolationVisual";
        clone.transform.SetPositionAndRotation(original.transform.position, original.transform.rotation);
        clone.transform.localScale=original.transform.lossyScale;
        foreach(var t in clone.GetComponentsInChildren<Transform>(true)) t.gameObject.layer=30;
        player.SetActive(false);
        var animator=clone.GetComponent<Animator>();
        foreach(var a in clone.GetComponentsInChildren<Animator>(true)) if(a!=animator) a.enabled=false;
        animator.cullingMode=AnimatorCullingMode.AlwaysAnimate;
        var hand=animator.GetBoneTransform(HumanBodyBones.RightHand);
        var lower=animator.GetBoneTransform(HumanBodyBones.RightLowerArm);
        var hips=animator.GetBoneTransform(HumanBodyBones.Hips);
        var chest=animator.GetBoneTransform(HumanBodyBones.Chest);
        var socket=hand.Find("WeaponGripSocket");
        var sword=socket.Find("SwordVisual");
        var pose=hand.GetComponent<QusapWeaponGripPosePlayer>();
        pose.enabled=false;
        var csv=new StringBuilder("test,phase,hand_x,hand_y,hand_z,lower_x,lower_y,lower_z,lower_hand_m,socket_x,socket_y,socket_z,hand_socket_m,sword_x,sword_y,sword_z,socket_sword_m,hips_x,hips_y,hips_z,chest_up_y\n");
        var audit=new StringBuilder();
        foreach(var a in original.GetComponentsInChildren<Animator>(true)) audit.AppendLine("Original Animator: "+a.name+" enabled="+a.enabled+" avatar="+a.avatar+" controller="+a.runtimeAnimatorController);
        var skin=clone.GetComponentInChildren<SkinnedMeshRenderer>();
        skin.updateWhenOffscreen=true;
        for(int i=0;i<skin.bones.Length;i++) audit.AppendLine("skin bone "+i+" "+skin.bones[i].name+" "+skin.bones[i].position);
        foreach(var test in new[]{"A","B","C","A_SingleAnimator","C_SingleAnimator"})
        {
            foreach(var a in clone.GetComponentsInChildren<Animator>(true)) if(a!=animator) a.enabled=!test.Contains("SingleAnimator");
            animator.Rebind(); animator.speed=0; sword.gameObject.SetActive(test.StartsWith("C"));pose.enabled=!test.StartsWith("A");
            foreach(float t in new[]{0f,.25f,.5f,.75f,.99f})
            {
                animator.Play("RollFront",0,t); animator.Update(0f);
                if(!test.StartsWith("A")) pose.GripClip.SampleAnimation(hand.gameObject,0);
                // Allow the PlayerLoop to upload the current skinned pose before rendering.
                // The old diagnostic rendered all sampled poses in one frame, leaving GPU skinning stale.
                yield return null;
                yield return null;
                csv.AppendLine(test+","+t.ToString("F2")+","+V(hand.position)+","+V(lower.position)+","+Vector3.Distance(hand.position,lower.position).ToString("F6")+","+V(socket.position)+","+Vector3.Distance(hand.position,socket.position).ToString("F6")+","+V(sword.position)+","+Vector3.Distance(socket.position,sword.position).ToString("F6")+","+V(hips.position)+","+chest.up.y.ToString("F6"));
                Capture(skin,clone.transform.position+Vector3.up*1.05f,test+"_roll_"+t.ToString("F2"),new Vector3(0,-1,0),1.6f);
                if(test=="C")ExportGeometry(skin,socket,"Roll_"+t.ToString("F2"));
                audit.AppendLine(test+" "+t+" hipsLocal="+hips.localPosition+" rot="+hips.localRotation+" chest="+chest.localRotation+" hand="+hand.localPosition+" "+hand.localRotation);
                foreach(var b in hand.GetComponentsInChildren<Transform>()) if(b.name.StartsWith("claw")) audit.AppendLine(test+" "+t+" "+b.name+" rot="+b.localRotation.ToString("F7"));
            }
        }
        File.WriteAllText(Path.Combine(Output,"roll_ABC_original.csv"),csv.ToString());
        var rmCsv=new StringBuilder("test,phase,hand_x,hand_y,hand_z,lower_x,lower_y,lower_z,lower_hand_m,socket_x,socket_y,socket_z,hand_socket_m,sword_x,sword_y,sword_z,socket_sword_m\n");
        foreach(var test in new[]{"A","B","C"})
        {
            animator.Rebind();animator.speed=0;pose.enabled=test!="A";sword.gameObject.SetActive(test=="C");
            foreach(float t in new[]{0f,.25f,.5f,.75f,.99f})
            {
                animator.Play("RollRMFrontInPlace",0,t);animator.Update(0);yield return null;yield return null;
                rmCsv.AppendLine(test+","+t.ToString("F2")+","+V(hand.position)+","+V(lower.position)+","+Vector3.Distance(hand.position,lower.position).ToString("F6")+","+V(socket.position)+","+Vector3.Distance(hand.position,socket.position).ToString("F6")+","+V(sword.position)+","+Vector3.Distance(socket.position,sword.position).ToString("F6"));
            }
        }
        File.WriteAllText(Path.Combine(Output,"roll_RM_ABC_final.csv"),rmCsv.ToString());
#if UNITY_EDITOR
        var controller=(UnityEditor.Animations.AnimatorController)animator.runtimeAnimatorController;
        var roll=controller.layers[0].stateMachine.states.First(s=>s.state.name=="RollFront").state.motion as AnimationClip;
        audit.AppendLine("Roll="+AssetDatabase.GetAssetPath(roll)+" length="+roll.length+" humanMotion="+roll.humanMotion);
        foreach(var binding in AnimationUtility.GetCurveBindings(roll)) audit.AppendLine("roll curve "+binding.path+" "+binding.propertyName);
        foreach(var binding in AnimationUtility.GetCurveBindings(pose.GripClip)) audit.AppendLine("grip curve "+binding.path+" "+binding.propertyName);
#endif
        File.WriteAllText(Path.Combine(Output,"roll_ABC_audit.txt"),audit.ToString());
        foreach(var state in new[]{"CombatIdle_B1","RunForward","RollRMFrontInPlace"})
        foreach(float t in new[]{0f,.25f,.5f,.75f,.99f})
        {
            animator.Play(state,0,t);animator.Update(0);yield return null;yield return null;
            ExportGeometry(skin,socket,state+"_"+t.ToString("F2"));
            if(state=="RollRMFrontInPlace")Capture(skin,clone.transform.position+Vector3.up*1.05f,state+"_"+t.ToString("F2"),new Vector3(0,-1,0),2f);
        }
        animator.Rebind();animator.speed=0;pose.enabled=true;animator.Play("CombatIdle_B1",0,.5f);animator.Update(0);
        pose.GripClip.SampleAnimation(hand.gameObject,0);
        yield return null;yield return null;
        // Oblique views keep the palm visible instead of hiding it behind the torso.
        foreach(var view in new[]{
            (new Vector3(1,-1,0),"right_oblique"),
            (new Vector3(-1,-1,0),"left_oblique"),
            (new Vector3(1,-1,-.5f),"right_lower"),
            (new Vector3(-1,-1,-.5f),"left_lower"),
            (new Vector3(0,-1,-1),"underside")})
            Capture(skin,socket.position,"NEW_Idle_"+view.Item2,view.Item1,.3f);
        foreach(var view in new[]{(Vector3.back,"front"),(Vector3.left,"profile"),(Vector3.right,"opposite")})
            Capture(skin,socket.position,"grip_before_"+view.Item2,view.Item1,.3f);
        var snapshot=new GameObject("CPU_PoseSnapshot");snapshot.layer=30;
        snapshot.transform.SetPositionAndRotation(skin.transform.position,skin.transform.rotation);snapshot.transform.localScale=skin.transform.lossyScale;
        var snapshotMesh=new Mesh();skin.BakeMesh(snapshotMesh,true);
        snapshot.AddComponent<MeshFilter>().sharedMesh=snapshotMesh;snapshot.AddComponent<MeshRenderer>().sharedMaterials=skin.sharedMaterials;
        skin.enabled=false;
        foreach(var view in new[]{(new Vector3(0,-1,0),"front"),(Vector3.left,"profile"),(Vector3.right,"opposite")})
            Capture(skin,socket.position,"grip_CPU_"+view.Item2,view.Item1,.3f);
        skin.enabled=true;UnityEngine.Object.Destroy(snapshot);
        var baked=new Mesh();skin.BakeMesh(baked,true);
        var data=new StringBuilder();
        var meshVertices=skin.sharedMesh.vertices;
        var meshWeights=skin.sharedMesh.boneWeights;
        var bakedVertices=baked.vertices;
        for(int i=0;i<meshVertices.Length;i++)
        {
            var w=meshWeights[i];
            data.AppendLine(i+","+V(hand.InverseTransformPoint(skin.transform.TransformPoint(bakedVertices[i])))+","+w.boneIndex0+","+w.weight0+","+w.boneIndex1+","+w.weight1+","+w.boneIndex2+","+w.weight2+","+w.boneIndex3+","+w.weight3);
        }
        File.WriteAllText(Path.Combine(Output,"mesh_hand_local.csv"),data.ToString());
#if UNITY_EDITOR
        if(Application.isBatchMode)EditorApplication.Exit(0);else EditorApplication.ExitPlaymode();
#endif
    }
    private static string V(Vector3 v)=>v.x.ToString("F6")+","+v.y.ToString("F6")+","+v.z.ToString("F6");
    public static void ExportGeometry(SkinnedMeshRenderer skin,Transform socket,string name)
    {
        var folder=Path.Combine(Output,"collision_geometry");Directory.CreateDirectory(folder);
        var baked=new Mesh();skin.BakeMesh(baked,true);var positions=baked.vertices;
        var weights=skin.sharedMesh.boneWeights;var hand=new bool[positions.Length];
        bool IsHand(int bone)=>skin.bones[bone].name=="hand_r"||skin.bones[bone].name.StartsWith("claw_r_");
        for(int i=0;i<hand.Length;i++)
        {
            var w=weights[i];float total=(IsHand(w.boneIndex0)?w.weight0:0)+(IsHand(w.boneIndex1)?w.weight1:0)+(IsHand(w.boneIndex2)?w.weight2:0)+(IsHand(w.boneIndex3)?w.weight3:0);
            hand[i]=total>.45f;
        }
        var triangles=skin.sharedMesh.triangles;
        var bodyTriangles=Enumerable.Range(0,triangles.Length/3).Where(i=>!hand[triangles[i*3]]&&!hand[triangles[i*3+1]]&&!hand[triangles[i*3+2]])
            .SelectMany(i=>new[]{triangles[i*3],triangles[i*3+1],triangles[i*3+2]}).ToArray();
        var sword=socket.GetComponentInChildren<MeshFilter>();var swordVertices=sword.sharedMesh.vertices;var swordTris=sword.sharedMesh.triangles;
        var bladeTriangles=Enumerable.Range(0,swordTris.Length/3).Where(i=>swordVertices[swordTris[i*3]].y>.13f&&swordVertices[swordTris[i*3+1]].y>.13f&&swordVertices[swordTris[i*3+2]].y>.13f)
            .SelectMany(i=>new[]{swordTris[i*3],swordTris[i*3+1],swordTris[i*3+2]}).ToArray();
        using(var writer=new BinaryWriter(File.Create(Path.Combine(folder,name+".bin"))))
        {
            writer.Write(positions.Length);foreach(var v in positions)WriteVector(writer,skin.transform.TransformPoint(v));
            writer.Write(bodyTriangles.Length);foreach(var i in bodyTriangles)writer.Write(i);
            writer.Write(swordVertices.Length);foreach(var v in swordVertices)WriteVector(writer,sword.transform.TransformPoint(v));
            writer.Write(bladeTriangles.Length);foreach(var i in bladeTriangles)writer.Write(i);
        }
        File.WriteAllText(Path.Combine(folder,name+"_socket.json"),JsonUtility.ToJson(new SocketFrame{position=socket.position,up=socket.up,right=socket.right,forward=socket.forward}));
        UnityEngine.Object.Destroy(baked);
    }
    private static void WriteVector(BinaryWriter writer,Vector3 p){writer.Write(p.x);writer.Write(p.y);writer.Write(p.z);}
    [Serializable]private class SocketFrame{public Vector3 position,up,right,forward;}
    public static void Capture(SkinnedMeshRenderer skin,Vector3 target,string name,Vector3 offsetLocal,float size)
    {
        var go=new GameObject("IsolationCapture");var cam=go.AddComponent<Camera>();cam.enabled=false;
        cam.orthographic=true;cam.orthographicSize=size;cam.cullingMask=1<<30;cam.clearFlags=CameraClearFlags.SolidColor;cam.backgroundColor=new Color(.12f,.13f,.14f);
        cam.transform.position=target+skin.transform.TransformDirection(offsetLocal).normalized*5;
        cam.transform.rotation=Quaternion.LookRotation(target-cam.transform.position,Vector3.up);
        var rt=new RenderTexture(1024,1024,24);var previous=RenderTexture.active;
        cam.targetTexture=rt;cam.Render();RenderTexture.active=rt;
        var tex=new Texture2D(1024,1024,TextureFormat.RGB24,false);tex.ReadPixels(new Rect(0,0,1024,1024),0,0);tex.Apply();
        File.WriteAllBytes(Path.Combine(Output,name+".png"),tex.EncodeToPNG());
        RenderTexture.active=previous;cam.targetTexture=null;
        UnityEngine.Object.Destroy(tex);UnityEngine.Object.Destroy(rt);UnityEngine.Object.Destroy(go);
    }
}
