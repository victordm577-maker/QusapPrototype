using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEngine;

namespace Qusap.NewQusap75KAnimationTest.Playable.DoubleL.AutoLocomotion.Validation
{
    [Serializable]
    public sealed class AutoLocomotionValidationReport
    {
        public string generatedAtUtc;
        public string scene;
        public string prefab;
        public string controller;
        public string driver;
        public bool structuralValidationPassed;
        public bool idleB1WhenStill;
        public bool runForwardWhenMovingRight;
        public bool runForwardWhenMovingLeft;
        public bool returnsToIdleWhenStopped;
        public bool rollFromIdle;
        public bool rollFromRun;
        public bool rollReturnsToIdle;
        public bool rollReturnsToRun;
        public bool rollNotLooped;
        public bool rootMotionDisabled;
        public bool animationDidNotMoveRoot;
        public bool movementPhysicsAndInputsPreserved;
        public bool playerTwoUntouched;
        public float enterRunThreshold;
        public float exitRunThreshold;
        public float runPlaybackSpeed;
        public int consoleWarningCount;
        public int consoleErrorCount;
        public string[] consoleWarnings;
        public string[] consoleErrors;
        public string screenshot;
        public string notes;
        public bool passed;
    }

    [DisallowMultipleComponent]
    public sealed class QusapDoubleLAutoLocomotionValidation : MonoBehaviour
    {
        private const string Root = "Assets/_Qusap/Tests/NewQusap75KAnimationTest/Playable/DoubleL/AutoLocomotion";
        private const string ReportPath = Root + "/Validation/AutoLocomotionValidation.json";
        private const string ScreenshotPath = Root + "/Validation/AutoLocomotion.png";
        private const string MarkerName = "QusapDoubleLAutoLocomotionValidation.request";

        private readonly List<string> warnings = new();
        private readonly List<string> errors = new();

        private string MarkerPath => Path.Combine(
            Directory.GetParent(Application.dataPath)?.FullName ?? Application.dataPath,
            "Library", MarkerName);

        private void Awake()
        {
            if (!File.Exists(MarkerPath))
            {
                enabled = false;
                return;
            }

            File.Delete(MarkerPath);
            Application.logMessageReceived += CaptureLog;
            StartCoroutine(RunValidation());
        }

        private void OnDestroy()
        {
            Application.logMessageReceived -= CaptureLog;
        }

        private void CaptureLog(string condition, string stackTrace, LogType type)
        {
            if (type == LogType.Warning)
            {
                warnings.Add(condition);
            }
            else if (type == LogType.Error || type == LogType.Assert || type == LogType.Exception)
            {
                errors.Add(condition + (string.IsNullOrWhiteSpace(stackTrace) ? string.Empty : "\n" + stackTrace));
            }
        }

