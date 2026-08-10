# R09 — Устойчивость к malformed/untrusted командам

**Severity:** High · **Домен:** robustness / network safety · **Roadmap:** P0
**Статус валидации:** ✅ Подтверждено по коду

## Проблема
Malformed команда может исключением оборвать tick — для untrusted network input это denial-of-service. Несколько мест бросают на «плохих» данных, а применение команд не изолирует исключения.

## Где в коде
- `src/SteelConveyorWar.Core/Commands/SimulationCommandSerializer.cs:164-168` — принимает произвольный (в т.ч. отрицательный) actor id без roster-проверки.
- `src/SteelConveyorWar.Core/GameSimulation.cs:171-174` и `GameSimulation.Commands.cs:62-64` — research dispatch через `GetPlayer(...).Single(...)` (бросит на неизвестном игроке).
- `src/SteelConveyorWar.Core/Research/ResearchSystem.cs:138-169` — `TrySetTrackAllocation` проверяет наличие обязательных tracks, но затем идёт по лишним ключам и обращается к `research.Tracks[pair.Key]` → возможен `KeyNotFoundException`.
- `src/SteelConveyorWar.Core/GameSimulation.Commands.cs:82-111` — `ApplyQueuedCommandsForCurrentTick` вызывает `ApplyCommand` без try/catch; `ApplyCommand` (строка 78) бросает `NotSupportedException` для неизвестного `Kind`.

## План фикса
1. **Envelope/schema-валидация до enqueue**: проверять `Kind`, диапазоны, обязательные поля; отклонять некорректное с результатом-ошибкой, не бросая внутри tick loop.
2. **Roster check**: actor id должен существовать в списке игроков; иначе reject.
3. Заменить `.Single(...)` на безопасный `TryGetPlayer` с reject при отсутствии.
4. `TrySetTrackAllocation`: отклонять неизвестные allocation keys (не индексировать `Tracks[pair.Key]` без проверки).
5. Сделать применение команды **no-throw**: handler возвращает `CommandResult` (Ok/Rejected/Reason); `ApplyQueuedCommandsForCurrentTick` собирает результаты, но не падает.
6. Добавить fuzz/property-тесты на десериализацию и применение.

## Тесты
- Fuzz: случайные/битые байты в serializer → не бросает, возвращает reject.
- Тест: команда с несуществующим actor → reject, tick не прерывается.
- Тест: extra allocation key с корректной суммой → reject, без `KeyNotFoundException`.
- Тест: неизвестный `Kind` → reject, а не исключение из tick.

## Definition of Done
- Ни один путь применения команды не бросает наружу из tick loop.
- Есть fuzz/property-тесты и негативные unit-тесты.

## Связанные замечания
R1 (authorization), R3 (idempotency), R11 (протокол/schema/version), R28 (валидация research-контента).
