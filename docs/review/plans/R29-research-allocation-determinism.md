# R29 — Research: сохранить identity пакета и честное распределение

**Severity:** High (gameplay correctness) · **Домен:** research · **Roadmap:** P0
**Статус валидации:** ✅ Подтверждено по коду

## Проблема
`TryConsumePackForAnyActiveProject` возвращает только `bool`, после чего Core увеличивает общий `packConsumptions`; identity проекта, под который списан pack, теряется. Затем generic work распределяется между всеми активными проектами — pack одного типа может продвинуть другой project. Дополнительно при `packConsumptions = 1` доли всех tracks кроме последнего округляются вниз до нуля, а весь остаток получает последний track; тот же last-entry bias — для weighted projects. Пример: allocation 70/30 при одном work unit каждый цикл превращается в 0/100. UI хранит настройку, но фактический прогресс её игнорирует.

## Где в коде
- `src/SteelConveyorWar.Core/Research/ResearchSystem.cs:413-427` — `TryConsumePackForAnyActiveProject` возвращает `bool`, теряя identity проекта.
- `src/SteelConveyorWar.Core/Research/ResearchSystem.cs:452-480` — `DistributeWork`: не-последний track `share = packConsumptions * AllocationBasisPoints / AllocationScale` (при 1 → 0), последний получает `packConsumptions - allocated`.
- `src/SteelConveyorWar.Core/Research/ResearchSystem.cs:499-523` — тот же last-entry bias для weighted projects.

## План фикса
1. `TryConsume*` должен возвращать identity проекта/трека, под который списан pack (например `TechnologyId?`/`(TrackId, TechnologyId)`), а не `bool`. Начислять work именно этому проекту.
2. Для generic-распределения ввести детерминированный accumulator остатка (largest-remainder / rotation между циклами): дробные доли накапливаются и периодически дают целую единицу нужному треку, вместо систематического сдвига к последнему.
3. Применить тот же largest-remainder к weighted projects.
4. Убедиться в детерминизме (стабильный порядок, целочисленная арифметика).

## Тесты
- Тест: смешанные типы packs — pack продвигает именно свой проект, а не чужой.
- Тест долгосрочный: allocation 70/30 за много циклов даёт ≈70/30, а не 0/100.
- Тест weighted distribution: доли соответствуют весам в пределах остатка.
- Регрессия детерминизма dual-run.

## Definition of Done
- Identity пакета сохраняется; долгосрочное распределение соответствует настройке.
- Нет систематического last-entry bias.

## Связанные замечания
R28 (валидация), R11 (research-intent'ы), R8 (hash треков/весов).
