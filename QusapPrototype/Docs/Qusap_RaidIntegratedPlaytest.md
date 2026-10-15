# Incursión integrada jugable — primer playtest local

Base: a19693d9d7086fdd81c6366aa9e78f28610abf52. Escena aislada:
`Assets/_Qusap/Scenes/RaidIntegratedPlaytest.unity`, fuera de Build Settings.
Este hito prueba integración y un primer recorrido libre para dos participantes.
El blockout, la distribución de loot y los tiempos reducidos son diagnósticos;
no representan mapa, balance, frontend o presentación finales.

## Jugar

Abrir el proyecto del worktree nuevo con Unity 6000.5.7f1. Conectar el gamepad
antes de pulsar Play: el lector nativo empareja el primer gamepad al crear el
actor P2. Si se conecta después, detener y volver a iniciar Play. P1 usa teclado.
No abrir una escena de producción para ejecutar esta prueba.

En preparación, elegir una espada propia o el préstamo de incursión, y hasta
dos stacks de consumibles completos. También se puede usar el ratón en ambos
paneles. La selección no retira objetos; confirmar ambos participantes inicia
el deployment compartido y sólo después Running y el reloj.

| Acción | P1 teclado | P2 gamepad |
|---|---|---|
| Seleccionar fila de preparación | Flechas arriba/abajo | D-pad arriba/abajo |
| Alternar objeto seleccionado | Enter | A / botón sur |
| Listo / volver a editar | P | Start |
| Alternar preset antes de estar listos | R | Select |
| Movimiento | A/D | Stick izquierdo o D-pad horizontal |
| Salto / bajar plataforma | Espacio / S | A / D-pad abajo |
| Dash | Shift izquierdo | RB |
| Golpe corporal / espada ligera / cabeza | J / K / L | X / Y / LB |
| Espada fuerte | Sin binding de teclado en el asset actual | B / botón este |
| Lanzar o cambiar arma nativa | E | RT |
| Parry | Sin binding de teclado en el asset actual | LT |
| Abrir loot cercano | Mantener F | Mantener D-pad arriba |
| Elegir objeto del loot abierto | [ / ] | Stick derecho izquierda/derecha |
| Tomar stack / unidad | T / Y | R3 stack |
| Cerrar loot | C | Alejarse o abrir detalles |
| Proteger primer objeto elegible | B | L3 |
| Consumir primera curación disponible | H | Select |
| Abrir/cerrar detalles | Tab | Start |
| Diagnóstico opcional de LT/parry | F8 o botón «LT diagnóstico» | Panel visible para el mando emparejado |
| Preparar siguiente incursión tras resultados | N | A |

Con los detalles abiertos se puede seleccionar un slot de mochila con las
flechas arriba/abajo de P1 o el stick derecho horizontal de P2. Enter/R3 equipa
la espada seleccionada, usa el consumible o protege el material, según categoría.
Guardar el arma equipada requiere Z / stick derecho arriba; sacar el bolsillo
requiere X / stick derecho abajo. Equipar con el slot ocupado se rechaza: primero
hay que guardar o lanzar el arma actual. También están los botones explícitos de
inventario. La mochila se desplaza dentro del panel cuando está llena.
Los detalles cierran la presentación de loot de ese participante para que R3
no tome del mundo y actúe sobre inventario en el mismo frame. El movimiento
continúa y no hay pausa ni inmunidad mientras se consulta el panel.

## Mapa y recorridos

Se inspeccionaron los blockouts Fase1_Quadrant_BottomLeft y RelicCombatArena.
Se reutiliza QusapOneWayPlatform.prefab y los materiales existentes de arena
mediante referencias y overrides de la escena nueva. No se modifican sus fuentes,
ni se recuperan fases, portales o una reliquia central del diseño antiguo.

