#if UNITY_EDITOR
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Qusap.EditorTools
{
    public static class MovementPlaygroundGenerator
    {
        private const string RootName = "GeneratedMovementCourse";
        private const float CourseDepth = 4f;

        [MenuItem("Tools/Qusap/Generate Movement Playground")]
        private static void GenerateMovementPlayground()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode)
            {
                EditorUtility.DisplayDialog(
                    "Movement Playground",
                    "El circuito no puede generarse mientras Unity está en Play Mode.",
                    "Aceptar");
                return;
            }

            if (!EditorUtility.DisplayDialog(
                    "Generate Movement Playground",
                    "Se generará un circuito grande en la escena activa. ¿Deseas continuar?",
                    "Generar",
                    "Cancelar"))
            {
                return;
            }

            Scene activeScene = SceneManager.GetActiveScene();
            GameObject existingCourse = FindRootInScene(activeScene, RootName);

            if (existingCourse != null)
            {
                bool replace = EditorUtility.DisplayDialog(
                    "Replace Movement Playground",
                    "GeneratedMovementCourse ya existe. ¿Deseas reemplazar únicamente ese objeto y sus hijos?",
                    "Reemplazar",
                    "Cancelar");

                if (!replace)
                {
                    return;
                }

                Undo.DestroyObjectImmediate(existingCourse);
            }

            GameObject oneWayTemplate = FindInScene(activeScene, "OneWayPlatformTest");
            GameObject courseRoot = CreateEmpty(RootName, null);
            SceneManager.MoveGameObjectToScene(courseRoot, activeScene);

            Transform run = CreateSection("01_Run", courseRoot.transform);
            Transform precision = CreateSection("02_PrecisionJumps", courseRoot.transform);
            Transform vertical = CreateSection("03_VerticalJumps", courseRoot.transform);
            Transform oneWay = CreateSection("04_OneWayArea", courseRoot.transform);
            Transform wallJump = CreateSection("05_WallJump", courseRoot.transform);
            Transform highFall = CreateSection("06_HighFall", courseRoot.transform);
            Transform combination = CreateSection("07_Combination", courseRoot.transform);

            CreateRunArea(run);
            CreatePrecisionArea(precision);
            CreateVerticalArea(vertical);
            CreateOneWayArea(oneWay, oneWayTemplate);
            CreateWallJumpArea(wallJump);
            CreateHighFallArea(highFall);
            CreateCombinationArea(combination);

            GameObject suggestedStart = CreateEmpty("SuggestedStartPoint", courseRoot.transform);
            suggestedStart.transform.position = new Vector3(-44f, 1f, 0f);

            EditorSceneManager.MarkSceneDirty(activeScene);
            Selection.activeGameObject = courseRoot;

            EditorUtility.DisplayDialog(
                "Movement Playground Generated",
                "GeneratedMovementCourse fue creado en la escena activa. Revísalo manualmente antes de guardar la escena.",
                "Aceptar");
        }

        private static Transform CreateSection(string name, Transform parent)
        {
            return CreateEmpty(name, parent).transform;
        }

        private static GameObject CreateEmpty(string name, Transform parent)
        {
            GameObject created = new GameObject(name);
            Undo.RegisterCreatedObjectUndo(created, $"Create {name}");

            if (parent != null)
            {
                created.transform.SetParent(parent, false);
            }

            return created;
        }

        private static GameObject CreateCube(
            string name,
            Transform parent,
            Vector3 position,
            Vector3 scale)
        {
            GameObject cube = GameObject.CreatePrimitive(PrimitiveType.Cube);
            cube.name = name;
            cube.transform.SetParent(parent, false);
            cube.transform.position = new Vector3(position.x, position.y, 0f);
            cube.transform.localScale = new Vector3(scale.x, scale.y, CourseDepth);
            Undo.RegisterCreatedObjectUndo(cube, $"Create {name}");
            return cube;
        }

        private static void CreateRunArea(Transform parent)
        {
            CreateCube("RunFloor_01", parent, new Vector3(-35f, -0.5f, 0f), new Vector3(20f, 1f, CourseDepth));
            CreateCube("RunFloor_02", parent, new Vector3(-15f, -0.5f, 0f), new Vector3(20f, 1f, CourseDepth));
        }

        private static void CreatePrecisionArea(Transform parent)
        {
            CreateCube("Precision_01", parent, new Vector3(-2f, 0f, 0f), new Vector3(4f, 1f, CourseDepth));
            CreateCube("Precision_02", parent, new Vector3(4f, 0.5f, 0f), new Vector3(3.5f, 1f, CourseDepth));
            CreateCube("Precision_03", parent, new Vector3(10.5f, 0f, 0f), new Vector3(3f, 1f, CourseDepth));
            CreateCube("Precision_04", parent, new Vector3(17.5f, 1f, 0f), new Vector3(4f, 1f, CourseDepth));
        }

        private static void CreateVerticalArea(Transform parent)
        {
            CreateCube("Vertical_01_Low", parent, new Vector3(23f, 1.5f, 0f), new Vector3(4f, 1f, CourseDepth));
            CreateCube("Vertical_02_Medium", parent, new Vector3(28f, 3.5f, 0f), new Vector3(3.5f, 1f, CourseDepth));
            CreateCube("Vertical_03_High", parent, new Vector3(33f, 6f, 0f), new Vector3(4f, 1f, CourseDepth));
            CreateCube("Vertical_04_Landing", parent, new Vector3(39f, 7.5f, 0f), new Vector3(5f, 1f, CourseDepth));
        }

        private static void CreateOneWayArea(Transform parent, GameObject template)
        {
            if (template == null)
            {
                GameObject marker = CreateEmpty("MISSING_OneWayPlatformTest", parent);
                marker.transform.position = new Vector3(47f, 2f, 0f);
                EditorUtility.DisplayDialog(
                    "One-Way Area Warning",
                    "No se encontró OneWayPlatformTest en la escena activa. El área one-way no pudo generarse automáticamente.",
                    "Aceptar");
                return;
            }

            Vector3[] positions =
            {
                new Vector3(45f, 2f, 0f),
                new Vector3(50f, 4f, 0f),
                new Vector3(55f, 6f, 0f),
                new Vector3(60f, 8f, 0f)
            };

            for (int index = 0; index < positions.Length; index++)
            {
                GameObject copy = Object.Instantiate(template, parent);
                copy.name = $"OneWay_{index + 1:00}";
                copy.transform.position = positions[index];
                FlattenHierarchyToZZero(copy.transform);
                Undo.RegisterCreatedObjectUndo(copy, $"Create {copy.name}");
            }
        }

        private static void CreateWallJumpArea(Transform parent)
        {
            CreateCube("WallJumpFloor", parent, new Vector3(69f, -0.5f, 0f), new Vector3(16f, 1f, CourseDepth));
            CreateCube("WallJumpLeft", parent, new Vector3(66f, 5f, 0f), new Vector3(1f, 11f, CourseDepth));
            CreateCube("WallJumpRight", parent, new Vector3(72f, 5f, 0f), new Vector3(1f, 11f, CourseDepth));
            CreateCube("WallJumpExit", parent, new Vector3(75f, 10f, 0f), new Vector3(7f, 1f, CourseDepth));
            CreateCube("SingleWallFloor", parent, new Vector3(83f, -0.5f, 0f), new Vector3(10f, 1f, CourseDepth));
            CreateCube("SingleWallTest", parent, new Vector3(84f, 4f, 0f), new Vector3(1f, 9f, CourseDepth));
        }

        private static void CreateHighFallArea(Transform parent)
        {
            CreateCube("TowerStep_01", parent, new Vector3(90f, 2f, 0f), new Vector3(4f, 1f, CourseDepth));
            CreateCube("TowerStep_02", parent, new Vector3(94f, 4.5f, 0f), new Vector3(4f, 1f, CourseDepth));
            CreateCube("TowerStep_03", parent, new Vector3(98f, 7f, 0f), new Vector3(4f, 1f, CourseDepth));
            CreateCube("HighFallPlatform", parent, new Vector3(102f, 11f, 0f), new Vector3(7f, 1f, CourseDepth));
            CreateCube("HighFallLanding", parent, new Vector3(109f, -0.5f, 0f), new Vector3(16f, 1f, CourseDepth));
        }

        private static void CreateCombinationArea(Transform parent)
        {
            CreateCube("ComboRun", parent, new Vector3(121f, -0.5f, 0f), new Vector3(10f, 1f, CourseDepth));
            CreateCube("ComboRaised_01", parent, new Vector3(130f, 1.5f, 0f), new Vector3(4f, 1f, CourseDepth));
            CreateCube("ComboWallLeft", parent, new Vector3(135f, 4f, 0f), new Vector3(1f, 8f, CourseDepth));
            CreateCube("ComboWallRight", parent, new Vector3(140f, 5.5f, 0f), new Vector3(1f, 9f, CourseDepth));
            CreateCube("ComboUpper", parent, new Vector3(143f, 10f, 0f), new Vector3(7f, 1f, CourseDepth));
            CreateCube("ComboDirectionChange", parent, new Vector3(136f, 12.5f, 0f), new Vector3(4f, 1f, CourseDepth));
            CreateCube("ComboFinalLanding", parent, new Vector3(151f, -0.5f, 0f), new Vector3(16f, 1f, CourseDepth));
        }

        private static GameObject FindInScene(Scene scene, string objectName)
        {
            foreach (GameObject root in scene.GetRootGameObjects())
            {
                foreach (Transform candidate in root.GetComponentsInChildren<Transform>(true))
                {
                    if (candidate.name == objectName)
                    {
                        return candidate.gameObject;
                    }
                }
            }

            return null;
        }

        private static GameObject FindRootInScene(Scene scene, string objectName)
        {
            foreach (GameObject root in scene.GetRootGameObjects())
            {
                if (root.name == objectName)
                {
                    return root;
                }
            }

            return null;
        }

        private static void FlattenHierarchyToZZero(Transform root)
        {
            Vector3 position = root.position;
            root.position = new Vector3(position.x, position.y, 0f);

            foreach (Transform child in root)
            {
                Vector3 localPosition = child.localPosition;
                child.localPosition = new Vector3(localPosition.x, localPosition.y, 0f);
                FlattenHierarchyToZZero(child);
            }
        }
    }
}
#endif
