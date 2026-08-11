# R24 — Дружелюбная обработка невалидного local player

**Severity:** Low · **Домен:** SFML / UX · **Roadmap:** P2
**Статус валидации:** ✅ Подтверждено по коду

## Проблема
CLI проверяет положительный id, но session предполагает наличие commander и вызывает `First`. `--local-player 3` для default map (где есть только P1/P2) завершится исключением без domain-friendly ошибки.

## Где в коде
- `src/SteelConveyorWar.Sfml/SfmlPlaySession.cs:38-40` — предположение о наличии commander + `First`.

## План фикса
1. Проверять, что запрошенный `local-player` присутствует в roster карты, до запуска сессии.
2. При отсутствии — выбрасывать/возвращать понятную доменную ошибку («Player N not present on this map; available: …»), а не `InvalidOperationException` из `First`.
3. Заменить `First` на `FirstOrDefault` + явную проверку/сообщение.

## Тесты
- Тест: `--local-player 3` на 2-игроковой карте → понятная ошибка, без необработанного исключения.
- Тест: валидный id запускается нормально.

## Definition of Done
- Невалидный local player даёт дружелюбное сообщение, а не сырое исключение.

## Связанные замечания
R31 (roster/seat), R25 (host bootstrap).

---

## Статус реализации: ✅ Реализовано 2026-08-11

⚠️ **Требуется прогон `dotnet build -c Release` + `dotnet test`** — VM был недоступен, код не компилировался и тесты не запускались. Изменения ниже нужно верифицировать сборкой и прогоном тестов.

### Что сделано
- **Новый тестируемый валидатор `LocalPlayerBinding.EnsureSeatControllable(GameSimulation, PlayerId)`** (`Sfml/LocalPlayerBinding.cs`): проверяет, что запрошенный сид присутствует в `simulation.Players`; при отсутствии кидает доменную ошибку `"Local player N is not present on this map. Available player ids: …"` (перечисляет доступные id). Дополнительно проверяет наличие командира у сида — иначе `"Local player N has no commander on this map and cannot be controlled."`. Выделено в статический метод, чтобы покрыть тестами без открытия SFML-окна.
- **Вызов до предположения о наличии командира** (`Sfml/SfmlPlaySession.cs`): `EnsureSeatControllable(simulation, localPlayer)` вызывается сразу после определения `localPlayer` и **до** строки `simulation.World.Entities.First(...)`, так что `--local-player 3` на 2-игроковой карте даёт понятное сообщение, а не сырое `InvalidOperationException` из `First`.

### Тесты
- `tests/SteelConveyorWar.Sfml.Tests/LocalPlayerBindingTests.cs`: `EnsureSeatControllable_ValidSeat_DoesNotThrow` (сиды 1 и 2 default-карты проходят); `EnsureSeatControllable_SeatNotOnMap_ThrowsFriendlyMessage` (сид 3 → сообщение содержит «Local player 3» и «Available player ids»).

### Отклонения / заметки
- `First` в `SfmlPlaySession` намеренно оставлен как есть — после `EnsureSeatControllable` он гарантированно находит командира, а замена на `FirstOrDefault` создала бы мёртвую ветку (нулевая проверка недостижима), что при `TreatWarningsAsErrors` только зашумляет. Дружелюбная ошибка выдаётся строго раньше, чем отработает `First`.
