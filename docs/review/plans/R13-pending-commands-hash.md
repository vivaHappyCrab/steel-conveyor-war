# R13 — Учесть pending command state в детерминизме

**Severity:** Medium · **Домен:** determinism / replay · **Roadmap:** P0
**Статус валидации:** ✅ Подтверждено по коду

## Проблема
Hasher не пишет `_commandBuffer`. Два snapshot с одинаковым state и разными будущими командами дают одинаковый hash. Это допустимо только если command ledger хранится и сравнивается отдельно; такого production-ledger пока нет.

## Где в коде
- `src/SteelConveyorWar.Core/GameSimulation.Commands.cs:12-15` — `_commandBuffer` / `PendingCommands`.
- `src/SteelConveyorWar.Core/Determinism/SimulationStateHasher.cs:26-70` — `Compute` не включает pending-очередь.

## План фикса
Выбрать одну из стратегий и реализовать целиком:
1. **Ledger-подход (предпочтительно для сети):** ввести отдельный replayable command ledger (append-only, с sequence из R3), который хранится и сравнивается между peer'ами. State hash остаётся «чистым» состоянием, но ledger — обязательная часть session fingerprint.
2. **Hash-подход:** включить содержимое `_commandBuffer` в отдельный `PendingCommandsHash` (детерминированно отсортированный по `(Tick, Actor, Sequence)`), сравниваемый вместе со state hash.
3. Зафиксировать в документации, какой инвариант гарантируется (что именно сравнивается и когда).

## Тесты
- Тест: одинаковый state + разные pending commands → расхождение обнаруживается (через ledger или PendingCommandsHash).
- Тест: реплей ledger воспроизводит идентичный итоговый state hash.

## Definition of Done
- Будущие команды либо в hash, либо в сравниваемом ledger; инвариант задокументирован и покрыт тестом.

## Связанные замечания
R2 (command log), R3 (sequence), R8, R10.

---

## Статус реализации: ✅ Реализовано 2026-08-11 (стратегия 2 — Hash-подход)

⚠️ **Требуется прогон `dotnet build -c Release` + `dotnet test`** — VM был недоступен, код не компилировался и тесты не запускались. Изменения ниже нужно верифицировать сборкой и прогоном тестов.

### Что сделано
- **Отдельный `PendingCommandsHash`.** Добавлен `SimulationStateHasher.ComputePendingCommandsHash(GameSimulation)` и публичная обёртка `GameSimulation.ComputePendingCommandsHash()`. State hash остаётся «чистым» состоянием (не изменён), а pending-очередь фингерпринтится отдельно. Полный session fingerprint между peer'ами = пара `(ComputeStateHash, ComputePendingCommandsHash)`.
- **Детерминированный порядок.** Команды сортируются канонически по `(Tick, Actor, Sequence)` — тем же ключом, которым их применяет tick-loop (R03), — поэтому порядок enqueue / сетевого прихода не влияет на хеш.
- **Стабильная сериализация.** Каждая команда пишется через `SimulationCommandSerializer.Serialize` (версионированный wire-конверт, R11); в хеш также включены `AlgorithmVersion` и счётчик команд. Пустая очередь даёт стабильную константу (digest пустого payload'а), отличную от state hash.

### Инвариант (документируется здесь)
Сравнивается **пара** хешей: `ComputeStateHash` гарантирует идентичность authoritative-состояния «сейчас», `ComputePendingCommandsHash` — идентичность ещё не применённых будущих команд. Расхождение любой из компонент между peer'ами означает desync; state hash при этом намеренно не «загрязняется» очередью, чтобы golden-снимки состояния оставались независимы от буфера команд.

### Тесты
- `tests/SteelConveyorWar.Core.Tests/PendingCommandsHashTests.cs`: (1) одинаковый state + разные pending → state hash равны, pending hash различаются; (2) pending hash не зависит от порядка enqueue (обратный порядок → тот же хеш); (3) пустая очередь → стабильная константа длины 64, ≠ state hash; (4) реплей той же очереди (10 тиков) воспроизводит идентичный итоговый state hash, а до применения — идентичный pending hash.

### Отклонения / заметки
- Выбрана стратегия 2 (Hash-подход), а не отдельный production command-ledger (стратегия 1): она самодостаточна, не требует новой инфраструктуры хранения/сети и напрямую покрывает DoD «будущие команды в сравниваемом хеше». Полноценный append-only ledger остаётся открытым для сетевого слоя (R02/сеть).
- `AlgorithmVersion` state-хеша не менялся (surface `Compute` не тронут); pending-хеш версионируется той же константой, чтобы изменения формата команд/алгоритма выражались единым числом.
