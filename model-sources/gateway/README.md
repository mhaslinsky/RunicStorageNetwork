# Runic Gateway source

Approved v14 appearance: ordinary Valheim stone, Yggdrasil timber, iron and silver fittings, engraved runes, blue-grey banners. The v10 FBX is kept unchanged; `tools/GatewayAssetBuilder.cs` and `tools/GatewayStoneFinish.cs` reproduce the approved geometry and UVs. Native game materials are resolved at runtime and are not redistributed.

The asset builder also adds the banner symbols to the reverse face, following the actual cloth folds. Both sides share the existing symbol renderer and material. This adds 390 triangles (9,459 total) without additional renderers or runtime geometry work. The bundle validation checks that both copies remain on opposite sides of the cloth; front and back previews are included in local build artifacts.
