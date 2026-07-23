# План: стабильная FSM базовых ходов

Цель: довести farm-бот до **детерминированного** цикла «keep → земля → каст → атака → pass» без «вечной тяги» руки и без rope timeout.

Разбор узкого места hand select: [`hand-select-and-cast.md`](hand-select-and-cast.md).  
Этот файл — **дорожная карта доработок по шагам**. Каждый шаг имеет приёмку; следующий шаг не трогает поведение предыдущих, кроме явных regression-фиксов.

---

## 0. Принципы

1. **Слои разделены.**  
   - **A (Decide/State):** когда и что решать — чистая FSM по GRE.  
   - **B (Actuate):** как попасть в UI — подпрограмма с бюджетом времени и критерием успеха по GRE.  
   Не смешивать «политика ошиблась» и «hover miss» в одной отладке.

2. **Успех = смена game state, не клик.**  
   Intent выполнен, только если лог/снимок подтвердил эффект (карта ушла из hand, legal изменился, client message ушёл). Hover+click — средство, не критерий.

3. **Один инкремент — одна способность.**  
   Сначала стабильная земля; каст и атака подключаются только после зелёной приёмки земли. Флаги/режимы (`--land-only`, потом `--land-and-cast`, …) или политика «разрешённые Intent-типы» — чтобы регресс ловился сразу.

4. **Не ломать предыдущий шаг.**  
   Перед мержем шага N: прогнать приёмку шагов 1…N−1. Изменения калибровки/скана для каста не должны ухудшать land success rate.

5. **Без слепых sleep/retry.**  
   Таймаут на весь actuate, событийный wait, явный fail → pass или один контролируемый retry.

---

## 1. Целевая FSM (слой A)

Минимальный автомат для starter/farm MVP. Состояния — логические; в коде могут жить как флаги хода + `DecisionGate`, главное — явные переходы и запрет действий вне окна.

```
                    ┌─────────────┐
                    │  Mulligan   │──Keep──►
                    └─────────────┘         │
                                            ▼
┌──────────────────────────────────────────────────────────────┐
│                     Turn loop (свой приоритет)                 │
│                                                              │
│  Main1 ──► [PlayLand?] ──► [CastSafe?] ──► Combat            │
│              │ done/skip      │ done/skip       │            │
│              ▼                ▼                 ▼            │
│           LandDone         CastDone      DeclareAttackers    │
│                                                 │            │
│                                                 ▼            │
│                                              AttackAll       │
│                                                 │            │
│  Main2 ──► (обычно Pass) ◄──────────────────────┘            │
│     │                                                        │
│     ▼                                                        │
│   Pass / End                                                 │
└──────────────────────────────────────────────────────────────┘
```

Правила переходов (инварианты):

| Из | Можно Intent | Нельзя |
|----|--------------|--------|
| Не Main1/Main2 (Beginning и т.п.) | только Pass / Keep / то, что требует prompt | Play / Cast / скан руки |
| Main1, земля ещё не сыграна, есть `ActionType_Play` | `PlayLandIntent` | Cast «вместо» земли, если Play legal |
| Main1, земля done/skip | `CastIntent` (безопасный) или Pass | повторный Play |
| DeclareAttackers | `AttackAllIntent` (MVP) | скан руки |
| Нет своего приоритета / ActuatorBusy | ничего | новый скан |

`Actuating*` — не отдельные фазы GRE, а **подсостояния исполнителя**: FSM Decide не выдаёт новый Intent, пока actuate не завершился success/fail.

---

## 2. Дорожная карта по шагам

### Шаг 0 — Каркас FSM и наблюдаемость (фундамент)

**Зачем:** чтобы шаги 1+ отлаживались по фактам, а не по «мышь ездит».

| # | Работа | Готово когда |
|---|--------|--------------|
| 0.1 | Развести Intent: `PlayLandIntent` vs `CastIntent` (метрики, клик-профиль) | reporter печатает тип явно |
| 0.2 | Перед actuate: re-check `DecisionId` + id всё ещё legal + ∈ hand | устаревший Intent не стартует скан |
| 0.3 | Режим политики: `LandOnly` / `LandAndCast` / `FullMvp` (или CLI-флаги) | можно гонять только землю |
| 0.4 | Лог attempt: phase, step, intent, target id, outcome, ms | один jsonl/строка на попытку |
| 0.5 | Успех actuate = GRE-эффект (см. шаг 1.4), не «клик без ошибки» | общий контракт `IActuateOutcome` |

**Приёмка шага 0:** shadow/live dry-run на Main1 показывает `PlayLand` только в Main1/Main2; на Beginning — Pass; нет скана вне окна.

**Не трогать:** геометрию скана (кроме логирования).

---

### Шаг 1 — Стабильная земля (P0)

**Цель:** каждый свой Main1 с legal `Play` → ровно одна земля на стол, без вечного скана.