El suelo de 24 unidades conecta los spawns simétricos X=-10.5/+10.5 y las salidas
X=-9/+9. Las dos aproximaciones superiores pasan por X=±8.5, ±5.5 y ±2.8.
Los centros de sus plataformas están en Y=2.1/3.55/4.9, con espesor 0.1.
Así queda paso por el suelo bajo el primer nivel, sin cambiar el collider del actor.
Las cajas inferiores están en X=±7.5; los consumibles en ±3.8, y las cajas
superiores en ±5.5. Hay una caja de material en la plataforma central intermedia.
Todos requieren apertura y toma explícitas; no hay recogidas al pasar.

Para subir, saltar desde un lateral de la primera plataforma, por ejemplo desde
el spawn, y después dirigir el salto hacia su centro. Si se ha caminado debajo,
volver al borde lateral para iniciar la aproximación. Se conserva la política
nativa de plataformas de sentido único. No se modifica para esta escena.

La zona final conserva suelo, peldaños X=±1.65 de ancho 1.6, plataforma central
intermedia de ancho 1.7 y puente superior de ancho 2.5. El hueco central de 1.7
unidades permite despegar desde X=0 hacia cualquiera de los peldaños sin tocar
un techo bajo al inicio del salto. Del peldaño se salta hacia la plataforma
central y luego al puente superior. S / D-pad abajo permite volver al suelo.
Pulsar descenso sólo sobre una plataforma: el lector nativo conserva una orden
no consumida si se pulsa estando en el suelo, y puede aplicarla al aterrizaje
siguiente. Es un comportamiento observado del sistema existente; esta escena
no cambia su buffering ni añade cancelaciones de input.
El centro físico del actor en el puente queda aproximadamente en Y=5.95,
dentro del límite final Y=6; el peligro conserva el muestreo del centro del Rigidbody.
No desaparecen plataformas al cerrar: cambian las áreas seguras y se aplica el
daño ambiental aprobado. Las rutas de retirada siguen existiendo.

La cámara compartida existente sigue ambos actores con límites exclusivos de
esta escena. HUD superior: reloj, aviso, HP, carga y extracción. Franja inferior:
interacción de loot y controles. Detalles y resultados sólo se expanden al pedirlos
o al finalizar. Loot usa cajas y rótulos ámbar, salidas cyan y peligro rojo con
borde seguro verde y aviso amarillo. El HUD Winner muestra `No aplica · Winner`.

## Autoridades y continuidad

QusapRaidIntegratedPlaytest compone las autoridades existentes: dos repositorios
QusapPersistentStashRepository con QusapMemoryProfileFiles independientes,
QusapRaidDeploymentAuthority, un QusapSharedRaidContext, QusapRaidBootstrap,
la competición de RaidSession y los drivers existentes de reloj y peligro.
No tiene inventario, reloj, catálogo, daño o settlement alternativos.

Cada perfil nuevo recibe, una sola vez, una espada azul propia y curaciones de
las definiciones ya aprobadas. El origen se identifica como preparación
diagnóstica. No se escriben perfiles a disco ni se consulta persistentDataPath.
Se mantienen las mismas instancias de repositorio entre incursiones. Una muerte
puede perder la espada propia: la preparación siguiente no la repone. El préstamo
de deployment continúa usando la política RaidLoaner y no puede persistirse.

QusapLocalWorldLootAuthority y QusapLootInteractionAuthority controlan las cajas,
cantidades, versiones e identidades. Los sensores e interactores existentes
validan distancia, visibilidad, daño, ataques y estado. Sus recetas sólo combinan
definiciones existentes; las armas seleccionables y obtenibles son espadas. El
catálogo global y sus demás definiciones permanecen intactos.

La única extensión del bootstrap de armas es un filtro opcional de solicitud de
pickup. Esta escena exige F / D-pad arriba para candidatos de espada caída; las
demás escenas mantienen el comportamiento anterior con filtro null. Se reutilizan
el resolver nativo, distancia, lockout del dueño anterior, orden y confirmación.
No se crea un segundo propietario ni se cambia daño, gravedad, motores, colliders
del personaje o animaciones.

