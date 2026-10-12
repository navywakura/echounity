# Publicación de WorldPrototype — auditoría del 12 de octubre de 2026

Esta copia contiene **BETA 0.0.2 de un jugador local**. El nombre interno «Echo MMO Prototype», el dominio generado de 10.000.000 × 10.000.000 bloques y la planificación MMO no acreditan multijugador ni 200 clientes simultáneos. No hay servidor implementado en esta beta, chat de red, enemigos, progreso de XP ni NPC conectados a ECHO. La publicación del trabajo Unity/MMO está autorizada por el propietario; las atribuciones de terceros se documentan en [ASSETS.md](ASSETS.md).

Se revisaron los archivos de esta copia, las cargas de recursos, las pruebas existentes y las fuentes de atribución. **Esta auditoría documental no ejecutó Unity.** Sus observaciones de código y procedencia se distinguen de la verificación principal realizada por separado.

**Verificación nueva de la copia pública:** el [recibo de WorldPrototype](../../Evidence/Publication-20261012/world-prototype-verification.json) registra **52/52 comprobaciones**, `valid: true` y cero fallos, a las **17:40:50 UTC del 12 de octubre de 2026**. La ejecución principal utilizó **Unity 6000.0.84f1** en modo headless; incluye `MaterialTexturesAndNightFloor` y `TreeBatchesRespectDamageAndFallback`. El [informe general de publicación](../../Docs/PUBLICATION-20261012.md) documenta su alcance por separado. **No se realizó una nueva revisión gráfica de WorldPrototype**: este resultado no renueva las capturas históricas ni acredita la apariencia del fallback en Play.

| Recurso ausente | Comportamiento previsto por el código revisado | Cobertura y límite |
| --- | --- | --- |
| `Art/Tiles/seasons_trees` | `ChunkWorld.GetTreeMaterial()` devuelve `null`; `BuildColumn()` conserva troncos y hojas en la malla de vóxeles y su colisión, sin lote de sprites. | `RenderingTests.TreeBatchesRespectDamageAndFallback` compara la colisión con los bloques de autoridad y, sin atlas, exige ausencia del lote y que la malla visual sea la del collider. La rama de daño de sprites sólo se ejecuta con el atlas presente. |
| `Art/SciFi/crate_metal` | `HudPaint.Item()` continúa con el dibujo procedural del cofre cuando falla la carga del icono. | Revisado en código. `HudTests` prueba estados de la interfaz, no los píxeles del icono; hace falta inspección gráfica del inventario y de la barra. |
| `Art/Terrain/crate` | `ChunkWorld.LoadTexture()` devuelve disponibilidad cero y `EchoTerrainAlbedo()` conserva el color de vértices del cofre. | `RenderingTests.MaterialTexturesAndNightFloor` contrasta la disponibilidad y las texturas enlazadas, incluido `crate`. No demuestra por sí solo el resultado visual del shader. |
| `seasons_tiles.png`, `iso_dungeon_walls_by_pfunked.png` y los otros tres sprites SciFi excluidos | No se encontraron cargas de esos recursos en los scripts actuales. | Los atlas `snowplains_*` incluidos tampoco se cargan como sustitutos automáticos. |

Las pruebas de render forman parte de `RenderingTests.RunAll()`, invocado por `EchoWorld.Editor.PrototypeEditor.Verify`. [El lanzador](../Tools/world.py) calcula la raíz a partir de su ubicación y acepta `UNITY_EDITOR`; no depende de rutas de la máquina que produjo las capturas. Las rutas de descarga en [ASSET-MANIFEST.json](ASSET-MANIFEST.json) sólo documentan procedencia. El [README](../README.md) da instrucciones para Hub y terminal, requisitos de pantalla y ubicación de guardados.

Para comprobar esta distribución, mantener ausentes los recursos excluidos y ejecutar secuencialmente, desde `WorldPrototype` y con su editor cerrado:

```bash
python3 Tools/world.py verify
python3 Tools/world.py smoke
python3 Tools/world.py smoke --seed 733173
```

En Windows se puede usar `python` para el mismo intérprete Python 3. `verify` regenera la escena y ajustes del proyecto además de ejecutar pruebas; `smoke` usa un guardado temporal y necesita un escritorio gráfico. Los resultados nuevos quedan en `Logs/verify-<UTC>/verification.json` y `Logs/smoke-<UTC>/play-smoke.json`, junto a logs y, en Play, capturas. `Logs/verification.json` es la copia de trabajo de la última verificación de editor.

