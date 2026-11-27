# GrabzVR — правила проекта

Кооперативная VR-игра про ограбление в стиле Gorilla Tag: передвижение руками, лут, лазеры, эвакуация.
**Unity 6000.6.10f1 + URP 17.6**, OpenXR (Quest через Link и Android), XR Interaction Toolkit 3.7.
Сеть: **Photon Fusion 2.1 (Shared Mode)** + **Photon Voice 2** + **PlayFab** (Title ID `1FEC73`).
Разработка идёт вдвоём с двух ПК через git, ветка `main`. Комментарии в коде и тексты в игре на русском.

## Сцены и Build Settings
| Индекс | Сцена | Назначение |
|---|---|---|
| 0 | `Assets/Scenes/MainHub.unity` | Главный хаб: зеркало, выбор цвета шерсти (`SkinButton`), стол режимов, фургон-портал (`VanPortal`), ретро-компьютер GRABZ OS (`GrabzComputer`), баскетбол |
| 1 | `Assets/Scenes/HeistLevel_Test.unity` | Банк SUNSET TRUST (Майами): закатная улица с фургоном грабителей, операционный зал с кассами, служебное крыло (комната отдыха, серверная, кабинет директора), подвал с главным хранилищем, галерея, офисы, гараж с броневиком (зона сдачи лута) |
| — | `Assets/Scenes/SampleScene.unity` | **Не трогать.** В Build Settings выключена |

`GameSession.HubScene` / `GameSession.HeistScene` задают имена сцен. Сцены в Build Settings ищутся по имени (`FusionNetworkManager.LoadSceneSynced`).

## ⚠️ Сцены собраны кодом: не перезапускать билдеры
Сцены и префабы когда-то построены редакторскими скриптами из `Assets/Editor/`. **Повторный запуск полностью пересоздаёт сцену или префаб и затирает все ручные правки и сетевую настройку.** Без прямой необходимости и без согласия команды НЕ запускать:
- `Tools/Heist/Build Main Hub` (`MainHubBuilder`) — пересоздаёт MainHub;
- `Tools/Heist/Build Heist Map (Full)` (`HeistMapBuilder`, `HeistLootFactory`) — пересоздаёт HeistLevel_Test и префабы лута (сетевые компоненты пропадут);
- `Tools/Heist/Network/Setup All` (`NetworkSetupBuilder`) — пересоздаёт `NetworkRunner.prefab` и `NetworkGorillaAvatar.prefab`;
- `Tools/Heist/Player/...`, `Tools/Heist/Setup VR Player`, `Tools/Heist/Fix VR Text + Desktop Mirror Avatar` — пересобирают игрока и аватар.

Новые изменения вносить **точечно**: правкой сцены в редакторе или отдельным идемпотентным editor-скриптом, который только добавляет нужное. Пример такого скрипта: `Tools/Heist/Network/Setup Co-op Sync` (`CoopSyncSetup`), его можно запускать повторно.

## Игроки
**`VRGorillaPlayer`** (`Assets/Prefabs/Player/VRGorillaPlayer.prefab`, стоит в обеих сценах):
- `Player` (`Assets/Scripts/Locomotion`, GorillaLocomotion, MIT): передвижение руками, коллайдеры рук и тела;
- `Left Controller` / `Right Controller` (трекинг рук); на руках `VRGrabber` (хват лута курком или грипом) и `GorillaHandFeedback` (вибро, звук, разбивание стекла рукой). На игроке также `GorillaStickTurn` (поворот стиком) и `GorillaHeavySlowdown` (−40% скорости с тяжёлым лутом);
- `GorillaAvatar` — модель **Gorilla_Final** (Tripo, `Assets/Art/GeneratedModels/Gorilla_Final`) с `GorillaFinalRig`; позы пальцев (кулак, указка) задаёт `VRHandPoseController`; ещё `PlayerSkinController` (цвет шерсти) и `PlayerCosmetics`;
- косметика по слотам (`GameSession.SlotOf`): **Headwear** (корона, шапки), **FaceMask** (маска грабителя), **Neck** (цепь). `GameSession.Cosmetic` — id через запятую, по одному на слот, предметы разных слотов носятся вместе; по сети — битовая маска `NetworkGorillaAvatar.Cosmetic`. Новый предмет: объект с именем id в `PlayerCosmetics.items` + строка в `SlotOf` и `NetworkGorillaAvatar.CosmeticIds`;
- лицевая маска `RobberMask` — меш по форме лица Gorilla_Final (`Tools/Heist/Player/Face Mask`, `FaceMaskBuilder`, идемпотентный: меняет только содержимое `RobberMask` у VR-, ПК- и сетевой гориллы и образец на витрине);
- локальная голова вынесена на слой `LocalHead` и убрана из culling mask своей камеры: в шлеме её не видно, в зеркале видно.

