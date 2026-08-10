# R22 — Performance regression gate

**Severity:** Medium · **Домен:** performance / CI · **Roadmap:** P1
**Статус валидации:** ✅ Подтверждено по коду

## Проблема
CI собирает coverage и запускает multi-OS build/tests/smoke (сильная сторона), но нет benchmark-проекта, порогов на tick time/allocations, large-world/large-army сценариев, coverage threshold и property-based тестов сериализатора для всех kinds. Из-за этого все performance-выводы (R7/R15/R16/R17/R33) остаются статическими.

## Где в коде
- `.github/workflows/ci.yml` — build/test/coverage/multi-OS/smoke; benchmark и порогов нет.
- В репозитории отсутствует benchmark-проект (нет BenchmarkDotNet).

## План фикса
1. Создать проект `SteelConveyorWar.Benchmarks` (BenchmarkDotNet) со сценариями из матрицы ревью (idle factory, belts, army move, battle, FoW, power).
2. Добавить измерение tick p50/p95 и alloc/tick; сериализовать результаты.
3. Ввести пороги (threshold) и регрессионный gate: падение при превышении бюджета времени/аллокаций.
4. Добавить property-based тесты сериализатора для всех `SimulationCommandKind` (см. R11).
5. Опционально: coverage threshold в CI.

## Тесты / метрики
- Benchmark-набор запускается локально и (хотя бы вручную/nightly) в CI.
- Property-тесты round-trip по всем командам зелёные.

## Definition of Done
- Есть benchmark-проект с матрицей сценариев и порогами.
- Регрессия производительности детектируется автоматически.

## Связанные замечания
R7, R15, R16, R17, R33 (все требуют benchmark для оценки severity), R11.
