using System;
using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEngine;

public static class QusapWeaponGripSourceAudit
{
    [Serializable]
    private sealed class CheckpointResult
    {
        public string[] scenes;
        public string[] dependencies;
        public bool avatarValid, avatarHuman, rootMotionDisabled;
        public int humanoidAssignments, customClawBones, activeSwordsRight, activeSwordsLeft, errors;
    }

    // Read-only checkpoint audit: never save scenes, prefabs, importers or animation assets.
    public static void ValidateCheckpoint()
    {
        const string testRoot = "Assets/_Qusap/Tests/NewQusap75KAnimationTest/WeaponGripV1";
        const string output = @"C:\Dev\Qusap_ArtSource_Recovered\Qusap75K_WeaponGrip_v1\HandSwitchV5\GitCheckpoint";
        var result = new CheckpointResult
        {
            scenes = new[] { testRoot + "/Scene/Qusap75K_WeaponGripV1_Test.unity", testRoot + "/HandSwitchV5/Scene/Qusap75K_WeaponGripV5_HandSwitch_Test.unity" }
        };
        Application.LogCallback capture = (message, trace, kind) =>
        {
            if (kind == LogType.Error || kind == LogType.Exception || kind == LogType.Assert) result.errors++;
        };
        Application.logMessageReceived += capture;
        GameObject clone = null;
        try
        {
            foreach (var path in result.scenes)
            {
                var scene = UnityEditor.SceneManagement.EditorSceneManager.OpenScene(path);
                if (!scene.IsValid() || !scene.isLoaded) throw new InvalidOperationException("Scene failed to open: " + path);
                foreach (var rootObject in scene.GetRootGameObjects())
                    foreach (var t in rootObject.GetComponentsInChildren<Transform>(true))
                        if (GameObjectUtility.GetMonoBehavioursWithMissingScriptCount(t.gameObject) != 0)
                            throw new InvalidOperationException("Missing script in scene: " + path + " / " + PathOf(t));
            }
            result.dependencies = AssetDatabase.GetDependencies(result.scenes, true).OrderBy(p => p).ToArray();
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(testRoot + "/HandSwitchV5/Prefab/Qusap75K_WeaponGripV5_HandSwitch_Test.prefab");
            var presentation = prefab.GetComponentInChildren<QusapSwordHandSwitchV5>(true);
            // Instantiate only presentation, without gameplay root, Input or physics.
            clone = UnityEngine.Object.Instantiate(presentation.gameObject);
            clone.SetActive(true);
            var facing = clone.GetComponent<QusapSwordHandSwitchV5>();
            var animator = clone.GetComponentsInChildren<Animator>(true).First(a => a.name == "Qusap75K_Visual");
            result.avatarValid = animator.avatar && animator.avatar.isValid;
            result.avatarHuman = animator.avatar && animator.avatar.isHuman;
            result.humanoidAssignments = animator.avatar.humanDescription.human.Length;
            result.customClawBones = clone.GetComponentsInChildren<Transform>(true).Count(t => t.name.StartsWith("claw_r_") || t.name.StartsWith("claw_l_"));
            result.rootMotionDisabled = !animator.applyRootMotion;
            var sword = facing.SwordVisual;
            var sockets = clone.GetComponentsInChildren<Transform>(true);
            facing.Configure(null, animator, facing.FacingBasis,
                sockets.Single(t => t.name == "WeaponGripSocket_R"), sockets.Single(t => t.name == "WeaponGripSocket_L"), sword);
            facing.SetPreviewFacing(1);
            result.activeSwordsRight = clone.GetComponentsInChildren<Transform>().Count(t => t.name == "SwordVisual" && t.gameObject.activeInHierarchy);
            if (sword.parent.name != "WeaponGripSocket_R") throw new InvalidOperationException("Right socket mismatch.");
            facing.SetPreviewFacing(-1);
            result.activeSwordsLeft = clone.GetComponentsInChildren<Transform>().Count(t => t.name == "SwordVisual" && t.gameObject.activeInHierarchy);
            if (sword != facing.SwordVisual || sword.parent.name != "WeaponGripSocket_L") throw new InvalidOperationException("Sword reference or left socket mismatch.");
            if (!result.avatarValid || !result.avatarHuman || result.humanoidAssignments != 22 || result.customClawBones != 12 || !result.rootMotionDisabled || result.activeSwordsRight != 1 || result.activeSwordsLeft != 1 || result.errors != 0)
                throw new InvalidOperationException("WeaponGrip checkpoint verification failed.");
            Directory.CreateDirectory(output);
            File.WriteAllText(Path.Combine(output, "Unity_Checkpoint_Validation.json"), JsonUtility.ToJson(result, true));
            Debug.Log("WEAPON_GRIP_CHECKPOINT_PASS: both scenes open, Avatar valid, 22 assignments, 12 custom bones, one sword in either direction, Root Motion off, zero errors.");
        }
        finally
        {
            if (clone) UnityEngine.Object.DestroyImmediate(clone);
            Application.logMessageReceived -= capture;
        }
    }

