using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using UnityEngine;

namespace Qusap.NewQusap75KAnimationTest
{
    [Serializable]
    public sealed class PlayModePoseSample
    {
        public string label;
        public float time;
        public float normalizedTime;
        public string rootPosition;
        public string boundsCenter;
        public string boundsSize;
        public float maximumBoneAngleDelta;
        public bool finiteBounds;
        public string screenshot;
    }

    [Serializable]
    public sealed class PlayModeValidationReport
    {
        public string generatedAtUtc;
        public string stateName;
        public float clipLengthSeconds;
        public float observedNormalizedTime;
        public bool animatorIsHuman;
        public bool applyRootMotion;
        public bool rootStayedFixed;
        public float maximumRootDisplacement;
        public float maximumBoneAngleDelta;
        public bool poseChanged;
        public bool heldFinalPoseWithoutLoop;
        public bool finiteBounds;
        public Vector3 sweptBoneBoundsCenter;
        public Vector3 sweptBoneBoundsSize;
        public List<PlayModePoseSample> samples = new List<PlayModePoseSample>();
        public bool passed;
        public string error;
    }

    public sealed class QusapRollPlayModeProbe : MonoBehaviour
    {
        private const string TestRoot = "Assets/_Qusap/Tests/NewQusap75KAnimationTest";
        private const string ReportPath = TestRoot + "/Validation/PlayModeValidation.json";
        private const string ScreenshotDirectory = TestRoot + "/Validation/PlayModeScreenshots";

        private Animator animator;
        private Camera sceneCamera;
        private Vector3 initialRootPosition;
        private Dictionary<HumanBodyBones, Quaternion> initialBoneRotations;
        private float startedAt;
        private float clipLength;
        private float maximumRootDisplacement;
        private float maximumBoneAngleDelta;
        private bool finiteBounds = true;
        private Bounds sweptBoneBounds;
        private bool hasSweptBoneBounds;
        private readonly HashSet<string> capturedLabels = new HashSet<string>();
        private readonly List<PlayModePoseSample> samples = new List<PlayModePoseSample>();

        private static readonly HumanBodyBones[] ObservedBones =
        {
            HumanBodyBones.Hips, HumanBodyBones.Spine, HumanBodyBones.Head,
            HumanBodyBones.LeftUpperArm, HumanBodyBones.LeftLowerArm, HumanBodyBones.LeftHand,
            HumanBodyBones.RightUpperArm, HumanBodyBones.RightLowerArm, HumanBodyBones.RightHand,
            HumanBodyBones.LeftUpperLeg, HumanBodyBones.LeftLowerLeg, HumanBodyBones.LeftFoot,
            HumanBodyBones.RightUpperLeg, HumanBodyBones.RightLowerLeg, HumanBodyBones.RightFoot
        };

        private void Start()
        {
            try
            {
                GameObject character = GameObject.Find("Qusap75K_AnimationTest");
                animator = character != null ? character.GetComponent<Animator>() : null;
                sceneCamera = Camera.main ?? FindAnyObjectByType<Camera>();
                if (animator == null || sceneCamera == null)
                {
                    CompleteWithError("The isolated scene is missing its root Animator or camera.");
                    return;
                }

                foreach (SkinnedMeshRenderer renderer in character.GetComponentsInChildren<SkinnedMeshRenderer>(true))
                {
                    renderer.updateWhenOffscreen = true;
                }

                RuntimeAnimatorController controller = animator.runtimeAnimatorController;
                AnimationClip clip = controller != null
                    ? controller.animationClips.FirstOrDefault(candidate => candidate.name == "RM_Roll_front")
                      ?? controller.animationClips.FirstOrDefault()
                    : null;
                if (clip == null)
                {
                    CompleteWithError("The Animator Controller has no AnimationClip.");
                    return;
                }

                clipLength = clip.length;
                initialRootPosition = animator.transform.position;
                initialBoneRotations = CaptureBoneRotations(animator);
                startedAt = Time.time;
                animator.applyRootMotion = false;
                animator.Play("RollFront", 0, 0f);
                animator.Update(0f);
                Bounds initialBounds = CalculateHumanoidBoneBounds(animator);
                EncapsulateSweptBoneBounds(initialBounds);
            }
            catch (Exception exception)
            {
                CompleteWithError(exception.ToString());
            }
        }

