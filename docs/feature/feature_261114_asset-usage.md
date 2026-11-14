# Feature: registro de uso de assets (mod nuevo)

**Fecha:** 2026-11-14
**Estado:** Propuesta

## Resumen
Un mod nuevo de Cities: Skylines II, independiente de Building Collections, en este repo (`CSII-asset-usage`). Hace inventario de
todos los prefabs que carga el juego, agrupados por el pack de Paradox Mods o la carpeta local de la que vienen.
Registra cuáles se usan en las ciudades que juegas, ya sea colocados o presentes al cargar, directa o
indirectamente. Con esos datos, un script genera un informe por pack: qué se usa, qué no se ha usado nunca y qué
packs se podrían quitar del playset para acortar el arranque. El mod solo informa: nunca desuscribe, desactiva ni
borra nada.

## Problema / motivación
El arranque tarda **20,5 min hasta el menú** y 25,1 min hasta que Extra Assets Importer termina (medición del
2026-11-14, `tools/startup_report.py`; el 2026-11-12 fueron 20,6 min). `docs/startup_analysis_261103.md` concluye que
el tiempo se va sobre todo en CPU cargando prefabs ("Loaded 70607 prefabs in 620 s", medido el 2026-11-08 en un
arranque más largo, de 37 min) y en los packs de assets, no en
la partida ni en mods de código. La palanca sin código es **cargar menos packs**, pero hoy no hay forma de saber qué
packs se usan de verdad en las ciudades y cuáles se instalaron y nunca se tocaron.

## Alcance
### Dentro de alcance
- **Inventario** en cada arranque: cada prefab cargado (`PrefabID`: tipo + nombre), su origen (Paradox Mods con id
  y nombre del mod, carpeta local con su ruta, o juego/DLC) y su tipo (edificio, prop, red, decal...).
- **Uso por ciudad**:
  - al cargar una partida, un job recorre todas las entidades con `PrefabRef` y apunta sus prefabs;
  - durante la partida, una consulta de entidades recién creadas (`Created` + `PrefabRef`) apunta lo nuevo, venga
    de la herramienta que venga (vanilla, Anarchy, Move It, Find It, Line Tool, Building Collections) o del juego
    (crecibles).
- **Propagación por referencias**: si un prefab está usado, todo lo que su definición referencia (sub-objetos,
  sub-redes, sub-lanes, variantes, placeholders) cuenta como **usado indirectamente**, con el prefab que lo arrastra.
- **Historial** por prefab y ciudad: primera y última fecha en que se vio y si el uso fue directo o indirecto.
- **Informe externo**: `tools/usage_report.py` lee el JSON y la carpeta `.cache` (para los tamaños) y genera un
  HTML estático. Agrupa por pack ("pack X: 3 de 240 prefabs usados"), lo ordena por número de prefabs y tamaño en
  disco, filtra por ciudad y fecha y permite bajar al detalle de prefabs.
- **Exportar candidatos**: una lista en texto o CSV con id de Paradox Mods, nombre, tamaño y número de prefabs de cada
  pack candidato, ordenada por lo que ahorraría.
- **Packs protegidos**: una lista en el JSON de ajustes que nunca entra en candidatos.

### Fuera de alcance (explícito)
- Desuscribir, desactivar o borrar packs: lo hace el usuario en Paradox Mods, Skyve o la carpeta local.
- **Extra Assets Importer** en la v1: sus prefabs se crean en tiempo de ejecución y el origen hay que deducirlo. Van a
  un grupo "EAI (sin atribuir)" que se muestra pero nunca entra en candidatos.
- Distinguir "lo coloqué yo" de "lo puso el juego".
- Panel dentro del juego (React). Puede venir después, con el mismo JSON.
- Escanear partidas sin cargarlas: una ciudad cuenta solo cuando se carga con el mod activo.
- Medir el tiempo de carga de cada pack. El peso se estima por número de prefabs y tamaño en disco.

## Reglas de negocio e invariantes
- **Un pack solo es candidato si**: aporta prefabs colocables, ninguno de sus prefabs tiene uso directo ni
  indirecto en las ciudades que el filtro incluye, no está protegido y no es del juego, de un DLC, de EAI ni
  "desconocido".
