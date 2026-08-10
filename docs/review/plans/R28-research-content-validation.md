# R28 — Валидация research-контента: опасные значения

**Severity:** High (content correctness) · **Домен:** research / validation · **Roadmap:** P0
**Статус валидации:** ✅ Подтверждено по коду

## Проблема
Валидатор требует наличие science packs, но не проверяет `Amount > 0` и дубликаты item-записей; допускает отрицательные default allocations; не бросает на неизвестном `TargetTierId`. Отрицательная стоимость приводит к тому, что `Inventory.TryRemove(item, negative)` **увеличивает** количество item. Дубликаты packs проходят `CanAfford` по отдельности, но могут частично списаться и завершиться `false`. Неизвестный tier после completion пишется в authoritative state.

## Где в коде
- `src/SteelConveyorWar.Core/Research/ResearchContentValidator.cs:34-37` — проверяет только `SciencePacks.Count != 0`, без `Amount > 0` и дубликатов.
- `src/SteelConveyorWar.Core/Research/ResearchContentValidator.cs:53-58` — profile budget: только сумма == Scale; отрицательные default allocations проходят.
- `src/SteelConveyorWar.Core/Research/ResearchContentValidator.cs:75-79` — ветка неизвестного `gate.TargetTierId` пустая (не бросает).
- `src/SteelConveyorWar.Core/Logistics/Inventory.cs:69-87` — `TryRemove` с отрицательным amount: `Has` возвращает true, `remaining = Count - (negative)` → рост количества.
- `src/SteelConveyorWar.Core/Research/ResearchSystem.cs:429-450` — `TryConsume` списывает по одному pack; при дубликатах возможно частичное списание.
- completion неизвестного tier: `ResearchSystem.cs:647-660`.

## План фикса
1. Валидатор: для каждого science pack требовать `Amount > 0`; запрещать дубликаты item-записей в `SciencePacks`.
2. Валидатор: запрещать отрицательные значения в `DefaultAllocations` (каждое `>= 0`), не только сумму.
3. Валидатор: неизвестный `TargetTierId` (кроме явного `tier.t3-boundary`/null) → бросать ошибку, а не молчать.
4. `Inventory.TryRemove`/`Add`: жёстко отклонять отрицательный amount на входе (guard), чтобы данные не могли «нарастить» инвентарь.
5. `TryConsume`: делать атомарно — проверить весь набор, затем списать (или откатить при неудаче), исключив частичное списание при дубликатах.
6. Fail-fast: неизвестный tier не должен попадать в authoritative state.

## Тесты
- Негативные config-тесты: pack `Amount <= 0`, дубликат pack, отрицательная allocation, неизвестный TargetTier → загрузка отклоняется.
- Тест: `Inventory.TryRemove(item, -5)` не увеличивает количество.
- Тест: дубликатные packs не приводят к частичному списанию.

## Definition of Done
- Все перечисленные опасные значения отклоняются на этапе валидации/загрузки.
- Инвентарь не мутируется отрицательными amount.

## Связанные замечания
R29 (research allocation), R9 (robustness), R34 (cross-catalog validation).
