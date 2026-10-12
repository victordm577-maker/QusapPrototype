using UnityEngine;

namespace Qusap
{
    // Diagnostic projection only: no item list, ownership, animation or transfer state lives here.
    public sealed class QusapWorldLootContainerView : MonoBehaviour
    {
        [SerializeField] private string spawnPointId;
        [SerializeField] private QusapWorldLootTable table;
        [SerializeField] private float interactionRadius = 2.75f;
        [SerializeField] private bool requiresOpeningHold;
        private IQusapWorldLootAuthority authority;
        private Renderer visual;
        private MaterialPropertyBlock color;
        private int shownState = -1;
        public string SpawnPointId => spawnPointId;
        public QusapWorldLootTable Table => table;
        public bool RequiresOpeningHold => requiresOpeningHold;
        public QusapWorldLootContainerSnapshot Snapshot => authority?.ReadContainer(spawnPointId);
        public void Configure(string id, QusapWorldLootTable data) { spawnPointId = id; table = data; }
        public void RequireOpeningHold(bool required) => requiresOpeningHold = required;
        public void Bind(IQusapWorldLootAuthority owner) { authority = owner; visual = GetComponent<Renderer>(); color = new MaterialPropertyBlock(); }
        public bool CanReach(QusapRaidInventory player)
        {
            if (player == null || !player.CanOperate || interactionRadius <= 0) return false;
            // Physics position is authoritative; rendered transforms may still be interpolating.
            var body = player.GetComponent<Rigidbody>();
            return Vector3.Distance(body != null ? body.position : player.transform.position, transform.position) <= interactionRadius;
        }
        private void LateUpdate()
        {
            var state = Snapshot; if (state == null || visual == null || shownState == (int)state.State) return;
            shownState = (int)state.State;
            var tint = state.State == QusapWorldLootState.Unopened ? new Color(.3f, .5f, .65f)
                : state.State == QusapWorldLootState.Opened ? new Color(1f, .65f, .12f) : new Color(.18f, .22f, .25f);
            color.SetColor("_BaseColor", tint); color.SetColor("_Color", tint); visual.SetPropertyBlock(color);
        }
    }
}