        private void Update()
        {
            if (animator == null)
            {
                return;
            }

            try
            {
                float elapsed = Time.time - startedAt;
                maximumRootDisplacement = Mathf.Max(maximumRootDisplacement,
                    Vector3.Distance(initialRootPosition, animator.transform.position));
                maximumBoneAngleDelta = Mathf.Max(maximumBoneAngleDelta,
                    CalculateMaximumBoneAngleDelta(animator, initialBoneRotations));
                EncapsulateSweptBoneBounds(CalculateHumanoidBoneBounds(animator));

                CaptureAt("Start", 0.03f, elapsed);
                CaptureAt("Quarter", clipLength * 0.25f, elapsed);
                CaptureAt("Middle", clipLength * 0.5f, elapsed);
                CaptureAt("ThreeQuarter", clipLength * 0.75f, elapsed);
                CaptureAt("End", clipLength, elapsed);

                if (elapsed >= clipLength + 0.25f)
                {
                    Complete();
                }
            }
            catch (Exception exception)
            {
                CompleteWithError(exception.ToString());
            }
        }

        private void CaptureAt(string label, float targetTime, float elapsed)
        {
            if (elapsed < targetTime || capturedLabels.Contains(label))
            {
                return;
            }

            capturedLabels.Add(label);
            AnimatorStateInfo state = animator.GetCurrentAnimatorStateInfo(0);
            Bounds bounds = CalculateRendererBounds(animator.gameObject);
            bool sampleFinite = IsFinite(bounds.center) && IsFinite(bounds.size);
            finiteBounds &= sampleFinite;
            float boneDelta = CalculateMaximumBoneAngleDelta(animator, initialBoneRotations);
            string screenshot = ScreenshotDirectory + "/RollFront_PlayMode_" + label + ".png";
            CaptureCamera(sceneCamera, screenshot);
            samples.Add(new PlayModePoseSample
            {
                label = label,
                time = elapsed,
                normalizedTime = state.normalizedTime,
                rootPosition = FormatVector(animator.transform.position),
                boundsCenter = FormatVector(bounds.center),
                boundsSize = FormatVector(bounds.size),
                maximumBoneAngleDelta = boneDelta,
                finiteBounds = sampleFinite,
                screenshot = screenshot
            });
        }

        private void Complete()
        {
            AnimatorStateInfo state = animator.GetCurrentAnimatorStateInfo(0);
            bool rootStayedFixed = maximumRootDisplacement < 0.0001f;
            bool poseChanged = maximumBoneAngleDelta > 5f;
            bool heldFinalPose = state.IsName("RollFront") && state.normalizedTime >= 1f;
            var report = new PlayModeValidationReport
            {
                generatedAtUtc = DateTime.UtcNow.ToString("O"),
                stateName = state.IsName("RollFront") ? "RollFront" : "Unexpected state",
                clipLengthSeconds = clipLength,
                observedNormalizedTime = state.normalizedTime,
                animatorIsHuman = animator.isHuman,
                applyRootMotion = animator.applyRootMotion,
                rootStayedFixed = rootStayedFixed,
                maximumRootDisplacement = maximumRootDisplacement,
                maximumBoneAngleDelta = maximumBoneAngleDelta,
                poseChanged = poseChanged,
                heldFinalPoseWithoutLoop = heldFinalPose,
                finiteBounds = finiteBounds,
                sweptBoneBoundsCenter = sweptBoneBounds.center,
                sweptBoneBoundsSize = sweptBoneBounds.size,
                samples = samples,
                passed = animator.isHuman && !animator.applyRootMotion && rootStayedFixed &&
                         poseChanged && heldFinalPose && finiteBounds && samples.Count == 5
            };
            WriteReport(report);
            enabled = false;
#if UNITY_EDITOR
            UnityEditor.EditorApplication.ExitPlaymode();
#endif
        }

        private void CompleteWithError(string message)
        {
            WriteReport(new PlayModeValidationReport
            {
                generatedAtUtc = DateTime.UtcNow.ToString("O"),
                passed = false,
                error = message
            });
            enabled = false;
#if UNITY_EDITOR
            UnityEditor.EditorApplication.ExitPlaymode();
#endif
        }

        private static Dictionary<HumanBodyBones, Quaternion> CaptureBoneRotations(Animator targetAnimator)
        {
            var rotations = new Dictionary<HumanBodyBones, Quaternion>();
            foreach (HumanBodyBones bone in ObservedBones)
            {
                Transform target = targetAnimator.GetBoneTransform(bone);
                if (target != null)
                {
                    rotations[bone] = target.localRotation;
                }
            }
            return rotations;
        }

