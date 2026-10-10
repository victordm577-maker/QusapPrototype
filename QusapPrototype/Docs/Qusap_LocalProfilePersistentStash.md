# Local Profile + Persistent Stash Foundation

Base aprobada: `c8ad95c4b7046dd26afbddfb15d2ef7029d0a2a1`. Rama: `feat/local-profile-persistent-stash`.

## Auditoría e identidad

`QusapMemoryStashRepository` sigue siendo la única autoridad de objetos del stash. `QusapPersistentStashRepository` la envuelve mediante `IQusapStashRepository`; no mantiene un segundo inventario. El candidato JSON es un DTO temporal de una transacción completa, validada por la autoridad antes de publicarse. El callback mínimo de preparación permite rechazar el settlement sin mover objetos ni perder equipo si el disco falla. Los eventos `ExtractionSettled` y `DeathSettled` existentes siguen publicando el resultado completo. Las reglas de mochila, bolsillo, eliminación y extracción permanecen iguales.

Los IDs existentes son strings `RaidId/loot/N`, con un contador `ulong`. Se restauran mediante el constructor interno con el mismo `LootInstanceId`, sin llamar a `Create` y sin emitir identidades sustitutas. `NextItemInstanceId` continúa por encima de los sufijos restaurados y de los objetos emitidos en el ledger observado, incluso si no llegaron al stash. Cada nueva incursión conserva su namespace propio. Las armas conservan además su `QusapWeaponInstance.InstanceId` y su definición nativa; `NextNativeWeaponInstanceId` reserva el rango antes de crear las nuevas armas iniciales.

No existe cantidad, apilamiento, durabilidad ni estadísticas mutables por objeto. Cada fragmento es una instancia independiente. Las propiedades persistentes existentes son `RaidId` de origen y `Provenance`, más la identidad nativa de las armas. No se inventa `Quantity`; tampoco se persisten localización temporal, dueño de equipo, slots o estados de incursión. Las referencias Unity se resuelven únicamente después de validar el archivo, mediante `DefinitionId` y el catálogo ya aprobado.

## Esquema v1

Producción: `Application.persistentDataPath/Qusap/profile-v1.json`. Archivos asociados: `.tmp` y `.bak`. No se usa PlayerPrefs.

El sobre tiene `Content` y `Checksum`. SHA-256 se calcula sobre los bytes UTF-8 del JSON compacto canónico de `Content`, sin BOM. Los campos se emiten en su orden declarado; `Stash` se ordena por `InstanceId` y `Settlements` por `SettlementId`, usando comparación ordinal. La carga rechaza campos omitidos, desconocidos, duplicados o un formato no canónico. El ejemplo siguiente se muestra con indentación sólo para documentar; un archivo real se escribe compacto y su checksum se calcula automáticamente.

```json
{
  "Content": {
    "SchemaVersion": 1,
    "ProfileId": "example-local-profile",
    "Revision": 1,
    "SavedAtUtc": "2026-10-10T00:00:00.0000000Z",
    "NextItemInstanceId": 8,
    "NextNativeWeaponInstanceId": 4,
    "Stash": [
      {
        "InstanceId": "example-raid/loot/7",
        "DefinitionId": "example-fragment",
        "RaidId": "example-raid",
        "Provenance": "Pickup: example fragment",
        "NativeWeaponInstanceId": 0,
        "NativeWeaponDefinitionId": ""
      }
    ],
    "Settlements": [
      {
        "SettlementId": "example-raid/extraction/P1",
        "Fingerprint": "example-local-profile\nexample-raid/loot/7"
      }
    ]
  },
  "Checksum": "25ca34a3f62291bb45b1413153a4a8fff1894c30b4581c11a1267e75a0585256"
}
```

