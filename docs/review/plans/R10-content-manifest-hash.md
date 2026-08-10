# R10 — Versioned content/session manifest hash

**Severity:** High · **Домен:** determinism / handshake · **Roadmap:** P0
**Статус валидации:** ✅ Подтверждено по коду

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
