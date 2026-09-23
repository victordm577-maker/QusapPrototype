using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEngine;
using Qusap.NewQusap75KAnimationTest.Playable.DoubleL.AutoLocomotion;

namespace Qusap.NewQusap75KAnimationTest.Playable.DoubleL.SwordIntegration.Diagnostics.HandSocket
{
    [Serializable]
    public sealed class DiagnosticTransformRecord
    {
        public string name;
        public string path;
        public int instanceId;
        public string worldPosition;
        public string worldRotation;
        public string worldScale;
        public string localPosition;
        public string localRotation;
        public string localScale;
        public string parentName;
        public string parentPath;
        public int parentInstanceId;
        public string[] ancestors;
    }

    [Serializable]
    public sealed class DiagnosticRendererRecord
    {
        public string name;
        public string path;
        public int instanceId;
        public string worldScale;
        public string meshName;
        public int vertexCount;
        public int boneCount;
        public int bindposeCount;
        public string rootBonePath;
        public int rootBoneInstanceId;
        public int animatorRightHandBoneIndex;
        public int namedHandRBoneIndex;
        public string selectedHandBoneName;
        public string selectedHandBonePath;
        public int selectedHandBoneInstanceId;
        public bool selectedBoneMatchesAnimatorRightHandReference;
        public bool selectedBoneMatchesAnimatorRightHandInstanceId;
        public string selectedHandBindpose;
        public int primaryRightHandVertexCount;
        public int rightHandWeightAtLeast05Count;
        public int rightHandWeightAtLeast09Count;
        public string weightInspectionError;
    }

    [Serializable]
    public sealed class HandSocketSample
    {
        public string label;
        public string state;
        public float normalizedTime;
        public string rightHandWorldPosition;
        public string socketWorldPosition;
        public string alignmentWorldPosition;
        public string socketLocalPosition;
        public string alignmentLocalPosition;
        public float rightHandToSocket;
        public float rightHandToAlignment;
        public float socketToAlignment;
        public bool visualCenterAvailable;
        public string visualCenterMethod;
        public string dominatedVerticesWorldCenter;
        public float visualCenterToRightHand;
        public float visualCenterToSocket;
    }

    [Serializable]
    public sealed class HandSocketDiagnosticReport
    {
        public string generatedAtUtc;
        public string prefabPath;
        public string scenePath;
        public DiagnosticTransformRecord animatorRightHand;
        public DiagnosticTransformRecord socket;
        public bool socketParentIsAnimatorRightHandReference;
        public bool socketParentInstanceIdMatchesAnimatorRightHand;
        public DiagnosticTransformRecord alignment;
        public DiagnosticRendererRecord[] renderers;
        public string selectedRendererPath;
        public int selectedRendererInstanceId;
        public HandSocketSample[] samples;
        public string maximumSeparationSample;
        public float maximumRightHandToAlignment;
        public string screenshot;
        public string previousHarnessIssue;
        public string preliminaryCase;
        public string[] consoleErrors;
    }

    [DisallowMultipleComponent]
    public sealed class QusapHandSocketRuntimeDiagnostic : MonoBehaviour
    {
        private const string Root = "Assets/_Qusap/Tests/NewQusap75KAnimationTest/Playable/DoubleL/SwordIntegration";
        private const string ReportPath = Root + "/Diagnostics/HandSocket/HandSocketDiagnostic.json";
        private const string ScreenshotPath = Root + "/Diagnostics/HandSocket/HandSocket_MaxSeparation.png";
        private readonly List<string> errors = new();

        private void Awake()
        {
            Application.logMessageReceived += CaptureLog;
            StartCoroutine(Run());
        }

        private void OnDestroy() => Application.logMessageReceived -= CaptureLog;

        private void CaptureLog(string condition, string stackTrace, LogType type)
        {
            if (type == LogType.Error || type == LogType.Assert || type == LogType.Exception)
                errors.Add(condition + (string.IsNullOrWhiteSpace(stackTrace) ? string.Empty : "\n" + stackTrace));
        }

