# План реализации MTGA-бота на C# (предпочтительная архитектура)

Документ описывает целевую архитектуру нового бота: **четыре независимых слоя** + **пятый этап — shadow mode и поэтапный rollout**.  
Источник данных — **GRE из `Player.log`** (как в Burning Lotus, но с нормальными границами модулей).

> **Цель:** farm daily quests / wins, не чемпионский AI.  
> **Принцип:** ingest → state → decide → actuate — каждый слой тестируется отдельно.  
> **Стек:** .NET 8+, C#; опционально Rust FFI для парсера ([manasight-parser](https://github.com/manasight/manasight-parser)).

---

## Обзор пайплайна

```
Player.log
    │
    ▼
┌──────────────┐     ┌──────────────┐     ┌──────────────┐     ┌──────────────┐
│ 1. INGEST    │ ──► │ 2. STATE     │ ──► │ 3. DECIDE    │ ──► │ 4. ACTUATE   │
│ GreLogTailer │     │ StateEngine  │     │ Policy / AI  │     │ Input+Vision │
│ typed events │     │ GameView     │     │ Intent       │     │ клики в MTGA │
└──────────────┘     └──────────────┘     └──────────────┘     └──────────────┘
```

**Контракт между слоями:** каждый следующий слой получает **типизированную модель**, не сырой JSON и не callback из 8000-строчного монолита.

---

## Структура solution

```
MtgaBot/
├── MtgaBot.sln
├── src/
│   ├── MtgaBot.Ingest/          # слой 1
│   ├── MtgaBot.State/           # слой 2
│   ├── MtgaBot.Decide/          # слой 3
│   ├── MtgaBot.Actuate/         # слой 4
│   ├── MtgaBot.Host/            # composition root, DI, конфиг
│   └── MtgaBot.Cli/             # shadow mode, replay, dev tools
├── tests/
│   ├── MtgaBot.Ingest.Tests/
│   ├── MtgaBot.State.Tests/
│   ├── MtgaBot.Decide.Tests/
│   └── MtgaBot.Integration.Tests/   # golden logs из runtime/debug/
└── data/
    └── cards.json               # symlink/copy из runtime/cache/cards.json
```

**Зависимости между проектами (только в одну сторону):**

`Ingest → State → Decide`  
`Actuate` зависит только от `Decide` (Intent) и конфига координат; **не** зависит от Ingest напрямую.

---

## Пункт 1. INGEST — доставка сырых GRE-событий

### Задача

Tail `Player.log` в реальном времени (и replay из файла) → поток **типизированных** `GreEvent`.

### Компоненты

| Класс / интерфейс | Ответственность |
|-------------------|-----------------|
| `IPlayerLogLocator` | Пути Windows / macOS / Proton (как в `LogReader._default_player_log_path`) |
| `GreLogTailer` | `FileStream` + async read; follow end-of-file; rotation на `Player-prev.log` |
| `GreLineRouter` | Поиск `greToClientEvent` / `greToClientMessages` в строке |
| `GreMessageDeserializer` | JSON parse; **второй pass** для string-escaped payloads |
| `GreEvent` (record) | `{ ulong Sequence, DateTimeOffset Timestamp, string RawLine, GreMessage Message }` |

### Обязательные типы сообщений (MVP)

Минимум для farm-бота — те же паттерны, что в `Controller.patterns`:

- `GREMessageType_GameStateMessage`
- `GREMessageType_QueuedGameStateMessage`
- `GREMessageType_TimerStateMessage`
- `GREMessageType_ActionsAvailableReq`
- `GREMessageType_MulliganReq`
- `GREMessageType_DeclareAttackersReq`
- `GREMessageType_DeclareBlockersReq`
- `GREMessageType_SelectTargetsReq`
- `GREMessageType_SelectNReq`
- `GREMessageType_GroupReq`
- `GREMessageType_PayCostsReq`
- `GREMessageType_CastingTimeOptionsReq`
- `GREMessageType_AssignDamageReq`
- Client: `ClientMessageType_SelectTargetsResp`, `SubmitAttackersReq`, `SetSettingsReq`
- Meta: `MatchGameRoomStateType_MatchCompleted`, hover `objectId`

### Правила парсинга (из опыта community / manasight)

1. **Итерировать весь** `greToClientMessages[]`, не брать только первый `GameStateMessage`.
2. Одна строка лога может содержать **несколько** batched state updates.
3. Не полагаться на устаревшие `Namespace.MethodName` API (удалены с ~2021).
4. Логировать `Sequence` + hash строки для replay/debug.

