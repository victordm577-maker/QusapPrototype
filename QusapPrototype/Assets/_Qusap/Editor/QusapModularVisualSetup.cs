using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Qusap;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

namespace Qusap.Editor
{
    public static class QusapModularVisualSetup
    {
        public const string BaseColorPath =
            "Assets/Scenes/Art/Characters/Qusap/Final_Light_v3/T_QusapLight_v2_BaseColor_4K.png";
        public const string GltfMetallicRoughnessPath =
            "Assets/Scenes/Art/Characters/Qusap/Final_Light_v3/T_QusapLight_v2_MetallicRoughness_GLTF_4K.png";
        public const string GeneratedMapPath =
            "Assets/_Qusap/Art/Characters/Materials/Generated/T_QusapLight_v2_MetallicSmoothness_URP.png";
        public const string MaterialPath =
            "Assets/_Qusap/Art/Characters/Materials/M_Qusap_Light_Modular_URP.mat";
        public const string VisualPrefabPath =
            "Assets/_Qusap/Prefabs/Characters/QusapLuzModularVisual.prefab";
        public const string PlayerVariantPath =
            "Assets/_Qusap/Prefabs/Characters/QusapCombatPlayer_ModularVisual.prefab";
        public const string CanonicalPlayerPath =
            "Assets/_Qusap/Prefabs/QusapCombatPlayer.prefab";
        public const string OfficialScenePath =
            "Assets/_Qusap/Scenes/CombatPlayground.unity";
        public const string PreviewScenePath =
            "Assets/_Qusap/Scenes/CombatPlayground_ModularVisualPreview.unity";

        private const float ExpectedHeight = 1.902766f;
        private const float HeightTolerance = 0.08f;

        [MenuItem("Qusap/10C.2A/Build Modular Visual Integration")]
        public static void BuildAll()
        {
            try
            {
                EnsureFolders();
                ConfigureSourceTextures();
                AssetDatabase.ImportAsset(
                    QusapModularModelImportPostprocessor.ModelPath,
                    ImportAssetOptions.ForceSynchronousImport | ImportAssetOptions.ForceUpdate);

                GameObject importedModel = AssetDatabase.LoadAssetAtPath<GameObject>(
                    QusapModularModelImportPostprocessor.ModelPath);
                if (importedModel == null)
                    throw new InvalidOperationException("Unity did not import the modular FBX as a GameObject.");

                ImportedHierarchy hierarchy = ValidateImportedHierarchy(importedModel);
                Texture2D metallicSmoothness = BuildMetallicSmoothnessMap();
                Material material = BuildMaterial(metallicSmoothness);
                GameObject visualPrefab = BuildVisualPrefab(importedModel, hierarchy, material);
                GameObject playerVariant = BuildPlayerVariant(visualPrefab);
                BuildPreviewScene(playerVariant);
                ValidateGeneratedAssets();

                AssetDatabase.SaveAssets();
                AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
                Debug.Log("ETAPA 10C.2A-UNITY build completed successfully.");
            }
            catch (Exception exception)
            {
                Debug.LogException(exception);
                if (Application.isBatchMode)
                    EditorApplication.Exit(1);
                throw;
            }
        }

        public static void BuildAllAndExit()
        {
            try
            {
                BuildAll();
                EditorApplication.Exit(0);
            }
            catch
            {
                EditorApplication.Exit(1);
                throw;
            }
        }

        private static void EnsureFolders()
        {
            EnsureFolder("Assets/_Qusap/Art/Characters/Materials");
            EnsureFolder("Assets/_Qusap/Art/Characters/Materials/Generated");
            EnsureFolder("Assets/_Qusap/Prefabs/Characters");
        }

        private static void EnsureFolder(string path)
        {
            string[] parts = path.Split('/');
            string current = parts[0];
            for (int i = 1; i < parts.Length; i++)
            {
                string next = current + "/" + parts[i];
                if (!AssetDatabase.IsValidFolder(next))
                    AssetDatabase.CreateFolder(current, parts[i]);
                current = next;
            }
        }