#### 1A — Решение (слой A)

| # | Работа |
|---|--------|
| 1.1 | Play только при `IsPlayablePriorityWindow` + свой seat + `instanceId` ∈ hand |
| 1.2 | `Play` без id не маскировать под Cast; skip land + явный лог |
| 1.3 | После успеха/fail земли — флаг `landPlayedThisTurn` / `landAttempted`; не крутить Play снова |
| 1.4 | Критерий успеха земли: id исчез из hand **или** `ActionType_Play` пропал из legal **или** виден client play/cast submit (что стабильнее в логе — зафиксировать в коде одним helper) |
| 1.5 | Бюджет actuate земли: ≤ 3–4 s; по истечении → fail → Pass (не 3×200 точек) |

#### 1B — Попадание в карту (слой B) — inventory-скан

Индекс hand → калиброванная точка **не используем** как основной hit: геометрия руки плавает (размер, overlap, letterbox).  
Слепой stop-on-first-match по целевому id тоже отвергнут (ранний выход ломает выбор и отладку).

**Целевой контракт 1B (земля):**

```
1. Полный скан дуги руки (один проход, без early-exit на первой земле)
2. Инвентарь: [{instanceId, screenX, screenY}, …] — плато hover id → середина сегмента
3. Фильтр земель по legal Play ids (из GRE / PlayLandIntent)
4. Decide/picker (stub): первая земля слева направо по X
   (позже — учёт цвета / маны; picker не двигает мышь)
5. Actuate: курсор на координаты → mouse down → drag вверх → mouse up
6. Успех = GRE-ack (id ∉ hand), не факт жеста
```

| # | Работа |
|---|--------|
| 1.6 | `HandInventoryScanner` — полный проход P1→P2, `WaitForAny` hover, без stop на match |
| 1.7 | `LandPlayPicker.PickFirst` — stub; API готов под цвет позже |
| 1.8 | Жест PlayLand: click+drag up (не double-click; не «клик = успех») |
| 1.9 | Бюджет: **один** полный проход скана + жест; timeout = estimate(arc)+slack (не резать дугу на 4 s) |
| 1.10 | Не стартовать hit, пока options/modal overlay открыт |
| 1.11 | Калибровка: window rect / design size + `land_drag_up` в design px |
| 1.12 | Debug при miss: inventory ids/coords, target set, endpoints |
| 1.13 | Live: после drag ждать GRE-ack (`HandActionAck`) без nested channel-read (рефактор loop) |

Cast (шаг 2) пока может оставаться на stop-on-target double-click; землю **не** рефакторить «заодно» с кастом.

**Режим:** `LandOnly` — после земли всегда Pass до конца хода (каст/атака выключены).

**Приёмка шага 1 (обязательная):**

1. Keep с catch-up — без регресса.  
2. Ход 1–N: при наличии Play — земля на столе **до** конца Main1; нет скана на Beginning.  
3. Нет «вечной тяги» руки (бюджет соблюдён; один проход дуги).  
4. **5 матчей подряд** starter (или эквивалент replay+live) с land success ≥ порога (цель: 100% на чистых Main1 с одной землёй в hand).  
5. Регресс: Keep/Pass по кнопкам как раньше.

**Стоп-критерий:** не начинать шаг 2, пока приёмка 1 красная.

**Статус (2026-07):** слой **1A** — в коде. Новый **1B hit** в коде: `HandInventoryScanner` → `LandPlayPicker.PickFirst` → drag up (`MouseDown`/`MouseUp`), бюджет 4 s, `WaitForAny`. Live outcome пока `UiSucceeded` (п. 1.13 GRE-ack в loop — следующий инкремент). `--land-only` live не считать готовым до приёмки 5 матчей.

---

### Шаг 2 — Стабильный каст существа

**Цель:** после земли (или skip) один безопасный перманент при наличии legal Cast.

| # | Работа | Ограничение «не ломать землю» |
|---|--------|-------------------------------|
| 2.1 | Включить `LandAndCast`: Cast только если land done/skipped | порядок в `FarmMvpPolicy` не менять |
| 2.2 | Отдельный клик-профиль: double-click / cast path | PlayLand путь не рефакторить «заодно» без зелёных тестов земли |
| 2.3 | Тот же GRE-ack: id ∉ hand / Cast пропал из legal | общий helper из 1.4 |
| 2.4 | Бюджет каста отдельный; fail → Pass, не новый бесконечный скан | |
| 2.5 | Не кастовать вне Main1/Main2; sticky Cast на Beginning → Pass | гейт шага 0/1 |

**Приёмка шага 2:**

1. Все пункты приёмки шага 1 — зелёные.  
2. Ход с маной: одно безопасное существо; без каста на Beginning.  
3. 5 матчей: land+cast без вечной тяги.  
4. Метрики раздельно: `% land ok`, `% cast ok`.

