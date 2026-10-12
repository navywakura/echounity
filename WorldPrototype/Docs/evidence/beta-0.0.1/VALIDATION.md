# Verificación del entorno local · 11 de octubre de 2026

Estado: entorno local preparado y probado en **Unity 6000.0.84f1** sobre Linux.
Se completaron **33 grupos/comprobaciones en editor** y **12 verificaciones
durante Play**, con recibos `valid: true` y sin fallos registrados por el arnés.
Esto verifica la preparación del juego; no modifica el estado científico de
ECHO-5 ni acredita capacidad de un servidor MMO.

![Captura real del prototipo durante Play](evidence/world-prototype.png)

La batería de editor pasó a las 03:29:59 UTC (05:29:59 en Madrid). Incluye 18
grupos de datos y rutas, 11 de geometría/streaming, 3 de juego/cámara y creación
de escena. Se comprobaron semilla y orden de generación, coordenadas negativas,
límites del mundo, 400 alturas, edición y recarga, rechazo de archivos dañados,
guardado atómico, A* acotado, caras expuestas y colliders, origen móvil, descarga
de chunks, inventario y chat. El recibo completo está en
[verification.json](evidence/verification.json).

La ejecución gráfica pasó a las 03:30:38 UTC (05:30:38 en Madrid). Recorrió una
ruta de siete bloques, colocó y retiró un bloque recuperando el inventario,
viajó a `(1.000.000, −1.000.000)` en el plano horizontal, comprobó las colisiones
inmediatamente y regresó. Se generó un mensaje real mediante el sistema de
chat y se capturó su burbuja sobre el humanoide. Véase
[play-smoke.json](evidence/play-smoke.json).

En el punto final había **25 meshes de columna**, **49 columnas de datos** y
**55.924 vértices**. El Profiler de Unity devolvió **213.265.279 bytes** asignados
(aproximadamente 203,4 MiB, con el editor en ejecución). La ruta tardó **1,558 s**.
Son observaciones de esta sesión y escena, no mediciones de un ejecutable
optimizado ni garantías de rendimiento para otros equipos o para 200 usuarios.

Durante la revisión se corrigieron una invocación de prueba incompatible con
Edit Mode, la aceptación silenciosa de guardados dañados, el desfase de cámara
al cambiar el origen, la sincronización de colliders al teletransportar y un
solapamiento de mensajes con el campo de entrada. El verificador ahora recoge
también los errores y aserciones que Unity emite sin lanzar una excepción C#.
El editor imprime además avisos de integración del escritorio Linux en su log;
los recibos no se presentan como ausencia universal de mensajes del motor.

La prueba gráfica automatizada llama al mismo método de destino que utiliza
el clic; no inyecta teclado/ratón del sistema operativo. WASD, Enter, la rueda y
los botones están conectados a la entrada de Unity y se documentan para la
prueba manual. No se han ensayado todavía sesiones de varias horas, múltiples
clientes, reconexión de red ni NPC ECHO.

- [x] Proyecto y escena independientes del sandbox anterior.
- [x] Humanoide procedural y cámara isométrica.
- [x] Movimiento WASD y selección de destino por clic implementados.
- [x] Chat local con burbuja, límite de longitud y pausa de navegación.
- [x] Mundo sembrado, chunks 16×16 y alturas −200…199.
- [x] Streaming acotado y origen móvil comprobados.
- [x] Colocar, extraer y fabricar un faro con inventario local.
- [x] Persistencia del terreno y rechazo de archivos incompatibles.
- [x] Recibos reproducibles y captura real.
- [ ] Inventario/posición persistentes, supervivencia y facciones.
- [ ] Servidor autoritativo y chat entre jugadores.
- [ ] NPC conectados a ECHO-5 y evaluación de su conducta.
- [ ] Ensayo con 200 clientes antes de anunciar ese aforo.

Los hashes de los archivos ejecutables y la configuración comprobada están en
[source-sha256.json](evidence/source-sha256.json). Los logs completos permanecen
locales en `Logs/verify-20261011T032952Z/` y `Logs/smoke-20261011T033025Z/`.
