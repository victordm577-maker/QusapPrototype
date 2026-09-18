using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEngine;

namespace Qusap.NewQusap75KAnimationTest.Playable.Validation
{
    [Serializable]
    public sealed class BasicMotionsRuntimeClipRecord
    {
        public string role;
        public string observedState;
        public float normalizedTime;
        public float rootDisplacement;
        public float maximumBoneAngleDelta;
        public float boundsGrowthRatio;
        public bool finitePose;
        public bool returnedToIdle;
        public bool heldFinalPose;
        public string screenshot;
        public bool passed;
    }

    [Serializable]
    public sealed class BasicMotionsRuntimeReport
    {
        public string generatedAtUtc;
        public bool animatorIsHuman;
        public bool applyRootMotion;
        public List<BasicMotionsRuntimeClipRecord> clips = new List<BasicMotionsRuntimeClipRecord>();
        public bool passed;
        public string error;
    }

    public sealed class QusapBasicMotionsPlayModeProbe : MonoBehaviour
    {
        private const string TestRoot = "Assets/_Qusap/Tests/NewQusap75KAnimationTest";
        private const string ReportPath = TestRoot + "/Playable/Validation/BasicMotionsRuntimeValidation.json";
        private const string ScreenshotRoot = TestRoot + "/Playable/Validation/BasicMotionsRuntimeScreenshots";
        private const string MarkerName = "QusapBasicMotionsRuntimeValidation.request";

        private static readonly HumanBodyBones[] ObservedBones =
        {
            HumanBodyBones.Hips, HumanBodyBones.Spine, HumanBodyBones.Chest, HumanBodyBones.Head,
            HumanBodyBones.LeftUpperArm, HumanBodyBones.LeftLowerArm, HumanBodyBones.LeftHand,
            HumanBodyBones.RightUpperArm, HumanBodyBones.RightLowerArm, HumanBodyBones.RightHand,
            HumanBodyBones.LeftUpperLeg, HumanBodyBones.LeftLowerLeg, HumanBodyBones.LeftFoot,
            HumanBodyBones.RightUpperLeg, HumanBodyBones.RightLowerLeg, HumanBodyBones.RightFoot
        };

        private Animator animator;
        private Camera validationCamera;
        private Vector3 initialVisualLocalPosition;
        private Bounds baselineBounds;
        private Dictionary<HumanBodyBones, Quaternion> initialRotations;
        private BasicMotionsRuntimeReport report;

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
            StartCoroutine(RunValidation());
        }

        private IEnumerator RunValidation()
        {
            report = new BasicMotionsRuntimeReport { generatedAtUtc = DateTime.UtcNow.ToString("O") };
            yield return null;
            animator = FindObjectsByType<Animator>(FindObjectsInactive.Exclude)
                .FirstOrDefault(candidate => candidate.runtimeAnimatorController != null
                    && candidate.runtimeAnimatorController.name == "Qusap75K_BasicMotionsPlayableTest");
            validationCamera = Camera.main ?? FindAnyObjectByType<Camera>();
            if (animator == null || validationCamera == null)
            {
                throw new InvalidOperationException("Playable scene is missing its Basic Motions Animator or camera.");
            }

            animator.applyRootMotion = false;
            animator.cullingMode = AnimatorCullingMode.AlwaysAnimate;
            foreach (SkinnedMeshRenderer renderer in animator.GetComponentsInChildren<SkinnedMeshRenderer>(true))
            {
                renderer.updateWhenOffscreen = true;
            }

            report.animatorIsHuman = animator.isHuman;
            report.applyRootMotion = animator.applyRootMotion;
            initialVisualLocalPosition = animator.transform.localPosition;
            initialRotations = CaptureBoneRotations();
            baselineBounds = CalculateRendererBounds(animator.gameObject);

            yield return CaptureLoopState("Idle", "PlayIdle", "Idle", 0.55f);
            yield return CaptureLoopState("Walk", "PlayWalk", "Walk", 0.55f);
            yield return CaptureLoopState("Run", "PlayRun", "Run", 0.35f);
            yield return CaptureOneShotReturningToIdle(
                "Jumping", "PlayJumping", "Jumping", 0.42f, 1.1f);
            yield return CaptureDying();
            yield return CaptureOneShotReturningToIdle(
                "Roll", "PlayRoll", "RollFront", 0.55f, 1.55f);

            report.passed = report.animatorIsHuman && !report.applyRootMotion
                && report.clips.Count == 6 && report.clips.All(record => record.passed);

            WriteReport();
            enabled = false;
#if UNITY_EDITOR
            UnityEditor.EditorApplication.ExitPlaymode();
#endif
        }

