# R16 — FoW и tech signatures: dirty-регионы вместо full-tick

**Severity:** Medium (performance) · **Домен:** performance · **Roadmap:** P1/P2
**Статус валидации:** ✅ Подтверждено по коду

## Проблема
Dirty-list decay уже сделан, но каждый vision source каждый tick заново «красит» весь круг видимости; tech signatures каждый tick строят LINQ/GroupBy pipeline для каждого игрока. При росте числа источников зрения это лишняя работа/аллокации на каждый тик.

## Где в коде
- `src/SteelConveyorWar.Core/Systems/FogOfWarSystem.cs:17-81` — перерисовка всего круга каждый tick.
- `src/SteelConveyorWar.Core/Systems/FogOfWarSystem.cs:84-105` — LINQ/GroupBy tech signatures на игрока каждый tick.

## План фикса
1. Отслеживать dirty sources/regions: пересчитывать видимость только при изменении (move/build/death/research), а не каждый тик.
2. Кэшировать «круг» видимости для неизменного радиуса/позиции; при перемещении инвалидировать только затронутые тайлы.
3. Tech signatures: заменить LINQ/GroupBy pipeline на инкрементальный агрегат, обновляемый по событиям, с переиспользуемыми буферами.
4. Сохранить детерминизм (порядок обхода/тай-брейки не меняются).

## Тесты
- Behavior-preserving: тот же state hash (для FoW-состояния, входящего в hash) и те же visibility-результаты.
- Benchmark: 100/1000 vision sources — время FoW до/после.

## Definition of Done
- FoW/signatures обновляются инкрементально по событиям, без full-tick перерасчёта.
- Детерминизм сохранён, benchmark подтверждает выигрыш.

## Связанные замечания
R7, R17, R22, R33 (rendering FoW-hotspots).
