using System;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace Qusap.Tests
{
    public sealed class QusapWeaponVisualPrefabEditModeTests
    {
        private const string MaterialFolder = "Assets/_Qusap/Art/Weapons/Materials";
        private const string GeneratedFolder = MaterialFolder + "/Generated";
        private const string PrefabFolder = "Assets/_Qusap/Prefabs/Weapons";

        private static readonly WeaponAssetCase[] Cases =
        {
            new(
                "Blue",
                "Assets/_Qusap/Art/Weapons/Models/Meshy_AI_qusap_sword_blue_3d_v_0909081140_image-to-3d-texture_fbx/Meshy_AI_qusap_sword_blue_3d_v_0909081140_image-to-3d-texture",
                true),
            new(
                "Purple",
                "Assets/_Qusap/Art/Weapons/Models/Meshy_AI_qusap_sword_purple_3d_0909081134_image-to-3d-texture_fbx/Meshy_AI_qusap_sword_purple_3d_0909081134_image-to-3d-texture",
                false),
            new(
                "White",
                "Assets/_Qusap/Art/Weapons/Models/Meshy_AI_qusap_sword_white_3d_0909081105_image-to-3d-texture_fbx/Meshy_AI_qusap_sword_white_3d_0909081105_image-to-3d-texture",
                false)
        };

        [Test]
        public void ImportedWeaponModelsAreRenderableStaticAndFinite()
        {
            foreach (WeaponAssetCase item in Cases)
            {
                GameObject model = AssetDatabase.LoadAssetAtPath<GameObject>(item.ModelPath);
                Assert.That(model, Is.Not.Null, item.ModelPath);
                Assert.That(model.GetComponentsInChildren<Animator>(true), Is.Empty, item.ModelPath);
                Assert.That(
                    Array.FindAll(
                        AssetDatabase.LoadAllAssetsAtPath(item.ModelPath),
                        asset => asset is Avatar || asset is AnimationClip),
                    Is.Empty,
                    item.ModelPath);

                ModelImporter importer = AssetImporter.GetAtPath(item.ModelPath) as ModelImporter;
                Assert.That(importer, Is.Not.Null, item.ModelPath);
                Assert.That(importer.animationType, Is.EqualTo(ModelImporterAnimationType.None));
                Assert.That(importer.importAnimation, Is.False);
                Assert.That(importer.importCameras, Is.False);
                Assert.That(importer.importLights, Is.False);
                Assert.That(importer.materialImportMode, Is.EqualTo(ModelImporterMaterialImportMode.None));

                Renderer[] renderers = model.GetComponentsInChildren<Renderer>(true);
                MeshFilter[] filters = model.GetComponentsInChildren<MeshFilter>(true);
                Assert.That(renderers, Is.Not.Empty, item.ModelPath);
                Assert.That(filters, Is.Not.Empty, item.ModelPath);
                foreach (MeshFilter filter in filters)
                {
                    Mesh mesh = filter.sharedMesh;
                    Assert.That(mesh, Is.Not.Null, item.ModelPath);
                    Assert.That(mesh.vertexCount, Is.GreaterThan(0), item.ModelPath);
                    Assert.That(mesh.normals, Has.Length.EqualTo(mesh.vertexCount), item.ModelPath);
                    Assert.That(mesh.uv, Has.Length.EqualTo(mesh.vertexCount), item.ModelPath);
                    AssertFiniteNonEmpty(mesh.bounds, item.ModelPath);
                }

                foreach (Transform transform in model.GetComponentsInChildren<Transform>(true))
                {
                    Assert.That(IsFinite(transform.localPosition), Is.True, item.ModelPath);
                    Assert.That(IsFinite(transform.localEulerAngles), Is.True, item.ModelPath);
                    Assert.That(IsFinite(transform.localScale), Is.True, item.ModelPath);
                }
            }
        }

        [Test]
        public void AllThreeVisualPrefabsExist()
        {
            foreach (WeaponAssetCase item in Cases)
            {
                Assert.That(
                    AssetDatabase.LoadAssetAtPath<GameObject>(item.PrefabPath),
                    Is.Not.Null,
                    item.PrefabPath);
            }
        }

        [Test]
        public void EachVisualPrefabContainsRenderableMesh()
        {
            ForEachPrefab((item, root) =>
            {
                Renderer[] renderers = root.GetComponentsInChildren<Renderer>(true);
                MeshFilter[] filters = root.GetComponentsInChildren<MeshFilter>(true);
                Assert.That(renderers, Is.Not.Empty, item.PrefabPath);
                Assert.That(filters, Is.Not.Empty, item.PrefabPath);
                foreach (MeshFilter filter in filters)
                {
                    Assert.That(filter.sharedMesh, Is.Not.Null, item.PrefabPath);
                    Assert.That(filter.sharedMesh.vertexCount, Is.GreaterThan(0), item.PrefabPath);
                }
            });
        }

        [Test]
        public void EachVisualPrefabUsesValidUrpMaterialAndTextures()
        {
            ForEachPrefab((item, root) =>
            {
                Material expected = AssetDatabase.LoadAssetAtPath<Material>(item.MaterialPath);
                Assert.That(expected, Is.Not.Null, item.MaterialPath);
                Assert.That(expected.shader, Is.Not.Null, item.MaterialPath);
                Assert.That(expected.shader.name, Is.EqualTo("Universal Render Pipeline/Lit"));
                Assert.That(
                    AssetDatabase.GetAssetPath(expected.GetTexture("_BaseMap")),
                    Is.EqualTo(item.BaseColorPath));
                Assert.That(
                    AssetDatabase.GetAssetPath(expected.GetTexture("_BumpMap")),
                    Is.EqualTo(item.NormalPath));
                Assert.That(
                    AssetDatabase.GetAssetPath(expected.GetTexture("_MetallicGlossMap")),
                    Is.EqualTo(item.MetallicSmoothnessPath));
                Assert.That(expected.IsKeywordEnabled("_NORMALMAP"), Is.True);
                Assert.That(expected.IsKeywordEnabled("_METALLICSPECGLOSSMAP"), Is.True);
                Assert.That(expected.GetFloat("_Smoothness"), Is.EqualTo(1f));

                TextureImporter normalImporter = AssetImporter.GetAtPath(item.NormalPath)
                    as TextureImporter;
                TextureImporter metallicImporter = AssetImporter.GetAtPath(item.MetallicPath)
                    as TextureImporter;
                TextureImporter roughnessImporter = AssetImporter.GetAtPath(item.RoughnessPath)
                    as TextureImporter;
                TextureImporter packedImporter = AssetImporter.GetAtPath(item.MetallicSmoothnessPath)
                    as TextureImporter;
                Assert.That(normalImporter.textureType, Is.EqualTo(TextureImporterType.NormalMap));
                Assert.That(metallicImporter.sRGBTexture, Is.False);
                Assert.That(roughnessImporter.sRGBTexture, Is.False);
                Assert.That(packedImporter.sRGBTexture, Is.False);

                if (item.HasEmission)
                {
                    Assert.That(
                        AssetDatabase.GetAssetPath(expected.GetTexture("_EmissionMap")),
                        Is.EqualTo(item.EmissionPath));
                    Assert.That(expected.IsKeywordEnabled("_EMISSION"), Is.True);
                }
                else
                {
                    Assert.That(expected.GetTexture("_EmissionMap"), Is.Null);
                    Assert.That(expected.IsKeywordEnabled("_EMISSION"), Is.False);
                }

                foreach (Renderer renderer in root.GetComponentsInChildren<Renderer>(true))
                {
                    Assert.That(renderer.sharedMaterials, Is.Not.Empty, item.PrefabPath);
                    foreach (Material material in renderer.sharedMaterials)
                    {
                        Assert.That(material, Is.SameAs(expected), item.PrefabPath);
                    }
                }
            });
        }

        [Test]
        public void VisualPrefabsContainNoRigidbody()
        {
            ForEachPrefab((item, root) =>
                Assert.That(root.GetComponentsInChildren<Rigidbody>(true), Is.Empty, item.PrefabPath));
        }

        [Test]
        public void VisualPrefabsContainNoColliders()
        {
            ForEachPrefab((item, root) =>
                Assert.That(root.GetComponentsInChildren<Collider>(true), Is.Empty, item.PrefabPath));
        }

        [Test]
        public void VisualPrefabsContainNoAnimator()
        {
            ForEachPrefab((item, root) =>
            {
                Assert.That(root.GetComponentsInChildren<Animator>(true), Is.Empty, item.PrefabPath);
                Assert.That(root.GetComponentsInChildren<Animation>(true), Is.Empty, item.PrefabPath);
            });
        }

        [Test]
        public void VisualPrefabsContainNoHitboxDamageOrOtherBehaviour()
        {
            ForEachPrefab((item, root) =>
            {
                Assert.That(
                    root.GetComponentsInChildren<QusapAttackHitbox>(true),
                    Is.Empty,
                    item.PrefabPath);
                Assert.That(
                    root.GetComponentsInChildren<MonoBehaviour>(true),
                    Is.Empty,
                    item.PrefabPath);
                Assert.That(root.GetComponentsInChildren<Light>(true), Is.Empty, item.PrefabPath);
                Assert.That(root.GetComponentsInChildren<Camera>(true), Is.Empty, item.PrefabPath);
                Assert.That(
                    root.GetComponentsInChildren<ParticleSystem>(true),
                    Is.Empty,
                    item.PrefabPath);
            });
        }

        [Test]
        public void VisualPrefabTransformsAndBoundsAreFiniteAndCoherent()
        {
            ForEachPrefab((item, root) =>
            {
                Assert.That(root.transform.localPosition, Is.EqualTo(Vector3.zero));
                Assert.That(root.transform.localRotation, Is.EqualTo(Quaternion.identity));
                Assert.That(root.transform.localScale, Is.EqualTo(Vector3.one));
                Assert.That(root.transform.childCount, Is.EqualTo(1));

                Transform pivot = root.transform.GetChild(0);
                Assert.That(pivot.name, Is.EqualTo("VisualPivot"));
                Assert.That(pivot.localPosition, Is.EqualTo(Vector3.zero));
                Assert.That(
                    Quaternion.Angle(pivot.localRotation, Quaternion.Euler(-90f, 0f, 0f)),
                    Is.LessThan(0.001f));
                Assert.That(pivot.localScale, Is.EqualTo(Vector3.one * 100f));
                Assert.That(pivot.childCount, Is.EqualTo(1));

                Transform model = pivot.GetChild(0);
                Assert.That(model.localPosition, Is.EqualTo(Vector3.zero));
                Assert.That(model.localRotation, Is.EqualTo(Quaternion.identity));
                Assert.That(model.localScale, Is.EqualTo(Vector3.one));

                Bounds combined = default;
                bool hasBounds = false;
                foreach (Renderer renderer in root.GetComponentsInChildren<Renderer>(true))
                {
                    AssertFiniteNonEmpty(renderer.bounds, item.PrefabPath);
                    if (!hasBounds)
                    {
                        combined = renderer.bounds;
                        hasBounds = true;
                    }
                    else
                    {
                        combined.Encapsulate(renderer.bounds);
                    }
                }

                Assert.That(hasBounds, Is.True, item.PrefabPath);
                Assert.That(combined.size.y, Is.InRange(1.8f, 2.0f), item.PrefabPath);
            });
        }

        [Test]
        public void EachVisualPrefabUsesCorrespondingColorFbxAndMaterial()
        {
            ForEachPrefab((item, root) =>
            {
                Transform modelRoot = root.transform.GetChild(0).GetChild(0);
                GameObject source = PrefabUtility.GetCorrespondingObjectFromSource(modelRoot.gameObject);
                Assert.That(source, Is.Not.Null, item.PrefabPath);
                Assert.That(AssetDatabase.GetAssetPath(source), Is.EqualTo(item.ModelPath));

                Renderer renderer = root.GetComponentInChildren<Renderer>(true);
                Assert.That(
                    AssetDatabase.GetAssetPath(renderer.sharedMaterial),
                    Is.EqualTo(item.MaterialPath));
            });
        }

        [Test]
        public void RepeatedInstantiationAndDestructionLeavesNoObjects()
        {
            foreach (WeaponAssetCase item in Cases)
            {
                GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(item.PrefabPath);
                GameObject container = new($"{item.ColorName}_VisualPrefabTestContainer");
                try
                {
                    for (int i = 0; i < 5; i++)
                    {
                        GameObject instance = PrefabUtility.InstantiatePrefab(
                            prefab,
                            container.transform) as GameObject;
                        Assert.That(instance, Is.Not.Null, item.PrefabPath);
                        Assert.That(container.transform.childCount, Is.EqualTo(1));
                        UnityEngine.Object.DestroyImmediate(instance);
                        Assert.That(instance == null, Is.True);
                        Assert.That(container.transform.childCount, Is.Zero);
                    }
                }
                finally
                {
                    UnityEngine.Object.DestroyImmediate(container);
                }

                Assert.That(container == null, Is.True);
            }
        }

        private static void ForEachPrefab(Action<WeaponAssetCase, GameObject> assertion)
        {
            foreach (WeaponAssetCase item in Cases)
            {
                GameObject root = PrefabUtility.LoadPrefabContents(item.PrefabPath);
                Assert.That(root, Is.Not.Null, item.PrefabPath);
                try
                {
                    assertion(item, root);
                }
                finally
                {
                    PrefabUtility.UnloadPrefabContents(root);
                }
            }
        }

        private static void AssertFiniteNonEmpty(Bounds bounds, string context)
        {
            Assert.That(IsFinite(bounds.center), Is.True, context);
            Assert.That(IsFinite(bounds.size), Is.True, context);
            Assert.That(bounds.size.sqrMagnitude, Is.GreaterThan(0f), context);
        }

        private static bool IsFinite(Vector3 value)
        {
            return float.IsFinite(value.x)
                && float.IsFinite(value.y)
                && float.IsFinite(value.z);
        }

        private readonly struct WeaponAssetCase
        {
            public WeaponAssetCase(string colorName, string sourceStem, bool hasEmission)
            {
                ColorName = colorName;
                SourceStem = sourceStem;
                HasEmission = hasEmission;
            }

            public string ColorName { get; }
            public string SourceStem { get; }
            public bool HasEmission { get; }
            public string ModelPath => SourceStem + ".fbx";
            public string BaseColorPath => SourceStem + ".png";
            public string NormalPath => SourceStem + "_normal.png";
            public string MetallicPath => SourceStem + "_metallic.png";
            public string RoughnessPath => SourceStem + "_roughness.png";
            public string EmissionPath => SourceStem + "_emission.png";
            public string MetallicSmoothnessPath =>
                $"{GeneratedFolder}/QusapSword{ColorName}_MetallicSmoothness.png";
            public string MaterialPath =>
                $"{MaterialFolder}/QusapSword{ColorName}Material.mat";
            public string PrefabPath => $"{PrefabFolder}/QusapSword{ColorName}Visual.prefab";
        }
    }
}
