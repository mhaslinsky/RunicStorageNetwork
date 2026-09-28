# Building from source

This repository contains the mod's C# source, isolated logic tests and DLL compilation script. The separate model-authoring projects and the full asset/release pipeline are not included. Source snapshots and Editor integrations for the upcoming Storage Codex stand and closed Runic Codex item visual are available in `model-sources/terminal`, `model-sources/codex`, `tools/BuildTerminalAssets.cs` and `tools/BuildCodexAssets.cs`. Compiling the DLL does **not** create an installable mod package: the matching release's `Assets/rsn_core_windows` bundle is also required.

`tools/BuildIcons.cs` and `tools/IconSilhouette.shader` preserve the approved icon-rendering recipe used by the separate Unity asset pipeline. They are Editor sources, not plugin sources or a standalone asset build; they require the author's asset project and local game materials. The mod icon uses the same render as the core's build-menu icon.

## Requirements

- Windows and PowerShell 5.1 or newer.
- Unity Editor **6000.0.75f1**, including its bundled C# compiler and .NET Framework reference assemblies. The commands below do not open the Unity Editor or launch Valheim.
- To build the plugin: your local Valheim installation and a BepInEx profile containing Jötunn. The current source targets Valheim **1.0.15**, BepInEx **5.4.23.5** and Jötunn **2.30.2**.

Game and dependency DLLs are local compilation references. They are not stored in this repository or copied to the output.

## Compile the DLL

Run from the repository root, replacing the example paths with your own:

```powershell
.\tools\Compile.ps1 `
  -EditorData 'C:\Path\To\Unity\6000.0.75f1\Editor\Data' `
  -ValheimManaged 'C:\Path\To\Valheim\valheim_Data\Managed' `
  -BepInExPath 'C:\Path\To\Profile\BepInEx'
```

The script expects Jötunn at `plugins\ValheimModding-Jotunn\Jotunn.dll` inside the supplied BepInEx folder. Output: `artifacts\compile\RunicStorageNetwork.dll`. Use `-Output` to select another directory.

For local testing, use the asset bundle from the release matching the source version. Future source changes to assets may require a new matching release bundle. Do not replace an installed mod while the game is running.

## Run isolated logic tests

These tests require only the Unity compiler/reference assemblies and Windows, without game or mod DLLs:

```powershell
.\tools\Compile.ps1 -Tests `
  -EditorData 'C:\Path\To\Unity\6000.0.75f1\Editor\Data' `
  -Output '.\artifacts\tests'
