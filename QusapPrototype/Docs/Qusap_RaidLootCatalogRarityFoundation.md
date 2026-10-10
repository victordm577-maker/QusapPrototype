# Raid Loot Catalog + Rarity Foundation

El catálogo `Assets/_Qusap/Settings/RaidLootCatalog/RaidLootCatalog.asset` es la
autoridad de metadatos del playground. Reutiliza las seis definiciones aprobadas
de RaidInventory y añade tres. No sustituye `QusapWeaponVisualCatalog`: las
espadas siguen resolviendo sus definiciones lógicas nativas desde ese catálogo.
No se cambian daño, ataques, animaciones, movimiento, físicas ni Root Motion.

## Definiciones y compatibilidad

Los IDs canónicos aprobados permanecen intactos. Los nombres solicitados que
describen un objeto existente son alias explícitos hacia la misma definición,
sin duplicar assets, instancias ni armas. Los saves guardan el ID canónico.

| Nombre solicitado / alias | DefinitionId canónico | Categoría | Rareza | MaxStack | BaseValue | Dato explícito |
| --- | --- | --- | --- | ---: | ---: | --- |
| raid_shell_fragment | raid_fragment | Material | Common | 10 | 1 | — |
| raid_ancient_scale | raid_ancient_scale | Material | Uncommon | 5 | 3 | — |
| raid_resonant_crystal | raid_resonant_crystal | Material | Rare | 3 | 8 | — |
| raid_minor_heal | raid_healing | Consumable | Common | 2 | 2 | HealAmount 25, ConsumeOnUse true |
| raid_major_heal | raid_major_heal | Consumable | Rare | 1 | 6 | HealAmount 50, ConsumeOnUse true |
| raid_basic_sword | raid_qusap_sword_blue | Weapon | Common | 1 | 5 | WeaponDefinitionId qusap_sword_blue |
| raid_purple_sword | raid_qusap_sword_purple | Weapon | Rare | 1 | 12 | WeaponDefinitionId qusap_sword_purple |
| raid_rare_relic | raid_rarerelic | Relic | Epic | 1 | 20 | SecurePocketAllowed true, sin efecto de victoria |
| Espada blanca existente | raid_qusap_sword_white | Weapon | Common | 1 | 5 | WeaponDefinitionId qusap_sword_white |

La espada morada posee una definición lógica real, utilizada por el bootstrap
nativo como arma inicial de P2. La blanca se conserva porque el bootstrap y los
objetos persistentes existentes ya la necesitan; no participa en la tabla nueva.
Los nombres visibles existentes se mantienen como texto provisional compatible.

Todas las definiciones permiten mochila, caída al morir y persistencia en stash.
Material, Consumable y Relic permiten bolsillo; Weapon lo prohíbe. Armor está
disponible únicamente como categoría de datos, sin asset jugable, equipamiento,
estadísticas, modelo ni reducción de daño. Su cantidad es 1 y no permite bolsillo.

`Material = 0` conserva el valor serializado de `Fragment`, cuyo nombre permanece
como alias de enum para los callers aprobados. Relic=1, Consumable=2, Weapon=3 y
Armor=4 conservan sus valores anteriores. Horn/Objective siguen rechazados por
el catálogo nuevo y no se implementa comportamiento adicional para ellos.

Rarezas: Common=0, Uncommon=1, Rare=2, Epic=3, Legendary=4. Determinan presentación
y restricciones declaradas de disponibilidad. **No multiplican daño, curación ni
defensa**. HealAmount proviene exclusivamente de la definición. MaxHealth sigue
limitando la curación efectiva mediante el receptor aprobado.

`WorldPickupPresentationId` usa `raid_pickup_common`, `raid_pickup_uncommon`,
`raid_pickup_rare`, `raid_pickup_epic` o `raid_pickup_legendary`. El playground
resuelve esas presentaciones mediante materiales provisionales gris, verde,
azul, morado y dorado. Los tags son `raid` y la categoría en minúsculas, ordenados
ordinalmente, sin repetidos. Nombres, valores, cantidades y pesos son provisionales.

## Stacks y autoridad

`QusapLootWorld` conserva un solo ledger y asigna cada InstanceId una sola vez.
Quantity se añade a instancias, snapshots y DTOs persistentes. Sólo Material y
Consumable apilan. Weapon, Armor y Relic admiten exclusivamente Quantity 1.

Al adquirir un pickup o contenido de un contenedor de muerte, se llenan primero
stacks parciales compatibles, por orden de slot, y luego un slot nuevo si queda
cantidad. Deben coincidir DefinitionId, RaidId y Provenance, y no existir arma
nativa. Propiedades persistentes distintas impiden la fusión. La operación se
planifica y reserva completa: si la cantidad restante necesita un séptimo slot,
se rechaza sin modificar origen ni parciales. La mochila mantiene seis slots.

Un origen absorbido por completo queda retirado como `Merged`, Quantity 0, sin
reutilizar su ID. Las unidades se conservan en los stacks destino. No existe
división manual. Mover explícitamente un stack completo entre mochila, bolsillo
y stash conserva InstanceId y Quantity; estas transferencias no fusionan stacks.
El bolsillo mantiene un slot. Los consumibles retiran una unidad sólo tras una
curación efectiva y sólo si ConsumeOnUse es true. A salud máxima, NoEffect
conserva íntegramente la cantidad.

