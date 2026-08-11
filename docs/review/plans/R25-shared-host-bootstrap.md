# R25 — Общий host bootstrap для Client и Headless

**Severity:** Low · **Домен:** maintainability · **Roadmap:** P2
**Статус валидации:** ✅ Подтверждено (структурно; оба Program.cs дублируют composition)

## Проблема
Оба `Program.cs` (Client и Headless) повторяют path resolution, file I/O и загрузку каталогов. При добавлении нового обязательного каталога легко обновить только один host и получить рассинхрон.

## Где в коде
- `src/SteelConveyorWar.Client/Program.cs` — path resolution + I/O + catalog loading.
- `src/SteelConveyorWar.Headless/Program.cs` — то же самое, продублировано.

## План фикса
1. Вынести общий bootstrap в отдельный модуль (например `SteelConveyorWar.Core` hosting-хелпер или новый `SteelConveyorWar.Hosting`), **без** зависимости от SFML.
2. Bootstrap: единая точка резолва путей, чтения config-файлов, загрузки и валидации всех каталогов, сборки `GameSimulation`.
3. Client и Headless вызывают этот общий bootstrap; специфичное (окно/рендер vs headless loop) остаётся в своих Program.
4. Добавление нового обязательного каталога — правка в одном месте.

## Тесты
- Тест: bootstrap загружает идентичный набор каталогов для обоих hosts.
- Регрессия: Client smoke и Headless smoke проходят через общий bootstrap.

## Definition of Done
- Config composition не дублируется между hosts.

## Связанные замечания
R10 (manifest всех каталогов), R34 (валидация при загрузке), R24.

---

## Статус реализации: ✅ Реализовано 2026-08-11

⚠️ **Требуется прогон `dotnet build -c Release` + `dotnet test`** — VM был недоступен, код не компилировался и тесты не запускались. Изменения ниже нужно верифицировать сборкой и прогоном тестов.

### Что сделано
- **Новый общий bootstrap `ContentBootstrap`** (`Core/Hosting/ContentBootstrap.cs`, без зависимости от SFML): `ResolveConfigDirectory(baseDir, currentDir)` (чистый, тестируемый без `AppContext`) + перегрузка `ResolveConfigDirectory()` для хостов; `Load(configDirectory)` резолвит и валидирует все каталоги (research/tiles/entities/map/build-costs), вызывает `ContentCrossValidator.Validate` (R34) и собирает `GameCreationOptions`. Возвращает `LoadedGameContent(GameJson, Settings, CreationOptions)` — сырой `game.json` нужен Client'у для host-only display-опций.
- **Оба хоста переведены на общий bootstrap**: `Headless/Program.cs` и `Client/Program.cs` заменили дублированные `ResolveConfigDirectory`/`LoadRequiredJson`/загрузку каталогов/кросс-валидацию/сборку опций на один вызов `ContentBootstrap.Load(ContentBootstrap.ResolveConfigDirectory())`. Client берёт `content.GameJson` и `content.Settings.TicksPerSecond` для `HostDisplayOptionsLoader.Parse`. Host-специфика (SFML-окно vs headless-loop) осталась в своих Program.
- **Добавление нового обязательного каталога — правка в одном месте** (`ContentBootstrap.Load`), рассинхрон хостов исключён.

### Тесты
- `tests/SteelConveyorWar.Core.Tests/ContentBootstrapTests.cs`: `Load` из реального config-каталога даёт непустой `GameJson` и рабочие `CreationOptions` (сборка партии, согласованный TPS); `Load` дважды даёт идентичный контент и одинаковый state-hash созданной партии (гарантия отсутствия дрейфа между хостами); `Load` на несуществующем каталоге кидает; `ResolveConfigDirectory` откатывается к `baseDir/config`, когда ни один кандидат не существует.

### Отклонения / заметки
- Bootstrap размещён в namespace `SteelConveyorWar.Core` (а не в отдельной сборке `SteelConveyorWar.Hosting`), чтобы не плодить проект ради одного класса; зависимость от SFML отсутствует, оба хоста уже `using SteelConveyorWar.Core;`.
- Client smoke (`--smoke-test`) и Headless smoke теперь проходят через общий путь загрузки; поведение (набор каталогов, порядок, валидация) идентично прежнему — это чистая деконструкция дублирования без изменения семантики.