        private IEnumerator Run()
        {
            yield return null;
            var report = new HandSocketDiagnosticReport
            {
                prefabPath = Root + "/Prefab/Qusap75K_DoubleL_SwordIntegrationTest.prefab",
                scenePath = Root + "/Scene/CombatPlayground_Qusap75K_DoubleL_SwordIntegrationTest.unity",
                screenshot = ScreenshotPath
            };

            QusapDoubleLAutoLocomotionDriver driver = FindAnyObjectByType<QusapDoubleLAutoLocomotionDriver>();
            if (driver == null || driver.Animator == null)
            {
                errors.Add("SwordIntegration driver or Animator was not found.");
                Finish(report);
                yield break;
            }

            Animator animator = driver.Animator;
            Transform rightHand = animator.GetBoneTransform(HumanBodyBones.RightHand);
            Transform socket = FindDescendant(driver.transform, "DoubleL_WeaponSocket");
            Transform alignment = socket != null ? FindDescendant(socket, "LargeGripAlignment") : null;
            if (rightHand == null || socket == null || alignment == null)
            {
                errors.Add("RightHand, DoubleL_WeaponSocket, or LargeGripAlignment was not found.");
                Finish(report);
                yield break;
            }

            report.animatorRightHand = Describe(rightHand);
            report.socket = Describe(socket);
            report.alignment = Describe(alignment);
            report.socketParentIsAnimatorRightHandReference = ReferenceEquals(socket.parent, rightHand);
            report.socketParentInstanceIdMatchesAnimatorRightHand = socket.parent != null
                && InstanceId(socket.parent) == InstanceId(rightHand);

            SkinnedMeshRenderer[] skinRenderers = animator.GetComponentsInChildren<SkinnedMeshRenderer>(true);
            var rendererData = new List<RendererAnalysis>();
            foreach (SkinnedMeshRenderer skin in skinRenderers)
                rendererData.Add(AnalyzeRenderer(skin, rightHand));
            report.renderers = rendererData.Select(item => item.record).ToArray();
            RendererAnalysis selected = rendererData
                .OrderByDescending(item => item.record.primaryRightHandVertexCount)
                .ThenByDescending(item => item.record.rightHandWeightAtLeast05Count)
                .FirstOrDefault(item => item.selectedBoneIndex >= 0);
            if (selected == null)
            {
                errors.Add("No SkinnedMeshRenderer exposes Animator RightHand or a hand_r bone.");
                Finish(report);
                yield break;
            }
            report.selectedRendererPath = FullPath(selected.renderer.transform);
            report.selectedRendererInstanceId = InstanceId(selected.renderer);

            float originalAnimatorSpeed = animator.speed;
            Vector3 originalCameraPosition = Camera.main != null ? Camera.main.transform.position : Vector3.zero;
            Quaternion originalCameraRotation = Camera.main != null ? Camera.main.transform.rotation : Quaternion.identity;
            float originalCameraSize = Camera.main != null ? Camera.main.orthographicSize : 0f;
            bool originalCameraOrthographic = Camera.main != null && Camera.main.orthographic;
            MonoBehaviour cameraRig = Camera.main != null ? Camera.main.GetComponents<MonoBehaviour>()
                .FirstOrDefault(item => item != null && item.GetType().FullName == "Qusap.QusapSharedCombatCamera") : null;
            bool cameraRigEnabled = cameraRig != null && cameraRig.enabled;
            if (cameraRig != null) cameraRig.enabled = false;
            animator.speed = 0f;

            var definitions = new[]
            {
                new SampleDefinition("Idle B1", "CombatIdle_B1", 0.20f),
                new SampleDefinition("Roll 0%", "RollFront", 0.00f),
                new SampleDefinition("Roll 25%", "RollFront", 0.25f),
                new SampleDefinition("Roll 50%", "RollFront", 0.50f),
                new SampleDefinition("Roll 75%", "RollFront", 0.75f),
                new SampleDefinition("Roll final", "RollFront", 0.999f)
            };
            var samples = new List<HandSocketSample>();
            var rightHandPositions = new List<Vector3>();
            foreach (SampleDefinition definition in definitions)
            {
                Pose(animator, definition.state, definition.normalizedTime);
                yield return null;
                samples.Add(Measure(definition, rightHand, socket, alignment, selected));
                rightHandPositions.Add(rightHand.position);
            }
            report.samples = samples.ToArray();
            HandSocketSample maximum = samples.OrderByDescending(item => item.rightHandToAlignment).First();
            report.maximumSeparationSample = maximum.label;
            report.maximumRightHandToAlignment = maximum.rightHandToAlignment;

            if (!report.socketParentIsAnimatorRightHandReference)
                report.preliminaryCase = "CASO 1";
            else
            {
                float visualVariation = samples.Where(item => item.visualCenterAvailable)
                    .Select(item => item.visualCenterToRightHand).DefaultIfEmpty(0f).Max()
                    - samples.Where(item => item.visualCenterAvailable)
                    .Select(item => item.visualCenterToRightHand).DefaultIfEmpty(0f).Min();
                bool sufficientWeights = selected.record.primaryRightHandVertexCount > 0
                    && selected.record.rightHandWeightAtLeast05Count > 0;
                float rightHandTravel = rightHandPositions.SelectMany(first => rightHandPositions.Select(second =>
                    Vector3.Distance(first, second))).DefaultIfEmpty(0f).Max();
                report.preliminaryCase = !sufficientWeights || visualVariation > 0.25f ? "CASO 2"
                    : rightHandTravel > 2f ? "CASO 4" : "CASO 3";
            }
            report.previousHarnessIssue =
                "The previous harness obtained RightHand from the driver's Animator but selected LargeGripAlignment globally with FindObjectsByType(...).FirstOrDefault, without proving both belonged to the same runtime hierarchy. It then interpreted RightHand-to-Alignment as claw-to-sword separation. In this prefab Alignment has a deliberate non-zero local offset under a hand hierarchy whose lossy scale is about 177.6, so that metric is amplified to about 10.34 world units even though RightHand-to-Socket is zero.";

            SampleDefinition maxDefinition = definitions.First(item => item.label == maximum.label);
            Pose(animator, maxDefinition.state, maxDefinition.normalizedTime);
            yield return null;
            Vector3 center;
            bool centerAvailable = TryDominatedCenter(selected, out center);
            GameObject green = CreateMarker("Animator_RightHand_GREEN", rightHand.position, Color.green);
            GameObject red = CreateMarker("WeaponSocket_RED", socket.position, Color.red);
            GameObject blue = centerAvailable
                ? CreateMarker("DominatedVerticesCenter_BLUE", center, Color.blue)
                : null;
            yield return CaptureDiagnostic(driver.transform.root, green, red, blue);
            Destroy(green);
            Destroy(red);
            if (blue != null) Destroy(blue);

            animator.speed = originalAnimatorSpeed;
            if (Camera.main != null)
            {
                Camera.main.transform.SetPositionAndRotation(originalCameraPosition, originalCameraRotation);
                Camera.main.orthographicSize = originalCameraSize;
                Camera.main.orthographic = originalCameraOrthographic;
            }
            if (cameraRig != null) cameraRig.enabled = cameraRigEnabled;
            Finish(report);
        }

