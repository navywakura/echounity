# Plan de implementación del entorno

El diseño está en [DESIGN.md](DESIGN.md). La preparación local y apertura de Unity
están autorizadas; no se desplegará un servidor público ni se modificarán los
ensayos o fuentes de ECHO-5.

1. Core: `Assets/World/Core/WorldData.cs`. Coordenadas de chunk negativas,
   generador entero determinista, modificaciones dispersas, almacenamiento por
   columna y rutas A* acotadas. Verificar semilla, bordes, límites, ediciones,
   recarga y obstáculos con `DataTests` en proceso Unity.
2. Vista: `Assets/World/Rendering/ChunkWorld.cs`, shader por color de vértice.
   Meshes de caras expuestas, cola acotada, colliders por chunk y origen móvil.
   Comprobar que no aparecen objetos por voxel y que la cola no crece sin límite
   al recorrer chunks. Generación superficial implícita; caves naturales son
   una ampliación futura, pero los bloques pueden excavarse.
3. Juego: `Assets/World/Gameplay/`. Humanoide procedural, movimiento por clic y
   WASD, selección de bloques, inventario local y chat con una burbuja temporal.
   Las acciones de construcción consultan alcance, ocupación y límites antes
   de editar. Texto limitado a 140 caracteres, sin rich text ni ejecución.
4. Editor: `Assets/World/Editor/PrototypeEditor.cs`. Construir una escena propia,
   comprobar Core y geometría en batch, abrir el editor y entrar en Play.
   Capturar el entorno real y un recibo separado de la confirmación científica.
5. Documentar controles, qué está implementado, coste del mundo conceptual y
   las etapas de servidor/NPC que faltan. Conservar un proyecto local con Git;
   este encargo no publica ni actualiza los repositorios GitHub anteriores.

Contratos entre componentes (namespace `EchoWorld`):

```text
WorldData(seed, saveRoot)
const ChunkSize=16, MinY=-200, MaxY=200, WorldMin=-5000000, WorldMax=5000000
int Seed; int LoadedColumns; int EditCount
int SurfaceHeight(int x,int z); byte GetBlock(int x,int y,int z)
bool InBounds(int x,int y,int z); bool SetBlock(int x,int y,int z,byte id)
void LoadColumn(Vector2Int column); void UnloadColumn(Vector2Int column); void SaveAll()
IEnumerable<Vector3Int> EditsNearColumn(Vector2Int column)
int StandHeight(int x,int z)   // y del pie sobre la superficie más alta
List<Vector3Int> FindPath(Vector3Int start,Vector3Int goal,int maxNodes=2048)
static int FloorDiv(int value,int divisor); static Vector2Int Column(int x,int z)
```

IDs: 0 aire, 1 suelo, 2 roca, 3 aleación, 4 cristal, 5 faro. Ningún material
implica una implementación biológica o un módulo de ECHO.

```text
ChunkWorld : MonoBehaviour
WorldData Data; Vector2Int Origin; Transform FollowTarget; int Radius (default 2)
int LoadedCount; int PendingCount; int VertexCount; float LastBuildMs
void Initialize(WorldData data); void Refresh(); void BuildImmediate()
Vector3 ToLocal(Vector3Int global); Vector3Int ToGlobal(Vector3 local)
void NotifyEdit(Vector3Int global); bool IsColumnReady(int x,int z)
void RebaseIfNeeded(); void SetRadius(int radius)
```

`DataTests.RunAll()` y `RenderingTests.RunAll()` devuelven una lista de nombres
de pruebas pasadas y lanzan excepción al fallar. El editor reúne el recibo y
sale con código distinto de cero ante un fallo. No se requerirá NUnit ni
entrenamiento GPU para preparar este entorno.