        private static void ConfigureSourceTextures()
        {
            ConfigureTextureImporter(BaseColorPath, true, false, TextureImporterAlphaSource.FromInput);
            ConfigureTextureImporter(
                GltfMetallicRoughnessPath,
                false,
                false,
                TextureImporterAlphaSource.None);
        }

        private static void ConfigureTextureImporter(
            string path,
            bool sRgb,
            bool readable,
            TextureImporterAlphaSource alphaSource)
        {
            var importer = AssetImporter.GetAtPath(path) as TextureImporter;
            if (importer == null)
                throw new InvalidOperationException($"TextureImporter not found for '{path}'.");

            bool changed = importer.sRGBTexture != sRgb
                || importer.isReadable != readable
                || importer.alphaSource != alphaSource;
            importer.sRGBTexture = sRgb;
            importer.isReadable = readable;
            importer.alphaSource = alphaSource;
            if (changed)
                importer.SaveAndReimport();
        }

        private static Texture2D BuildMetallicSmoothnessMap()
        {
            byte[] sourceBytes = File.ReadAllBytes(ToAbsolutePath(GltfMetallicRoughnessPath));
            var source = new Texture2D(2, 2, TextureFormat.RGBA32, false, true);
            if (!ImageConversion.LoadImage(source, sourceBytes, false))
                throw new InvalidOperationException("Could not decode the glTF metallic/roughness PNG.");

            Color32[] sourcePixels = source.GetPixels32();
            var output = new Texture2D(source.width, source.height, TextureFormat.RGBA32, false, true);
            var outputPixels = new Color32[sourcePixels.Length];
            for (int i = 0; i < sourcePixels.Length; i++)
            {
                byte metallic = sourcePixels[i].b;
                byte smoothness = (byte)(255 - sourcePixels[i].g);
                outputPixels[i] = new Color32(metallic, 0, 0, smoothness);
            }

            output.SetPixels32(outputPixels);
            output.Apply(false, false);
            File.WriteAllBytes(ToAbsolutePath(GeneratedMapPath), output.EncodeToPNG());
            Object.DestroyImmediate(source);
            Object.DestroyImmediate(output);

            AssetDatabase.ImportAsset(
                GeneratedMapPath,
                ImportAssetOptions.ForceSynchronousImport | ImportAssetOptions.ForceUpdate);
            var importer = (TextureImporter)AssetImporter.GetAtPath(GeneratedMapPath);
            importer.textureType = TextureImporterType.Default;
            importer.sRGBTexture = false;
            importer.alphaSource = TextureImporterAlphaSource.FromInput;
            importer.alphaIsTransparency = false;
            importer.isReadable = false;
            importer.mipmapEnabled = true;
            importer.textureCompression = TextureImporterCompression.Uncompressed;
            importer.maxTextureSize = 4096;
            importer.SaveAndReimport();
            return AssetDatabase.LoadAssetAtPath<Texture2D>(GeneratedMapPath);
        }

        private static Material BuildMaterial(Texture2D metallicSmoothness)
        {
            Shader shader = Shader.Find("Universal Render Pipeline/Lit");
            if (shader == null)
                throw new InvalidOperationException("URP Lit shader is not available.");

            Material material = AssetDatabase.LoadAssetAtPath<Material>(MaterialPath);
            if (material == null)
            {
                material = new Material(shader) { name = "M_Qusap_Light_Modular_URP" };
                AssetDatabase.CreateAsset(material, MaterialPath);
            }
            else
            {
                material.shader = shader;
            }

            Texture2D baseColor = AssetDatabase.LoadAssetAtPath<Texture2D>(BaseColorPath);
            material.SetTexture("_BaseMap", baseColor);
            material.SetColor("_BaseColor", Color.white);
            material.SetTexture("_MetallicGlossMap", metallicSmoothness);
            material.SetFloat("_Metallic", 1f);
            material.SetFloat("_Smoothness", 1f);
            material.SetFloat("_SmoothnessTextureChannel", 0f);
            material.SetFloat("_Surface", 0f);
            material.SetFloat("_AlphaClip", 0f);
            material.SetFloat("_EmissionEnabled", 0f);
            material.DisableKeyword("_EMISSION");
            material.EnableKeyword("_METALLICSPECGLOSSMAP");
            EditorUtility.SetDirty(material);
            return material;
        }