**`DesktopTestPlayer`** — тестовый игрок без шлема: `CharacterController`, WASD + мышь, Shift для бега, Space для прыжка. На камере `VRGrabber` в режиме Mouse (ЛКМ — взять, нажать кнопку или разбить стекло; ПКМ — бросить). Аватар двигает `DesktopAvatarDriver`.
**`PlayerModeSwitcher`** выбирает игрока: если шлем активен, включается VR, иначе Desktop. F1 переключает на VR-руки с клавиатуры (XR Device Simulator).

## Слои и теги
- `Player` (8) — тело и руки игрока;
- `LocalHead` (9) — своя голова и косметика на ней: своей камере не видны, зеркалу и другим игрокам видны;
- `Ignore Raycast` (2) — триггеры, кнопки, осколки и лут в руке (чтобы руки GorillaLocomotion не цеплялись);
- тег `Loot` — на корне каждого предмета лута, тег `Player` — на игроке (по нему срабатывают лазеры).

## Текст в игре — только 3D World Space
Любой игровой текст (табло, подсказки, ники, GRABZ OS, таблички) делать **только 3D-объектами в мире**: `TextMeshPro` (не `TextMeshProUGUI`) или World Space-объекты. **Никаких экранных Canvas (Screen Space Overlay/Camera)** и никакого UI, приклеенного к голове игрока: в шлеме экранный UI двоится и уезжает за край. Исключение — отладочный `OnGUI` у `DesktopTestPlayer`, он отключается при активном шлеме.

## Визуал MainHub (стиль Gorilla Tag / PS1)
- **Апгрейд поверх сцены:** `Tools/Heist/Hub/Visual Overhaul` (`HubVisualOverhaul`). Идемпотентный: пересоздаёт только корень сцены `HubDecor` и перетекстурирует архитектуру. Сетевые объекты, GRABZ OS, SkinButton, VanPortal, зеркало и баскетбол не трогает.
- **Текстуры генерируются кодом** (`HubRetroTextures`) в `Assets/Art/Textures/Hub`: кирпич, бетон, доски, ржавый рифлёный металл, ковры. 256 px (ковры 128), Bilinear, BaseMap + лёгкая NormalMap.
- **Материалы Хаба** — отдельные `Assets/Art/Materials/Hub/M_Hub_*`: Smoothness 0, без бликов и отражений. Общие `Assets/Art/Materials/Map/M_*.mat` использует и карта ограбления, поэтому **ради Хаба их не менять**.
- **Тайлинг:** у примитивов архитектуры UV пересчитаны в метры (меши `Assets/Art/Meshes/HubUV/HubBox_*` / `HubCyl_*`), текстура тайлится, а не растягивается. Новые кубы и цилиндры создавай через тот же скрипт.
- **Tripo-пропсы:** `.glb` из Tripo Studio кладём в `Assets/Art/GeneratedModels/Hub_*.glb`. `TripoAssetPostprocessor` сам делает префаб, текстуру 128 px и матовый материал. Промпт-суффикс: `stylized low poly PS1 retro game asset, chunky shapes, hand-painted matte texture`, режим «Умная сетка», треугольники, 3500 полигонов.
- **Свет под Quest:** в Mobile URP не больше 4 дополнительных источников на объект, тени у новых ламп выключены. Новые источники добавлять экономно, видимые абажуры — просто меши.
- **Чёрный автобус вместо джипа:** `Tools/Heist/Hub/Black Bus` (`BlackBusBuilder`), идемпотентный: пересоздаёт корень сцены `BlackBus` и точечно правит `VanPortal`, `Van_Countdown`, коллайдеры `Wall_N`, разметку `VanDecor`. Салон в 1.7 раза длиннее кузова (Stencil-портал):
  - слои `BusPortal` (маски, Stencil = 1), `BusInterior` (салон, рисуется только при Stencil == 1), `BusExterior` (кузов, колёса и **`Wall_N`**, рисуются только при Stencil != 1). Эти три слоя исключены из Opaque/Transparent Layer Mask обоих рендереров, их рисуют Render Objects `Bus 1/2/3` в `Mobile_Renderer` и `PC_Renderer`;
  - салон физически уходит на ~4.5 м за северную стену хаба. **Не ставить объекты в коридор x 2.8…5.6, z 7.3…18.2, y 0…2.6** — они будут видны внутри салона. Коллайдеры стены — в `Wall_N_Colliders` с проёмом под коридор;
  - маска «изнутри» включается для каждой камеры отдельно (`BusPortal`, `beginCameraRendering`).