        private sealed class RendererAnalysis
        {
            public SkinnedMeshRenderer renderer;
            public DiagnosticRendererRecord record;
            public int selectedBoneIndex;
            public int[] dominatedVertexIndices;
        }

        private readonly struct SampleDefinition
        {
            public readonly string label;
            public readonly string state;
            public readonly float normalizedTime;
            public SampleDefinition(string label, string state, float normalizedTime)
            {
                this.label = label;
                this.state = state;
                this.normalizedTime = normalizedTime;
            }
        }

        private static RendererAnalysis AnalyzeRenderer(SkinnedMeshRenderer renderer, Transform rightHand)
        {
            Transform[] bones = renderer.bones;
            int exactIndex = Array.FindIndex(bones, item => ReferenceEquals(item, rightHand));
            int namedIndex = Array.FindIndex(bones, item => item != null
                && (string.Equals(item.name, "hand_r", StringComparison.OrdinalIgnoreCase)
                    || string.Equals(item.name, "RightHand", StringComparison.OrdinalIgnoreCase)));
            int selectedIndex = exactIndex >= 0 ? exactIndex : namedIndex;
            Mesh mesh = renderer.sharedMesh;
            var record = new DiagnosticRendererRecord
            {
                name = renderer.name,
                path = FullPath(renderer.transform),
                instanceId = InstanceId(renderer),
                worldScale = Format(renderer.transform.lossyScale),
                meshName = mesh != null ? mesh.name : string.Empty,
                vertexCount = mesh != null ? mesh.vertexCount : 0,
                boneCount = bones.Length,
                bindposeCount = mesh != null ? mesh.bindposes.Length : 0,
                rootBonePath = renderer.rootBone != null ? FullPath(renderer.rootBone) : string.Empty,
                rootBoneInstanceId = renderer.rootBone != null ? InstanceId(renderer.rootBone) : 0,
                animatorRightHandBoneIndex = exactIndex,
                namedHandRBoneIndex = namedIndex
            };
            if (selectedIndex >= 0 && bones[selectedIndex] != null)
            {
                Transform selectedBone = bones[selectedIndex];
                record.selectedHandBoneName = selectedBone.name;
                record.selectedHandBonePath = FullPath(selectedBone);
                record.selectedHandBoneInstanceId = InstanceId(selectedBone);
                record.selectedBoneMatchesAnimatorRightHandReference = ReferenceEquals(selectedBone, rightHand);
                record.selectedBoneMatchesAnimatorRightHandInstanceId = InstanceId(selectedBone) == InstanceId(rightHand);
                if (mesh != null && selectedIndex < mesh.bindposes.Length)
                    record.selectedHandBindpose = Format(mesh.bindposes[selectedIndex]);
            }

            var dominated = new List<int>();
            if (mesh != null && selectedIndex >= 0)
            {
                try
                {
                    BoneWeight[] weights = mesh.boneWeights;
                    for (int vertex = 0; vertex < weights.Length; vertex++)
                    {
                        BoneWeight weight = weights[vertex];
                        float selectedWeight = WeightForBone(weight, selectedIndex);
                        int primaryIndex = PrimaryBoneIndex(weight);
                        if (primaryIndex == selectedIndex)
                        {
                            record.primaryRightHandVertexCount++;
                            dominated.Add(vertex);
                        }
                        if (selectedWeight >= 0.5f) record.rightHandWeightAtLeast05Count++;
                        if (selectedWeight >= 0.9f) record.rightHandWeightAtLeast09Count++;
                    }
                }
                catch (Exception exception)
                {
                    record.weightInspectionError = exception.GetType().Name + ": " + exception.Message;
                }
            }
            return new RendererAnalysis
            {
                renderer = renderer,
                record = record,
                selectedBoneIndex = selectedIndex,
                dominatedVertexIndices = dominated.ToArray()
            };
        }

