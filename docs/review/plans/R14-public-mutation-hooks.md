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

---

## Статус реализации: ✅ Реализовано 2026-08-11

⚠️ **Требуется прогон `dotnet build -c Release` + `dotnet test`** — VM был недоступен, код не компилировался и тесты не запускались. Изменения ниже нужно верифицировать сборкой и прогоном тестов.

### Что сделано
- **`GameSimulation.TryAddOutputItemToEntity` → `internal`** (`GameSimulation.cs`). Cheat/test-seam впрыска предметов в live-entity без источника/actor. Остаётся доступен `Core.Tests` через `InternalsVisibleTo`, закрыт для Sfml/Headless/plugin.
- **`GameSimulation.TryConsumeBuildingEnergy` → `internal`** (`GameSimulation.cs`). Форвардер к `PowerSystem` был `public` (неявно реализовывал internal-интерфейс `ISimulationSystemContext`). Т.к. неявная реализация требует `public`, добавлена **явная** реализация `bool ISimulationSystemContext.TryConsumeBuildingEnergy(...) => TryConsumeBuildingEnergy(...)`, а сам instance-метод понижен до `internal`. Внутренние bare-вызовы (`ProduceRawResources`/`ProcessBuildingWork`/`ProcessFactoryProduction`/lab) продолжают работать через `this.`.
- **`PowerSystem.TryConsumeBuildingEnergy` → `internal`** (`Systems/PowerSystem.cs`). Класс и так `internal`; понижение метода делает намерение явным.
- **`ResearchSystem` → `internal sealed class`** (`Research/ResearchSystem.cs`). Класс принимал и мутировал live authoritative research/simulation. Sfml/Headless его не используют (проверено grep'ом), `Core.Tests` использует только `LabCycleTicks`/`SelectByLargestRemainder` — доступ сохранён через IVT. Хранится только в приватном поле `GameSimulation._researchSystem`; вся research-мутация идёт через command-pipeline. Публичных сигнатур, возвращающих `ResearchSystem`, вне самого класса нет (проверено grep'ом), поэтому inconsistent-accessibility не возникает.

### Тесты
- `tests/SteelConveyorWar.Core.Tests/MutationHookVisibilityTests.cs` (reflection-архитектурный тест): `TryAddOutputItemToEntity` и `TryConsumeBuildingEnergy` отсутствуют в public-surface (`BindingFlags.Public` → null) и присутствуют как non-public; `ResearchSystem`/`PowerSystem`/`CombatSystem` не являются public-типами (`.IsPublic == false`). Тест падает при повторном промоушене любого хука в public.

### Отклонения / заметки
- **Legacy `public Try*` команды не трогались** (пункт плана 4): они входят в immediate-mutation путь для SFML/тестов (см. заголовок `GameSimulation.Commands.cs`) и являются частью command-модели, а не cheat-хуками; их понижение сломало бы Sfml-хост. Закрыты ровно три не-командных mutation-хука из раздела «Где в коде».
- Явная реализация интерфейса выбрана вместо возврата метода в `public`, чтобы одновременно удовлетворить internal-контракт `ISimulationSystemContext` (Combat/Production/FactoryBastion) и требование R14 «не публично».
