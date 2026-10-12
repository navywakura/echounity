# Assets de WorldPrototype BETA 0.0.2 — copia pública

Auditoría del 12 de octubre de 2026 sobre la copia de publicación. Se conservan **14 imágenes y dos archivos de créditos** en `Assets/Resources/Art/`: cinco texturas de terreno y nueve atlas de nieve. Sus SHA-256 coinciden con las entradas correspondientes del [manifiesto histórico](ASSET-MANIFEST.json) y con las descargas actuales del autor indicadas en [PUBLICATION.md](PUBLICATION.md). Los bytes de las imágenes y de los créditos no se han modificado en esta preparación.

`ASSET-MANIFEST.json` registra la importación local original, incluidos siete PNG ahora excluidos. Sus rutas absolutas son procedencia histórica, no dependencias para ejecutar el proyecto. Este documento describe el subconjunto público; el manifiesto no acredita una licencia para cada entrada ni asegura que todas estén incluidas.

Las licencias de terceros se conservan por recurso. Este inventario no concede una licencia nueva al código ni sustituye las condiciones del arte por la licencia general del repositorio. Al redistribuir obras CC-BY y CC-BY-SA, conserva autores, títulos, avisos y enlaces de licencia; identifica las adaptaciones. Las adaptaciones de arte CC-BY-SA conservan las obligaciones de CompartirIgual. Textos de referencia: [CC-BY 3.0](https://creativecommons.org/licenses/by/3.0/), [CC-BY-SA 3.0](https://creativecommons.org/licenses/by-sa/3.0/) y [CC0 1.0](https://creativecommons.org/publicdomain/zero/1.0/).

Las texturas sueltas de `Assets/Resources/Art/Terrain/` proceden de `snow_tilesets_source.zip`. Los nombres originales, autores y licencias declaradas en el paquete son:

| Archivo público ← original | Autor y obra de origen | Licencia/aviso del paquete |
| --- | --- | --- |
| `grass.png` ← `grass_overcast.png` | Lamoot; **copyright Blender Foundation \| apricot.blender.org**. [Grass texture](https://opengameart.org/content/grass-texture). | [CC-BY 3.0](https://creativecommons.org/licenses/by/3.0/) |
| `rock.png` ← `rock_diffuse.png` | Yughues. [Tilling texture pack 13, actualmente Free Materials Pack #13 Redux](https://opengameart.org/content/tilling-texture-pack-13). | «Public Domain» en los créditos suministrados; discrepancia con la página actual, detallada abajo. |
| `snow.jpg` ← `snow_ground.jpg` | rubberduck. [Isometric snow tileset (flare)](https://opengameart.org/content/isometric-snow-tileset-flare). | CC0, declaración específica para `snow_ground` en los créditos de fuentes. |
| `bark.png` ← `bark.png` | Bart K., OpenGameArt.org. [Seamless, tiling tree bark texture](https://opengameart.org/content/seamless-tiling-tree-bark-texture); la página identifica además la corrección de color de qubodup. | [CC-BY-SA 3.0](https://creativecommons.org/licenses/by-sa/3.0/) |
| `metal.png` ← `metal.png` | p0ss. [Rusted metal texture pack](https://opengameart.org/content/rusted-metal-texture-pack). | [CC-BY 3.0](https://creativecommons.org/licenses/by/3.0/) |

Los créditos íntegros del paquete de fuentes permanecen en [Terrain/CREDITS.txt](../Assets/Resources/Art/Terrain/CREDITS.txt). El proyecto renombró algunos archivos, pero sus bytes coinciden con los originales del paquete. Unity genera su importación comprimida y el shader aplica muestreo, tintes e iluminación durante el render; esto no altera los archivos fuente conservados.

**Roca: diferencia entre fuentes.** Los créditos incluidos atribuyen `rock_diffuse.png` a Yughues y lo declaran dominio público. La [página actual del autor](https://opengameart.org/content/tilling-texture-pack-13), consultada el 12 de octubre de 2026, lista CC-BY 3.0 y CC-BY 4.0 para un paquete denominado Redux. La textura incluida coincide byte por byte con la del archivo de fuentes de rubberduck; no se ha demostrado que sea el mismo archivo que las descargas Redux. Se conserva el aviso recibido y se hace visible esta discrepancia, sin convertir silenciosamente «Public Domain» en CC0 ni dar por resuelta la licencia de esa versión. La atribución a Yughues se mantiene en cualquier caso.

Los nueve atlas de `Assets/Resources/Art/Tiles/` son `snowplains_ice.png`, `snowplains_other.png`, `snowplains_rottentower.png`, `snowplains_structures.png`, `snowplains_tileset.png`, `snowplains_tileset_final.png`, `snowplains_trees.png`, `snowplains_water.png` y `tiled_snowplains_2x2.png`. Pertenecen a **Isometric snow tileset (flare)**, de **rubberduck**, basado en el **Grassland tileset de Clint Bellanger**, distribuido como conjunto bajo [CC-BY-SA 3.0](https://creativecommons.org/licenses/by-sa/3.0/), según la [publicación del autor](https://opengameart.org/content/isometric-snow-tileset-flare). El PNG final es una descarga independiente de esa misma publicación y tiene esa procedencia comprobada; no es uno de los tres atlas excluidos.

Se conservan tanto [Tiles/CREDITS.txt](../Assets/Resources/Art/Tiles/CREDITS.txt) como los créditos de fuentes anteriores. Estos detallan los componentes de Clint Bellanger, Lamoot y Blender Foundation, p0ss, Zuendholz, Bart K., Sindwiller, Blender Texture Disc, Yughues, Public Domain Textures, cemkalyoncu, Daniel Andersson y Stefan Beller. Incluyen obras CC-BY, CC-BY-SA, CC0/dominio público y alternativas de licencia para algunos componentes. Los avisos individuales permanecen intactos: que una textura base sea CC0 no convierte el atlas compuesto en CC0.

El render actual usa las cinco texturas sueltas sobre superficies de bloques. No hay una carga de los atlas `snowplains_*` en los scripts de juego/render revisados; se conservan como recursos importables. `snowplains_trees.png` no reemplaza por nombre ni por coordenadas al atlas opcional `seasons_trees.png`.

**`crate.png` y `crate_metal.png` son recursos distintos.** Los dos archivos de créditos atribuyen `crate.png` a **Clint Bellanger**, obra [Wood Apple Crate](https://opengameart.org/content/wood-apple-crate), y declaran **CC-BY-SA 3.0**. La página actual de esa obra lista **CC-BY 3.0**; se registra la diferencia sin cambiar el aviso del paquete. No hay `Assets/Resources/Art/Terrain/crate.png` ni entrada para él en el manifiesto de esta copia. `ChunkWorld` admite esa textura opcional; su ausencia activa el color de vértices del cofre. El icono excluido `Art/SciFi/crate_metal` pertenece al paquete Adventure Forge y no adquiere la licencia de la caja de Clint Bellanger por tener un nombre parecido.

Recursos excluidos de esta copia pública:

| Ruta bajo `Assets/Resources/Art/` | Procedencia registrada y motivo |
| --- | --- |
| `Tiles/seasons_tiles.png`, `Tiles/seasons_trees.png`, `Tiles/iso_dungeon_walls_by_pfunked.png` | Llegaron como PNG sueltos sin licencia acompañante. El nombre del último menciona pfunked; el nombre por sí solo no acredita autoría y permiso de redistribución de esos bytes. |
| `SciFi/console_01.png`, `SciFi/control_panel.png`, `SciFi/crate_metal.png`, `SciFi/terminal_01.png` | Adventure Forge — Sci-fi Spaceship, Endless Adventures Inc., según la procedencia local registrada. No se aportó una licencia independiente que acreditase redistribución de esos PNG. |

La exclusión comprende los PNG y sus `.meta`; `.gitignore` ayuda a evitar su reintroducción accidental. El permiso del propietario para publicar el proyecto, incluido el trabajo MMO, no aporta por sí mismo los permisos de autores externos. Para jugar no es necesario restaurar estos archivos ni extraerlos de las capturas.

La geometría del humanoide, las casas de vóxeles, las antorchas, los materiales caídos, el marcador de destino y el dibujo de la interfaz se generan mediante código del proyecto. Las texturas de terceros conservan las atribuciones anteriores. La copia pública usa árboles de vóxeles e icono de cofre dibujado por código. Las [capturas y recibos históricos](VALIDATION.md) muestran la entrega anterior, con sprites que se han excluido; sus condiciones de publicación y los límites de los fallbacks se explican en [PUBLICATION.md](PUBLICATION.md).
