# Feature: panel de problemas de carga (fase 1, solo lectura)

**Fecha:** 2026-11-22
**Estado:** Propuesta

## Resumen
Un panel dentro del juego que, al terminar de cargar una partida, lista lo que la partida referencia y ya no existe
(contenido de mods que faltan) y los nodos con el grafo eléctrico roto. Agrupa por mod y tipo, cuenta los casos y
permite saltar con la cámara a cada uno. La fase 1 **no modifica la partida**: el usuario arregla cada caso con las
herramientas que ya tiene y el panel refleja el resultado. Vive de momento dentro de asset-usage, en un módulo aislado
que se pueda extraer a un mod propio si algún día se publica.

## Problema / motivación
Al cargar, el juego avisa de "missing content" y escribe en el log líneas como `Unknown prefab ID: ...` o
`ElectricityFlowEdge for connected node X not found!`, pero no dice **dónde** está cada problema. Limpiar el pack
160092 obligó a reactivarlo, buscar por nombre y comprobar el log tras cada recarga. Los índices de entidad además
cambian en cada carga, así que una lista sacada del log solo vale para esa sesión.

## Qué dice el código del juego (versión 1.6.4f1)
Estos hechos condicionan el diseño y se han leído del juego descompilado:

- **Prefabs que faltan:** `ResolvePrefabsSystem` conserva como entidad cada prefab desconocido que siga referenciado,
  con `PrefabData.m_Index < 0` y `PrefabData` deshabilitado. `PrefabSystem.GetObsoleteID(entity)` devuelve su `PrefabID`.
  La consulta debe usar `EntityQueryOptions.IgnoreComponentEnabledState`.
- **Por qué sigue saliendo el aviso:** `PrefabSystem.Serialize` solo guarda un ID obsoleto si algo lo referencia. Sin
  referencias, desaparece al guardar y recargar.
- **Referencias directas** (`PrimaryPrefabReferencesSystem`): `PrefabRef`, `UnderConstruction.m_NewPrefab`,
  `CompanyData.m_Brand`, `Policy`, `ServiceBudgetData`, `VehicleModel`, `EditorContainer`, `Chirp` (remitente y
  `ChirpEntity`) y `SubReplacement`.
- **Referencias entre prefabs** (`SecondaryPrefabReferencesSystem`): `SpawnableBuildingData`,
  `PlaceholderBuildingData`, `ServiceObjectData`, `NetLaneData`, `TransportLineData`, `ContentPrerequisiteData` y
  `ModPrerequisiteData`. Un edificio que falta mantiene vivos su zona y su mod.
- **Zonas:** al cargar, `FillZoneTypeArrayJob` convierte en `ZoneType.None` las celdas cuya zona no se resuelve. Una zona
  que falta no se limpia repintando celdas, sigue viva por el prefab que la referencia.
- **Nodos rotos:** `ElectricityGraphSystem.UpdateEdgeMiddleNodeConnections` recorre los `ConnectedNode` de cada arista
  eléctrica y avisa si no existe la arista de flujo. Se puede replicar sin escribir con
  `ElectricityGraphUtils.TryGetFlowEdge`.

## Alcance
### Dentro de alcance
- **Escaneo** una vez al terminar la carga y con un botón **Rescan**.
- **Árbol** mod que falta > tipo > prefab, con contadores en cada nivel y un grupo aparte de nodos rotos.
- **Navegación** por prefab con `◀ 3/37 ▶`: cada flecha mueve la cámara y selecciona el caso.
- **Casos sin ubicación** (chirps, políticas, presupuestos, modelos de vehículo): se cuentan como `no location`.
- **Dependencias:** lo que solo sigue vivo por otro prefab que falta indica cuál es (por ejemplo, zona → edificio).
- **Desaparición inmediata:** al eliminar un caso con las herramientas del juego, sale de la lista sin esperar a Rescan.
- **Atribución** a mod con degradación suave (ver reglas).

### Fuera de alcance (explícito)
- Borrar, reasignar o reparar nada desde el mod. Es la fase 2 (edificios, objetos, superficies) y la fase 3 (marcas y
  nodos).
- Escanear partidas sin cargarlas.
- Consultar Paradox Mods por red.
- Diagnosticar el cierre al quitar la pausa.
- Publicar el mod o decidir su nombre definitivo.

## Reglas de negocio e invariantes
- **Solo lectura:** sin escrituras en componentes de la simulación, sin `EntityCommandBuffer` y sin Harmony. Lo único que
  cambia es la selección y la cámara, como hace la búsqueda del menú developer (`DebugSystemSearch.FocusEntity`).
