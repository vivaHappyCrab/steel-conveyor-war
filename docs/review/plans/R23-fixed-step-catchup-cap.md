# R23 — Ограничить catch-up в fixed-step цикле

**Severity:** Low · **Домен:** SFML loop / robustness · **Roadmap:** P2
**Статус валидации:** ✅ Подтверждено по коду

## Проблема
`while (accumulator >= fixedDelta)` не ограничивает число тиков за кадр. После длинной паузы (свернутое окно, точка останова) возможна spiral-of-death: симуляция пытается «догнать» огромное число тиков за один кадр. Нужна документированная catch-up/drop политика, особенно перед network host.

## Где в коде
- `src/SteelConveyorWar.Sfml/SfmlPlaySession.cs:872-925` — цикл `while (accumulator >= fixedDelta)` без max ticks/frame.

## План фикса
1. Ввести `MaxTicksPerFrame` (например 5–10). После достижения — прекратить catch-up в этом кадре.
2. Политика для остатка: либо «drop» лишнего accumulator (clamp), либо задокументированное отставание. Зафиксировать выбор в комментарии/доке.
3. Для будущего network host — согласовать с lockstep-темпом (drop недопустим в lockstep; там нужен stall/resync).

## Тесты
- Тест: искусственно большой `deltaTime` не приводит к многотысячному числу тиков за кадр (ограничено `MaxTicksPerFrame`).

## Definition of Done
- Число тиков за кадр ограничено; политика остатка задокументирована.

## Связанные замечания
R32 (TPS/pacing), R2 (network path).

---

## Статус реализации: ✅ Реализовано 2026-08-11

⚠️ **Требуется прогон `dotnet build -c Release` + `dotnet test`** — VM был недоступен, код не компилировался и тесты не запускались. Изменения ниже нужно верифицировать сборкой и прогоном тестов.

### Что сделано
- **Новый тестируемый `FixedStepPacer`** (`Sfml/FixedStepPacer.cs`): `const int MaxTicksPerFrame = 8` и чистый метод `Plan(accumulator, fixedDelta) → (int Ticks, float RemainingAccumulator)`. Логика: гоняем шаги, пока `accumulator >= fixedDelta` и `ticks < MaxTicksPerFrame`; **политика остатка** — обычный кадр несёт суб-шаговый остаток для плавности, но при достижении лимита с непогашенным долгом лишнее **сбрасывается** (`accumulator %= fixedDelta`), чтобы долг не рос неограниченно. Политика задокументирована в xml-doc: drop приемлем для локального одиночного хоста; будущий lockstep network host **не** должен дропать — только stall/resync.
- **Цикл fixed-step переведён на пейсер** (`Sfml/SfmlPlaySession.cs`): вместо `while (accumulator >= fixedDelta) { … accumulator -= fixedDelta; }` теперь `Plan(...)` считает число тиков и новый accumulator, а тело гоняется `for (tickIndex < ticksThisFrame)`. Реальная логика ограничения = ровно та, что покрыта юнит-тестами (нет расхождения helper↔loop).

### Тесты
- `tests/SteelConveyorWar.Sfml.Tests/FixedStepPacerTests.cs`: обычный кадр (2.5 шага → 2 тика, половина шага в остатке); огромная дельта (10 c при 30 TPS = 300 тиков без кэпа) ограничивается `MaxTicksPerFrame`, остаток < шага (backlog сброшен); недостаточный backlog → 0 тиков, accumulator сохранён; `fixedDelta <= 0` → `ArgumentOutOfRangeException`.

### Отклонения / заметки
- Сам игровой цикл (`Run`) требует SFML-окна и не юнит-тестируется напрямую; поэтому ограничивающая логика вынесена в чистый `FixedStepPacer.Plan`, который и используется в цикле — тест покрывает именно её.
- `MaxTicksPerFrame = 8` (в диапазоне 5–10 из плана): при 30 TPS это до ~0.27 c симуляции на кадр до срабатывания drop.
