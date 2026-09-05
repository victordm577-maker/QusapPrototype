#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Qusap;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Qusap.EditorTools
{
    public static class QusapGroundAlignedLocomotionTestCreator
    {
        private const string MenuPath =
            "Tools/Qusap/Create Ground-Aligned Locomotion Test";
        private const string InitialSceneName =
            "Qusap_Luz_Locomotion_GroundAlignedTest_v1";
        private const string IdleClipName = "Qusap_Idle";
        private const string LeftFootName = "FloatingFoot_L";
        private const string RightFootName = "FloatingFoot_R";
        private const string BodyName = "Body";
        private const string ArmatureRootName = "Root";
        private const string DialogTitle = "Ground-Aligned Locomotion Test";
        private const float AlignmentTolerance = 0.01f;
        private const float PoseTolerance = 0.0001f;

        [MenuItem(MenuPath)]
        private static void CreateGroundAlignedTest()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode)
            {
                ShowError("La herramienta no puede ejecutarse mientras Unity está entrando o se encuentra en Play Mode.");
                return;
            }

            if (AnimationMode.InAnimationMode())
            {
                ShowError("Sal de Animation Mode antes de crear la prueba alineada al suelo.");
                return;
            }

            Scene sourceScene = SceneManager.GetActiveScene();
            if (!TryResolveContext(sourceScene, out PlayerContext context, out string error))
            {
                ShowError(error + "\n\nNo se modificó ningún asset ni objeto de escena.");
                return;
            }

            string targetScenePath = BuildAvailableScenePath(sourceScene.path);
            if (!CanWriteDestination(targetScenePath))
            {
                return;
            }

            try
            {
                // This is a Save As operation: the in-memory scene becomes the new copy,
                // while the original scene asset on disk remains untouched.
                if (!EditorSceneManager.SaveScene(sourceScene, targetScenePath, false))
                {
                    ShowError(
                        $"Unity no pudo crear la copia de escena en:\n{targetScenePath}\n\n" +
                        "La escena original no fue modificada.");
                    return;
                }

                Scene copiedScene = SceneManager.GetActiveScene();
                float previousLocalY = context.VisualRoot.localPosition.y;
                Vector3 originalVisualPosition = context.VisualRoot.localPosition;
                Quaternion originalVisualRotation = context.VisualRoot.localRotation;
                Vector3 originalVisualScale = context.VisualRoot.localScale;
                LocalPose originalArmatureRootPose = LocalPose.Capture(context.ArmatureRoot);

                AlignmentMeasurement before = MeasureIdlePose(context);
                float previousSeparation = before.FootBottomY - before.ColliderBottomY;
                float worldYPerLocalY = context.VisualRoot.parent
                    .TransformVector(Vector3.up).y;
                if (Mathf.Abs(worldYPerLocalY) < 0.000001f)
                {
                    throw new InvalidOperationException(
                        "El eje Y local del PlayerVisual no produce un desplazamiento vertical mundial utilizable.");
                }

                Undo.SetCurrentGroupName("Ground-align Qusap PlayerVisual");
                int undoGroup = Undo.GetCurrentGroup();
                Undo.RecordObject(context.VisualRoot, "Align PlayerVisual feet to physical collider");

                Vector3 correctedPosition = originalVisualPosition;
                correctedPosition.y = previousLocalY - (previousSeparation / worldYPerLocalY);
                context.VisualRoot.localPosition = correctedPosition;
                Physics.SyncTransforms();
                PrefabUtility.RecordPrefabInstancePropertyModifications(context.VisualRoot);

                AlignmentMeasurement after = MeasureIdlePose(context);
                float finalSeparation = after.FootBottomY - after.ColliderBottomY;

                if (Mathf.Abs(finalSeparation) > AlignmentTolerance)
                {
                    // A single residual correction is allowed and still changes only localPosition.y.
                    correctedPosition = context.VisualRoot.localPosition;
                    correctedPosition.y -= finalSeparation / worldYPerLocalY;
                    context.VisualRoot.localPosition = correctedPosition;
                    Physics.SyncTransforms();
                    PrefabUtility.RecordPrefabInstancePropertyModifications(context.VisualRoot);

                    after = MeasureIdlePose(context);
                    finalSeparation = after.FootBottomY - after.ColliderBottomY;
                }

                ValidateResult(
                    context,
                    before,
                    after,
                    originalVisualPosition,
                    originalVisualRotation,
                    originalVisualScale,
                    originalArmatureRootPose,
                    finalSeparation);

                EditorSceneManager.MarkSceneDirty(copiedScene);
                if (!EditorSceneManager.SaveScene(copiedScene))
                {
                    throw new InvalidOperationException(
                        $"Unity no pudo guardar la escena alineada '{targetScenePath}'.");
                }

                Undo.CollapseUndoOperations(undoGroup);
                Selection.activeTransform = context.VisualRoot;

                string report =
                    $"localPosition.y anterior: {previousLocalY:0.######}\n" +
                    $"localPosition.y nuevo: {context.VisualRoot.localPosition.y:0.######}\n" +
                    $"Separación anterior: {previousSeparation:0.######}\n" +
                    $"Separación final: {finalSeparation:0.######}\n" +
                    $"Escena nueva: {targetScenePath}";

                Debug.Log(report, context.VisualRoot);
                EditorUtility.DisplayDialog(DialogTitle, report, "Aceptar");
            }
            catch (Exception exception)
            {
                Debug.LogException(exception);
                ShowError(
                    "No se pudo completar la alineación. La escena original permanece intacta; " +
                    "revisa la copia y la consola para conocer el detalle.\n\n" +
                    exception.Message);
            }
        }

        private static bool TryResolveContext(
            Scene scene,
            out PlayerContext context,
            out string error)
        {
            context = null;
            error = null;

            if (!scene.IsValid() || !scene.isLoaded)
            {
                error = "No hay una escena activa válida y cargada.";
                return false;
            }

            if (string.IsNullOrEmpty(scene.path)
                || !scene.path.StartsWith("Assets/", StringComparison.Ordinal))
            {
                error = "La escena activa debe estar guardada dentro de Assets.";
                return false;
            }

            if (Path.GetFileNameWithoutExtension(scene.path)
                .StartsWith("Qusap_Luz_Locomotion_GroundAlignedTest_v", StringComparison.Ordinal))
            {
                error = "La escena activa ya es una prueba GroundAligned. Abre la escena de locomoción que quieres copiar.";
                return false;
            }

            QusapAnimationDriver[] activeDrivers = scene.GetRootGameObjects()
                .SelectMany(root => root.GetComponentsInChildren<QusapAnimationDriver>(false))
                .Where(driver =>
                    driver != null
                    && driver.isActiveAndEnabled
                    && driver.gameObject.scene == scene)
                .ToArray();
            if (activeDrivers.Length != 1)
            {
                error =
                    $"Se esperaba un único QusapAnimationDriver activo, pero se encontraron {activeDrivers.Length}.";
                return false;
            }

            QusapAnimationDriver driver = activeDrivers[0];
            Transform[] physicalCandidates = EnumerateAncestors(driver.transform)
                .Where(candidate => candidate.GetComponents<Rigidbody>().Length == 1)
                .ToArray();
            if (physicalCandidates.Length != 1)
            {
                error =
                    "No se pudo resolver sin ambigüedad un único objeto físico con Rigidbody desde QusapAnimationDriver.";
                return false;
            }

            Transform physicalRoot = physicalCandidates[0];
            Rigidbody rigidbody = physicalRoot.GetComponent<Rigidbody>();
            Collider[] physicalColliders = physicalRoot.GetComponents<Collider>()
                .Where(collider =>
                    collider != null
                    && collider.enabled
                    && !collider.isTrigger
                    && collider.gameObject.activeInHierarchy)
                .ToArray();
            if (physicalColliders.Length != 1)
            {
                error =
                    $"El objeto físico '{physicalRoot.name}' debe tener exactamente un Collider activo y no trigger; " +
                    $"se encontraron {physicalColliders.Length}.";
                return false;
            }

            Animator[] activeAnimators = physicalRoot.GetComponentsInChildren<Animator>(false)
                .Where(animator =>
                    animator != null
                    && animator.enabled
                    && animator.gameObject.activeInHierarchy)
                .ToArray();
            if (activeAnimators.Length != 1)
            {
                error =
                    $"Se esperaba un único Animator activo debajo de '{physicalRoot.name}', " +
                    $"pero se encontraron {activeAnimators.Length}.";
                return false;
            }

            Animator activeAnimator = activeAnimators[0];
            Transform visualRoot = FindDirectChildRoot(physicalRoot, activeAnimator.transform);
            if (visualRoot == null || !visualRoot.gameObject.activeInHierarchy)
            {
                error = "No se pudo resolver la raíz visual activa que contiene el Animator.";
                return false;
            }

            AnimationClip[] idleMatches = activeAnimator.runtimeAnimatorController == null
                ? Array.Empty<AnimationClip>()
                : activeAnimator.runtimeAnimatorController.animationClips
                    .Where(clip => clip != null && clip.name == IdleClipName)
                    .Distinct()
                    .ToArray();
            if (idleMatches.Length != 1)
            {
                error =
                    $"El Animator activo debe resolver exactamente un clip '{IdleClipName}'; " +
                    $"se encontraron {idleMatches.Length}.";
                return false;
            }

            SkinnedMeshRenderer[] activeRenderers = visualRoot
                .GetComponentsInChildren<SkinnedMeshRenderer>(false)
                .Where(renderer => renderer.enabled && renderer.gameObject.activeInHierarchy)
                .ToArray();

            if (!TryFindUniqueRenderer(activeRenderers, LeftFootName, out SkinnedMeshRenderer leftFoot, out error)
                || !TryFindUniqueRenderer(activeRenderers, RightFootName, out SkinnedMeshRenderer rightFoot, out error)
                || !TryFindUniqueRenderer(activeRenderers, BodyName, out SkinnedMeshRenderer body, out error))
            {
                return false;
            }

            if (leftFoot == rightFoot || leftFoot == body || rightFoot == body)
            {
                error = "FloatingFoot_L, FloatingFoot_R y Body deben corresponder a renderizadores distintos.";
                return false;
            }

            Transform[] armatureRoots = visualRoot.GetComponentsInChildren<Transform>(false)
                .Where(candidate => candidate.name == ArmatureRootName)
                .ToArray();
            if (armatureRoots.Length != 1)
            {
                error =
                    $"Se esperaba un único transform de armadura llamado '{ArmatureRootName}', " +
                    $"pero se encontraron {armatureRoots.Length}.";
                return false;
            }

            context = new PlayerContext(
                driver,
                physicalRoot,
                rigidbody,
                physicalColliders[0],
                activeAnimator,
                visualRoot,
                idleMatches[0],
                leftFoot,
                rightFoot,
                body,
                armatureRoots[0]);
            return true;
        }

        private static IEnumerable<Transform> EnumerateAncestors(Transform start)
        {
            for (Transform current = start; current != null; current = current.parent)
            {
                yield return current;
            }
        }

        private static Transform FindDirectChildRoot(Transform physicalRoot, Transform descendant)
        {
            Transform current = descendant;
            while (current != null && current.parent != physicalRoot)
            {
                current = current.parent;
            }

            return current != null && current.parent == physicalRoot ? current : null;
        }

        private static bool TryFindUniqueRenderer(
            IEnumerable<SkinnedMeshRenderer> renderers,
            string requiredName,
            out SkinnedMeshRenderer renderer,
            out string error)
        {
            SkinnedMeshRenderer[] matches = renderers
                .Where(candidate => RendererMatchesName(candidate, requiredName))
                .ToArray();
            renderer = matches.Length == 1 ? matches[0] : null;
            error = matches.Length == 1
                ? null
                : $"Se esperaba un único SkinnedMeshRenderer correspondiente a '{requiredName}', " +
                  $"pero se encontraron {matches.Length}.";
            return matches.Length == 1;
        }

        private static bool RendererMatchesName(SkinnedMeshRenderer renderer, string requiredName)
        {
            string meshName = renderer.sharedMesh != null ? renderer.sharedMesh.name : null;
            return IsRendererNameMatch(renderer.name, requiredName)
                || IsRendererNameMatch(meshName, requiredName);
        }

        private static bool IsRendererNameMatch(string candidate, string requiredName)
        {
            return string.Equals(candidate, requiredName, StringComparison.Ordinal)
                || string.Equals(candidate, requiredName + "_Mesh", StringComparison.Ordinal);
        }

        private static AlignmentMeasurement MeasureIdlePose(PlayerContext context)
        {
            if (AnimationMode.InAnimationMode())
            {
                throw new InvalidOperationException("Unity ya se encuentra en Animation Mode.");
            }

            Vector3 expectedVisualPosition = context.VisualRoot.localPosition;
            Quaternion expectedVisualRotation = context.VisualRoot.localRotation;
            Vector3 expectedVisualScale = context.VisualRoot.localScale;
            bool sampling = false;

            AnimationMode.StartAnimationMode();
            try
            {
                AnimationMode.BeginSampling();
                sampling = true;
                AnimationMode.SampleAnimationClip(
                    context.VisualRoot.gameObject,
                    context.IdleClip,
                    0f);
                AnimationMode.EndSampling();
                sampling = false;

                if (!Approximately(context.VisualRoot.localPosition, expectedVisualPosition)
                    || Quaternion.Angle(context.VisualRoot.localRotation, expectedVisualRotation) > PoseTolerance
                    || !Approximately(context.VisualRoot.localScale, expectedVisualScale))
                {
                    throw new InvalidOperationException(
                        "Qusap_Idle intentó mover la raíz visual durante el muestreo; la herramienta no aplicará una corrección insegura.");
                }

                Physics.SyncTransforms();
                SceneView.RepaintAll();

                BakedGeometry left = BakeGeometry(context.LeftFoot, context.VisualRoot);
                BakedGeometry right = BakeGeometry(context.RightFoot, context.VisualRoot);
                BakedGeometry body = BakeGeometry(context.Body, context.VisualRoot);
                float footBottom = Mathf.Min(left.LowestWorldY, right.LowestWorldY);
                float colliderBottom = context.PhysicalCollider.bounds.min.y;

                return new AlignmentMeasurement(
                    footBottom,
                    colliderBottom,
                    left.CenterInVisualSpace,
                    right.CenterInVisualSpace,
                    body.CenterInVisualSpace,
                    LocalPose.Capture(context.ArmatureRoot));
            }
            finally
            {
                if (sampling)
                {
                    AnimationMode.EndSampling();
                }

                if (AnimationMode.InAnimationMode())
                {
                    AnimationMode.StopAnimationMode();
                }
            }
        }

        private static BakedGeometry BakeGeometry(
            SkinnedMeshRenderer renderer,
            Transform visualRoot)
        {
            Mesh bakedMesh = new Mesh
            {
                name = renderer.name + "_GroundAlignmentSample",
                hideFlags = HideFlags.HideAndDontSave
            };

            try
            {
                renderer.BakeMesh(bakedMesh, false);
                var vertices = new List<Vector3>(bakedMesh.vertexCount);
                bakedMesh.GetVertices(vertices);
                if (vertices.Count == 0)
                {
                    throw new InvalidOperationException(
                        $"El renderer '{renderer.name}' no produjo vértices al hornear su pose.");
                }

                Vector3 firstWorld = renderer.transform.TransformPoint(vertices[0]);
                Bounds worldBounds = new Bounds(firstWorld, Vector3.zero);
                float lowestWorldY = firstWorld.y;
                for (int index = 1; index < vertices.Count; index++)
                {
                    Vector3 worldVertex = renderer.transform.TransformPoint(vertices[index]);
                    worldBounds.Encapsulate(worldVertex);
                    lowestWorldY = Mathf.Min(lowestWorldY, worldVertex.y);
                }

                return new BakedGeometry(
                    lowestWorldY,
                    visualRoot.InverseTransformPoint(worldBounds.center));
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(bakedMesh);
            }
        }

        private static void ValidateResult(
            PlayerContext context,
            AlignmentMeasurement before,
            AlignmentMeasurement after,
            Vector3 originalVisualPosition,
            Quaternion originalVisualRotation,
            Vector3 originalVisualScale,
            LocalPose originalArmatureRootPose,
            float finalSeparation)
        {
            if (Mathf.Abs(finalSeparation) > AlignmentTolerance)
            {
                throw new InvalidOperationException(
                    $"La separación final es {finalSeparation:0.######}, mayor que la tolerancia {AlignmentTolerance:0.##}.");
            }

            Vector3 finalVisualPosition = context.VisualRoot.localPosition;
            if (!Mathf.Approximately(finalVisualPosition.x, originalVisualPosition.x)
                || !Mathf.Approximately(finalVisualPosition.z, originalVisualPosition.z)
                || Quaternion.Angle(context.VisualRoot.localRotation, originalVisualRotation) > PoseTolerance
                || !Approximately(context.VisualRoot.localScale, originalVisualScale))
            {
                throw new InvalidOperationException(
                    "La corrección alteró X, Z, rotación o escala del PlayerVisual.");
            }

            float leftBodyDistanceBefore = Vector3.Distance(before.LeftCenter, before.BodyCenter);
            float rightBodyDistanceBefore = Vector3.Distance(before.RightCenter, before.BodyCenter);
            float leftBodyDistanceAfter = Vector3.Distance(after.LeftCenter, after.BodyCenter);
            float rightBodyDistanceAfter = Vector3.Distance(after.RightCenter, after.BodyCenter);
            if (leftBodyDistanceBefore <= PoseTolerance
                || rightBodyDistanceBefore <= PoseTolerance
                || Mathf.Abs(leftBodyDistanceAfter - leftBodyDistanceBefore) > PoseTolerance
                || Mathf.Abs(rightBodyDistanceAfter - rightBodyDistanceBefore) > PoseTolerance)
            {
                throw new InvalidOperationException(
                    "Los dos pies no conservaron su separación relativa respecto al Body.");
            }

            if (!before.ArmatureRootPose.Matches(after.ArmatureRootPose)
                || !originalArmatureRootPose.Matches(LocalPose.Capture(context.ArmatureRoot)))
            {
                throw new InvalidOperationException(
                    "El transform Root de la armadura cambió durante la corrección.");
            }

            if (context.Rigidbody == null
                || context.PhysicalCollider == null
                || context.Driver == null
                || context.PhysicalRoot == null)
            {
                throw new InvalidOperationException(
                    "La referencia al jugador físico cambió durante la corrección.");
            }
        }

        private static bool Approximately(Vector3 left, Vector3 right)
        {
            return (left - right).sqrMagnitude <= PoseTolerance * PoseTolerance;
        }

        private static string BuildAvailableScenePath(string sourceScenePath)
        {
            string directory = Path.GetDirectoryName(sourceScenePath);
            for (int version = 1; version <= 999; version++)
            {
                string sceneName = version == 1
                    ? InitialSceneName
                    : InitialSceneName.Substring(0, InitialSceneName.Length - 1) + version;
                string candidate = CombineAssetPath(directory, sceneName + ".unity");
                if (AssetDatabase.LoadAssetAtPath<SceneAsset>(candidate) == null
                    && !File.Exists(candidate))
                {
                    return candidate;
                }
            }

            throw new InvalidOperationException(
                "No se encontró un número de versión disponible para la escena GroundAligned.");
        }

        private static string CombineAssetPath(string directory, string fileName)
        {
            return (directory + "/" + fileName).Replace('\\', '/');
        }

        private static bool CanWriteDestination(string assetPath)
        {
            if (AssetDatabase.LoadMainAssetAtPath(assetPath) == null
                || AssetDatabase.IsOpenForEdit(assetPath, StatusQueryOptions.UseCachedIfPossible))
            {
                return true;
            }

            ShowError($"La escena de destino no está disponible para edición:\n{assetPath}");
            return false;
        }

        private static void ShowError(string message)
        {
            Debug.LogError(message);
            EditorUtility.DisplayDialog(DialogTitle, message, "Aceptar");
        }

        private sealed class PlayerContext
        {
            public PlayerContext(
                QusapAnimationDriver driver,
                Transform physicalRoot,
                Rigidbody rigidbody,
                Collider physicalCollider,
                Animator animator,
                Transform visualRoot,
                AnimationClip idleClip,
                SkinnedMeshRenderer leftFoot,
                SkinnedMeshRenderer rightFoot,
                SkinnedMeshRenderer body,
                Transform armatureRoot)
            {
                Driver = driver;
                PhysicalRoot = physicalRoot;
                Rigidbody = rigidbody;
                PhysicalCollider = physicalCollider;
                Animator = animator;
                VisualRoot = visualRoot;
                IdleClip = idleClip;
                LeftFoot = leftFoot;
                RightFoot = rightFoot;
                Body = body;
                ArmatureRoot = armatureRoot;
            }

            public QusapAnimationDriver Driver { get; }
            public Transform PhysicalRoot { get; }
            public Rigidbody Rigidbody { get; }
            public Collider PhysicalCollider { get; }
            public Animator Animator { get; }
            public Transform VisualRoot { get; }
            public AnimationClip IdleClip { get; }
            public SkinnedMeshRenderer LeftFoot { get; }
            public SkinnedMeshRenderer RightFoot { get; }
            public SkinnedMeshRenderer Body { get; }
            public Transform ArmatureRoot { get; }
        }

        private readonly struct BakedGeometry
        {
            public BakedGeometry(float lowestWorldY, Vector3 centerInVisualSpace)
            {
                LowestWorldY = lowestWorldY;
                CenterInVisualSpace = centerInVisualSpace;
            }

            public float LowestWorldY { get; }
            public Vector3 CenterInVisualSpace { get; }
        }

        private readonly struct AlignmentMeasurement
        {
            public AlignmentMeasurement(
                float footBottomY,
                float colliderBottomY,
                Vector3 leftCenter,
                Vector3 rightCenter,
                Vector3 bodyCenter,
                LocalPose armatureRootPose)
            {
                FootBottomY = footBottomY;
                ColliderBottomY = colliderBottomY;
                LeftCenter = leftCenter;
                RightCenter = rightCenter;
                BodyCenter = bodyCenter;
                ArmatureRootPose = armatureRootPose;
            }

            public float FootBottomY { get; }
            public float ColliderBottomY { get; }
            public Vector3 LeftCenter { get; }
            public Vector3 RightCenter { get; }
            public Vector3 BodyCenter { get; }
            public LocalPose ArmatureRootPose { get; }
        }

        private readonly struct LocalPose
        {
            private LocalPose(Vector3 position, Quaternion rotation, Vector3 scale)
            {
                Position = position;
                Rotation = rotation;
                Scale = scale;
            }

            private Vector3 Position { get; }
            private Quaternion Rotation { get; }
            private Vector3 Scale { get; }

            public static LocalPose Capture(Transform transform)
            {
                return new LocalPose(
                    transform.localPosition,
                    transform.localRotation,
                    transform.localScale);
            }

            public bool Matches(LocalPose other)
            {
                return Approximately(Position, other.Position)
                    && Quaternion.Angle(Rotation, other.Rotation) <= PoseTolerance
                    && Approximately(Scale, other.Scale);
            }
        }
    }
}
#endif
