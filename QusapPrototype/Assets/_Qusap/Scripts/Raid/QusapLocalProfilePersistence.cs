using System;
using System.IO;
using System.Linq;
using UnityEngine;

namespace Qusap
{
    [DisallowMultipleComponent]
    [RequireComponent(typeof(QusapRaidBootstrap))]
    public sealed class QusapLocalProfilePersistence : MonoBehaviour
    {
        [SerializeField] private string localParticipantId = "P1";
        public QusapPersistentStashRepository Repository { get; private set; }
        public string IsolatedPlaygroundDirectory { get; private set; }
        public void ConfigureIsolatedPlayground(string directory)
        {
            if (Repository != null || GetComponent<QusapRaidLoadoutPlayground>() == null
                || !Path.IsPathRooted(directory) || Path.GetFullPath(directory) == Path.GetFullPath(ProductionDirectory))
                throw new InvalidOperationException("An isolated diagnostic playground directory is required");
            IsolatedPlaygroundDirectory = directory;
        }
#if UNITY_EDITOR
        // Test fixture injection. No persisted editor preference and no production path is touched.
        public static string IsolatedStorageDirectory { get; set; }
#endif
        public static string ProductionDirectory => Path.Combine(Application.persistentDataPath, "Qusap");

        private static string StorageDirectory()
        {
#if UNITY_EDITOR
            if (!string.IsNullOrWhiteSpace(IsolatedStorageDirectory)) return IsolatedStorageDirectory;
            string[] args = Environment.GetCommandLineArgs();
            int index = Array.IndexOf(args, "-qusapProfileDirectory");
            if (index >= 0 && index + 1 < args.Length) return args[index + 1];
            if (args.Contains("-runTests")) throw new InvalidOperationException("Tests must inject an isolated profile directory.");
#endif
            return ProductionDirectory;
        }

        public void Initialize(QusapRaidBootstrap raid)
        {
            if (Repository != null) return;
            Repository = new QusapPersistentStashRepository(IsolatedPlaygroundDirectory ?? StorageDirectory(), raid.Definitions);
            var local = raid.Participants.Single(p => p.ParticipantId == localParticipantId);
            local.Configure(local.ParticipantId, Repository.ProfileId, local.BackpackCapacity);
            var weapons = FindAnyObjectByType<QusapWeaponMatchBootstrap>();
            if (weapons != null)
            {
                weapons.ReserveWeaponInstanceIdsThrough(Repository.NextNativeWeaponInstanceId - 1);
                Repository.ObserveNativeAllocator(() => weapons.NextWeaponInstanceId);
            }
            Debug.Log("LOCAL_PROFILE_LOAD " + Repository.LastResult + " | " + Repository.Storage.MainPath);
        }
        private void OnApplicationQuit() => Repository?.ConfirmUnchanged();
    }
}
