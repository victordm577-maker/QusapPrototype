#if UNITY_EDITOR
using UnityEditor;

namespace Qusap.EditorTools
{
    public static class QusapLuzFinalLocomotionTestCreator
    {
        private const string MenuPath =
            "Tools/Qusap/Create Qusap Luz Final Locomotion Test";

        private static readonly QusapLuzAirLocomotionTestCreator.InstallationConfig
            FinalConfiguration = new(
                "Qusap_Luz_Locomotion_v5.fbx",
                "Qusap_Luz_Animator_v3.controller",
                "PlayerVisual_LocomotionV4_Backup",
                "Qusap Luz Final Locomotion Test",
                "Qusap_Luz_Locomotion_FinalTest_v1.unity",
                null,
                "Qusap_Luz_Locomotion_v4.fbx",
                true);

        [MenuItem(MenuPath)]
        private static void CreateFinalTestInstallation()
        {
            QusapLuzAirLocomotionTestCreator.CreateTestInstallation(FinalConfiguration);
        }
    }
}
#endif
