using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

namespace Qusap.Tests
{
    public sealed class QusapModularVisualEditModeTests
    {
        private const string ModelPath =
            "Assets/_Qusap/Art/Characters/Models/Qusap_Luz_Modular_v1.fbx";
        private const string ExpectedModelGuid = "005115c90e7786a41bbf03a058378cc7";
        private const string BaseColorPath =
            "Assets/Scenes/Art/Characters/Qusap/Final_Light_v3/T_QusapLight_v2_BaseColor_4K.png";
        private const string GltfMapPath =
            "Assets/Scenes/Art/Characters/Qusap/Final_Light_v3/T_QusapLight_v2_MetallicRoughness_GLTF_4K.png";
        private const string DerivedMapPath =
            "Assets/_Qusap/Art/Characters/Materials/Generated/T_QusapLight_v2_MetallicSmoothness_URP.png";
        private const string MaterialPath =
            "Assets/_Qusap/Art/Characters/Materials/M_Qusap_Light_Modular_URP.mat";
        private const string VisualPrefabPath =
            "Assets/_Qusap/Prefabs/Characters/QusapLuzModularVisual.prefab";
        private const string PlayerVariantPath =
            "Assets/_Qusap/Prefabs/Characters/QusapCombatPlayer_ModularVisual.prefab";

        [Test]
        public void ModelAndStableMetaExist()
        {
            Assert.That(File.Exists(ModelPath), Is.True);
            Assert.That(File.Exists(ModelPath + ".meta"), Is.True);
            Assert.That(AssetDatabase.AssetPathToGUID(ModelPath), Is.EqualTo(ExpectedModelGuid));
        }

        [Test]
        public void ModelImporterIsRigidAndNonDestructive()
        {
            var importer = AssetImporter.GetAtPath(ModelPath) as ModelImporter;
            Assert.That(importer, Is.Not.Null);
            Assert.That(importer.globalScale, Is.EqualTo(1f));
            Assert.That(importer.useFileScale, Is.True);
            Assert.That(importer.animationType, Is.EqualTo(ModelImporterAnimationType.None));
            Assert.That(importer.importAnimation, Is.False);
            Assert.That(importer.importCameras, Is.False);
            Assert.That(importer.importLights, Is.False);
            Assert.That(importer.addCollider, Is.False);
            Assert.That(importer.isReadable, Is.False);
            Assert.That(importer.preserveHierarchy, Is.True);
            Assert.That(importer.optimizeGameObjects, Is.False);
            Assert.That(importer.importNormals, Is.EqualTo(ModelImporterNormals.Import));
            Assert.That(importer.importTangents, Is.EqualTo(ModelImporterTangents.Import));
            Assert.That(importer.materialImportMode, Is.EqualTo(ModelImporterMaterialImportMode.None));
        }

        [Test]
        public void ImportedHierarchyContainsExactlyThreeRigidMeshesAndRequiredPivots()
        {
            GameObject model = AssetDatabase.LoadAssetAtPath<GameObject>(ModelPath);
            Assert.That(model, Is.Not.Null);
            Transform[] all = model.GetComponentsInChildren<Transform>(true);
            Transform root = FindUnique(all, "QusapVisualRoot");
            Transform bodyPivot = FindUnique(all, "BodyPivot");
            Transform body = FindUnique(all, "Body");
            Transform leftPivot = FindUnique(all, "FootPivot_L");
            Transform left = FindUnique(all, "FloatingFoot_L");
            Transform rightPivot = FindUnique(all, "FootPivot_R");
            Transform right = FindUnique(all, "FloatingFoot_R");

            Assert.That(bodyPivot.parent, Is.SameAs(root));
            Assert.That(leftPivot.parent, Is.SameAs(root));
            Assert.That(rightPivot.parent, Is.SameAs(root));
            Assert.That(body.parent, Is.SameAs(bodyPivot));
            Assert.That(left.parent, Is.SameAs(leftPivot));
            Assert.That(right.parent, Is.SameAs(rightPivot));
            Assert.That(model.GetComponentsInChildren<MeshFilter>(true), Has.Length.EqualTo(3));
            Assert.That(model.GetComponentsInChildren<MeshRenderer>(true), Has.Length.EqualTo(3));
            Assert.That(model.GetComponentsInChildren<SkinnedMeshRenderer>(true), Is.Empty);
            Assert.That(model.GetComponentsInChildren<Animator>(true), Is.Empty);
            Assert.That(model.GetComponentsInChildren<Collider>(true), Is.Empty);
            Assert.That(model.GetComponentsInChildren<Camera>(true), Is.Empty);
            Assert.That(model.GetComponentsInChildren<Light>(true), Is.Empty);
        }

