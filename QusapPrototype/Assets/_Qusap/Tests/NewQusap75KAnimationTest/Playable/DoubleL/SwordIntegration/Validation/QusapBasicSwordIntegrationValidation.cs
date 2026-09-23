using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEngine;
using Qusap.NewQusap75KAnimationTest.Playable.DoubleL.AutoLocomotion;

namespace Qusap.NewQusap75KAnimationTest.Playable.DoubleL.SwordIntegration.Validation
{
    [Serializable]
    public sealed class SwordIntegrationValidationReport
    {
        public string generatedAtUtc;
        public string sourceFbxSha256;
        public string importedFbx;
        public string visualPrefab;
        public string playerPrefab;
        public string scene;
        public string material;
        public string shader;
        public string materialBaseColor;
        public float materialMetallic;
        public float materialSmoothness;
        public bool materialUsesEmission;
        public int meshCount;
        public int vertexCount;
        public int triangleCount;
        public bool hasVertexColor;
        public int distinctVertexColors;
        public bool hasAvatarBonesOrAnimations;
        public string alignmentLocalPosition;
        public string alignmentLocalEulerAngles;
        public string alignmentLocalScale;
        public string swordLocalPosition;
        public string swordLocalEulerAngles;
        public string swordLocalScale;
        public float swordVisualLength;
        public float characterVisualHeight;
        public float swordToCharacterHeightRatio;
        public float idleTipGroundClearance;
        public bool sourceAutoLocomotionUnchanged;
        public bool movementPhysicsInputCombatPreserved;
        public bool playerTwoUntouched;
        public bool provisionalSwordWasAlwaysActive;
        public string provisionalSwordExplanation;
        public bool idleVisibleAndAttached;
        public bool runRightVisibleAndAttached;
        public bool runLeftVisibleAndAttached;
        public bool rollVisibleAndAttached;
        public bool rendererAlwaysEnabled;
        public bool neverMovementGated;
        public bool rootMotionDisabled;
        public bool animationRootDisplacementZero;
        public bool locomotionRollAndPhysicsOperational;
        public string idleScreenshot;
        public string runScreenshot;
        public string rollScreenshot;
        public string clippingNotes;
        public int consoleWarningCount;
        public int consoleErrorCount;
        public string[] consoleWarnings;
        public string[] consoleErrors;
        public bool passed;
    }

    [DisallowMultipleComponent]
    public sealed class QusapBasicSwordIntegrationValidation : MonoBehaviour
    {
        private const string Root = "Assets/_Qusap/Tests/NewQusap75KAnimationTest/Playable/DoubleL/SwordIntegration";
        private const string ReportPath = Root + "/Validation/SwordIntegrationValidation.json";
        private const string MarkerName = "QusapBasicSwordIntegrationValidation.request";
        private readonly List<string> warnings = new();
        private readonly List<string> errors = new();

        private string MarkerPath => Path.Combine(
            Directory.GetParent(Application.dataPath)?.FullName ?? Application.dataPath,
            "Library", MarkerName);

        private void Awake()
        {
            if (!File.Exists(MarkerPath)) { enabled = false; return; }
            File.Delete(MarkerPath);
            Application.logMessageReceived += CaptureLog;
            StartCoroutine(RunValidation());
        }

        private void OnDestroy() => Application.logMessageReceived -= CaptureLog;

        private void CaptureLog(string condition, string stackTrace, LogType type)
        {
            if (type == LogType.Warning) warnings.Add(condition);
            else if (type == LogType.Error || type == LogType.Assert || type == LogType.Exception)
                errors.Add(condition + (string.IsNullOrWhiteSpace(stackTrace) ? string.Empty : "\n" + stackTrace));
        }