        private static float CalculateMaximumBoneAngleDelta(
            Animator targetAnimator, Dictionary<HumanBodyBones, Quaternion> initialRotations)
        {
            float maximum = 0f;
            foreach (KeyValuePair<HumanBodyBones, Quaternion> pair in initialRotations)
            {
                Transform target = targetAnimator.GetBoneTransform(pair.Key);
                if (target != null)
                {
                    maximum = Mathf.Max(maximum, Quaternion.Angle(pair.Value, target.localRotation));
                }
            }
            return maximum;
        }

        private void EncapsulateSweptBoneBounds(Bounds bounds)
        {
            if (!hasSweptBoneBounds)
            {
                sweptBoneBounds = bounds;
                hasSweptBoneBounds = true;
            }
            else
            {
                sweptBoneBounds.Encapsulate(bounds);
            }
        }

        private static Bounds CalculateHumanoidBoneBounds(Animator targetAnimator)
        {
            Transform first = null;
            foreach (HumanBodyBones bone in ObservedBones)
            {
                first = targetAnimator.GetBoneTransform(bone);
                if (first != null)
                {
                    break;
                }
            }

            if (first == null)
            {
                return new Bounds(targetAnimator.transform.position, Vector3.one);
            }

            var bounds = new Bounds(first.position, Vector3.zero);
            foreach (HumanBodyBones bone in ObservedBones)
            {
                Transform target = targetAnimator.GetBoneTransform(bone);
                if (target != null)
                {
                    bounds.Encapsulate(target.position);
                }
            }
            return bounds;
        }

        private static void FitCameraToBounds(Camera camera, Bounds bounds)
        {
            float height = Mathf.Max(0.5f, bounds.size.y);
            float width = Mathf.Max(0.5f, bounds.size.z);
            float verticalHalfExtent = Mathf.Max(height * 0.5f, width * 0.5f / Mathf.Max(0.1f, camera.aspect));
            camera.orthographic = true;
            camera.orthographicSize = verticalHalfExtent * 2.1f;
            Vector3 target = bounds.center;
            camera.transform.position = target + Vector3.right * Mathf.Max(2f, height * 3f);
            camera.transform.rotation = Quaternion.LookRotation(target - camera.transform.position, Vector3.up);
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

        private static void CaptureCamera(Camera camera, string assetPath)
        {
            string absolutePath = ToAbsoluteAssetPath(assetPath);
            Directory.CreateDirectory(Path.GetDirectoryName(absolutePath) ?? throw new InvalidOperationException());
            const int width = 960;
            const int height = 960;
            var renderTexture = new RenderTexture(width, height, 24, RenderTextureFormat.ARGB32);
            var texture = new Texture2D(width, height, TextureFormat.RGB24, false);
            RenderTexture previousActive = RenderTexture.active;
            RenderTexture previousTarget = camera.targetTexture;
            try
            {
                camera.targetTexture = renderTexture;
                RenderTexture.active = renderTexture;
                camera.Render();
                texture.ReadPixels(new Rect(0, 0, width, height), 0, 0);
                texture.Apply();
                File.WriteAllBytes(absolutePath, texture.EncodeToPNG());
            }
            finally
            {
                camera.targetTexture = previousTarget;
                RenderTexture.active = previousActive;
                Destroy(texture);
                renderTexture.Release();
                Destroy(renderTexture);
            }
        }

        private static void WriteReport(PlayModeValidationReport report)
        {
            string absolutePath = ToAbsoluteAssetPath(ReportPath);
            Directory.CreateDirectory(Path.GetDirectoryName(absolutePath) ?? throw new InvalidOperationException());
            File.WriteAllText(absolutePath, JsonUtility.ToJson(report, true));
        }

        private static string ToAbsoluteAssetPath(string assetPath)
        {
            string relative = assetPath.Substring("Assets".Length).TrimStart('/', '\\');
            return Path.GetFullPath(Path.Combine(Application.dataPath, relative));
        }

        private static bool IsFinite(Vector3 value)
        {
            return IsFinite(value.x) && IsFinite(value.y) && IsFinite(value.z);
        }

        private static bool IsFinite(float value)
        {
            return !float.IsNaN(value) && !float.IsInfinity(value);
        }

        private static string FormatVector(Vector3 value)
        {
            return string.Format(CultureInfo.InvariantCulture,
                "({0:F5}, {1:F5}, {2:F5})", value.x, value.y, value.z);
        }
    }
}
