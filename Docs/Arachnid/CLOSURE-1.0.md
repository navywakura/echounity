# ECHO-Station 1.0 — cierre de las cinco fases

Fecha: 12 de octubre de 2026. **VERDE dentro del contrato de ECHO-Station 1.0.** La tanda completa terminó a las 19:21:41 (Europe/Madrid), en 5 minutos y 48 segundos. `status.json` y `audit-final.json` son válidos.

Ejecución de referencia: `Artifacts/Closure/20261012-191553-460628/`.
Este informe describe la ejecución original. La copia pública y sus cambios de empaquetado se verifican por separado en [PUBLICATION-20261012.md](../PUBLICATION-20261012.md).

Contrato previo: [CLOSURE-1.0-PLAN.md](CLOSURE-1.0-PLAN.md).

## Checklist cumplido

- [x] **1.1 Segundo encuentro:** PhysX registra un choque inicial; proponer el mismo comando desde el mismo origen después produce `wait`, cero movimientos y cero choques. Con CAM desactivada y el mismo checkpoint inicial, el control ejecuta `move` y choca una vez. Se evita un intento fallido en este ensayo.
- [x] **1.2 Contexto seguro similar:** el mismo comando en otra coordenada despejada permite un movimiento físico confirmado, sin veto.
- [x] **1.3 Extinción:** retirar el obstáculo aporta profundidad nueva; el agente cruza la celda antes bloqueada. El episodio original permanece y `destroyed == 0`.
- [x] **1.4 Persistencia:** serialización validada de las 4.096 posiciones; reinicio real del proceso, restauración y veto antes del segundo choque. La prueba usa un puerto loopback aislado; el servicio normal 8771 utiliza el mismo almacén y adaptador. Se conserva un checkpoint motor adquirido por movimientos reales para mantener el significado del comando.
- [x] **2.1 Cuerpo unificado:** riego, entrega y desvío completados en PhysX con el prefab del arácnido y frames reales de sus patas IK: **3/3**.
- [x] **2.2 E[6]/CAM en Station:** batería, misión y puerta sin llave enlazadas con observaciones; recibos WSP16 y episodios auditados. Las pruebas de componente incluyen batería baja y refutación al obtener llave.
- [x] **3.1 Inspector causal:** clic real en navegador sobre un veto, cinco nodos y extracto histórico con SHA-256 comprobado. [Captura](../../Evidence/2026-10-12/Logs/Closure/trace-inspector.png) · [Recibo de la comprobación](../../Evidence/2026-10-12/Logs/Closure/trace-ui-verification.json).
- [x] **3.2 D y B:** etiquetas visibles «sin transductor físico asignado».
- [x] **3.3 LIF:** etiqueta de diagnóstico pasivo y `drives_policy = false`.
- [x] **4.1 Recursos:** medidor externo del PID Python; pico RSS observado **35.078.144 bytes (35,08 MB)**, inferior a 60.000.000. Véase el desglose siguiente.
- [x] **4.2 Offline:** misión completa dentro de namespace solo-loopback; **0 paquetes externos transmitidos**, red del anfitrión intacta.
- [x] **5.1 Confirmación:** **16/16** mapas nuevos desde cero, cuatro familias con 4/4 cada una; cero bloqueos irrecuperables. Exigencia previa: 15/16. Las semillas se generaron después de congelar las fuentes, sin filtrar por rendimiento del agente.
- [x] **5.2 Auditoría:** **34/34 diarios válidos**, sin discrepancias de hashes o recibos detectadas.
- [x] **5.3 Congelamiento y orden única:** **121 pruebas Python**, 22 comprobaciones de mundo, 12 físicas y la cadena completa pasaron mediante `Tools/check.py --integration`. Se mantuvieron iguales los **674 archivos** del manifiesto durante la confirmación.

## Resultados y procedencia