        [Test]
        public void RigReferencesAreCompleteUniqueAndStructurallyValid()
        {
            WithPrefab(VisualPrefabPath, root =>
            {
                QusapModularVisualRig rig = root.GetComponent<QusapModularVisualRig>();
                Assert.That(rig, Is.Not.Null);
                Assert.That(rig.TryValidateReferences(out string error), Is.True, error);
                Transform[] references =
                {
                    rig.VisualRoot, rig.BodyPivot, rig.Body, rig.FootPivotLeft,
                    rig.FootLeft, rig.FootPivotRight, rig.FootRight
                };
                Assert.That(references.All(item => item != null), Is.True);
                Assert.That(references.Distinct().Count(), Is.EqualTo(references.Length));
                Assert.That(rig.NeutralPoseCaptured, Is.True);
            });
        }

        [Test]
        public void NeutralPoseRestoresExactly()
        {
            WithPrefab(VisualPrefabPath, root =>
            {
                QusapModularVisualRig rig = root.GetComponent<QusapModularVisualRig>();
                Transform[] transforms =
                {
                    rig.VisualRoot, rig.BodyPivot, rig.Body, rig.FootPivotLeft,
                    rig.FootLeft, rig.FootPivotRight, rig.FootRight
                };
                PoseSnapshot[] neutral = transforms.Select(PoseSnapshot.Capture).ToArray();
                foreach (Transform item in transforms)
                {
                    item.localPosition += new Vector3(0.17f, -0.23f, 0.31f);
                    item.localRotation *= Quaternion.Euler(9f, 17f, 23f);
                    item.localScale = Vector3.one * 1.25f;
                }

                Assert.That(rig.ResetNeutralPose(), Is.True);
                for (int i = 0; i < transforms.Length; i++)
                    neutral[i].AssertMatches(transforms[i]);
            });
        }

        [Test]
        public void VisualPrefabIsPureRigidVisualWithExpectedHeight()
        {
            WithPrefab(VisualPrefabPath, root =>
            {
                Assert.That(root.transform.localPosition, Is.EqualTo(Vector3.zero));
                Assert.That(root.transform.localRotation, Is.EqualTo(Quaternion.identity));
                Assert.That(root.transform.localScale, Is.EqualTo(Vector3.one));
                Assert.That(root.GetComponentsInChildren<Rigidbody>(true), Is.Empty);
                Assert.That(root.GetComponentsInChildren<Collider>(true), Is.Empty);
                Assert.That(root.GetComponentsInChildren<QusapAttackHitbox>(true), Is.Empty);
                Assert.That(root.GetComponentsInChildren<QusapHurtbox>(true), Is.Empty);
                Assert.That(root.GetComponentsInChildren<Animator>(true), Is.Empty);
                Assert.That(root.GetComponentsInChildren<Camera>(true), Is.Empty);
                Assert.That(root.GetComponentsInChildren<Light>(true), Is.Empty);
                Assert.That(root.GetComponentsInChildren<ParticleSystem>(true), Is.Empty);
                Assert.That(root.GetComponentsInChildren<MonoBehaviour>(true),
                    Has.Length.EqualTo(1));

                Renderer[] renderers = root.GetComponentsInChildren<Renderer>(true);
                Bounds bounds = CombinedBounds(renderers);
                Assert.That(bounds.min.y, Is.EqualTo(0f).Within(0.001f));
                Assert.That(bounds.size.y, Is.EqualTo(1.902766f).Within(0.08f));
            });
        }