        private IEnumerator CaptureLoopState(
            string role, string trigger, string expectedState, float waitSeconds)
        {
            animator.SetTrigger(trigger);
            yield return WaitForState(expectedState);
            yield return new WaitForSeconds(waitSeconds);
            yield return null;
            report.clips.Add(CaptureRecord(role, expectedState, false, false));
        }

        private IEnumerator CaptureOneShotReturningToIdle(
            string role, string trigger, string expectedState, float captureAfter, float idleCheckAfter)
        {
            animator.SetTrigger(trigger);
            yield return WaitForState(expectedState);
            yield return new WaitForSeconds(captureAfter);
            yield return null;
            BasicMotionsRuntimeClipRecord record = CaptureRecord(role, expectedState, false, false);
            yield return new WaitForSeconds(Mathf.Max(0f, idleCheckAfter - captureAfter));
            yield return null;
            record.returnedToIdle = IsState(animator.GetCurrentAnimatorStateInfo(0), "Idle");
            record.passed &= record.returnedToIdle;
            report.clips.Add(record);
        }

        private IEnumerator CaptureDying()
        {
            animator.SetTrigger("PlayDying");
            yield return WaitForState("Dying");
            yield return new WaitForSeconds(2.35f);
            yield return null;
            BasicMotionsRuntimeClipRecord record = CaptureRecord("Dying", "Dying", false, false);
            yield return new WaitForSeconds(2.55f);
            yield return null;
            AnimatorStateInfo finalState = animator.GetCurrentAnimatorStateInfo(0);
            record.heldFinalPose = IsState(finalState, "Dying") && finalState.normalizedTime >= 1f;
            record.passed &= record.heldFinalPose;
            record.screenshot = CaptureCamera("Dying_Final");
            report.clips.Add(record);
        }

        private BasicMotionsRuntimeClipRecord CaptureRecord(
            string role, string expectedState, bool returnedToIdle, bool heldFinalPose)
        {
            AnimatorStateInfo state = animator.GetCurrentAnimatorStateInfo(0);
            if (!IsState(state, expectedState) && animator.IsInTransition(0))
            {
                AnimatorStateInfo next = animator.GetNextAnimatorStateInfo(0);
                if (IsState(next, expectedState))
                {
                    state = next;
                }
            }
            Bounds bounds = CalculateRendererBounds(animator.gameObject);
            bool finite = IsFinite(bounds.center) && IsFinite(bounds.size)
                && animator.GetComponentsInChildren<Transform>(true).All(transform =>
                    IsFinite(transform.position) && IsFinite(transform.localScale));
            float growth = MaxComponent(bounds.size) / Mathf.Max(0.0001f, MaxComponent(baselineBounds.size));
            float rootDisplacement = Vector3.Distance(
                initialVisualLocalPosition, animator.transform.localPosition);
            float boneDelta = CalculateMaximumBoneAngleDelta();
            bool inState = IsState(state, expectedState);
            return new BasicMotionsRuntimeClipRecord
            {
                role = role,
                observedState = inState ? expectedState : "Unexpected state",
                normalizedTime = state.normalizedTime,
                rootDisplacement = rootDisplacement,
                maximumBoneAngleDelta = boneDelta,
                boundsGrowthRatio = growth,
                finitePose = finite,
                returnedToIdle = returnedToIdle,
                heldFinalPose = heldFinalPose,
                screenshot = CaptureCamera(role),
                passed = inState && rootDisplacement <= 0.001f && boneDelta > 1f
                    && growth < 3f && finite
            };
        }

        private IEnumerator WaitForState(string expectedState)
        {
            float deadline = Time.realtimeSinceStartup + 2f;
            while (Time.realtimeSinceStartup < deadline)
            {
                AnimatorStateInfo current = animator.GetCurrentAnimatorStateInfo(0);
                AnimatorStateInfo next = animator.GetNextAnimatorStateInfo(0);
                if (IsState(current, expectedState)
                    || (animator.IsInTransition(0) && IsState(next, expectedState)))
                {
                    yield break;
                }
                yield return null;
            }

            throw new InvalidOperationException("Animator did not enter state " + expectedState + ".");
        }

