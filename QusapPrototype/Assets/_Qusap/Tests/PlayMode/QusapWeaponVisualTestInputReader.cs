namespace Qusap.Tests
{
    // A no-device input source for weapon presentation tests. It deliberately
    // suppresses QusapInputReader's Unity messages; these tests never drive input.
    public sealed class QusapWeaponVisualTestInputReader : QusapInputReader
    {
        private void Awake()
        {
        }

        private void OnEnable()
        {
        }

        private void OnDisable()
        {
        }

        private void OnDestroy()
        {
        }

        private void Update()
        {
        }
    }
}