- **Sin carga en la simulación:** ningún sistema en fases de simulación ni jobs Burst. Debe funcionar en pausa. Sin panel
  abierto, el coste por frame es nulo salvo comprobar una bandera.
- **Identidad de un caso:** `Entity` completa (índice y versión), que evita confundir una entidad reciclada. Nunca se
  persiste, porque cambia en cada carga.
- **Desaparición inmediata:** con el panel abierto, una comprobación cada segundo y con presupuesto de tiempo revisa solo
  los casos listados. Un caso sale si la entidad ya no existe, tiene `Deleted` o ya no referencia ese prefab.
- **Rescan es la verdad:** un nodo reconstruido sobre la misma entidad sigue en la lista hasta el siguiente Rescan.
- **Atribución, por orden:** `ContentPrerequisiteData` del prefab obsoleto, historial de asset-usage (`Type:Name` →
  `packs`) y, si no hay nada, grupo `Unknown pack` con el hash del paquete. El módulo funciona sin datos de asset-usage.
- **Aislamiento:** el módulo solo conoce el historial a través de una interfaz (`IPrefabOriginLookup`). Nada del resto de
  asset-usage depende del panel.
- **Errores:** todo capturado y registrado, como en `UsageSystem`. Un fallo del escaneo deja el panel con un aviso y nunca
  afecta a la partida (R3.5).
- **Compatibilidad:** los tipos internos del juego se concentran en pocos ficheros (R10.1). No se asume el tamaño del mapa
  (R12.2).
- **Convenciones:** código, logs y textos en inglés, sin comentarios, versión patch por cambio.

## Modelo (borrador)
- `LoadIssueCategory`: `Building`, `Object`, `Surface`, `Brand`, `Zone`, `Content`, `BrokenNode`, `Other`.
- `MissingPrefabIssue`: `PrefabId`, `Category`, `Origin`, `Located: List<Entity>`, `UnlocatedCount`, `KeptAliveBy`.
- `BrokenNodeIssue`: `Node`, `Edge`.
- `LoadIssueReport`: árbol de solo presentación. A la UI llegan contadores y filas visibles, nunca la lista de entidades.

**Ubicación de un caso:** posición propia (`Transform`, `Node`, geometría de área). Si no tiene, se sube por `Owner`; una
empresa va a `PropertyRenter.m_Property`. Si nada resuelve, cuenta como `no location`.

## Plan de implementación
Rama propia en `CSII-asset-usage`. Lógica pura en `Core/LoadIssues/`, ECS en `Systems/LoadIssues/`, UI en `UI/`.

0. **Spike en el juego (solo log):** con la partida actual, volcar prefabs obsoletos, número de referencias por tipo,
   tiempo de escaneo y nodos rotos. Criterio: coincide con las líneas del log de la misma carga. Confirmar también si los
   prefabs obsoletos conservan `ContentPrerequisiteData`.
1. **Core con tests:** clasificación por tipo, agrupación, cadena de atribución, `KeptAliveBy`, retirada de casos.
2. **Escáner:** prefabs obsoletos, índice inverso de referencias y resolución de ubicación.
3. **Nodos rotos:** réplica de solo lectura, tolerante a buffers o componentes ausentes.
4. **Sistema de UI:** bindings (R6.1), navegación, Rescan y comprobación de casos retirados.
5. **Panel React:** árbol, contadores, flechas y `no location`, partiendo de la cadena de UI de Building Collections sin
   dependencia entre ambos.
6. **Validación en el juego (R8.3, R12.5):** con MapExt a 57 km, saltar a un caso de cada tipo, borrarlo, verlo salir de
   la lista, pulsar Rescan, guardar, recargar y confirmar en el log que su `Unknown prefab ID` ya no aparece.

## Preguntas abiertas / riesgos
- **No desplegar hasta terminar la captura del cierre.** Añadir código a la sesión cambiaría las condiciones de la prueba
  con ProcDump.
- **Coste del escaneo** con cerca de un millón de entidades. Si bloquea demasiado, se reparte en varios frames.
- **Chirps y otras referencias sin ubicación** pueden mantener vivo un mod sin que el usuario pueda eliminarlas a mano. La
  fase 1 las hace visibles, y la solución queda para fases posteriores.
- **Marcas:** una empresa sin edificio no tendrá posición. Se contará como `no location`.
- **`Assert` en `TryGetFlowEdge`:** exige índices positivos y buffers presentes, así que el escáner debe validarlos antes.
- **Primera UI de este repo:** añade Node y webpack a la compilación de asset-usage.
- **Extracción futura:** si se publica sin asset-usage, la atribución se quedará en prerrequisitos y hash de paquete.
