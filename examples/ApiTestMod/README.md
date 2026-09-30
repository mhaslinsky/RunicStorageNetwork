# RSN API local test consumer

This separate plugin is for a disposable **single-player** test world. It deliberately refuses multiplayer commands and processing: its simple saved input buffer is authoritative only on the local server. The RSN API itself has a server/client protocol; this consumer does not claim to demonstrate a complete multiplayer machine integration.

The test **consumes real resources** from connected chests. Successful payments increment a saved input-credit counter on the test workbench. They do not create replacement items. Destroying this test piece does not refund its accumulated credits.

## Install

1. Import `RunicStorageNetwork-0.8.8-api-preview.1.zip` into a separate test profile with the usual BepInEx and Jotunn dependencies.
2. Extract this archive's `plugins/RSN.ApiTest` into that profile's `BepInEx/plugins`. Keep the mod separate from the RSN directory.
3. Set `ExperimentalUnloadedNetworks = true` in `local.runicstoragenetwork.cfg`, then fully restart the game.
4. Build **API Test Consumer** under Hammer / Crafting within the supply radius of a core or connected relay. It looks like a workbench and costs one wood. Stand within 8 metres of it.
5. Enable the Valheim console (`-console` launch option) and open it with F5. No `devcommands` is needed for these commands.

## Commands

```text
rsn_api_test status
rsn_api_test list
rsn_api_test amount Wood
rsn_api_test pay Wood 5
rsn_api_test status
rsn_api_test retry
```

`amount` accepts an optional exact quality. `pay` accepts a quantity (1–1000) and optional quality: `rsn_api_test pay Wood 5 1`. Use the prefab name, not a translated item name. `retry` resubmits the last identical request from this server session, without incrementing the saved input counter again.

First discovery is asynchronous. Repeat `list` or `amount` after a moment if the status is `NotReady` / `Updating`. Normal successful progression is `Pending` → `Paid` → `Applied`; the saved counter should increase by the requested amount exactly once.

## Local checklist

- Put ten wood in connected chests, pay five, then check five remaining and five paid credits.
- Call `retry` repeatedly: neither the remaining wood nor the credit counter should change.
- Split the wood between several chests and repeat.
- Ask for more than is available. The intent enters `Waiting`; add resources and it should complete automatically.
- Move/remove/rebuild a connecting relay while an unpaid intent waits. Restoring the connection should allow the intent to proceed.
- Test a chest outside the loaded area, including entry into the world far from the core.
- Open a selected chest while submitting payment. Payment should wait/retry without duplicating materials.
- After completion, open and dismantle the chest: no API reservation should remain.
- With the experimental option disabled and a full restart, reads report `FeatureDisabled` and resources remain untouched.

The consumer saves the pending request before asking for payment, saves `Paid` before adding input credit, and saves the credit plus `Applied` marker together. An unresolved request from a previous server session enters `RecoveryRequired` instead of inventing a success or retrying a possible payment. Use a disposable test piece/world for that crash-recovery scenario.

Source: `examples/ApiTestMod/ApiTestMod.cs`. Build both local archives with `tools/BuildApiPreview.ps1`; it does not start the game or install anything into a profile.