El bootstrap original materializa los contenedores de muerte. La escena los
proyecta sobre la autoridad de loot existente cuando la sesión todavía admite
operaciones. Con dos participantes, una eliminación que deja un Active resuelve
Last Survivor inmediatamente; el contenedor no abre una ventana de saqueo tras
Winner. Se conservan receipts, reservas, préstamos, bolsillo y reintentos.
Los settlements siguen siendo atómicos por perfil, sin promesa de transacción global.

| Configuración | Normal | Reducida, diagnóstica |
|---|---:|---:|
| Plazo colectivo | 480 s | 120 s |
| Apertura de ambas salidas | 180 s | 20 s |
| Cuenta física de extracción | 5 s | 5 s |
| Cierres | 240 / 300 / 360 / 420 | 60 / 75 / 90 / 105 |
| Aviso | 15 s | 5 s |
| Intervalo de daño | 1 s | 1 s |
| Daño por etapa | 10 / 12 / 15 / 20 | 10 / 12 / 15 / 20 |

Las salidas comparten sesión y reloj y sus volúmenes están separados 18 unidades.
No hay progreso antes de la apertura ni reinicio automático después de daño;
hay que salir y volver a entrar. La salida no protege del peligro. Expiración
colectiva no concede Winner ni un Cuerno nuevo.

Al finalizar se conservan resultados y stash. N/A crea actores, cámara, mundo,
deployment y RaidId nuevos conservando los perfiles y sus desbloqueos. Se reserva
el allocator nativo más allá de los IDs emitidos antes de crear nuevas armas.
Reloj, peligro, loot abierto, input, extracción y actores no se reutilizan.
No se permite preparar otra incursión si quedan settlements o deployments pendientes.

## Validación y límites

QusapRaidIntegratedEditModeTests comprueba aislamiento, dependencias, presets,
salidas, catálogo y referencias seguras. QusapRaidIntegratedPlayModeTests añade
integración de preparación, recogida real por dispositivos nativos, bolsillo,
equipamiento, consumibles, ambas salidas e interrupciones, peligro, expiración,
conservación entre incursiones y recorridos nativos después de cada cierre.
Las pruebas usan perfiles aislados, dispositivos de prueba y clocks del fixture;
los saltos y recorridos se conducen con los controles originales sin teletransportes.
Los demás tests de estados pueden situar actores para aislar una condición concreta.

La entrega externa contiene resultados, diagnósticos, plano, capturas y video
continuo. Builders, grabadores, automatización y evidencia no son dependencias de
la escena ni candidatos. Los cambios automáticos de importación y las
normalizaciones de checkout se documentan aparte y permanecen excluidos.

Límites: sólo dos jugadores locales, primer gamepad emparejado al crear actores,
perfiles RAM por sesión de Play y cámara compartida que reduce el detalle al
separarse. No hay persistencia entre cierres del Editor ni balance demostrado.
El peligro mantiene la política aprobada de procesar ticks vencidos con posiciones
actuales muestreadas. No se implementan networking, bots, arte final, torneos ni Trono.

## Corrección tras prueba humana: D-pad arriba y parry

D-pad arriba también llegaba a `QusapDoubleLGroundAttackPresenter.Update`, fuera
del InputActionAsset: su atajo diagnóstico solicitaba `RequestRoll()` y mostraba
`RollRMFrontInPlace`. Era presentación adicional, sin Root Motion ni impulso físico.
Esta escena desactiva exclusivamente esos atajos de revisión en sus dos instancias.
El prefab y las escenas anteriores conservan el valor predeterminado activado;
`RequestRoll`, movimiento, salto, dash y combate siguen disponibles según sus reglas.
No se rehacen clips, rig ni Animator. La recogida mantiene F / D-pad arriba y toma explícita.

LT está enlazado a `Gameplay/Parry`, grupo Player2. Es una acción Button sin
interacción de mantenimiento: se ejecuta una vez al superar el umbral del gatillo,
no otra vez por mantenerlo. Hay que soltar por debajo del umbral de liberación y
volver a pulsar. El panel muestra el umbral efectivo del mando; en el entorno
validado es 0,50, con liberación a 0,375 (factor 0,75). No se modifica ese binding.