        private IEnumerator RunValidation()
        {
            yield return null;
            QusapDoubleLAutoLocomotionDriver driver = FindAnyObjectByType<QusapDoubleLAutoLocomotionDriver>();
            if (driver == null || driver.Animator == null || driver.TargetRigidbody == null)
            {
                Debug.LogError("AutoLocomotion validation could not find its configured driver.");
                yield return Finish(null);
                yield break;
            }

            Animator animator = driver.Animator;
            Rigidbody body = driver.TargetRigidbody;
            QusapHorizontalMotor horizontalMotor = driver.GetComponent<QusapHorizontalMotor>();
            QusapVerticalMotor verticalMotor = driver.GetComponent<QusapVerticalMotor>();
            QusapDashMotor dashMotor = driver.GetComponent<QusapDashMotor>();
            bool horizontalEnabled = horizontalMotor != null && horizontalMotor.enabled;
            bool verticalEnabled = verticalMotor != null && verticalMotor.enabled;
            bool dashEnabled = dashMotor != null && dashMotor.enabled;
            Vector3 originalVelocity = body.linearVelocity;
            Vector3 originalPosition = body.position;

            if (horizontalMotor != null) horizontalMotor.enabled = false;
            if (verticalMotor != null) verticalMotor.enabled = false;
            if (dashMotor != null) dashMotor.enabled = false;

            var report = LoadReport() ?? new AutoLocomotionValidationReport();
            report.enterRunThreshold = driver.EnterRunThreshold;
            report.exitRunThreshold = driver.ExitRunThreshold;
            report.runPlaybackSpeed = driver.RunPlaybackSpeed;
            report.rootMotionDisabled = !animator.applyRootMotion;

            body.linearVelocity = Vector3.zero;
            driver.RefreshFromRigidbody();
            yield return WaitForState(animator, "CombatIdle_B1", 1.0f);
            report.idleB1WhenStill = IsState(animator, "CombatIdle_B1");

            body.linearVelocity = new Vector3(1f, 0f, 0f);
            driver.RefreshFromRigidbody();
            yield return WaitForLocomotionState(driver, body, animator, "RunForward", 1f, 1.5f);
            report.runForwardWhenMovingRight = IsState(animator, "RunForward");

            body.linearVelocity = new Vector3(-1f, 0f, 0f);
            driver.RefreshFromRigidbody();
            yield return WaitForLocomotionState(driver, body, animator, "RunForward", -1f, 1.5f);
            report.runForwardWhenMovingLeft = IsState(animator, "RunForward");

            string screenshotAbsolute = ToAbsoluteAssetPath(ScreenshotPath);
            Directory.CreateDirectory(Path.GetDirectoryName(screenshotAbsolute));
            ScreenCapture.CaptureScreenshot(screenshotAbsolute);
            report.screenshot = ScreenshotPath;
            yield return new WaitForEndOfFrame();

            body.linearVelocity = Vector3.zero;
            driver.RefreshFromRigidbody();
            yield return WaitForState(animator, "CombatIdle_B1", 1.0f);
            report.returnsToIdleWhenStopped = IsState(animator, "CombatIdle_B1");

            Vector3 rootBeforeRoll = driver.transform.position;
            driver.RequestRoll();
            yield return WaitForState(animator, "RollFront", 1.0f);
            report.rollFromIdle = IsState(animator, "RollFront");
            yield return WaitForState(animator, "CombatIdle_B1", 3.0f);
            report.rollReturnsToIdle = IsState(animator, "CombatIdle_B1");
            report.animationDidNotMoveRoot = Vector2.Distance(
                new Vector2(rootBeforeRoll.x, rootBeforeRoll.z),
                new Vector2(driver.transform.position.x, driver.transform.position.z)) < 0.01f;

            body.linearVelocity = new Vector3(1f, 0f, 0f);
            driver.RefreshFromRigidbody();
            yield return WaitForLocomotionState(driver, body, animator, "RunForward", 1f, 1.5f);
            driver.RequestRoll();
            yield return WaitForState(animator, "RollFront", 1.0f);
            report.rollFromRun = IsState(animator, "RollFront");
            yield return WaitForState(animator, "RunForward", 3.0f);
            report.rollReturnsToRun = IsState(animator, "RunForward");
            report.rollNotLooped = report.rollReturnsToIdle && report.rollReturnsToRun;

            body.linearVelocity = Vector3.zero;
            body.position = originalPosition;
            if (horizontalMotor != null) horizontalMotor.enabled = horizontalEnabled;
            if (verticalMotor != null) verticalMotor.enabled = verticalEnabled;
            if (dashMotor != null) dashMotor.enabled = dashEnabled;

            report.consoleWarnings = warnings.ToArray();
            report.consoleErrors = errors.ToArray();
            report.consoleWarningCount = warnings.Count;
            report.consoleErrorCount = errors.Count;
            report.generatedAtUtc = DateTime.UtcNow.ToString("O");
            report.notes = "Play Mode injected only test velocities after temporarily disabling motors; the production driver only reads Rigidbody.linearVelocity.x. Original runtime values were restored.";
            report.passed = report.structuralValidationPassed
                && report.idleB1WhenStill && report.runForwardWhenMovingRight
                && report.runForwardWhenMovingLeft && report.returnsToIdleWhenStopped
                && report.rollFromIdle && report.rollFromRun && report.rollReturnsToIdle
                && report.rollReturnsToRun && report.rollNotLooped
                && report.rootMotionDisabled && report.animationDidNotMoveRoot
                && report.movementPhysicsAndInputsPreserved && report.playerTwoUntouched
                && report.consoleErrorCount == 0;

            File.WriteAllText(ToAbsoluteAssetPath(ReportPath), JsonUtility.ToJson(report, true));
            yield return new WaitForSecondsRealtime(0.25f);
            Application.logMessageReceived -= CaptureLog;
#if UNITY_EDITOR
            UnityEditor.EditorApplication.ExitPlaymode();
#endif
        }

        private IEnumerator Finish(AutoLocomotionValidationReport report)
        {
            report ??= LoadReport() ?? new AutoLocomotionValidationReport();
            report.consoleWarnings = warnings.ToArray();
            report.consoleErrors = errors.ToArray();
            report.consoleWarningCount = warnings.Count;
            report.consoleErrorCount = errors.Count;
            report.generatedAtUtc = DateTime.UtcNow.ToString("O");
            report.passed = false;
            File.WriteAllText(ToAbsoluteAssetPath(ReportPath), JsonUtility.ToJson(report, true));
            yield return null;
#if UNITY_EDITOR
            UnityEditor.EditorApplication.ExitPlaymode();
#endif
        }

        private static IEnumerator WaitForState(Animator animator, string stateName, float timeout)
        {
            float deadline = Time.realtimeSinceStartup + timeout;
            while (Time.realtimeSinceStartup < deadline && !IsState(animator, stateName))
            {
                yield return null;
            }
        }

        private static IEnumerator WaitForLocomotionState(
            QusapDoubleLAutoLocomotionDriver driver,
            Rigidbody body,
            Animator animator,
            string stateName,
            float horizontalVelocity,
            float timeout)
        {
            float deadline = Time.realtimeSinceStartup + timeout;
            while (Time.realtimeSinceStartup < deadline && !IsState(animator, stateName))
            {
                Vector3 velocity = body.linearVelocity;
                velocity.x = horizontalVelocity;
                velocity.z = 0f;
                body.linearVelocity = velocity;
                driver.RefreshFromRigidbody();
                yield return null;
            }
        }

        private static bool IsState(Animator animator, string stateName)
        {
            return animator.GetCurrentAnimatorStateInfo(0).IsName(stateName)
                && !animator.IsInTransition(0);
        }

        private static AutoLocomotionValidationReport LoadReport()
        {
            string path = ToAbsoluteAssetPath(ReportPath);
            return File.Exists(path)
                ? JsonUtility.FromJson<AutoLocomotionValidationReport>(File.ReadAllText(path))
                : null;
        }

        private static string ToAbsoluteAssetPath(string assetPath)
        {
            return Path.GetFullPath(Path.Combine(Application.dataPath,
                assetPath.Substring("Assets".Length).TrimStart('/', '\\')));
        }
    }
}
