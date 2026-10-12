# ECHO-Station 1.0 — contrato y plan de implementación

Fecha: 2026-10-12. Alcance autorizado: cinco fases de la solicitud del propietario.

**Objetivo:** memoria revisable y persistente, arácnido en tres misiones, inspector causal, medición independiente y confirmación reservada.
**Arquitectura:** reutilizar Cam/WSP16/gate originales, NavigationAgent y StationAgent; adaptadores en Bridge, una sola traza Recorder/thought.jsonl. Unity es sensor/actuador y presentación.
**Tecnología:** Python, Unity 6000, C#, psutil para el observador externo, namespace de red Linux.

## Condiciones congeladas antes de confirmar

- CPU del cerebro; sin LLM. D/B sin transductor. LIF pasivo.
- 4096 ranuras, ninguna destrucción de episodios. Refutar una inferencia no borra su recibo.
- Contexto exacto: mundo, origen, comando y modelo motor compatible. Un hash parecido no autoriza un veto.
- Segundo encuentro, contexto seguro y retirada del obstáculo: ensayos separados; observar recibos físicos y coste, incluyendo control sin CAM.
- Persistencia con JSON validado y escritura atómica. Rechazar corrupción; no usar pickle.
- Tres misiones Station completas con prefab, articulaciones y recibo terminal.
- Medir RSS en bytes del PID Python, CPU y latencia; <60 MB significa <60.000.000 bytes. Sin atribuir memoria/energía del render al cerebro.
- Prueba offline en namespace con solo loopback, contadores antes/después. No cambiar interfaces del anfitrión.
- Confirmación: 16 mapas nuevos generados después de congelar código; al menos 15/16 completados, cero bloqueos irrecuperables. Publicar todos los casos. Presupuesto actual de 2.000.000 por episodio.
- No usar mapas reservados históricos. Si falla, conservar el resultado; no adaptar código a ese examen.
- Auditoría separada de política: hashes, recibos, lecturas y cambios de inferencia comprobados. Una auditoría válida no sustituye al éxito de misión.

## Revisión de riesgos concretos

1. Colisiones de WSP al reiniciar: identificador monotónico conservado y prueba de inmutabilidad.
2. Inferencia refutada que vuelve al recargar: persistir su retirada sin borrar el episodio.
3. Mundo/modelo distintos: impedir el uso de recuerdos fuera de contexto.
4. Reintentos HTTP: no duplicar episodios ni alterar presupuesto.
5. Visor/medidor: lectura pasiva; no modificar estado del agente; medir su coste por separado.

## Tareas y comprobaciones

- [ ] 1. `Bridge/cam_persistence.py`, `arachnid_state.py`: almacén CAM, validación, contexto y reconciliación. Pruebas en `Bridge/tests/test_cam_persistence.py`; ensayos en `Tools/conditioning_check.py` y verificación física Unity.
- [ ] 2. `Bridge/station_basal.py`, factoría `BridgeService`, prefab/cliente Station: señales reales de batería, puerta y misión; pruebas de recibos y tres ejecuciones Unity.
- [ ] 3. `Bridge/trace_inspector.py`, HTML local y HUD: GET pasivo, extracto íntegro, SHA y etiquetas D/B/LIF. Pruebas HTTP y revisión visual.
- [ ] 4. `Tools/measure_resources.py`, `offline_check.py`: proceso separado, distribución de latencia y contadores de red. Un ensayo completo aislado.
- [ ] 5. `Tools/check.py --integration`: pruebas, Unity, condicionamiento, recursos, offline, confirmación y auditoría final en directorio único. Guardar hashes antes y después, sin tocar fuentes durante el examen.
- [ ] 6. `Docs/Arachnid/CLOSURE-1.0.md`: checklist con evidencia real y límites; revisar implementación final y dejar demo abierta.

## Ejecución

Pruebas CPU en segundo plano. Fuentes anteriores conservadas en `Logs/Closure/source-before` para revisión; este proyecto Unity no tiene repositorio git local. La solicitud ya autoriza implementar el plan; no se requiere otra aprobación. No se modifica el núcleo histórico ni se reabren S6 o campañas selladas.
