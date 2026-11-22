# Changelog

## 0.1.10 (2026-11-22)
- Botón `Remove all` para los objetos que faltan y tienen posición, con confirmación. No toca edificios, ni objetos de
  carreteras, pilares, servicios o conexiones exteriores.

## 0.1.9 (2026-11-22)
- Cambiar marcas: una empresa sin ninguna marca compatible se deja como está, para que el juego no muestre su nombre como
  "brand is null".
- Eliminar o sustituir superficies incluye ahora las que están dentro de edificios. Eliminar excluye las áreas de
  almacén, extracción y terreno. Sustituir excluye además las áreas especiales, y solo ofrece superficies normales.

## 0.1.8 (2026-11-22)
- Panel de problemas de carga agrupado por tipo de error y, dentro, por mod.
- Aviso al cargar una ciudad con problemas, con botones para abrir el panel o saltarlo.
- Arreglos en bloque con confirmación: cambiar todas las marcas que faltan y eliminar o sustituir todas las superficies
  que faltan. Las superficies que forman parte de un edificio no se tocan.
- El log describe las entidades sin ubicación para poder identificarlas.
- Los nodos rotos indican qué son (carretera, línea eléctrica, tubería...) y si están bajo tierra. Al ir a uno se abre la
  vista de electricidad y, si hace falta, la subterránea.
- Al solucionar un problema, el panel pasa solo al siguiente y abre su ventana.

## 0.1.7 (2026-11-22)
- Panel de problemas de carga en la partida, de solo lectura: árbol mod > tipo > prefab con contadores, flechas para ir
  con la cámara a cada caso, grupo de nodos rotos y botón Rescan. Lo que se elimina sale de la lista sin recargar.
- Una misma referencia ya no se cuenta dos veces en el log.

## 0.1.6 (2026-11-22)
- Diagnóstico de problemas de carga, solo en el log: al terminar de cargar una partida lista los prefabs que faltan, qué
  los referencia, qué mod los aporta y los nodos con el grafo eléctrico roto. No modifica la partida.

## 0.1.5 (2026-11-15)
- La carga de la partida ya no espera al inventario (antes, de 15 a 58 s bloqueando el juego).
  - Si los prefabs cargados son los mismos que en el `inventory.json` guardado, que se lee en segundo plano al arrancar
    el juego, se reutiliza. Se compara una huella de sus nombres, y el guardado no puede tener más de 7 días.
  - Si no, el inventario se construye poco a poco, 4 ms por fotograma, mientras se juega.
  - El uso de la ciudad se guarda cuando el inventario está listo.
  - Si se sale de la ciudad o del juego antes de que esté listo, se guarda solo el uso directo, sin esperar.
  - La huella incluye la versión del mod, así que el inventario se rehace al actualizarlo.

## 0.1.4 (2026-11-15)
- Informe: dependencias entre packs, sacadas de la caché de mods del juego.
  - Cada pack indica qué packs lo necesitan.
  - Un candidato del que depende un pack usado pasa a revisar a mano.
  - Se avisa cuando un pack excluido lo necesita otro que se conserva.

## 0.1.3 (2026-11-15)
- Informe: miniatura de cada pack de Paradox Mods (la portada de la caché de mods del juego), que se amplía al pasar el
  ratón. El nombre del pack sale también de esa caché.

## 0.1.2 (2026-11-15)
- Informe: los enlaces a Paradox Mods se abren en una pestaña nueva.

## 0.1.1 (2026-11-15)
- Informe: botones Protect y Exclude en cada candidato y en cada pack a revisar, con sus listas en `protected.txt` y
  `excluded.txt` (esta última, para quitar los packs en Skyve), y la barra de secciones y el filtro siempre visibles.
- Un pack cuyo único tipo que no se ve en la ciudad es el filtro del pack (`AssetPackPrefab`) ya cuenta como candidato.
  Por ejemplo, los Static Ploppables.

## 0.1.0 (2026-11-14)
- Primera versión interna: inventario de prefabs por pack, uso al cargar la ciudad y durante la partida, propagación
  por referencias, historial por ciudad en `ModsData/asset-usage/usage.json` y script de informe
  `tools/usage_report.py`.