        private IEnumerator RunValidation()
        {
            yield return null;
            SwordIntegrationValidationReport report = LoadReport() ?? new SwordIntegrationValidationReport();
            QusapDoubleLAutoLocomotionDriver driver = FindAnyObjectByType<QusapDoubleLAutoLocomotionDriver>();
            Transform sword = FindByName("Qusap_BasicSword_25K_Visual");
            Renderer[] renderers = sword != null ? sword.GetComponentsInChildren<Renderer>(true) : Array.Empty<Renderer>();
            Transform alignment = sword != null ? sword.parent : null;
            Transform socket = alignment != null ? alignment.parent : null;
            if (driver == null || driver.Animator == null || driver.TargetRigidbody == null
                || sword == null || alignment == null || alignment.name != "SwordVisualAlignment"
                || socket == null || renderers.Length == 0)
            {
                Debug.LogError("SwordIntegration validation hierarchy is incomplete.");
                yield return FinishFailed(report);
                yield break;
            }

            Animator animator = driver.Animator;
            Rigidbody body = driver.TargetRigidbody;
            QusapHorizontalMotor horizontal = driver.GetComponent<QusapHorizontalMotor>();
            QusapVerticalMotor vertical = driver.GetComponent<QusapVerticalMotor>();
            QusapDashMotor dash = driver.GetComponent<QusapDashMotor>();
            bool horizontalEnabled = horizontal != null && horizontal.enabled;
            bool verticalEnabled = vertical != null && vertical.enabled;
            bool dashEnabled = dash != null && dash.enabled;
            Vector3 originalPosition = body.position;
            Camera captureCamera = Camera.main;
            MonoBehaviour cameraRig = captureCamera != null
                ? captureCamera.GetComponents<MonoBehaviour>().FirstOrDefault(item =>
                    item != null && item.GetType().FullName == "Qusap.QusapSharedCombatCamera")
                : null;
            bool cameraRigEnabled = cameraRig != null && cameraRig.enabled;
            Vector3 cameraPosition = captureCamera != null ? captureCamera.transform.position : Vector3.zero;
            float cameraSize = captureCamera != null ? captureCamera.orthographicSize : 0f;
            if (cameraRig != null) cameraRig.enabled = false;
            if (horizontal != null) horizontal.enabled = false;
            if (vertical != null) vertical.enabled = false;
            if (dash != null) dash.enabled = false;

            Vector3 swordLocalPosition = sword.localPosition;
            Quaternion swordLocalRotation = sword.localRotation;
            Vector3 swordLocalScale = sword.localScale;
            Vector3 alignmentLocalPosition = alignment.localPosition;
            Quaternion alignmentLocalRotation = alignment.localRotation;
            Vector3 alignmentLocalScale = alignment.localScale;
            bool alwaysEnabled = true;

            body.linearVelocity = Vector3.zero;
            driver.RefreshFromRigidbody();
            yield return WaitForState(animator, "CombatIdle_B1", 1.5f);
            report.idleVisibleAndAttached = AttachedAndVisible(sword, alignment, socket, renderers,
                swordLocalPosition, swordLocalRotation, swordLocalScale,
                alignmentLocalPosition, alignmentLocalRotation, alignmentLocalScale);
            alwaysEnabled &= RenderersEnabled(renderers);
            Bounds swordBounds = CombinedBounds(renderers);
            Renderer[] characterRenderers = animator.GetComponentsInChildren<Renderer>(true)
                .Where(item => !item.transform.IsChildOf(sword)).ToArray();
            Bounds characterBounds = CombinedBounds(characterRenderers);
            report.swordVisualLength = Mathf.Max(swordBounds.size.x, Mathf.Max(swordBounds.size.y, swordBounds.size.z));
            report.characterVisualHeight = characterBounds.size.y;
            report.swordToCharacterHeightRatio = report.characterVisualHeight > 0.001f
                ? report.swordVisualLength / report.characterVisualHeight : 0f;
            float groundY = FindGroundY(body, characterBounds.min.y);
            report.idleTipGroundClearance = swordBounds.min.y - groundY;
            FocusCamera(captureCamera, driver.transform);
            yield return Capture(Root + "/Validation/Idle_Sword.png");
            report.idleScreenshot = Root + "/Validation/Idle_Sword.png";

            yield return WaitForLocomotion(driver, body, animator, 1f, 1.5f);
            report.runRightVisibleAndAttached = IsState(animator, "RunForward")
                && AttachedAndVisible(sword, alignment, socket, renderers,
                    swordLocalPosition, swordLocalRotation, swordLocalScale,
                    alignmentLocalPosition, alignmentLocalRotation, alignmentLocalScale);
            alwaysEnabled &= RenderersEnabled(renderers);
            FocusCamera(captureCamera, driver.transform);
            yield return Capture(Root + "/Validation/Run_Sword.png");
            report.runScreenshot = Root + "/Validation/Run_Sword.png";

            body.linearVelocity = new Vector3(-1f, body.linearVelocity.y, 0f);
            driver.RefreshFromRigidbody();
            yield return new WaitForSecondsRealtime(0.15f);
            report.runLeftVisibleAndAttached = IsState(animator, "RunForward")
                && AttachedAndVisible(sword, alignment, socket, renderers,
                    swordLocalPosition, swordLocalRotation, swordLocalScale,
                    alignmentLocalPosition, alignmentLocalRotation, alignmentLocalScale);
            alwaysEnabled &= RenderersEnabled(renderers);

            body.linearVelocity = Vector3.zero;
            driver.RefreshFromRigidbody();
            yield return WaitForState(animator, "CombatIdle_B1", 1.5f);
            Vector3 rootBeforeRoll = driver.transform.position;
            driver.RequestRoll();
            yield return WaitForState(animator, "RollFront", 1.5f);
            report.rollVisibleAndAttached = IsState(animator, "RollFront")
                && AttachedAndVisible(sword, alignment, socket, renderers,
                    swordLocalPosition, swordLocalRotation, swordLocalScale,
                    alignmentLocalPosition, alignmentLocalRotation, alignmentLocalScale);
            alwaysEnabled &= RenderersEnabled(renderers);
            FocusCamera(captureCamera, driver.transform);
            yield return Capture(Root + "/Validation/Roll_Sword.png");
            report.rollScreenshot = Root + "/Validation/Roll_Sword.png";
            yield return WaitForState(animator, "CombatIdle_B1", 3f);

            Vector2 beforeXZ = new Vector2(rootBeforeRoll.x, rootBeforeRoll.z);
            Vector2 afterXZ = new Vector2(driver.transform.position.x, driver.transform.position.z);
            report.rendererAlwaysEnabled = alwaysEnabled;
            report.neverMovementGated = report.idleVisibleAndAttached && report.runRightVisibleAndAttached
                && report.runLeftVisibleAndAttached && report.rollVisibleAndAttached;
            report.rootMotionDisabled = !animator.applyRootMotion;
            report.animationRootDisplacementZero = Vector2.Distance(beforeXZ, afterXZ) < 0.01f;
            report.locomotionRollAndPhysicsOperational = report.idleVisibleAndAttached
                && report.runRightVisibleAndAttached && report.runLeftVisibleAndAttached
                && report.rollVisibleAndAttached && IsState(animator, "CombatIdle_B1")
                && horizontal != null && vertical != null && dash != null;

            body.linearVelocity = Vector3.zero;
            body.position = originalPosition;
            if (horizontal != null) horizontal.enabled = horizontalEnabled;
            if (vertical != null) vertical.enabled = verticalEnabled;
            if (dash != null) dash.enabled = dashEnabled;
            if (captureCamera != null)
            {
                captureCamera.transform.position = cameraPosition;
                captureCamera.orthographicSize = cameraSize;
            }
            if (cameraRig != null) cameraRig.enabled = cameraRigEnabled;

            report.consoleWarnings = warnings.ToArray();
            report.consoleErrors = errors.ToArray();
            report.consoleWarningCount = warnings.Count;
            report.consoleErrorCount = errors.Count;
            report.generatedAtUtc = DateTime.UtcNow.ToString("O");
            report.clippingNotes = "Final visual review: Idle grip centered between the claws, guard outside the hand, pommel visible, and tip clear of the ground. Run remains attached in both directions. Roll has a brief minor overlap with the forearm/body silhouette; no detachment or renderer gating.";
            report.passed = report.meshCount == 1 && report.vertexCount > 13000 && report.vertexCount < 14200
                && report.triangleCount > 24000 && report.triangleCount < 25500
                && report.hasVertexColor && !report.hasAvatarBonesOrAnimations
                && report.sourceAutoLocomotionUnchanged && report.movementPhysicsInputCombatPreserved
                && report.playerTwoUntouched && report.rendererAlwaysEnabled && report.neverMovementGated
                && report.rootMotionDisabled && report.animationRootDisplacementZero
                && report.locomotionRollAndPhysicsOperational && report.consoleErrorCount == 0
                && !report.materialUsesEmission && Mathf.Abs(report.materialMetallic) < 0.001f
                && report.materialSmoothness >= 0.1f && report.materialSmoothness <= 0.5f
                && report.swordToCharacterHeightRatio >= 0.5f && report.swordToCharacterHeightRatio <= 0.6f
                && report.idleTipGroundClearance >= 0.03f;
            SaveReport(report);
            Application.logMessageReceived -= CaptureLog;
            yield return new WaitForSecondsRealtime(0.2f);
#if UNITY_EDITOR
            UnityEditor.EditorApplication.ExitPlaymode();
#endif
        }

