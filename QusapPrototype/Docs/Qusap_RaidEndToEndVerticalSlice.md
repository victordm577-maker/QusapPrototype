# Raid End-to-End Vertical Slice

Escena de diagnóstico: `Assets/_Qusap/Scenes/RaidEndToEndPlayground.unity`.
Permanece fuera de Build Settings. El HUD, los contenedores geométricos y la
presentación están aprobados para diagnóstico; no constituyen arte final.

## Puente compartido

`QusapSharedRaidContext` crea un RaidId, una `QusapRaidSession` y un
`QusapLootWorld`. Cada participante conserva una
`QusapRaidDeploymentAuthority`, un `QusapPersistentStashRepository`, un
ProfileId y un backend `QusapMemoryProfileFiles` independientes. El contexto
enruta settlements por ProfileId hacia esos repositorios. No posee otro stash
ni otro ledger de objetos.

El backend mantiene los documentos exclusivamente en memoria. El codec,
checksum, revisión, manifiesto y validaciones del file-store existente siguen
activos. Esta ruta compartida exige almacenamiento reversible en memoria y
rechaza backends físicos antes del deployment. La atomicidad de esta ruta no
abarca transacciones entre archivos físicos de varios perfiles. La ruta
individual conserva su API y su soporte de persistencia en disco.

1. Enlistar los participantes requeridos con identidades distintas y preparar
   selecciones de InstanceId y cantidades completas desde sus propios stashes.
2. Capturar checkpoints de documentos, estado del file-store, repositorios,
   recibos, contadores y referencias originales de los objetos del stash.
3. Crear la única sesión y reservar el contador de instancias por encima de
   los contadores de ambos perfiles.
4. Confirmar los deployments mediante el mismo núcleo del flujo individual.
   Los inventarios preparados permanecen bloqueados.
5. Confirmar Running de todos los perfiles antes de habilitar las operaciones
   de cualquier participante y enlazar el bootstrap a la sesión compartida.
6. Si falla el segundo participante durante Prepared o Running, liberar el
   equipo, disponer los adaptadores, retirar los préstamos, vaciar la sesión
   preparada y restaurar ambos checkpoints. El contexto vuelve a None y
   permite reintentar con los objetos y documentos originales.

`HasActiveDeployment` sigue impidiendo más de un deployment activo por perfil.
También se rechazan participantes repetidos, ProfileId repetidos, repositorios
compartidos y autoridades enlistadas en otro contexto. Confirmación,
cancelación y disposición bloquean comandos reentrantes. Cancelar Prepared y
finalizar una raid resuelta son idempotentes; cancelar Running se rechaza.

## Ciclo de la escena

Dos perfiles temporales comienzan con stashes vacíos. La selección previa al
deployment permite elegir un arma propia o un RaidLoaner y stacks completos
de consumibles disponibles. El préstamo se obtiene mediante deployment, sin
inyectar inventario durante la raid.

La escena reutiliza las tablas CommonCache, MedicalCache y HighValueCache,
seleccionando un seed reproducible antes de generar loot. La recogida y el
saqueo utilizan sensor, distancia, línea de visión, hold y RequestTake
versionado. P1 utiliza F; P2, D-pad arriba. E conserva el intercambio o
lanzamiento nativo. El HUD existente ejecuta las acciones reales «A seguro» y
«Consumir».

La tira visible «PELIGRO · 99» utiliza el DamageVolume existente. Movimiento,
daño ambiental, impacto nativo, eliminación y bloqueo terminal usan las
autoridades existentes. El bootstrap crea un único Death Loot Container; su
marcador es una presentación hija, no otra autoridad. El bolsillo seguro se
asienta en el stash del eliminado; mochila y equipo saqueables pasan al
contenedor. Los RaidLoaner quedan excluidos del botín y se destruyen.

El superviviente saquea físicamente y camina hasta la zona de extracción. La
cuenta regresiva real dura cinco segundos y se cancela al salir o recibir
daño. El observer y la autoridad de sesión producen una única resolución.
Los objetos abandonados no se asientan ni reaparecen en el stash.

La siguiente raid usa actores nuevos, una sesión nueva y otro RaidId,
conservando los dos repositorios y sus stashes resultantes. Los objetos
completos transferidos sin split o merge mantienen su InstanceId desde el
cache hasta mochila, contenedor de muerte, superviviente y stash. Las reglas
existentes de split y merge se conservan. La comprobación de duplicados cuenta
propietarios vivos; las proyecciones Stash y los tombstones del ledger no
constituyen una segunda ubicación propietaria.

Root Motion permanece desactivado. El frontend, InputActions, InputReader,
movimiento, combate, animaciones, prefab principal y escenas de producción
conservan sus assets y configuración.

## Separación de fuentes y validación

- Puente: `QusapSharedRaidContext`, `QusapMemoryProfileFiles` y extensiones
  mínimas de LootWorld, MemoryStashRepository, PersistentDeployment,
  ProfileFileStore, RaidBootstrap, RaidDeploymentAuthority y RaidSession.
- Vertical slice: `QusapRaidEndToEndPlayground` y su escena de diagnóstico.
- Pruebas definitivas: `QusapSharedRaidDeploymentEditModeTests` y
  `QusapRaidEndToEndPlayModeTests`, junto con las regresiones individuales
  existentes.
- Documentación: este documento.

El generador de escena, fixtures de captura, automatización, vídeos, logs,
perfiles y evidencias temporales se conservan fuera del commit. La escena
serializada utiliza únicamente fuentes y assets incluidos o ya versionados;
no requiere el generador para abrirse o ejecutarse.

La validación final ejecuta las suites completas EditMode y PlayMode, sin
filtros, en una copia aislada del contenido del commit. Los tests de perfiles
en disco reciben directorios temporales explícitos. El perfil real no se lee
ni modifica. Los XML completos, diagnósticos comparados con el baseline y la
auditoría de referencias se entregan como evidencias externas.