```

They cover resource planning, network graphs, reservations, recovery, localization, resource counts, container and build-tool allow/deny rules, and recipe name resolution. They do not simulate Valheim networking, Harmony patches or the game UI; multiplayer changes also need in-game testing.

The same command also runs `BuildToolRuntimeTests.exe`. It compiles the production build-tool policy, planner and selected build/menu methods against game stand-ins to check shared menus, late registration, serving-tray supply and inventory-only fallback. It does not load the game or apply Harmony patches.

It also runs `RecipeRuntimeTests.exe` against the production recipe index and transaction requirement-selection methods. This covers live recipe changes, duplicate names, different registration orders across peers, stale operations and rate-limited diagnostics. These tests use stand-ins; they do not establish compatibility with a mod's custom crafting callbacks.

Recipe requests now carry a versioned content key in the existing target field. All participating clients and the server need this implementation for network crafting; older name-only requests are rejected. Use matching builds on all peers.

The command also runs `StorageIndexRuntimeTests.exe`, `CraftInspectionRuntimeTests.exe` and `CraftPreparationRuntimeTests.exe` against production code with game stand-ins. They cover incremental updates across 95 containers, inventory events and synchronized revisions, owner changes, delayed replies, reservations acquired only on click, cancellation, repeated clicks and recipe checks before starting a craft. Pure index tests also cover queries with 5,000 unrelated sources. These are correctness checks, not in-game performance measurements.

Starting with 0.6.0, ingredient-inspection replies include the owner's storage revision. Update all clients and the server together; the older reply format is incompatible.

The `feature/core-terminal` branch adds the Storage Codex access point and a Runic Codex item crafted at a forge. Interact with the Storage Codex inside core/relay supply coverage to open storage; interacting with a core renames the network. For withdrawals, the existing operation `Station` field identifies the placed Storage Codex while `Core` identifies its storage network. The coordinator validates the stand's synchronized record, creator, distance, ward and supply coverage; it does not require a loaded stand instance on the host.

`TerminalRuntimeTests.exe` exercises production delivery/receipt handlers and the extracted coordinator access-point check against stand-ins: custom item data, quantity/quality checks, duplicate messages, cancellation, stand destruction, lost coverage, wards, different open stands, full inventories and partial-insertion rollback. These do not verify real multiplayer or item registration. Use the matching build on all peers. No release publication is part of this branch's local build.

## Save local paths

Optionally create `.local\BuildPaths.psd1`:

```powershell
@{
  EditorData = 'C:\Path\To\Unity\6000.0.75f1\Editor\Data'
  ValheimManaged = 'C:\Path\To\Valheim\valheim_Data\Managed'
  BepInExPath = 'C:\Path\To\Profile\BepInEx'
}
```

The script loads these defaults; explicit parameters take priority. The `.local` directory is ignored by Git.

`RunicStorageNetwork.csproj` is available for IDE use. Supply `EditorData`, `ValheimManaged` and `BepInExPath` as MSBuild properties or in an ignored `.local\Build.props` file. The tested compilation path is `tools\Compile.ps1`.

## Unloaded-network experiment

The optional unloaded-network feature is included in `main` and disabled by default. To build a local test package, configure the paths above, then run:

```powershell
.\tools\BuildUnloadedExperiment.ps1 -BasePackage 'C:\Path\To\RunicStorageNetwork-0.8.1.zip'
```

This compiles the DLL, runs isolated tests, reuses the verified 0.8.1 assets and validates a six-file local package at `dist/RunicStorageNetwork-<version>-unloaded-experiment.zip`, using the manifest version. It neither installs nor publishes the package. `UnloadedNetworkRuntimeTests.exe` uses game stand-ins to exercise cold-start discovery, container eligibility, deferred work, live-instance handover, reservations and inventory save guards.

Optional game patches and the unloaded inventory scheduler are installed only when the setting is enabled at plugin startup. They use a separate Harmony owner; a failed installation rolls back its patches and restores ordinary source lookup without disabling normal supply. With the experiment off, craft proposals use the original registered-member list and container discovery uses the original traversal. The ordinary inventory update routine matches the stable 0.8.1 implementation. Only a passive protocol refusal remains registered on an opted-out server so an opted-in client can fall back without trying to inspect remote inventories.

For an installed experiment, `ZNetScene.Awake` attaches the new world, then saved records are indexed after `ZNet.Start` completes the world load. Both old and chunked saves are loaded by that method; scanning in scene Awake is too early. Retained live containers use their existing inventory even if their surrounding zone is unloaded, provided the scene instance and indexed world record still match. Normal eligibility, placement, ward and reservation checks still apply.

The `[Experimental] ExperimentalUnloadedNetworks` setting defaults to `false` and is sampled at plugin startup. Changing it requires fully restarting the game or dedicated server; leaving and re-entering a world does not change installed patches. Enable it on the host/dedicated server and all clients using the same experimental build. The server indexes saved object metadata once, then creates inactive adapters for requested inventories. It does not simulate distant creatures, factories or entire zones. Containers use the existing allow/deny rules. An inventory must survive an unchanged save/load round trip before remote writes are allowed; custom storage formats and older inventory serialization may require visiting the chest first. This is not proof of compatibility with every container mod.

Clients request discovery at an authenticated player's access point. The server sends paged IDs and revision hints, followed by native targeted ZDO replication. Client adapters are read-only; unloaded inventory writes remain with the server, through the existing prepare/commit/release protocol. A connected owner is never displaced. A client unloading an unreserved live container saves it and releases ownership through native replication. Unreserved containers left by disconnected peers can be reclaimed; an unresolved reservation is never cleared just because its owner disconnected.

The save/ownership handoff is restricted to the `ResetZDO` call inside `ZNetScene.RemoveObjects`, with an exact single-call transpiler guard. Do not patch `ResetZDO` globally: `ZNetScene.Destroy` calls it before checking ownership for permanent record deletion, after materials have already dropped. The isolated destruction fixture preserves that order and checks that the record and adapter disappear after one material return. For in-game validation, have a non-host client dismantle a chest with the experiment enabled, repeat by damaging a chest, and verify it does not return after leaving/re-entering the area or reconnecting. Also verify ordinary area unload still makes its inventory available remotely.

Discovery subscriptions expire after six seconds without access. Only active subscribers receive changed records; unchanged ownership attempts do not recount resources. Network movement within the same component does not resend the full catalog. Native records and catalog pages have per-frame budgets. The isolated fixture exercises both production unloaded-network classes, including cold remote discovery, delayed/out-of-order data, access checks, ownership transitions, idle expiry and client write refusal. It does not simulate game transport or prove multiplayer correctness.

Manual validation on a backed-up test world: enter near the far end of a relay chain without visiting its core, inspect and craft with distant resources, withdraw through a Storage Codex, return to the chests and verify the remaining counts, then save and re-enter. Repeat with a modded container, an interrupted/broken relay chain, and the option disabled. In multiplayer, test both a player-hosted and dedicated server: keep everyone away from the core, then have two players craft/withdraw simultaneously. Have one player add/remove items directly while another browses the codex, move between the chest and distant network, disconnect the chest owner, and verify persisted counts after a restart. Also check disabled-server fallback. Runtime and performance results remain unverified until these checks are performed in game.

## Repository contents

- `src/`: plugin and shared logic.
- `tests/`: isolated logic tests.
- `tools/Compile.ps1`: DLL and test compilation.
- `README.md`: player documentation in English and Russian.
- `CHANGELOG_EN.md`: English release notes used in mod packages.
- `CHANGELOG.md`: Russian release notes.
- `.github/ISSUE_TEMPLATE/`: English and Russian bug report forms.

The root `.gitignore` allows only the public source and documentation paths. Build output, logs, local configuration, game references, Unity caches and authoring notes stay outside Git. Add new public paths explicitly when needed.

To publish a prepared package through GitHub Actions, see [PUBLISHING.md](PUBLISHING.md).
