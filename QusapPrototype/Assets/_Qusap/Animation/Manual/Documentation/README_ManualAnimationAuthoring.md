# Qusap: espacio de autoría de animación manual

Este espacio está aislado de `CombatPlayground` y del gameplay. El rig no contiene scripts de runtime, Rigidbody, colliders, hitboxes, skinning ni Root Motion. Las mallas rígidas del personaje siguen referenciando directamente `Qusap_Luz_Modular_v1.fbx`; no se duplicaron mallas.

## Abrir y animar

1. Abrir `Assets/_Qusap/Scenes/Animation/Qusap_AnimationAuthoring.unity`.
2. Seleccionar `QusapManualAnimationRig` en la Hierarchy.
3. Abrir **Window > Animation > Animation**.
4. Seleccionar `Qusap_Idle_Manual_v1` o crear un clip nuevo dentro de `Assets/_Qusap/Animation/Manual/Clips/Locomotion` o `Combat`.
5. Añadir keyframes solamente a **Local Position** y **Local Rotation** de `BodyPivot`, `FootPivot_L`, `FootPivot_R` o `WeaponSocket`.

No añadir curvas de Scale ni curvas para `AnimationRoot`. El clip neutral incluido está a 60 FPS y solo contiene la misma pose en los tiempos 0 y 1 segundo.

## Jerarquía estable

```text
QusapManualAnimationRig  [Animator; Apply Root Motion = false]
└── AnimationRoot       [no animar]
    ├── BodyPivot       [animable; ejes Unity; escala 1]
    │   └── BodyModelSpace [corrección del FBX; no animar]
    │       └── Body
    ├── FootPivot_L     [animable; ejes Unity; escala 1]
    │   └── FootLModelSpace [corrección del FBX; no animar]
    │       └── FloatingFoot_L
    ├── FootPivot_R     [animable; ejes Unity; escala 1]
    │   └── FootRModelSpace [corrección del FBX; no animar]
    │       └── FloatingFoot_R
    └── WeaponSocket
        └── QusapSwordBlueVisual  [prefab anidado; espada y mano rígidas]
            └── VisualPivot
                └── Model_Blue
```

## Bindings del clip

El clip contiene Local Position (x, y, z) y Local Rotation quaternion (x, y, z, w) en estas cuatro rutas exactas:

- `AnimationRoot/BodyPivot`
- `AnimationRoot/FootPivot_L`
- `AnimationRoot/FootPivot_R`
- `AnimationRoot/WeaponSocket`

No hay bindings bajo ningún nodo `ModelSpace` ni bajo el hijo de `WeaponSocket`. Por eso el prefab azul puede sustituirse por `QusapSwordPurpleVisual.prefab` o `QusapSwordWhiteVisual.prefab` sin invalidar clips.

## Pivotes inspeccionados

Los controles ya están expresados en unidades y ejes normales de Unity. La orientación 2.5D que antes estaba en el prefab de escena se incorporó en los descendientes correctivos para que la pose mundial no cambie:

| Control | Local Position | Local Rotation | Local Scale |
| --- | --- | --- | --- |
| BodyPivot | aproximadamente (-0.00089, 1.12874, -0.00012) | (0, 0, 0, 1) | (1, 1, 1) |
| FootPivot_L | aproximadamente (-0.17079, 0.39175, 0.17551) | (0, 0, 0, 1) | (1, 1, 1) |
| FootPivot_R | aproximadamente (0.06597, 0.39164, -0.23554) | (0, 0, 0, 1) | (1, 1, 1) |
| WeaponSocket | aproximadamente (0.27189, 1.25, -1.17093) | (0, 0, 0, 1) | (1, 1, 1) |

`AnimationRoot`, el prefab raíz y todos los controles animables tienen rotación identidad y escala 1. Las rotaciones de importación y la escala uniforme 100 están únicamente en `BodyModelSpace`, `FootLModelSpace` y `FootRModelSpace`, que son descendientes no animables.

En producción, `QusapEquippedWeaponPresenter` escribe el socket con offset (1.15, 0.25, -0.35) y rotación Z de -12 grados. En el rig aislado, el control `WeaponSocket` permanece con rotación identidad; la orientación visual preexistente se conserva en el prefab hijo. La espada mantiene escala uniforme 0.70 y su `VisualPivot` conserva la conversión rígida del FBX. Así, mover Local X/Y/Z del socket corresponde directamente a los ejes mundiales de Unity.

## Sustituir el arma temporal

En Prefab Mode, sustituir únicamente el hijo de `WeaponSocket` por uno de estos prefabs:

- `Assets/_Qusap/Prefabs/Weapons/QusapSwordPurpleVisual.prefab`
- `Assets/_Qusap/Prefabs/Weapons/QusapSwordWhiteVisual.prefab`

Para conservar la pose neutral exacta, copiar del hijo azul actual Local Position, Local Rotation y Local Scale al nuevo prefab. La corrección visual está en el hijo, nunca en `WeaponSocket`. La espada y la mano pertenecen al mismo visual rígido; no separar la malla ni crear otra mano.

## Escritores de transforms detectados en producción

- `QusapModularVisualRig`: captura/restaura `BodyPivot`, `FootPivot_L` y `FootPivot_R` al habilitar/deshabilitarse.
- `QusapModularFacingPresenter`: escribe la orientación y escala del facing en `LateUpdate`.
- `QusapModularCombatVisualPresenter`: escribe cuerpo, pies y arma en `LateUpdate` durante ataques.
- `QusapEquippedWeaponPresenter`: escribe `WeaponSocket` en `LateUpdate` y crea/configura el visual equipado.
- `QusapWeaponAttackVisualPresenter`: puede escribir el visual equipado en `LateUpdate` (está deshabilitado en el prefab de producción actual).

Ninguno de esos componentes existe en `QusapManualAnimationRig`. El único componente de comportamiento del rig es `Animator`.

## Validación

Usar **Qusap > Manual Animation > Validate Authoring Workspace**. La validación comprueba los bindings, frames 0/60 a 60 FPS, ausencia de Scale y de curvas sobre `AnimationRoot` o `ModelSpace`, identidad de controles y ancestros, correspondencia Local Y/mundo Y y Local X/mundo X, matrices mundiales y materiales frente a la pose anterior, referencias a las mallas actuales, inventario de componentes, arma azul completa, compatibilidad de las tres espadas, cámara/luz/suelo y exclusión de Build Settings.
