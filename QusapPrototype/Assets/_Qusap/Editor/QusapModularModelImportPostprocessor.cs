using UnityEditor;

namespace Qusap.Editor
{
    public sealed class QusapModularModelImportPostprocessor : AssetPostprocessor
    {
        public const string ModelPath =
            "Assets/_Qusap/Art/Characters/Models/Qusap_Luz_Modular_v1.fbx";

        private void OnPreprocessModel()
        {
            if (assetPath != ModelPath)
                return;

            var importer = (ModelImporter)assetImporter;
            importer.globalScale = 1f;
            importer.useFileScale = true;
            importer.animationType = ModelImporterAnimationType.None;
            importer.importAnimation = false;
            importer.importCameras = false;
            importer.importLights = false;
            importer.addCollider = false;
            importer.isReadable = false;
            importer.preserveHierarchy = true;
            importer.optimizeGameObjects = false;
            importer.importBlendShapes = false;
            importer.importVisibility = false;
            importer.importConstraints = false;
            importer.importNormals = ModelImporterNormals.Import;
            importer.importTangents = ModelImporterTangents.Import;
            importer.materialImportMode = ModelImporterMaterialImportMode.None;
        }
    }
}
