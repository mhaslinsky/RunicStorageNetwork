# Network resources API — local preview 1

Assembly: `RunicStorageNetwork.dll` 0.8.8. Namespace: `RunicStorageNetwork.API`. Contract version: `NetworkResources.ContractVersion == 1`.

This is a development build, not a published release. Automated checks use game stand-ins; actual single-player and multiplayer verification is still required.

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

Register a stationary player-built prefab with a `Piece` and persistent `ZNetView`, then add the marker on **every peer, including the server**:

```csharp
prefab.AddComponent<NetworkConsumer>().ModId = "your.mod.guid";
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
```

- `Ready` and `IsComplete` describe a complete current observation, not a reservation or future guarantee.
- `Partial` exposes known `ObservedAmount` values and the number of unknown sources. `ResourceAmountResult.Amount` is null until the observation is complete. Unknown is never represented as an exact zero.
- A known item absent from a complete network has amount zero. An unregistered item returns `UnknownItem`.
- `Updating` / `NotReady` mean discovery or refresh is pending. Stale values are observations only.
- `NoNetwork`, `NotOwner`, `AccessDenied`, `FeatureDisabled`, `SnapshotLimit` describe why a usable current view is unavailable.
- `SessionId`, `Revision`, `Age`, and `Recovery` let an integration correlate its saved intent and the current server observation. `OwnerEpoch` is the current ZDO ownership revision within this server session.

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

Explicit `AnyMatchingInstance` consent is required: the contract aggregates prefab + quality. It does not preserve or select custom item metadata, durability or cosmetic variants for the consuming machine. Do not use this policy when an exact unique item must be transferred intact.

`AdmissionCode` and payment outcome are separate:

| Result | Meaning |
| --- | --- |
| `Success` | Every requested quantity was saved out of the source inventories. The receipt authorizes that exact paid input once. |
| `NoDebit` | Nothing remains debited by this operation; any partial debit has been restored. A new sequence is safe after the final source cleanup. |
| `OutcomeUnknown` | A server-session boundary prevents proof of the old outcome. Do not create output or issue replacement payment automatically. |
| No outcome (`null`) | Admission/observation result such as `Busy`, `Throttled`, `NotOwner` or `RequestConflict`. It is not proof that an earlier payment failed. |

Retrying the same normalized payload under the same identity observes the original operation/receipt. Changing its resources, cycle or policy conflicts. Older sequences return `ExpiredRequest` once a later sequence has replaced their receipt. An accepted operation cannot be replaced until source cleanup completes. A temporary admission rejection can be retried with the same request.

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

## Limits in preview 1

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

See [the separate consumer README](examples/ApiTestMod/README.md) and its source. It uses only the public API. Build with `tools/BuildApiPreview.ps1`. It consumes materials into a saved counter, demonstrates same-ID replay and automatic retry after `NoDebit`, and deliberately restricts itself to single-player. The API's multiplayer transport still needs an actual client/server test before release.
