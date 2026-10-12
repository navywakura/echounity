# WorldPrototype — BETA 0.0.2 local

Un mundo terrestre de bloques con campamentos sci-fi, biomas, cuevas y árboles. La semilla inicial es `72831`, con bosque próximo al campamento. Proyecto local independiente en **Unity 6000.0.84f1**, C# y render clásico. El humanoide se controla con WASD o un destino por clic. **Esta beta es de un jugador, con chat local.** «Echo MMO Prototype» es el nombre interno del proyecto; los servidores, el multijugador y los NPC de ECHO pertenecen a fases posteriores. El objetivo de 200 jugadores no está implementado ni validado aquí.

La copia pública omite los sprites de Adventure Forge y tres atlas cuya licencia de redistribución no se acreditó. El código conserva árboles de vóxeles y dibuja el icono de cofre por código cuando faltan esos recursos. La [captura histórica del 11 de octubre de 2026](Docs/evidence/world-prototype.png) pertenece a la entrega local anterior a esas exclusiones: muestra arte que esta copia no incluye. Los recibos originales tampoco validan una ejecución nueva de la copia pública. Véanse [publicación, fallbacks y límites de la evidencia](Docs/PUBLICATION.md) y [créditos de los assets](Docs/ASSETS.md).

Para jugar, instala el Editor **6000.0.84f1**, añade esta carpeta `WorldPrototype` en Unity Hub, abre `Assets/Scenes/WorldPrototype.unity` y pulsa Play. No hace falta recuperar los recursos excluidos. La versión de referencia figura en `ProjectSettings/ProjectVersion.txt`; los recibos históricos corresponden a Linux, sin validación equivalente de Windows/macOS.

El lanzador opcional necesita Python 3.8 o posterior y sólo su biblioteca estándar. Ejecuta las órdenes desde `WorldPrototype`, con el editor de este proyecto cerrado. `UNITY_EDITOR` debe señalar el ejecutable del Editor instalado, no Unity Hub ni un directorio. Por ejemplo, en bash, sustituyendo las rutas:

```bash
cd "/ruta/a/echounity/WorldPrototype"
export UNITY_EDITOR="/ruta/al/ejecutable/Unity"
python3 Tools/world.py open
```

En PowerShell, desde la misma carpeta:

```powershell
$env:UNITY_EDITOR = 'C:\ruta\al\Editor\Unity.exe'
python Tools/world.py open
```

`open --seed 218` abre otra semilla. Sin `UNITY_EDITOR`, el lanzador busca primero la instalación Linux habitual bajo el directorio personal y luego `Unity` o `unity` en `PATH`. El bloqueo de un editor ya abierto usa `/proc` en Linux; en otros sistemas hay que cerrar manualmente ese editor antes de lanzar otra orden. `open` y `smoke` necesitan una sesión gráfica; `verify` usa el Editor sin gráficos. Los logs se escriben en `Logs/` dentro de esta copia.

La ayuda está dentro de **Esc → Ayuda**. E abre el inventario, T abre el chat y Enter envía el mensaje local con burbuja. Escape cierra primero el chat enfocado, después inventario/cofre y finalmente abre o cierra la pausa. La pausa ofrece Reanudar, Reiniciar partida, Ayuda y Salir partida; Opciones y Cambiar servidor están deshabilitados. **Reiniciar partida** hace reaparecer al personaje en el campamento con vida y stamina completas, cancela su movimiento y guarda la partida. Conserva el inventario, las construcciones y el reloj del mundo.

WASD mueve al personaje respecto a la cámara; Shift corre y Espacio salta consumiendo stamina. El personaje no sube bloques automáticamente. Q/Z gira la cámara y la rueda ajusta el zoom. El clic izquierdo marca un destino con un anillo que desaparece; no se dibuja la ruta. A* usa ocho direcciones, costes 1 y √2 y una heurística octil: encuentra el camino mínimo disponible en su rejilla, sin atravesar esquinas ni subir escalones. La búsqueda tiene un máximo de 4.096 nodos descubiertos; un destino inaccesible se marca en rojo. WASD o un salto cancelan el recorrido. Esta navegación está programada; no representa aprendizaje de un agente ECHO.

Hay diez ranuras, seleccionables con 1–9/0 o el ratón. Seleccionar un material activa la construcción; B alterna construcción y movimiento por clic. El clic izquierdo coloca y el derecho extrae, a seis bloques de alcance. La extracción deja un objeto visible: acercarse a menos de 3,2 bloques lo atrae si hay espacio y visión libre. Los materiales no se añaden al inventario hasta recogerlos. C fabrica cuatro antorchas con una madera y una piedra. Las antorchas colocadas iluminan de verdad y no bloquean el paso. Un cofre cercano se abre con clic izquierdo en modo movimiento; se vacía antes de poder extraerlo.