        [Test]
        public void UrpMaterialIsExplicitlyAssignedToAllThreeMeshes()
        {
            Material material = AssetDatabase.LoadAssetAtPath<Material>(MaterialPath);
            Assert.That(material, Is.Not.Null);
            Assert.That(material.shader.name, Is.EqualTo("Universal Render Pipeline/Lit"));
            Assert.That(AssetDatabase.GetAssetPath(material.GetTexture("_BaseMap")), Is.EqualTo(BaseColorPath));
            Assert.That(AssetDatabase.GetAssetPath(material.GetTexture("_MetallicGlossMap")), Is.EqualTo(DerivedMapPath));
            Assert.That(material.IsKeywordEnabled("_METALLICSPECGLOSSMAP"), Is.True);
            Assert.That(material.IsKeywordEnabled("_EMISSION"), Is.False);

            WithPrefab(VisualPrefabPath, root =>
            {
                Renderer[] renderers = root.GetComponentsInChildren<Renderer>(true);
                Assert.That(renderers, Has.Length.EqualTo(3));
                foreach (Renderer renderer in renderers)
                foreach (Material assigned in renderer.sharedMaterials)
                    Assert.That(assigned, Is.SameAs(material));
            });
        }

        [Test]
        public void TextureImportColorSpacesAndAlphaSourceAreCorrect()
        {
            var baseImporter = (TextureImporter)AssetImporter.GetAtPath(BaseColorPath);
            var gltfImporter = (TextureImporter)AssetImporter.GetAtPath(GltfMapPath);
            var derivedImporter = (TextureImporter)AssetImporter.GetAtPath(DerivedMapPath);
            Assert.That(baseImporter.sRGBTexture, Is.True);
            Assert.That(gltfImporter.sRGBTexture, Is.False);
            Assert.That(derivedImporter.sRGBTexture, Is.False);
            Assert.That(derivedImporter.alphaSource, Is.EqualTo(TextureImporterAlphaSource.FromInput));
            Assert.That(derivedImporter.alphaIsTransparency, Is.False);
            Assert.That(derivedImporter.isReadable, Is.False);
        }

        [Test]
        public void DerivedMapPacksGltfMetallicAndInvertedRoughness()
        {
            Texture2D source = LoadRawPng(GltfMapPath);
            Texture2D derived = LoadRawPng(DerivedMapPath);
            try
            {
                Assert.That(derived.width, Is.EqualTo(source.width));
                Assert.That(derived.height, Is.EqualTo(source.height));
                Color32[] sourcePixels = source.GetPixels32();
                Color32[] derivedPixels = derived.GetPixels32();
                for (int y = 0; y < source.height; y += Math.Max(1, source.height / 31))
                for (int x = 0; x < source.width; x += Math.Max(1, source.width / 31))
                {
                    int index = y * source.width + x;
                    Assert.That(derivedPixels[index].r, Is.EqualTo(sourcePixels[index].b));
                    Assert.That(derivedPixels[index].a, Is.EqualTo(255 - sourcePixels[index].g));
                }
            }
            finally
            {
                Object.DestroyImmediate(source);
                Object.DestroyImmediate(derived);
            }
        }

        [Test]
        public void PlayerVariantInheritsCanonicalLogicWithoutDuplicates()
        {
            Assert.That(PrefabUtility.GetPrefabAssetType(
                AssetDatabase.LoadAssetAtPath<GameObject>(PlayerVariantPath)),
                Is.EqualTo(PrefabAssetType.Variant));
            WithPrefab(PlayerVariantPath, root =>
            {
                AssertSingleComponent<Rigidbody>(root);
                AssertSingleComponent<CapsuleCollider>(root);
                AssertSingleComponent<QusapInputReader>(root);
                AssertSingleComponent<QusapHorizontalMotor>(root);
                AssertSingleComponent<QusapVerticalMotor>(root);
                AssertSingleComponent<QusapDashMotor>(root);
                AssertSingleComponent<QusapCombatController>(root);
                AssertSingleComponent<QusapHitReceiver>(root);
                AssertSingleComponent<QusapHurtbox>(root);
                AssertSingleComponent<QusapAttackHitbox>(root);
                AssertSingleComponent<QusapWeaponEquipment>(root);
                AssertSingleComponent<QusapEquippedWeaponPresenter>(root);
                AssertSingleComponent<QusapWeaponAttackVisualPresenter>(root);
                AssertSingleComponent<QusapModularCombatVisualPresenter>(root);
                AssertSingleComponent<QusapModularVisualRig>(root);
                AssertSingleComponent<QusapModularFacingPresenter>(root);
            });
        }

