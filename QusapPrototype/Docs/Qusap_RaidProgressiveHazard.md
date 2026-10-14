# Peligro progresivo de incursión: prueba diagnóstica

Base aprobada: 31d57c9712543f4472ddedaa561dfc7e85da1f83.
Rama de revisión: feat/raid-progressive-hazard. Sin commit ni publicación.

## Configuración provisional

La escena aislada es Assets/_Qusap/Scenes/RaidHazardPlayground.unity, fuera de
Build Settings. Desmarcar reducedDiagnosticTimes antes de Play para usar tiempos
normales. Los dos modos usan exactamente la misma autoridad de reloj, salud,
extracción, eliminación, lote de daño y victoria; solo cambia la configuración.
Todos los perfiles de esta escena son nuevos y permanecen en memoria.

| Etapa | Inicio normal | Inicio diagnóstico | Área segura X | Área segura Y | Daño/tick |
|---|---:|---:|---|---|---:|
| Inicial, sin daño | Running | Running | [-12, 12] | [-0.5, 11] | 0 |
| 1 | 240 | 6 | [-10, 10] | [-0.5, 10] | 10 |
| 2 | 300 | 12 | [-7, 7] | [-0.5, 9] | 12 |
| 3 | 360 | 18 | [-4, 4] | [-0.5, 8] | 15 |
| Final | 420 | 24 | [-2.5, 2.5] | [-0.5, 6] | 20 |

Segundos de gameplay compartido. Intervalo provisional: 1 s en ambos modos.
Aviso previo: 15 s normal, 3 s diagnóstico. Plazo: 480 s normal, 36 s diagnóstico.
Apertura de extracciones: 180 s normal, 2 s diagnóstico; cuenta física de 5 s.
No hay daño durante Deployment ni antes de la primera etapa. El primer tick es
un intervalo después del inicio: 241 normal / 7 diagnóstico. Los siguientes se
anclan al inicio de la primera etapa; no se reinician al cambiar de etapa ni al
entrar/salir. Se aplica la configuración correspondiente al instante de cada tick.

Las listas de etapas, límites, daños, intervalos y avisos están serializadas en
QusapRaidHazardPlayground. El modelo acepta otras configuraciones explícitas;
rechaza áreas inválidas, no anidadas, sin reducción, etapas desordenadas, tiempos
o daños no finitos e intervalos inferiores a 0.01 s. La última etapa debe preceder
al plazo de la sesión. Los defaults normales también están en
QusapRaidHazardSettings.Normal(). Los valores son provisionales, sin balance final.

## Autoridad y resolución

QusapRaidHazard solo consulta QusapRaidSession.Timing; nunca avanza otro reloj.
QusapRaidTimingDriver conserva orden -1000 y resuelve primero la expiración.
QusapRaidHazardDriver usa -900 y consume los ticks vencidos del reloj compartido.
Si la sesión ya expiró se descartan los ticks atrasados; la expiración tiene
prioridad incluso si un frame cruza simultáneamente un tick letal y el plazo.
Tras terminar no se aplican daños ni ticks nuevos. Las sesiones antiguas mantienen
su comportamiento mientras no se vincule explícitamente un peligro.

Solo son elegibles inventarios Active operables, del mismo roster y con receptor
vivo. La posición usada es el centro del Rigidbody, sobre X/Y, límites inclusivos;
Z se ignora y sus restricciones físicas no cambian. No se prueba la intersección
de todo el collider. El tick reúne los expuestos y llama a
QusapRaidSession.ApplyEnvironmentalDamageBatch, que reutiliza
QusapHitReceiver.TryReceiveEnvironmentDamage, sin knockback. El parry de combate
no interviene en esa ruta. No se añaden invulnerabilidades, motores ni animaciones.

La salud se agota sincrónicamente; el receptor existente elimina en LateUpdate.
QusapRaidSessionAuthority suspende EvaluateVictory durante el lote y conserva
los IDs agotados hasta aceptar sus outcomes Eliminated. Así no publica Winner
entre callbacks ni mientras haya una baja ambiental pendiente de settlement.
La suspensión NO usa ExpirationStarted. Al confirmar la última baja pendiente,
la regla existente concede Winner si queda un Active; con cero termina sin
Winner ni Cuerno, motivo AllParticipantsResolved. La expiración sigue usando
Expired. Reentrar en el reloj o extraer desde un callback del lote se rechaza.