El corazón representa **20 HP**. Las caídas de menos de cinco bloques no dañan; desde cinco bloques, el daño es `min(20, 2 × (floor(altura) − 4))`. Una caída de 5 resta 2 HP; de 8 resta 8; de 14 o más resulta letal con salud completa. Al morir se vuelve al campamento conservando el inventario. Correr gasta 18 stamina/s, descansar recupera 15/s y saltar cuesta 12. La stamina máxima es 100. El nivel permanece en 1 y la XP está bloqueada en esta beta.

Cada ciclo tiene **15 minutos de día + 15 de noche** de tiempo de juego. Pausar congela el reloj. Hay hasta 24 antorchas cercanas con luces activas para limitar el coste gráfico. Los bloques que tapan al personaje se desvanecen mediante un shader de tramado en un tubo de visión; sus colisiones y su existencia en el mundo se conservan.

El mundo se genera mediante ruido entero determinista: pradera, bosque, desierto, nieve y montaña. Las cuevas contienen aire real bajo el terreno y pueden alcanzarse excavando o por sus entradas naturales. Los campamentos contienen casas, ventanas y cofres con suministros. En la copia pública los árboles muestran sus troncos y hojas de vóxeles extraíbles; la presentación con sprites estacionales requiere el atlas excluido. Las cinco texturas de terreno incluidas conservan sus créditos; los atlas de nieve incluidos no sustituyen automáticamente al atlas estacional. La procedencia, los usos y las licencias constan en [Docs/ASSETS.md](Docs/ASSETS.md).

El dominio horizontal sigue siendo de **10.000.000 × 10.000.000** bloques, generado por semilla. Cada columna tiene 16×16 y el intervalo vertical es −200…199. Sólo se mantienen 25 columnas gráficas alrededor del jugador, con un halo de datos y un origen móvil. No se genera el planeta completo al arrancar.

El terreno, inventario, posición, salud, stamina, cofres, objetos caídos y reloj se guardan cada 30 segundos y al salir. La beta usa el generador v2 y guarda bajo `Application.persistentDataPath/worlds-beta-0.0.2/seed-N`. La base depende del sistema operativo; en Linux, con `XDG_CONFIG_HOME` sin personalizar, es:

```
~/.config/unity3d/ECHO/Echo MMO Prototype/worlds-beta-0.0.2/seed-N
```

Consulta las rutas de cada plataforma en [Application.persistentDataPath](https://docs.unity3d.com/6000.0/Documentation/ScriptReference/Application-persistentDataPath.html). La ubicación no depende del directorio donde clones este repositorio; otra copia con los mismos nombres de compañía/producto y semilla puede abrir la misma partida.

Los guardados anteriores de `ECHO/World Prototype/worlds` se conservan. No se migran silenciosamente al terreno nuevo. Las columnas se escriben en un directorio de trabajo; el guardado publica juntos terreno y sesión mediante un checkpoint completo y un puntero `CURRENT` reemplazado atómicamente. Se conservan los dos últimos checkpoints. Una escritura fallida no mezcla inventario y terreno de generaciones distintas ni permite salir desde el menú como si hubiera guardado. Se conserva también el estado de una caída al reanudarla. Un cierre forzado puede perder el progreso posterior al último checkpoint; la durabilidad ante cortes de corriente depende también del sistema de archivos y del almacenamiento. Se rechazan archivos incompatibles o dañados antes de sobreescribirlos. El chat es efímero y local.

Para generar comprobaciones nuevas de esta copia, cierra el editor de este proyecto y ejecuta desde `WorldPrototype`, con `UNITY_EDITOR` configurado si hace falta (`python` en lugar de `python3` en Windows):

```bash
python3 Tools/world.py verify
python3 Tools/world.py smoke
python3 Tools/world.py smoke --seed 733173
```

`verify` ejecuta las pruebas de editor y regenera la escena y su configuración; puede modificar archivos del proyecto. Cada `smoke` abre una escena gráfica con guardado temporal, prueba los comportamientos y toma capturas en un subdirectorio nuevo de `Logs/`. Conserva esos resultados con fecha e inventario propios, sin sobrescribir `Docs/evidence/` ni atribuir sus recibos históricos a esta ejecución. [Docs/PUBLICATION.md](Docs/PUBLICATION.md) describe las pruebas existentes para recursos ausentes y lo que falta comprobar visualmente. [Docs/VALIDATION.md](Docs/VALIDATION.md) y [Docs/BETA-0.0.2-PLAN.md](Docs/BETA-0.0.2-PLAN.md) conservan la validación y el contrato de la entrega local original.

Quedan fuera de esta beta las cuentas, servidores públicos/privados, chat de red, enemigos, progresión de XP, facciones y NPC conectados a ECHO. El objetivo de 200 jugadores todavía necesita implementación y ensayos de carga. El prototipo no cambia el estado de validación de ECHO-5 ni del sandbox científico anterior.
