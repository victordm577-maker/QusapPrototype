using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEngine;

namespace Qusap.NewQusap75KAnimationTest.Playable.DoubleL.SwordIntegration.Validation
{
    [Serializable]
    public sealed class SwordIdleComparisonReport
    {
        public string generatedAtUtc;
        public bool keys1To3Mapped;
        public bool[] statesReached;
        public bool[] clipsLoop;
        public bool sameSwordTransform;
        public bool rendererAlwaysEnabled;
        public bool rootMotionDisabled;
        public string alignmentLocalPosition;
        public string alignmentLocalEulerAngles;
        public string alignmentLocalScale;
        public string[] screenshots;
        public string[] consoleErrors;
        public bool passed;
    }

    [DisallowMultipleComponent]
    public sealed class QusapSwordIdleComparisonValidation : MonoBehaviour
    {
        private const string Root = "Assets/_Qusap/Tests/NewQusap75KAnimationTest/Playable/DoubleL/SwordIntegration";
        private const string ReportPath = Root + "/Validation/IdleComparisonValidation.json";
        private const string MarkerName = "QusapSwordIdleComparisonValidation.request";
        private readonly List<string> errors = new();

        private string MarkerPath => Path.Combine(
            Directory.GetParent(Application.dataPath)?.FullName ?? Application.dataPath,
            "Library", MarkerName);

        private void Awake()
        {
            if (!File.Exists(MarkerPath)) { enabled = false; return; }
            File.Delete(MarkerPath);
            Application.logMessageReceived += CaptureLog;
            StartCoroutine(RunComparison());
        }

        private void OnDestroy() => Application.logMessageReceived -= CaptureLog;

        private void CaptureLog(string condition, string stackTrace, LogType type)
        {
            if (type == LogType.Error || type == LogType.Assert || type == LogType.Exception)
                errors.Add(condition + (string.IsNullOrWhiteSpace(stackTrace) ? string.Empty : "\n" + stackTrace));
        }

        private IEnumerator RunComparison()
        {
            yield return null;
            QusapSwordIdleComparisonController selector = FindAnyObjectByType<QusapSwordIdleComparisonController>();
            Transform alignment = FindObjectsByType<Transform>(FindObjectsInactive.Include)
                .FirstOrDefault(item => item.name == "SwordVisualAlignment");
            Renderer[] renderers = alignment != null
                ? alignment.GetComponentsInChildren<Renderer>(true) : Array.Empty<Renderer>();
            Animator animator = selector != null ? selector.Animator : null;
            Camera camera = Camera.main;
            var report = new SwordIdleComparisonReport
            {
                statesReached = new bool[3],
                clipsLoop = new bool[3],
                screenshots = new string[3]
            };

            if (selector == null || alignment == null || animator == null || camera == null || renderers.Length == 0)
            {
                errors.Add("Idle comparison hierarchy is incomplete.");
                Finish(report);
                yield break;
            }

            MonoBehaviour cameraRig = camera.GetComponents<MonoBehaviour>().FirstOrDefault(item =>
                item != null && item.GetType().FullName == "Qusap.QusapSharedCombatCamera");
            if (cameraRig != null) cameraRig.enabled = false;
            FocusCamera(camera, selector.transform);

            Vector3 position = alignment.localPosition;
            Quaternion rotation = alignment.localRotation;
            Vector3 scale = alignment.localScale;
            bool sameTransform = true;
            bool rendererEnabled = true;

            for (int index = 0; index < 3; index++)
            {
                int selection = index + 1;
                string stateName = QusapSwordIdleComparisonController.StateNameFor(selection);
                selector.SelectIdle(selection, true);
                yield return WaitForState(animator, stateName, 1.5f);
                yield return new WaitForSecondsRealtime(0.25f);

                report.statesReached[index] = IsState(animator, stateName);
                AnimatorClipInfo[] clips = animator.GetCurrentAnimatorClipInfo(0);
                report.clipsLoop[index] = clips.Length > 0 && clips[0].clip != null && clips[0].clip.isLooping;
                sameTransform &= Vector3.Distance(alignment.localPosition, position) < 0.0001f
                    && Quaternion.Angle(alignment.localRotation, rotation) < 0.01f
                    && Vector3.Distance(alignment.localScale, scale) < 0.0001f;
                rendererEnabled &= renderers.All(item => item != null && item.enabled && item.gameObject.activeInHierarchy);

                string screenshot = $"{Root}/Validation/Idle_B{selection}_SwordComparison.png";
                yield return Capture(screenshot);
                report.screenshots[index] = screenshot;
            }

            report.keys1To3Mapped = true;
            report.sameSwordTransform = sameTransform;
            report.rendererAlwaysEnabled = rendererEnabled;
            report.rootMotionDisabled = !animator.applyRootMotion;
            report.alignmentLocalPosition = Format(position);
            report.alignmentLocalEulerAngles = Format(rotation.eulerAngles);
            report.alignmentLocalScale = Format(scale);
            Finish(report);
        }

        private void Finish(SwordIdleComparisonReport report)
        {
            report.generatedAtUtc = DateTime.UtcNow.ToString("O");
            report.consoleErrors = errors.ToArray();
            report.passed = report.keys1To3Mapped
                && report.statesReached != null && report.statesReached.All(value => value)
                && report.clipsLoop != null && report.clipsLoop.All(value => value)
                && report.sameSwordTransform && report.rendererAlwaysEnabled
                && report.rootMotionDisabled && errors.Count == 0;
            File.WriteAllText(ToAbsolutePath(ReportPath), JsonUtility.ToJson(report, true));
            Application.logMessageReceived -= CaptureLog;
#if UNITY_EDITOR
            UnityEditor.EditorApplication.ExitPlaymode();
#endif
        }

        private static IEnumerator WaitForState(Animator animator, string stateName, float timeout)
        {
            float deadline = Time.realtimeSinceStartup + timeout;
            while (Time.realtimeSinceStartup < deadline && !IsState(animator, stateName)) yield return null;
        }

        private static bool IsState(Animator animator, string stateName) =>
            animator.GetCurrentAnimatorStateInfo(0).IsName(stateName) && !animator.IsInTransition(0);

        private static IEnumerator Capture(string assetPath)
        {
            string absolute = ToAbsolutePath(assetPath);
            Directory.CreateDirectory(Path.GetDirectoryName(absolute));
            ScreenCapture.CaptureScreenshot(absolute);
            yield return new WaitForEndOfFrame();
            yield return new WaitForSecondsRealtime(0.15f);
        }

        private static void FocusCamera(Camera camera, Transform player)
        {
            camera.orthographic = true;
            camera.orthographicSize = 2.25f;
            camera.transform.position = new Vector3(player.position.x, player.position.y + 1.15f, -20f);
            camera.transform.rotation = Quaternion.identity;
        }

        private static string Format(Vector3 value) => $"({value.x:F6}, {value.y:F6}, {value.z:F6})";
        private static string ToAbsolutePath(string assetPath) => Path.GetFullPath(Path.Combine(
            Application.dataPath, assetPath.Substring("Assets".Length).TrimStart('/', '\\')));
    }
}
