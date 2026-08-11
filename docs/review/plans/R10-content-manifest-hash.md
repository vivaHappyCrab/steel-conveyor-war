# R10 — Versioned content/session manifest hash

**Severity:** High · **Домен:** determinism / handshake · **Roadmap:** P0
**Статус валидации:** ✅ Подтверждено по коду
**Статус реализации:** ✅ Реализовано 2026-08-11 (`Content/SimulationContentManifest.cs`, `Determinism/SimulationStateHasher.cs`; тесты в `ContentManifestHashTests.cs`). ⚠️ Требуется прогон `dotnet build -c Release` + `dotnet test` (VM недоступна, изменения не скомпилированы).

### Что сделано
- `SimulationContentManifest` — новый класс: канонические content-identity для `BuildCostCatalog` (schema + costs + ticks + requirements, всё упорядочено), `EntityCatalog` (schema + entities с `LossCondition`), `TileCatalog` (schema + tiles). `Compute(simulation)` объединяет research ContentHash + profile + build/entity/tile identity в единый versioned manifest hash (`ManifestVersion = 1`). Identity считается из in-memory контента (одинаково для embedded и JSON-loaded каталогов при совпадении содержимого).
- `SimulationContentManifest.EnsureMatch(local, remote)` — fail-fast: бросает `ContentManifestMismatchException` при расхождении manifest до старта симуляции (для handshake/replay header).
- `SimulationStateHasher`: вместо одного `ResearchCatalog.ContentHash` в fingerprint теперь пишется полный `SimulationContentManifest.Compute(...)`. `AlgorithmVersion` 6 → 7.

### Тесты (`SteelConveyorWar.Core.Tests`)
- Identity: разный/одинаковый build cost → разный/одинаковый hash; смена `LossCondition` entity → разный hash.
- Manifest: одинаковый контент → равный manifest; разные build costs → разный manifest; `EnsureMatch` бросает при расхождении и проходит при совпадении.
- `StateHash_DiffersAtTickZero_ForDivergentBuildCosts` — расхождение каталогов ловится на tick 0.

### Отклонения от плана
- Пункт 3/4 (handshake/replay header) реализованы как публичный API (`Compute` + `EnsureMatch` + fail-fast exception); проводки в сетевой/replay-слой нет, т.к. этих слоёв в репозитории пока нет — интеграция произойдёт при их появлении.
- Пункт 2 (TPS/session-настройки, R32) в manifest пока не включён — будет добавлен в R32; текущий manifest покрывает все gameplay-каталоги (DoD по идентичности каталогов выполнен).
- `TileCatalog`/`EntityCatalog` по умолчанию `Empty` (embedded путь), поэтому их identity стабильны и не ломают dual-run equality.

## Проблема
Hasher включает `ResearchCatalog.ContentHash`, но не идентичность `BuildCostCatalog` и `EntityCatalog`. Два peer с разными authoritative-каталогами могут иметь одинаковый state hash до первого затронутого события, а затем разойтись (build costs влияют на команды/правила, entity `lossCondition` влияет на victory).

## Где в коде
- `src/SteelConveyorWar.Core/Determinism/SimulationStateHasher.cs:39` — пишется только `simulation.ResearchCatalog.ContentHash`; идентичности build/entity каталогов нет.
- `src/SteelConveyorWar.Core/Research/ResearchContentLoader.cs:108-125` — production JSON research hash канонизирует полный DTO.
- `src/SteelConveyorWar.Core/Research/MvpResearchCatalog.cs:5-17,612-620` — слабый ID/count-only hash для embedded/default fallback.

## План фикса
1. Добавить `ContentHash`/`Identity` для `BuildCostCatalog`, `EntityCatalog` (и `TileCatalog` — на будущее), канонизируя весь DTO как в research.
2. Ввести `SimulationContentManifestHash`, объединяющий все каталоги + релевантные session-настройки (включая TPS, см. R32).
3. Включить manifest hash в handshake/replay header и в state fingerprint (перед tick 0).
4. Fail-fast при несовпадении manifest у peer'ов до старта симуляции.
5. Унифицировать силу хэша (не допускать count-only для боевых путей).

## Тесты
- Тест: разные build-costs/entity каталоги → разный manifest hash.
- Тест: несовпадение manifest → fail-fast до tick 0.
- Тест: одинаковый контент → одинаковый manifest.

## Definition of Done
- Идентичность всех gameplay-каталогов входит в versioned manifest hash.
- Handshake/replay header содержит manifest; есть fail-fast.

## Связанные замечания
R8 (state hash surface), R32 (TPS в manifest), R34 (schema/cross-catalog validation).