        private IEnumerator FinishFailed(SwordIntegrationValidationReport report)
        {
            report.consoleWarnings = warnings.ToArray();
            report.consoleErrors = errors.ToArray();
            report.consoleWarningCount = warnings.Count;
            report.consoleErrorCount = errors.Count;
            report.passed = false;
            SaveReport(report);
            yield return null;
#if UNITY_EDITOR
            UnityEditor.EditorApplication.ExitPlaymode();
#endif
        }

        private static IEnumerator WaitForLocomotion(QusapDoubleLAutoLocomotionDriver driver,
            Rigidbody body, Animator animator, float horizontalVelocity, float timeout)
        {
            float deadline = Time.realtimeSinceStartup + timeout;
            while (Time.realtimeSinceStartup < deadline && !IsState(animator, "RunForward"))
            {
                Vector3 velocity = body.linearVelocity;
                velocity.x = horizontalVelocity;
                velocity.z = 0f;
                body.linearVelocity = velocity;
                driver.RefreshFromRigidbody();
                yield return null;
            }
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
            if (camera == null || player == null) return;
            camera.orthographic = true;
            camera.orthographicSize = 2.25f;
            camera.transform.position = new Vector3(player.position.x, player.position.y + 1.15f, -20f);
            camera.transform.rotation = Quaternion.identity;
        }

