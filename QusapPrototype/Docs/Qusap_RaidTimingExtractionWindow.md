# Raid Timing y apertura de extracciones

Base: a2f93066df0c4557dac1ff3e3f6fadf00d4e3c0f. Rama de revisión:
feat/raid-timing-extraction-window. Sin commit, push ni integración.

## Decisiones y alcance

Incursión de 2 a 8 participantes, una vida por raid. Duración normal configurable
480 segundos; extracciones desde 180; permanencia física existente de 5 segundos.
El último Active gana inmediatamente antes o después de abrir las salidas. Extraer
no concede cuerno. Al alcanzar el plazo, los Active se eliminan colectivamente,
conservando solo el Bolsillo Seguro; no hay Winner ni concesión de Cuerno por
expiración. Resultados y desbloqueos ya confirmados permanecen.

Dirección posterior aprobada: peligro progresivo desde el minuto 4 y cierre hacia
el minuto 8. No se implementan aquí peligro ni modificaciones del mapa.
Decisión de lanzamiento: únicamente espadas como armas. Se registra esta decisión
sin retirar assets ni cambiar catálogo, estadísticas o animaciones.

## Autoridad y tiempo

QusapRaidTimingSettings valida valores finitos: duración positiva y apertura
entre cero y la duración, excluyendo el plazo final. Su constructor normal es
480/180. QusapRaidTiming pertenece a QusapRaidSession y solo esta lo avanza.
QusapSharedRaidContext(settings, participantes) habilita la política temporal.
Las rutas anteriores sin settings mantienen su comportamiento histórico, para
que escenas de otros hitos no se conviertan en demostraciones temporizadas.

El reloj se crea durante Prepared pero empieza únicamente tras completar todos
los journals Running y cerrar el roster de la competición. No corre durante
Deployment y se detiene al finalizar. AdvanceTime permite pasos deterministas,
rechaza valores inválidos y reentrada durante operaciones reservadas. Cada raid
tiene un reloj; los participantes no suman tiempo por separado.

QusapRaidTimingDriver avanza tiempo de gameplay mediante Time.deltaTime, con
orden -1000 antes del Update de input, loot y extracción. No modifica timeScale,
fixedDeltaTime, movimiento, Root Motion ni las físicas. El plazo se aplica en
el paso autoritativo: al alcanzar duración, se cierra extracción y se bloquean
operaciones antes de ejecutar callbacks o settlements.

La ventana se comprueba en TryBegin, el Update y Resolve de extracción y,
principalmente, en QusapRaidSession.SettleExtraction. No depende del HUD. La
resolución Winner omite solo la ventana de extracción, conservando las demás
garantías; la expiración bloquea también ese settlement.

El volumen recuerda quién entró antes de abrir y permite un primer intento desde
cero al abrirse. Esto no reinicia automáticamente una cuenta interrumpida por
daño o salida: se mantienen las reglas de reentrada. En sesiones temporizadas,
cada componente de extracción consume el incremento observado del reloj
compartido, una sola vez; el tiempo anterior a comenzar no aporta progreso.

## Expiración y persistencia

La autoridad fija ExpirationStarted antes de resolver a nadie y suprime Winner
durante todo el lote y sus reintentos. Se bloquean todos los inventarios Active
antes del primer callback. Se reutiliza SettleDeath para bolsillo, equipamiento,
préstamos, Death Loot y receipts; FinishSession elimina cargo de muerte abandonado.
El motivo terminal Expired se añade sin renumerar los motivos anteriores.

La garantía sigue siendo atómica por perfil, no una transacción entre perfiles.
Si una escritura falla, ese jugador queda bloqueado y pendiente; los settlements
confirmados de otros jugadores se conservan. No se publica Finished mientras
queden resoluciones pendientes y nunca se convierte ese último pendiente en
Winner. AdvanceTime(0) permite un reintento determinista; el driver reintenta
en los pasos siguientes. Un .tmp rechazado mantiene el bloqueo y la recuperación
controlada existente; no se borra ni promueve automáticamente.

La retirada del receptor se realiza después de aceptar el settlement, para que
el equipamiento nativo pueda liberar/restaurar el arma durante un fallo. Antes
de eso se usan el daño ambiental y el bloqueo de eliminación existentes.

No cambia schema 3 ni el almacenamiento del cuerno. Una concesión confirmada en
otra raid permanece tras expirar; una victoria rechazada antes del plazo no
puede completarse como Winner después del plazo.

## Escena diagnóstica y prueba manual

Assets/_Qusap/Scenes/RaidTimingPlayground.unity permanece fuera de Build Settings.
Sus actores y assets vienen del template versionado; no depende del builder ni
de grabadores externos. Se preparan perfiles independientes en memoria, mochila
y bolsillo mediante operaciones existentes. No se prepara salud a 1 HP ni se
selecciona ganador desde la escena. Los botones solo inician nuevos deployments.

Valores reducidos visibles: A 15 segundos / apertura 2; B 6 segundos / apertura 2.
La extracción conserva 5 segundos. A: iniciar, mover P1 con D hasta la salida y
permanecer: antes de abrir no hay progreso; luego cuenta cinco segundos, P1
extrae y P2 gana. B: nueva raid con perfiles nuevos, mantener ambos fuera de la
salida y esperar; expiran sin ganador, con bolsillo y sin cuerno.

Para tiempos normales, desmarcar reducedDiagnosticTimes en el componente antes
de Play. Se utilizan 480/180/5 sin modificar assets de producción. El cambio
del Inspector puede ser transitorio; no guardar otro preset sobre la escena
de revisión sin indicarlo. P1 usa teclado A/D; P2 gamepad, controles existentes.

El HUD muestra configuración, elapsed/restante/apertura, estado de salidas,
cuenta física, roster, estados, ganador, préstamos, stash, cuerno y motivo final.
Los casos usan perfiles nuevos para que B demuestre ausencia de nueva concesión.

## Validación y evidencia

Pruebas definitivas nuevas: QusapRaidTimingEditModeTests y
QusapRaidTimingPlayModeTests. Comprueban inicio, configuración, API, apertura,
cinco segundos, interrupciones, victoria antes/después de apertura, rosters 2–8,
plazo exacto, bolsillos, terminales, cuerno previo, idempotencia, fallo antes y
después de staging y reentrada. Unity 6000.5.7f1; Burst resuelto 1.8.30.

Los resultados exactos, Console, video, diffs automáticos y estados completos se
conservan externamente. Builders, grabadores, logs y cambios automáticos de
importación quedan fuera del candidato. No se han leído perfiles reales.
