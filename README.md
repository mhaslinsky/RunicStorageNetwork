# Runic Storage Network

> **0.8.2 experimental local build / экспериментальная локальная сборка**
>
> Optional unloaded-network support is available in single player. It is **disabled by default** and may cause errors, including inventory problems. Back up the world before testing. In `BepInEx/config/local.runicstoragenetwork.cfg`, set `[Experimental]` → `ExperimentalUnloadedNetworks = true`, then reload the world. Set it back to `false` and reload to return to normal operation. Multiplayer continues to use normal loaded-area networking.
>
> Экспериментальная работа с выгруженной сетью доступна в одиночной игре и **выключена по умолчанию**. Возможны ошибки, в том числе с инвентарём. Перед тестом сделайте резервную копию мира. В `BepInEx/config/local.runicstoragenetwork.cfg`, в разделе `[Experimental]`, установите `ExperimentalUnloadedNetworks = true` и перезайдите в мир. Для отключения верните `false` и перезайдите. В мультиплеере сохраняется обычный режим работы с загруженными объектами.
>
> Both vanilla and eligible modded storage use the existing container filters. If an inventory cannot be read and saved without changing its data, offline access is refused. Containers with custom storage behaviour still need individual testing. First access may take several frames while the index is prepared. This build has not been verified in a running game.
>
> Ванильные и модовые сундуки используют существующие фильтры. Если содержимое нельзя прочитать и сохранить без изменений, удалённый доступ отклоняется. Хранилища с собственной логикой требуют отдельной проверки. При первом обращении подготовка индекса может занять несколько кадров. Сборка ещё не проверена в запущенной игре.

