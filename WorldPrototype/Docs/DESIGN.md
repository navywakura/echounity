# Mundo isométrico · entorno previo a la beta

Proyecto local nuevo para preparar el juego futuro mientras ECHO-5 completa su
validación. Nombre de trabajo: ECHO-World-Prototype; el juego aún no tiene nombre.
Unity 6000.0.84f1, C#, render clásico, sin dependencias de red ni artwork descargado.

El primer entregable es un mundo local jugable: humanoide, WASD y destino por
clic, chat local con burbuja, construcción/minería acotada, una receta inicial,
guardado y carga por semilla. La estética combina terreno de bloques, materiales
fríos, minerales luminosos y equipo de exploración. La cámara ortográfica fija
usa una presentación isométrica 2.5D sobre geometría y colisiones 3D.

El espacio horizontal tiene 10.000.000 × 10.000.000 posiciones enteras, centrado
en cero: [-5.000.000, 5.000.000). La altura es [-200, 200): 400 bloques, con
secciones lógicas de 16 alturas relativas a -200. Una columna de chunk contiene
16 × 16 × 400 = 102.400 posiciones; no se reserva esa matriz para cada columna.
El mundo completo tendría 4×10^16 posiciones: 40 PB incluso a un byte por bloque.
Por eso la semilla define el terreno sin materializarlo y sólo se conservan las
ediciones. Los meshes contienen caras expuestas, nunca un GameObject por bloque.

Las coordenadas del mundo son enteras; las posiciones del renderer son locales.
Al alejarse el jugador, se desplaza el origen y se recolocan las columnas para
evitar usar floats a millones de unidades. Se mantiene un radio acotado de
chunks. La generación, el meshing y la simulación de entidades son presupuestos
distintos: no se simularán NPC lejanos a la misma frecuencia que los cercanos.

El terreno y sus cambios tienen una única autoridad local. La beta necesitará
un servidor dedicado que valide alcance, inventario, daño, recetas y permisos.
El cliente enviará intenciones. La difusión se limitará por vecindad/interés y
las regiones vacías conservarán datos en disco, sin NPC activos. Un máximo de
200 jugadores es un objetivo pendiente de carga simultánea y perfilado; no lo
acredita una escena local con 200 muñecos. Los perfiles privado/público incluidos
son configuración de diseño, no servicios desplegados.

La integración ECHO se hará después de su verde. Se entregarán observaciones
locales y acciones con presupuesto a un adaptador del núcleo existente; no se
copiará el cerebro ni se inventará otro thought-bus. Recolección, hambre,
alianzas, facciones y planificación de NPC necesitarán contratos, controles y
mundos reservados propios. En este entorno no se muestran NPC simulados como
si fueran agentes ECHO ni se generan monólogos ficticios.

Criterios del primer entorno: terreno reproducible, tratamiento correcto de
coordenadas negativas y límites, carga/descarga acotada, persistencia de cambios,
camino bloqueado tratado sin atravesarlo, chat que no mueve al jugador al
escribir y un humanoide controlable desde la vista isométrica. Se comprobarán
en Unity; se publicarán los fallos, FPS/memoria observados y el alcance local.
