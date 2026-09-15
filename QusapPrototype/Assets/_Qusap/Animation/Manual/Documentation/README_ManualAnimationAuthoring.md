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
    ├── ModelSpace      [conversión uniforme del FBX; no animar]
    │   ├── BodyPivot
    │   │   └── Body
    │   ├── FootPivot_L
    │   │   └── FloatingFoot_L
    │   └── FootPivot_R
    │       └── FloatingFoot_R
    └── WeaponSocket
        └── QusapSwordBlueVisual  [prefab anidado; espada y mano rígidas]
            └── VisualPivot
                └── Model_Blue
```

## Bindings del clip

El clip contiene Local Position (x, y, z) y Local Rotation quaternion (x, y, z, w) en estas cuatro rutas exactas:

- `AnimationRoot/ModelSpace/BodyPivot`
- `AnimationRoot/ModelSpace/FootPivot_L`
- `AnimationRoot/ModelSpace/FootPivot_R`
- `AnimationRoot/WeaponSocket`

No hay bindings bajo el hijo de `WeaponSocket`. Por eso el prefab azul puede sustituirse por `QusapSwordPurpleVisual.prefab` o `QusapSwordWhiteVisual.prefab` sin invalidar clips.

## Pivotes inspeccionados

Valores locales del modelo modular actual, preservados en el rig de autoría:

| Control | Local Position | Local Rotation | Local Scale |
| --- | --- | --- | --- |
| BodyPivot | (-0.0000034422342, 0.0000046346613, 0.01128741) | (0, 0, 0, 1) | (1, 1, 1) |
| FootPivot_L | (-0.0023739683, 0.000597857, 0.003917471) | (0, 0, 0, 1) | (1, 1, 1) |
| FootPivot_R | (0.002369882, 0.00060223666, 0.00391639) | (0, 0, 0, 1) | (1, 1, 1) |

`ModelSpace` conserva la conversión del FBX: posición local (0, 0, -0.00036484003), rotación quaternion (-0.7071068, 0, 0, 0.7071067) y escala uniforme (100, 100, 100).

En producción, `WeaponSocket` parte de posición (0, 0, 0), rotación identidad y escala (1, 1, 1). `QusapEquippedWeaponPresenter` lo escribe en `LateUpdate` con offset (1.15, 0.25, -0.35) y rotación Z de -12 grados para facing derecho; además aplica escala uniforme 0.70 al prefab visual. En el rig aislado, `WeaponSocket` usa (1.15, 1.25, -0.35), compensando el desplazamiento Y=-1 del visual modular en el prefab de producción. El prefab de espada conserva posición/rotación local neutras y escala uniforme 0.70. Su `VisualPivot` conserva la conversión rígida del FBX (-90 grados en X, escala uniforme 100).

## Sustituir el arma temporal

En Prefab Mode, sustituir únicamente el hijo de `WeaponSocket` por uno de estos prefabs:

- `Assets/_Qusap/Prefabs/Weapons/QusapSwordPurpleVisual.prefab`
- `Assets/_Qusap/Prefabs/Weapons/QusapSwordWhiteVisual.prefab`

Dejar el nuevo hijo en Local Position (0, 0, 0), Local Rotation (0, 0, 0) y escala uniforme (0.70, 0.70, 0.70). La espada y la mano pertenecen al mismo visual rígido; no separar la malla ni crear otra mano.

## Escritores de transforms detectados en producción

- `QusapModularVisualRig`: captura/restaura `BodyPivot`, `FootPivot_L` y `FootPivot_R` al habilitar/deshabilitarse.
- `QusapModularFacingPresenter`: escribe la orientación y escala del facing en `LateUpdate`.
- `QusapModularCombatVisualPresenter`: escribe cuerpo, pies y arma en `LateUpdate` durante ataques.
- `QusapEquippedWeaponPresenter`: escribe `WeaponSocket` en `LateUpdate` y crea/configura el visual equipado.
- `QusapWeaponAttackVisualPresenter`: puede escribir el visual equipado en `LateUpdate` (está deshabilitado en el prefab de producción actual).

Ninguno de esos componentes existe en `QusapManualAnimationRig`. El único componente de comportamiento del rig es `Animator`.

## Validación

Usar **Qusap > Manual Animation > Validate Authoring Workspace**. La validación comprueba los bindings, 60 FPS, ausencia de Scale y de curvas en `AnimationRoot`, referencias a las mallas actuales, inventario de componentes, arma azul anidada, compatibilidad rígida de las tres espadas, cámara/luz/suelo de la escena y exclusión de Build Settings.
