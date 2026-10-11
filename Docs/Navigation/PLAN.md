# Plan de implementación de navegación aprendida

Objetivo: cámara real del robot, mapa observado, modelo de actuadores aprendido,
checkpoint transferible, pruebas comparadas en mundos nuevos y visor factual.
Especificación: DESIGN.md. Se continúa la implementación autorizada por el usuario.

- [x] Protocolo de sensor y decisión separado de las antiguas misiones simbólicas.
- [x] Cámara perspectiva RGB 384×216, profundidad en frustum, oclusión física;
      tests de pared delante/detrás, alcance, campo visual y limpieza de recursos.
- [x] Integración de ocupación desde rayos; desconocido, libre, ocupado e inferido.
- [x] Aprendizaje por observe y checkpoint/restauración, sin effect_prior.
      Tests de evidencia real, duplicados, colisión ambigua, persistencia y cambio.
- [x] Servicio local con sesiones, límites, recibos causales y cierre idempotente.
- [x] Entornos procedurales y juez independientes; manifiesto previo de particiones.
- [x] Cliente Unity de scan/move, miniventana RGB y minimapa basado en memoria.
- [x] Adquisición y comparación CPU en segundo plano; recibos completos y fuentes.
- [x] Integración física Unity, mundo nuevo, bloqueo, repetición y auditoría.
- [x] Revisión de código, documentación de resultados y apertura de la escena.

Revisión 2 terminada el 11-10-2026. Navegación adquirida: 16/16 metas reservadas;
arranque frío: 15/16; sin mapa acumulado: 0/16. Adquisición contabilizada aparte.
117 pruebas Python (94 del puente/evaluador y 23 del auditor), 24 comprobaciones
Unity y 7 episodios físicos completos. Véase RESULTS.md y los recibos enlazados.
Este checklist cierra el alcance de navegación de DESIGN.md; no cierra ECHO-5.

Interfaz exacta en PROTOCOL.md. Sensor, backend y escena tienen propietarios
separados durante el trabajo paralelo. Solo el coordinador lanza Unity.

Riesgos que se comprueban: oclusión y rayos fuera de escena; redondeo de pose tras
colisión; fuga de mapa/semántica desde el juez; duplicado de petición; contaminación
de modelos/mapas entre mundos y brazos; replanificación usando hipótesis no
adquiridas; recibo perdido al pausar; captura RGB vacía o cámara dentro del robot.