        [Test]
        public void VariantUsesSeparatedAlignedVisualAndSingleWeaponSocket()
        {
            WithPrefab(PlayerVariantPath, root =>
            {
                Transform oldVisual = root.transform.Find("PlayerVisual");
                Transform backupVisual = root.transform.Find("PlayerVisual_v1_Backup");
                Assert.That(oldVisual, Is.Not.Null);
                Assert.That(oldVisual.gameObject.activeSelf, Is.False);
                Assert.That(backupVisual, Is.Not.Null);
                Assert.That(backupVisual.gameObject.activeSelf, Is.False);

                Transform alignment = root.transform.Find("PlayerVisual_ModularAlignment");
                Assert.That(alignment, Is.Not.Null);
                Assert.That(alignment.localPosition, Is.EqualTo(new Vector3(0f, -1f, 0f)));
                Assert.That(alignment.localScale, Is.EqualTo(Vector3.one));
                Transform facingPivot = alignment.Find("ModularFacingPivot");
                Assert.That(facingPivot, Is.Not.Null);
                Assert.That(facingPivot.localScale, Is.EqualTo(Vector3.one));
                Transform capturePivot = facingPivot.Find("CombatFacingCapturePivot");
                Assert.That(capturePivot, Is.Not.Null);
                Assert.That(capturePivot.localScale, Is.EqualTo(Vector3.one));
                Assert.That(capturePivot.GetComponentInChildren<QusapModularVisualRig>(true), Is.Not.Null);

                Transform[] sockets = root.GetComponentsInChildren<Transform>(true)
                    .Where(item => item.name == "WeaponSocket").ToArray();
                Assert.That(sockets, Has.Length.EqualTo(1));
                Assert.That(sockets[0].gameObject.activeInHierarchy, Is.True);
                Assert.That(root.GetComponent<QusapAnimationDriver>().enabled, Is.False);
            });
        }

        private static void AssertSingleComponent<T>(GameObject root) where T : Component
        {
            Assert.That(root.GetComponentsInChildren<T>(true), Has.Length.EqualTo(1), typeof(T).Name);
        }

        private static Transform FindUnique(IEnumerable<Transform> transforms, string name)
        {
            Transform[] matches = transforms.Where(item => item.name == name).ToArray();
            Assert.That(matches, Has.Length.EqualTo(1), name);
            return matches[0];
        }

        private static void WithPrefab(string path, Action<GameObject> assertion)
        {
            GameObject root = PrefabUtility.LoadPrefabContents(path);
            try
            {
                assertion(root);
            }
            finally
            {
                PrefabUtility.UnloadPrefabContents(root);
            }
        }

        private static Bounds CombinedBounds(IReadOnlyList<Renderer> renderers)
        {
            Assert.That(renderers, Is.Not.Empty);
            Bounds bounds = renderers[0].bounds;
            for (int i = 1; i < renderers.Count; i++)
                bounds.Encapsulate(renderers[i].bounds);
            return bounds;
        }

        private static Texture2D LoadRawPng(string path)
        {
            var texture = new Texture2D(2, 2, TextureFormat.RGBA32, false, true);
            Assert.That(ImageConversion.LoadImage(texture, File.ReadAllBytes(path), false), Is.True);
            return texture;
        }

        private readonly struct PoseSnapshot
        {
            private PoseSnapshot(Vector3 position, Quaternion rotation, Vector3 scale)
            {
                positionValue = position;
                rotationValue = rotation;
                scaleValue = scale;
            }

            private readonly Vector3 positionValue;
            private readonly Quaternion rotationValue;
            private readonly Vector3 scaleValue;

            public static PoseSnapshot Capture(Transform transform)
            {
                return new PoseSnapshot(
                    transform.localPosition,
                    transform.localRotation,
                    transform.localScale);
            }

            public void AssertMatches(Transform transform)
            {
                Assert.That(transform.localPosition, Is.EqualTo(positionValue));
                Assert.That(transform.localRotation, Is.EqualTo(rotationValue));
                Assert.That(transform.localScale, Is.EqualTo(scaleValue));
            }
        }
    }
}
