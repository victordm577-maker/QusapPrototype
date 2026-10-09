using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace Qusap
{
    [DisallowMultipleComponent]
    [RequireComponent(typeof(BoxCollider))]
    public sealed class QusapExtractionVolume : MonoBehaviour
    {
        [SerializeField, Min(0.01f)] private float requiredSeconds = 5f;
        private BoxCollider volume;
        private readonly Dictionary<QusapRaidExtraction, HashSet<Collider>> occupants = new();
        public float RequiredSeconds => requiredSeconds;
        public bool Available => isActiveAndEnabled && volume != null && volume.enabled && volume.isTrigger;
        public bool Contains(QusapRaidExtraction player) => occupants.TryGetValue(player, out var colliders)
            && colliders.Any(c => c != null && c.enabled && c.gameObject.activeInHierarchy);
        public void Configure(float seconds)
        {
            if (!float.IsFinite(seconds) || seconds <= 0f) throw new System.ArgumentOutOfRangeException(nameof(seconds));
            requiredSeconds = seconds;
        }
        private void Awake() { volume = GetComponent<BoxCollider>(); }
        private void Reset() { GetComponent<BoxCollider>().isTrigger = true; }
        private void OnValidate() { if (!float.IsFinite(requiredSeconds) || requiredSeconds <= 0f) requiredSeconds = 5f; }
        private void OnTriggerEnter(Collider other)
        {
            if (!Available) return;
            var player = other.GetComponentInParent<QusapRaidExtraction>();
            if (player == null) return;
            if (!occupants.TryGetValue(player, out var contacts)) occupants.Add(player, contacts = new HashSet<Collider>());
            bool wasInside = contacts.Count > 0;
            contacts.Add(other);
            if (!wasInside) player.TryBegin(this);
        }
        private void OnTriggerExit(Collider other)
        {
            var player = other.GetComponentInParent<QusapRaidExtraction>();
            if (player == null || !occupants.TryGetValue(player, out var contacts)) return;
            contacts.Remove(other);
            if (contacts.Count != 0) return;
            occupants.Remove(player); player.Leave(this, QusapExtractionCancellation.LeftVolume);
        }
        private void Update()
        {
            if (!Available) { CancelAll(); return; }
            foreach (var pair in occupants.ToArray())
            {
                pair.Value.RemoveWhere(c => c == null || !c.enabled || !c.gameObject.activeInHierarchy);
                if (pair.Value.Count != 0) continue;
                occupants.Remove(pair.Key);
                if (pair.Key != null) pair.Key.Leave(this, QusapExtractionCancellation.LeftVolume);
            }
        }
        private void OnDisable() => CancelAll();
        private void CancelAll()
        {
            foreach (var player in occupants.Keys.ToArray())
                if (player != null) player.Leave(this, QusapExtractionCancellation.ZoneDisabled);
            occupants.Clear();
        }
    }
}
