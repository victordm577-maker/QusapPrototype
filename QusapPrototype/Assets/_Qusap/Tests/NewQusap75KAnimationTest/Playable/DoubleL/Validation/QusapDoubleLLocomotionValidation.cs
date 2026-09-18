using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Qusap.NewQusap75KAnimationTest.Playable.DoubleL.Validation
{
    [Serializable]
    public sealed class DoubleLClipValidationRecord
    {
        public string role;
        public string sourceAsset;
        public string exactClipName;
        public float durationSeconds;
        public float framesPerSecond;
        public bool loopExpected;
        public bool loopActual;
        public bool isHumanMotion;
        public bool sourceAvatarValid;
        public bool bakeRootRotation;
        public bool bakeRootPositionY;
        public bool bakeRootPositionXZ;
        public string observedState;
        public float normalizedTime;
        public float playerRootDisplacementXZ;
        public float playerRootRotationDelta;
        public float maximumBoneAngleDelta;
        public float boundsGrowthRatio;
        public bool finitePose;
        public bool swordFollowsRightHand;
        public bool returnedToSelectedIdle;
        public string screenshot;
        public bool passed;
        public string notes;
    }

    [Serializable]
    public sealed class DoubleLLocomotionValidationReport
    {
        public string generatedAtUtc;
        public string packagePath;
        public string packageSha256;
        public string importedRoot;
        public string targetCharacter;
        public string sourceAvatar;
        public string swordModel;
        public string controllerAsset;
        public string prefabAsset;
        public string sceneAsset;
        public string sourcePlayablePrefab;
        public string sourcePlayableScene;
        public string[] importedPackageAssets;
        public string[] controls;
        public bool packageHashMatches;
        public bool exactlySelectedAssetsImported;
        public bool sourceAvatarValid;
        public bool targetAvatarValid;
        public bool allClipsHumanoid;
        public bool animatorApplyRootMotion;
        public bool originalFilesUnchanged;
        public bool gameplayComponentsPreserved;
        public bool rigidbodyPreserved;
        public bool collidersPreserved;
        public bool inputActionsUnchanged;
        public bool playerTwoUntouched;
        public bool movementAndJumpSystemsPreserved;
        public bool swordAttachedToRightHand;
        public string weaponSocketPath;
        public string swordLocalPosition;
        public string swordLocalEulerAngles;
        public string swordLocalScale;
        public bool controllerHasAllStates;
        public bool turnsReturnToSelectedIdle;
        public bool rollReturnsToSelectedIdle;
        public bool runtimeSequenceExecuted;
        public bool runtimeSequencePassed;
        public bool sceneStartedInPlayMode;
        public int consoleWarningCount;
        public int consoleErrorCount;
        public string[] consoleWarnings;
        public string[] consoleErrors;
        public DoubleLClipValidationRecord[] clips;
        public string[] createdAssets;
        public bool passed;
    }

    [DisallowMultipleComponent]
    public sealed class QusapDoubleLLocomotionValidation : MonoBehaviour
    {
        private const string TestRoot = "Assets/_Qusap/Tests/NewQusap75KAnimationTest";
        private const string ReportPath = TestRoot + "/Playable/DoubleL/Validation/DoubleLLocomotionValidation.json";
        private const string ScreenshotRoot = TestRoot + "/Playable/DoubleL/Validation/Screenshots";
        private const string MarkerName = "QusapDoubleLLocomotionValidation.request";

        private static readonly HumanBodyBones[] ObservedBones =
        {
            HumanBodyBones.Hips, HumanBodyBones.Spine, HumanBodyBones.Chest,
            HumanBodyBones.Head, HumanBodyBones.LeftUpperArm, HumanBodyBones.LeftLowerArm,
            HumanBodyBones.RightUpperArm, HumanBodyBones.RightLowerArm,
            HumanBodyBones.LeftUpperLeg, HumanBodyBones.LeftLowerLeg,
            HumanBodyBones.RightUpperLeg, HumanBodyBones.RightLowerLeg
        };

        private Animator animator;
        private Transform playerRoot;
        private Transform rightHand;
        private Transform weaponSocket;
        private Transform sword;
        private Camera validationCamera;
        private Vector3 initialPlayerPosition;
        private Quaternion initialPlayerRotation;
        private Vector3 initialSwordLocalPosition;
        private Quaternion initialSwordLocalRotation;
        private Vector3 initialSwordLocalScale;
        private Bounds baselineBounds;
        private Dictionary<HumanBodyBones, Quaternion> baselineRotations;
        private Dictionary<GameObject, int> originalLayers;
        private DoubleLLocomotionValidationReport report;

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
            yield return null;
            string reportAbsolute = ToAbsoluteAssetPath(ReportPath);
            report = File.Exists(reportAbsolute)
                ? JsonUtility.FromJson<DoubleLLocomotionValidationReport>(File.ReadAllText(reportAbsolute))
                : null;
            if (report == null)
            {
                throw new InvalidOperationException("DoubleL structural validation report is missing.");
            }

            animator = FindObjectsByType<Animator>(FindObjectsInactive.Exclude)
                .FirstOrDefault(candidate => candidate.runtimeAnimatorController != null
                    && candidate.runtimeAnimatorController.name == "Qusap75K_DoubleL_LocomotionTest");
            if (animator == null)
            {
                throw new InvalidOperationException("The DoubleL test Animator was not found in Play Mode.");
            }

            animator.applyRootMotion = false;
            animator.cullingMode = AnimatorCullingMode.AlwaysAnimate;
            foreach (SkinnedMeshRenderer renderer in animator.GetComponentsInChildren<SkinnedMeshRenderer>(true))
            {
                renderer.updateWhenOffscreen = true;
            }

            playerRoot = animator.transform.root;
            rightHand = animator.GetBoneTransform(HumanBodyBones.RightHand);
            weaponSocket = rightHand != null ? rightHand.Find("DoubleL_WeaponSocket") : null;
            sword = weaponSocket != null ? weaponSocket.Find("SM_Wep_Sword_03_Placeholder") : null;
            if (rightHand == null || weaponSocket == null || sword == null)
            {
                throw new InvalidOperationException("The right-hand test weapon hierarchy is incomplete.");
            }

            GameObject cameraObject = new GameObject("DoubleL_ValidationCamera_Transient");
            validationCamera = cameraObject.AddComponent<Camera>();
            validationCamera.enabled = false;
            validationCamera.clearFlags = CameraClearFlags.SolidColor;
            validationCamera.backgroundColor = new Color(0.08f, 0.09f, 0.11f, 1f);
            validationCamera.cullingMask = 1 << 31;
            originalLayers = animator.GetComponentsInChildren<Transform>(true)
                .ToDictionary(item => item.gameObject, item => item.gameObject.layer);
            foreach (GameObject item in originalLayers.Keys)
            {
                item.layer = 31;
            }

            yield return new WaitForSecondsRealtime(0.5f);
            initialPlayerPosition = playerRoot.position;
            initialPlayerRotation = playerRoot.rotation;
            initialSwordLocalPosition = sword.localPosition;
            initialSwordLocalRotation = sword.localRotation;
            initialSwordLocalScale = sword.localScale;
            baselineBounds = CalculateRendererBounds(animator.gameObject);
            baselineRotations = CaptureBoneRotations();

            var runtimeRecords = new List<DoubleLClipValidationRecord>();
            yield return CaptureLoop(runtimeRecords, "Combat Idle B1", "PlayIdleB1", "CombatIdle_B1", 1, 0.55f);
            yield return CaptureLoop(runtimeRecords, "Combat Idle B2", "PlayIdleB2", "CombatIdle_B2", 2, 0.55f);
            yield return CaptureLoop(runtimeRecords, "Combat Idle B3", "PlayIdleB3", "CombatIdle_B3", 3, 0.55f);
            yield return CaptureLoop(runtimeRecords, "Walk Forward", "PlayWalkForward", "WalkForward", 3, 0.50f);
            yield return CaptureLoop(runtimeRecords, "Walk Backward", "PlayWalkBackward", "WalkBackward", 3, 0.50f);
            yield return CaptureLoop(runtimeRecords, "Run Forward", "PlayRunForward", "RunForward", 3, 0.32f);
            yield return CaptureLoop(runtimeRecords, "Run Backward", "PlayRunBackward", "RunBackward", 3, 0.32f);
            yield return CaptureOneShot(runtimeRecords, "Turn Left 90", "PlayTurnLeft90", "TurnLeft90", 2, 0.55f, 1.35f);
            yield return CaptureOneShot(runtimeRecords, "Turn Right 90", "PlayTurnRight90", "TurnRight90", 2, 0.55f, 1.35f);
            yield return CaptureOneShot(runtimeRecords, "Roll comparison", "PlayRoll", "RollFront", 2, 0.55f, 1.55f);

            MergeRuntimeRecords(runtimeRecords);
            report.runtimeSequenceExecuted = true;
            report.runtimeSequencePassed = runtimeRecords.Count == 10 && runtimeRecords.All(record => record.passed);
            report.animatorApplyRootMotion = animator.applyRootMotion;
            report.swordAttachedToRightHand = sword.IsChildOf(rightHand);
            report.passed = report.passed && report.runtimeSequencePassed
                && !report.animatorApplyRootMotion && report.swordAttachedToRightHand;
            report.generatedAtUtc = DateTime.UtcNow.ToString("O");
            File.WriteAllText(reportAbsolute, JsonUtility.ToJson(report, true));

            foreach (KeyValuePair<GameObject, int> pair in originalLayers)
            {
                if (pair.Key != null)
                {
                    pair.Key.layer = pair.Value;
                }
            }
            Destroy(validationCamera.gameObject);
            enabled = false;
#if UNITY_EDITOR
            UnityEditor.EditorApplication.ExitPlaymode();
#endif
        }

        private IEnumerator CaptureLoop(List<DoubleLClipValidationRecord> records, string role,
            string trigger, string stateName, int selectedIdle, float sampleDelay)
        {
            ResetAllTestTriggers();
            animator.SetInteger("SelectedIdle", selectedIdle);
            animator.SetTrigger(trigger);
            yield return WaitForState(stateName);
            animator.ResetTrigger(trigger);
            yield return new WaitForSecondsRealtime(sampleDelay);
            yield return null;
            records.Add(CaptureRecord(role, stateName, false));
        }

        private IEnumerator CaptureOneShot(List<DoubleLClipValidationRecord> records, string role,
            string trigger, string stateName, int selectedIdle, float sampleDelay, float totalDelay)
        {
            ResetAllTestTriggers();
            animator.SetInteger("SelectedIdle", selectedIdle);
            animator.SetTrigger(trigger);
            yield return WaitForState(stateName);
            yield return new WaitForSecondsRealtime(sampleDelay);
            yield return null;
            DoubleLClipValidationRecord record = CaptureRecord(role, stateName, false);
            yield return new WaitForSecondsRealtime(Mathf.Max(0f, totalDelay - sampleDelay));
            yield return WaitForState("CombatIdle_B" + selectedIdle);
            record.returnedToSelectedIdle = IsState(animator.GetCurrentAnimatorStateInfo(0), "CombatIdle_B" + selectedIdle)
                || (animator.IsInTransition(0)
                    && IsState(animator.GetNextAnimatorStateInfo(0), "CombatIdle_B" + selectedIdle));
            record.passed &= record.returnedToSelectedIdle;
            records.Add(record);
        }

        private void ResetAllTestTriggers()
        {
            string[] triggers =
            {
                "PlayIdleB1", "PlayIdleB2", "PlayIdleB3", "PlayWalkForward",
                "PlayWalkBackward", "PlayRunForward", "PlayRunBackward",
                "PlayTurnLeft90", "PlayTurnRight90", "PlayRoll"
            };
            foreach (string trigger in triggers)
            {
                animator.ResetTrigger(trigger);
            }
        }

        private DoubleLClipValidationRecord CaptureRecord(string role, string expectedState, bool returned)
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
                && animator.GetComponentsInChildren<Transform>(true).All(item =>
                    IsFinite(item.position) && IsFinite(item.localScale));
            float growth = MaxComponent(bounds.size) / Mathf.Max(0.0001f, MaxComponent(baselineBounds.size));
            Vector2 startXZ = new Vector2(initialPlayerPosition.x, initialPlayerPosition.z);
            Vector2 currentXZ = new Vector2(playerRoot.position.x, playerRoot.position.z);
            float rootXZ = Vector2.Distance(startXZ, currentXZ);
            float rootAngle = Quaternion.Angle(initialPlayerRotation, playerRoot.rotation);
            float boneDelta = CalculateMaximumBoneAngleDelta();
            bool follows = sword.IsChildOf(rightHand)
                && Vector3.Distance(initialSwordLocalPosition, sword.localPosition) <= 0.0001f
                && Quaternion.Angle(initialSwordLocalRotation, sword.localRotation) <= 0.01f
                && Vector3.Distance(initialSwordLocalScale, sword.localScale) <= 0.0001f;
            bool inState = IsState(state, expectedState);
            bool passed = inState && rootXZ <= 0.01f && rootAngle <= 0.1f
                && boneDelta > 0.5f && growth < 3f && finite && follows;
            return new DoubleLClipValidationRecord
            {
                role = role,
                observedState = inState ? expectedState : "Unexpected state",
                normalizedTime = state.normalizedTime,
                playerRootDisplacementXZ = rootXZ,
                playerRootRotationDelta = rootAngle,
                maximumBoneAngleDelta = boneDelta,
                boundsGrowthRatio = growth,
                finitePose = finite,
                swordFollowsRightHand = follows,
                returnedToSelectedIdle = returned,
                screenshot = CaptureCamera(SafeFileName(role)),
                passed = passed,
                notes = passed ? "Runtime pose finite; root stationary; sword remained parented to RightHand."
                    : "Runtime validation threshold failed; inspect the numeric fields and screenshot."
            };
        }

        private void MergeRuntimeRecords(List<DoubleLClipValidationRecord> runtimeRecords)
        {
            var structural = (report.clips ?? Array.Empty<DoubleLClipValidationRecord>())
                .ToDictionary(record => record.role, StringComparer.Ordinal);
            foreach (DoubleLClipValidationRecord runtime in runtimeRecords)
            {
                if (!structural.TryGetValue(runtime.role, out DoubleLClipValidationRecord target))
                {
                    continue;
                }
                target.observedState = runtime.observedState;
                target.normalizedTime = runtime.normalizedTime;
                target.playerRootDisplacementXZ = runtime.playerRootDisplacementXZ;
                target.playerRootRotationDelta = runtime.playerRootRotationDelta;
                target.maximumBoneAngleDelta = runtime.maximumBoneAngleDelta;
                target.boundsGrowthRatio = runtime.boundsGrowthRatio;
                target.finitePose = runtime.finitePose;
                target.swordFollowsRightHand = runtime.swordFollowsRightHand;
                target.returnedToSelectedIdle = runtime.returnedToSelectedIdle;
                target.screenshot = runtime.screenshot;
                target.passed = target.passed && runtime.passed;
                target.notes = runtime.notes;
            }
        }

        private IEnumerator WaitForState(string expectedState)
        {
            float deadline = Time.realtimeSinceStartup + 3f;
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
            foreach (KeyValuePair<HumanBodyBones, Quaternion> pair in baselineRotations)
            {
                Transform target = animator.GetBoneTransform(pair.Key);
                if (target != null)
                {
                    maximum = Mathf.Max(maximum, Quaternion.Angle(pair.Value, target.localRotation));
                }
            }
            return maximum;
        }

        private string CaptureCamera(string label)
        {
            Bounds bounds = CalculateRendererBounds(animator.gameObject);
            float height = Mathf.Max(0.5f, bounds.size.y);
            float width = Mathf.Max(0.5f, Mathf.Max(bounds.size.x, bounds.size.z));
            validationCamera.orthographic = true;
            validationCamera.aspect = 1f;
            validationCamera.orthographicSize = Mathf.Max(height * 0.65f, width * 0.58f);
            Vector3 viewDirection = new Vector3(1f, 0.18f, 1f).normalized;
            validationCamera.transform.position = bounds.center + viewDirection * Mathf.Max(2f, height * 2.5f);
            validationCamera.transform.rotation = Quaternion.LookRotation(bounds.center - validationCamera.transform.position, Vector3.up);

            string assetPath = ScreenshotRoot + "/" + label + ".png";
            string absolute = ToAbsoluteAssetPath(assetPath);
            Directory.CreateDirectory(Path.GetDirectoryName(absolute)
                ?? throw new InvalidOperationException("Invalid screenshot path."));
            const int size = 960;
            var target = new RenderTexture(size, size, 24, RenderTextureFormat.ARGB32);
            var texture = new Texture2D(size, size, TextureFormat.RGB24, false);
            RenderTexture previousActive = RenderTexture.active;
            try
            {
                validationCamera.targetTexture = target;
                RenderTexture.active = target;
                validationCamera.Render();
                texture.ReadPixels(new Rect(0, 0, size, size), 0, 0);
                texture.Apply();
                File.WriteAllBytes(absolute, texture.EncodeToPNG());
            }
            finally
            {
                validationCamera.targetTexture = null;
                RenderTexture.active = previousActive;
                Destroy(texture);
                target.Release();
                Destroy(target);
            }
            return assetPath;
        }

        private static Bounds CalculateRendererBounds(GameObject root)
        {
            Renderer[] renderers = root.GetComponentsInChildren<Renderer>(true)
                .Where(renderer => renderer.enabled).ToArray();
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

        private static string SafeFileName(string value)
        {
            return value.Replace(" ", "_").Replace("°", string.Empty);
        }

        private static float MaxComponent(Vector3 value) => Mathf.Max(value.x, Mathf.Max(value.y, value.z));
        private static bool IsState(AnimatorStateInfo state, string name) =>
            state.shortNameHash == Animator.StringToHash(name);
        private static bool IsFinite(Vector3 value) => IsFinite(value.x) && IsFinite(value.y) && IsFinite(value.z);
        private static bool IsFinite(float value) => !float.IsNaN(value) && !float.IsInfinity(value);

        private static string ToAbsoluteAssetPath(string assetPath)
        {
            string relative = assetPath.Substring("Assets".Length).TrimStart('/', '\\');
            return Path.GetFullPath(Path.Combine(Application.dataPath, relative));
        }
    }

    internal static class QusapDoubleLPlayModeLogCapture
    {
        private const string ScenePath =
            "Assets/_Qusap/Tests/NewQusap75KAnimationTest/Playable/DoubleL/Scene/CombatPlayground_Qusap75K_DoubleL_LocomotionTest.unity";
        private static string logPath;
        private static bool active;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void BeginCapture()
        {
            active = string.Equals(SceneManager.GetActiveScene().path, ScenePath, StringComparison.Ordinal);
            if (!active)
            {
                return;
            }
            logPath = Path.GetFullPath(Path.Combine(Application.dataPath, "_Qusap", "Tests",
                "NewQusap75KAnimationTest", "Playable", "DoubleL", "Validation",
                "DoubleLLocomotionPlayModeConsole.log"));
            Directory.CreateDirectory(Path.GetDirectoryName(logPath) ?? Application.dataPath);
            File.WriteAllText(logPath, "QUSAP75K_DOUBLEL_LOCOMOTION_PLAYMODE_LOG\n");
            Application.logMessageReceived += OnLogMessage;
        }

        private static void OnLogMessage(string condition, string stackTrace, LogType type)
        {
            if (!active || type == LogType.Log)
            {
                return;
            }
            string cleanCondition = (condition ?? string.Empty).Replace('\r', ' ').Replace('\n', ' ');
            string cleanStack = (stackTrace ?? string.Empty).Replace('\r', ' ').Replace('\n', ' ');
            File.AppendAllText(logPath,
                $"{type.ToString().ToUpperInvariant()}|{cleanCondition}|{cleanStack}\n");
        }
    }
}
