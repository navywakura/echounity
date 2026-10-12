# Cámara, mapa y aprendizaje de navegación — revisión 2

El 11 de octubre de 2026 la revisión 2 cumplió la puerta de navegación fijada
en DESIGN.md. El alcance es PointGoal en rejillas planas con cámara RGB-D y
odometría simuladas. Cada caso dispone de 320 decisiones y 2.000.000 unidades
lógicas. El evaluador, la geometría y el mapa privado están separados del actor.

| Condición, en los mismos 16 mapas nuevos | Metas | Acciones, incluidos giros | Movimientos completados | Trabajo lógico |
|---|---:|---:|---:|---:|
| Modelo adquirido + mapa acumulado | 16/16 | 427 | 337 | 1.133.734 |
| Modelo vacío + aprendizaje durante el episodio | 15/16 | 572 | 442 | 1.401.644 |
| Modelo adquirido sin mapa acumulado entre observaciones | 0/16 | 2.605 | 840 | 5.469.956 |

La adquisición CPU, excluida de esas columnas, resolvió sus 4 mapas con 67 acciones
y 199.006 unidades. Sumándola a la condición adquirida se obtienen 494 acciones
y 1.332.740 unidades. Las cifras comparan este protocolo concreto; no son una
medición eléctrica ni garantizan ventaja en otras tareas.

En los 15 casos resueltos por ambos brazos, la condición adquirida necesitó
414 acciones frente a 558 (25,81 % menos), y 328 desplazamientos frente a 436.
No hubo movimientos bloqueados en esa condición durante la confirmación. Los
diarios no muestran repeticiones de intentos fallidos. Esto describe los casos
medidos y no acredita ausencia universal de colisiones.

La prueba adicional de repetir el primer mapa reservado verificó restauración
de la memoria espacial: 44 → 23 acciones y 37 → 23 desplazamientos. Esa repetición
se informa aparte y no incrementa las 16 tareas de transferencia. La política
de elegir la primera tarea estaba fijada antes de la confirmación.

Recibos CPU:

- [Confirmación R2](../../Evidence/2026-10-12/Artifacts/Navigation/confirmation-v2-20261011/report.json).
- [Desarrollo R2](../../Evidence/2026-10-12/Artifacts/Navigation/development-v2-20261011/report.json):
  8/8, 7/8 y 0/8 metas respectivamente.
- [Fuentes, datos y modelo congelados](../../Evidence/2026-10-12/Artifacts/Navigation/development-v2-20261011/freeze.json).
- [Manifiesto de particiones](../../Evidence/2026-10-12/Artifacts/Navigation/worlds-v2.seed-manifest.json).

La ejecución física de Unity utilizó otro espacio de artefactos y comenzó sin
checkpoint. Resolvió 4 episodios de adquisición y 3 de desarrollo. La auditoría
reconstruyó 27 muestras distintas y restauró exactamente el checkpoint, con
soporte [2,13,10,2]. Las tres sesiones de desarrollo conservaron su hash. Una
intervención añadió un obstáculo después de decidir el movimiento: produjo un
bloqueo real, se actualizó el mapa y se alcanzó la meta. También se comprobó
pausar y continuar sin reiniciar la sesión.

- [Verificación completa](../../Evidence/2026-10-12/Logs/navigation-verification.json): válida, fuentes
  sin cambios durante las comprobaciones, servidor nuevo con PID y argumentos.
- [Episodios físicos](../../Evidence/2026-10-12/Logs/navigation-integration-tests.json): 7/7.
- [Auditoría física](../../Evidence/2026-10-12/Logs/navigation-physical-audit.json): válida, 152 planes
  reproducidos mediante el núcleo sin priors de efectos.
- [Pruebas Python](../../Evidence/2026-10-12/Logs/navigation-python.log): 94.
- [Pruebas de manipulación de recibos](../../Evidence/2026-10-12/Logs/navigation-audit-unit.log): 23.
- [Cámara](../../Evidence/2026-10-12/Logs/sensor-tests.json): 13; [mundo](../../Evidence/2026-10-12/Logs/navigation-world-tests.json): 11.

El modelo de la interfaz se copió del checkpoint adquirido físicamente. Su
[recibo de procedencia](../../Evidence/2026-10-12/Artifacts/Navigation/approved-model.json) conserva
el hash y la auditoría de origen. Las sesiones interactivas escriben en otro
directorio y no modifican los recibos de aquella evaluación.

La primera revisión también cumplió su umbral, con 15/16 frente a 13/16 y 0/16,
pero una revisión de código reprodujo un fallo independiente: un rayo que rozaba
el margen de una celda podía declararla libre y borrar una pared recordada. La
[prueba de regresión](../../Bridge/tests/test_navigation_corner.py) usa un mundo
sintético, sin tareas reservadas. Primero falló y después pasó al exigir que el
segmento observado atraviese la región central de la celda. Los registros
[rojo](../../Evidence/2026-10-12/Logs/navigation-corner-red.log) y [verde](../../Evidence/2026-10-12/Logs/navigation-corner-green.log)
conservan el cambio. Las 28 geometrías de R2 son distintas de las 28 de R1.
Los números 15/16 y 16/16 proceden de conjuntos diferentes; no permiten atribuir
su diferencia exclusivamente a esta corrección.

Se conserva el [resultado R1](../../Evidence/2026-10-12/Artifacts/Navigation/confirmation-20261011/report.json),
sus fuentes y datos. La primera automatización física llegó al paso de auditoría
antes de que su script estuviera disponible; falló con código 2. Después se
auditó la evidencia sin alterarla, dejando un [recibo de recuperación](../../Evidence/2026-10-12/Logs/navigation-verification-recovery-v1.json).
La automatización completa de R2 terminó sin ese fallo.

El efecto de los cuatro comandos se aprende por observación y eliminación de
hipótesis con HypothesisAgent de ECHO-5. La geometría de sensores, integración
del mapa, elección de fronteras, búsqueda de rutas y locomoción están programadas.
El RGB pertenece a una cámara real de Unity; el actor usa su profundidad. No se
ha entrenado un reconocedor RGB ni modificado pesos neuronales. La coordenada
objetivo y las dimensiones de la rejilla son datos de la tarea. La pose simulada
es exacta y el movimiento es discreto. Estos resultados acreditan este prototipo
y su reutilización de experiencia; no cierran la versión ECHO-5 ni validan
navegación autónoma en entornos físicos arbitrarios.

Uso y controles: [USO.md](USO.md). Las hipótesis y frases del panel son resúmenes
de observaciones y decisiones registradas; no son un monólogo generado por un LLM.
