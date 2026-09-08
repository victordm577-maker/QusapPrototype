using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.Animations;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Qusap.Tests
{
    public sealed class QusapWeakKickAnimationEditModeTests
    {
        private const string FbxPath =
            "Assets/Scenes/Art/Characters/Qusap/Final_Light_v3/Qusap_Luz_Combat_v1.fbx";
        private const string ControllerPath =
            "Assets/Scenes/Art/Characters/Qusap/Final_Light_v3/Qusap_Luz_Animator_WeakKickTest_v1.controller";
        private const string ScenePath = "Assets/_Qusap/Scenes/Qusap_WeakKickAnimationTest_v1.unity";
        private static readonly string[] ClipNames =
        {
            "Qusap_Idle", "Qusap_Run", "Qusap_JumpRise", "Qusap_Fall", "Qusap_Land",
            "Qusap_WallSlide", "Qusap_WallJump", "Qusap_Dash",
            "Qusap_WeakKickGround", "Qusap_WeakKickAir"
        };

        [SetUp]
        public void RequireGeneratedTestCopy()
        {
            if (AssetDatabase.LoadAssetAtPath<AnimatorController>(ControllerPath) == null
                || AssetDatabase.LoadAssetAtPath<SceneAsset>(ScenePath) == null)
                Assert.Ignore("Run Tools > Qusap > Create Weak Kick Animation Test before asset validation.");
        }

        [Test]
        public void CombatFbxExposesExactlyTenNamedClips()
        {
            AnimationClip[] clips = PublicClips();
            Assert.That(clips.Select(clip => clip.name), Is.EquivalentTo(ClipNames));
            Assert.That(clips.GroupBy(clip => clip.name).All(group => group.Count() == 1), Is.True);
        }

        [Test]
        public void CombatFbxUsesGenericAvatarCompressionOffAndAnimationImport()
        {
            var importer = AssetImporter.GetAtPath(FbxPath) as ModelImporter;
            Assert.That(importer, Is.Not.Null);
            Assert.That(importer.animationType, Is.EqualTo(ModelImporterAnimationType.Generic));
            Assert.That(importer.avatarSetup, Is.EqualTo(ModelImporterAvatarSetup.CreateFromThisModel));
            Assert.That(importer.importAnimation, Is.True);
            Assert.That(importer.animationCompression, Is.EqualTo(ModelImporterAnimationCompression.Off));
        }

        [Test]
        public void OnlyIdleRunFallAndWallSlideLoop()
        {
            var importer = (ModelImporter)AssetImporter.GetAtPath(FbxPath);
            string[] looped = importer.clipAnimations.Where(clip => clip.loopTime)
                .Select(clip => clip.name).ToArray();
            Assert.That(looped, Is.EquivalentTo(new[]
                { "Qusap_Idle", "Qusap_Run", "Qusap_Fall", "Qusap_WallSlide" }));
        }

        [Test]
        public void EveryImportedClipLocksRootMotionChannels()
        {
            var importer = (ModelImporter)AssetImporter.GetAtPath(FbxPath);
            Assert.That(importer.motionNodeName, Is.Empty);
            Assert.That(importer.clipAnimations, Has.Length.EqualTo(10));
            Assert.That(importer.clipAnimations.All(clip => clip.lockRootRotation
                && clip.lockRootHeightY && clip.lockRootPositionXZ
                && clip.keepOriginalOrientation && clip.keepOriginalPositionY
                && clip.keepOriginalPositionXZ), Is.True);
        }

        [Test]
        public void CombatModelHasThreeMeshesOneArmatureAndFourBones()
        {
            GameObject model = AssetDatabase.LoadAssetAtPath<GameObject>(FbxPath);
            SkinnedMeshRenderer[] skinned = model.GetComponentsInChildren<SkinnedMeshRenderer>(true);
            int meshes = skinned.Length + model.GetComponentsInChildren<MeshFilter>(true).Length;
            Transform[] bones = skinned.SelectMany(renderer => renderer.bones)
                .Where(bone => bone != null).Distinct().ToArray();
            int armatures = bones.Select(bone =>
            {
                Transform root = bone;
                while (root.parent != null && root.parent != model.transform) root = root.parent;
                return root;
            }).Distinct().Count();
            Assert.That((meshes, armatures, bones.Length), Is.EqualTo((3, 1, 4)));
        }

        [Test]
        public void ControllerHasTenStatesAndEightTypedParameters()
        {
            AnimatorController controller = Controller();
            Assert.That(States(controller).Select(state => state.name), Is.EquivalentTo(ClipNames));
            Assert.That(controller.parameters, Has.Length.EqualTo(8));
            Assert.That(controller.parameters.Single(item => item.name == "CombatAnimating").type,
                Is.EqualTo(AnimatorControllerParameterType.Bool));
            Assert.That(controller.parameters.Single(item => item.name == "AttackVariant").type,
                Is.EqualTo(AnimatorControllerParameterType.Int));
        }

        [Test]
        public void EveryControllerStateUsesItsCombatFbxMotion()
        {
            Dictionary<string, AnimationClip> clips = PublicClips()
                .ToDictionary(clip => clip.name, StringComparer.Ordinal);
            foreach (AnimatorState state in States(Controller()))
                Assert.That(state.motion, Is.SameAs(clips[state.name]), state.name);
        }

        [Test]
        public void WeakKickSpeedsMatchRealMechanicalDurations()
        {
            Dictionary<string, AnimatorState> states = States(Controller())
                .ToDictionary(state => state.name, StringComparer.Ordinal);
            Assert.That(states["Qusap_WeakKickGround"].speed,
                Is.EqualTo((19f / 60f) / 0.30f).Within(0.000001f));
            Assert.That(states["Qusap_WeakKickAir"].speed,
                Is.EqualTo((16f / 60f) / 0.26f).Within(0.000001f));
        }

        [Test]
        public void WeakKickAnyStateEntriesUseOnlyBoolAndExactVariant()
        {
            AnimatorController controller = Controller();
            AnimatorStateMachine machine = controller.layers[0].stateMachine;
            foreach ((string name, QusapAttackVariant variant) in new[]
            {
                ("Qusap_WeakKickGround", QusapAttackVariant.WeakKickGround),
                ("Qusap_WeakKickAir", QusapAttackVariant.WeakKickAir)
            })
            {
                AnimatorState state = States(controller).Single(item => item.name == name);
                AnimatorStateTransition entry = machine.anyStateTransitions.Single(item => item.destinationState == state);
                AssertTransition(entry, 0.02f);
                Assert.That(entry.conditions.Any(item => item.parameter == "CombatAnimating"
                    && item.mode == AnimatorConditionMode.If), Is.True);
                Assert.That(entry.conditions.Any(item => item.parameter == "AttackVariant"
                    && item.mode == AnimatorConditionMode.Equals
                    && item.threshold == (int)variant), Is.True);
            }
        }

        [Test]
        public void EachWeakKickHasFourMechanicalBoolDrivenExits()
        {
            foreach (AnimatorState kick in States(Controller()).Where(state => state.name.StartsWith("Qusap_WeakKick")))
            {
                Assert.That(kick.transitions, Has.Length.EqualTo(4));
                Assert.That(kick.transitions.Select(item => item.destinationState.name),
                    Is.EquivalentTo(new[] { "Qusap_Idle", "Qusap_Run", "Qusap_JumpRise", "Qusap_Fall" }));
                foreach (AnimatorStateTransition exit in kick.transitions)
                {
                    AssertTransition(exit, 0.03f);
                    Assert.That(exit.conditions.Any(item => item.parameter == "CombatAnimating"
                        && item.mode == AnimatorConditionMode.IfNot), Is.True);
                }
            }
        }

        [Test]
        public void GeneralTransitionsCannotInterruptWeakKick()
        {
            AnimatorController controller = Controller();
            AnimatorStateMachine machine = controller.layers[0].stateMachine;
            HashSet<AnimatorState> kicks = States(controller)
                .Where(state => state.name.StartsWith("Qusap_WeakKick")).ToHashSet();
            IEnumerable<AnimatorStateTransition> general = machine.anyStateTransitions
                .Where(item => !kicks.Contains(item.destinationState))
                .Concat(States(controller).Where(state => !kicks.Contains(state))
                    .SelectMany(state => state.transitions));
            Assert.That(general.All(transition => transition.conditions.Any(condition =>
                condition.parameter == "CombatAnimating" && condition.mode == AnimatorConditionMode.IfNot)), Is.True);
        }

        [Test]
        public void DriverVisuallySelectsOnlyGroundAndAirWeakKickVariants()
        {
            MethodInfo method = typeof(QusapAnimationDriver).GetMethod("IsAnimatedWeakKick",
                BindingFlags.NonPublic | BindingFlags.Static);
            Assert.That(method, Is.Not.Null);
            foreach (QusapAttackVariant variant in Enum.GetValues(typeof(QusapAttackVariant)))
            {
                bool actual = (bool)method.Invoke(null, new object[] { variant });
                bool expected = variant == QusapAttackVariant.WeakKickGround
                    || variant == QusapAttackVariant.WeakKickAir;
                Assert.That(actual, Is.EqualTo(expected), variant.ToString());
            }
        }

        [Test]
        public void DriverContainsStartPhaseEndSubscriptionsAndDisableCleanup()
        {
            const BindingFlags flags = BindingFlags.NonPublic | BindingFlags.Instance;
            foreach (string method in new[]
            {
                "HandleAttackVariantStarted", "HandleAttackPhaseChanged", "HandleAttackEnded",
                "SubscribeToCombatEvents", "UnsubscribeFromCombatEvents", "ClearCombatAnimation"
            })
                Assert.That(typeof(QusapAnimationDriver).GetMethod(method, flags), Is.Not.Null, method);
        }

        [Test]
        public void DriverKeepsFacingLockedThroughAllWeakKickPhasesAndClearsAtEnd()
        {
            const BindingFlags flags = BindingFlags.NonPublic | BindingFlags.Instance;
            GameObject root = new("WeakKickDriverLifecycle");
            root.SetActive(false);
            QusapAnimationDriver driver = root.AddComponent<QusapAnimationDriver>();
            Transform visual = new GameObject("PlayerVisual").transform;
            visual.SetParent(root.transform);
            visual.localEulerAngles = new Vector3(0f, 150f, 0f);
            typeof(QusapAnimationDriver).GetField("playerVisual", flags).SetValue(driver, visual);
            MethodInfo start = typeof(QusapAnimationDriver).GetMethod("HandleAttackVariantStarted", flags);
            MethodInfo phase = typeof(QusapAnimationDriver).GetMethod("HandleAttackPhaseChanged", flags);
            MethodInfo end = typeof(QusapAnimationDriver).GetMethod("HandleAttackEnded", flags);
            FieldInfo locked = typeof(QusapAnimationDriver).GetField("combatFacingLocked", flags);
            FieldInfo yaw = typeof(QusapAnimationDriver).GetField("combatFacingYaw", flags);
            try
            {
                start.Invoke(driver, new object[] { QusapAttackVariant.WeakKickGround });
                Assert.That((bool)locked.GetValue(driver), Is.True);
                Assert.That((float)yaw.GetValue(driver), Is.EqualTo(150f).Within(0.001f));
                phase.Invoke(driver, new object[]
                    { QusapAttackVariant.WeakKickGround, QusapAttackPhase.Active });
                Assert.That((bool)locked.GetValue(driver), Is.True);
                phase.Invoke(driver, new object[]
                    { QusapAttackVariant.WeakKickGround, QusapAttackPhase.Recovery });
                Assert.That((bool)locked.GetValue(driver), Is.True);
                end.Invoke(driver, new object[] { QusapAttackVariant.WeakKickGround, true });
                Assert.That((bool)locked.GetValue(driver), Is.False);
            }
            finally { UnityEngine.Object.DestroyImmediate(root); }
        }

        [Test]
        public void GeneratedSceneKeepsDisabledV12BackupAndActiveRootMotionFreeCombatVisual()
        {
            WithScene(players =>
            {
                foreach (QusapCombatController player in players)
                {
                    Transform active = player.transform.Find("PlayerVisual");
                    Transform backup = player.transform.Find("PlayerVisual_LocomotionV12_Backup");
                    Assert.That(active, Is.Not.Null);
                    Assert.That(backup, Is.Not.Null);
                    Assert.That(active.gameObject.activeSelf, Is.True);
                    Assert.That(backup.gameObject.activeSelf, Is.False);
                    Assert.That(active.GetComponent<Animator>().applyRootMotion, Is.False);
                    Assert.That(AssetDatabase.GetAssetPath(PrefabUtility.GetCorrespondingObjectFromSource(active.gameObject)),
                        Is.EqualTo(FbxPath));
                }
            });
        }

        [Test]
        public void GeneratedSceneUsesCorrectedWeakKickBoxesOnly()
        {
            WithScene(players =>
            {
                foreach (QusapCombatController player in players)
                {
                    SerializedObject serialized = new(player);
                    AssertBox(serialized.FindProperty("weakKick"), new Vector2(0.72f, 0.60f),
                        new Vector2(0.56f, -0.35f));
                    AssertBox(serialized.FindProperty("weakKickAir"), new Vector2(0.64f, 0.55f),
                        new Vector2(0.50f, 0f));
                }
            });
        }

        [Test]
        public void GeneratedSceneKeepsGroundHeadbuttAndKnockbackAndSetsDiveHitstun()
        {
            WithScene(players =>
            {
                foreach (QusapCombatController player in players)
                {
                    SerializedObject serialized = new(player);
                    SerializedProperty weak = serialized.FindProperty("weakKick");
                    SerializedProperty headbutt = serialized.FindProperty("headbutt");
                    SerializedProperty dive = serialized.FindProperty("diveHeadbuttAir");
                    Assert.That(weak.FindPropertyRelative("horizontalKnockback").floatValue, Is.EqualTo(4f));
                    Assert.That(weak.FindPropertyRelative("verticalKnockback").floatValue, Is.EqualTo(1f));
                    Assert.That(headbutt.FindPropertyRelative("hitboxSize").vector2Value,
                        Is.EqualTo(new Vector2(1.2f, 0.8f)));
                    Assert.That(headbutt.FindPropertyRelative("horizontalKnockback").floatValue, Is.EqualTo(16.5f));
                    Assert.That(dive.FindPropertyRelative("hitstunDuration").floatValue, Is.EqualTo(0.35f));
                }
            });
        }

        [Test]
        public void GeneratedSceneRetainsHudAndPlacesTwoTargetsAtUsefulDistance()
        {
            Scene scene = EditorSceneManager.OpenPreviewScene(ScenePath);
            try
            {
                QusapCombatController[] players = Players(scene);
                Assert.That(players, Has.Length.EqualTo(2));
                Assert.That(Mathf.Abs(players[1].transform.position.x - players[0].transform.position.x),
                    Is.EqualTo(1.5f).Within(0.001f));
                Assert.That(scene.GetRootGameObjects().SelectMany(root =>
                    root.GetComponentsInChildren<QusapAirCombatDebugHud>(true)).Count(), Is.EqualTo(1));
                Assert.That(scene.GetRootGameObjects().SelectMany(root =>
                    root.GetComponentsInChildren<QusapWeakKickAnimationDebugHud>(true)).Count(), Is.EqualTo(1));
            }
            finally { EditorSceneManager.ClosePreviewScene(scene); }
        }

        [Test]
        public void HitstunLeavesRigidbodyGravityEnabled()
        {
            GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(
                "Assets/_Qusap/Prefabs/QusapCombatPlayer.prefab");
            GameObject instance = PrefabUtility.InstantiatePrefab(prefab) as GameObject;
            try
            {
                Rigidbody body = instance.GetComponent<Rigidbody>();
                QusapHitstunController hitstun = instance.GetComponent<QusapHitstunController>();
                body.useGravity = true;
                hitstun.EnterHitstun(0.35f);
                Assert.That(body.useGravity, Is.True);
            }
            finally { UnityEngine.Object.DestroyImmediate(instance); }
        }

        private static AnimatorController Controller() =>
            AssetDatabase.LoadAssetAtPath<AnimatorController>(ControllerPath);

        private static AnimatorState[] States(AnimatorController controller) =>
            controller.layers[0].stateMachine.states.Select(item => item.state).ToArray();

        private static AnimationClip[] PublicClips() =>
            AssetDatabase.LoadAllAssetsAtPath(FbxPath).OfType<AnimationClip>()
                .Where(clip => !clip.name.StartsWith("__preview__", StringComparison.OrdinalIgnoreCase)).ToArray();

        private static void AssertTransition(AnimatorStateTransition transition, float duration)
        {
            Assert.That(transition.hasExitTime, Is.False);
            Assert.That(transition.hasFixedDuration, Is.True);
            Assert.That(transition.duration, Is.EqualTo(duration).Within(0.000001f));
            Assert.That(transition.canTransitionToSelf, Is.False);
        }

        private static void AssertBox(SerializedProperty property, Vector2 size, Vector2 offset)
        {
            Assert.That(property.FindPropertyRelative("hitboxSize").vector2Value, Is.EqualTo(size));
            Assert.That(property.FindPropertyRelative("hitboxOffset").vector2Value, Is.EqualTo(offset));
        }

        private static void WithScene(Action<QusapCombatController[]> assertion)
        {
            Scene scene = EditorSceneManager.OpenPreviewScene(ScenePath);
            try { assertion(Players(scene)); }
            finally { EditorSceneManager.ClosePreviewScene(scene); }
        }

        private static QusapCombatController[] Players(Scene scene) => scene.GetRootGameObjects()
            .SelectMany(root => root.GetComponentsInChildren<QusapCombatController>(true))
            .OrderBy(player => player.transform.position.x).ToArray();
    }
}