| Medida | Desarrollo y Station | Confirmación |
|---|---:|---:|
| Episodios completados | 8/8 navegación + 3/3 misiones | 16/16 navegación |
| RSS máximo muestreado del cerebro | 35.078.144 bytes | 33.300.480 bytes |
| CPU media en reposo | 0,00 % | 0,00 % |
| CPU media durante las misiones, un núcleo = 100 % | 4,67 % | 7,68 % |
| Peticiones de decisión medidas | 593 | 535 |
| Latencia mediana del servidor | 10,74 ms | 14,43 ms |
| Percentil 95 de latencia, rango inferior | 19,51 ms | 21,17 ms |

Máquina: AMD Ryzen 5 5500U; Unity 6000.0.84f1; cerebro Python 3.12.13; observador externo Python 3.14.6 y psutil 7.0.0. La ejecución física por lotes usa `timeScale=6`; estas cifras son mediciones del servidor, no una garantía de control en tiempo real.

La navegación puede encontrar bloqueos recuperables durante la calibración. «Sin bloqueos irrecuperables» no significa cero colisiones en todos los mapas. Los 16 casos nuevos pertenecen a las cuatro familias del generador actual; no prueban transferencia a cualquier mundo o nuevos mecanismos de misión. El 100 % es el resultado de esta muestra, no una cota poblacional.

- [Resultado global](../../Evidence/2026-10-12/Artifacts/Closure/20261012-191553-460628/status.json).
- [Auditoría integral](../../Evidence/2026-10-12/Artifacts/Closure/20261012-191553-460628/audit-final.json).
- [Ensayos físicos de condicionamiento](../../Evidence/2026-10-12/Artifacts/Closure/20261012-191553-460628/conditioning/conditioning-physical.json) y [control emparejado](../../Evidence/2026-10-12/Artifacts/Closure/20261012-191553-460628/conditioning/paired-control.json).
- [Tres misiones Station](../../Evidence/2026-10-12/Artifacts/Closure/20261012-191553-460628/development/unity-station.json).
- [Todos los mapas reservados](../../Evidence/2026-10-12/Artifacts/Closure/20261012-191553-460628/confirmation/unity-confirmation.json) y [semillas](../../Evidence/2026-10-12/Artifacts/Closure/20261012-191553-460628/confirmation/seeds.json).
- [Recibo offline](../../Evidence/2026-10-12/Artifacts/Closure/20261012-191553-460628/offline/offline.json).
- [Muestras de recursos](../../Evidence/2026-10-12/Artifacts/Closure/20261012-191553-460628/confirmation/resources.json).
- [Manifiesto de cierre](CLOSURE-1.0.json) y [SHA-256 de fuentes](CLOSURE-1.0-SHA256SUMS), incluidos prefab, script de Blender, puente y escenas.

Se conservan las tandas de desarrollo fallidas anteriores. Dos fallos nativos del renderizador Null de Unity se resolvieron desactivando solo cámaras RGB/renderizados offscreen en modo headless; profundidad PhysX y actuadores siguen ejecutándose. La confirmación no se abrió hasta que pasó desarrollo. No hubo cambios de código durante el examen ni correcciones basadas en sus mapas.

## Alcance

El arácnido usa el navegador y el planificador de ECHO-5, CAM/WSP16/gate originales y un único diario `thought.jsonl`. Las reglas del gate, los transductores E[6], las dependencias de las misiones y la locomoción IK están programadas. El modelo motor, el mapa observado y los episodios se adquieren durante la interacción. Estos ensayos comprueban condicionamiento contextual en simulación, no aprendizaje biológico ni competencia general.

El ensayo físico de segundo encuentro fija la propuesta motora para aislar el veto. Es distinto de la navegación autónoma evaluada en mapas nuevos. Su control sin CAM recibe una copia idéntica del checkpoint anterior a la extinción; los hashes quedan en `conditioning/paired-control.json`.

## Uso

```sh
python3 Tools/start_arachnid.py --no-blender
```