[English](#english) | [Русский](#русский)

[Report a bug / Сообщить об ошибке](https://github.com/rerit33/RunicStorageNetwork/issues/new/choose) · [Changelog (EN)](https://github.com/rerit33/RunicStorageNetwork/blob/main/CHANGELOG_EN.md) · [История изменений (RU)](https://github.com/rerit33/RunicStorageNetwork/blob/main/CHANGELOG.md)

## English

**Keep your resources in storage — craft and build where you need them.**

Runic Storage Network connects your base's chests into a supply network. Craft and upgrade items at connected crafting stations, build with your construction tools, and expand your base without hauling materials from one building to another.

Craft and build through the familiar menus while materials are consumed directly from connected chests. A Storage Codex also lets you search the network and retrieve items into your inventory.

### Build pieces

#### Storage Network Core

The center of your network. Connects nearby chests and makes their resources available for crafting, upgrading items, and building within its coverage area. One core is enough for a small workshop and storage area.

#### Runic Relay

Extends the network between buildings. Connects to the core directly or through other relays, links nearby chests, and lets you craft and build nearby using resources from the entire network.

Relays can form chains and branches. They need an uninterrupted connection to the core to function. If the connection breaks, the disconnected section loses access to network resources, but items remain in their chests and can still be retrieved manually.

#### Storage Codex

A book on a stone pedestal that opens network storage. Place it within the supply range of a core or connected relay. It gives access to that network without extending its coverage. Hover over it to see whether it is connected.

All three structures are built with the regular hammer near a workbench and require no fuel.

#### Runic Codex

The closed book used to build a Storage Codex. Craft it at a level 1 forge after discovering its ingredients. It can be carried, stored, dropped and picked up like a regular item.

### Getting started

1. Build a core near your storage chests.
2. Place crafting stations within its coverage area, or extend the network to other buildings with relays.
3. Use crafting stations and your construction tools as usual — the required materials will be drawn from the available supply.

Materials in your inventory are used first, followed by any missing materials from connected chests. Recipe requirements and crafting station level requirements still apply.

Interact with a Storage Codex to open network storage. Search for an item, choose a quantity and press **Take** to retrieve it into your inventory. Items of different qualities are listed separately; retrieved items keep their original properties. Depositing items through this window is not available.

Interact with a core (E with default controls) to give its network an optional name. Leave the field empty to remove the name. Connected cores share one network name. Hover over a connected container to see its network; unnamed networks simply show "Connected to network". Unconnected containers receive no additional line.

### Ranges and recipes

By default, chests connect within **20 m** of a node. Crafting stations and builders are supplied within **20 m**, and neighboring network nodes can connect over distances of up to **50 m**. These ranges can be changed in the mod configuration.

| Material | Core | Relay |
|---|---:|---:|
| Stone | 30 | 10 |
| Fine wood | 20 | 6 |
| Chains | 2 | — |
| Iron ingots | — | 2 |
| Surtling cores | 4 | 1 |
| Greydwarf eyes | 10 | 5 |

**Runic Codex — forge level 1:** Silver ×4, Crystal ×2, Greydwarf Eye ×6, Linen Thread ×4, Leather Scraps ×4. Produces one book. The recipe uses normal ingredient discovery.

**Storage Codex — hammer, near a workbench:** Runic Codex ×1, Fine Wood ×10, Stone ×8, Iron ×2, Red Jute ×2. Dismantling returns the materials, including the book.

### Installation and compatibility

Requires **BepInExPack Valheim** and **Jötunn**. For multiplayer, install the same version of the mod and its required dependencies on the server and every player's client.

By default, the network works with stationary containers built by players in loaded areas of the world, including containers added by other mods. This experimental build can also access eligible unloaded storage in single player when the option described above is enabled. Backpacks, tombstones, ship and cart storage, and personal chests are not connected.

Machines that consume or fire their contents stay out of the network by default. The network does not draw crafting materials from the obliterator. Smelters, kilns, cooking stations, fermenters, beehives, sap collectors, ballistae and catapults are excluded on the same rule, including modded equivalents built on the same components.

Automatic eligibility does not guarantee compatibility with every modded container. Containers with custom inventory, saving or access behavior need separate compatibility testing.

The `Containers` section of the configuration decides which containers take part:

| Setting | Default | Effect |
|---|---|---|
| `AllowedContainers` | empty | Empty: every eligible container is connected. Filled: only the listed prefab names are connected. |
| `DeniedContainers` | `piece_trashcan` | Prefab names that are never connected. |
| `DeniedComponents` | machine components | A container is never connected when its prefab has one of these components. |

Exclusion always wins over inclusion, so a container listed in both is excluded. Changes take effect without restarting the game. In multiplayer these settings are administrator-only and come from the server, so the server decides which containers the whole session uses.

`rsn_status` in the console reports how many container types are supported and which are excluded, and the mod log lists them by name.

Building draws on the network with any build tool, including hammers added by other mods. A tool takes part when the game gives it its own build menu, so nothing needs to be registered with this mod. Ordinary build pieces and planting can use stored resources. Serving trays can also draw food from connected storage. Terrain shaping uses inventory resources by default.

The `Building` section of the configuration decides which tools take part:

| Setting | Default | Effect |
|---|---|---|
| `AllowedBuildTools` | empty | Empty: every build tool qualifies. Filled: only the listed item prefabs build from the network. |
| `DeniedBuildTools` | empty | Item prefabs that never build from the network. |
| `DeniedPieceComponents` | `TerrainOp,TerrainModifier` | A piece is never supplied when its prefab has one of these components. |

Exclusion wins over inclusion here too, and these settings are administrator-only, so the server decides for the session. The equipped tool must be allowed, even if it shares a build menu with another tool. Excluded actions still work normally with materials in your inventory. When you already carry enough materials, building does not wait for the network. Normal placement and crafting station requirements still apply.

Do not enable multiple crafting-from-chests systems at the same time without checking compatibility. Back up your world and character before installing or updating the mod.

### Created with AI assistance

AI tools were used to develop the code, create concept art, and produce the 3D models. Gameplay decisions, selection of results, and in-game testing are handled by the author.

### Incompatible mods and integration limits

Runic Storage Network disables its resource supply when it detects NearbyCrafting, AzuCraftyBoxes, DvergerAutomation, CraftFromContainers or CraftFromChests. Use one storage-supply system at a time.

MultiUserChest and Quick Stack Store Sort Trash Restock are optional. The current integrations accept **MultiUserChest 0.6.2** and **Quick Stack 1.4.15**; other versions disable network supply until their integration is updated. With Quick Stack but without MultiUserChest, `AllowAreaStackingInMultiplayerWithoutMUC` must be disabled. These version checks do not guarantee compatibility with every mod combination.

---

## Русский

**Ресурсы остаются на складе — стройте и создавайте предметы там, где удобно.**

Runic Storage Network объединяет сундуки базы в сеть снабжения. Изготавливайте и улучшайте предметы на подключённых станках, стройте строительными инструментами и расширяйте базу, не перенося материалы из одного здания в другое.

Пользуйтесь привычными меню крафта и строительства — необходимые ресурсы расходуются прямо из подключённых сундуков. Кодекс запасов также позволяет искать предметы в сети и забирать их в инвентарь.

### Постройки

#### Ядро сети хранилищ — Storage Network Core

Центр вашей сети. Подключает соседние сундуки и предоставляет их ресурсы для крафта, улучшения предметов и строительства в своей зоне действия. Одного ядра достаточно для небольшой мастерской со складом.

#### Рунное реле — Runic Relay

Расширяет сеть между зданиями. Соединяется с ядром напрямую или через другие реле, подключает сундуки вокруг себя и снабжает ближайшие станки и строителя ресурсами всей сети.

Реле можно выстраивать в цепочки и ответвления. Для работы нужен непрерывный путь до ядра. При разрыве связи отключённый участок перестаёт снабжаться, но предметы остаются в сундуках и доступны вручную.

#### Кодекс запасов

Книга на каменном постаменте, открывающая хранилище сети. Разместите её в зоне снабжения ядра или подключённого реле. Она даёт доступ к этой сети, не расширяя покрытие. При наведении показывается состояние подключения.

Все три постройки устанавливаются обычным молотом рядом с верстаком и не требуют топлива.

#### Рунный кодекс

Закрытая книга для постройки Кодекса запасов. Создаётся на кузнице первого уровня после знакомства с ингредиентами. Её можно носить, хранить, выбрасывать и подбирать как обычный предмет.

### Как начать

1. Постройте ядро рядом со складскими сундуками.
2. Разместите станки в его зоне действия или протяните сеть реле к другим зданиям.
3. Пользуйтесь станками и строительными инструментами как обычно — необходимые материалы будут взяты из доступного запаса.

Сначала расходуются материалы при себе, затем — недостающее из сундуков. Требования рецептов и уровни станков сохраняются.

Взаимодействуйте с Кодексом запасов, чтобы открыть хранилище сети. Найдите предмет, выберите количество и нажмите **«Забрать»**, чтобы получить его в инвентарь. Предметы разного качества показаны отдельно и сохраняют свои свойства при получении. Складывание предметов через это окно пока недоступно.

Взаимодействие с ядром (E при стандартном управлении) позволяет задать необязательное название сети. Пустое поле удаляет название. Соединённые ядра используют общее название сети. При наведении на подключённое хранилище показывается его сеть; для безымянной сети — просто «Подключено к сети». У неподключённых хранилищ дополнительной строки нет.

### Радиусы и рецепты

По умолчанию сундуки подключаются в радиусе **20 м** от узла. Радиус снабжения станков и строителя — также **20 м**, а расстояние между соседними узлами связи — до **50 м**. Радиусы можно изменить в конфигурации мода.

| Материал | Ядро | Реле |
|---|---:|---:|
| Камень | 30 | 10 |
| Качественная древесина | 20 | 6 |
| Цепи | 2 | — |
| Железные слитки | — | 2 |
| Ядра суртлинга | 4 | 1 |
| Глаза грейдворфа | 10 | 5 |

**Рунный кодекс — кузница первого уровня:** серебро ×4, кристалл ×2, глаз грейдворфа ×6, льняная нить ×4, обрывки кожи ×4. Получается одна книга. Рецепт открывается по обычным правилам знакомства с ингредиентами.

**Кодекс запасов — молоток, рядом с верстаком:** Рунный кодекс ×1, качественная древесина ×10, камень ×8, железо ×2, красный джут ×2. При разборке материалы возвращаются, включая книгу.

### Установка и совместимость

Требуются **BepInExPack Valheim** и **Jötunn**. Для совместной игры установите одинаковую версию мода и необходимые зависимости на сервере и у всех игроков.

По умолчанию сеть работает со стационарными хранилищами, построенными игроками, в загруженной области мира, включая хранилища из других модов. В этой экспериментальной сборке можно также использовать подходящие выгруженные хранилища в одиночной игре, включив описанную выше настройку. Рюкзаки, надгробия, корабельные трюмы, повозки и личные сундуки не подключаются.

Устройства, которые расходуют или расстреливают своё содержимое, по умолчанию в сеть не входят. Сеть не забирает материалы для крафта из уничтожителя. По тому же правилу исключаются плавильни, углевыжигательные печи, очаги, бродильни, ульи, сокосборники, баллисты и катапульты, в том числе их аналоги из других модов, собранные на тех же компонентах.

Автоматическое подключение не гарантирует совместимость со всеми модовыми хранилищами. Хранилища с нестандартной работой инвентаря, сохранений или прав доступа требуют отдельной проверки совместимости.

Состав сети задаётся в разделе `Containers` конфигурации:

| Параметр | По умолчанию | Действие |
|---|---|---|
| `AllowedContainers` | пусто | Пусто: подключаются все подходящие хранилища. Заполнено: подключаются только перечисленные префабы. |
| `DeniedContainers` | `piece_trashcan` | Префабы, которые не подключаются никогда. |
| `DeniedComponents` | компоненты устройств | Хранилище не подключается, если в его префабе есть один из этих компонентов. |

Исключение всегда важнее включения: хранилище, указанное в обоих списках, остаётся отключённым. Изменения применяются без перезапуска игры. В совместной игре эти параметры доступны только администратору и приходят с сервера, поэтому состав хранилищ для всей сессии определяет сервер.

Команда `rsn_status` в консоли показывает, сколько типов хранилищ поддерживается и сколько исключено, а журнал мода перечисляет их по именам.

Строительство берёт ресурсы из сети любым строительным инструментом, включая молоты из других модов. Инструмент участвует, если игра даёт ему собственное меню построек, поэтому регистрировать его в этом моде не нужно. Обычные постройки и посадки могут использовать ресурсы хранилищ. Поднос также может брать еду из подключённых хранилищ. Изменение ландшафта по умолчанию использует ресурсы инвентаря.

Состав инструментов задаётся в разделе `Building` конфигурации:

| Параметр | По умолчанию | Действие |
|---|---|---|
| `AllowedBuildTools` | пусто | Пусто: подходит любой строительный инструмент. Заполнено: из сети строят только перечисленные префабы предметов. |
| `DeniedBuildTools` | пусто | Префабы предметов, которые никогда не строят из сети. |
| `DeniedPieceComponents` | `TerrainOp,TerrainModifier` | Постройка не снабжается, если в её префабе есть один из этих компонентов. |

Исключение здесь также важнее включения, а сами параметры доступны только администратору, поэтому состав определяет сервер. Разрешён должен быть именно инструмент в руках, даже если его меню совпадает с меню другого инструмента. Исключённые действия продолжают работать с ресурсами инвентаря. Если нужных материалов в инвентаре достаточно, строительство не ждёт сеть. Обычные требования к размещению и верстаку сохраняются.

Не включайте одновременно несколько систем крафта из сундуков без проверки совместимости. Перед установкой и обновлением делайте резервную копию мира и персонажа.

### Создано с помощью AI

Мод создан с использованием AI-инструментов при разработке кода, концептов и 3D-моделей. Игровые решения, отбор результатов и проверку в игре выполняет автор.

### Несовместимые моды и ограничения интеграций

Runic Storage Network отключает снабжение ресурсами при обнаружении NearbyCrafting, AzuCraftyBoxes, DvergerAutomation, CraftFromContainers или CraftFromChests. Используйте одну систему снабжения из хранилищ.

MultiUserChest и Quick Stack Store Sort Trash Restock необязательны. Текущие интеграции допускают **MultiUserChest 0.6.2** и **Quick Stack 1.4.15**; с другими версиями снабжение отключается до обновления интеграции. При использовании Quick Stack без MultiUserChest параметр `AllowAreaStackingInMultiplayerWithoutMUC` должен быть выключен. Эти проверки версий не гарантируют совместимость с любой комбинацией модов.
