# GrabzVR — правила проекта

Кооперативная VR-игра про ограбление в стиле Gorilla Tag: передвижение руками, лут, лазеры, эвакуация.
**Unity 6000.6.10f1 + URP 17.6**, OpenXR (Quest через Link и Android), XR Interaction Toolkit 3.7.
Сеть: **Photon Fusion 2.1 (Shared Mode)** + **Photon Voice 2** + **PlayFab** (Title ID `1FEC73`).
Разработка идёт вдвоём с двух ПК через git, ветка `main`. Комментарии в коде и тексты в игре на русском.

## Сцены и Build Settings
| Индекс | Сцена | Назначение |
|---|---|---|
| 0 | `Assets/Scenes/MainHub.unity` | Главный хаб: зеркало, выбор цвета шерсти (`SkinButton`), стол режимов, фургон-портал (`VanPortal`), ретро-компьютер GRABZ OS (`GrabzComputer`), баскетбол |
| 1 | `Assets/Scenes/HeistLevel_Test.unity` | Банк: ночная улица с фургоном грабителей, фасад, операционный зал, галерея, офисы, хранилище за сетевой бронедверью, гараж с броневиком (зона сдачи лута) |
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

## Карта Банка (HeistLevel_Test)
- **Апгрейд поверх сцены:** `Tools/Heist/Bank/Visual Overhaul` (`BankVisualOverhaul`, partial: `BankExterior`, `BankInteriorProps`). Идемпотентный: пересоздаёт корень сцены `BankDecor`, перетекстурирует архитектуру `HeistMap/Z*/Static`, прячет (не удаляет) заменённые примитивы. `HeistMapBuilder` не вызывает. Превью: `Tools/Heist/Bank/Capture Preview` → `HubPreview/Bank_*.png`.
- **Папки Банка:** скрипты `Assets/Scripts/Bank/` (editor — в `Assets/Scripts/Bank/Editor/`), материалы, текстуры и профиль Post-FX `Assets/Art/Materials/Bank/` (`M_Bank_*`), Tripo-модели, UV-меши и префаб лута `Assets/Art/GeneratedModels/Bank/`. Общие `M_*.mat` не менять.
- **Старт ограбления:** тестового Убежища со шлюзом и пультом больше нет (`Z1_Hub_Bunker` спрятан). Игроки появляются в кузове фургона `GetawayVan_Arrival` у ступенек Банка.
  - `NetworkHeistStartTrigger` — объект сцены, Master Client Object. Каждый клиент следит за своим игроком; вышел из кузова — RPC хосту.
  - Хост ставит `[Networked] IsHeistStarted`, когда вышла вся команда или через 10 с после первого вышедшего, и вызывает `HeistGameManager.StartHeist()`.
  - У всех синхронно: двери захлопываются, двигатель газует, фургон уезжает за угол и прячется. Кто остался в кузове — переносится на тротуар.
- **Дверь хранилища** `VaultDoor_Net` (`NetworkVaultDoor`, Master Client Object, `[Networked] IsOpen` + таймер взлома): кнопка «ВЗЛОМ» в Холле — 5 с взлома, кнопка «ДВЕРЬ» внутри — сразу. Старая решётка `VaultGate` (локальная `SlidingDoor`) спрятана.
- **Улица:** тротуар, бордюр, асфальт с разметкой, дома напротив с окнами и неоном, фонари, гидранты, урны, газетные автоматы. Фасад с портиком, 6 колоннами (по кольцам можно залезть), лестницей и неоновой вывеской GRABZ BANK (TextMeshPro с HDR-материалом `M_Bank_Neon*`). Невидимая граница бокового переулка — слой Ignore Raycast.
- **Tripo** (`Assets/Art/GeneratedModels/Bank/Bank_*.glb`): VaultDoor, GoldenStatue, ReceptionDesk, GetawayTruck, SecurityCamera, IronBars, ATM, TellerCounter, OfficeDesk, PottedPlant, LeatherBench, StreetLamp, FireHydrant, TrashCan, NewsBox. У мебели — MeshCollider на слое Default. Камеры видеонаблюдения: модель внутри поворотной части `SecurityCamera`, без коллайдеров (не закрывает луч обзора); тревога работает через `AlarmSystem`, как раньше.
- **Текстуры по зонам** (`BankRetroTextures`, 256 px, ковролин и краска 128): мрамор, известняк, обои, деревянные панели, ковролин, бронеплиты, рифлёный пол, асфальт, бетон опалубки, тротуарная плитка, паркет, штукатурка. С «чужой» стороны общих стен — облицовка `BankDecor/Static/Cladding`.
- **Лут:** золотая статуя основателя `Loot_GoldenStatue` ($1500, тяжёлая) в Галерее, сетевые компоненты как у остального лута. **Эвакуация:** броневик в гараже, зона сдачи у его задних дверей.
- **Свет:** ночное небо (`M_Bank_NightSky`), тёплые фонари, холодный операционный зал, приглушённое золото в хранилище, красная сирена (`AlarmSystem`). Новых источников 8: 4 фонаря, 2 фары и плафон фургона (гаснут, когда он уезжает), прожектор статуи. Volume `BankPostFX` (Bloom в четверть разрешения + цветокоррекция) на камерах игроков в этой сцене.

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
