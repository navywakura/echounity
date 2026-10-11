> Historical verification of the private station. The publication changes visuals and source discovery; see [PUBLICATION.md](PUBLICATION.md) for fresh checks. Archived receipts: [Evidence](../Evidence/INDEX.md).

# Verificación de ECHO Station

Comprobación completada el 11 de octubre de 2026, a las 02:28 de Madrid.
Unity 6000.0.84f1; puente Python local; planificador original ECHO-5.

Pasaron 37 pruebas Python, 22 comprobaciones del mundo y 12 del movimiento.
Las tres misiones siguientes se ejecutaron de principio a fin con Unity, física,
HTTP y ECHO, sin sustituirlos por un mock. El runner utilizó tiempo de simulación
acelerado ×6 para esta comprobación. La vista interactiva usa velocidad normal.

| Misión | Acciones ejecutadas | Energía restante | Tiempo real en la prueba |
|---|---:|---:|---:|
| Restablecer riego y regresar | 82 | 78.16 % | 22.48 s |
| Entrega y regreso | 73 | 79.88 % | 20.76 s |
| Riego con bloqueo nuevo | 96 | 74.24 % | 26.49 s |

En el caso de desvío se exigió observar el bloqueo de (5,9), pasar por (5,3) o
(7,6) y no ocupar la celda bloqueada. Las tres condiciones y la misión completa
se cumplieron. Es una prueba concreta de actualización de mapa y replanificación.

El recibo `Logs/verification.json` conserva hashes de las fuentes comprobadas y
confirma que no cambiaron durante la ejecución. `Logs/world-tests.json` y
`Logs/motion-tests.json` contienen comprobaciones individuales;
`Logs/integration-tests.json` conserva los resultados reales.

`Logs/journal-audit.json` comprueba la cadena mediante `echo5.trace.verify_journal`,
la correspondencia entre las 251 acciones del entorno y sus 251 consecuencias
confirmadas, y un recibo de cierre por sesión. Las llegadas requieren un resultado
físico compatible y distancia residual no superior a 200 mm. Se verificaron
1.016 eventos en total. Los diarios se conservan en `Artifacts/Unity/`.

La revisión independiente de código detectó problemas en llegada, cancelación,
contabilidad manual, reintentos y cierre por agotamiento de recursos. Se repararon
antes de esta ejecución. Se conservaron los recibos de las pruebas anteriores.

Estos resultados verifican un prototipo situado y reproducible. Las dependencias
llave/puerta, batería/bomba y bomba/válvula son reglas explícitas del adaptador.
El controlador no las aprendió en estas pruebas. Las cápsulas son predicción
geométrica local, no una red neuronal entrenada. No se midió transferencia a
mapas reservados, aprendizaje causal autónomo, percepción por cámara ni robótica
física. El proyecto no modifica los criterios de cierre de ECHO-5 ni S6.

Para repetir la prueba, cerrar este proyecto en Unity y ejecutar
`python3 Tools/check.py --integration`. No editar fuentes mientras corre.
