# R14 — Закрыть публичные mutation/cheat-хуки

**Severity:** Medium · **Домен:** encapsulation / authority · **Roadmap:** P0
**Статус валидации:** ✅ Подтверждено по коду

## Проблема
Остались публичные API, позволяющие мутировать authoritative-состояние вне tick-оркестрации и без игрового источника/actor. Это противоречит цели gating cheat-хелперов и опасно для plugin/bot host.

## Где в коде
- `src/SteelConveyorWar.Core/GameSimulation.cs:1028-1039` — `TryAddOutputItemToEntity` публично добавляет item без источника/actor.
- `src/SteelConveyorWar.Core/Systems/PowerSystem.cs:26-53` — `TryConsumeBuildingEnergy` публично меняет energy/stat accounting через переданную live entity.
- `src/SteelConveyorWar.Core/Research/ResearchSystem.cs:3-21,222-228` — публичный `ResearchSystem` принимает live authoritative research/simulation и может выполнять мутацию вне `AdvanceTick`.

## План фикса
1. Пометить `TryAddOutputItemToEntity` как `internal` (или спрятать за `TestOnly`-фасадом, доступным только тестам через `InternalsVisibleTo`).
2. `TryConsumeBuildingEnergy` — сделать `internal`, вызывать только из PowerSystem tick.
3. `ResearchSystem` — не давать публично мутировать: методы мутации `internal`, публичным оставить только read-only snapshot-доступ. Мутации — только внутри tick.
4. Проверить остальные `public Try*`, которые не являются частью command-модели, и закрыть их.

## Тесты
- Тест компиляции/архитектуры: перечисленные mutation-API недоступны из Sfml/Headless сборок.
- Регрессия: тик и тесты ядра работают через оставшийся внутренний доступ.

## Definition of Done
- Ни один cheat/mutation-хук не доступен публично за пределами тестов.

## Связанные замечания
R1, R5, R2 (единый sink как единственный вход мутаций).
