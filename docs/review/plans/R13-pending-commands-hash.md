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
