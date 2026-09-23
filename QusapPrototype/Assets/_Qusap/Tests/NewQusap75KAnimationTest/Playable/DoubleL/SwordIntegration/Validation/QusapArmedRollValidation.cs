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
    public sealed class ArmedRollValidationReport
    {
        public string generatedAtUtc;
        public string assetPath;
        public string assetGuid;
        public string clipName;
        public string avatarPath;
        public string avatarGuid;
        public bool humanoid;
        public bool copyFromOtherAvatar;
        public bool inPlaceVariant;
        public bool loopDisabled;
        public float fps;
        public float sourceDurationSeconds;
        public float stateSpeed;
        public float effectiveDurationSeconds;
        public bool applyRootMotionDisabled;
        public bool idleRollCompletedWithoutInterruption;
        public bool idleRollReturnedToIdle;
        public bool runRollCompletedWithoutInterruption;
        public bool runRollReturnedToRun;
        public bool returnedToIdleAfterStopping;
        public bool jumpComponentEnabled;
        public bool dashComponentEnabled;
        public bool alignmentUnchanged;
        public bool rendererAlwaysEnabled;
        public bool swordAlwaysParentedToAlignment;
        public bool gripRemainedInsideVisibleClaw;
        public bool severeSwordBodyOrGroundClipping;
        public bool visibleSwordSeparation;
        public float maximumAnimatorRootLocalDisplacement;
        public float maximumHandToAlignmentDistance;
        public string[] screenshots;
        public string[] consoleErrors;
        public bool automatedChecksPassed;
        public string visualClassification;
        public string visualNotes;
    }

    [DisallowMultipleComponent]
    public sealed class QusapArmedRollValidation : MonoBehaviour
    {
        private const string TestRoot = "Assets/_Qusap/Tests/NewQusap75KAnimationTest";
        private const string Root = TestRoot + "/Playable/DoubleL/SwordIntegration";
        private const string ReportPath = Root + "/Validation/ArmedRollValidation.json";
        private const string MarkerName = "QusapArmedRollValidation.request";
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
            if (type == LogType.Error || type == LogType.Assert || type == LogType.Exception)
                errors.Add(condition + (string.IsNullOrWhiteSpace(stackTrace) ? string.Empty : "\n" + stackTrace));
        }

        private IEnumerator RunValidation()
        {
            yield return null;
            var report = new ArmedRollValidationReport
            {
                assetPath = TestRoot + "/Imported/DoubleL/OneHandBase/Roll/Base/InPlace/1Hand_Base_Roll_F_InPlace.fbx",
                assetGuid = "1289a380d63c625488042925bae18207",
                clipName = "1Hand_Base_Roll_F_InPlace",
                avatarPath = TestRoot + "/Imported/DoubleL/OneHandBase/Model/T-Pose.fbx",
                avatarGuid = "af0adcb624545c24e8c836635d07b769",
                humanoid = true,
                copyFromOtherAvatar = true,
                inPlaceVariant = true,
                loopDisabled = true,
                fps = 30f,
                sourceDurationSeconds = 55f / 30f,
                stateSpeed = 1.5f,
                effectiveDurationSeconds = (55f / 30f) / 1.5f,
                screenshots = new string[5],
                visualClassification = "NO APROBADA",
                visualNotes = "The grip does not remain inside the visible claw. The sword separates from the hand, disappears behind/outside the body during the middle of the roll, and reaches the ground near 75%."
            };

            QusapDoubleLAutoLocomotionDriver driver = FindAnyObjectByType<QusapDoubleLAutoLocomotionDriver>();
            Transform alignment = FindObjectsByType<Transform>(FindObjectsInactive.Include)
                .FirstOrDefault(item => item.name == "LargeGripAlignment");
            Transform sword = FindObjectsByType<Transform>(FindObjectsInactive.Include)
                .FirstOrDefault(item => item.name == "Qusap_BasicSword_LargeGrip_25K_Visual");
            Camera camera = Camera.main;
            if (driver == null || driver.Animator == null || driver.TargetRigidbody == null
                || alignment == null || sword == null || camera == null)
            {
                errors.Add("SwordIntegration armed-roll hierarchy is incomplete.");
                Finish(report);
                yield break;
            }

            Animator animator = driver.Animator;
            Rigidbody body = driver.TargetRigidbody;
            Renderer[] swordRenderers = sword.GetComponentsInChildren<Renderer>(true);
            QusapHorizontalMotor horizontal = driver.GetComponent<QusapHorizontalMotor>();
            QusapVerticalMotor vertical = driver.GetComponent<QusapVerticalMotor>();
            QusapDashMotor dash = driver.GetComponent<QusapDashMotor>();
            report.jumpComponentEnabled = vertical != null && vertical.enabled;
            report.dashComponentEnabled = dash != null && dash.enabled;
            report.applyRootMotionDisabled = !animator.applyRootMotion;

            Vector3 originalBodyPosition = body.position;
            Quaternion originalBodyRotation = body.rotation;
            Vector3 originalVelocity = body.linearVelocity;
            bool originalGravity = body.useGravity;
            bool horizontalEnabled = horizontal != null && horizontal.enabled;
            bool verticalEnabled = vertical != null && vertical.enabled;
            bool dashEnabled = dash != null && dash.enabled;
            Vector3 originalRootLocalPosition = animator.transform.localPosition;
            Vector3 alignmentPosition = alignment.localPosition;
            Quaternion alignmentRotation = alignment.localRotation;
            Vector3 alignmentScale = alignment.localScale;
            float originalAnimatorSpeed = animator.speed;
            Vector3 cameraPosition = camera.transform.position;
            Quaternion cameraRotation = camera.transform.rotation;
            float cameraSize = camera.orthographicSize;
            bool cameraOrthographic = camera.orthographic;
            MonoBehaviour cameraRig = camera.GetComponents<MonoBehaviour>().FirstOrDefault(item =>
                item != null && item.GetType().FullName == "Qusap.QusapSharedCombatCamera");
            bool cameraRigEnabled = cameraRig != null && cameraRig.enabled;

            if (cameraRig != null) cameraRig.enabled = false;
            if (horizontal != null) horizontal.enabled = false;
            if (vertical != null) vertical.enabled = false;
            if (dash != null) dash.enabled = false;
            body.useGravity = false;
            body.linearVelocity = Vector3.zero;
            body.position = originalBodyPosition + Vector3.back * 6f;
            Physics.SyncTransforms();
            FocusCamera(camera, body.transform);

            bool alignmentUnchanged = true;
            bool renderersEnabled = swordRenderers.Length > 0;
            bool parented = true;
            float maxRootDisplacement = 0f;
            float maxHandDistance = 0f;
            Transform rightHand = animator.GetBoneTransform(HumanBodyBones.RightHand);

            driver.RefreshFromRigidbody();
            yield return WaitForState(animator, "CombatIdle_B1", 2f);
            driver.RequestRoll();
            yield return WaitForState(animator, "RollFront", 2f);
            bool idleInterrupted = false;
            yield return MonitorRoll(animator, alignment, sword, swordRenderers, rightHand,
                originalRootLocalPosition, alignmentPosition, alignmentRotation, alignmentScale,
                values =>
                {
                    idleInterrupted |= values.interrupted;
                    alignmentUnchanged &= values.alignmentUnchanged;
                    renderersEnabled &= values.renderersEnabled;
                    parented &= values.parented;
                    maxRootDisplacement = Mathf.Max(maxRootDisplacement, values.rootDisplacement);
                    maxHandDistance = Mathf.Max(maxHandDistance, values.handDistance);
                });
            report.idleRollCompletedWithoutInterruption = !idleInterrupted;
            yield return WaitForState(animator, "CombatIdle_B1", 1f);
            report.idleRollReturnedToIdle = IsState(animator, "CombatIdle_B1");

            Renderer[] characterRenderers = animator.GetComponentsInChildren<Renderer>(true)
                .Where(item => !item.transform.IsChildOf(sword)).ToArray();
            Renderer[] externalRenderers = FindObjectsByType<Renderer>(FindObjectsInactive.Exclude)
                .Where(item => !item.transform.IsChildOf(driver.transform.root)).ToArray();
            bool[] externalRendererStates = externalRenderers.Select(item => item.enabled).ToArray();
            foreach (Renderer item in externalRenderers) item.enabled = false;
            animator.speed = 0f;
            float[] samples = { 0.01f, 0.25f, 0.50f, 0.75f, 0.99f };
            string[] labels = { "Start", "25", "50", "75", "Final" };
            for (int index = 0; index < samples.Length; index++)
            {
                animator.Play("RollFront", 0, samples[index]);
                animator.Update(0f);
                Physics.SyncTransforms();
                Bounds framingBounds = CombinedBounds(characterRenderers);
                framingBounds.Encapsulate(CombinedBounds(swordRenderers));
                FocusCameraOnBounds(camera, framingBounds);
                UpdateInvariants(animator, alignment, sword, swordRenderers, rightHand,
                    originalRootLocalPosition, alignmentPosition, alignmentRotation, alignmentScale,
                    ref alignmentUnchanged, ref renderersEnabled, ref parented,
                    ref maxRootDisplacement, ref maxHandDistance);
                report.screenshots[index] = Root + "/Validation/ArmedRoll_" + labels[index] + ".png";
                yield return Capture(report.screenshots[index]);
            }
            animator.speed = originalAnimatorSpeed;
            for (int index = 0; index < externalRenderers.Length; index++)
                if (externalRenderers[index] != null) externalRenderers[index].enabled = externalRendererStates[index];

            animator.Play("CombatIdle_B1", 0, 0f);
            animator.Update(0f);
            body.linearVelocity = Vector3.right * 2f;
            driver.RefreshFromRigidbody();
            yield return WaitForState(animator, "RunForward", 2f);
            driver.RequestRoll();
            yield return WaitForState(animator, "RollFront", 2f);
            bool runInterrupted = false;
            yield return MonitorRoll(animator, alignment, sword, swordRenderers, rightHand,
                originalRootLocalPosition, alignmentPosition, alignmentRotation, alignmentScale,
                values =>
                {
                    runInterrupted |= values.interrupted;
                    alignmentUnchanged &= values.alignmentUnchanged;
                    renderersEnabled &= values.renderersEnabled;
                    parented &= values.parented;
                    maxRootDisplacement = Mathf.Max(maxRootDisplacement, values.rootDisplacement);
                    maxHandDistance = Mathf.Max(maxHandDistance, values.handDistance);
                });
            report.runRollCompletedWithoutInterruption = !runInterrupted;
            yield return WaitForState(animator, "RunForward", 1f);
            report.runRollReturnedToRun = IsState(animator, "RunForward");
            body.linearVelocity = Vector3.zero;
            driver.RefreshFromRigidbody();
            yield return WaitForState(animator, "CombatIdle_B1", 2f);
            report.returnedToIdleAfterStopping = IsState(animator, "CombatIdle_B1");

            report.alignmentUnchanged = alignmentUnchanged;
            report.rendererAlwaysEnabled = renderersEnabled;
            report.swordAlwaysParentedToAlignment = parented;
            report.gripRemainedInsideVisibleClaw = false;
            report.severeSwordBodyOrGroundClipping = true;
            report.visibleSwordSeparation = true;
            report.maximumAnimatorRootLocalDisplacement = maxRootDisplacement;
            report.maximumHandToAlignmentDistance = maxHandDistance;

            body.linearVelocity = originalVelocity;
            body.position = originalBodyPosition;
            body.rotation = originalBodyRotation;
            body.useGravity = originalGravity;
            animator.speed = originalAnimatorSpeed;
            if (horizontal != null) horizontal.enabled = horizontalEnabled;
            if (vertical != null) vertical.enabled = verticalEnabled;
            if (dash != null) dash.enabled = dashEnabled;
            camera.transform.SetPositionAndRotation(cameraPosition, cameraRotation);
            camera.orthographicSize = cameraSize;
            camera.orthographic = cameraOrthographic;
            if (cameraRig != null) cameraRig.enabled = cameraRigEnabled;
            Physics.SyncTransforms();

            report.automatedChecksPassed = report.applyRootMotionDisabled
                && report.idleRollCompletedWithoutInterruption && report.idleRollReturnedToIdle
                && report.runRollCompletedWithoutInterruption && report.runRollReturnedToRun
                && report.returnedToIdleAfterStopping && report.jumpComponentEnabled && report.dashComponentEnabled
                && report.alignmentUnchanged && report.rendererAlwaysEnabled
                && report.swordAlwaysParentedToAlignment
                && report.maximumAnimatorRootLocalDisplacement < 0.001f && errors.Count == 0;
            Finish(report);
        }

        private struct SampleValues
        {
            public bool interrupted;
            public bool alignmentUnchanged;
            public bool renderersEnabled;
            public bool parented;
            public float rootDisplacement;
            public float handDistance;
        }

        private static IEnumerator MonitorRoll(Animator animator, Transform alignment, Transform sword,
            Renderer[] renderers, Transform rightHand, Vector3 rootPosition, Vector3 alignmentPosition,
            Quaternion alignmentRotation, Vector3 alignmentScale, Action<SampleValues> sample)
        {
            float deadline = Time.realtimeSinceStartup + 4f;
            bool observedRoll = false;
            while (Time.realtimeSinceStartup < deadline)
            {
                bool inRoll = IsState(animator, "RollFront") || (animator.IsInTransition(0)
                    && animator.GetNextAnimatorStateInfo(0).IsName("RollFront"));
                if (inRoll) observedRoll = true;
                else if (observedRoll) break;

                var values = new SampleValues
                {
                    interrupted = observedRoll && !inRoll && animator.GetCurrentAnimatorStateInfo(0).normalizedTime < 0.99f,
                    alignmentUnchanged = Vector3.Distance(alignment.localPosition, alignmentPosition) < 0.0001f
                        && Quaternion.Angle(alignment.localRotation, alignmentRotation) < 0.01f
                        && Vector3.Distance(alignment.localScale, alignmentScale) < 0.0001f,
                    renderersEnabled = renderers.All(item => item != null && item.enabled && item.gameObject.activeInHierarchy),
                    parented = sword.parent == alignment && sword.gameObject.activeInHierarchy,
                    rootDisplacement = Vector3.Distance(animator.transform.localPosition, rootPosition),
                    handDistance = rightHand != null ? Vector3.Distance(rightHand.position, alignment.position) : float.MaxValue
                };
                sample(values);
                yield return null;
            }
        }

        private static void UpdateInvariants(Animator animator, Transform alignment, Transform sword,
            Renderer[] renderers, Transform rightHand, Vector3 rootPosition, Vector3 alignmentPosition,
            Quaternion alignmentRotation, Vector3 alignmentScale, ref bool alignmentUnchanged,
            ref bool renderersEnabled, ref bool parented, ref float maxRootDisplacement, ref float maxHandDistance)
        {
            alignmentUnchanged &= Vector3.Distance(alignment.localPosition, alignmentPosition) < 0.0001f
                && Quaternion.Angle(alignment.localRotation, alignmentRotation) < 0.01f
                && Vector3.Distance(alignment.localScale, alignmentScale) < 0.0001f;
            renderersEnabled &= renderers.All(item => item != null && item.enabled && item.gameObject.activeInHierarchy);
            parented &= sword.parent == alignment && sword.gameObject.activeInHierarchy;
            maxRootDisplacement = Mathf.Max(maxRootDisplacement,
                Vector3.Distance(animator.transform.localPosition, rootPosition));
            if (rightHand != null)
                maxHandDistance = Mathf.Max(maxHandDistance, Vector3.Distance(rightHand.position, alignment.position));
        }

        private void Finish(ArmedRollValidationReport report)
        {
            report.generatedAtUtc = DateTime.UtcNow.ToString("O");
            report.consoleErrors = errors.ToArray();
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
            ScreenCapture.CaptureScreenshot(absolute, 2);
            yield return new WaitForEndOfFrame();
            yield return new WaitForSecondsRealtime(0.2f);
        }

        private static void FocusCamera(Camera camera, Transform player)
        {
            camera.orthographic = true;
            camera.orthographicSize = 2.35f;
            camera.transform.position = new Vector3(player.position.x, player.position.y + 1.1f, -20f);
            camera.transform.rotation = Quaternion.identity;
        }

        private static Bounds CombinedBounds(Renderer[] renderers)
        {
            if (renderers == null || renderers.Length == 0) return new Bounds();
            Bounds bounds = renderers[0].bounds;
            for (int index = 1; index < renderers.Length; index++) bounds.Encapsulate(renderers[index].bounds);
            return bounds;
        }

        private static void FocusCameraOnBounds(Camera camera, Bounds bounds)
        {
            camera.orthographic = true;
            camera.orthographicSize = Mathf.Max(2.35f, Mathf.Max(bounds.extents.y, bounds.extents.x / camera.aspect) * 1.2f);
            camera.transform.position = new Vector3(bounds.center.x, bounds.center.y, -20f);
            camera.transform.rotation = Quaternion.identity;
        }

        private static string ToAbsolutePath(string assetPath) => Path.GetFullPath(Path.Combine(
            Application.dataPath, assetPath.Substring("Assets".Length).TrimStart('/', '\\')));
    }
}