- **Packs que se revisan a mano**: un pack cuyos prefabs son sobre todo de tipos no colocables (temas, mapas, UI,
  iconos, solo editor) va a "revisar a mano", no a candidatos. Un pack sin prefabs (mod de código) no aparece.
- **Prefabs sin instancias**: algunos tipos se usan sin que haya entidades con `PrefabRef` en la ciudad: zonas,
  temas, `AssetPack`, estilos, superficies o materiales. El escaneo no puede verlos, así que un pack que aporte esos
  tipos va a "revisar a mano", nunca a candidatos. El spike fija la lista de tipos. Los crecibles que aún no han
  salido en ninguna ciudad sí pueden ser candidatos, porque no se han usado nunca; el informe avisa de que quitarlos
  reduce la variedad de las zonas.
- **Atribución incompleta**: si algún prefab de un pack no se puede atribuir con seguridad, el pack se marca como
  "atribución incompleta" y no es candidato.
- **Dependencias**: un pack del que depende algo usado nunca es candidato, aunque ninguno de sus prefabs esté en la
  ciudad. El informe dice qué prefab y qué pack lo arrastran. La propagación solo falla hacia el lado seguro: un
  prefab muy referenciado, como un tema, puede proteger packs enteros, y el informe lo muestra para que se vea.
- **`PrefabID` repetido**: si dos packs aportan el mismo `PrefabID`, su uso cuenta para todos ellos, porque no se
  sabe cuál ha ganado en la carga. Así ninguno de los dos sale como candidato por error.
- **Nunca se pierde historial**: si un pack deja de estar instalado, sus prefabs se quedan en el JSON marcados como
  "no cargado ahora", con sus fechas. El usuario decide qué ciudades y qué fechas cuentan; nada caduca solo.
- **Identidad de un prefab**: `PrefabID` (tipo + nombre). Si dos packs traen el mismo `PrefabID`, que es la causa de
  los "Duplicate prefab ID" del log, se guardan los dos orígenes y el informe lo señala.
- **Sin Harmony**: solo ECS y APIs del juego (R4.4). Así funciona con cualquier herramienta y aguanta mejor los
  parches.
- **No se pierde uso**: lo que se acumula durante la partida se escribe también cada pocos minutos, fuera del
  fotograma, además de al guardar y al salir. Así, un cierre forzado o un cuelgue solo pierden los últimos minutos.
- **Sin doble conteo**: el escaneo de la carga y la consulta `Created` no deben contar dos veces la misma entidad. En
  la carga también aparecen entidades marcadas como `Created`, y el spike decide si basta con la consulta o si hacen
  falta las dos.
- **Rendimiento**: el escaneo al cargar va en un job (R4.2). El objetivo es unos segundos en una ciudad grande y nada
  perceptible por fotograma durante la partida. El inventario de ~70.000 prefabs se escribe una vez por arranque.
- **Datos** en `ModsData/asset-usage/`, en JSON con versión de esquema y escritura atómica (R9.1, R9.2). El mod nunca
  toca datos de otros mods (R9.3).
- **Convenciones del repo**: código, logs, tests y herramientas en inglés; sin comentarios en el código; versión
  semver patch por cambio.

## Modelo de datos (borrador)
`usage.json` (esquema versionado):
- `packs`: id → `{ source: pdx|local|game|eai|unknown, pdxId, name, path, sizeBytes?, lastLoaded }`
- `prefabs`: `PrefabID` → `{ type, packs: [packId], placeable, loadedNow, refs: [PrefabID] }`
- `usage`: `PrefabID` → `{ cityId → { firstDirect, lastDirect, firstIndirect, lastIndirect, via: [PrefabID] } }`.
  Las fechas van por separado porque un prefab puede ser directo en una sesión e indirecto en otra; `via` guarda
  todos los prefabs usados que lo arrastran, no solo uno.