`ProfileId` se conserva al cargar; una nueva identidad se genera sólo si no existe un perfil cargable. `Revision` comienza en cero en memoria y la primera transferencia con objetos guarda uno. `SavedAtUtc` usa UTC en formato round-trip. Los comprobantes `Settlements` conservan sólo la idempotencia de las transferencias, no progreso ni resultados de la sesión. El save contiene un único perfil persistente; los demás participantes del playground conservan su comportamiento diagnóstico en memoria.

## Escritura y fallos

Cada settlement con objetos produce como máximo una revisión y una escritura. La autoridad rechaza duplicados y conflictos antes del callback; las señales repetidas y el cierre sin cambios no crean archivos ni revisiones. Se escribe `.tmp` mediante `CreateNew`, se fuerza el flush al disco, se cierra el stream y se vuelve a leer y validar el archivo completo. Sólo entonces `File.Replace` sustituye atómicamente el principal y conserva su versión anterior como `.bak`. Si aún no existe principal, se usa `File.Move` en el mismo directorio. No se introduce un fallback destructivo si el sistema de archivos no admite la sustitución: el settlement se rechaza con un resultado controlado.

La carga valida versión, checksum, orden, metadatos, IDs únicos, contadores, definiciones y comprobantes antes de restaurar objetos. Prefiere Main; si no es válido, intenta Backup. Un backup válido restaura los mismos IDs una sola vez. Si ambos son inválidos se inicia un perfil vacío seguro y se informa Recovery. Una versión futura bloquea escrituras y nunca se sustituye por una versión anterior ni por un perfil vacío.

Los archivos corruptos se conservan. Como sólo se permiten tres nombres de perfil, un principal corrupto recuperado desde backup permanece en su sitio y bloquea nuevas escrituras hasta revisión; no se lo elimina ni se lo renombra a un cuarto archivo. Un backup corrupto también se protege. Un `.tmp` huérfano o incompleto no se toma como stash y no se sobrescribe. Los errores de lectura/escritura se convierten en diagnóstico y no impiden iniciar el juego. El guardado compara el contenido cargado con el actual para rechazar instancias obsoletas y cambios externos.

## Escena y pruebas

`Assets/_Qusap/Scenes/LocalProfilePersistencePlayground.unity` deriva de `RaidSessionPlayground`, reutiliza definiciones, prefabs, movimiento, extracción de cinco segundos y observación de sesión aprobados. Añade la capa de persistencia y un HUD de diagnóstico. Permanece fuera de Build Settings; no cambia escenas ni prefabs de producción. Root Motion sigue desactivado.

La ruta se inyecta en el repositorio y en fixtures del componente. EditMode y PlayMode usan directorios GUID bajo el temporal del sistema; la limpieza valida la ruta exacta y sólo borra el directorio creado por ese test. Un runner de pruebas que intente iniciar el componente sin una ruta aislada se rechaza antes de leer el perfil real. La evidencia usa el argumento de editor `-qusapProfileDirectory`, fuera del repositorio y distinto del perfil real.

La demostración usa dos procesos completos de Unity: A recoge tres fragmentos, guarda la reliquia en el bolsillo y completa una extracción real para Revision 1; el proceso termina. B carga el mismo archivo desde disco, compara todos los DTOs e IDs, crea un ID nuevo y realiza una segunda transferencia autoritativa para Revision 2. La recarga lógica adicional de B verifica Revision 2 y cero duplicados; no sustituye la evidencia del cierre entre A y B. El video compuesto separa los procesos visiblemente.

El informe y las consolas completas, resultados XML, PID/horarios, hashes de archivos y video se conservan en una carpeta externa de evidencias, fuera del repositorio y del perfil de producción.

## Alcance

El checksum detecta corrupción accidental; no protege contra edición deliberada por el jugador. No hay cifrado, antitrampas, Steam Cloud, backend, autenticación, cuentas ni sincronización. No se persisten vida, posición, físicas, Animator, hitstun, estados de raid, contenedores o resultados de sesión. Tampoco progreso, monedas, desbloqueos, cuerno, recompensas o una UI definitiva. La persistencia no controla combate, movimiento, físicas ni input.
