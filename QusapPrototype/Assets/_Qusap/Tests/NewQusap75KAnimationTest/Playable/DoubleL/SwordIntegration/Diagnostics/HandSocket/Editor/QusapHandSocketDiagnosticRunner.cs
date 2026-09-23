using System;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Qusap.NewQusap75KAnimationTest.Playable.DoubleL.SwordIntegration.Diagnostics.HandSocket.Editor
{
    [InitializeOnLoad]
    public static class QusapHandSocketDiagnosticRunner
    {
        private const string Root = "Assets/_Qusap/Tests/NewQusap75KAnimationTest/Playable/DoubleL/SwordIntegration";
        private const string ScenePath = Root + "/Scene/CombatPlayground_Qusap75K_DoubleL_SwordIntegrationTest.unity";
        private const string ResultPath = Root + "/Diagnostics/HandSocket/HandSocketDiagnosticResult.txt";
        private const string ReportPath = Root + "/Diagnostics/HandSocket/HandSocketDiagnostic.json";
        private const string MarkerName = "QusapHandSocketDiagnostic.request";
        private const string PendingKey = "Qusap.HandSocketDiagnostic.Pending";
        private static double playStartedAt;

        static QusapHandSocketDiagnosticRunner()
        {
            EditorApplication.delayCall += RunWhenReady;
            EditorApplication.playModeStateChanged += OnPlayModeStateChanged;
        }

        private static string ProjectRoot => Directory.GetParent(Application.dataPath)?.FullName ?? Application.dataPath;
        private static string MarkerPath => Path.Combine(ProjectRoot, "Library", MarkerName);

        private static void RunWhenReady()
        {
            if (!File.Exists(MarkerPath)) return;
            if (EditorApplication.isCompiling || EditorApplication.isUpdating)
            {
                EditorApplication.delayCall += RunWhenReady;
                return;
            }
            File.Delete(MarkerPath);
            if (EditorApplication.isPlayingOrWillChangePlaymode)
            {
                WriteResult("BLOCKED: Unity is already in Play Mode.");
                return;
            }
            Scene current = SceneManager.GetActiveScene();
            if (current.IsValid() && current.isDirty)
            {
                WriteResult("BLOCKED: Active scene has unsaved changes.");
                return;
            }
            try
            {
                EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
                SessionState.SetBool(PendingKey, true);
                WriteResult("PLAY_MODE_PENDING");
                EditorApplication.EnterPlaymode();
            }
            catch (Exception exception)
            {
                WriteResult("FAILED: " + exception);
                Debug.LogException(exception);
            }
        }

        public static void RunFromCommandLine()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode)
                throw new InvalidOperationException("Unity is already in or entering Play Mode.");
            if (File.Exists(MarkerPath)) File.Delete(MarkerPath);
            Scene current = SceneManager.GetActiveScene();
            if (current.IsValid() && current.isDirty)
                throw new InvalidOperationException("Active scene has unsaved changes.");
            EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
            SessionState.SetBool(PendingKey, true);
            WriteResult("PLAY_MODE_PENDING");
            EditorApplication.EnterPlaymode();
        }

        private static void OnPlayModeStateChanged(PlayModeStateChange state)
        {
            if (!SessionState.GetBool(PendingKey, false)) return;
            if (state == PlayModeStateChange.EnteredPlayMode)
            {
                playStartedAt = EditorApplication.timeSinceStartup;
                new GameObject("Qusap_HandSocket_Runtime_Diagnostic")
                    .AddComponent<QusapHandSocketRuntimeDiagnostic>();
                EditorApplication.update -= Timeout;
                EditorApplication.update += Timeout;
            }
            else if (state == PlayModeStateChange.EnteredEditMode)
            {
                EditorApplication.update -= Timeout;
                SessionState.SetBool(PendingKey, false);
                EditorApplication.delayCall += FinalizeDiagnostic;
            }
        }

        private static void Timeout()
        {
            if (EditorApplication.isPlaying && !EditorApplication.isPaused)
                EditorApplication.QueuePlayerLoopUpdate();
            if (EditorApplication.isPlaying && EditorApplication.timeSinceStartup - playStartedAt > 60d)
            {
                Debug.LogError("Hand/socket diagnostic exceeded 60 seconds.");
                EditorApplication.ExitPlaymode();
            }
        }

        private static void FinalizeDiagnostic()
        {
            AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
            EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
            Selection.activeObject = AssetDatabase.LoadAssetAtPath<SceneAsset>(ScenePath);
            WriteResult(File.Exists(ToAbsolutePath(ReportPath))
                ? "DIAGNOSTIC_COMPLETED"
                : "FAILED: HandSocketDiagnostic.json was not generated.");
        }

        private static void WriteResult(string value)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(ToAbsolutePath(ResultPath)));
            File.WriteAllText(ToAbsolutePath(ResultPath), value);
            AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
        }

        private static string ToAbsolutePath(string assetPath) => Path.GetFullPath(Path.Combine(
            Application.dataPath, assetPath.Substring("Assets".Length).TrimStart('/', '\\')));
    }
}
