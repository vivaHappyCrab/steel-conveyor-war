# R28 — Валидация research-контента: опасные значения

**Severity:** High (content correctness) · **Домен:** research / validation · **Roadmap:** P0
**Статус валидации:** ✅ Подтверждено по коду
**Статус реализации:** ✅ Реализовано 2026-08-11 (`ResearchContentValidator.cs`, `Logistics/Inventory.cs`, `Research/ResearchSystem.cs`; тесты в `ResearchContentValidationTests.cs`). ⚠️ Требуется прогон `dotnet build -c Release` + `dotnet test` (VM недоступна, изменения не скомпилированы).

### Что сделано
- `ResearchContentValidator`: для каждого science pack требуется `Amount > 0`; дубликаты item-записей в `SciencePacks` отклоняются (`HashSet<ItemId> seenPackItems`).
- `ResearchContentValidator`: отрицательные значения в `DefaultAllocations` отклоняются (каждое `>= 0`) до проверки суммы == Scale.
- `ResearchContentValidator`: неизвестный `gate.TargetTierId` (кроме `tier.t3-boundary` / null) теперь бросает `InvalidOperationException`, а не молчит.
- `Inventory.TryRemove`: guard `if (amount < 0) return false;` перед `Has()` — отрицательный amount больше не «наращивает» инвентарь. (`Add` уже бросал `ArgumentOutOfRangeException` на отрицательном amount.)
- `ResearchSystem.CanAfford`/`TryConsume`: агрегируют дубликатные packs по `ItemId` (`AggregateCost`) и используют `Inventory.HasAll`/`TryRemoveAll` — списание атомарно, частичного списания при дубликатах нет.

### Тесты
- `SciencePack_NonPositiveAmount_IsRejected`, `SciencePack_DuplicateItem_IsRejected`, `Profile_NegativeDefaultAllocation_IsRejected`, `Gate_UnknownTargetTier_IsRejected` — мутируют сериализованный embedded-каталог (JsonNode) и проверяют, что `ResearchContentLoader.Parse` бросает.
- `Baseline_EmbeddedCatalog_Parses` — санити, немутированный каталог валиден.
- `Inventory_TryRemove_NegativeAmount_DoesNotIncreaseCount`, `Inventory_TryRemoveAll_IsAtomic_NoPartialConsumption`.

### Отклонения от плана
- «Частичное списание дубликатов» покрыто на уровне `Inventory.TryRemoveAll` (атомарность) + агрегация в `AggregateCost`, а не отдельным тестом на `TechnologyDefinition` (построение definition в тесте избыточно сложное).

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
