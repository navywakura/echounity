# Navegación aprendida en Unity

Arranca `./Tools/start.sh`. En la escena NavigationLab, **Adquirir modelo** abre
un mapa de adquisición; **Iniciar** ejecuta las observaciones y los movimientos.
El modelo se guarda tras completar el episodio con evidencia suficiente. **Otro
mundo** carga una geometría de desarrollo distinta usando lo adquirido. **Desde
cero** compara con un modelo inicialmente vacío. **Repetir / memoria** reutiliza
el mapa observado de ese mundo. Los mapas reservados se ejecutan mediante el
evaluador, fuera de este selector de demostración.

La vista isométrica pertenece al observador humano. La ventana pequeña muestra
una cámara perspectiva del robot. Los rayos de profundidad salen de su campo
visual, terminan en la primera superficie física y no atraviesan paredes. El
minimapa muestra la memoria del agente: azul libre, rojo superficie ocupada,
ámbar obstáculo inferido de un fallo, oscuro desconocido. El punto de destino
es una coordenada que la tarea proporciona explícitamente.

**B / Añadir obstáculo** cambia la geometría física en el camino propuesto. Si
existe otro recorrido, el robot puede observarlo y recalcular. Si cierras todos
los caminos, no hay una promesa de éxito: el presupuesto limita su exploración.
**V** muestra los volúmenes predictivos del motor; **F** sigue al robot;
**Q/E**, estando en pausa, orientan la cámara. Espacio inicia o pausa.

Se aprenden los efectos de cuatro comandos mediante transiciones ejecutadas:
posición anterior, comando, posición posterior y confirmación física. ECHO
descarta hipótesis incompatibles y conserva las que explican los resultados.
El checkpoint se verifica reconstruyendo su evidencia. Al resolver otro mapa
se restaura ese modelo; la ruta se calcula sobre las zonas observadas.

La integración de profundidad, la selección de fronteras de exploración, la
búsqueda de rutas y el controlador anticolisión están programados. Este
experimento mide adquisición de un modelo motor y reutilización de memoria,
con hipótesis discretas; no entrena una red neuronal por retropropagación. Esa
técnica serviría para otra intervención, por ejemplo reconocer categorías desde
imágenes, que necesitaría datos etiquetados y una evaluación visual independiente.

El panel presenta observaciones, hipótesis, predicciones y resultados que
existen en el registro. Las frases son resúmenes de esos datos. El robot no
genera un monólogo privado ni una explicación mediante LLM. La memoria del mapa
y la evidencia de actuadores son dos almacenes distintos y auditables.

Las limitaciones actuales son explícitas: odometría simulada perfecta, movimiento
en una rejilla plana, obstáculos geométricos, orientación cardinal de actuadores
y destino indicado. No se atribuye reconocimiento de objetos RGB, SLAM visual,
transferencia física a un robot real ni descubrimiento de las reglas del riego.
Las misiones anteriores siguen disponibles con `./Tools/start.sh --classic`.

Pruebas, siempre en segundo plano:

```sh
# Con este proyecto cerrado en el editor. --integration necesita el puente local.
python3 Tools/check_navigation.py --integration

# La evaluación CPU usa su propio modelo y sus propios diarios.
python3 -B Tools/nav_benchmark.py --phase development

# Solo tras revisar el desarrollo y congelar las fuentes; no ajustar con este examen.
python3 -B Tools/nav_benchmark.py --phase confirmation --freeze RUTA_DEL_DESARROLLO/freeze.json
```

Los recibos distinguen trabajo lógico, acciones físicas, exploración, aprendizaje
y coste de adquisición. Las comparaciones no miden energía eléctrica ni certifican
una mejora universal de inteligencia. La confirmación de navegación tiene un
alcance independiente del cierre general de ECHO-5.
