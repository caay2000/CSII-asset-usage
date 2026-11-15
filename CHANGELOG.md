# Changelog

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