- **Post-Processing:** глобальный Volume `HubPostFX` с профилем `Assets/Settings/HubPostFXProfile.asset` (Bloom в четверть разрешения + лёгкая цветокоррекция). Включён только на камерах игроков в MainHub.

## Карта Банка (HeistLevel_Test) — SUNSET TRUST BANK, Майами
- **Апгрейд поверх сцены:** `Tools/Heist/Bank/Visual Overhaul` (`BankVisualOverhaul`, partial: `BankExterior`, `BankInteriorProps`, `BankServiceWing`, `BankFurniture`, `BankDoors`). Идемпотентный: пересоздаёт корень сцены `BankDecor`, перетекстурирует архитектуру `HeistMap/Z*/Static`, прячет (не удаляет) заменённые объекты; старое содержимое хранилища переносится в подвал один раз (метка `Z5_Vault/MovedToBasement`). `HeistMapBuilder` не вызывает. В конце сам проверяет надписи (не сжаты ли) и кнопки (есть действие, луч мыши доходит) — см. `texts:` / `buttons:` в логе. Превью: `Tools/Heist/Bank/Capture Preview` → `HubPreview/Bank_*.png`.
- **Детальный проход:** `Tools/Heist/Bank/Detail Pass (props, light, parkour)` (`BankDetailPass`). Идемпотентный: пересоздаёт только корень сцены `BankDetail` и прячет старую люстру холла; `BankVisualOverhaul` не вызывает. Машины Майами, скамейки, неон, стойки с канатами, табло курсов, картины, люстры-«лианы» (паркур над холлом, z = 12), кабель-лоток над лестницей, трубы-турник и клетка с золотом в подвале, ячейки и стеллажи с деньгами за лазерами. Мелкие примитивы склеены по материалам в меши `GeneratedModels/Bank/Detail/Meshes` (статический батчинг в проекте выключен), колайдеры — отдельные BoxCollider на Default, новых источников света 4 (без теней), остальное — аддитивные пятна `M_BankDetail_Pool*`. Tripo: `Bank_MiamiCar` (3 цвета перекраской текстуры), `Bank_Chandelier`, `Bank_GoldPallet`. В зоне лазеров (z 32.3…36.8) колайдеров нет — скрипт проверяет. Превью: `Tools/Heist/Bank/Detail Pass Preview` → `HubPreview/Detail_*.png`.
- **Стены билдера называются кусками** `Wall_N_0`, `Wall_N_1`… — прятать по префиксу, а не по точному имени (иначе старая стена остаётся и закрывает новые проёмы).
- **Папки Банка:** скрипты `Assets/Scripts/Bank/` (editor — `Assets/Scripts/Bank/Editor/`), материалы, текстуры, небо и Post-FX `Assets/Art/Materials/Bank/` (`M_Bank_*`), Tripo-модели, UV-меши и префабы лута `Assets/Art/GeneratedModels/Bank/`. Общие `M_*.mat` не менять.
- **Улица Майами:** закатное небо — панорама `T_Bank_SunsetSky` (Skybox/Panoramic, не Procedural: тот красит закат в жёлто-зелёный), тёплое низкое солнце с юго-запада, розовый туман. Пастельные отели Ар-деко с неоном (OCEAN HOTEL, FLAMINGO…), пальмы в кадках (`Bank_PalmPlanter`), фасад банка с колоннами и неоновой вывеской **SUNSET TRUST BANK** + логотип (солнце с пальмой из примитивов, `Logo()`). Фонари без источников света.
- **Старт:** игроки появляются в салоне чёрного автобуса `GetawayVan_Arrival` у ступенек — модель `GRABZ_BlackBus.glb` (`BankBus.cs`: у модели только оболочка, поэтому в копии меша `GRABZ_BlackBus_BodyOpen.asset` вырезан задний проём, салон и коллайдеры построены отдельно). `NetworkHeistStartTrigger` (Master Client Object): кто-то вышел → RPC хосту; хост ставит `[Networked] IsHeistStarted`, когда вышли все или через 10 с, и запускает таймер (`HeistGameManager.StartHeist()`); у всех рольставня опускается, автобус уезжает. Тестового Убежища (`Z1_Hub_Bunker`) нет.
- **Операционный зал:** мрамор с золотыми полосами, белые колонны с золотом, бирюзовый цоколь, панорамные «закатные» окна (Unlit) с аддитивными лучами и 2 тёплыми спотами. Кассовые стойки `TellerDesk` (из примитивов: стекло, лотки, кассовый аппарат и монитор со стороны кассира, логотип на фасаде), рабочая зона за ними и **`CashierSpawnPoint_1..4`** (`BankDecor/CashierSpawnPoints`) — пустые точки под будущих NPC-кассирш (лицом к клиентам).
- **Служебное крыло** (x −14…10, z 24…40, потолок 3.4): северная стена Холла пересобрана (`HallWall_N`) с двумя сетевыми служебными дверями `ServiceDoor_A/B_Net` (`NetworkVaultDoor`; А открывается кнопкой, Б — взлом 4 с). Коридор с дверями комнат `RoomDoor_*_Net` (сетевые, кнопки с обеих сторон; кабинет директора — взлом 3 с) → комната отдыха (диван, кухня, шкафчики, кулер, ключ-карта $100 и деньги), серверная (стойки, люк вентиляции в подвал), кабинет директора (стол, сейф за картиной с деньгами, пульт видеонаблюдения, щиток лазеров `LaserControl_Net` — `NetworkLaserControl`, выключает лазеры у всех на 20 с).
- **Подвал** (пол −3, потолок −0.2): из коридора — широкая лестница (15 ступеней) с пандусом и перилами для вытаскивания сейфа; предбанник с выходом в гараж (проём в восточной стене x = 10); круглая сетевая бронедверь `VaultDoor_Net` (взлом 5 с снаружи, кнопка внутри); лазеры, ячейки, решётка. Главный лут здесь: сейф $2500, золотая статуя $1500, слитки, бриллиант (витрина перенесена из галереи). Вентшахта из серверной приводит прямо за лазеры.
- **Лут:** `Loot_GoldenStatue`, `Loot_KeyCard`, `Loot_CashStack` (префабы в `GeneratedModels/Bank`) — сетевые компоненты как у остального лута. Эвакуация — броневик в гараже, зона сдачи у задних дверей.
- **Мебель банка — из примитивов** (`BankFurniture`: кассы, справочная стойка, кресла, кассовые аппараты, мониторы, скамьи, банкоматы, диван, кухня, шкафчики, кулер, столы, пульт охраны, серверные стойки, решётка) — Tripo для мебели не использовать. **Tripo** (`Bank_*.glb`) только там, где сложно собрать руками: VaultDoor, GoldenStatue, GetawayTruck, PalmPlanter, StreetLamp, FireHydrant, TrashCan, NewsBox. Камеры видеонаблюдения и растения — исходные из билдера.
- **Свет:** новых источников 11 — 3 у фургона (гаснут, когда он уезжает), 2 луча заката в зале, 5 ламп служебного крыла, прожектор статуи. Volume `BankPostFX` (Bloom в четверть разрешения, тёплая сочная цветокоррекция) на камерах игроков в этой сцене. Конусы обзора камер (`M_ConeView`) видны сквозь стены — так задумано билдером.