        private static ImportedHierarchy ValidateImportedHierarchy(GameObject importedModel)
        {
            Transform[] transforms = importedModel.GetComponentsInChildren<Transform>(true);
            Transform visualRoot = FindUnique(transforms, "QusapVisualRoot");
            Transform bodyPivot = FindUnique(transforms, "BodyPivot");
            Transform body = FindUnique(transforms, "Body");
            Transform footPivotLeft = FindUnique(transforms, "FootPivot_L");
            Transform footLeft = FindUnique(transforms, "FloatingFoot_L");
            Transform footPivotRight = FindUnique(transforms, "FootPivot_R");
            Transform footRight = FindUnique(transforms, "FloatingFoot_R");

            if (bodyPivot.parent != visualRoot || footPivotLeft.parent != visualRoot
                || footPivotRight.parent != visualRoot || body.parent != bodyPivot
                || footLeft.parent != footPivotLeft || footRight.parent != footPivotRight)
            {
                throw new InvalidOperationException("The imported FBX hierarchy was flattened or rearranged.");
            }

            MeshFilter[] meshes = importedModel.GetComponentsInChildren<MeshFilter>(true);
            if (meshes.Length != 3 || importedModel.GetComponentsInChildren<SkinnedMeshRenderer>(true).Length != 0)
                throw new InvalidOperationException($"Expected three rigid meshes; found {meshes.Length} MeshFilters.");
            if (importedModel.GetComponentsInChildren<Animator>(true).Length != 0
                || importedModel.GetComponentsInChildren<Collider>(true).Length != 0
                || importedModel.GetComponentsInChildren<Rigidbody>(true).Length != 0
                || importedModel.GetComponentsInChildren<Camera>(true).Length != 0
                || importedModel.GetComponentsInChildren<Light>(true).Length != 0)
            {
                throw new InvalidOperationException("The imported FBX contains a forbidden component.");
            }

            Debug.Log("Imported hierarchy: QusapVisualRoot/{BodyPivot/Body, "
                + "FootPivot_L/FloatingFoot_L, FootPivot_R/FloatingFoot_R}.");
            return new ImportedHierarchy(
                visualRoot, bodyPivot, body, footPivotLeft, footLeft, footPivotRight, footRight);
        }

        private static GameObject BuildVisualPrefab(
            GameObject importedModel,
            ImportedHierarchy importedHierarchy,
            Material material)
        {
            var root = new GameObject("QusapLuzModularVisual");
            GameObject modelInstance = (GameObject)PrefabUtility.InstantiatePrefab(importedModel);
            modelInstance.transform.SetParent(root.transform, false);

            Transform[] instanceTransforms = modelInstance.GetComponentsInChildren<Transform>(true);
            Transform visualRoot = FindUnique(instanceTransforms, importedHierarchy.VisualRoot.name);
            Transform bodyPivot = FindUnique(instanceTransforms, importedHierarchy.BodyPivot.name);
            Transform body = FindUnique(instanceTransforms, importedHierarchy.Body.name);
            Transform footPivotLeft = FindUnique(instanceTransforms, importedHierarchy.FootPivotLeft.name);
            Transform footLeft = FindUnique(instanceTransforms, importedHierarchy.FootLeft.name);
            Transform footPivotRight = FindUnique(instanceTransforms, importedHierarchy.FootPivotRight.name);
            Transform footRight = FindUnique(instanceTransforms, importedHierarchy.FootRight.name);

            Renderer[] renderers = { body.GetComponent<Renderer>(), footLeft.GetComponent<Renderer>(), footRight.GetComponent<Renderer>() };
            foreach (Renderer renderer in renderers)
            {
                if (renderer == null)
                    throw new InvalidOperationException("A modular mesh has no Renderer.");
                renderer.sharedMaterials = Enumerable.Repeat(material, renderer.sharedMaterials.Length).ToArray();
            }

            Bounds bounds = CalculateBounds(renderers);
            modelInstance.transform.localPosition -= new Vector3(bounds.center.x, bounds.min.y, bounds.center.z);
            Bounds normalizedBounds = CalculateBounds(renderers);
            if (Mathf.Abs(normalizedBounds.size.y - ExpectedHeight) > HeightTolerance)
            {
                throw new InvalidOperationException(
                    $"Imported visual height is {normalizedBounds.size.y:F6}; expected approximately {ExpectedHeight:F6}.");
            }

            QusapModularVisualRig rig = root.AddComponent<QusapModularVisualRig>();
            rig.Configure(visualRoot, bodyPivot, body, footPivotLeft, footLeft, footPivotRight, footRight);
            if (!rig.TryValidateReferences(out string error))
                throw new InvalidOperationException(error);

            GameObject prefab = PrefabUtility.SaveAsPrefabAsset(root, VisualPrefabPath);
            Object.DestroyImmediate(root);
            if (prefab == null)
                throw new InvalidOperationException("Could not save the modular visual prefab.");

            Debug.Log($"Modular visual height: {normalizedBounds.size.y:F6}; normalized bottom: {normalizedBounds.min.y:F6}.");
            return prefab;
        }