    private const string Root = "Assets/_Qusap/Tests/NewQusap75KAnimationTest";
    private const string CharPath = Root + "/Character/Qusap75K_Rigged_Test_v2.fbx";
    private const string PrefabPath = Root + "/Playable/DoubleL/SwordIntegration/Prefab/Qusap75K_DoubleL_SwordIntegrationTest.prefab";
    private const string SwordPath = Root + "/Imported/QusapBasicSwordLargeGrip25K/Model/Qusap_BasicSword_LargeGrip_25K.fbx";
    private const string ReferencePath = Root + "/Imported/DoubleL/OneHandBase/Model/SM_Wep_Sword_03.fbx";
    private const string VariantCharacterPath = Root + "/WeaponGripV1/Models/Qusap75K_WeaponGrip_v4.fbx";
    private const string VariantSwordPath = Root + "/WeaponGripV1/Models/QusapSword_GripCentered_v1.fbx";

    public static void AuditVariant()
    {
        AssetDatabase.ImportAsset(VariantCharacterPath, ImportAssetOptions.ForceSynchronousImport);
        var importer = (ModelImporter)AssetImporter.GetAtPath(VariantCharacterPath);
        importer.animationType = ModelImporterAnimationType.Human;
        importer.avatarSetup = ModelImporterAvatarSetup.CreateFromThisModel;
        importer.optimizeGameObjects = false;
        importer.importAnimation = false;
        importer.SaveAndReimport();
        var originalAvatar = AssetDatabase.LoadAllAssetsAtPath(CharPath).OfType<Avatar>().First();
        var description = importer.humanDescription;
        description.human = originalAvatar.humanDescription.human;
        importer.humanDescription = description;
        importer.SaveAndReimport();
        AssetDatabase.ImportAsset(VariantSwordPath, ImportAssetOptions.ForceSynchronousImport);
        var swordImporter = (ModelImporter)AssetImporter.GetAtPath(VariantSwordPath);
        swordImporter.animationType = ModelImporterAnimationType.None;
        swordImporter.importAnimation = false;
        swordImporter.SaveAndReimport();
        var sb = new StringBuilder();
        var avatar = AssetDatabase.LoadAllAssetsAtPath(VariantCharacterPath).OfType<Avatar>().FirstOrDefault();
        sb.AppendLine("Variant avatar valid=" + (avatar && avatar.isValid) + " human=" + (avatar && avatar.isHuman));
        if (avatar != null) foreach (var h in avatar.humanDescription.human) sb.AppendLine(h.humanName + " -> " + h.boneName);
        DumpModel(VariantCharacterPath, "Variant character", sb);
        DumpModel(VariantSwordPath, "Variant sword", sb);
        var testRoot = PrefabUtility.LoadPrefabContents(PrefabPath);
        try
        {
            var visual = testRoot.GetComponentsInChildren<Transform>(true).First(t => t.name == "Qusap75K_Visual");
            sb.AppendLine("Existing visual root: " + PathOf(visual) + " localPosition=" + visual.localPosition.ToString("F7") + " localRotation=" + visual.localRotation.ToString("F7") + " localScale=" + visual.localScale.ToString("F7"));
            sb.AppendLine("Existing visual active=" + visual.gameObject.activeSelf + " controller=" + visual.GetComponent<Animator>()?.runtimeAnimatorController?.name);
            foreach (var child in visual.Cast<Transform>()) sb.AppendLine("Existing visual child: " + child.name + " localPosition=" + child.localPosition.ToString("F7") + " localRotation=" + child.localRotation.ToString("F7") + " localScale=" + child.localScale.ToString("F7"));
            foreach (var renderer in visual.GetComponentsInChildren<Renderer>(true)) sb.AppendLine("Existing renderer: " + PathOf(renderer.transform) + " material=" + (renderer.sharedMaterial ? renderer.sharedMaterial.name : "none"));
        }
        finally { PrefabUtility.UnloadPrefabContents(testRoot); }
        File.WriteAllText(Path.Combine(Directory.GetParent(Application.dataPath).FullName, "WeaponGrip_VariantAudit.txt"), sb.ToString());
        Debug.Log("Weapon grip variant audit complete");
    }