## Сеть (Fusion 2 Shared)
- `FusionNetworkManager` (в обеих сценах, DontDestroyOnLoad): автовход в комнату `GRABZ_TEST` или по коду из GRABZ OS → ROOM. Режимы микрофона: ALL CHAT / PUSH TO TALK / PUSH TO MUTE. Смена сцены общая для всей комнаты.
- `NetworkGorillaAvatar` (`Assets/Prefabs/Network`): голова, руки, пальцы, цвет, косметика, ник, голос из рта (3D).
- **Кооп ограбления (HeistLevel_Test):**
  - `HeistNetworkState` — объект сцены, NetworkObject с флагом **Master Client Object** (власть всегда у хоста комнаты). В `[Networked]`-свойствах раунд, таймер, общий котёл, режим, тревога, маска разбитых стёкол и событие на табло. Остальные игроки шлют хосту RPC.
  - `HeistGameManager` / `AlarmSystem` / `BreakableGlass` / `LootRegistry` автоматически работают через `HeistNetworkState`, если он заспавнен. Без сети всё работает локально.
  - Лут: на каждом префабе `NetworkObject` (Allow State Authority Override), `NetworkTransform` и `NetworkLootItem`. При хвате вызывается `RequestStateAuthority()`, и физику считает взявший игрок. Предмет в чужих руках взять нельзя. Сданный или разбитый лут **прячется, а не уничтожается**, сброс возвращает его на место.
  - Новые сетевые объекты сцены после правки нужно запечь (`Tools/Fusion/Scene/Bake Scene Objects`; при сохранении сцены Fusion делает это сам), а префабы попадают в таблицу через `Tools/Fusion/Rebuild Prefab Table`.
