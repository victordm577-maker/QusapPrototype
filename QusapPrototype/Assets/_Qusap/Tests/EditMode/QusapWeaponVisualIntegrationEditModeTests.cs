using System.Linq;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Qusap.Tests
{
    public sealed class QusapWeaponVisualIntegrationEditModeTests
    {
        private const string CatalogPath =
            "Assets/_Qusap/Settings/Combat/Weapons/QusapWeaponVisualCatalog.asset";
        private const string PlayerPrefabPath =
            "Assets/_Qusap/Prefabs/QusapCombatPlayer.prefab";
        private const string OfficialScenePath =
            "Assets/_Qusap/Scenes/CombatPlayground.unity";

        [Test]
        public void CatalogRegistersBluePurpleAndWhiteVisuals()
        {
            QusapWeaponVisualCatalog catalog = LoadCatalog();
            Assert.That(catalog.Count, Is.EqualTo(3));

            AssertEntry(
                catalog,
                QusapWeaponVisualCatalog.BlueDefinitionId,
                "Blue Sword",
                "Assets/_Qusap/Prefabs/Weapons/QusapSwordBlueVisual.prefab");
            AssertEntry(
                catalog,
                QusapWeaponVisualCatalog.PurpleDefinitionId,
                "Purple Sword",
                "Assets/_Qusap/Prefabs/Weapons/QusapSwordPurpleVisual.prefab");
            AssertEntry(
                catalog,
                QusapWeaponVisualCatalog.WhiteDefinitionId,
                "White Sword",
                "Assets/_Qusap/Prefabs/Weapons/QusapSwordWhiteVisual.prefab");
        }

        [Test]
        public void CanonicalPlayerHasOneEquipmentPresenterAndSocket()
        {
            GameObject root = PrefabUtility.LoadPrefabContents(PlayerPrefabPath);
            try
            {
                QusapWeaponEquipment[] equipment =
                    root.GetComponentsInChildren<QusapWeaponEquipment>(true);
                QusapEquippedWeaponPresenter[] presenters =
                    root.GetComponentsInChildren<QusapEquippedWeaponPresenter>(true);
                Transform[] sockets = root.GetComponentsInChildren<Transform>(true)
                    .Where(candidate => candidate.name == "WeaponSocket")
                    .ToArray();

                Assert.That(equipment, Has.Length.EqualTo(1));
                Assert.That(presenters, Has.Length.EqualTo(1));
                Assert.That(sockets, Has.Length.EqualTo(1));
                Assert.That(equipment[0].gameObject, Is.SameAs(root));
                Assert.That(presenters[0].gameObject, Is.SameAs(root));
                Assert.That(sockets[0].parent, Is.SameAs(root.transform));
                Assert.That(sockets[0].childCount, Is.Zero,
                    "The canonical prefab must not bake an initial sword instance.");
            }
            finally
            {
                PrefabUtility.UnloadPrefabContents(root);
            }
        }

        [Test]
        public void CanonicalPresenterUsesOnlyLocalSerializedReferences()
        {
            GameObject root = PrefabUtility.LoadPrefabContents(PlayerPrefabPath);
            try
            {
                QusapEquippedWeaponPresenter presenter =
                    root.GetComponent<QusapEquippedWeaponPresenter>();
                Assert.That(presenter, Is.Not.Null);
                Assert.That(presenter.Equipment,
                    Is.SameAs(root.GetComponent<QusapWeaponEquipment>()));
                Assert.That(presenter.CombatController,
                    Is.SameAs(root.GetComponent<QusapCombatController>()));
                Assert.That(presenter.Catalog, Is.SameAs(LoadCatalog()));
                Assert.That(presenter.WeaponSocket.parent, Is.SameAs(root.transform));
                Assert.That(presenter.SocketOffset,
                    Is.EqualTo(QusapEquippedWeaponPresenter.DefaultSocketOffset));
                Assert.That(presenter.SocketEulerAngles,
                    Is.EqualTo(QusapEquippedWeaponPresenter.DefaultSocketEulerAngles));
                Assert.That(presenter.VisualScale,
                    Is.EqualTo(QusapEquippedWeaponPresenter.DefaultVisualScale));
            }
            finally
            {
                PrefabUtility.UnloadPrefabContents(root);
            }
        }

        [Test]
        public void OfficialSceneHasOneExplicitlyConfiguredMatchBootstrap()
        {
            WithOfficialScene(scene =>
            {
                QusapWeaponMatchBootstrap[] bootstraps = scene.GetRootGameObjects()
                    .SelectMany(root =>
                        root.GetComponentsInChildren<QusapWeaponMatchBootstrap>(true))
                    .ToArray();
                Assert.That(bootstraps, Has.Length.EqualTo(1));

                QusapWeaponMatchBootstrap bootstrap = bootstraps[0];
                Assert.That(bootstrap.Catalog, Is.SameAs(LoadCatalog()));
                Assert.That(bootstrap.PlayerOneEquipment, Is.Not.Null);
                Assert.That(bootstrap.PlayerOnePresenter, Is.Not.Null);
                Assert.That(bootstrap.PlayerTwoEquipment, Is.Not.Null);
                Assert.That(bootstrap.PlayerTwoPresenter, Is.Not.Null);
                Assert.That(bootstrap.PlayerOneEquipment,
                    Is.Not.SameAs(bootstrap.PlayerTwoEquipment));
                Assert.That(bootstrap.PlayerOnePresenter,
                    Is.Not.SameAs(bootstrap.PlayerTwoPresenter));
                Assert.That(bootstrap.PlayerOneEquipment.gameObject,
                    Is.SameAs(bootstrap.PlayerOnePresenter.gameObject));
                Assert.That(bootstrap.PlayerTwoEquipment.gameObject,
                    Is.SameAs(bootstrap.PlayerTwoPresenter.gameObject));
                Assert.That(bootstrap.PickupRadius,
                    Is.EqualTo(QusapWeaponPickupResolver.DefaultPickupRadius));
                Assert.That(bootstrap.PreviousOwnerPickupLockout,
                    Is.EqualTo(QusapWeaponPickupResolver.DefaultPreviousOwnerLockout));
            });
        }

        [Test]
        public void OfficialSceneIdentifiesPlayerOneAndPlayerTwoByInputSlots()
        {
            WithOfficialScene(scene =>
            {
                QusapWeaponMatchBootstrap bootstrap = scene.GetRootGameObjects()
                    .SelectMany(root =>
                        root.GetComponentsInChildren<QusapWeaponMatchBootstrap>(true))
                    .Single();
                QusapInputReader playerOneInput =
                    bootstrap.PlayerOneEquipment.GetComponent<QusapInputReader>();
                QusapInputReader playerTwoInput =
                    bootstrap.PlayerTwoEquipment.GetComponent<QusapInputReader>();

                Assert.That(playerOneInput.LocalPlayerSlot,
                    Is.EqualTo(QusapLocalPlayerSlot.Player1Keyboard));
                Assert.That(playerTwoInput.LocalPlayerSlot,
                    Is.EqualTo(QusapLocalPlayerSlot.Player2Gamepad));
                SerializedObject playerOneCombat = new(
                    bootstrap.PlayerOnePresenter.CombatController);
                SerializedObject playerTwoCombat = new(
                    bootstrap.PlayerTwoPresenter.CombatController);
                Assert.That(
                    playerOneCombat.FindProperty("initialFacingDirection").intValue,
                    Is.EqualTo(1));
                Assert.That(
                    playerTwoCombat.FindProperty("initialFacingDirection").intValue,
                    Is.EqualTo(-1));
            });
        }

        private static QusapWeaponVisualCatalog LoadCatalog()
        {
            QusapWeaponVisualCatalog catalog =
                AssetDatabase.LoadAssetAtPath<QusapWeaponVisualCatalog>(CatalogPath);
            Assert.That(catalog, Is.Not.Null, CatalogPath);
            return catalog;
        }

        private static void AssertEntry(
            QusapWeaponVisualCatalog catalog,
            string definitionId,
            string displayName,
            string visualPath)
        {
            Assert.That(catalog.TryGetEntry(definitionId, out QusapWeaponVisualEntry entry),
                Is.True,
                definitionId);
            Assert.That(entry.DefinitionId, Is.EqualTo(definitionId));
            Assert.That(entry.DisplayName, Is.EqualTo(displayName));
            Assert.That(AssetDatabase.GetAssetPath(entry.VisualPrefab), Is.EqualTo(visualPath));
        }

        private static void WithOfficialScene(System.Action<Scene> assertion)
        {
            Scene scene = SceneManager.GetSceneByPath(OfficialScenePath);
            bool openedForTest = !scene.IsValid() || !scene.isLoaded;
            if (openedForTest)
            {
                scene = EditorSceneManager.OpenScene(
                    OfficialScenePath,
                    OpenSceneMode.Additive);
            }

            try
            {
                assertion(scene);
            }
            finally
            {
                if (openedForTest && scene.IsValid() && scene.isLoaded)
                {
                    EditorSceneManager.CloseScene(scene, true);
                }
            }
        }
    }
}
