# Changelog

## 0.1.1 (2026-11-15)
- Informe: botones Protect y Exclude en cada candidato y en cada pack a revisar, con sus listas en `protected.txt` y
  `excluded.txt` (esta última, para quitar los packs en Skyve), y la barra de secciones y el filtro siempre visibles.
- Un pack cuyo único tipo que no se ve en la ciudad es el filtro del pack (`AssetPackPrefab`) ya cuenta como candidato.
  Por ejemplo, los Static Ploppables.

## 0.1.0 (2026-11-14)
- Primera versión interna: inventario de prefabs por pack, uso al cargar la ciudad y durante la partida, propagación
  por referencias, historial por ciudad en `ModsData/asset-usage/usage.json` y script de informe
  `tools/usage_report.py`.