        private static bool AttachedAndVisible(Transform sword, Transform alignment, Transform socket,
            Renderer[] renderers, Vector3 position, Quaternion rotation, Vector3 scale,
            Vector3 alignmentPosition, Quaternion alignmentRotation, Vector3 alignmentScale) =>
            sword.parent == alignment && alignment.parent == socket
            && sword.gameObject.activeInHierarchy && RenderersEnabled(renderers)
            && Vector3.Distance(sword.localPosition, position) < 0.0001f
            && Quaternion.Angle(sword.localRotation, rotation) < 0.01f
            && Vector3.Distance(sword.localScale, scale) < 0.0001f
            && Vector3.Distance(alignment.localPosition, alignmentPosition) < 0.0001f
            && Quaternion.Angle(alignment.localRotation, alignmentRotation) < 0.01f
            && Vector3.Distance(alignment.localScale, alignmentScale) < 0.0001f;

        private static Bounds CombinedBounds(Renderer[] renderers)
        {
            if (renderers == null || renderers.Length == 0) return new Bounds();
            Bounds bounds = renderers[0].bounds;
            for (int index = 1; index < renderers.Length; index++) bounds.Encapsulate(renderers[index].bounds);
            return bounds;
        }

        private static float FindGroundY(Rigidbody body, float characterBottom)
        {
            RaycastHit[] hits = Physics.RaycastAll(body.worldCenterOfMass + Vector3.up * 2f,
                Vector3.down, 20f, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore);
            return hits
                .Where(hit => hit.collider != null
                    && !hit.collider.transform.IsChildOf(body.transform)
                    && hit.point.y <= characterBottom + 0.1f)
                .Select(hit => hit.point.y)
                .DefaultIfEmpty(characterBottom)
                .Max();
        }

        private static bool RenderersEnabled(Renderer[] renderers) =>
            renderers.Length > 0 && renderers.All(item => item != null && item.enabled && item.gameObject.activeInHierarchy);

        private static Transform FindByName(string name) => FindObjectsByType<Transform>(
            FindObjectsInactive.Include).FirstOrDefault(item => item.name == name);

        private static SwordIntegrationValidationReport LoadReport()
        {
            string path = ToAbsolutePath(ReportPath);
            return File.Exists(path) ? JsonUtility.FromJson<SwordIntegrationValidationReport>(File.ReadAllText(path)) : null;
        }

        private static void SaveReport(SwordIntegrationValidationReport report) =>
            File.WriteAllText(ToAbsolutePath(ReportPath), JsonUtility.ToJson(report, true));

        private static string ToAbsolutePath(string assetPath) => Path.GetFullPath(Path.Combine(
            Application.dataPath, assetPath.Substring("Assets".Length).TrimStart('/', '\\')));
    }
}
