# Echo MMO Prototype BETA 0.0.2

Implementación autorizada por el encargo del 11 de octubre. Se actualiza el
proyecto local y se vuelve a abrir Unity al terminar. No se toca ECHO-5 ni su
sandbox científico. La beta continúa siendo de un jugador local.

El mundo sigue usando coordenadas enteras, chunks 16×16, origen móvil y carga
acotada. El generador v2 incorpora biomas terrestres por ruido, montañas, cuevas,
árboles y un pequeño asentamiento sci-fi en el origen con casas y cofres. Los
guardados v1 se conservan en su carpeta; v2 utiliza un espacio independiente.

La navegación usa vecinos diagonales con coste euclídeo y sin cortar esquinas.
No sube escalones ni salta sola. El clic señala un destino mediante un círculo;
la ruta queda interna. WASD cancela el destino, Shift corre y Espacio salta.
El óptimo se refiere a la rejilla navegable con costes 1 y √2; si se agota el límite de
búsqueda, se comunica que no hay ruta dentro del presupuesto, sin atravesar
obstáculos. E abre inventario, T chat, Enter envía. Escape cierra el chat si lo
estamos escribiendo; fuera de él cierra primero inventario/cofre y luego abre/cierra la pausa. La ayuda vive dentro de
esa pausa. Opciones y cambiar servidor aparecen deshabilitados.

Vida máxima: 20 HP. Caída <5 bloques: 0 daño; desde 5: dos HP por bloque por
encima de cuatro, hasta 20. Stamina máxima100, correr consume18/s, saltar12;
regenera15/s al descansar/caminar. Nivel fijo1 y XP bloqueada; no se añaden
enemigos ni minería de menas en esta beta. Materiales comunes sí se extraen.
El material roto aparece en el mundo; se atrae al acercarse con línea de visión
y entra al inventario sólo al alcanzarlo y si cabe. Hotbar de diez ranuras.
Antorchas colocables emiten luz. Día900s+noche900s de tiempo de juego, congelado
durante pausa. Objetos entre cámara y jugador usan un recorte/transparencia
visual, conservando sus colisiones.

Distribución de implementación y contratos (namespace EchoWorld):

- Core: WorldData.cs, nuevo TerrainGenerator.cs y DataTests.cs.
  Mantener APIs actuales salvo FindPath ahora diagonal y sin ascensos.
  `BiomeKind {Meadow,Forest,Desert,Snow,Mountain}`;
  `BiomeKind BiomeAt(int x,int z)`; `static bool IsSolid(byte id)`;
  `IEnumerable<Vector3Int> GeneratedCandidates(Vector2Int column)` incluye
  cuevas, árboles/estructuras y sus caras potencialmente expuestas;
  `IEnumerable<Vector3Int> TreesInColumn(Vector2Int column)` devuelve bases;
  `int WalkHeight(int x,int z,int nearY)` encuentra apoyo a nearY o nearY−1,
  sin convertir una ruta subterránea en una ruta sobre el techo.
  `StandHeight` sigue siendo superficie sólida más alta para spawn/teleport.
  Materiales:0aire,1tierra/hierba,2piedra,3aleación,4vidrio,5antorcha,
  6madera,7hojas,8arena,9nieve,10cofre. Sin generación de menas.
- Render: VoxelMesher.cs, ChunkWorld.cs, shaders y vistas de vegetación.
  Respetar APIs de streaming y métricas. Usar candidatos generados y edits;
  IsSolid para colisiones/oclusiones. Antorchas se representan desde gameplay.
  Texturas en Resources/Art/Terrain/{grass,rock,bark,metal}.png y snow.jpg;
  atlas originales en Resources/Art/Tiles, sin modificar sus bytes.
  Árboles opcionalmente usan el atlas seasons_trees con troncos sólidos.
  La vista sólo muestra vegetación cuyos troncos todavía existen.
  Shader de terreno/vegetación usa globals `_EchoPlayer` (posición local del
  torso), `_EchoCamera` y `_EchoCutawayRadius` para descubrir al jugador.
- UI: WorldHud.cs y auxiliares propios. Estética de RPG discreta, diez slots,
  corazón grande con vaciado proporcional, stamina y burbuja. Sin guía fija.
  `WorldHud.Game`, `IsTyping`, `IsInventoryOpen`, `IsPaused`, `BlocksGameplay`,
  `ConsumesPointer(Vector3)`, `SubmitChat(string)`, `BeginChat()`,
  `HandleEscape()`, `ToggleInventory()`, `OpenChest(Vector3Int)`.
  Entrada del HUD antes del jugador (`DefaultExecutionOrder(-200)`).
  `Game.Session.Health`, `MaxHealth=20`, `Stamina`, `MaxStamina=100`,
  `Level=1`, `Xp=0`, `Count(byte)`, `Take(byte,int amount=1)`,
  `Add(byte,int amount=1)` devuelve cantidad aceptada, `Name`, `Color`.
  `Game.Player.SelectedBlock` byte, `Building`, `Notice`, `GlobalCell`.
  `Game.Chests.Count(cell,id)`, `TakeOne(cell,id)`, `DepositOne(cell,id)`.
  `Game.DayNight.IsDay`, `TimeLabel`; `Game.Save()`, `QuitGame()`.
- Principal: PlayerExplorer, LocalSession, HumanoidVisual, IsometricCamera,
  PrototypeGame, drops/chests/antorchas/ciclo, importación de arte, integración
  y pruebas en Unity. Ningún trabajador ejecuta Unity en paralelo.

Verificación: semilla y biomas; cuevas visibles y deterministas; guardado sin
perder ediciones; rutas diagonales frente a un oráculo de coste, sin atravesar
esquinas y sin subir automáticamente; transferencia de items atómica; caída
en 4,5,8 y14 bloques; stamina limitada; ciclo900/900; prioridad de T/E/Escape;
cofre no se rellena al reabrir; cámara y drops sobreviven al origen móvil.
Después se prueba Play con captura de día, noche, inventario y pausa. Se
publica el checklist con recibos, limitaciones y assets efectivamente usados.

- [x] Generación y navegación v2.
- [x] Materiales, vegetación y oclusión.
- [x] Movimiento, animación, vida y stamina.
- [x] Inventario, drops, cofres, antorchas y reloj.
- [x] HUD y menú de pausa.
- [x] Verificación, documentación y reapertura de Unity.

Cierre local: 52 comprobaciones de editor y 40 de Play en cada una de dos semillas.
Recibos y capturas: `Docs/VALIDATION.md`. Editor reabierto en Play con la
semilla 72831; log `Logs/open-20261011T043641Z/editor.log`.
