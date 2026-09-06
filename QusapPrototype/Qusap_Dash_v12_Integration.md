# Integración visual Qusap Dash v12

Preparada y validada con Unity **6000.5.6f1**. La herramienta de creación de escena **no se ejecutó**.

## Uso manual

Con Unity fuera de Play Mode y sin escenas con cambios pendientes, ejecutar:

`Tools > Qusap > Create Qusap Luz Dash Test`

La herramienta encuentra la escena WallJump funcional más reciente y crea exclusivamente:

`Assets/_Qusap/Scenes/Qusap_Luz_Locomotion_DashTest_v1.unity`

La escena WallJump original no se modifica. El visual v11 de la copia queda desactivado como `PlayerVisual_WallJumpV11_Backup`; el visual v12 conserva posición, rotación, escala y altura de suelas. Si el destino ya existe, la herramienta se detiene sin sobrescribirlo.

## Archivos creados

- `Assets/Scenes/Art/Characters/Qusap/Final_Light_v3/Qusap_Luz_Locomotion_v12.fbx`
- `Assets/Scenes/Art/Characters/Qusap/Final_Light_v3/Qusap_Luz_Locomotion_v12.fbx.meta`
- `Assets/Scenes/Art/Characters/Qusap/Final_Light_v3/Qusap_Luz_Animator_DashTest_v1.controller`
- `Assets/Scenes/Art/Characters/Qusap/Final_Light_v3/Qusap_Luz_Animator_DashTest_v1.controller.meta`
- `Assets/_Qusap/Editor/QusapLuzDashTestCreator.cs`
- `Assets/_Qusap/Editor/QusapLuzDashTestCreator.cs.meta`
- `Qusap_Dash_v12_Integration.md`

Los tres `.meta` fueron generados por Unity. La escena Dash todavía no existe y su `.meta` tampoco.

## Archivo modificado

- `Assets/_Qusap/Scripts/Animation/QusapAnimationDriver.cs`

El driver usa `Animator.StringToHash("Dashing")`, lee directamente `QusapDashMotor.IsDashing` y transmite ese valor al Animator. Al comenzar el Dash captura la dirección de la velocidad horizontal real que ya aplicó el motor y mantiene el yaw existente de 150°/210° durante todo el Dash. Cuando `IsDashing` termina, publica `Dashing=false` y devuelve la orientación al flujo normal. No añade temporizadores, inputs, sensores ni campos serializados; tampoco escribe en el Rigidbody, collider o raíz física.

`QusapDashMotor.cs`, `QusapCombatController.cs`, WallJump, escenas, prefabs, FBX v11 y versiones anteriores permanecen sin cambios. El bloqueo de ataques continúa usando `QusapDashMotor.IsDashing` en `TryStartAttack`.

## Importación v12

El archivo copiado coincide con el FBX externo por SHA-256:

`D441CCEF4128035E6537728C3C3AE79686B5D7D2C44B39D862DC181CB2B3F8C8`

ModelImporter: Generic, Create From This Model, Import Animation activo, Compression Off, Root Motion bloqueado por clip. Los ajustes de malla, escala, orientación, avatar y materiales coinciden con v11.

Unity reporta el rango del Source Take `Qusap_Dash` como **0–10**. Es el rango de autoría 1–11 convertido a base cero por FBX/Unity: diez frames efectivos, `0,166666672 s` a 60 FPS. No se añadió un frame fuera del Source Take.

| Clip | Frames efectivos | Duración | Loop |
| --- | ---: | ---: | --- |
| Qusap_Idle | 120 | 2,00 s | Sí |
| Qusap_Run | 36 | 0,60 s | Sí |
| Qusap_JumpRise | 18 | 0,30 s | No |
| Qusap_Fall | 24 | 0,40 s | Sí |
| Qusap_Land | 18 | 0,30 s | No |
| Qusap_WallSlide | 48 | 0,80 s | Sí |
| Qusap_WallJump | 18 | 0,30 s | No |
| Qusap_Dash | 10 | 0,166667 s | No |

El modelo contiene tres mallas, una raíz de armadura compartida y cuatro huesos.

## Controller

`Qusap_Luz_Animator_DashTest_v1.controller` conserva los siete estados, cinco parámetros y transiciones de WallJump, cambia sus Motion a v12 y añade `Qusap_Dash` y el Bool `Dashing`.

- Any State → Dash: sin Exit Time, duración 0,02, `Dashing=true`, sin transición a sí mismo.
- Dash → Run/Idle/JumpRise/Fall: sin Exit Time, duración 0,03 y las condiciones solicitadas.
- Las transiciones generales hacia estados previos incluyen `Dashing=false`, de modo que no interrumpen Dash mientras el estado real siga activo.
- El clip no hace loop. Si termina antes que el Dash mecánico, conserva su último frame hasta que `IsDashing` cambie a false.

## Verificación

- Runtime y Editor compilaron en Unity 6000.5.6f1 sin errores ni advertencias C#.
- Ocho Source Takes y ocho clips públicos únicos verificados después de `SaveAndReimport`.
- Todos los clips fueron muestreados sin alterar la transformación raíz visual.
- Diferencia máxima de suelas v11/v12: **0,00000011920929 unidades**.
- `Dashing` siguió `IsDashing` en ambas direcciones; el yaw quedó bloqueado aunque la velocidad de prueba cambiara de signo.
- Dash permaneció activo después de terminar el clip mientras el Bool seguía true.
- Se evaluaron las cuatro salidas Run, Idle, JumpRise y Fall.
- Serialización de `QusapDashMotor`, script de combate y escena WallJump verificadas sin cambios.
- La escena Dash no se creó ni guardó.

Evidencia local: `Logs/Dash-v12-prepare.log` y `Logs/Dash-v12-validation.log`. La validación final terminó con código 0. El aviso de enlace simbólico de esos logs corresponde exclusivamente a la copia técnica temporal de `Library`, ya eliminada; no pertenece a los assets ni a la compilación del proyecto.
