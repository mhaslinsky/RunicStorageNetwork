# Runic Builder's Codex

Approved `RunicBuilderCodex/model_v01/RunicBuilderCodex_v01.blend`, exported by `tools/ExportBuilderCodex.py`. The authoring file is never saved by the exporter. See `source.json` for hashes, exact bounds and FBX round-trip checks.

- 1,389 triangles; nine material groups, including a separate clasp leather material.
- The export retains evaluated geometry and authored corner normals. Camera, lights and fitting mannequin are excluded.
- The Blender Preview scene places the belt loops at the origin with the book hanging down. FBX uses metre units, forward `-Z`, up `Y`.
- `tools/BuildBuilderCodexAssets.cs` imports this into the separate Unity 6000.0.75f1 build project. It matches the existing Runic Codex's dark leather, silver and cyan rune palette.
- The prefab has an active `attach` for the dropped item and an inactive `attach_Hips` for vanilla Utility equipment. Only the root has a dropped-item collider. Both visuals share the imported meshes.
- Native game material bindings are resolved at runtime and for Editor photographs; no game material or character assets are distributed.
- Internal prefab: `RSN_RunicBuilderCodex`. The runtime plugin registers the black forge recipe and per-item network binding; see the main README for use and ingredients.

`icon.png` is the generated 256×256 inventory icon. Equipped screenshots use an in-memory vanilla rig rendered as a neutral mannequin; they do not verify animation or armour clipping in game.
