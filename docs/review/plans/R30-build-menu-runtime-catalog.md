# R30 — Build UI из authoritative build catalog

**Severity:** High (gameplay/content) · **Домен:** SFML UI / content · **Roadmap:** P0/P1
**Статус валидации:** ✅ Подтверждено по коду

## Проблема
Build bar считает affordability без `simulation.BuildCostCatalog`, поэтому модель использует embedded fallback; UI учитывает только commander inventory, тогда как Core может оплатить build из nearby hubs. Список buildable entities захардкожен и не включает настроенные `UndergroundConveyor`, `SteelWall`, `CannonTurret`, `AntiAirTurret`. UI расходится с рантайм-правилами.

## Где в коде
- `src/SteelConveyorWar.Sfml/Ui/BuildBarOverlay.cs:50-73` + `BuildBarModel.cs:50-58` — affordability без `BuildCostCatalog` (embedded fallback), учёт только commander inventory.
- `src/SteelConveyorWar.Sfml/Ui/BuildMenuCatalog.cs:5-27` — хардкод `BuildableKinds` (есть `Wall`, `MachineGunTurret`, но нет `UndergroundConveyor`/`SteelWall`/`CannonTurret`/`AntiAirTurret`).
- `config/build-costs.json:53-57,85-105` — настроенные entity, отсутствующие в меню.

## План фикса
1. Строить build menu/view-model из authoritative `BuildCostCatalog` (список строится из каталога, а не из хардкода) + capability snapshot (что доступно игроку по исследованиям).
2. Affordability считать одним запросом в Core, который учитывает те же источники оплаты (commander inventory + nearby hubs), что и реальный build.
3. Убрать `BuildMenuCatalog` хардкод или свести его к порядку/иконкам, а состав — из каталога.
4. Синхронизировать с data-driven контентом (R18): новые entity появляются в меню без правки SFML.

## Тесты
- Тест: все entity из `build-costs.json` доступны в меню (нет расхождения состава).
- Тест: affordability в UI совпадает с результатом реального build (включая оплату из hubs).

## Definition of Done
- Меню и affordability строятся из authoritative каталога/Core, без embedded fallback и хардкода состава.

## Связанные замечания
R18 (data-driven), R10 (manifest), R2 (команды build через sink).