La verificación headless anterior cubre las comprobaciones de editor; la apariencia en Play queda fuera de su alcance. Una futura revisión gráfica podrá comprobar árboles de vóxeles visibles y extraíbles, cofre visible en el mundo, icono procedural en inventario/barra y ausencia de materiales de error. Para cualquier reproducción, registrar Editor, plataforma, semillas, recursos presentes, hashes de los archivos comprobados y recibos con `valid: true`, sin fallos y salida cero. Un nombre de prueba en un recibo antiguo no demuestra que la rama de recursos ausentes se haya ejecutado.

**Evidencia histórica conservada.** [VALIDATION.md](VALIDATION.md), [BETA-0.0.2-PLAN.md](BETA-0.0.2-PLAN.md), [ASSET-MANIFEST.json](ASSET-MANIFEST.json) y los archivos existentes en `Docs/evidence/` describen la entrega local del **11 de octubre de 2026**, anterior a la exclusión de arte. Se preservan sus bytes, fechas, semillas y recuentos. `evidence/beta-0.0.1/` corresponde a una versión anterior. Los 52 grupos de editor, las 40 condiciones de Play por semilla y las 44 del recibo posterior de reinicio pertenecen a esas ejecuciones; no son mediciones de esta distribución pública.

`world-forest.png` y `world-inventory.png` muestran árboles del atlas estacional y el icono SciFi del cofre. La apariencia pública difiere. La captura principal del README se enlaza expresamente como histórica. Los hashes históricos incluyen recursos excluidos y versiones anteriores de archivos: no deben reescribirse para hacerlos coincidir con la copia pública. Los directorios `Logs/` citados en la documentación original tampoco se distribuyen como parte de aquellos recibos.

Guardar nuevas capturas y recibos con un nombre o directorio distinto, por ejemplo `Docs/evidence/public-fallback-AAAAMMDD/`, junto con su inventario y un enlace desde la documentación actual. No sobrescribir las imágenes originales ni reutilizar sus etiquetas de aprobación.

**Comprobación de procedencia.** Las 16 entradas conservadas del manifiesto (14 imágenes y dos créditos) coinciden por SHA-256; las siete imágenes excluidas faltan deliberadamente. Además, se compararon en memoria los bytes con estas descargas enlazadas desde la [publicación de rubberduck](https://opengameart.org/content/isometric-snow-tileset-flare), sin instalar ni extraer recursos en el proyecto:

| Descarga de origen | Coincidencias en esta copia |
| --- | --- |
| [snow_tilesets_source_1.zip](https://opengameart.org/sites/default/files/snow_tilesets_source_1.zip) | Cinco texturas de `Art/Terrain/` y su `CREDITS.txt`: 6/6. |
| [snow_tilesets_2.zip](https://opengameart.org/sites/default/files/snow_tilesets_2.zip) | Ocho atlas y `Art/Tiles/CREDITS.txt`: 9/9. |
| [snowplains_tileset_final_2.png](https://opengameart.org/sites/default/files/snowplains_tileset_final_2.png) | `Art/Tiles/snowplains_tileset_final.png`: 1/1. |

**Cuestiones de publicación aún abiertas:**

- Las capturas históricas contienen representaciones de arte excluido. Quitar los PNG fuente no resuelve automáticamente los derechos de esas representaciones. Etiquetarlas como históricas evita confundir la evidencia, pero no acredita permiso de redistribución. Revisar esos términos antes de distribuirlas; mantener cualquier captura nueva del fallback separada del original.
- La roca de Yughues tiene un aviso de dominio público en los créditos del paquete y una página actual con licencias CC-BY para un paquete Redux. [ASSETS.md](ASSETS.md) documenta la discrepancia. El cotejo de bytes acredita procedencia, no resuelve por sí solo esa diferencia.
- `crate.png` se atribuye a Clint Bellanger con CC-BY-SA 3.0 en el paquete, frente a CC-BY 3.0 en su página actual. No está incluido como textura suelta y no se restauró. Su atribución no cubre `crate_metal.png` de Adventure Forge.
- La verificación principal de editor está completada con 52/52 comprobaciones. No se ha realizado una nueva inspección gráfica del subconjunto público. Las pruebas cubren la estructura del fallback de árboles y las banderas de textura; no hay una prueba automatizada específica de píxeles para el icono de cofre ausente.

Las exclusiones de [.gitignore](../.gitignore) reducen la reintroducción accidental de los recursos omitidos; no eliminan archivos ya seguidos por Git. Esta auditoría sólo modifica documentación y esas exclusiones; no cambia código, recursos, escenas, recibos históricos ni el índice de Git.
