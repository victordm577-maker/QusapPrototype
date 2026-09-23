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
    public sealed class LargeGripSwordValidationReport
    {
        public string generatedAtUtc;
        public int meshCount;
        public int vertexCount;
        public int triangleCount;
        public bool hasVertexColor;
        public bool hasAvatarBonesOrAnimations;
        public string alignmentLocalPosition;
        public string alignmentLocalEulerAngles;
        public string alignmentLocalScale;
        public float swordToCharacterHeightRatio;
        public float idleTipAboveFeet;
        public bool idleB1RightPassed;
        public bool idleB1LeftPassed;
        public bool runRightPassed;
        public bool runLeftPassed;
        public bool rollPassed;
        public bool jumpAndDashComponentsEnabled;
        public bool rendererAlwaysEnabled;
        public bool sameTransformInAllStates;
        public bool autoLocomotionEnabled;
        public bool rootMotionDisabled;
        public bool animationRootDisplacementZero;
        public bool hasForbiddenPhysicsOrDamageComponents;
        public string material;
        public string shader;
        public float metallic;
        public float smoothness;
        public bool emissionEnabled;
        public string[] screenshots;
        public string clippingNotes;
        public bool visualAcceptancePassed;
        public string[] consoleErrors;
        public bool passed;
    }

    [DisallowMultipleComponent]
    public sealed class QusapLargeGripIntegrationValidation : MonoBehaviour
    {
        private const string Root = "Assets/_Qusap/Tests/NewQusap75KAnimationTest/Playable/DoubleL/SwordIntegration";
        private const string ReportPath = Root + "/Validation/LargeGripSwordValidation.json";
        private const string MarkerName = "QusapLargeGripIntegrationValidation.request";
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
            var report = new LargeGripSwordValidationReport
            {
                screenshots = new string[3]
            };
            QusapDoubleLAutoLocomotionDriver driver = FindAnyObjectByType<QusapDoubleLAutoLocomotionDriver>();
            Transform alignment = FindObjectsByType<Transform>(FindObjectsInactive.Include)
                .FirstOrDefault(item => item.name == "LargeGripAlignment");
            Transform sword = FindObjectsByType<Transform>(FindObjectsInactive.Include)
                .FirstOrDefault(item => item.name == "Qusap_BasicSword_LargeGrip_25K_Visual");
            Renderer[] renderers = sword != null ? sword.GetComponentsInChildren<Renderer>(true) : Array.Empty<Renderer>();
            Camera camera = Camera.main;

            if (driver == null || driver.Animator == null || driver.TargetRigidbody == null
                || alignment == null || sword == null || sword.parent != alignment
                || renderers.Length == 0 || camera == null)
            {
                errors.Add("LargeGrip SwordIntegration hierarchy is incomplete.");
                Finish(report);
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
            bool originalGravity = body.useGravity;
            Vector3 originalPosition = body.position;
            Quaternion originalRotation = body.rotation;
            Vector3 originalAnimatorPosition = animator.transform.localPosition;
            Vector3 alignmentPosition = alignment.localPosition;
            Quaternion alignmentRotation = alignment.localRotation;
            Vector3 alignmentScale = alignment.localScale;
            MonoBehaviour cameraRig = camera.GetComponents<MonoBehaviour>().FirstOrDefault(item =>
                item != null && item.GetType().FullName == "Qusap.QusapSharedCombatCamera");
            bool cameraRigEnabled = cameraRig != null && cameraRig.enabled;
            Vector3 cameraPosition = camera.transform.position;
            Quaternion cameraRotation = camera.transform.rotation;
            float cameraSize = camera.orthographicSize;

            report.autoLocomotionEnabled = driver.enabled;
            report.jumpAndDashComponentsEnabled = verticalEnabled && dashEnabled;
            report.rootMotionDisabled = !animator.applyRootMotion;
            report.alignmentLocalPosition = Format(alignmentPosition);
            report.alignmentLocalEulerAngles = Format(alignmentRotation.eulerAngles);
            report.alignmentLocalScale = Format(alignmentScale);

            MeshFilter[] filters = sword.GetComponentsInChildren<MeshFilter>(true);
            Mesh[] meshes = filters.Select(item => item.sharedMesh).Where(item => item != null).Distinct().ToArray();
            Color32[] colors = meshes.SelectMany(item => item.colors32).ToArray();
            report.meshCount = meshes.Length;
            report.vertexCount = meshes.Sum(item => item.vertexCount);
            report.triangleCount = meshes.Sum(item => item.triangles.Length / 3);
            report.hasVertexColor = colors.Length == report.vertexCount && colors.Distinct().Count() > 1;
            report.hasAvatarBonesOrAnimations = sword.GetComponentInChildren<Animator>(true) != null
                || sword.GetComponentsInChildren<SkinnedMeshRenderer>(true).Length > 0;
            report.hasForbiddenPhysicsOrDamageComponents = sword.GetComponentsInChildren<Collider>(true).Length > 0
                || sword.GetComponentsInChildren<Rigidbody>(true).Length > 0
                || sword.GetComponentsInChildren<Component>(true).Any(item => item != null
                    && (item.GetType().Name.Contains("Hitbox", StringComparison.OrdinalIgnoreCase)
                        || item.GetType().Name.Contains("Damage", StringComparison.OrdinalIgnoreCase)));

            Material material = renderers[0].sharedMaterial;
            report.material = material != null ? material.name : string.Empty;
            report.shader = material != null && material.shader != null ? material.shader.name : string.Empty;
            report.metallic = material != null && material.HasProperty("_Metallic") ? material.GetFloat("_Metallic") : -1f;
            report.smoothness = material != null && material.HasProperty("_Smoothness") ? material.GetFloat("_Smoothness") : -1f;
            report.emissionEnabled = material != null && material.IsKeywordEnabled("_EMISSION");

            if (cameraRig != null) cameraRig.enabled = false;
            if (horizontal != null) horizontal.enabled = false;
            if (vertical != null) vertical.enabled = false;
            if (dash != null) dash.enabled = false;
            body.useGravity = false;
            body.linearVelocity = Vector3.zero;
            body.position = originalPosition + Vector3.back * 6f;
            body.rotation = originalRotation;
            Physics.SyncTransforms();
            FocusCamera(camera, body.transform);

            bool sameTransform = true;
            bool renderersEnabled = true;

            driver.RefreshFromRigidbody();
            yield return WaitForState(animator, "CombatIdle_B1", 1.5f);
            yield return new WaitForSecondsRealtime(0.2f);
            report.idleB1RightPassed = IsState(animator, "CombatIdle_B1")
                && AttachedAndVisible(sword, alignment, renderers);
            UpdateInvariantState(alignment, renderers, alignmentPosition, alignmentRotation, alignmentScale,
                ref sameTransform, ref renderersEnabled);
            Bounds swordBounds = CombinedBounds(renderers);
            Renderer[] characterRenderers = animator.GetComponentsInChildren<Renderer>(true)
                .Where(item => !item.transform.IsChildOf(sword)).ToArray();
            Bounds characterBounds = CombinedBounds(characterRenderers);
            float swordLength = Mathf.Max(swordBounds.size.x, Mathf.Max(swordBounds.size.y, swordBounds.size.z));
            report.swordToCharacterHeightRatio = characterBounds.size.y > 0.001f
                ? swordLength / characterBounds.size.y : 0f;
            report.idleTipAboveFeet = swordBounds.min.y - characterBounds.min.y;
            report.screenshots[0] = Root + "/Validation/LargeGrip_Idle_Right.png";
            yield return Capture(report.screenshots[0]);

            body.rotation = Quaternion.AngleAxis(180f, Vector3.up) * originalRotation;
            Physics.SyncTransforms();
            yield return null;
            report.idleB1LeftPassed = IsState(animator, "CombatIdle_B1")
                && AttachedAndVisible(sword, alignment, renderers);
            UpdateInvariantState(alignment, renderers, alignmentPosition, alignmentRotation, alignmentScale,
                ref sameTransform, ref renderersEnabled);
            report.screenshots[1] = Root + "/Validation/LargeGrip_Idle_Left.png";
            yield return Capture(report.screenshots[1]);

            body.rotation = originalRotation;
            body.linearVelocity = Vector3.right;
            driver.RefreshFromRigidbody();
            yield return WaitForState(animator, "RunForward", 1.5f);
            report.runRightPassed = IsState(animator, "RunForward")
                && AttachedAndVisible(sword, alignment, renderers);
            UpdateInvariantState(alignment, renderers, alignmentPosition, alignmentRotation, alignmentScale,
                ref sameTransform, ref renderersEnabled);
            body.rotation = Quaternion.AngleAxis(180f, Vector3.up) * originalRotation;
            body.linearVelocity = Vector3.left;
            driver.RefreshFromRigidbody();
            yield return new WaitForSecondsRealtime(0.2f);
            report.runLeftPassed = IsState(animator, "RunForward")
                && AttachedAndVisible(sword, alignment, renderers);
            UpdateInvariantState(alignment, renderers, alignmentPosition, alignmentRotation, alignmentScale,
                ref sameTransform, ref renderersEnabled);

            body.rotation = originalRotation;
            body.linearVelocity = Vector3.zero;
            driver.RefreshFromRigidbody();
            yield return WaitForState(animator, "CombatIdle_B1", 1.5f);
            driver.RequestRoll();
            yield return WaitForState(animator, "RollFront", 1.5f);
            report.rollPassed = IsState(animator, "RollFront")
                && AttachedAndVisible(sword, alignment, renderers);
            UpdateInvariantState(alignment, renderers, alignmentPosition, alignmentRotation, alignmentScale,
                ref sameTransform, ref renderersEnabled);
            Bounds rollBounds = CombinedBounds(characterRenderers);
            rollBounds.Encapsulate(CombinedBounds(renderers));
            FocusCameraObliqueOnBounds(camera, rollBounds, sword);
            Renderer[] externalRenderers = FindObjectsByType<Renderer>(FindObjectsInactive.Exclude)
                .Where(item => !item.transform.IsChildOf(driver.transform.root)).ToArray();
            bool[] externalRendererStates = externalRenderers.Select(item => item.enabled).ToArray();
            foreach (Renderer item in externalRenderers) item.enabled = false;
            report.screenshots[2] = Root + "/Validation/LargeGrip_Roll_Attached.png";
            yield return Capture(report.screenshots[2]);
            for (int index = 0; index < externalRenderers.Length; index++)
                if (externalRenderers[index] != null) externalRenderers[index].enabled = externalRendererStates[index];
            yield return WaitForState(animator, "CombatIdle_B1", 3f);

            report.sameTransformInAllStates = sameTransform;
            report.rendererAlwaysEnabled = renderersEnabled;
            report.animationRootDisplacementZero = Vector3.Distance(
                animator.transform.localPosition, originalAnimatorPosition) < 0.001f;
            report.clippingNotes = "Idle is clear. Run remains hierarchy-attached but turns nearly edge-on. Roll keeps the same hierarchy and transform, but the hand socket separates visibly and substantially from the rendered claw; fixing this requires animation/rig work outside the allowed scope.";
            report.visualAcceptancePassed = false;

            body.linearVelocity = Vector3.zero;
            body.position = originalPosition;
            body.rotation = originalRotation;
            body.useGravity = originalGravity;
            if (horizontal != null) horizontal.enabled = horizontalEnabled;
            if (vertical != null) vertical.enabled = verticalEnabled;
            if (dash != null) dash.enabled = dashEnabled;
            camera.transform.SetPositionAndRotation(cameraPosition, cameraRotation);
            camera.orthographicSize = cameraSize;
            if (cameraRig != null) cameraRig.enabled = cameraRigEnabled;
            Physics.SyncTransforms();

            Finish(report);
        }

        private void Finish(LargeGripSwordValidationReport report)
        {
            report.generatedAtUtc = DateTime.UtcNow.ToString("O");
            report.consoleErrors = errors.ToArray();
            report.passed = report.meshCount == 1
                && report.vertexCount >= 12000 && report.vertexCount <= 13000
                && report.triangleCount >= 24500 && report.triangleCount <= 25100
                && report.hasVertexColor && !report.hasAvatarBonesOrAnimations
                && report.idleB1RightPassed && report.idleB1LeftPassed
                && report.runRightPassed && report.runLeftPassed && report.rollPassed
                && report.jumpAndDashComponentsEnabled && report.rendererAlwaysEnabled
                && report.sameTransformInAllStates && report.autoLocomotionEnabled
                && report.rootMotionDisabled && report.animationRootDisplacementZero
                && !report.hasForbiddenPhysicsOrDamageComponents
                && Mathf.Abs(report.metallic) < 0.001f
                && report.smoothness >= 0.1f && report.smoothness <= 0.5f
                && !report.emissionEnabled && report.visualAcceptancePassed && errors.Count == 0;
            File.WriteAllText(ToAbsolutePath(ReportPath), JsonUtility.ToJson(report, true));
            Application.logMessageReceived -= CaptureLog;
#if UNITY_EDITOR
            UnityEditor.EditorApplication.ExitPlaymode();
#endif
        }

        private static void UpdateInvariantState(Transform alignment, Renderer[] renderers,
            Vector3 position, Quaternion rotation, Vector3 scale,
            ref bool sameTransform, ref bool renderersEnabled)
        {
            sameTransform &= Vector3.Distance(alignment.localPosition, position) < 0.0001f
                && Quaternion.Angle(alignment.localRotation, rotation) < 0.01f
                && Vector3.Distance(alignment.localScale, scale) < 0.0001f;
            renderersEnabled &= renderers.All(item => item != null && item.enabled && item.gameObject.activeInHierarchy);
        }

        private static bool AttachedAndVisible(Transform sword, Transform alignment, Renderer[] renderers) =>
            sword.parent == alignment && sword.gameObject.activeInHierarchy
            && renderers.All(item => item != null && item.enabled && item.gameObject.activeInHierarchy);

        private static Bounds CombinedBounds(Renderer[] renderers)
        {
            if (renderers == null || renderers.Length == 0) return new Bounds();
            Bounds bounds = renderers[0].bounds;
            for (int index = 1; index < renderers.Length; index++) bounds.Encapsulate(renderers[index].bounds);
            return bounds;
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
            yield return new WaitForSecondsRealtime(0.15f);
        }

        private static void FocusCamera(Camera camera, Transform player)
        {
            camera.orthographic = true;
            camera.orthographicSize = 2.25f;
            camera.transform.position = new Vector3(player.position.x, player.position.y + 1.15f, -20f);
            camera.transform.rotation = Quaternion.identity;
        }

        private static void FocusCameraObliqueOnBounds(Camera camera, Bounds bounds, Transform sword)
        {
            MeshFilter filter = sword.GetComponentInChildren<MeshFilter>(true);
            Vector3 faceNormal = Vector3.forward;
            if (filter != null && filter.sharedMesh != null)
            {
                Vector3 size = filter.sharedMesh.bounds.size;
                Vector3 localNormal = size.x <= size.y && size.x <= size.z ? Vector3.right
                    : size.y <= size.z ? Vector3.up : Vector3.forward;
                faceNormal = filter.transform.TransformDirection(localNormal).normalized;
                if (Vector3.Dot(faceNormal, Vector3.forward) < 0f) faceNormal = -faceNormal;
            }
            Vector3 viewForward = Vector3.Slerp(Vector3.forward, faceNormal, 0.25f).normalized;
            Vector3 up = Vector3.ProjectOnPlane(Vector3.up, viewForward).normalized;
            if (up.sqrMagnitude < 0.01f) up = Vector3.up;
            camera.orthographic = true;
            camera.orthographicSize = Mathf.Max(2.5f, bounds.extents.magnitude * 1.1f);
            camera.transform.position = bounds.center - viewForward * 20f;
            camera.transform.rotation = Quaternion.LookRotation(viewForward, up);
        }

        private static string Format(Vector3 value) => $"({value.x:F6}, {value.y:F6}, {value.z:F6})";
        private static string ToAbsolutePath(string assetPath) => Path.GetFullPath(Path.Combine(
            Application.dataPath, assetPath.Substring("Assets".Length).TrimStart('/', '\\')));
    }
}
