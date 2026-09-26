using System.Collections;
using System.IO;
using UnityEngine;

public sealed class QusapWeaponGripV1RuntimeValidation : MonoBehaviour
{
    private IEnumerator Start()
    {
        if (File.Exists(@"C:\Dev\Qusap_ArtSource_Recovered\Qusap75K_WeaponGrip_v1\phase1.request"))
            yield return QusapWeaponGripRollIsolation.Run();
        else
            gameObject.AddComponent<QusapSwordCarryValidation>().final = true;
    }
}
