# M03 — Strict schema + полный cross-catalog graph

**Source review:** [`CODE_REVIEW_POST_REMEDIATION_2026-08-11.md`](../../CODE_REVIEW_POST_REMEDIATION_2026-08-11.md) § M3  
**Severity:** Medium · **Домен:** content validation · **Roadmap:** P1  
**Related residual:** R34 (partial), R28, H01  
**Статус валидации:** ✅ Подтверждено по коду

---

## Проблема

`ContentCrossValidator` проверяет только:

- build-cost → research tech;
- production recipe → research tech;
- entity Defeat kind parseability.

Не проверяются research unlocks → content, recipe item domains, map start bounds/overlap, numeric ranges, unknown JSON properties, undefined numeric enums. Research DTO без `schemaVersion`. Unknown unlock kinds могут silently no-op.

---

## Доказательства

- `ContentCrossValidator.cs:17-67` — только build→tech, recipe→tech, Defeat→EntityKind; **`tiles` accepted but never referenced**
- `ContentBootstrap.cs:71` — вызывает validator (сила = ширина графа)
- `ResearchContentLoader.cs:285-291` — `ResearchCatalogDto` **без** `SchemaVersion` (в отличие от остальных loaders + `ContentSchema.RequireSupportedVersion`)
- `ResearchContentLoader.cs:144-148` — `ParseUnlock` принимает любые contentKind/contentId strings
- `ResearchSystem.cs:723-735` — `UnlockContentEffect`: unknown `ContentKind` → silent no-op (нет `default`)
- `GameplayTablesLoader` — `Enum.TryParse` принимает numeric strings; limited range checks
- `MapSettingsLoader.cs:98-105` — coordinates without bounds (связка M11)
- JSON options: case-insensitive, **нет** `UnmappedMemberHandling = Disallow`
- `ContentCrossValidationTests` — только три текущих edge + schema на build-costs

---

## План фикса

1. Add `schemaVersion` to research root DTO + `ContentSchema.RequireSupportedVersion`.
2. Enable `JsonSerializerOptions` unmapped member failure (or explicit extension data forbid) on all content loaders.
3. Expand `ContentCrossValidator`:
   - research UnlockContentEffect → entity/recipe/item recipe catalogs;
   - recipe inputs/outputs ∈ ItemId / EntityKind domains;
   - build costs kinds ∈ placeable set;
   - tiles referenced by map/terrain if applicable;
   - map starts inside world, no overlap of footprints.
4. Reject undefined enum numeric values (`Enum.IsDefined`).
5. Validate gameplay stats ranges (health>0, cooldowns>=0, etc.).
6. Unknown unlock kind → fail-fast at validate time (not apply time).
7. Negative test matrix per edge in `ContentCrossValidationTests`.

Coordinate with H01: validator should validate the catalog that will be executed.

---

## Тесты

One negative test per reference edge + schema version missing/too new + unknown property + undefined enum number.

---

## Definition of Done

- [ ] Research has schema version.
- [ ] Cross-catalog graph covers unlocks and recipes.
- [ ] Unknown/unmapped content fails load.
- [ ] R34 marked closed for stated DoD.

## Специалисты

`modding-extensibility`, `map-generation`, `test-ci`.
