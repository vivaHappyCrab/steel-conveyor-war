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