**El parry actual sólo admite finalizadores de combo armados** (Damage, Disarm y
Launch), no golpes normales, proyectiles, daño ambiental ni una guardia mantenida.
Un intento sin finalizador se recibe y falla con `NoIncomingFinisher`.
«Aceptación de estado» en el panel significa que el controlador aceptó un intento,
no que haya un ataque elegible ni que el parry haya tenido éxito.

Los tiempos reales, conservados, son 0,10 s desde armar hasta abrir la ventana;
0,35 s de ventana (de +0,10 a +0,45); recuperación de intentos 0,50 s desde la
última pulsación procesada, incluso si se rechaza por recuperación; vulnerabilidad
del defensor tras fallo 0,30 s y del atacante tras éxito 0,60 s.
Una pulsación temprana consume la oportunidad contra ese finalizador: mantener LT
o pulsarlo otra vez no rescata ese mismo ataque. Se rechaza también por ataque
propio, dash, hitstun, combate desactivado o entrada bloqueada. No se exige orientar
al defensor hacia el atacante; no se amplían ni restringen estas reglas aquí.

F8 o «LT diagnóstico» abre una observación opcional, sin enviar acciones ni armar
combos. Muestra LT analógico y pulsación/liberación, dispositivo observado frente
al realmente emparejado, callback de la acción, comando recibido, aceptación,
resultado legible, finalizador entrante, ventana temporal y oportunidad disponible,
y resolución de éxito/fallo. Una conexión posterior a Play puede producir entrada
física observable sin emparejamiento nativo: conectar antes de Play y reiniciar Play.
Los contadores se reinician al crear actores para otra incursión; no afectan al perfil.

Para repetir con mando físico: conectar antes de Play, confirmar ambos jugadores
y abrir F8. Mantener/soltar/repetir D-pad arriba primero lejos y después junto a
una caja: sólo debe actuar el loot, sin rodada. Mantener y soltar LT sin ataque
debe mostrar una pulsación, un `NoIncomingFinisher` y un fallo; mantener no repite.
Esperar al menos 0,60 s sin pulsar, soltar completamente y repetir para ver otro intento.
Para éxito: situar P1 a unos 1,1–1,3 m a la izquierda de P2, mirando hacia él;
P1 pulsa J, K, J, con aproximadamente 0,35–0,40 s entre pulsaciones, y los dos
primeros golpes deben alcanzar al mismo rival. Ajustar la aproximación si el
knockback separa a los actores. En la tercera entrada se arma Launch. P2 pulsa LT
una vez al aparecer la ventana cyan/«ABIERTA», aproximadamente +0,15 s desde el armado.
Debe aparecer «Éxito resuelto · Launch», sin aplicar el finalizador. No probar LT
contra los dos golpes de preparación esperando un bloqueo normal.

No se aplica rebalanceo. La señal existente mide 0,28–0,38 unidades de mundo y sólo
se dibuja durante la ventana: con la cámara compartida puede ser pequeña. Se propone
como siguiente ajuste visual medir 0,45–0,60, manteniendo 0,10/0,35/0,50 s; requiere
evaluación humana y no se incorpora en esta corrección. El panel aclara entretanto
la diferencia entre no detectar LT, estar bloqueado y pulsar contra un ataque no elegible.
La reproducción también encontró un defecto de timestamp: un evento real dentro
de una ventana abierta podía preceder la última muestra de física y ser descartado
como `InvalidTimestampIgnored`. La máquina de parry ahora acepta ese evento fresco
si su timestamp pertenece a la ventana que sigue abierta, sin retrasar el reloj,
ampliar la ventana ni reabrir fases vencidas, canceladas o resueltas. Se mantienen
IDs únicos, gate de recuperación y un intento por finalizador. Una regresión reproduce
el rechazo anterior y comprueba el caso corregido y el rechazo tras expiración.
Las suites completas 729/729 y 617/617 anteriores corresponden al candidato previo;
la corrección tiene sus propios hashes y resultados de pruebas afectadas en la entrega externa.
