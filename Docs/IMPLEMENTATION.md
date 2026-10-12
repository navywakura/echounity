# ECHO Station · Unity isométrico

Objetivo autorizado: crear y abrir una estación jugable de cinco zonas en Unity
6000.0.84f1, reutilizando el pack descargado, con robot 3D original, movimiento
anticipativo flexible, misiones y observabilidad real conectada a ECHO-5.

Proyecto separado de las fuentes congeladas de ECHO-5. No modifica contratos,
campañas ni módulos históricos. No usa LLM ni descarga pesos. El puente importa
el planificador y el Recorder existentes mediante una ruta explícita de lectura.

## Arquitectura y tareas

- [x] Importar PNG/FBX ordinarios del AFPack (ZIP64 con relleno final), con hashes.
  Conservar autoría y README; uso local. No publicar los assets descargados.
- [x] `Assets/Echo/Runtime/PredictiveMotor.cs`: controlador cinemático con
  `SetTarget(Vector3)`, `Stop()`, `HasTarget`, `Arrived`, `Speed`, `Blocked`,
  `ShowPrediction`. Trayectoria curvada de cápsulas delante/detrás, aceleración,
  freno anticipado, deslizamiento y comprobación final del desplazamiento.
- [x] `RobotVisual.cs`: robot original generado con geometría Unity; marcha
  procedural, pies apoyados, brazos y mirada desacoplados de la política.
  `RobotVisual.Build(Transform parent)` devuelve el componente.
- [x] Escena persistida `Assets/Scenes/EchoStation.unity`: cámara ortográfica
  isométrica, cinco habitaciones, muebles del pack, colliders, iluminación,
  objetos con interacciones verificables y HUD. Controles manuales y automáticos.
- [x] Puente Python local 127.0.0.1:8770: observación parcial estructurada,
  mapa recordado, objetivos explícitos, invocación real de
  `echo5.interaction.HypothesisAgent.plan`, presupuesto y `Recorder` existente.
  No nueva red neural ni thought-bus. La física sigue siendo autoridad sobre llegada.
- [x] Tres escenarios: riego, transporte y ruta bloqueada. El adaptador de tareas
  y los efectos de movimiento son conocimiento declarado; no afirmar aprendizaje
  autónomo de reglas que se han escrito en el adaptador.
- [x] Pruebas: colisión frontal/trasera/esquina, llegada, presupuesto, observación
  parcial, respuesta ausente/antigua, rechazo de datos privados, misiones completas,
  compilación Unity, captura de escena y diario real. Documentar resultados.

## Decisiones

2026-10-11: la autorización del usuario incluye construir y abrir el proyecto;
se continúa la implementación aprobada sin solicitar otra confirmación.
El puente es JSON por loopback para un controlador externo existente; ML-Agents
no es necesario para este prototipo. El mapa lógico es de hasta 16×16 celdas,
respetando el dominio del planificador heredado. Las posiciones de objetos solo
se entregan cuando son visibles. El visor puede mostrar la escena completa al
humano, diferenciando lo conocido por el robot.

El éxito en esta demo verifica integración y misiones concretas. No certifica
transferencia, ausencia universal de alucinaciones, cierre ECHO-5 ni S6.

La revisión detectó y corrigió cuatro problemas de registro y control: confirmar
llegadas usando solo la celda redondeada, perder la consecuencia al pausar, omitir
el coste de movimientos manuales e impedir reintentos al mismo destino tras un
bloqueo. La respuesta física incluye ahora sesión, secuencia, acción, objetivo,
estado y distancia residual. Toda salida automática cierra su sesión con la
última observación. Las cápsulas anticipan; el movimiento observado confirma.

El primer obstáculo de demostración estaba fuera de la ruta utilizada. Se conserva
el recibo inicial como `Logs/initial-integration-tests.json`. La prueba vigente
lo coloca en `(5,9)` y exige observarlo, recorrer una alternativa y no cruzarlo.
Las pruebas físicas iniciales también revelaron un problema en el fixture de
escena de edición y otro en la liberación de materiales; ambos se corrigieron
y sus recibos iniciales se conservaron.

Validación final: 37 pruebas Python, 22 comprobaciones del mundo, 12 físicas y
3/3 misiones reales Unity + ECHO. Las 251 acciones y 1.016 eventos de esos tres
diarios se verificaron mediante la cadena del Recorder existente y los recibos
físicos. Detalles y límites en `Docs/VERIFICATION.md`.

- [x] Vista Play abierta y revisada visualmente en la pantalla real; conexión local
  activa y panel sin datos ficticios antes de la primera decisión. Captura en
  `Logs/control-panel.png`; recibo en `Logs/ui-review.json`.
