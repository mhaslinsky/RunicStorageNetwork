# Buildings, ranges and recipes

Reference for **Runic Storage Network 1.0**. [Русская версия](https://github.com/rerit33/RunicStorageNetwork/wiki/Buildings-and-recipes-RU) · [All settings](https://github.com/rerit33/RunicStorageNetwork/wiki/Configuration).

## What each object does

| Object | Purpose |
| --- | --- |
| Storage Network Core | Starts a network, connects nearby storage and supplies crafting, upgrades and building. Interact to set an optional network name. |
| Runic Relay | Extends an existing network. Connects nearby storage and supplies its surroundings while a path to a core exists. |
| Storage Codex | Opens a searchable resource list and withdraws items into your inventory. Place it inside network supply coverage. It does not extend coverage or accept deposits. |
| Runic Gateway | A pair with the same link name connects distant parts of one network. Each end has normal relay coverage. Requires distant storage; does not teleport players. |
| Runic Codex | A portable crafting ingredient for the Storage Codex and Builder's Codex. Has no network range of its own. |
| Runic Builder's Codex | Bind the book by using it on a core, then equip it in the utility slot shared with Megingjord. Supplies building from that network only; it does not supply other players or extend crafting-station coverage. |

## Default ranges

Distances are in metres, measured between object positions in three dimensions. These are radii or maximum link distances, not diameters.

| Connection or function | Default | Setting in `[Network]` |
| --- | ---: | --- |
| Core → storage | 20 m | `StorageRadius` |
| Core → crafting/building/Storage Codex access | 20 m | `SupplyRadius` |
| Relay or gateway → storage | 20 m | `RelayStorageRadius` |
| Relay or gateway → crafting/building/Storage Codex access | 20 m | `RelaySupplyRadius` |
| Ordinary links between network nodes | 50 m | `RelayLinkRange` |
| Equipped Builder's Codex → core or connected relay | 50 m | `RelayLinkRange` |
| Distant link between two paired gateways | No distance limit | No separate range setting |

All five configurable ranges accept **1–100 m**. The Builder's Codex shares the link-range setting; it has no separate range setting and does not connect directly to gateways. The Storage Codex has no independent network radius. Stay within **5 m** to continue using its storage window; this interaction limit is not configurable.

Relays can form chains: the 50 m limit applies to each link, not to the whole network. A continuous path to a core is required. Access to unloaded parts uses distant storage.

Exactly two gateways may share a link name. A third disables the pair until the conflict is resolved. Gateways keep their network binding and cannot merge independent networks. Only resources allowed through ordinary portals under the world's portal rules can cross the distant link; local resources and ordinary relay routes remain usable. Renaming a network does not change a Builder's Codex binding; use the book on another core to rebind it.

## Recipes

All recipes unlock through normal ingredient discovery. The four build pieces use **Hammer → Crafting**, require a nearby **workbench**, consume no fuel and return their materials when dismantled.

| Object | Where | Materials for one |
| --- | --- | --- |
| Storage Network Core | Hammer | Stone ×30, Fine Wood ×20, Chain ×2, Surtling Core ×4, Greydwarf Eye ×10 |
| Runic Relay | Hammer | Stone ×10, Fine Wood ×6, Iron ×2, Surtling Core ×1, Greydwarf Eye ×5 |
| Storage Codex | Hammer | Runic Codex ×1, Fine Wood ×10, Stone ×8, Iron ×2, Red Jute ×2 |
| Runic Gateway | Hammer | Stone ×20, Yggdrasil Wood ×10, Silver ×6, Crystal ×10, Refined Eitr ×5 |
| Runic Codex | Forge, level 1 | Silver ×4, Crystal ×2, Greydwarf Eye ×6, Linen Thread ×4, Leather Scraps ×4 |
| Runic Builder's Codex | Black forge, level 1 | Runic Codex ×1, Black Core ×1, Refined Eitr ×5, Silver ×2, Crystal ×2 |

## Settings at a glance

| Setting | Default in 1.0 | Effect |
| --- | --- | --- |
| `[Network] SupplyEnabled` | `true` | Enables resource supply from the network. |
| `[Network] RescanIntervalSeconds` | `2` | Ordinary topology rescan interval, allowed 0.5–30 seconds. This is not a resource-refresh timer for every operation. |
| `[Content] StorageCodexEnabled` | `true` | Enables the terminal recipe and storage access. |
| `[Content] BuildersCodexEnabled` | `true` | Enables the wearable book recipe, binding and extended building range. |
| `[Content] RunicGatewayEnabled` | `true` | Enables the gateway recipe and network links. |
| `[Experimental] ExperimentalUnloadedNetworks` | `true` | Enables unloaded storage and the API; also required for gateways. |

Existing saved settings are kept on update, including a previous `false`. Content switches keep existing objects when disabled. Only gateways require distant storage; the Storage Codex and Builder's Codex also work with loaded-area networks. Network and content settings are synchronized from the server; the distant-storage option must match on all installations and requires a full restart after changing. See [Configuration](https://github.com/rerit33/RunicStorageNetwork/wiki/Configuration) for container filters, tool settings and diagnostics.
