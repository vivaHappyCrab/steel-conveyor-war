# R20 — Fixed-point координаты для кросс-платформенного lockstep

**Severity:** Medium (accepted risk) · **Домен:** determinism / networking · **Roadmap:** P2
**Статус валидации:** ✅ Подтверждено по коду

## Проблема
`WorldPosition` и нормализация движения используют `double` + `Sqrt`. ADR 0001 корректно фиксирует ограничение (lockstep в пределах одного runtime/ABI), но документирование не устраняет риск cross-platform desync при heterogeneous lockstep.

## Где в коде
- `src/SteelConveyorWar.Core/Domain/ValueObjects.cs:39-66` — `WorldPosition` на `double`.
- `src/SteelConveyorWar.Core/Systems/MovementSystem.cs:121-149` — нормализация через `Sqrt`.
- ADR 0001 (в `docs/`) — зафиксированное ограничение.

## План фикса (крупный, отдельный этап перед heterogeneous lockstep)
1. Ввести fixed-point тип координат (например Q32.32 или целочисленные суб-тайлы) для authoritative-позиций.
2. Заменить нормализацию/расстояния на целочисленные/fixed-point реализации (integer sqrt / предвычисленные направления).
3. Пройтись по всем местам, где авторитетная логика зависит от `double`, и перевести на fixed-point; presentation может оставаться в `double`.
4. Обновить hasher (позиции уже хэшируются как IEEE-биты — перейти на fixed-point-биты) и поднять `AlgorithmVersion`.
5. Обновить ADR 0001: снятие ограничения после миграции.

## Тесты
- Кросс-платформенный дифф-тест (несколько ОС/рантаймов) → идентичный state hash на длинном прогоне.
- Behavior-regression на игровых сценариях (движение/бой).

## Definition of Done
- Authoritative-координаты не зависят от FP; heterogeneous lockstep детерминирован.

## Связанные замечания
R8/R10 (hash surface), R7 (movement).
