# R17 — Кэшировать Factory/Bastion accounting

**Severity:** Medium (performance) · **Домен:** performance · **Roadmap:** P1/P2
**Статус валидации:** ✅ Подтверждено по коду

## Проблема
Deficit/supply/in-flight расчёты выполняют вложенные `Count/Where/OrderBy` по `World.Entities` даже на idle-тике; bastion processing повторно собирает units/orders; repair pass делает вложенный поиск units для каждого bastion. При MVP-лимитах приемлемо, но плохо масштабируется с числом bastions/factories.

## Где в коде
- `src/SteelConveyorWar.Core/Systems/FactoryBastionSystem.cs:86-245` — вложенные `Count/Where/OrderBy` на тик.
- `src/SteelConveyorWar.Core/Systems/FactoryBastionSystem.cs:364-470` — повторный сбор units/orders.
- `src/SteelConveyorWar.Core/Systems/CombatSystem.cs:166-197` — вложенный поиск units на каждый bastion в repair pass.

## План фикса
1. Строить один раз за тик индексы: units по владельцу/bastion, orders, supply/in-flight — переиспользуемые буферы, а не повторные LINQ-сканы.
2. Использовать общий spatial-query service (R7) для «ближайших» запросов вместо полного скана.
3. Пропускать пересчёт для idle-фабрик (грязный флаг при изменении входов/назначений).
4. Убрать промежуточные аллокации `Where/OrderBy` на горячем пути.

## Тесты
- Behavior-preserving: тот же state hash и то же поведение назначений/ремонта.
- Benchmark: 100/500/2000 buildings — время и alloc/tick.

## Definition of Done
- Accounting не делает повторных полных сканов `World.Entities` на тик.
- Benchmark подтверждает масштабирование.

## Связанные замечания
R7 (spatial service), R16, R22.
