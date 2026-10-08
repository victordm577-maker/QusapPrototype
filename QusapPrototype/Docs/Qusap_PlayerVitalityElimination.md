# Fundamento de vida y eliminación

Base: `master` en `8582a48e7f8d9544887e5259312913b0c94bdab1`.
Rama: `feat/player-vitality-elimination`. La integración a master queda fuera
del alcance de este hito.

El fundamento de vida, curación, eliminación, HUD y recuperación técnica
está aprobado visual y técnicamente. `MaxHealth = 100` está aprobado únicamente
como valor provisional de ingeniería. No está aprobado el balance de vida
ni de daños y los valores de ataques permanecen intactos.

No se implementan ganador, stocks, rondas, extracción, cuerno, ocho jugadores,
inventario persistente, bolsillo seguro, caída de loot, escudo, menús ni
regeneración automática.

## Autoridad y ciclo de vida

`QusapHitReceiver.TotalDamageReceived` sigue siendo el único acumulador.
`MaxHealth` está serializado y `CurrentHealth` calcula
`Clamp(MaxHealth - TotalDamageReceived, 0, MaxHealth)`. Los daños de impactos,
finishers y proyectiles conservan su ruta original, magnitud y knockback.
El daño acumulado conserva el exceso de un impacto letal; la vida visible
nunca baja de cero.

`Heal(amount)` devuelve la curación efectiva, reduce ese acumulador y no cura
a un jugador con vida agotada o eliminado. No supera MaxHealth ni regenera
automáticamente. Rechaza cantidades negativas, no finitas y cero.

Eventos públicos:

- `HealthChanged(QusapHealthChange)`: cambio autoritativo de vida/daño.
- `Healed(QusapHealthChange)`: curación efectiva, con cantidad aplicada.
- `HealthDepleted(QusapHealthChange)`: vida agotada, una vez por sesión.
- `Eliminated(QusapHitReceiver)`: eliminación completa, una vez por sesión.
- `RecoveryCompleted(QusapRecoveryCause)`: recuperación técnica o nueva sesión.

El payload identifica `Combat`, `Environment`, `Healing` o `NewSession` y la
fuente del cambio. Los eventos originales `HitReceived` y `FinisherReceived`
siguen disponibles.

Al agotarse la vida se rechazan inmediatamente impactos y curación posteriores.
La eliminación se resuelve en LateUpdate, después del callback del impacto
y de su procesamiento por el atacante. Desactiva combate/parry, hitstun,
dash, motores e input; conserva receptor, equipamiento, visual y referencias.
Retira las colisiones y congela el Rigidbody del eliminado, que deja de
bloquear físicamente al rival. No llama a Respawn, recarga la escena ni
declara ganador. El inventario se conserva y se bloquean pickups del eliminado.

El bloqueo externo de input es independiente del bloqueo temporal de hitstun.
La limpieza de hitstun no puede liberar el bloqueo de eliminación.

## Recuperación técnica

El detector sigue siendo `Y < -8`. `TechnicalRecovery()` y el alias compatible
`Respawn()` restauran posición segura, velocidades, dash, ataque e hitstun,
conservando daño, vida e inventario. No recuperan jugadores con vida agotada
y publican `QusapRecoveryCause.TechnicalRecovery`.

`ResetForNewSession()` es la operación explícita de nueva sesión, disponible
para pruebas/futuro inicio de partida. Puede restaurar vida y autoridades.
`ResetDamage()` se conserva para fixtures de jugadores vivos; no revive
eliminados ni libera su bloqueo. Ninguna fórmula de movimiento cambia.

## Inventario de daños existente

Fuente: definiciones serializadas de QusapCombatPlayer.prefab, controlador
de combate y perfil de proyectil de CombatPlayground. No se modificó
ninguna de estas definiciones.

| Ataque / definición | Daño | Contexto |
|---|---:|---|
| WeaponLight / Light | 1 | Ataque actual con arma; suelo y aire |
| WeaponStrong / Heavy | 1 | Ataque actual con arma; suelo y aire |
| Combo BodyAttack Ground | 1 | Ataque corporal actual en suelo |
| Combo BodyAttack Air | 1 | Ataque corporal actual en aire |
| Combo WeaponLight Ground | 1 | Definición de preparación existente |
| Combo WeaponLight Air | 1 | Definición de preparación existente |
| WeakKick Ground legado | 0 | Definición independiente existente |
| StrongKick Ground legado | 0 | Definición independiente existente |
| Headbutt Ground independiente | 0 | Se conserva sin daño añadido |
| WeakKick Air independiente | 4 | Definición aérea existente |
| StrongKick Air independiente | 9 | Definición aérea existente |
| DiveHeadbutt Air | 12 | Cabezazo aéreo actual |
| Finisher Damage / B_2 | 15 | Resolución existente tras ventana de parry |
| Finisher Disarm / H2 | 2 | Cabezazo final con petición de desarme |
| Finisher Launch / E2 | 3 | Resolución existente de lanzamiento |
| Proyectil de arma | 6 | Perfil actual del bootstrap de armas |