---

### Шаг 3 — Атака

**Цель:** на `DeclareAttackers` стабильный Attack All (кнопка), без hand scan.

| # | Работа |
|---|--------|
| 3.1 | Intent только на Attackers prompt; фиксированная калибровка `AttackAll` |
| 3.2 | Успех = submit attackers / смена step (GRE), не «два клика без ошибки» |
| 3.3 | Режим `FullMvp` или `LandCastAttack` |

**Приёмка шага 3:** приёмка 1+2 зелёные; атака не открывает скан руки; 5 матчей без зависания на combat.

---

### Шаг 4 — Pass / Main2 / хвост хода

| # | Работа |
|---|--------|
| 4.1 | Main2: по умолчанию Pass (MVP) |
| 4.2 | Pass по калиброванной кнопке + подтверждение ухода приоритета |
| 4.3 | Сброс флагов хода (`landPlayed`, attempts) на смене turn number |

**Приёмка:** полный круг хода без sticky-сканов; rope timeout = 0 на 5 матчах.

---

### Шаг 5 — Усиление скана (только если 1B/2 ещё хрупкие)

Делать **после** зелёной земли на inventory-пути, если нужны руки >7 / сильный overlap / цветной picker:

| # | Работа |
|---|--------|
| 5.1 | Событийный wait hover (новая строка), не фиксированные 40 ms на клетку |
| 5.2 | Грубый проход → refine около кандидата (плато id) |
| 5.3 | Picker по цвету / мане вместо `PickFirst` |
| 5.4 | Метрики miss rate / mean scan ms / inventory size |

Не открывать шаг 5 «на всякий случай», если приёмка 1–2 уже зелёная.

---

## 3. Что сознательно вне плана (пока)

- Полный AI / оценка линий / mulligan-эвристики сверх Keep.  
- Блокирование, таргеты, PayCosts, GroupReq, casting options — отдельные эпики после шага 4.  
- CV / memory-reading координат.  
- Меню / очередь матчей / farm loop вне матча.

---

## 4. Режимы и защита от регресса

| Режим | Разрешено | Для чего |
|-------|-----------|----------|
| `LandOnly` | Keep, PlayLand, Pass | шаг 1 |
| `LandAndCast` | + CastSafe | шаг 2 |
| `FullMvp` | + AttackAll | шаг 3–4 |

CI / ручной чеклист перед мержем:

```
[ ] Приёмка шага 1 (земля)
[ ] Если меняли Actuate/Hover — отдельно land-only live
[ ] Новые тесты: гейт фазы, PlayLand vs Cast Intent, ack helper
[ ] Не повышать default scan budget без метрики
```

Юнит-тесты (дёшево, каждый шаг):

- `DecisionGate` / policy: Beginning → не PlayLand.  
- Policy: при legal Play → PlayLand, не Cast.  
- IntentExecutor: PlayLand → single-click path (mock).  
- Ack helper: фикстуры лога «до/после земли».

---

## 5. Порядок работ (сводка)

```
Шаг 0  Каркас Intent / re-check / режимы / лог attempt
   │
   ▼
Шаг 1  Земля: A-гейты + inventory-скан + PickFirst + drag + GRE-ack  ──► СТОП пока 5 матчей ok
   │
   ▼
Шаг 2  Каст (не ломая land path)
   │
   ▼
Шаг 3  Attack All
   │
   ▼
Шаг 4  Pass / сброс хода / полный круг
   │
   ▼
Шаг 5  (опционально) усиление скана
```

Критерий «базовые ходы стабильны» (итог): **5 подряд матчей starter без rope и без вечной тяги руки** — land → cast → attack → pass.

---

## 6. Связь с кодом (куда класть изменения)

| Слой | Файлы (ориентир) |
|------|------------------|
| A — FSM / гейты | `PromptTracker`, `DecisionGate`, `FarmMvpPolicy`, `Intent.cs` |
| A — валидация | `IntentValidator`, pre-flight в `LiveExecuteRunner` / `IntentExecutor` |
| B — земля | `HandInventoryScanner`, `LandPlayPicker`, drag в `IntentExecutor` / `SendInputBackend` |
| B — каст (шаг 2) | `HoverResolver` stop-on-target + double-click (пока отдельно от земли) |
| Hover signal | `LogHoverObjectIdSource` (`WaitFor` / `WaitForAny`) |
| Ack | `HandActionAck` + wiring в live после жеста |
| Режимы | CLI `actuate live` + policy options |
| Калибровка | `CalibrationProfile`, `hand_scan_points`, `land_drag_up` |

Детали багов и чеклист причин сбоев — в [`hand-select-and-cast.md`](hand-select-and-cast.md) §4–7; этот план задаёт **порядок**, а не дублирует весь разбор.