- `cities`: cityId → `{ name, lastLoaded }`. La ciudad se identifica por el id del save, no por su nombre. El spike
  confirma de dónde sale y debe resolverlo antes del paso 2.

## Plan de implementación
Este repo (`CSII-asset-usage`), separado de Building Collections. Misma estructura que
Building Collections: `Core/` con lógica pura y tests, `Systems/` con ECS, y `tools/` con el script del informe. Se
reutilizan ideas y el csproj de Building Collections, sin dependencia entre los dos.

0. **Spike (en el juego, solo log)**:
   - volcar el origen de cada prefab (`PrefabBase.asset`, su base de datos, ruta y meta de Paradox Mods) y ver qué
     porcentaje se atribuye limpio a un pack;
   - medir el escaneo de `PrefabRef` en la ciudad más grande;
   - fijar el punto de enganche de "partida cargada" y comprobar si al cargar se marcan entidades como `Created`
     (doble conteo);
   - comprobar que la consulta `Created` ve los crecibles y lo que colocan Anarchy y Move It;
   - listar los tipos de prefab que se usan sin entidades con `PrefabRef` (zonas, temas, `AssetPack`, estilos,
     superficies...);
   - listar qué componentes de los prefabs referencian otros prefabs (sub-objetos, sub-lanes, sub-redes, variantes,
     placeholders);
   - decidir la identidad de la ciudad (id del save), que bloquea los pasos 2 y 3;
   - mirar si Skyve importa una lista de ids o un playset en JSON.
   El nombre del mod es **asset-usage** (decidido el 2026-11-14): datos en `ModsData/asset-usage/`.
1. **Inventario**: un sistema que, al acabar la carga de prefabs, escribe `packs` y `prefabs` (con `refs`). Tests en
   `Core` de la clasificación por origen y de "colocable".
2. **Uso al cargar**: un job sobre las entidades con `PrefabRef` al terminar de cargar la partida, que actualiza
   `usage` y `cities`.
3. **Uso en partida**: un sistema sobre `Created` que acumula en memoria. Escribe cada pocos minutos, al guardar y al
   salir de la partida, nunca en cada fotograma.
4. **Propagación** en `Core` (pura y con tests): cierre transitivo sobre `refs` a partir del uso directo, con `via`.
   Tests con ciclos de referencias y con `PrefabID` repetidos en dos packs.
5. **Informe** `tools/usage_report.py`: HTML con packs (usados, revisar a mano, candidatos, EAI/desconocido), filtros
   por ciudad y fecha, detalle por prefab y tamaños de `.cache`.
6. **Exportar candidatos**: texto o CSV desde el informe, más el formato de Skyve si el spike lo confirma.
7. **Validación en el juego** (R8.3): cargar dos ciudades, colocar con vanilla, Anarchy y Move It, dejar crecer zonas
   y comprobar que el informe lo refleja. Probar a quitar un pack candidato y confirmar que las ciudades cargan sin
   avisos de "missing reference" nuevos.

## Preguntas abiertas / riesgos
- **Atribución**: si los prefabs de Paradox Mods no exponen de forma fiable su id de mod, el spike puede obligar a
  deducirlo por la ruta en `.cache/Mods/pdx_mods/<id>_...`.
- **EAI** queda fuera de la v1, y es justo una parte cara del arranque (superficies PNG, ~7 GB leídos tras el menú).
  Atribuirlo por su carpeta de pack es la candidata para la v2.
- **Referencias de prefab** que no salgan en el spike, por ejemplo las resueltas por nombre en tiempo de ejecución,
  pueden dar falsos "no usado". Mitigación: la lista de protegidos y probar a quitar el pack antes de borrarlo.
- **Ciudades no cargadas**: un pack usado solo en una partida que no has abierto con el mod saldrá como candidato.
  El informe debe decir qué ciudades se han escaneado.
- **Identidad de la ciudad**: si se renombra o se guarda con otro nombre puede contar como ciudad nueva.
- **Ahorro real**: el informe estima, no mide. La confirmación es arrancar con y sin los candidatos y comparar con
  `tools/startup_report.py`, del repo de Building Collections (se puede copiar aquí si hace falta).