        private static HandSocketSample Measure(SampleDefinition definition, Transform rightHand,
            Transform socket, Transform alignment, RendererAnalysis selected)
        {
            bool hasCenter = TryDominatedCenter(selected, out Vector3 center);
            return new HandSocketSample
            {
                label = definition.label,
                state = definition.state,
                normalizedTime = definition.normalizedTime,
                rightHandWorldPosition = Format(rightHand.position),
                socketWorldPosition = Format(socket.position),
                alignmentWorldPosition = Format(alignment.position),
                socketLocalPosition = Format(socket.localPosition),
                alignmentLocalPosition = Format(alignment.localPosition),
                rightHandToSocket = Vector3.Distance(rightHand.position, socket.position),
                rightHandToAlignment = Vector3.Distance(rightHand.position, alignment.position),
                socketToAlignment = Vector3.Distance(socket.position, alignment.position),
                visualCenterAvailable = hasCenter,
                visualCenterMethod = hasCenter ? "BakeMesh(useScale:true), averaged primary-RightHand vertices, then rotation/translation-only conversion to world" : string.Empty,
                dominatedVerticesWorldCenter = hasCenter ? Format(center) : string.Empty,
                visualCenterToRightHand = hasCenter ? Vector3.Distance(center, rightHand.position) : -1f,
                visualCenterToSocket = hasCenter ? Vector3.Distance(center, socket.position) : -1f
            };
        }

