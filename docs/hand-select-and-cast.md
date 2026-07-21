# Hand select / cast: сбивающая логика (ключевое узкое место)

Документ для разбора **самого нестабильного участка** farm-бота: «найти карту в руке по `instanceId` и сыграть её».  
И Burning Lotus, и текущий MtgaBot C# падают именно здесь на «простых» ходах (земля / каст существа), хотя keep/pass по фиксированным кнопкам часто работают.

Цель файла — зафиксировать **как устроено сейчас**, **почему ломается**, и **что доводить до ума** для рабочей версии. Не roadmap меню и не полный AI.

---

## 1. В чём задача на пальцах

GRE говорит: «можно сыграть карту с `instanceId = 160`».

UI MTGA **не даёт** координаты карты. Есть только:

1. курсор едет по нижней дуге руки;
2. в `Player.log` появляется hover `objectId` (тот же смысл, что instanceId);
3. когда `objectId == 160` — клик / double-click.

Это **closed-loop по логу**, не «посчитать X от позиции в руке».

```
LegalAction(Play/Cast, instanceId)
        │
        ▼
   Intent (FarmMvp)
        │
        ▼
 HoverResolver: скан руки ──► objectId из лога ──► match? ──► click
        ▲                              │
        └──────── miss / retry ────────┘
```

Пока петля ненадёжна — бот «вечно возит» курсор слева направо или кликает не ту карту.

---

## 2. Два разных слоя (их нельзя смешивать в голове)

| Слой | Вопрос | Где код |
|------|--------|---------|
| **A. Когда и что решать** | Уже ли Main1? Земля или каст? Не sticky-список с Beginning? | `State` (`PromptTracker`, `DecisionGate`) + `Decide` (`FarmMvpPolicy`) |
| **B. Как попасть в карту** | Скан → hover → click | `Actuate` (`HoverResolver`, `LogHoverObjectIdSource`, `SendInput`) + Ingest hover parse |

Ошибки слоя A выглядят как ошибки слоя B: бот сканирует руку, хотя **сейчас нельзя** нормально кастовать, или кастует вместо земли.

На тестах 2026-07 мы видели оба слоя:

- Keep (фиксированная кнопка) — ок.
- На `Phase_Beginning` пришли sticky `Cast`/`Play` → `CastIntent` → долгий скан (A ломает B).
- Даже после гейта Main1/Main2 скан может не стабилизировать каст (B сам по себе хрупкий — как в Lotus).

---

## 3. Слой A — «когда решать»

### 3.1 Откуда берётся DecisionReady

`StateEngine` копит GRE → `PromptTracker` + `LegalActionFilter` → `DecisionGate` → `DecisionReady` / `TryGetDecisionView`.

Live loop (`LiveExecuteRunner`):

1. **Catch-up** последних ~2 MB `Player.log` (иначе mulligan уже на экране не виден).
2. Параллельный tail: строки → GRE в канал + hover в `LogHoverObjectIdSource`.
3. На решение: `FarmMvpPolicy.Decide` → `IntentExecutor`.

`ActuatorBusy` блокирует новые решения, пока идёт скан/клик.

### 3.2 Sticky actions и фазы

GRE часто кладёт в Diff список `actions` (Cast/Play/Pass) **раньше**, чем игрок реально в окне приоритета Main.

Типичный провал:

```
turn=1 phase=Phase_Beginning  kind=MainPhase
legal: Cast, Cast, Play, Play, …
→ CastIntent(159)     ← политика думает «можно кастовать»
→ HoverResolver сканит всю руку снова и снова
```

**Текущий костыль/фикс в C#:**

- `PromptTracker.IsPlayablePriorityWindow` — только `Phase_Main1` / `Phase_Main2`.
- `FarmMvpPolicy` на `MainPhase` вне Main1/Main2 → `PassPriorityIntent`.

Это уменьшает ложные сканы на Beginning, но **не чинит** сам hover.

### 3.3 Политика FarmMvp (что кликать)

На Main1/Main2:

1. один раз за ход первая `ActionType_Play` (земля) → `CastIntent(landId)` *(имя Intent историческое — это и Play, и Cast)*;
2. иначе «безопасный» перманент (`CardPolicy`) с макс. CMC;
3. иначе pass.

Важно: **мана уже отфильтрована GRE** в legal actions. Политика не считает стоимость заново.

Слабые места политики для стабильных базовых ходов:

- `Play` без `instanceId` → земля пропускается, уходим в Cast.
- Prune `LegalActionFilter` выкидывает Play, если id ещё не в `HandInstanceIds`.
- Нет проверки «карта всё ещё в руке / действие ещё валидно» **перед** долгим сканом.
- Нет отличия Play vs Cast на уровне Intent (оба `CastIntent`) — отладка путается.

