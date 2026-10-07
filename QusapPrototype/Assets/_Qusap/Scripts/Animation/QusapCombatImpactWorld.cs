using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace Qusap
{
    // InputState deadlines keep running during a scaled pause. Consequently this
    // hit-stop holds only the rendered pose, and restores it after each camera.
    // Neither timeScale, fixedDeltaTime, Animator.speed nor gameplay is changed.
    [DefaultExecutionOrder(1000)]
    [DisallowMultipleComponent]
    public sealed class QusapCombatImpactWorld : MonoBehaviour
    {
        private static QusapCombatImpactWorld instance;
        private readonly List<PoseHold> holds = new();
        private Camera renderedCamera;
        private Vector3 cameraPosition;
        private double shakeStarted, shakeEnds;
        private float shakeAmplitude;
        private Material swordTrailMaterial;
        public Material SwordTrailMaterial
        {
            get
            {
                if (swordTrailMaterial == null)
                    swordTrailMaterial = new Material(Shader.Find("Sprites/Default")) { name = "QusapImpactTrail_RuntimeShared" };
                return swordTrailMaterial;
            }
        }
        public int HoldCount => holds.Count;
        public bool IsShaking => Time.unscaledTimeAsDouble < shakeEnds;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics() => instance = null;

        public static QusapCombatImpactWorld GetOrCreate()
        {
            if (instance == null)
                instance = new GameObject("QusapCombatImpactWorld").AddComponent<QusapCombatImpactWorld>();
            return instance;
        }

        private void OnEnable()
        {
            RenderPipelineManager.beginCameraRendering += BeginScriptableCamera;
            RenderPipelineManager.endCameraRendering += EndScriptableCamera;
            Camera.onPreCull += BeginBuiltInCamera;
            Camera.onPostRender += EndBuiltInCamera;
        }

        private void OnDisable()
        {
            RenderPipelineManager.beginCameraRendering -= BeginScriptableCamera;
            RenderPipelineManager.endCameraRendering -= EndScriptableCamera;
            Camera.onPreCull -= BeginBuiltInCamera;
            Camera.onPostRender -= EndBuiltInCamera;
            ResetPresentation();
        }
        private void OnDestroy()
        {
            if (instance == this) instance = null;
            if (swordTrailMaterial != null)
            {
                if (Application.isPlaying) Destroy(swordTrailMaterial); else DestroyImmediate(swordTrailMaterial);
            }
        }
        private void OnApplicationPause(bool paused) { if (paused) ResetPresentation(); }
        private void OnApplicationFocus(bool focused) { if (!focused) ResetPresentation(); }

        public void Request(Transform visual, QusapCombatImpactProfile profile)
        {
            if (!isActiveAndEnabled || profile.Hold <= 0f) return;
            double now = Time.unscaledTimeAsDouble;
            Refresh(now);
            if (visual != null)
            {
                PoseHold found = holds.Find(h => h.Root == visual);
                if (found == null) holds.Add(new PoseHold(visual, now + profile.Hold));
                else found.Ends = Math.Max(found.Ends, now + profile.Hold);
            }
            shakeAmplitude = IsShaking ? Mathf.Max(shakeAmplitude, profile.ShakeAmplitude) : profile.ShakeAmplitude;
            shakeStarted = now;
            shakeEnds = Math.Max(shakeEnds, now + profile.ShakeDuration);
        }

        public void Release(Transform visual)
        {
            EndCamera();
            holds.RemoveAll(h => h.Root == null || h.Root == visual);
            if (holds.Count == 0) { shakeEnds = 0; shakeAmplitude = 0; }
        }

        private void LateUpdate() => Refresh(Time.unscaledTimeAsDouble);
        public void Refresh(double now)
        {
            EndCamera();
            holds.RemoveAll(h => h.Root == null || !h.Root.gameObject.activeInHierarchy || now >= h.Ends);
            if (now >= shakeEnds) { shakeEnds = 0; shakeAmplitude = 0; }
        }

        private void BeginScriptableCamera(ScriptableRenderContext context, Camera camera) => BeginCamera(camera);
        private void EndScriptableCamera(ScriptableRenderContext context, Camera camera) => EndCamera();
        private void BeginBuiltInCamera(Camera camera) { if (GraphicsSettings.currentRenderPipeline == null) BeginCamera(camera); }
        private void EndBuiltInCamera(Camera camera) { if (GraphicsSettings.currentRenderPipeline == null) EndCamera(); }

        public void BeginCamera(Camera camera)
        {
            if (!isActiveAndEnabled || camera == null || camera.cameraType != CameraType.Game) return;
            EndCamera();
            double now = Time.unscaledTimeAsDouble;
            Refresh(now);
            foreach (PoseHold hold in holds) hold.Apply();
            renderedCamera = camera;
            cameraPosition = camera.transform.position;
            if (now < shakeEnds)
            {
                float elapsed = (float)(now - shakeStarted);
                float fade = Mathf.Clamp01((float)((shakeEnds - now) / Math.Max(shakeEnds - shakeStarted, .001)));
                Vector3 offset = new(Mathf.Cos(elapsed * 183f), Mathf.Sin(elapsed * 157f), 0f);
                camera.transform.position = cameraPosition + camera.transform.TransformDirection(offset * shakeAmplitude * fade);
            }
        }

        public void EndCamera()
        {
            foreach (PoseHold hold in holds) hold.Restore();
            if (renderedCamera != null) renderedCamera.transform.position = cameraPosition;
            renderedCamera = null;
        }

        public void ResetPresentation()
        {
            EndCamera(); holds.Clear(); shakeEnds = 0; shakeAmplitude = 0;
        }

        private sealed class PoseHold
        {
            public readonly Transform Root;
            public double Ends;
            private readonly Transform[] bones;
            private readonly Transform[] parents;
            private readonly Vector3[] heldPosition, livePosition;
            private readonly Quaternion[] heldRotation, liveRotation;
            private Vector3 heldRoot, liveRoot;
            private bool captured, applied;

            public PoseHold(Transform root, double ends)
            {
                Root = root; Ends = ends; bones = root.GetComponentsInChildren<Transform>(true);
                parents = new Transform[bones.Length];
                heldPosition = new Vector3[bones.Length]; livePosition = new Vector3[bones.Length];
                heldRotation = new Quaternion[bones.Length]; liveRotation = new Quaternion[bones.Length];
            }
            public void Apply()
            {
                if (Root == null) return;
                for (int i = 0; i < bones.Length; i++)
                    if (bones[i] != null && captured && bones[i].parent != parents[i]) captured = false;
                liveRoot = Root.position;
                if (!captured) heldRoot = liveRoot;
                for (int i = 0; i < bones.Length; i++)
                {
                    if (bones[i] == null) continue;
                    livePosition[i] = bones[i].localPosition; liveRotation[i] = bones[i].localRotation;
                    if (!captured) { parents[i] = bones[i].parent; heldPosition[i] = livePosition[i]; heldRotation[i] = liveRotation[i]; }
                    bones[i].SetLocalPositionAndRotation(heldPosition[i], heldRotation[i]);
                }
                Root.position = heldRoot; captured = true; applied = true;
            }
            public void Restore()
            {
                if (!applied) return;
                for (int i = 0; i < bones.Length; i++)
                    if (bones[i] != null) bones[i].SetLocalPositionAndRotation(livePosition[i], liveRotation[i]);
                if (Root != null) Root.position = liveRoot;
                applied = false;
            }
        }
    }
}