        private static bool TryDominatedCenter(RendererAnalysis analysis, out Vector3 center)
        {
            center = Vector3.zero;
            if (analysis == null || analysis.renderer == null || analysis.dominatedVertexIndices == null
                || analysis.dominatedVertexIndices.Length == 0) return false;

            var baked = new Mesh();
            try
            {
                // useScale:true bakes the renderer's scale into the output vertices. Applying
                // TransformPoint afterwards would apply the approximately 177.6 inherited scale
                // a second time, which was the source of the first diagnostic's ~100-unit centers.
                analysis.renderer.BakeMesh(baked, true);
                Vector3[] vertices = baked.vertices;
                int count = 0;
                foreach (int index in analysis.dominatedVertexIndices)
                {
                    if (index < 0 || index >= vertices.Length) continue;
                    center += analysis.renderer.transform.position
                        + analysis.renderer.transform.rotation * vertices[index];
                    count++;
                }
                if (count == 0) return false;
                center /= count;
                return true;
            }
            finally
            {
                Destroy(baked);
            }
        }

        private static IEnumerator CaptureDiagnostic(Transform playerRoot, params GameObject[] markers)
        {
            Camera camera = Camera.main;
            if (camera == null) yield break;
            Renderer[] included = playerRoot.GetComponentsInChildren<Renderer>(true)
                .Concat(markers.Where(item => item != null).SelectMany(item => item.GetComponentsInChildren<Renderer>(true)))
                .Where(item => item != null && item.enabled).ToArray();
            if (included.Length > 0)
            {
                Bounds bounds = included[0].bounds;
                for (int index = 1; index < included.Length; index++) bounds.Encapsulate(included[index].bounds);
                camera.orthographic = true;
                camera.orthographicSize = Mathf.Max(2.35f,
                    Mathf.Max(bounds.extents.y, bounds.extents.x / camera.aspect) * 1.2f);
                camera.transform.position = new Vector3(bounds.center.x, bounds.center.y, -20f);
                camera.transform.rotation = Quaternion.identity;
            }
            Renderer[] external = FindObjectsByType<Renderer>(FindObjectsInactive.Exclude)
                .Where(item => !item.transform.IsChildOf(playerRoot)
                    && !markers.Any(marker => marker != null && item.transform.IsChildOf(marker.transform))).ToArray();
            bool[] states = external.Select(item => item.enabled).ToArray();
            foreach (Renderer item in external) item.enabled = false;
            string absolute = ToAbsolutePath(ScreenshotPath);
            Directory.CreateDirectory(Path.GetDirectoryName(absolute));
            ScreenCapture.CaptureScreenshot(absolute, 2);
            yield return new WaitForEndOfFrame();
            yield return new WaitForSecondsRealtime(0.2f);
            for (int index = 0; index < external.Length; index++)
                if (external[index] != null) external[index].enabled = states[index];
        }

        private static GameObject CreateMarker(string name, Vector3 position, Color color)
        {
            GameObject marker = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            marker.name = name;
            marker.transform.position = position;
            marker.transform.localScale = Vector3.one * 0.14f;
            Collider collider = marker.GetComponent<Collider>();
            if (collider != null) Destroy(collider);
            Renderer renderer = marker.GetComponent<Renderer>();
            Shader shader = Shader.Find("Universal Render Pipeline/Unlit") ?? Shader.Find("Unlit/Color");
            var material = new Material(shader);
            if (material.HasProperty("_BaseColor")) material.SetColor("_BaseColor", color);
            if (material.HasProperty("_Color")) material.SetColor("_Color", color);
            renderer.material = material;
            return marker;
        }

        private static void Pose(Animator animator, string state, float normalizedTime)
        {
            animator.Play(state, 0, normalizedTime);
            animator.Update(0f);
            Physics.SyncTransforms();
        }