---

## 4. Слой B — «как найти карту» (ядро нестабильности)

### 4.1 Как сделано в Burning Lotus (эталон боли)

Файл: `Controller.py` — `cast` / `_cast_once` / hand select.

Идея та же:

1. закрыть options overlay;
2. сбросить флаг hover в `LogReader`;
3. курсор **над** рукой (reset);
4. ехать по линии `hand_scan_p1 → hand_scan_p2` шагом `cast_card_dist`, ждать **новую** строку с `objectId`;
5. парсить id; если совпал с целью — клик;
6. до 3 retry с паузой ~0.8 s;
7. на провал — debug bundle (скрин + мета).

Особенности Lotus, которых у C# пока мало/нет:

- скан **относительными** шагами (`move_rel`), а не только сеткой design→screen;
- явная реакция на «hover пришёл на долю секунды позже точки»;
- debug bundle при miss;
- куча эвристик вокруг overlay / group req / suppress.

Именно этот блок в Lotus был **хронически нестабилен** на базовых ходах.

### 4.2 Как сделано в MtgaBot C# сейчас

| Компонент | Роль |
|-----------|------|
| `HoverObjectIdParser` | Из строки лога: `uiMessage.hover.objectId` (+ seat); не брать GameState с кучей objectId |
| `LogHoverObjectIdSource` | Host: `ObserveLine` из tail → `WaitForAsync(instanceId)` |
| `HoverResolver` | Reset над рукой → точки P1…P2 шагом `HandScanStep` → wait timeout на точку → double-click |
| `IntentExecutor` | Cast/AttackWith/SelectTarget → до 3 retry скана с паузой |
| `CalibrationProfile` | design 1920×1080: `hand_scan_points`, кнопки Keep/Next/… |
| `SendInputBackend` | SetCursorPos + absolute SendInput, проверка GetCursorPos |

Параметры по умолчанию (хрупкие):

- ~40 ms ожидания hover на **точку**;
- ~10 ms между точками;
- шаг 10 px в design space → на 1920 ширины ~200 точек на проход;
- 3 прохода → визуально «вечная тяга» слева направо.

### 4.3 Почему петля сбивается (чеклист причин)

**Сигнал hover**

- Строка hover не GRE / парсер отфильтровал.
- Seat filter отбросил свой hover.
- Hover запаздывает сильнее 40 ms → точка уже уехала («No hover update before bounds» в Lotus).
- В pending копятся чужие id; Reset недостаточен между точками.

**Геометрия**

- Калибровка hand arc не совпадает с реальной рукой (разрешение, letterbox, DPI).
- Окно не в design 1920×1080 client; `CoordinateMap` масштабирует криво.
- Fullscreen / скрытый hardware cursor — кажется, что «мышь не едет», клики иногда всё же доходят.

**Время и состояние**

- Скан начат по устаревшему DecisionId (карта уже не в руке / не legal).
- Во время скана пришёл новый Diff; busy не отменяет текущий скан.
- Catch-up / двойное решение: Keep ок, следом ложный MainPhase.

**Исполнение**

- Кликнули не ту карту (ложный match / stale objectId).
- Нужен single-click для земли vs double-click для cast (сейчас везде double-click в `HoverResolver`).
- После «успеха» GRE не подтвердил Play/Cast — бот считает ok по hover+click, не по логу ответа.

---

## 5. Live loop вокруг скана (как тестировали)

Команда: `dotnet run --project src/MtgaBot.Cli -- actuate live`

```
ParseRecent (catch-up)
    → TryGetDecisionView → возможно Keep сразу
TailRawLines(resumeOffset) ──┬── GreEvent → StateEngine → DecisionReady
                             └── hover lines → LogHoverObjectIdSource
Decision → FarmMvp → IntentExecutor
                      ├─ Keep/Pass/Attack: клик по калиброванной кнопке
                      └─ CastIntent: HoverResolver (долгий путь)
```

Вспомогательно: `actuate mouse-probe` — только Win32 мышь, без GRE.  
`actuate dry-run` / `actuate live --dry-run` — план UiAction **без** реального hover match (`ImmediateHoverObjectIdSource`).

Имеет смысл разделять тесты:

| Что проверяете | Команда / способ |
|----------------|------------------|
| Мышь вообще едет | `actuate mouse-probe` |
| Keep / Next без руки | live на mulligan / pass (фиксированные точки) |
| Только политика (без кликов) | `shadow --follow` |
| Полный hand select | `actuate live` на Main1 с землёй |

Смешивать «политика выбрала Cast на Beginning» и «hover miss» в одном выводе — главный источник путаницы при отладке.