Una secuencia Damage con tres preparaciones de 1 y un finisher de 15 suma 18:
cinco secuencias dejan 10 de vida y la sexta puede eliminar. Esta referencia
justifica el valor provisional para probar huida y curación; no constituye
un balance aprobado. El Headbutt terrestre independiente conserva daño cero;
DiveHeadbutt y el finisher H2 sí reducen vida. Un parry exitoso no añade daño;
el fallido conserva la resolución existente, sin una penalización nueva.

## HUD y volumen ambiental

CombatPlayground incorpora únicamente la nueva referencia CombatVitalityHUD.
El HUD UGUI observa P1/P2: barra, vida numérica y ELIMINADO, sin ganador,
stocks, contador ni un segundo acumulador de daño. El Canvas se crea durante
la ejecución a partir de esa referencia y usa la fuente integrada de Unity.

QusapDamageVolume permite OncePerEntry o Periodic, daño e intervalo
configurables y un vector de knockback explícito opcional (cero por defecto).
Identifica la fuente como Environment y consolida contactos por receptor
para evitar daño duplicado por varios colliders. Salir detiene el daño
periódico. No crea fuego visual ni hitstun ambiental.

La curación de producción es Heal(amount). El pickup, los materiales y la
zona diagnóstica usados durante la revisión visual quedan fuera del commit,
al igual que builders, grabación y evidencia temporal. No hay loot final.
Las pruebas definitivas crean sus volúmenes en memoria y no dependen de
esos assets locales excluidos.

## Pruebas definitivas

EditMode cubre autoridad única, MaxHealth serializado, curación parcial y
clamps, valores inválidos/cero, agotamiento diferido, nueva sesión,
compatibilidad de ResetDamage y apertura del prefab/escena con HUD válido.
PlayMode cubre impactos existentes, HUD, daño ambiental y salida, curación,
eliminación única, bloqueo de teclado y gamepad (incluido parry nativo),
ausencia de colisiones del eliminado y recuperación conservando vida/inventario.
Comprueba una espada visible, Root Motion desactivado, Time.timeScale y
Time.fixedDeltaTime intactos.

Las suites completas se ejecutaron sin filtros con Unity 6000.5.7f1
(017862109af0), revisión 96354, incluyendo --burst-disable-compilation.
Aprobaron 374 EditMode y 463 PlayMode, con cero fallidas, omitidas e
inconclusas y salida 0 en ambas ejecuciones. Incluyen 11 pruebas EditMode
y 12 PlayMode de vitalidad. La diferencia de una prueba por plataforma
respecto de las estimaciones 373/462 corresponde a la apertura del
prefab/escena y al bloqueo de comandos nativos de gamepad, incluido parry.

Las pruebas de vitalidad no publicaron errores o warnings de runtime.
Las consolas completas conservan el aviso del editor por Burst desactivado,
cuatro errores esperados mediante LogAssert.Expect en pruebas preexistentes
de integración visual y dos avisos preexistentes de fixtures sin gamepad.
No se silencian ni se atribuyen esos mensajes al fundamento de vitalidad.
Los resultados, consolas y evidencia se conservan externamente y no forman
parte del commit.

La escena Manual, el prefab principal, los 1530 archivos preexistentes y los
3996 archivos comprobados del frontend conservan sus hashes del inicio de
la fase de publicación. Packages/packages-lock.json y
ProjectSettings/ProjectAuditorSettings.asset quedan fuera del commit;
no se revierten sus cambios previos.

## Limitación aceptada del entorno

Smart App Control permanece activo. Se aceptan únicamente bloqueos de
Library/BurstCache/JIT/*.dll y copias temporales de
Unity.UIToolkit.SourceGenerator.dll cargadas por el dotnet.exe incluido
en Unity 6000.5.7f1. La revisión de Code Integrity debe detener el commit
si aparece Bee.Tools.dll, NiceIO.dll, Assembly-CSharp.dll, una DLL bajo
Assets o Packages, o cualquier componente diferente no identificado.

Los bloqueos conocidos no se interpretan como pruebas aprobadas por sí solos:
se exige ejecución completa, cero fallidas/omitidas/inconclusas y ausencia
de errores o warnings nuevos de runtime. No se desactiva seguridad ni se
borra Library/BurstCache.

Durante las suites finales Code Integrity registró 20 eventos en EditMode
(3033: 4, 3077: 4, 3089: 8, 3118: 4), todos por copias temporales del
generador UI Toolkit cargadas por dotnet.exe del editor. En PlayMode no hubo
eventos nuevos de esos IDs. No apareció ninguna categoría prohibida.
