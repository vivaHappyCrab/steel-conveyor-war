# H07 — Deep-freeze authoritative content catalogs

**Source review:** [`CODE_REVIEW_POST_REMEDIATION_2026-08-11.md`](../../CODE_REVIEW_POST_REMEDIATION_2026-08-11.md) § H7  
**Severity:** High · **Домен:** encapsulation / determinism · **Roadmap:** P0  
**Related residual:** R05 (partial), R12, H01  
**Статус валидации:** ✅ Подтверждено по коду

---

## Проблема

Публичные catalog records объявлены как `IReadOnlyDictionary`, но loaders часто возвращают живые `Dictionary<,>`. Внешний код может downcast и мутировать:

- entity defeat rules (`EntityCatalog.Entities`);
- tiles;
- nested build costs `Costs[kind]`;
- nested gameplay recipe input maps (manifest metadata today; runtime authority after H01).

Мутация после tick 0 обходит command stream и может рассинхронизировать peers / исказить manifest при позднем `Compute`.

---

## Доказательства

| Участок | Что происходит |
|---|---|
| `EntityContentLoader.cs:57-70` | Returns raw `entities` dictionary |
| `EntityCatalog.cs:10-14` | Public `IReadOnlyDictionary` over that instance |
| `TileContentLoader.cs:40-45` / `TileCatalog.cs:9-13` | Same pattern |
| `BuildCostContentLoader.cs:60-90` | Root `.AsReadOnly()`, nested cost dicts raw |
| `GameplayTablesLoader.cs:37-48,150-180,311-337` | Root AsReadOnly; recipe input maps retained inside records |
| `ValueObjects.cs:134` | `ProductionRecipe.Inputs` typed `IReadOnlyDictionary` over potentially live dict |
| `EncapsulationTests.ContentTables_AreNotCastMutable` | Embedded Frozen/AsReadOnly **power maps only** — not loaded Entity/Tile/nested costs/recipes |
| `ContentManifestHashTests` | Even mutates via `with` + dict replace to prove identity sensitivity (assumes mutability) |
| R05 tests | Cover world/players/queue/root MvpDefinitions — not entity/tile nested graphs |

---

## Почему R05 «closed» неполный

R05 защитил simulation root collections и часть top-level tables. Content catalogs / nested payload graphs остались.

---

## Целевой дизайн

At parse/construction time:

1. Deep copy into `FrozenDictionary` / `ImmutableDictionary` / nested `AsReadOnly` recursively.
2. Catalog constructors accept only already-frozen graphs (or freeze inside ctor).
3. No public API returns cast-mutable nested collections.
4. Encapsulation tests attempt cast+mutate at every level.

Note on H01: freeze gameplay tables even while Embedded is executed — prevents manifest metadata mutation; after H01 freeze protects runtime rules.

---

## План фикса

1. Inventory all content graphs: Entity, Tile, BuildCost, GameplayTables, Research (if any mutable maps), Map settings player lists.
2. Helper `ContentFreeze.Dictionary(...)` / recursive freeze for recipe inputs.
3. Update loaders to return frozen graphs.
4. Expand `EncapsulationTests` / `ContentImmutabilityTests`:
   - cast root → NotSupported / wrong type;
   - cast nested cost/recipe inputs → immutable;
   - mutate attempt does not change subsequent gameplay/manifest.
5. Document: catalogs immutable after Load; hosts must create new match to change content.

PR can ship independently of H01; coordinate merge order if both touch GameplayTablesLoader.

---

## Тесты

- `ContentImmutabilityTests.EntityCatalog_NotCastMutable`
- `...BuildCostNestedCosts_NotCastMutable`
- `...GameplayRecipeInputs_NotCastMutable`
- Manifest stable if caller attempts mutation (no-op / throw)

---

## Риски / non-goals

**Риски:** FrozenDictionary enumeration order is deterministic by hash policy — ensure manifest canonicalization still sorts keys (already does).  
**Non-goals:** making observation snapshots immutable (M10); command `with` bypass (M1).

---

## Definition of Done

- [ ] All authoritative catalog graphs deep-immutable.
- [ ] Recursive cast-mutation tests green.
- [ ] R05 residual for catalogs closed.
- [ ] verify green.

---

## Верификация

```powershell
dotnet test tests/SteelConveyorWar.Core.Tests -c Release --filter "Encapsulation|ContentImmutability|ConfigContent"
powershell -NoProfile -ExecutionPolicy Bypass -File eng/verify.ps1
```

## Специалисты

`modding-extensibility`, `multiplayer-correctness`, `engine-architect`, `test-ci`.