Se reutilizan SettleDeath, receipts, reservas, bolsillo y préstamo. La garantía
es atómica por perfil: confirmar P1 y rechazar P2 no revierte P1. Los rechazados
no operan ni vuelven a recibir daño. El driver reintenta mediante
RetryDeathSettlement; un .tmp rechazado conserva el bloqueo de persistencia
existente. No lo elimina ni lo promueve automáticamente. La sesión puede seguir
pendiente mientras persista ese fallo. Al llegar al plazo se reutiliza el lote
de expiración y sus reintentos. No se afirma una transacción global ni recuperación
del estado de este lote tras reiniciar el proceso.

## Extracción y presentación

La salida cyan en X=8.8 no da inmunidad. Está dentro de la primera área segura y
fuera desde la etapa 2. Un daño positivo publica HealthChanged: la extracción
actual se cancela con Damage y progreso cero; seguir dentro no la reinicia.
Hay que salir y volver a entrar, respetando la ventana autoritativa y cinco
segundos completos. Si el daño agota salud se aplica la eliminación existente.
Los terminales y pendientes no reciben operaciones nuevas del peligro.
Winner conserva el texto No aplica · Winner.

Verde: límite actual. Amarillo: próxima área durante el aviso. Rojo oscuro:
espacio peligroso. Los elementos de peligro solo tienen renderers; no modifican
colliders ni eliminan plataformas. El suelo y las tres plataformas centrales
permanecen conectados dentro de la zona final, mediante las rutas de salto
existentes; se mantienen también las plataformas exteriores del suelo.
El HUD muestra reloj, siguiente cambio, ambos límites, aviso, exposición, HP,
daño acumulado por participante, daño por tick, extracción, pendientes y ganador.

## Prueba manual

Abrir la escena y pulsar Iniciar incursión. P1: A/D y salto/dash existentes;
P2: gamepad. Ambos empiezan con 100 HP, mochila, bolsillo y arma de préstamo.
No se prepara salud reducida ni hay botones de daño o victoria.

En diagnóstico, observar aviso en 3 s, cierre en 6 s y primer daño en 7 s.
Mantener P1 fuera hasta después de 9 s y moverlo con A al centro: el daño cesa
al entrar. Dejar P2 fuera permite ver su muerte ambiental y la victoria de P1.
Para ver todos los cierres, mover ambos al centro antes del primer tick y observar
6/12/18/24; continuar hasta 36 para expiración colectiva. Para extracción, probar
la salida después de abrir y recibir daño cuando quede fuera del área segura.
Una nueva incursión usa actores y perfiles nuevos.

## Validación y límites

QusapRaidHazardEditModeTests cubre configuración, límites, ticks a 30/60/144 fps,
catch-up, reentrada, callbacks, rosters 2–8, orden inverso de bajas, superviviente,
persistencia rechazada antes/después de staging y prioridad de expiración.
QusapRaidHazardPlayModeTests cubre la escena con salud real, LateUpdate, movimiento
nativo, interrupción de extracción, parry, fin y expiración.
Resultados, comparación de diagnósticos y video se conservan en la entrega externa.
Entorno: Unity 6000.5.7f1 y Burst resuelto 1.8.30. Los cambios automáticos del Editor
se conservan aparte y no son candidatos.

Un frame tardío procesa ticks debidos con las posiciones actuales muestreadas;
no reconstruye recorridos históricos ni entradas/salidas entre muestras. La
independencia del framerate prueba los mismos tiempos y exposición muestreada;
no implica trayectorias físicas idénticas entre tasas distintas. El modo diagnóstico
reduce tiempos de etapa/aviso/plazo y conserva el intervalo de daño para hacerlos
visibles: no representa una escala temporal uniforme ni balance de producción.
El mapa es una prueba de conectividad y desplazamiento, no demuestra balance de
encuentros multijugador. No implementa bots, networking, arte o mapa final, torneos,
Trono ni economía de Cuernos.
