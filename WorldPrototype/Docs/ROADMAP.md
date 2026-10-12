# Del prototipo local a la beta

La BETA 0.0.2 local ya incorpora biomas, cuevas, árboles, cofres, guardado conjunto de terreno y sesión, salud, stamina, antorchas y ciclo de día/noche. Permite comprobar la sensación de movimiento, la lectura
isométrica del terreno y las operaciones básicas sobre el mundo. La escala
horizontal es un dominio de generación; su tamaño no implica que esté cargado
o simulado. El presupuesto real depende de los jugadores simultáneos, sus
vecindades, las ediciones y las entidades activas.

El primer siguiente paso es una partida compartida de 2–16 jugadores. Un
servidor dedicado será dueño del terreno, inventarios y relojes. Los clientes
enviarán intenciones con secuencia y recibirán estados confirmados; no podrán
atribuirse materiales, teletransportarse o borrar bloques remotos. Los cambios
se escribirán en un diario recuperable y snapshots versionados. El formato de
guardado de este prototipo local deberá migrarse explícitamente.

Después se sincronizarán colocación y extracción, inventario y recetas, con
permisos sobre construcciones, mensajes por proximidad y reconexión.
El inventario y la partida ya son durables en el modo local.
La persistencia tendrá pruebas de caída, escrituras incompletas, duplicación de
mensajes y recuperación. El cliente podrá mostrar predicciones de movimiento,
pero la simulación autoritativa resolverá el estado final.

La integración ECHO comenzará con una misión de un único NPC en un mundo
reservado: observar recursos próximos, escoger una acción, comprobar su
consecuencia y volver a un refugio con el coste registrado. Se comparará con un
control convencional equivalente. El adaptador limitará observaciones al campo
perceptivo y convertirá las decisiones en acciones del mismo servidor. Los
NPC no obtendrán un mapa completo por estar ejecutándose en el servidor.

Supervivencia, alianzas y facciones se construirán sobre ese contrato. Cada NPC
tendrá un presupuesto de decisión y un estado persistente; los lejanos podrán
dormir o usar simulación agregada. Esto requiere medir qué comportamientos se
conservan al reducir su frecuencia. Llamar a un LLM por fotograma y por NPC no
es el diseño previsto.

Los mundos público y privado compartirán el mismo servidor configurable. Se
medirá capacidad en etapas de **2, 16, 50, 100 y 200 clientes**, con escenarios
de concentración en una plaza y de dispersión por el mapa. Se registrarán
tiempo de tick p50/p95/p99, latencia de acciones, tráfico de red, RAM, cola de
guardado, CPU de simulación y recuperación tras reconexión. Se fijarán límites
antes del ensayo. Una carga con NPC inertes no equivale a 200 jugadores con
minería, construcción y chat activos.

El render actual produce un mesh por columna y sólo sus caras visibles. Para
ediciones intensivas y cuevas extensas se estudiarán secciones verticales con
remallado independiente, unión de caras coplanares y trabajo distribuido por
frame. Las decisiones deben basarse en perfiles: añadir Jobs/Burst o una base
de datos distribuida no acredita una ganancia por sí mismo.

La estética puede evolucionar hacia sprites direccionales o mantener los
humanoides 3D con cámara ortográfica. La simulación entera del mundo, la
autoridad de acciones y la identidad de los bloques permanecerán separadas de
la representación visual. El nombre definitivo sigue pendiente; «Echo MMO Prototype BETA 0.0.2» identifica
esta entrega local. La beta multijugador todavía no tiene una fecha comprometida.

Referencias de implementación: [cámara ortográfica](https://docs.unity3d.com/6000.0/Documentation/ScriptReference/Camera-orthographic.html),
[CharacterController.Move](https://docs.unity3d.com/6000.0/Documentation/ScriptReference/CharacterController.Move.html),
[índices de meshes](https://docs.unity3d.com/6000.0/Documentation/ScriptReference/Mesh-indexFormat.html).
