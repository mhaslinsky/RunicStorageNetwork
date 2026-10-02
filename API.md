# Network resources API

Assembly: `RunicStorageNetwork.dll` 0.8.8. Namespace: `RunicStorageNetwork.API`. Contract version: `NetworkResources.ContractVersion == 1`.

Available starting with **Runic Storage Network 0.8.8**. The API requires distant storage, enabled by default for new configurations from 1.0. You can turn it off in [Configuration](https://github.com/rerit33/RunicStorageNetwork/wiki/Configuration#experimental-distant-storage). Existing settings are kept when updating.

The author has tested the API in single-player near a core and with the core unloaded, consuming iron nails in different quantities. Reviewed logs contain successful one- and five-nail payments without API errors. Multiplayer testing of the API is still pending. The automated suite uses game stand-ins and does not replace that testing.

## What it provides

The API lets a stationary machine find its connected network, inspect available materials, and pay for a production cycle. RSN removes the requested resources from network storage and returns a receipt. **It does not spawn items, add them to the machine, or run that machine's recipe.** The integrating mod applies the paid input to its own production system once.

This is separate from player crafting and the Storage Codex interface. It does not add a fallback to the player's inventory or enable APIs while the experimental option is off.

## Three operations

```csharp
ResourceSnapshot NetworkResources.GetResources(ApiContext context);
ResourceAmountResult NetworkResources.GetResourceAmount(ApiContext context, ResourceKey resource);
ConsumeOperation NetworkResources.TryConsumeResources(
    ApiContext context, ConsumeRequest request, Action<ConsumeResult> completed = null);
```

Call on Unity's main thread. The read methods return cached immutable data immediately; initial discovery and remote refresh happen later. They do not reserve inventories. Keep polling at a modest interval, such as once per second.

API access requires `ExperimentalUnloadedNetworks = true` on the server and participating clients, enabled RSN supply, and a full restart after changing the experimental option. There is no fallback to ordinary player crafting. With the option disabled at startup, the API host, RPC handlers and optional API patches are not installed. Accepted payments and receipts are allowed to finish if supply is subsequently disabled.

## Declare a consumer

Reference `RunicStorageNetwork.dll` from version 0.8.8 as a compile-time dependency alongside your usual Valheim, Unity and BepInEx references. Do not bundle a second copy of RSN or the game's DLLs in your mod. For a required integration, add this attribute to your BepInEx plugin class:

```csharp
[BepInDependency("local.runicstoragenetwork", "0.8.8")]
```

Use `using RunicStorageNetwork.API;` in your consumer code. If RSN is optional for your mod, use a soft dependency and isolate the RSN-referencing code so that your plugin can load when that assembly is absent.

Register a stationary player-built prefab with a `Piece` and persistent `ZNetView`, then add the marker on **every peer, including the server**:

```csharp
// During prefab registration, before spawning it:
var marker = prefab.GetComponent<NetworkConsumer>()
    ?? prefab.AddComponent<NetworkConsumer>();
marker.ModId = "your.mod.guid";

// Later, for a spawned instance with a valid persistent ZNetView:
if (!consumerView.IsValid() || !consumerView.IsOwner()) return;
var context = new ApiContext(consumerView.GetZDO().m_uid, "your.mod.guid");
```

The current ZDO owner initiates new payments and reads. Ownership and the registered prefab marker are checked on the server; a caller cannot supply a different player identity or arbitrary position. The piece creator supplies ward permissions. Only stationary, eligible, public storage is supported. A prefab marker declares the integration, not a sandbox against other installed mods.

The machine must be within a core/relay supply radius. Its network is resolved from saved world records; it does not need a loaded core or an open player interface. Network display names do not identify payments.

## Reads

`ResourceKey` is an ordinal prefab name plus exact quality. There is no item-category allowlist. Existing container eligibility/access rules still apply.

```csharp
var wood = new ResourceKey("Wood", 1);
var snapshot = NetworkResources.GetResources(context);
var amount = NetworkResources.GetResourceAmount(context, wood);

if (amount.Status == ApiStatus.Ready && amount.Amount.HasValue)
{
    long availableWood = amount.Amount.Value;
    // Safe to display as an observed count, not as a reservation.
}
```

- `Ready` and `IsComplete` describe a complete current observation, not a reservation or future guarantee.
- `Partial` exposes known `ObservedAmount` values and the number of unknown sources. `ResourceAmountResult.Amount` is null until the observation is complete. Unknown is never represented as an exact zero.
- A known item absent from a complete network has amount zero. An unregistered item returns `UnknownItem`.
- `Updating` / `NotReady` mean discovery or refresh is pending. Stale values are observations only.
- `NoNetwork`, `NotOwner`, `AccessDenied`, `FeatureDisabled`, `SnapshotLimit` describe why a usable current view is unavailable.
- `SessionId`, `Revision`, `Age`, and `Recovery` let an integration correlate its saved intent and the current server observation. `Recovery.OwnerEpoch` is the current ZDO ownership revision within this server session. `ObservedAt` is a server runtime timestamp, not a wall-clock date; use `Age` for freshness.

Scope caches are shared by network and creator permissions, expire after 30 seconds without interest, and refresh cooperatively. The reverse resource index selects candidate sources without scanning every inventory per API call. Inventory content is decoded again when its revision/owner changes or after the bounded freshness check.

## Payment and replay

Persist the intent **before** calling the API. Supply the current server `SessionId`, a monotonically increasing positive `Sequence` for this consumer, an immutable `CycleRevision`, and the whole resource list:

```csharp
var request = new ConsumeRequest(
    snapshot.SessionId,
    savedSequence,
    savedCycleRevision,
    new[] {
        new ResourceRequirement(new ResourceKey("Wood", 1), 5),
        new ResourceRequirement(new ResourceKey("Iron", 1), 2)
    },
    ResourceMatchPolicy.AnyMatchingInstance);

var operation = NetworkResources.TryConsumeResources(context, request);
// Poll operation.IsFinal / operation.Result, or supply one completion callback.
```

This snippet assumes `snapshot.SessionId`, `savedSequence` and `savedCycleRevision` belong to a **new intent that your machine has already persisted**. Do not replace the session or increment the sequence each time a callback is delayed. `CycleRevision` is your stable recipe/production revision string, not the network snapshot revision. If a recipe changes, keep an accepted intent unchanged until its payment is resolved; the next intent can use the new recipe.

Use one consumer identity per machine. For new work, sequences increase across that consumer's saved history; do not reset them whenever the component is recreated. Duplicate resource requirements are combined before comparison, so merely reordering identical requirements does not create a different request.

Explicit `AnyMatchingInstance` consent is required: the contract aggregates prefab + quality. It does not preserve or select custom item metadata, durability or cosmetic variants for the consuming machine. Do not use this policy when an exact unique item must be transferred intact.

`AdmissionCode` and payment outcome are separate:

| Result | Meaning |
| --- | --- |
| `Success` | Every requested quantity was saved out of the source inventories. The receipt authorizes that exact paid input once. |
| `NoDebit` | Nothing remains debited by this operation; any partial debit has been restored. A new sequence is safe after the final source cleanup. |
| `OutcomeUnknown` | A server-session boundary prevents proof of the old outcome. Do not create output or issue replacement payment automatically. |
| No outcome (`null`) | Admission/observation result such as `Busy`, `Throttled`, `NotOwner` or `RequestConflict`. It is not proof that an earlier payment failed. |

Retrying the same normalized payload under the same identity observes the original operation/receipt. Changing its resources, cycle, policy or `StartWithin` conflicts. Older sequences return `ExpiredRequest` once a later sequence has replaced their receipt. An accepted operation cannot be replaced until source cleanup completes. A temporary admission rejection can be retried with the same request.

One local operation handle retains the first non-null callback; it runs at most once on a later main-thread tick. Exceptions in it are logged and do not change the receipt. Immediate argument/thread/disabled-host rejections return a final handle directly and do not schedule callbacks. Always inspect the returned handle.

## Automatic recovery

Before the first debit, a request re-resolves its network, refreshes relevant observations/access, avoids failed sources, and replans from fresh owner-confirmed inventory data. The default start budget is ten seconds, with at most two fast and two delayed repair passes. A locked or still transferring source is polled within that budget. Persistent shortage/access denial ends with `NoDebit`, never invented success.

Once any write starts, the start deadline cannot cancel it. `Recovering` retains the original ID and source reservations until the server has saved all removals or restored its own deltas. It cannot charge an alternate network to cover an uncertain old payment. Diagnostics go to the log; the API does not create player error popups.

After `NoDebit`, an integration should keep the same production intent, wait for usable counts/access, and then try a new sequence. Do not require the player to rebuild the network for a temporary fault. The sample implements this waiting loop.

## Multiplayer and input ownership

The server coordinates and writes API payments. It asks connected source owners to freeze an idle inventory and return its fresh serialized contents, then takes ownership before writing. The API uses the same source gate and inventory mutation guards as player crafting. Unpaid handoffs are released on cancellation; a client does not blindly unlock an offered inventory on a timer. Late native ZDO frames from an old source owner are filtered, including frames with a newer content revision.

All participants owning sources must run this API build. A machine owner may disconnect after handoff: the server still resolves the original transaction. A new owner reads `Recovery` and resubmits its original immutable request. The server-side integration may replay a known receipt even when it does not own the consumer, but cannot initiate a new request on that consumer's behalf.

Successful payment does **not** make arbitrary output code exactly-once. A production integration must own a server-authoritative `Pending → Paid → Applied` input buffer, verify the same receipt on the server, and commit credit with its applied marker atomically. A client callback that spawns items or writes an unsynchronized "applied" flag is insufficient. Different ownership of the machine and input buffer must be handled by the integrating mod.

Receipts are in-memory for the server session. There is no atomic world-save transaction spanning source chests and another mod's machine. A crash/restart with unresolved intent requires explicit reconciliation by that integration; automatic duplicate production is forbidden.

## Status handling

`ApiStatus` explains readiness or refusal; only `ConsumeResult.Outcome` confirms the payment outcome. `AdmissionCode == Ready` means the call was admitted locally, **not that the resources have been paid**. Wait for `IsFinal` and inspect `Result`.

| Status | Integration response |
| --- | --- |
| `Ready` | Use a complete read as an observation, or continue waiting for the admitted operation's result. |
| `Partial`, `Updating`, `NotReady` | Keep unknown/stale counts distinct from zero and refresh later. |
| `NoNetwork`, `AccessDenied`, `SourcesUnavailable`, `InsufficientResources` | Keep the production intent. After a definitive `NoDebit`, wait for conditions to improve before starting its next payment attempt. |
| `Busy`, `Throttled` | Back off, respecting a positive `RetryAfter` when provided; retry the same request. Do not submit every frame. |
| `CapacityExceeded`, `RecoveryCapacityExceeded` | Reduce concurrent work and wait; do not generate replacement identities to bypass the limit. |
| `NotOwner` | Only the current consumer owner starts new work. Preserve the saved intent for the new owner to reconcile. |
| `FeatureDisabled` | Explain the required experimental setting in your own integration settings; do not silently fall back to another debit path. |
| `Unavailable`, `IncompatibleVersion` | Wait for a usable session or matching mod versions. An unresolved request from an older session must not become a fresh payment automatically. |
| `UnknownItem`, `UnsupportedConsumer`, `InvalidRequest`, `WrongThread` | Correct the prefab, consumer registration, request or calling thread. |
| `RequestConflict`, `ExpiredRequest` | Reconcile your saved intent/receipt. Do not assume the resources were never debited. |
| `StartExpired`, `PlanLimitExceeded` | Check the final outcome, then retry later after `NoDebit` or redesign an oversized operation. |
| `SnapshotLimit` | The requested view exceeds a read limit; an empty/partial view is not proof that storage is empty. |

Only `Success` authorizes using the paid resources. Checking a displayed count, receiving a callback or seeing `Completed` on a rejected handle is not sufficient. When combining callback and polling, route both through the same receipt-application guard.

## Limits in 0.8.8

- 32 distinct requirements, 100,000 units per key, exact quality 1–10,000, at most 32 selected sources per operation.
- 64 active API operations globally, 16 per initiating peer, 8 per network. API operations also count toward the common 256-operation ceiling and cannot occupy more than a quarter of it. Earlier admitted requests have priority; waiting player operations have priority on shared sources.
- Four new payments per second per peer, sixteen globally; separate bounded read/control rates and outgoing queues.
- 4,096 consumer histories, one retained request/receipt per consumer. No silent eviction of payment high-water marks.
- 128 read scopes, 12 MiB compact inventory cache plus 4 MiB network-view budget, 16 MiB client page budget. Up to 4,096 resource entries in a full transmitted snapshot; larger views return `SnapshotLimit`.
- 1 MiB serialized inventory per selected chest, 64 MiB reserved handoff/recovery bytes, at most 256 API-held sources/adapters.
- 32 KiB messages; handoff content is chunked. Identifiers are bounded by UTF-8 bytes as well as characters.
- At 16 unresolved recovery operations, new payments are held back. Existing recovery/receipt replay continues.

The normal path is lightweight; decoding one inventory, building a graph and invoking other mods' inventory hooks are indivisible main-thread work. The cooperative catalogue budget is not a promise that every external hook finishes within that time. Packet-filter compatibility is checked against the installed game assembly by the local builder.

## Local test consumer

See [the separate consumer README](https://github.com/rerit33/RunicStorageNetwork/blob/main/examples/ApiTestMod/README.md) and [its source](https://github.com/rerit33/RunicStorageNetwork/blob/main/examples/ApiTestMod/ApiTestMod.cs). It uses only the public API. Build with `tools/BuildApiPreview.ps1` using the repository's [local build setup](https://github.com/rerit33/RunicStorageNetwork/blob/main/BUILDING.md). It consumes materials into a saved counter, demonstrates same-ID replay and automatic retry after `NoDebit`, and deliberately restricts itself to single-player. The test plugin is not included in the Thunderstore or Hexium package.

For an actual multiplayer integration, test simultaneous payments and player crafting, another player opening the same chest, source-owner disconnects, machine ownership changes, and recovery of the original request without duplicate production. Also test startup with the experimental option disabled and access to a core that has not loaded near any player.

## Contract reference

- [Public methods and consumer marker](https://github.com/rerit33/RunicStorageNetwork/blob/v0.8.8/src/ApiFacade.cs)
- [DTO constructors, properties and enums](https://github.com/rerit33/RunicStorageNetwork/blob/v0.8.8/src/ApiTypes.cs)
- [Request validation and identity rules](https://github.com/rerit33/RunicStorageNetwork/blob/v0.8.8/src/ApiRules.cs)

This page documents contract version 1 in RSN 0.8.8. Check the release notes and `NetworkResources.ContractVersion` when targeting later versions.
