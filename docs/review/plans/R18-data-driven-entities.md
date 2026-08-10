# R18 — Data-driven контент вместо enum/switch модели

**Severity:** Medium · **Домен:** extensibility / content · **Roadmap:** P2
**Статус валидации:** ✅ Подтверждено по коду

## Проблема
`MvpDefinitions` продолжает владеть unit/factory-наборами, power, stack sizes, footprints/collision, resistances, production recipes, combat stats. `entities.json` требует точного `EntityKind` enum. Добавление нового entity требует code + config + renderer/tests; plugin/mod-контент без rebuild невозможен. Data-driven subscore — 4.0/10.

## Где в коде
- `src/SteelConveyorWar.Core/Definitions/MvpDefinitions.cs:22-267` — таблицы наборов/power/stacks/footprints/resistances/recipes/combat.
- `src/SteelConveyorWar.Core/Content/EntityContentLoader.cs:42-50` — привязка к enum `EntityKind`.

## План фикса (поэтапно)
1. Ввести versioned content-каталоги для доменов, которые сейчас в `MvpDefinitions`: recipes, combat stats, stacks, power, footprints/collision, resistances.
2. Заменить `EntityKind`-enum как первичный ключ на строковый `EntityTypeId` (enum оставить как оптимизацию/legacy-совместимость на переходный период).
3. Загрузчик строит каталог из JSON; `MvpDefinitions` остаётся только default/fallback для тестов.
4. Renderer/UI получают внешний вид из каталога (см. R30), а не из хардкода.
5. Включить идентичность новых каталогов в manifest hash (R10) и cross-catalog валидацию (R34).

## Тесты
- Тест: добавление нового entity только через JSON (без изменения enum) работает в headless.
- Тест: несогласованный контент отклоняется валидатором.

## Definition of Done
- Ключевые gameplay-таблицы вынесены в versioned каталоги.
- Новый контент добавляется без правки enum/rebuild ядра (в рамках выбранного этапа).

## Связанные замечания
R10 (manifest), R30 (build menu из каталога), R34 (schema/cross-catalog).
