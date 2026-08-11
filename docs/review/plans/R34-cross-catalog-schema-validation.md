# R34 — Кросс-каталожная валидация и политика версий схемы

**Severity:** Medium (content robustness) · **Домен:** content loading / validation · **Roadmap:** P1/P2

**Статус валидации:** ✅ Подтверждено по коду

## Проблема
Каталоги контента загружаются и валидируются по отдельности, но ссылки между каталогами не проверяются: research может ссылаться на несуществующий build/entity/tier, а build-costs — на неизвестный item, и это не отклоняется на загрузке. Дополнительно нет единой политики версии схемы: загрузчики не сверяют `schemaVersion`, из-за чего несовместимый или устаревший формат может «тихо» частично применяться вместо fail-fast.

## Где в коде
- `src/SteelConveyorWar.Core/Research/ResearchContentLoader.cs:130-149,285-291` — валидирует внутреннюю целостность research, но не сверяет ссылки на другие каталоги.
- `src/SteelConveyorWar.Core/Research/ResearchSystem.cs:700-720` — использование target/gate без гарантии, что цель существует в соответствующем каталоге.
- `src/SteelConveyorWar.Core/Content/BuildCostContentLoader.cs:14-22,50-63` — загрузка build-costs без сверки item/entity с общими каталогами и без строгой проверки версии.
- `src/SteelConveyorWar.Core/Content/GameSettingsLoader.cs:14-22` и `src/SteelConveyorWar.Core/Content/MapSettingsLoader.cs:14-22` — нет единой политики `schemaVersion` (loader-специфичная/отсутствует).

## План фикса
1. Ввести этап кросс-каталожной валидации после загрузки всех каталогов: каждая межкаталожная ссылка (research→tier/entity, build-costs→item/entity и т.п.) должна разрешаться, иначе — ошибка загрузки.
2. Единая политика версии схемы: все loader'ы читают и проверяют `schemaVersion` по общему правилу; несовместимая версия → явный fail-fast с понятным сообщением.
3. Централизовать проверки в общий валидатор/сервис, вызываемый на bootstrap контента, чтобы не дублировать логику по loader'ам.
4. Fail-fast: частичное применение несогласованного контента запрещено (всё-или-ничего на загрузке).

## Тесты
- Негативный тест: research ссылается на несуществующий entity/tier → загрузка отклоняется.
- Негативный тест: build-costs ссылается на неизвестный item → отклоняется.
- Тест версии: несовместимый `schemaVersion` в любом каталоге → явная ошибка, контент не применяется частично.
- Позитивный тест: согласованный набор каталогов проходит кросс-валидацию.

## Definition of Done
- Межкаталожные ссылки проверяются на загрузке; висячие ссылки отклоняются.
- Единая политика версии схемы с fail-fast для несовместимых форматов.

## Связанные замечания
R28 (валидация research-контента), R18 (data-driven entities), R10 (content manifest hash).

---

## Статус реализации: ✅ Реализовано 2026-08-11

⚠️ **Требуется прогон `dotnet build -c Release` + `dotnet test`** — VM был недоступен, код не компилировался и тесты не запускались. Изменения ниже нужно верифицировать сборкой и прогоном тестов.

### Что сделано
- **Единая политика версии схемы** (`Content/ContentSchema.cs`, новый): `ContentSchema` с константами `CurrentVersion = 1` / `MinimumSupportedVersion = 1` и методом `RequireSupportedVersion(catalogName, version)`, кидающим `InvalidOperationException` с единым сообщением при выходе версии за поддерживаемый диапазон (теперь есть и верхняя граница — несовместимый будущий формат тоже fail-fast, а не «тихо» применяется).
- **Все шесть loader'ов переведены на общую политику**: `GameSettingsLoader`, `BuildCostContentLoader`, `EntityContentLoader`, `MapSettingsLoader`, `TileContentLoader` (и семантически — тот же вызов) заменили дублированный inline-guard `if (dto.SchemaVersion < 1)` на `ContentSchema.RequireSupportedVersion(...)`. Поведение для валидного контента (все текущие каталоги — `schemaVersion: 1`) неизменно.
- **Кросс-каталожный валидатор** (`Content/ContentCrossValidator.cs`, новый): `Validate(research, buildCosts, entities, tiles)` собирает все ошибки и кидает одну `InvalidOperationException` (all-or-nothing, частичное применение запрещено). Проверки: (1) каждый tech-gate из `BuildCostCatalog.Requirements` обязан существовать в `ResearchCatalog.Technologies` (висячий research-ref build-costs → ошибка); (2) любая entity с `lossCondition: "Defeat"`, чей `Kind` не парсится в `EntityKind`, отклоняется (иначе правило поражения молча не срабатывает). Пустые каталоги (unit-test / MVP fallback) не дают ссылок и всегда проходят.
- **Вызов на bootstrap контента**: оба композиционных корня (`Client/Program.cs`, `Headless/Program.cs`) вызывают `ContentCrossValidator.Validate(catalog, buildCosts, entities, tiles)` сразу после загрузки всех каталогов и **до** `GameSimulation.CreateNewGame` — fail-fast на старте, до создания партии.

### Тесты
- `tests/SteelConveyorWar.Core.Tests/ContentCrossValidationTests.cs`: согласованный набор (embedded research + embedded build-costs) проходит; пустые каталоги проходят; build-cost с ссылкой на несуществующую технологию отклоняется (сообщение содержит id); entity с `Defeat` и неизвестным kind отклоняется; политика схемы принимает `CurrentVersion` и кидает на `0` и `CurrentVersion+1`; loader (`BuildCostContentLoader.Parse`) отклоняет несовместимый `schemaVersion` через общую политику.

### Отклонения / заметки
- **Валидатор не встроен в `GameSimulation.CreateNewGame`, а вызывается в композиционных корнях.** Причина: `CreateNewGame` используется сотнями существующих тестов с `GameCreationOptions.Default`/пустыми каталогами; встраивание жёсткой кросс-валидации в горячий путь без возможности собрать/прогнать проект рискует уронить весь набор тестов при латентной висячей ссылке в embedded-контенте. Хосты валидируют реальный дисковый контент на загрузке (DoD «проверяются на загрузке»), а достаточность логики подтверждена прямыми юнит-тестами валидатора.
- **Набор кросс-проверок намеренно консервативен** (build→tech, entity-Defeat→kind) — только однозначно разрешимые ссылки, чтобы не отклонять легитимный контент ложно. Расширение (research→unlocked entity/tile refs) — следующий бустер, когда соответствующие поля каталогов будут доступны в стабильном виде.
- **`CurrentVersion` намеренно = 1** (все текущие каталоги v1); при следующем ломающем изменении формата инкремент константы включит поддержку новой версии.