        private void Finish(HandSocketDiagnosticReport report)
        {
            report.generatedAtUtc = DateTime.UtcNow.ToString("O");
            report.consoleErrors = errors.ToArray();
            string absolute = ToAbsolutePath(ReportPath);
            Directory.CreateDirectory(Path.GetDirectoryName(absolute));
            File.WriteAllText(absolute, JsonUtility.ToJson(report, true));
            Application.logMessageReceived -= CaptureLog;
#if UNITY_EDITOR
            UnityEditor.EditorApplication.ExitPlaymode();
#endif
        }

        private static DiagnosticTransformRecord Describe(Transform transform)
        {
            var ancestors = new List<string>();
            for (Transform current = transform.parent; current != null; current = current.parent)
                ancestors.Add($"{current.name} [{InstanceId(current)}]");
            return new DiagnosticTransformRecord
            {
                name = transform.name,
                path = FullPath(transform),
                instanceId = InstanceId(transform),
                worldPosition = Format(transform.position),
                worldRotation = Format(transform.rotation),
                worldScale = Format(transform.lossyScale),
                localPosition = Format(transform.localPosition),
                localRotation = Format(transform.localRotation),
                localScale = Format(transform.localScale),
                parentName = transform.parent != null ? transform.parent.name : string.Empty,
                parentPath = transform.parent != null ? FullPath(transform.parent) : string.Empty,
                parentInstanceId = transform.parent != null ? InstanceId(transform.parent) : 0,
                ancestors = ancestors.ToArray()
            };
        }

        private static Transform FindDescendant(Transform root, string name)
        {
            if (root.name == name) return root;
            foreach (Transform child in root)
            {
                Transform found = FindDescendant(child, name);
                if (found != null) return found;
            }
            return null;
        }

        private static string FullPath(Transform transform)
        {
            var names = new Stack<string>();
            for (Transform current = transform; current != null; current = current.parent) names.Push(current.name);
            return string.Join("/", names);
        }

        private static float WeightForBone(BoneWeight weight, int boneIndex)
        {
            float result = 0f;
            if (weight.boneIndex0 == boneIndex) result += weight.weight0;
            if (weight.boneIndex1 == boneIndex) result += weight.weight1;
            if (weight.boneIndex2 == boneIndex) result += weight.weight2;
            if (weight.boneIndex3 == boneIndex) result += weight.weight3;
            return result;
        }

        private static int PrimaryBoneIndex(BoneWeight weight)
        {
            int index = weight.boneIndex0;
            float maximum = weight.weight0;
            if (weight.weight1 > maximum) { maximum = weight.weight1; index = weight.boneIndex1; }
            if (weight.weight2 > maximum) { maximum = weight.weight2; index = weight.boneIndex2; }
            if (weight.weight3 > maximum) index = weight.boneIndex3;
            return index;
        }

        // Unity 6.5 marks the public call obsolete-as-error in favor of EntityId. The user-facing
        // diagnostic explicitly requires the legacy runtime InstanceID, so query the same API
        // reflectively without changing any scene or asset identity.
        private static int InstanceId(UnityEngine.Object value)
        {
            if (value == null) return 0;
            var method = typeof(UnityEngine.Object).GetMethod("GetInstanceID",
                System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Public);
            return method != null ? (int)method.Invoke(value, null) : value.GetEntityId().GetHashCode();
        }

        private static string Format(Vector3 value) => FormattableString.Invariant(
            $"({value.x:F6}, {value.y:F6}, {value.z:F6})");
        private static string Format(Quaternion value) => FormattableString.Invariant(
            $"({value.x:F6}, {value.y:F6}, {value.z:F6}, {value.w:F6})");
        private static string Format(Matrix4x4 value) => FormattableString.Invariant(
            $"[{value.m00:F6},{value.m01:F6},{value.m02:F6},{value.m03:F6};{value.m10:F6},{value.m11:F6},{value.m12:F6},{value.m13:F6};{value.m20:F6},{value.m21:F6},{value.m22:F6},{value.m23:F6};{value.m30:F6},{value.m31:F6},{value.m32:F6},{value.m33:F6}]");
        private static string ToAbsolutePath(string assetPath) => Path.GetFullPath(Path.Combine(
            Application.dataPath, assetPath.Substring("Assets".Length).TrimStart('/', '\\')));
    }
}