Abre el laboratorio en Unity y el puente en `127.0.0.1:8771`. Para las misiones con el mismo cuerpo, sal de Play y abre `Assets/Scenes/EchoStation.unity`. Su cliente usa ese mismo puerto; el antiguo servicio 8770 sigue siendo una integración histórica distinta.

El botón **Abrir cadena causal y extracto CAM** abre el [inspector local](http://127.0.0.1:8771/). Selecciona una decisión marcada «veto CAM»: muestra observación, contexto, ranura, SHA-256, recibo histórico íntegro, gate y comando. Solo lee registros; no genera monólogos ni dirige al actor. D/B aparecen sin transductor y el observador LIF declara `drives_policy = false`.

La CAM persistente está en `Artifacts/Arachnid/runtime-v1/cam.json`. Conserva las 4.096 posiciones, episodios, índices y un contador monotónico; las inferencias activas se guardan aparte de los episodios inmutables. Una medida de profundidad nueva puede retirar el veto sin borrar el hecho. Solo se restaura su efecto para un mundo, origen, comando y significado motor compatibles. El perfil frío vuelve a adquirir el modelo motor; la adquisición y los perfiles entrenados conservan el mecanismo existente de checkpoints. La persistencia de CAM no atribuye al perfil frío un modelo entrenado.

Solo una sesión de navegación puede escribir en el almacén persistente a la vez. Termina la anterior antes de abrir otra. `--no-persistence` crea CAM independientes para comparaciones controladas. La memoria Station es por sesión; sus recibos quedan conservados en disco.

## Repetición y límites

Con este proyecto cerrado en Unity:

```sh
python3 Tools/check.py --integration
```

La orden inicia un trabajo en segundo plano y devuelve PID y carpeta única. Ejecuta pruebas Python, comprobaciones Unity, condicionamiento físico, ocho mapas de desarrollo, tres misiones, medición de recursos y misión offline. Solo si pasan esas etapas congela las fuentes y genera 16 semillas nuevas. Exige al menos 15 llegadas y ningún bloqueo irrecuperable. Cada caso conserva presupuesto, recibo terminal y diario. No modifica el código según el resultado de la confirmación.

`status.json` es el resultado global; `audit-final.json` reúne la auditoría de todos los diarios. `freeze.json` incluye adaptadores, núcleo importado, prefabs, Blender, escenas y configuración. `--development-only` omite la confirmación y nunca acredita el cierre completo.

El medidor externo usa psutil del Python del sistema. RSS es el máximo observado en muestras cada 50 ms del PID del cerebro; CPU utiliza un núcleo lógico como 100 %. Las latencias del servidor incluyen decisión, serialización y escritura HTTP. Unity y su renderizado quedan fuera de esa medición. El umbral de 60 MB no es una garantía para una CAM llena o cualquier carga futura, ni una medición de energía.

El ensayo offline ejecuta una misión completa con cerebro y simulador geométrico CPU dentro de un namespace con solo loopback. Registra interfaces y contadores; no deshabilita la red del anfitrión. Esa prueba no incluye el renderizador ni PhysX, que se comprueban en las otras misiones.

La auditoría usa biblioteca estándar, separada del código del actor, para contrastar hashes, restauraciones, recibos y acciones. Comprueba integridad y coherencia; no es una validación independiente del sensor físico ni una prueba formal universal. La revisión del cambio es una autorrevisión técnica, no una revisión humana externa. Los resultados no reabren S6 ni sustituyen los exámenes de otras ramas de ECHO.

## Demo gráfica documentada

En la máquina original se dejó Unity abierto en el workspace 2. La ejecución gráfica posterior a la confirmación llegó a la meta, guardó 26 episodios sin destruir ninguno y dejó el inspector disponible en el puerto 8771. [Recibo](../../Evidence/2026-10-12/Logs/Closure/live-1.0.json) · [Captura de Unity](../../Evidence/2026-10-12/Logs/Closure/unity-live-1.0.png). No forma parte de la muestra reservada. Pulsa **Reiniciar mapa** u **Otro mundo / desde cero** para continuar.