        private string CaptureCamera(string label)
        {
            Bounds boneBounds = CalculateBoneBounds();
            float height = Mathf.Max(0.5f, boneBounds.size.y);
            float depth = Mathf.Max(0.5f, boneBounds.size.z);
            validationCamera.orthographic = true;
            validationCamera.aspect = 1f;
            validationCamera.orthographicSize = Mathf.Max(height * 0.65f, depth * 0.65f) * 1.35f;
            validationCamera.transform.position = boneBounds.center + Vector3.right * Mathf.Max(2f, height * 3f);
            validationCamera.transform.rotation = Quaternion.LookRotation(
                boneBounds.center - validationCamera.transform.position, Vector3.up);

            string assetPath = ScreenshotRoot + "/" + label + ".png";
            string absolutePath = ToAbsoluteAssetPath(assetPath);
            Directory.CreateDirectory(Path.GetDirectoryName(absolutePath)
                ?? throw new InvalidOperationException("Runtime screenshot directory is invalid."));
            const int size = 960;
            var target = new RenderTexture(size, size, 24, RenderTextureFormat.ARGB32);
            var texture = new Texture2D(size, size, TextureFormat.RGB24, false);
            RenderTexture previousActive = RenderTexture.active;
            RenderTexture previousTarget = validationCamera.targetTexture;
            try
            {
                validationCamera.targetTexture = target;
                RenderTexture.active = target;
                validationCamera.Render();
                texture.ReadPixels(new Rect(0, 0, size, size), 0, 0);
                texture.Apply();
                File.WriteAllBytes(absolutePath, texture.EncodeToPNG());
            }
            finally
            {
                validationCamera.targetTexture = previousTarget;
                RenderTexture.active = previousActive;
                Destroy(texture);
                target.Release();
                Destroy(target);
            }
            return assetPath;
        }

        private Dictionary<HumanBodyBones, Quaternion> CaptureBoneRotations()
        {
            var values = new Dictionary<HumanBodyBones, Quaternion>();
            foreach (HumanBodyBones bone in ObservedBones)
            {
                Transform target = animator.GetBoneTransform(bone);
                if (target != null)
                {
                    values[bone] = target.localRotation;
                }
            }
            return values;
        }

        private float CalculateMaximumBoneAngleDelta()
        {
            float maximum = 0f;
            foreach (KeyValuePair<HumanBodyBones, Quaternion> pair in initialRotations)
            {
                Transform target = animator.GetBoneTransform(pair.Key);
                if (target != null)
                {
                    maximum = Mathf.Max(maximum, Quaternion.Angle(pair.Value, target.localRotation));
                }
            }
            return maximum;
        }

        private Bounds CalculateBoneBounds()
        {
            Transform first = ObservedBones.Select(animator.GetBoneTransform).FirstOrDefault(value => value != null);
            if (first == null)
            {
                return CalculateRendererBounds(animator.gameObject);
            }
            var bounds = new Bounds(first.position, Vector3.zero);
            foreach (HumanBodyBones bone in ObservedBones)
            {
                Transform target = animator.GetBoneTransform(bone);
                if (target != null)
                {
                    bounds.Encapsulate(target.position);
                }
            }
            return bounds;
        }

        private void WriteReport()
        {
            string absolutePath = ToAbsoluteAssetPath(ReportPath);
            Directory.CreateDirectory(Path.GetDirectoryName(absolutePath)
                ?? throw new InvalidOperationException("Runtime report directory is invalid."));
            File.WriteAllText(absolutePath, JsonUtility.ToJson(report, true));
        }

        private static Bounds CalculateRendererBounds(GameObject root)
        {
            Renderer[] renderers = root.GetComponentsInChildren<Renderer>(true);
            if (renderers.Length == 0)
            {
                return new Bounds(root.transform.position, Vector3.one);
            }
            Bounds bounds = renderers[0].bounds;
            for (int index = 1; index < renderers.Length; index++)
            {
                bounds.Encapsulate(renderers[index].bounds);
            }
            return bounds;
        }

        private static float MaxComponent(Vector3 value) => Mathf.Max(value.x, Mathf.Max(value.y, value.z));
        private static bool IsState(AnimatorStateInfo state, string shortName) =>
            state.shortNameHash == Animator.StringToHash(shortName);
        private static bool IsFinite(Vector3 value) => IsFinite(value.x) && IsFinite(value.y) && IsFinite(value.z);
        private static bool IsFinite(float value) => !float.IsNaN(value) && !float.IsInfinity(value);

        private static string ToAbsoluteAssetPath(string assetPath)
        {
            string relative = assetPath.Substring("Assets".Length).TrimStart('/', '\\');
            return Path.GetFullPath(Path.Combine(Application.dataPath, relative));
        }
    }
}
