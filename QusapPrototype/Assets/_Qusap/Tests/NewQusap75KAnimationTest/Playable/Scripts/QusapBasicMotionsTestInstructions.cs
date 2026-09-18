using UnityEngine;

namespace Qusap.NewQusap75KAnimationTest.Playable
{
    [DisallowMultipleComponent]
    public sealed class QusapBasicMotionsTestInstructions : MonoBehaviour
    {
        private static readonly Rect PanelRect = new Rect(18f, 18f, 190f, 150f);

        private void OnGUI()
        {
            GUI.Box(PanelRect, "Qusap75K Basic Motions");
            GUI.Label(new Rect(34f, 48f, 155f, 110f),
                "1  Idle\n2  Walk\n3  Run\n4  Jump\n5  Dying\nR  Roll");
        }
    }
}
