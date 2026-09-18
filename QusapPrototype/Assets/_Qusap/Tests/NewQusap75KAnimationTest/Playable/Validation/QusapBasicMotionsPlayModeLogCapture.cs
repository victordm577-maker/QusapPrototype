using System;
using System.IO;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Qusap.NewQusap75KAnimationTest.Playable.Validation
{
    internal static class QusapBasicMotionsPlayModeLogCapture
    {
        private const string ScenePath =
            "Assets/_Qusap/Tests/NewQusap75KAnimationTest/Playable/Scene/CombatPlayground_Qusap75K_PlayableTest.unity";

        private static string logPath;
        private static bool active;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        private static void BeginCapture()
        {
            active = string.Equals(SceneManager.GetActiveScene().path, ScenePath, StringComparison.Ordinal);
            if (!active)
            {
                return;
            }

            string relative = Path.Combine("_Qusap", "Tests", "NewQusap75KAnimationTest", "Playable",
                "Validation", "BasicMotionsPlayModeConsole.log");
            logPath = Path.GetFullPath(Path.Combine(Application.dataPath, relative));

            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(logPath) ?? Application.dataPath);
                File.WriteAllText(logPath, "QUSAP75K_BASIC_MOTIONS_PLAYMODE_LOG\n");
                Application.logMessageReceived += OnLogMessage;
            }
            catch
            {
                active = false;
            }
        }

        private static void OnLogMessage(string condition, string stackTrace, LogType type)
        {
            if (!active || type == LogType.Log)
            {
                return;
            }

            try
            {
                string cleanCondition = (condition ?? string.Empty).Replace('\r', ' ').Replace('\n', ' ');
                string cleanStack = (stackTrace ?? string.Empty).Replace('\r', ' ').Replace('\n', ' ');
                File.AppendAllText(logPath,
                    $"{type.ToString().ToUpperInvariant()}|{cleanCondition}|{cleanStack}\n");
            }
            catch
            {
                // Validation logging must not alter gameplay.
            }
        }
    }
}