- PlayFab (`PlayFabAuthManager`, MainHub): тихий вход по ID устройства, цвет шерсти хранится в UserData `FurColor`.

## Проверка перед коммитом
- Компиляция без ошибок и предупреждений (лог `Logs/Editor.log` или batchmode).
- Пока шлема нет, проверяем через `DesktopTestPlayer`. Сетевой кооп проверяем двумя клиентами (билд + редактор).
- Не коммитить секреты. App ID Photon и Title ID PlayFab уже лежат в ассетах настроек.

---

## Студия: Claude Code Game Studios (агенты, скиллы, хуки)
Подключён шаблон [Claude Code Game Studios](https://github.com/donchitos/claude-code-game-studios) v1.3.0 (MIT, лицензия — `.claude/LICENSE-ClaudeCodeGameStudios.txt`): 49 агентов-ролей студии, 75 скиллов-процессов и 19 хуков в `.claude/`, настройки в `project.yaml`, документы в `design/`, `docs/`, `production/`. **Правила GrabzVR выше главнее** правил студии: при расхождении действуют они (сцены, билдеры, папки, 3D-текст, сеть).

### Технологии
- **Движок**: Unity 6000.6.10f1 (URP 17.6)
- **Язык**: C#
- **Контроль версий**: Git, двое разработчиков, ветки `main` + личные (`bank`)
- **Сборка**: Unity Editor / batchmode (`-executeMethod`), Quest (Android) и ПК (Link)
- **Ассеты**: код и ассеты в `Assets/`; Tripo-модели через `TripoAssetPostprocessor`

### Структура проекта
@.claude/docs/directory-structure.md

### Версия движка
<!-- ENGINE-REFERENCE-IMPORT: the line under this comment is engine-specific,
     and there is none until an engine is set. /setup-engine, the grow's record
     step and /project-settings (engine.name) write
     @docs/engine-reference/<engine>/VERSION.md here for the chosen engine. Skills
     that need the pinned version read that file on demand either way. -->


### Технические настройки
`project.yaml` в корне — основное хранилище настроек студии (движок, специалисты, именование, платформа, режимы); скиллы читают его через `resolve_config` (`.claude/docs/config-resolution.md`). `.claude/docs/technical-preferences.md` — запасной вариант, читается по требованию.

### Правила координации агентов
@.claude/docs/coordination-rules.md

### Протокол совместной работы
**Решает пользователь, агенты не действуют сами.** Порядок: **Вопрос → Варианты → Решение → Черновик → Одобрение**.
- Агенты спрашивают «Можно записать в [путь]?» перед Write/Edit (исключения: дописывание `active.md`, память агента, новый файл субагента по пути, который назвал его скилл; см. `.claude/rules/agent-memory.md`).
- Перед одобрением агенты показывают черновик или сводку; правки в нескольких файлах — одобрение на весь набор.
- Без указания пользователя — никаких коммитов.
- Пропущенный или отклонённый вопрос — не ответ: ничего не делать, переспросить обычным текстом и ждать.
- Сессия открыта не в корне репозитория? Сначала предупредить: хуки, запреты и режим «сначала спросить» грузятся, только когда Claude Code запущен здесь.

Подробности и примеры — `docs/COLLABORATIVE-DESIGN-PRINCIPLE.md`. Начало работы: `/start` (онбординг), `/setup-engine unity 6000.6` (закрепить движок), `/project-stage-detect` (разбор существующего проекта), `/next-step`.

### Стандарты кода
@.claude/docs/coding-standards.md

### Управление контекстом
`.claude/docs/context-management.md` читать по требованию. Два правила обязательны:
- **`production/session-state/active.md` — контрольная точка сессии.** Память — файл, а не переписка. После сжатия контекста, сбоя или `/clear` читать его первым.
- **Скрипты в `.claude/scripts/` дают наблюдения, а не вердикты.**