        private static GameObject BuildPlayerVariant(GameObject visualPrefab)
        {
            GameObject canonical = AssetDatabase.LoadAssetAtPath<GameObject>(CanonicalPlayerPath);
            if (canonical == null)
                throw new InvalidOperationException("Canonical player prefab is missing.");

            GameObject instance = (GameObject)PrefabUtility.InstantiatePrefab(canonical);
            instance.name = "QusapCombatPlayer_ModularVisual";
            foreach (Transform child in instance.transform)
            {
                if (child.name == "PlayerVisual" || child.name == "PlayerVisual_v1_Backup")
                    child.gameObject.SetActive(false);
            }

            var alignment = new GameObject("PlayerVisual_ModularAlignment");
            alignment.transform.SetParent(instance.transform, false);
            alignment.transform.localPosition = new Vector3(0f, -1f, 0f);
            alignment.transform.localRotation = Quaternion.identity;
            alignment.transform.localScale = Vector3.one;

            var orientation = new GameObject("ModularFacingPivot");
            orientation.transform.SetParent(alignment.transform, false);
            orientation.transform.localPosition = Vector3.zero;
            orientation.transform.localRotation = Quaternion.identity;
            orientation.transform.localScale = Vector3.one;

            GameObject visual = (GameObject)PrefabUtility.InstantiatePrefab(visualPrefab, orientation.transform);
            visual.name = "QusapLuzModularVisual";
            visual.transform.SetLocalPositionAndRotation(Vector3.zero, Quaternion.identity);
            visual.transform.localScale = Vector3.one;

            QusapCombatController combat = instance.GetComponent<QusapCombatController>();
            QusapModularFacingPresenter facing = instance.AddComponent<QusapModularFacingPresenter>();
            facing.Configure(combat, orientation.transform, 60f, -60f);

            QusapAnimationDriver animationDriver = instance.GetComponent<QusapAnimationDriver>();
            if (animationDriver != null)
                animationDriver.enabled = false;
            QusapHitReactionVisual hitReaction = instance.GetComponent<QusapHitReactionVisual>();
            if (hitReaction != null)
                hitReaction.Configure(alignment.transform, visual.GetComponentsInChildren<Renderer>(true));

            GameObject variant = PrefabUtility.SaveAsPrefabAsset(instance, PlayerVariantPath);
            Object.DestroyImmediate(instance);
            if (variant == null || PrefabUtility.GetPrefabAssetType(variant) != PrefabAssetType.Variant)
                throw new InvalidOperationException("The modular player asset was not saved as a prefab variant.");
            return variant;
        }

