using UnityEngine;

namespace Qusap
{
    [RequireComponent(typeof(QusapRaidIntegratedPlaytest))]
    public sealed class QusapRaidIntegratedHazardView : MonoBehaviour
    {
        private QusapRaidIntegratedPlaytest scene;
        private LineRenderer current, warning;
        private Material safeMaterial, warningMaterial, dangerMaterial;
        private Mesh mesh;
        private readonly Transform[] panels = new Transform[4];
        private void Start()
        {
            scene = GetComponent<QusapRaidIntegratedPlaytest>();
            var shader = Shader.Find("Universal Render Pipeline/Unlit");
            safeMaterial = new(shader) { color = new(.2f, 1, .35f) };
            warningMaterial = new(shader) { color = Color.yellow }; dangerMaterial = new(shader) { color = new(.26f, .04f, .03f) };
            current = Line("SafeBounds_Green", safeMaterial); warning = Line("NextBounds_Yellow", warningMaterial);
            mesh = new Mesh { vertices = new[] { new Vector3(-.5f, -.5f), new Vector3(-.5f, .5f), new Vector3(.5f, .5f), new Vector3(.5f, -.5f) }, triangles = new[] { 0, 1, 2, 0, 2, 3 } }; mesh.RecalculateNormals();
            for (int i = 0; i < 4; i++)
            {
                var go = new GameObject("DangerBackdrop_" + i, typeof(MeshFilter), typeof(MeshRenderer)); go.transform.SetParent(transform);
                go.GetComponent<MeshFilter>().sharedMesh = mesh; go.GetComponent<MeshRenderer>().sharedMaterial = dangerMaterial; panels[i] = go.transform;
            }
        }
        private LineRenderer Line(string name, Material material)
        {
            var go = new GameObject(name, typeof(LineRenderer)); go.transform.SetParent(transform);
            var line = go.GetComponent<LineRenderer>(); line.sharedMaterial = material; line.loop = true; line.positionCount = 4; line.widthMultiplier = .04f; return line;
        }
        private static void Draw(LineRenderer line, QusapRaidSafeArea area) => line.SetPositions(new[] {
            new Vector3(area.MinX,area.MinY,-.6f), new Vector3(area.MinX,area.MaxY,-.6f),
            new Vector3(area.MaxX,area.MaxY,-.6f), new Vector3(area.MaxX,area.MinY,-.6f)});
        private void LateUpdate()
        {
            var hazard = scene.Hazard; bool visible = hazard != null && !scene.Preparing;
            current.gameObject.SetActive(visible); warning.gameObject.SetActive(visible && hazard.WarningActive);
            if (!visible) { foreach (var panel in panels) panel.gameObject.SetActive(false); return; }
            Draw(current, hazard.CurrentArea); if (hazard.NextStage != null) Draw(warning, hazard.NextStage.Area);
            var a = hazard.CurrentArea;
            Panel(0, -13, -.6f, a.MinX, 11); Panel(1, a.MaxX, -.6f, 13, 11);
            Panel(2, a.MinX, -.6f, a.MaxX, a.MinY); Panel(3, a.MinX, a.MaxY, a.MaxX, 11);
        }
        private void Panel(int i, float x0, float y0, float x1, float y1)
        {
            panels[i].gameObject.SetActive(scene.Hazard.StageIndex >= 0 && x1 > x0 && y1 > y0);
            panels[i].position = new((x0 + x1) / 2, (y0 + y1) / 2, 2); panels[i].localScale = new(x1 - x0, y1 - y0, 1);
        }
        private void OnDestroy() { Destroy(safeMaterial); Destroy(warningMaterial); Destroy(dangerMaterial); Destroy(mesh); }
    }
}
