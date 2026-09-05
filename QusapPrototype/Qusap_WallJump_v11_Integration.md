# Integración visual Qusap Luz v11

Preparada y validada con Unity **6000.5.6f1**. La herramienta de creación de escena **no se ejecutó**.

## Uso manual

Fuera de Play Mode, con las escenas guardadas, ejecutar:

`Tools > Qusap > Create Qusap Luz Wall Jump Test`

La herramienta busca la escena WallSlide guardada más reciente que tenga jugador y controller válidos. Actualmente es `Assets/_Qusap/Scenes/Qusap_Luz_Locomotion_WallSlideTest_v1.unity`. Crea `Assets/_Qusap/Scenes/Qusap_Luz_Locomotion_WallJumpTest_v1.unity`, conserva el visual v10 de la copia como `PlayerVisual_WallSlideV10_Backup` desactivado, instala v11 y guarda/abre solo la copia. Si el destino ya existe, no lo sobrescribe. Tampoco descarta cambios sin guardar.

Antes de guardar, comprueba la transformación local, las suelas y la serialización de los componentes ajenos al visual. El cuadro final muestra las rutas, los siete clips, la señal utilizada, el respaldo, la diferencia de suelas y la verificación de jugabilidad.

## Archivos creados

- `Assets/Scenes/Art/Characters/Qusap/Final_Light_v3/Qusap_Luz_Locomotion_v11.fbx`
- `Assets/Scenes/Art/Characters/Qusap/Final_Light_v3/Qusap_Luz_Locomotion_v11.fbx.meta`
- `Assets/Scenes/Art/Characters/Qusap/Final_Light_v3/Qusap_Luz_Animator_WallJumpTest_v1.controller`
- `Assets/Scenes/Art/Characters/Qusap/Final_Light_v3/Qusap_Luz_Animator_WallJumpTest_v1.controller.meta`
- `Assets/_Qusap/Editor/QusapLuzWallJumpTestCreator.cs`
- `Assets/_Qusap/Editor/QusapLuzWallJumpTestCreator.cs.meta`
- `Qusap_WallJump_v11_Integration.md`

Todos los `.meta` nuevos fueron generados por Unity. No se copiaron ni editaron manualmente. La escena WallJump aún no existe; se generará al ejecutar el menú.

## Archivos modificados

- `Assets/_Qusap/Scripts/Movement/QusapVerticalMotor.cs`: propiedad pública de lectura `WallJumpSequence` y un incremento al final de la rama que ya aplica el Wall Jump. Sin campos serializados nuevos ni cambios en cálculos, condiciones, velocidades, fuerzas o tiempos.
- `Assets/_Qusap/Scripts/Animation/QusapAnimationDriver.cs`: consume esa secuencia solo para controllers con el Bool `WallJumping`. Conserva la orientación visual 0,10 s y luego utiliza la velocidad horizontal real. Mantiene 150°/210° y la velocidad de giro existente. Libera el Bool al salir del estado, tocar suelo o superar el límite visual de 0,33 s (clip de 0,30 s más entrada de 0,03 s). No escribe en Rigidbody, collider ni transformación física raíz.

Los archivos v10 y anteriores, las escenas, prefabs, controllers anteriores, los scripts de dash, combate y respawn y los valores serializados existentes permanecen sin cambios según la comparación con Git. Al retirar las dos adiciones visuales del motor vertical, su código coincide exactamente con la versión original.

## Importación y controller

Generic, Create From This Model, Import Animation activo, Compression Off; Root Motion bloqueado en todos los clips y desactivado en el Animator. Los ajustes de malla, escala, ejes, avatar y materiales coinciden con v10. El FBX de destino coincide por SHA-256 con el origen:

`19B7EBD8307BE8E656AE98C7404E09581AA0034A7F021642E08727DA6CE665D7`

Source Takes leídos de `defaultClipAnimations`, todos desde frame 0, a 60 FPS:

| Clip | Frames efectivos | Duración | Loop Time / Pose |
| --- | ---: | ---: | --- |
| Qusap_Idle | 120 | 2,00 s | Sí |
| Qusap_Run | 36 | 0,60 s | Sí |
| Qusap_JumpRise | 18 | 0,30 s | No |
| Qusap_Fall | 24 | 0,40 s | Sí |
| Qusap_Land | 18 | 0,30 s | No |
| Qusap_WallSlide | 48 | 0,80 s | Sí |
| Qusap_WallJump | 18 | 0,30 s | No |

El controller conserva los seis estados originales y sus transiciones, sustituye sus Motion por v11 y añade WallJump. Las transiciones generales hacia Fall, JumpRise y WallSlide llevan `WallJumping = false`. La entrada desde WallSlide dura 0,03 s, sin Exit Time. Hay entradas explícitas desde los demás estados para cubrir saltos reales que ocurren antes de que el Animator llegue a WallSlide; no hay entrada Any State a WallJump ni auto-transiciones.

WallJump sale hacia JumpRise al 80%, con mezcla de 0,05 s. Grounded permite salir a Land sin Exit Time, incluso durante las mezclas de entrada/salida. La salida usa interrupción desde el estado de origen para impedir que una entrada de JumpRise reinicie WallJump durante la mezcla.

## Verificación realizada

- Compilación de runtime y Editor: sin errores ni advertencias C#.
- Importación y siete clips verificados tras `SaveAndReimport`.
- Muestreo de todos los frames: ningún clip cambia posición, rotación o escala de la raíz visual.
- Diferencia máxima de suelas v10/v11 en Idle: **0,00000011920929 unidades**, sin ajustar posición local.
- Ejecución de la rama real del motor en objetos temporales: ambas paredes con input neutral, hacia la pared y contrario; señal emitida y velocidades originales verificadas.
- Driver: orientación retenida/liberada, Bool activo solo tras señal, raíz física y velocidad intactas.
- Animator evaluado: WallSlide → WallJump → JumpRise; Grounded → Land.
- Salto normal, abandonar la pared y dash: no emiten señal ni activan WallJumping.
- `git diff --check` correcto; escena WallJump ausente.

Evidencia local, ignorada por Git: `Logs/WallJump-prepare.log`, `Logs/WallJump-validation.log` y `Library/WallJump-validation.txt`. La ejecución final terminó con código 0. Los logs incluyen mensajes del entorno de licencias/conectividad de Unity; no son diagnósticos C# de esta integración.

Las comprobaciones usaron objetos de una escena de previsualización desechable y evaluación manual del Animator/motor, sin guardar escenas. Falta la revisión interactiva en Play Mode que realizará el usuario tras ejecutar el menú, especialmente la apariencia del empuje de los pies en ambas paredes.

Para repetir únicamente la validación, el método batch `Qusap.EditorTools.QusapLuzWallJumpTestCreator.ValidatePreparedAssets` no ejecuta el creador de escena. `PrepareAssets` prepara/importa FBX y controller sin crear la escena.