        private static void BuildPreviewScene(GameObject playerVariant)
        {
            Scene source = EditorSceneManager.OpenScene(OfficialScenePath, OpenSceneMode.Single);
            if (!EditorSceneManager.SaveScene(source, PreviewScenePath, true))
                throw new InvalidOperationException("Could not create the modular preview scene copy.");

            Scene preview = SceneManager.GetActiveScene();
            List<GameObject> players = preview.GetRootGameObjects()
                .Where(root => PrefabUtility.GetPrefabAssetPathOfNearestInstanceRoot(root) == CanonicalPlayerPath)
                .ToList();
            if (players.Count != 2)
                throw new InvalidOperationException($"Expected two canonical player instances; found {players.Count}.");

            foreach (GameObject player in players)
            {
                PrefabUtility.ReplacePrefabAssetOfPrefabInstance(
                    player,
                    playerVariant,
                    InteractionMode.AutomatedAction);
            }

            EditorSceneManager.MarkSceneDirty(preview);
            if (!EditorSceneManager.SaveScene(preview, PreviewScenePath))
                throw new InvalidOperationException("Could not save the modular preview scene.");
        }

        private static void ValidateGeneratedAssets()
        {
            GameObject visualPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(VisualPrefabPath);
            if (visualPrefab.GetComponentsInChildren<Rigidbody>(true).Length != 0
                || visualPrefab.GetComponentsInChildren<Collider>(true).Length != 0
                || visualPrefab.GetComponentsInChildren<Animator>(true).Length != 0)
            {
                throw new InvalidOperationException("The visual prefab contains physics or animation components.");
            }

            GameObject variant = AssetDatabase.LoadAssetAtPath<GameObject>(PlayerVariantPath);
            if (variant.GetComponentsInChildren<Rigidbody>(true).Length != 1
                || variant.GetComponentsInChildren<Collider>(true).Length != 1
                || variant.GetComponentsInChildren<QusapModularVisualRig>(true).Length != 1
                || variant.GetComponentsInChildren<QusapModularFacingPresenter>(true).Length != 1)
            {
                throw new InvalidOperationException("The player variant component inventory is invalid.");
            }

            Transform[] sockets = variant.GetComponentsInChildren<Transform>(true)
                .Where(item => item.name == "WeaponSocket").ToArray();
            if (sockets.Length != 1)
                throw new InvalidOperationException($"Expected one WeaponSocket; found {sockets.Length}.");
        }

        private static Bounds CalculateBounds(IReadOnlyList<Renderer> renderers)
        {
            Bounds bounds = renderers[0].bounds;
            for (int i = 1; i < renderers.Count; i++)
                bounds.Encapsulate(renderers[i].bounds);
            return bounds;
        }

        private static Transform FindUnique(IEnumerable<Transform> transforms, string name)
        {
            Transform[] matches = transforms.Where(item => item.name == name).ToArray();
            if (matches.Length != 1)
                throw new InvalidOperationException($"Expected exactly one '{name}', found {matches.Length}.");
            return matches[0];
        }

        private static string ToAbsolutePath(string assetPath)
        {
            string projectRoot = Directory.GetParent(Application.dataPath).FullName;
            return Path.Combine(projectRoot, assetPath.Replace('/', Path.DirectorySeparatorChar));
        }

        private readonly struct ImportedHierarchy
        {
            public ImportedHierarchy(
                Transform visualRoot,
                Transform bodyPivot,
                Transform body,
                Transform footPivotLeft,
                Transform footLeft,
                Transform footPivotRight,
                Transform footRight)
            {
                VisualRoot = visualRoot;
                BodyPivot = bodyPivot;
                Body = body;
                FootPivotLeft = footPivotLeft;
                FootLeft = footLeft;
                FootPivotRight = footPivotRight;
                FootRight = footRight;
            }

            public Transform VisualRoot { get; }
            public Transform BodyPivot { get; }
            public Transform Body { get; }
            public Transform FootPivotLeft { get; }
            public Transform FootLeft { get; }
            public Transform FootPivotRight { get; }
            public Transform FootRight { get; }
        }
    }
}
