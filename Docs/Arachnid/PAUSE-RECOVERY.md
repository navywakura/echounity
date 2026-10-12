# Detención durante la demo: diagnóstico y corrección

12 de octubre de 2026. Se conservaron las sesiones fallidas y se trabajó únicamente con mapas de desarrollo.

## Causas comprobadas

1. **Calibración incompleta:** en el mapa `c-575b9a64ab80f1e5f85d3653`, las sesiones terminaban en `wait` con soporte motor `[2,1,2,1]`. El selector descartaba repetir una muestra ya observada; el planificador general requería dos muestras para extrapolar ese comando. Faltaba volver por recorridos confirmados para obtener una muestra nueva desde otro origen.
2. **Corte prematuro de destinos:** se examinaban como máximo ocho fronteras. Ocho destinos inaccesibles no prueban que todos sean inaccesibles.
3. **Obstáculo de prueba imposible:** el editor se había dejado abierto con `-arachnidBlockProbe`. Al cambiar de mapa, la sonda seguía activa. En `c-b3126f2da82f0a48e35db489` colocó una pared en `(9,2)` desde `(9,3)`, cerrando la única salida. La comprobación sobre la geometría real confirmó que la meta quedaba desconectada.
4. **Estado de interfaz ambiguo:** `Automatic=false` se mostraba como pausa también al terminar, quedar sin ruta o fallar una petición. «Continuar» podía abrir una sesión nueva sobre una posición terminal.

## Corrección

- Reposicionamiento de calibración mediante aristas con recibos físicos confirmados de la misma sesión, coherentes con el modelo actual y sin bloqueo vigente. Permite alcanzar otro origen desde el que obtener evidencia nueva. La repetición no aumenta el soporte; no se reduce el requisito del núcleo para extrapolar.
- Búsqueda de destinos hasta hallar uno alcanzable o consumir el presupuesto compartido. Se elimina el corte artificial tras ocho intentos.
- La sonda automática consulta, solo del lado del evaluador, que el obstáculo conserve una ruta física a la meta. La ejecución interactiva se relanza **sin esa sonda**.
- Estados separados: explorando, pausa manual, cerrando, detenido con motivo y meta confirmada. Pausar conserva sesión y memoria; reiniciar recarga el mapa desde el punto de aparición. Un cierre pendiente impide abrir otra sesión concurrente.

Una misión terminada sigue deteniendo sus actuadores. Un fallo real, presupuesto agotado o ausencia de una ruta respaldada también puede detenerlos; ahora se informa la causa y no se etiqueta como pausa manual.

## Comprobaciones

- La regresión reproduce el `wait` original con un historial de observaciones públicas: [fallo previo](../../Evidence/2026-10-12/Logs/Arachnid/pause-reposition-red.log).
- **114 pruebas Python pasadas**, incluidas separación entre sesiones, respeto de bloqueos, prohibición de inventar aristas y conservación del checkpoint al elegir un recorrido: [log](../../Evidence/2026-10-12/Logs/Arachnid/pause-python-r2.log).
- **8/8 mapas de desarrollo completados físicamente en Unity**, desde cero, con el arácnido, profundidad, E[6] y CAM. Todos conservaron sesión durante pausa manual y distinguieron cierre de pausa. Reiniciar devolvió el cuerpo al punto inicial. [Recibo](../../Evidence/2026-10-12/Logs/Arachnid/pause-recovery-result.json).
- **2.463 eventos auditados** en ocho diarios. Tres reposicionamientos usaron únicamente aristas previamente observadas; el resto de movimientos siguió el aprendizaje/planificador existente. Fuentes del actor y Unity sin cambios durante la tanda. [Auditoría y hashes](pause-fix-verification-20261012.json).

La prueba física se ejecutó con tiempo de simulación acelerado ×4. Sus tiempos no son medidas de latencia del razonador. Los ocho mapas son de desarrollo; este arreglo no constituye un nuevo examen reservado ni una comparación con LLMs.

[Qué demos están operativas y qué falta](DEMO-STATUS.md).
