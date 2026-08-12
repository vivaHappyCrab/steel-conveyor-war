# Индекс планов доработки (post-remediation H/M/L)

Сводка по [`CODE_REVIEW_POST_REMEDIATION_2026-08-11.md`](../CODE_REVIEW_POST_REMEDIATION_2026-08-11.md).  
Каждый пункт каталога проблем (§4) оформлен отдельным файлом: **1 файл = 1 доработка**.

Предыдущие планы R01–R34 остаются в `docs/review/plans/` как история первого remediation wave. Этот набор закрывает **остаточные** замечания третьего ревью.

**Базовый commit ревью:** `ba43004` / `develop` после PR #111 + отчёт PR #112.  
**Связанный issue:** [#61](https://github.com/vivaHappyCrab/steel-conveyor-war/issues/61)

Evidence в планах дополнительно уточнён по повторной сверке кода на `802d5b4` (call-site inventory, protocol/batch gaps, spatial rebuild sites, zero-team victory hole, dirty-catch-up options).

**Четвёртое ревью (после реализации):** [`../CODE_REVIEW_FOURTH_2026-08-12.md`](../CODE_REVIEW_FOURTH_2026-08-12.md) — все H/M/L **CLOSED** на `c31ba95` (PR #114).

## Легенда

- **Severity:** High / Medium / Low
- **Roadmap:** P0 (критично сейчас) · P1 (ближайшее) · P2 (позже)
- **Related Rxx:** какой старый пункт остался partial/open

## Таблица планов

| ID | План | Severity | Домен | Roadmap | Related |
|----|------|----------|-------|---------|---------|
| H01 | [Gameplay tables authoritative](H01-gameplay-tables-authoritative.md) | High | content authority | P0 | R18 |
| H02 | [Research remainder hash](H02-research-remainder-hash.md) | High | determinism | P0 | R29 |
| H03 | [Command identity / actor binding](H03-command-identity-actor-binding.md) | High | lockstep / trust | P0 | R03, R01 |
| H04 | [Factory idle-cache invalidation](H04-factory-idle-cache-invalidation.md) | High | correctness | P0 | R17 |
| H05 | [Build menu runtime catalog](H05-build-menu-runtime-catalog.md) | High | SFML / content | P0 | R30 |
| H06 | [Combat tracer FoW endpoints](H06-combat-tracer-fow-endpoints.md) | High | FoW / fairness | P0 | R27 |
| H07 | [Content catalog deep freeze](H07-content-catalog-deep-freeze.md) | High | encapsulation | P0 | R05 |
| M01 | [Payload `with` bypass](M01-payload-with-bypass.md) | Medium | encapsulation | P1 | R12 |
| M02 | [Command protocol vocabulary](M02-command-protocol-vocabulary.md) | Medium | protocol / bots | P1 | R11 |
| M03 | [Schema + cross-catalog](M03-schema-cross-catalog-validation.md) | Medium | content validation | P1 | R34 |
| M04 | [Benchmark hard gate](M04-benchmark-hard-gate.md) | Medium | CI / perf | P1 | R22 |
| M05 | [Energy level water-fill](M05-energy-level-waterfill.md) | Medium | performance | P1/P2 | R15 |
| M06 | [Spatial/FoW allocations](M06-spatial-fow-allocations.md) | Medium | performance | P1/P2 | R07, R16 |
| M07 | [Minimap catch-up dirty](M07-minimap-catchup-dirty.md) | Medium | SFML | P1 | R33 |
| M08 | [SFML resources + decomposition](M08-sfml-resources-decomposition.md) | Medium | maintainability | P1/P2 | R21 |
| M09 | [Host bootstrap boundary](M09-host-bootstrap-boundary.md) | Medium | architecture | P2 | R25 |
| M10 | [Observation frame immutability](M10-observation-frame-immutability.md) | Medium | bots | P1 | R04, R19 |
| M11 | [Session manifest / roster](M11-session-manifest-roster.md) | Medium | session / multiplayer | P1 | R10, R31, R32 |
| L01 | [Trailing `--local-player`](L01-local-player-trailing-flag.md) | Low | CLI UX | P2 | R24 |
| L02 | [Commander select after death](L02-commander-select-after-death.md) | Low | SFML robustness | P2 | R24 |
| L03 | [UI mode exclusivity](L03-ui-mode-exclusivity.md) | Low | SFML UX | P2 | R21 |

## Рекомендуемый порядок внедрения

### Wave P0 (correctness / determinism)

Параллелить маленькие Core PR:

1. **H04** + **H02** (маленькие, независимые) — freeze bug + remainder hash
2. **H01** — gameplay tables authoritative (крупный wiring)
3. **H07** — deep-freeze catalogs (до/параллельно с H01)
4. **H05** — SFML build menu runtime catalog
5. **H06** — tracer FoW policy
6. **H03** — command identity + bound actor sink

### Wave P1

M01 → M02 → M10 → M03 → M11 → M07 → M04 → M05/M06 → M08

### Wave P2

M09, L01–L03, remaining perf polish

## Зависимости (кратко)

```text
H01 ──┬──> H05 (menu still useful before H01, but content story completes with H01)
      └──> M03 (validate executed catalog)

H07 parallel/safe before or with H01

H06 ──> M10 (events in observation frame)

H03 ──> M02 (protocol vocabulary)

M04 ──> M05 / M06 (bench proves perf DoD)

M09 after hosts stable (independent)
```

## Definition of Done для всего набора

- Каждый план реализован отдельным PR (или явно сгруппирован с указанными зависимостями).
- Для каждого: тесты из плана + `eng/verify.ps1`.
- После волны P0: обновить `docs/review/STATUS.md` и комментарий в #61.
- Исправления **не** смешивать с правкой текста ревью без необходимости.

## Верификация набора планов (docs-only)

```powershell
Get-ChildItem docs/review/plans/post-remediation -Filter *.md | Measure-Object
# ожидается: 21 plan + 1 index = 22
```