    public static void Run()
    {
        var sb = new StringBuilder();
        var importer = (ModelImporter)AssetImporter.GetAtPath(CharPath);
        var avatar = AssetDatabase.LoadAllAssetsAtPath(CharPath).OfType<Avatar>().FirstOrDefault();
        sb.AppendLine("Character importer: animationType=" + importer.animationType + ", avatarSetup=" + importer.avatarSetup + ", optimizeGameObjects=" + importer.optimizeGameObjects);
        sb.AppendLine("Avatar: " + (avatar == null ? "MISSING" : $"isValid={avatar.isValid}, isHuman={avatar.isHuman}, name={avatar.name}"));
        if (avatar != null) foreach (var h in avatar.humanDescription.human)
            sb.AppendLine("Human mapping: " + h.humanName + " -> " + h.boneName);
        var instance = PrefabUtility.LoadPrefabContents(PrefabPath);
        try
        {
            foreach (var animator in instance.GetComponentsInChildren<Animator>(true))
                sb.AppendLine("Animator: " + PathOf(animator.transform) + ", avatar=" + (animator.avatar ? animator.avatar.name : "MISSING") + ", valid=" + (animator.avatar && animator.avatar.isValid));
            var hand = instance.GetComponentsInChildren<Transform>(true).FirstOrDefault(t => t.name == "hand_r");
            if (hand != null)
            {
                sb.AppendLine("Hand: " + PathOf(hand));
                sb.AppendLine("Hand axes world: right=" + hand.right.ToString("F6") + ", up=" + hand.up.ToString("F6") + ", forward=" + hand.forward.ToString("F6"));
                sb.AppendLine("Hand local: position=" + hand.localPosition.ToString("F7") + ", rotation=" + hand.localRotation.ToString("F7") + ", scale=" + hand.localScale.ToString("F7"));
                DumpTree(hand, sb, 0);
            }
            foreach (var smr in instance.GetComponentsInChildren<SkinnedMeshRenderer>(true))
            {
                sb.AppendLine("Skinned mesh: " + PathOf(smr.transform) + ", verts=" + smr.sharedMesh.vertexCount + ", bones=" + smr.bones.Length);
                sb.AppendLine("Bone names: " + string.Join(", ", smr.bones.Select(b => b ? b.name : "NULL")));
            }
        }
        finally { PrefabUtility.UnloadPrefabContents(instance); }
        DumpModel(SwordPath, "Qusap sword", sb);
        DumpModel(ReferencePath, "DoubleL reference sword", sb);
        string outPath = Path.Combine(Directory.GetParent(Application.dataPath).FullName, "WeaponGrip_SourceAudit.txt");
        File.WriteAllText(outPath, sb.ToString());
        Debug.Log("Weapon grip source audit: " + outPath);
    }

    private static void DumpModel(string path, string label, StringBuilder sb)
    {
        var model = AssetDatabase.LoadAssetAtPath<GameObject>(path);
        if (!model) { sb.AppendLine(label + ": MISSING"); return; }
        var instance = UnityEngine.Object.Instantiate(model);
        try
        {
            sb.AppendLine(label + ": " + path);
            DumpTree(instance.transform, sb, 0);
            foreach (var filter in instance.GetComponentsInChildren<MeshFilter>(true))
                sb.AppendLine("Mesh: " + PathOf(filter.transform) + ", vertices=" + filter.sharedMesh.vertexCount + ", local bounds=" + filter.sharedMesh.bounds);
        }
        finally { UnityEngine.Object.DestroyImmediate(instance); }
    }

    private static void DumpTree(Transform t, StringBuilder sb, int depth)
    {
        sb.AppendLine(new string(' ', depth * 2) + t.name + " pos=" + t.localPosition.ToString("F7") + " rot=" + t.localRotation.ToString("F7") + " scale=" + t.localScale.ToString("F7"));
        foreach (Transform child in t) DumpTree(child, sb, depth + 1);
    }
    private static string PathOf(Transform t) => t.parent == null ? t.name : PathOf(t.parent) + "/" + t.name;
}
