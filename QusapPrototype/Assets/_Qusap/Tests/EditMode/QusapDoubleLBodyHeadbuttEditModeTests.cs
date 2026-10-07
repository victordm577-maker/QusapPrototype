using System.IO;
using System.Linq;
using System.Security.Cryptography;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;

namespace Qusap.Tests
{
    public sealed class QusapDoubleLBodyHeadbuttEditModeTests
    {
        const string Root="Assets/_Qusap/Presentation/DoubleLGroundAttacks/";
        AnimatorController Controller=>AssetDatabase.LoadAssetAtPath<AnimatorController>(Root+"DoubleLGroundAttacks.controller");
        Object Presenter=>AssetDatabase.LoadAssetAtPath<GameObject>("Assets/_Qusap/Prefabs/QusapCombatPlayer.prefab")
            .GetComponents<MonoBehaviour>().Single(c=>c.GetType().Name=="QusapDoubleLGroundAttackPresenter");
        [Test] public void BodyMotionIsTheApprovedUneditedPurchasedClip()
        {
            var state=Controller.layers[0].stateMachine.states.Single(s=>s.state.name=="BodyAttack").state;
            Assert.That(state.motion.name,Is.EqualTo("1Hand_Base_Skill_7_InPlace"));
            Assert.That(AssetDatabase.GetAssetPath(state.motion),Does.EndWith("1Hand_Base_Skill_7_InPlace.fbx"));
            Assert.That(((AnimationClip)state.motion).events,Is.Empty);
        }
        [Test] public void HeadbuttHasOnlyTheApprovedEightRotationBindings()
        {
            var h2=AssetDatabase.LoadAssetAtPath<AnimationClip>(Root+"Qusap_Headbutt_H2_Forceful.anim");
            Assert.That(h2.length,Is.EqualTo(.93f).Within(.00001f));Assert.That(h2.events,Is.Empty);
            Assert.That(h2.hasMotionCurves||h2.hasRootCurves,Is.False);
            var bindings=AnimationUtility.GetCurveBindings(h2);
            Assert.That(bindings,Has.Length.EqualTo(32));
            CollectionAssert.AreEquivalent(new[]{"spine_01","spine_02","spine_03","spine_04","spine_05","neck_01","neck_02","head"},bindings.Select(b=>b.path.Split('/').Last()).Distinct());
            Assert.That(bindings.All(b=>b.propertyName.StartsWith("m_LocalRotation.")),Is.True);
        }
        [Test] public void PromotedH2BytesMatchTheApprovedAssetDigest()
        {
            using(var sha=SHA256.Create())
                Assert.That(System.BitConverter.ToString(sha.ComputeHash(File.ReadAllBytes(Root+"Qusap_Headbutt_H2_Forceful.anim"))).Replace("-",""),
                    Is.EqualTo("6DE2CAE4D9072448FECE1E23B1B959D9127300F212B3AAA27E132D69F304A086"));
        }
        [Test] public void RuntimeRotationTracksExactlyMatchH2Curves()
        {
            var serialized=new SerializedObject(Presenter);
            var clip=(AnimationClip)serialized.FindProperty("headbuttClip").objectReferenceValue;
            Assert.That(AssetDatabase.GetAssetPath(clip),Is.EqualTo(Root+"Qusap_Headbutt_H2_Forceful.anim"));
            var tracks=serialized.FindProperty("headbuttRotations");Assert.That(tracks.arraySize,Is.EqualTo(8));
            var bindings=AnimationUtility.GetCurveBindings(clip);
            for(int i=0;i<tracks.arraySize;i++)
            {
                var track=tracks.GetArrayElementAtIndex(i);string path=track.FindPropertyRelative("path").stringValue;
                foreach(string component in new[]{"x","y","z","w"})
                {
                    var curve=AnimationUtility.GetEditorCurve(clip,bindings.Single(b=>b.path==path&&b.propertyName=="m_LocalRotation."+component));
                    CollectionAssert.AreEqual(curve.keys,track.FindPropertyRelative(component).animationCurveValue.keys);
                }
            }
        }
        [Test] public void BodyStatesHaveNoAutomaticExitsOrPersistentParameters()
        {
            foreach(string name in new[]{"BodyAttack","Headbutt"})
                Assert.That(Controller.layers[0].stateMachine.states.Single(s=>s.state.name==name).state.transitions,Is.Empty);
            Assert.That(Controller.parameters.Any(p=>p.name.Contains("BodyAttack")||p.name.Contains("Headbutt")||p.name=="StateRecovered"),Is.False);
            Assert.That(Controller.layers.SelectMany(l=>l.stateMachine.anyStateTransitions).Any(t=>t.hasExitTime),Is.False);
        }
        [Test] public void DamageFinisherAndRootMotionRemainApproved()
        {
            Assert.That(Controller.layers[0].stateMachine.states.Single(s=>s.state.name=="FiveAttack_2").state.motion.name,Is.EqualTo("1Hand_Base_Attack_B_2_InPlace"));
            var prefab=AssetDatabase.LoadAssetAtPath<GameObject>("Assets/_Qusap/Prefabs/QusapCombatPlayer.prefab");
            Assert.That(prefab.GetComponentsInChildren<Animator>(true).All(a=>!a.applyRootMotion),Is.True);
            Assert.That(AssetDatabase.GetDependencies("Assets/_Qusap/Prefabs/QusapCombatPlayer.prefab",true).Any(p=>p.Contains("CustomHeadbutt")||p.Contains("H1_Compact")||p.Contains("H3_HornsFirst")),Is.False);
        }
    }
}