---

## 6. Что уже зафиксировано в git (контекст)

Отдельные коммиты по теме:

1. HoverObjectIdParser  
2. ParseRecent / catch-up  
3. SendInput + focus окна  
4. Hover retry + calibration defaults  
5. Live actuate + mouse-probe  
6. Gate Main1/Main2  

Последний пункт — **частичный** фикс слоя A. Слой B для «стабильная земля каждый ход» **ещё не доведён**.

---

## 7. Куда доводить для рабочей версии (приоритеты)

Без костылей «ещё один sleep». Порядок осмысленного усиления:

### P0 — корректность решения до скана

1. Эмитить / исполнять hand-cast **только** в подтверждённом окне приоритета (Main1/Main2 + свой seat + legal id ∈ hand).
2. Перед сканом **перепроверить** `TryGetDecisionView`: тот же DecisionId / instance всё ещё legal.
3. В логе всегда печатать `Play(id)` / `Cast(id)`, phase, step (уже частично в reporter).
4. Развести Intent: `PlayLandIntent` vs `CastIntent` (хотя бы для метрик и клик-профиля).

### P0 — подтверждение успеха не по «кликнули»

5. Успех = в логе ушёл client message / карта ушла из hand / legal список изменился; иначе fail + retry или pass.
6. Таймаут на весь cast (например 3–4 s), не «3 полных прохода по 200 точек без бюджета».

### P1 — сам скан (порт лучших идей Lotus + ужесточение)

7. Ждать hover **событие** (новая строка), а не фиксированные 40 ms на клетку сетки.
8. Адаптивный шаг: грубый проход → уточнение около кандидата.
9. Калибровка hand arc под реальное окно + проверка в начале live (`window` rect уже есть).
10. Debug bundle при miss: screenshot, target id, last N hover ids, scan endpoints (как Lotus).

### P1 — ввод

11. Single-click vs double-click по типу действия (земля часто иначе, чем spell).
12. Не сканировать, пока options/modal overlay открыт (Lotus `_ensure_options_overlay_closed`).

### P2 — наблюдаемость

13. Метрики: `% land success`, `% cast success`, `hover miss rate`, среднее время скана.
14. Режим `actuate live --cast-only-after-main1` / запись jsonl на каждый attempt.

---

## 8. Минимальный сценарий приёмки «базовые ходы стабильны»

Пока не пройдено — hand select нельзя считать готовым:

1. Keep с catch-up — стабильно.  
2. Ход 1: ровно одна земля, без скана на Beginning.  
3. Ход 1–2: одно безопасное существо при наличии маны.  
4. Pass / attack all по кнопкам.  
5. 5 матчей starter подряд без «вечной тяги» руки и без rope из‑за miss.

Критерий из плана архитектуры (§4 Actuate): *5 подряд матчей starter deck без rope timeout* — по сути про этот блок.

---

## 9. Карта файлов (куда смотреть в коде)

```
Ingest/
  HoverObjectIdParser.cs      — парсинг objectId
  GreLogTailer.cs             — TailRawLines, ParseRecent
Host/Actuate/
  LiveExecuteRunner.cs        — live loop + catch-up + busy
  LogHoverObjectIdSource.cs   — WaitForAsync(instanceId)
  LiveExecuteReporter.cs      — вывод decision/legal
Actuate/
  IHoverResolver.cs           — скан руки
  IntentExecutor.cs           — Intent → hover/кнопки + retry
  CalibrationLoader.cs        — hand_scan_points
  Windows/SendInputBackend.cs — мышь
State/
  PromptTracker.cs            — Main1/Main2 gate, sticky prompts
Decide/
  FarmMvpPolicy.cs            — land → cast → pass
```

Lotus (для сравнения): `Controller.py` (`cast`, `_cast_once`, hand/battlefield select), `LogReader`, `calibration_config.json` → `hand_scan_points`.

---

## 10. Краткий вывод

**Сбивающая логика** — не «AI», а **синхронизация трёх часов**:

1. GRE legal / фаза (можно ли уже Play/Cast),  
2. поток hover `objectId` из лога,  
3. движение курсора по калиброванной дуге руки.

Lotus нестабилен в (2)+(3). C# сейчас добавил каркас и частично закрыл (1) гейтом Main1/Main2, но **петля hand select ещё не production-grade**: нет подтверждения успеха по GRE, грубая сетка с коротким timeout, мало диагностики на miss.

Дальше имеет смысл **сначала** ужесточить слой A и критерий успеха, **потом** переписывать алгоритм скана (событийный hover + бюджет времени + debug bundle), а не наращивать sleep/retry вслепую.
