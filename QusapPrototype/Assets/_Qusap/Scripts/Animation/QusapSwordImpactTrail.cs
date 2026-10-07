using UnityEngine;

namespace Qusap
{
    [DefaultExecutionOrder(900)]
    [DisallowMultipleComponent]
    public sealed class QusapSwordImpactTrail : MonoBehaviour
    {
        private IQusapImpactVisualSource source;
        private Transform sword, previousParent;
        private TrailRenderer trail;
        private Material material;
        public TrailRenderer Trail => trail;
        public bool FeedbackEnabled { get; set; } = true;

        public void Initialize(IQusapImpactVisualSource visualSource) => source = visualSource;
        private void LateUpdate() => Refresh();
        public void Refresh()
        {
            Transform next = source?.ImpactSword;
            if (next != sword)
            {
                ClearTrail(); sword = next;
                if (sword != null) CreateTrail();
            }
            if (trail == null) return;
            if (sword.parent != previousParent) { trail.Clear(); previousParent = sword.parent; }
            bool active = isActiveAndEnabled && FeedbackEnabled && source != null && source.IsImpactSwordWindowActive;
            trail.emitting = active;
            trail.enabled = active;
            if (!active) trail.Clear();
        }
        private void CreateTrail()
        {
            GameObject tip = new("QusapImpactTrail"); tip.transform.SetParent(sword, false);
            MeshFilter mesh = sword.GetComponentInChildren<MeshFilter>(true);
            if (mesh != null && mesh.sharedMesh != null)
            {
                Bounds bounds = mesh.sharedMesh.bounds;
                Vector3 size = bounds.size;
                int axis = size.x >= size.y && size.x >= size.z ? 0 : size.y >= size.z ? 1 : 2;
                Vector3 endpoint = bounds.center;
                endpoint[axis] = Mathf.Abs(bounds.max[axis]) >= Mathf.Abs(bounds.min[axis]) ? bounds.max[axis] : bounds.min[axis];
                tip.transform.position = mesh.transform.TransformPoint(endpoint);
            }
            trail = tip.AddComponent<TrailRenderer>();
            trail.time = .065f; trail.minVertexDistance = .025f;
            trail.startWidth = .045f; trail.endWidth = 0f;
            trail.autodestruct = false; trail.emitting = false; trail.enabled = false;
            trail.startColor = new Color(1f, .85f, .35f, .75f); trail.endColor = new Color(1f, .65f, .15f, 0f);
            material = QusapCombatImpactWorld.GetOrCreate().SwordTrailMaterial;
            trail.sharedMaterial = material;
            previousParent = sword.parent;
        }
        private void OnDisable() { if (trail != null) { trail.emitting = false; trail.Clear(); trail.enabled = false; } }
        private void OnDestroy() => ClearTrail();
        private void ClearTrail()
        {
            if (trail != null) { if (Application.isPlaying) Destroy(trail.gameObject); else DestroyImmediate(trail.gameObject); }
            trail = null; material = null;
        }
    }
}
