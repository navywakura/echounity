# ECHO Station — cámara, mapa y aprendizaje de navegación

Revisión 2 del mapeado, fijada antes de generar sus mapas: una medida libre debe
atravesar el cuadrado central de 1×1 m de una celda de 2×2 m. Rozar el margen no
autoriza declararla libre ni refutar un obstáculo. Corrige un contraejemplo
sintético de alcance finito en una esquina; la confirmación inicial permanece
archivada con sus fuentes. Esta revisión usa `worlds-v2.json`, con 4 mapas de
adquisición, 8 de desarrollo y 16 de confirmación generados de nuevo. Se exige
que sus geometrías difieran de las anteriores. Se mantienen presupuestos,
brazos, métricas y umbrales; los resultados de ambas revisiones se informan
por separado. La regla conservadora no constituye una prueba geométrica universal
de que cualquier volumen real quepa en una celda; el motor físico sigue comprobando
su cuerpo antes de moverse.

Encargo: vista RGB en primera persona, percepción situada, memoria de mapa,
reacción ante avance bloqueado, aprendizaje medido y transferencia a mapas nuevos.
Se implementa en el proyecto Unity separado. Las fuentes y campañas ECHO-5
actualmente congeladas se importan en lectura; no se modifican.

El primer alcance es navegación a una coordenada objetivo proporcionada por el
usuario (PointGoal), con odometría perfecta simulada. Una cámara RGB-D de campo
limitado proporciona imagen para el visor y distancias para el agente. Las
medidas de profundidad se obtienen de rayos del frustum contra colliders físicos;
no se transmiten listas de paredes, nombres de objetos ni el mapa del juez.
No hay reconocimiento aprendido de categorías a partir de RGB ni SLAM visual.

El mapa de ocupación se integra desde medidas, se recuerda durante la sesión y
puede conservarse para repetir el mismo mundo. Espacio no observado queda
explicitamente desconocido. El minimapa dibuja exclusivamente esa memoria.
El objetivo indicado es una instrucción; su coordenada no implica haberlo visto.
Los obstáculos inferidos de un movimiento fallido se distinguen de las superficies
medidas. Ante fallo se registra incertidumbre y se evita repetir ciegamente la
misma acción. Una medida nueva puede refutar un obstáculo recordado.

El aprendizaje reutiliza HypothesisAgent.observe/plan/checkpoint/restore de
ECHO-5. Cuatro comandos opacos producen traslaciones cardinales cuya asociación
NO se pasa al agente. Cada desplazamiento físicamente confirmado aporta una
transición. Para identificar el actuador se proyecta esa transición sobre un
plano libre de 16×16: mismo origen/destino odométricos, acciones genéricas,
tick 0→1 por muestra independiente y ninguna afirmación sobre paredes. La
proyección solo se usa para desplazamientos unitarios confirmados; bloqueos
no se convierten en evidencia de un actuador nulo. Se conservan el recibo físico
y la proyección juntos. Duplicados de la proyección no añaden soporte.

Al planificar sobre el mapa recordado se llama plan SIN effect_prior. El modelo
utilizado proviene exclusivamente de evidencia. Los estados del mapa pueden
cambiar; el modelo de actuadores se transfiere. No se duplican motores cognitivos,
no se añaden un LLM ni un segundo thought-bus. Registro mediante Recorder actual.
La selección de fronteras y el modelo geométrico del sensor son algoritmos
programados. El efecto de los comandos es lo aprendido en este experimento.

Comparaciones prerregistradas: modelo adquirido y congelado, modelo inicialmente
vacío con aprendizaje durante la prueba, y modelo adquirido sin memoria espacial
entre observaciones. Mismo límite de 320 decisiones y 2.000.000 unidades lógicas
por episodio; informar adquisición aparte, pasos, scans, fallos, repeticiones,
trabajo, éxito y restauración del checkpoint. No convertir las pruebas en una
medida de inteligencia general.

Desarrollo: cuatro mapas de adquisición y ocho de desarrollo. Confirmación:
dieciséis geometrías nuevas en cuatro familias (habitaciones, corredores,
barreras con pasos alternos y obstáculos dispersos). Fijar manifiesto y fuentes
antes de ejecutar la confirmación; no ajustar después sobre esos resultados.
Puerta de navegación: al menos 13/16 metas con modelo adquirido; evidencia
reproducible de adquisición; ninguna lectura de mapa privado; ganancia de pasos
frente a arranque frío con tasa de éxito conservada. Informar el control sin
memoria incluso si no es inferior. Evaluación Unity de cámara, bloqueo y mundos
adicionales complementa el simulador CPU; no sustituye transferencia robótica.

Referencias: Active Neural SLAM (Chaplot et al., ICLR 2020), separación modular
entre percepción, mapa y planificación; Sutton (1990), aprendizaje de modelo y
planificación. Esta implementación no reproduce sus redes ni sus resultados.
https://devendrachaplot.github.io/projects/Neural-SLAM
https://papers.neurips.cc/paper_files/paper/1990/file/d9fc5b73a8d78fad3d6dffe419384e70-Paper.pdf
https://docs.unity3d.com/6000.0/Documentation/ScriptReference/Camera.ViewportPointToRay.html