CanEnterBackpack y CanEnterSecurePocket se verifican en la autoridad de
transferencia. CanDropOnDeath=false excluye el objeto del contenedor y lo retira
del raid; CanPersistInStash=false impide persistirlo y lo retira al liquidar una
extracción o un bolsillo protegido. El catálogo provisional utiliza true para
ambas reglas en todas sus definiciones. La rareza nunca participa en estas decisiones.

## Tabla determinista

`Assets/_Qusap/Settings/RaidLootCatalog/RaidTestRouteLoot.asset`:

| Entrada | Peso | Quantity mínima | Quantity máxima |
| --- | ---: | ---: | ---: |
| raid_shell_fragment | 45 | 1 | 3 |
| raid_ancient_scale | 25 | 1 | 2 |
| raid_minor_heal | 15 | 1 | 2 |
| raid_resonant_crystal | 8 | 1 | 2 |
| raid_major_heal | 4 | 1 | 1 |
| raid_basic_sword | 2 | 1 | 1 |
| raid_rare_relic | 1 | 1 | 1 |

LootTableId: `raid_test_route_loot`. Total de pesos: 100. No tiene restricciones
de rareza mínima activadas; la estructura admite una mínima explícita por entrada.
La selección usa SplitMix64 local a cada llamada, semilla ulong inyectable,
aritmética especificada y rechazo del sesgo de módulo. Siempre consume una tirada
para selección y otra para cantidad. Misma seed, orden de entradas y número de
tiradas producen exactamente las mismas definiciones y cantidades. Los IDs de
instancia nuevos siguen siendo únicos, no se vuelven deterministas ni reutilizan.
No se consulta ni modifica UnityEngine.Random global. No hay pity, suerte,
nivel, escalado dinámico ni economía definitiva.

Se rechazan IDs desconocidos, pesos no positivos, entradas duplicadas incluso
mediante alias, min/max inválidos, cantidades superiores a MaxStack, cantidades
distintas de 1 para categorías sin stacks y rarezas mínimas incompatibles.
El catálogo valida IDs únicos, alias sin cadenas, categorías y rarezas válidas,
reglas, datos por categoría, tags, presentación y una sola definición de loot
por definición nativa de arma.

## Persistencia SchemaVersion 1

Se mantiene SchemaVersion 1. Sólo se incorpora Quantity a los campos primitivos
ya aprobados. No se guardan rareza, categoría, efectos, tags, valores ni objetos
Unity: al cargar se resuelven desde las definiciones canónicas del catálogo.

El lector acepta exactamente el formato canónico aprobado sin Quantity, valida
su checksum sobre su contenido original e interpreta cada objeto como Quantity 1.
La carga no reescribe el archivo ni incrementa Revision. La siguiente transferencia
autoritaria completa guarda el formato con Quantity; sus IDs se conservan.
Se rechazan formatos incompletos, campos desconocidos, duplicados y checksums
incorrectos. Los DefinitionId desconocidos mantienen la recuperación segura,
preservación de archivos y bloqueo de escrituras del hito anterior.

El checksum detecta corrupción accidental; **no constituye protección antitrampas**.
El principal y backup, las escrituras atómicas, receipts y counters siguen bajo
la misma autoridad persistente. No se introduce un segundo stash ni backend.

Producción conserva `Application.persistentDataPath/Qusap/profile-v1.json`.
El playground fuerza en el Editor una carpeta temporal única si no se inyectó
otra. Pruebas y captura inyectan sus propias carpetas aisladas; no leen ni escriben
el perfil real. Ningún perfil de prueba o evidencia forma parte de los assets
definitivos. El fixture de formato antiguo usa exclusivamente valores anónimos.

## Escena y controles

`Assets/_Qusap/Scenes/RaidLootCatalogPlayground.unity` permanece fuera de Build
Settings. Reutiliza las dependencias runtime y jugadores existentes en una escena
aislada. La ruta fija presenta Shell 6+7, Scale 3, Crystal 2, Minor 1, Relic 1 y una
espada básica nativa. No altera escenas de producción ni prefabs principales.

ENTER inicia la sesión; A/D mueve P1 mediante sus controles existentes; G genera
ocho pickups de la seed visible; N muestra la secuencia de la seed siguiente;
TAB cambia selección; H consume el primer consumible; R mueve la reliquia al
bolsillo; B guarda el arma equipada si no hay un arma en mochila, o equipa la
primera arma en mochila si el slot nativo está libre; L recarga lógicamente el
perfil aislado. El HUD muestra metadatos, cantidad, reglas, tabla, seed, tiradas,
mochila, bolsillo, stash, revisión y validación. Estos componentes sólo se
referencian desde la nueva escena diagnóstica.

Los builders, grabadores, perfiles, capturas, videos, logs y resultados XML/JSON
son evidencia externa; no pertenecen a los archivos definitivos del hito.
Las pruebas afectadas cubren catálogo, stacking, consumibles, equipo nativo,
tablas deterministas, persistencia v1, extracción, muerte, stash y regresiones de
RaidInventory/RaidSession. La captura continua utiliza la extracción real de
cinco segundos y no cambia relojes ni duración de extracción.

No se realiza staging, commit, merge ni push antes de aprobación visual.
