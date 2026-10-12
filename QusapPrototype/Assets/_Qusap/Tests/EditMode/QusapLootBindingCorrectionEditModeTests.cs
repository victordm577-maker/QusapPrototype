using System;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using NUnit.Framework;
using UnityEditor;
using UnityEngine.InputSystem;

namespace Qusap.Tests
{
    public sealed class QusapLootBindingCorrectionEditModeTests
    {
        private static string Hash(string path)
        { using var sha = SHA256.Create(); return BitConverter.ToString(sha.ComputeHash(File.ReadAllBytes(path))).Replace("-", ""); }
        [Test] public void InputActionsReaderMainPrefabAndSceneMatchApprovedBytes()
        {
            Assert.That(Hash("Assets/_Qusap/Settings/QusapControls.inputactions"), Is.EqualTo("928669F8FA54A36F792F1E6A4B367759CE1E004EA3E3670C39F5C8BA9651D912"));
            Assert.That(Hash("Assets/_Qusap/Prefabs/QusapCombatPlayer.prefab"), Is.EqualTo("FB72228431EC4F8620D8F5A6FD8240ECABCFC0D5AB046AF3C8BF1984A53D53D5"));
            Assert.That(Hash("Assets/_Qusap/Scripts/Input/QusapInputReader.cs"), Is.EqualTo("77FD97B48684C854E1A2D8AC55CCBC99AAFDFA1EA2D40FD44D6C7DFB435B4B47"));
            Assert.That(Hash("Assets/_Qusap/Scenes/RaidWorldLootInteractionPlayground.unity"), Is.EqualTo("CA7F0A321DE08971ACFD065DFBBA25C4CF8C81565E9AE604E3444F1D1D733568"));
            Assert.That(EditorBuildSettings.scenes.Any(s => s.path.EndsWith("RaidWorldLootInteractionPlayground.unity")), Is.False);
        }
        [Test] public void FAndDpadUpHaveNoGameplayBindingWhileNativeEIsUnchanged()
        {
            var asset = AssetDatabase.LoadAssetAtPath<InputActionAsset>("Assets/_Qusap/Settings/QusapControls.inputactions");
            var map = asset.FindActionMap("Gameplay", true);
            Assert.That(map.bindings.Any(b => string.Equals(b.effectivePath, "<Keyboard>/f", StringComparison.OrdinalIgnoreCase)), Is.False);
            Assert.That(map.bindings.Any(b => string.Equals(b.effectivePath, "<Gamepad>/dpad/up", StringComparison.OrdinalIgnoreCase)), Is.False);
            Assert.That(map.FindAction("WeaponSwapThrow", true).bindings.Any(b => b.effectivePath == "<Keyboard>/e"), Is.True);
        }
        [Test] public void BridgeIsInstalledOnlyByTheIsolatedPlayground()
        {
            var installers = Directory.GetFiles("Assets/_Qusap/Scripts", "*.cs", SearchOption.AllDirectories)
                .Where(p => File.ReadAllText(p).Contains("AddComponent<QusapLootInteractionInputBridge>"))
                .Select(p => p.Replace('\\', '/')).OrderBy(p => p, StringComparer.Ordinal).ToArray();
            Assert.That(installers, Is.EqualTo(new[] {
                "Assets/_Qusap/Scripts/Raid/QusapRaidDeathLootContainerPlayground.cs",
                "Assets/_Qusap/Scripts/Raid/QusapRaidLootInteractionPlayground.cs" }));
            string guid = AssetDatabase.AssetPathToGUID("Assets/_Qusap/Scripts/Raid/QusapLootInteractionInputBridge.cs");
            foreach (string path in Directory.GetFiles("Assets", "*.unity", SearchOption.AllDirectories)
                .Concat(Directory.GetFiles("Assets", "*.prefab", SearchOption.AllDirectories)))
                StringAssert.DoesNotContain("guid: " + guid, File.ReadAllText(path), path);
        }
    }
}