### Выход слоя

```csharp
public interface IGreEventSource
{
    IAsyncEnumerable<GreEvent> TailLive(CancellationToken ct);
    IReadOnlyList<GreEvent> ParseFile(string path); // для тестов и shadow
}
```

### Тесты

- Golden files: фрагменты из `runtime/debug/*/log_tail.txt`
- Unit: batched messages, double-encoded JSON, пустые строки
- **Критерий готовности:** replay лога матча выдаёт тот же count событий при повторном прогоне

### Опция ускорения

Обернуть [manasight-parser](https://github.com/manasight/manasight-parser) (Rust → WASM или native DLL) вместо своего deserializer — только если свой парсер не проходит golden tests за 1–2 недели.

---

## Пункт 2. STATE — event-sourced модель и GameView

### Задача

Из потока `GreEvent` собрать **стабильный snapshot** и **DecisionPoint** — момент, когда можно звать AI.

### Комponentы

| Класс | Ответственность |
|-------|-----------------|
| `GameStateReducer` | Merge incremental `GameStateMessage`; `diffDeletedInstanceIds` |
| `ObjectRegistry` | `instanceId → GameObject` (grpId, zone, power/toughness, tapped…) |
| `ZoneIndex` | Hand, battlefield, stack, graveyard по `ownerSeatId` |
| `AnnotationTracker` | Merge + **explicit purge** transient annotations (`PlayerSelectingTargets`) |
| `PromptTracker` | Текущий активный GRE req (SelectTargets, Attackers, …) |
| `MatchLifecycleFsm` | OUT_OF_GAME / IN_MATCH / SIDEboard / POST_MATCH (отдельно от in-game) |
| `DecisionGate` | Разрешить decide только при выполнении условий (см. ниже) |

### DecisionGate — условия «можно принимать решение»

```csharp
bool CanDecide(GameSnapshot snap, DecisionPoint prompt) =>
    snap.PendingMessageCount == 0
    && prompt.Kind != DecisionKind.None
    && prompt.LegalActions.Count > 0
    && prompt.SystemSeatId == snap.MySeatId
    && !ActuatorBusy;  // координация с слоем 4
```

### Модели (публичный контракт для Decide)

```csharp
public sealed record GameSnapshot(
    int MySeatId,
    TurnInfo Turn,
    IReadOnlyDictionary<int, CardView> Objects,
    IReadOnlyList<int> HandInstanceIds,
    IReadOnlyList<int> BattlefieldInstanceIds,
    IReadOnlyList<int> StackInstanceIds,
    int MyLife,
    int OpponentLife,
    ManaPool Mana,
    int PendingMessageCount
);

public sealed record DecisionPoint(
    ulong DecisionId,           // монотонный, для idempotency
    DecisionKind Kind,          // MainPhase, Mulligan, SelectTargets, Attackers, Blockers, ...
    IReadOnlyList<LegalAction> LegalActions,
    PromptContext? Prompt       // targets, min/max selects, attack restrictions
);

public sealed record GameView(
    GameSnapshot Board,
    DecisionPoint Decision,
    MatchPhase Lifecycle
);
```

### `LegalAction`

Нормализовать из `ActionsAvailableReq` + `actions` в GameState:

```csharp
public sealed record LegalAction(
    string ActionType,          // "Cast", "Attack", "Pass", "Activate", ...
    int? InstanceId,
    int SeatId,
    IReadOnlyDictionary<string, object>? Payload
);
```

### Выход слоя

```csharp
public interface IStateEngine
{
    void Apply(GreEvent evt);
    GameView? TryGetDecisionView();  // null если gate closed
    event Action<GameView> DecisionReady;
}
```

### Тесты

- Replay полного матча → snapshot на каждом `DecisionId` (snapshot testing)
- Annotation purge после `SelectTargetsResp`
- Turn/phase transitions при batched updates
- **Критерий готовности:** shadow mode показывает корректные hand size / life / legal actions на 10+ replay-логах

---

## Пункт 3. DECIDE — Policy / AI → Intent

### Задача

Чистая функция: `GameView → Intent`. **Без** файлов, мыши, таймеров, лога.

### Комponentы

| Класс | Ответственность |
|-------|-----------------|
| `ICardDatabase` | Load `cards.json`; grpId → name, types, mana cost, oracle text |
| `ICardPolicy` | Regex/blocklist «опасных» карт (scry, modal, fight…) — порт `CardPolicy` |
| `IFarmPolicy` | MVP: land → safe creature/enchantment → all_attack → pass |
| `IIntentSelector` | Выбор одного Intent из legal actions |
| `SafeFallbackPolicy` | Timeout / exception → `PassIntent` или `ResolveIntent` |

### Intent (выход Decide)

```csharp
public abstract record Intent;

public sealed record CastIntent(int InstanceId) : Intent;
public sealed record AttackAllIntent() : Intent;
public sealed record AttackWithIntent(int InstanceId) : Intent;
public sealed record PassPriorityIntent() : Intent;
public sealed record ResolveIntent() : Intent;
public sealed record SelectTargetIntent(int InstanceId) : Intent;
public sealed record KeepHandIntent(bool Keep) : Intent;
public sealed record DeclareNoBlocksIntent() : Intent;
public sealed record AcknowledgeGroupIntent() : Intent;  // scry/surveil → Done
public sealed record NoOpIntent(string Reason) : Intent;
```

### Маппинг Intent → исполнение

Decide **не знает** про координаты. Actuate содержит `IIntentExecutor` с pattern matching по типу Intent.

### Расширения (после MVP)

- `RemovalPolicy` / `CounterPolicy` — только если Actuate умеет стабильный `SelectTarget`
- Sideboard / mulligan heuristics
- Deck-specific profiles (JSON config per deck)

### API

```csharp
public interface IPolicy
{
    Intent Decide(GameView view, ICardDatabase cards);
}

public interface IDecisionService
{
    Task<Intent> DecideAsync(GameView view, CancellationToken ct);
}
```

### Тесты

- Unit: mana payment, land-once-per-turn, skip unsupported oracle patterns
- Property: Intent.InstanceId всегда ∈ LegalActions
- **Критерий готовности:** 100% shadow-решений на replay не содержат illegal instanceId

---

## Пункт 4. ACTUATE — Intent → UI-действия

### Задача

Исполнить Intent через **SendInput** + опционально **OpenCvSharp** для verify. Hover `objectId` — только здесь.

### Комponentы

| Класс | Ответственность |
|-------|-----------------|
| `IWindowLocator` | Find MTGA window (Win32 `FindWindow` / UI Automation) |
| `ICoordinateMap` | 1920×1080-relative → window client rect (из calibration JSON) |
| `IInputBackend` | `SendInput`, mouse move/click; без pyautogui |
| `IHoverResolver` | Scan hand/battlefield → wait `objectId` in log → click |
| `IVisionVerify` | Template match post-click (port `VisionEngine` concepts) |
| `IPromptHandler` | Strategy per `DecisionKind` для mid-priority prompts |
| `IIntentExecutor` | Orchestrator: Intent → sequence of `UiAction` |
| `ActuatorScheduler` | **Один** queue; cancel stale; no `Timer` per decision |

### Prompt handlers (отдельные классы, не switch на 8000 строк)

```
MulliganHandler
SelectTargetsHandler
DeclareAttackersHandler
DeclareBlockersHandler      → MVP: DeclareNoBlocks
GroupReqHandler             → Done only
SelectNHandler
PayCostsHandler
CastingTimeOptionsHandler   → always non-kicker option
MenuNavigationHandler       → Play, deck select, post-match (template-based)
```

### HoverResolver

1. Move mouse along hand arc (configurable points)
2. Subscribe to `GreEvent` with `objectId` (от Ingest, fan-out)
3. Match `instanceId` → click
4. Retry ≤ N, timeout → fail up to Host → re-decide or safe pass

### Координация с State

```
ActuatorBusy = true  на время серии UiAction
DecisionGate блокирует новый decide пока исполняется Intent
После Submit* client message в логе — ActuatorBusy = false
```

### Menu / farm loop (вне GRE)

Отдельный `FarmOrchestrator` в Host:

- account switch
- quest deck selection
- queue / play button
- match restart

Можно временно переиспользовать Python Burning Lotus как **menu-only subprocess** (опциональный bridge), пока C# Actuate не покрывает навигацию.

### Тесты

- Mock input: record `UiAction` sequences per Intent
- Integration (manual): calibration profile + one full game
- **Критерий готовности:** 5 подряд матчей starter deck без rope timeout

---

## Пункт 5. Shadow mode, rollout и migration

### 5.1 Shadow mode (первый запуск в production-quality)

**Цель:** отладить Ingest + State + Decide **без единого клика**.

```bash
MtgaBot.Cli shadow --log "C:\...\Player.log" --policy FarmMvp
# или live tail:
MtgaBot.Cli shadow --follow --log "...\Player.log"
```

Вывод на каждый `DecisionId`:

```
[turn 4 MAIN1] legal: Cast(123), Cast(124), Pass
→ Intent: CastIntent(123)  // Lightning Strike — SKIPPED (unsupported)
→ Intent: CastIntent(124)  // Grizzly Bears
```

Сравнение с `runtime/logs/bot.log` Burning Lotus (optional diff tool).

### 5.2 Фазы rollout

| Фаза | Слои | Результат |
|------|------|-----------|
| **0** | Ingest + golden tests | Стабильный парсер |
| **1** | + State + shadow | Корректный GameView на replay |
| **2** | + Decide + shadow | Legal intents на 10+ логах |
| **3** | + Actuate in-game only | Играет матч; menu вручную |
| **4** | + Menu navigation | Полный farm loop |
| **5** | + Account switch, supervisor | Замена Burning Lotus |

### 5.3 Сосуществование с Burning Lotus

| Режим | Описание |
|-------|----------|
| `HybridMenu` | Python UI/меню + C# in-game via pipe |
| `FullCSharp` | `MtgaBot.Host` + простой WPF/tray UI |
| `CliOnly` | Headless farm, конфиг YAML |

Env-переменные (если hybrid):

```
MTGA_BOT_ENGINE=csharp
MTGA_BOT_CSHARP_EXE=MtgaBot.Host.exe
```

### 5.4 Observability

| Артефакт | Назначение |
|----------|------------|
| `runtime/logs/mtgabot.jsonl` | Structured: DecisionId, Intent, latency ms |
| `runtime/debug/decision-{id}/` | Snapshot + legal actions + screenshot |
| Metrics | `% successful casts`, `% rope timeouts`, hover miss rate |

### 5.5 Риски и митигация

| Риск | Митигация |
|------|-----------|
| GRE format change | Golden tests + manasight-parser fallback |
| Hover miss | Wider scan, calibration profiles |
| Unsupported prompts | CardPolicy + AcknowledgeGroupIntent |
| Wizards ToS | Документировать «at your own risk»; без injection в MVP |
| Scope creep | MVP = mono-color aggro starter; no sideboard AI |

---

## MVP scope (что сознательно не делаем в v1)

- [ ] Injection / BepInEx bridge (как mtgacoach)
- [ ] OCR как primary state (как chris-lansman/mtga-bot)
- [ ] Полноценный combat math / blocking AI
- [ ] Bo3 sideboard strategy
- [ ] Draft bot
- [ ] macOS/Linux Actuate (сначала Windows only)

---

## Карта соответствия Burning Lotus → C#

| Burning Lotus (Python) | Новый модуль C# |
|------------------------|-----------------|
| `LogReader.py` | `MtgaBot.Ingest` |
| `GameState.py` + merge в Controller | `MtgaBot.State` |
| `AI/StableFarmAI.py`, `DummyAI.py` | `MtgaBot.Decide` |
| `Controller.py` (in-game actions) | `MtgaBot.Actuate` |
| `Game.py` | `MtgaBot.Host` |
| `vision/`, `input_controller` | `MtgaBot.Actuate.Vision`, `.Input` |
| `ui.py` | Phase 5 — WPF или временно Python shell |
| `runtime/cache/cards.json` | `data/cards.json` (shared) |

---

## Оценка сроков (solo, part-time)

| Фаза | Срок |
|------|------|
| 0–1: Ingest + State + shadow | 3–4 недели |
| 2: Decide MVP | 1–2 недели |
| 3: Actuate in-game | 3–4 недели |
| 4–5: Menu + farm loop | 2–4 недели |
| **Итого до замены Lotus** | **~2.5–4 месяца** |

---

## Следующий шаг

1. Создать `MtgaBot.sln` с пустыми проектами и `GreLogTailer` stub.
2. Скопировать 3–5 файлов `runtime/debug/*/log_tail.txt` в `tests/fixtures/`.
3. Реализовать Ingest до green golden tests.
4. Запустить `shadow` на live `Player.log` во время ручной партии.

---

*Документ создан как архитектурный план; не является частью runtime Burning Lotus.*
