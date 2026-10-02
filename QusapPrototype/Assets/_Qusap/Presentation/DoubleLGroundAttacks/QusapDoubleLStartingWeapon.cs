using Qusap;
using UnityEngine;

// Initial loadout only. The existing inventory owns every later transition.
[DisallowMultipleComponent]
[DefaultExecutionOrder(200)]
public sealed class QusapDoubleLStartingWeapon : MonoBehaviour
{
    void Start()
    {
        var equipment=GetComponent<QusapWeaponEquipment>();
        if(equipment.HasWeapon)return;
        var controller=GetComponent<QusapCombatController>();
        ulong instanceId=(EntityId.ToULong(controller.GetEntityId())<<32)|1UL;
        equipment.TryEquip(new QusapWeaponInstance(instanceId,new QusapWeaponDefinition(QusapWeaponVisualCatalog.BlueDefinitionId,"Espada B")),out _);
    }
}
