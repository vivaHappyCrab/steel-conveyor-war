# Индекс планов фиксов (R1–R34)

Сводка по результатам code review (`docs/review/CODE_REVIEW_REPEAT_2026-08-10.md`).
Каждое замечание провалидировано по коду и оформлено отдельным файлом-планом (1 файл = 1 доработка).

**Статус валидации:** все 34 замечания подтверждены по исходникам — файлы и номера строк в ревью совпадают с фактическим кодом.

## Легенда
- **Severity:** High / Medium / Low
- **Roadmap:** P0 (критично сейчас) · P1 (ближайшее) · P2 (позже)

## Таблица планов

| # | План | Severity | Домен | Roadmap |
|---|------|----------|-------|---------|
| R1 | [Actor authorization](R01-actor-authorization.md) | High | command / trust | P0 |
| R2 | [Command queue production path](R02-command-queue-production-path.md) | High | command | P0 |
| R3 | [Canonical command order](R03-canonical-command-order.md) | High | determinism | P0 |
| R4 | [Immutable observation snapshot](R04-immutable-observation-snapshot.md) | High | fair observation | P0 |
| R5 | [Cast-mutable collections](R05-cast-mutable-collections.md) | High | encapsulation | P0 |
| R6 | [GameSimulation god object](R06-gamesimulation-god-object.md) | Medium | maintainability | P1 |
| R7 | [Movement/collision spatial](R07-movement-collision-spatial.md) | Medium | performance | P2 |
| R8 | [Hash commander orders](R08-hash-commander-orders.md) | High | determinism | P0 |
| R9 | [Malformed command hardening](R09-malformed-command-hardening.md) | High | robustness | P0 |
| R10 | [Content manifest hash](R10-content-manifest-hash.md) | High | multiplayer correctness | P0 |
| R11 | [Command protocol grade](R11-command-protocol-grade.md) | Medium | protocol | P1 |
| R12 | [Deep immutable payloads](R12-deep-immutable-payloads.md) | Medium | encapsulation | P1 |
| R13 | [Pending commands hash](R13-pending-commands-hash.md) | Medium | determinism | P1 |
| R14 | [Public mutation hooks](R14-public-mutation-hooks.md) | Medium | encapsulation | P1 |
| R15 | [Energy batched water-filling](R15-energy-batched-waterfilling.md) | Medium | performance | P2 |
| R16 | [FoW dirty regions](R16-fow-dirty-regions.md) | Medium | performance | P2 |
| R17 | [Factory/bastion accounting](R17-factory-bastion-accounting.md) | Medium | correctness | P1 |
| R18 | [Data-driven entities](R18-data-driven-entities.md) | Medium | extensibility | P1 |
| R19 | [Bot observation contract](R19-bot-observation-contract.md) | Medium | bots / API | P1 |
| R20 | [Fixed-point coordinates](R20-fixed-point-coordinates.md) | Medium | determinism | P1 |
| R21 | [SFML session decomposition](R21-sfml-session-decomposition.md) | Medium | maintainability | P1 |
| R22 | [Performance regression gate](R22-performance-regression-gate.md) | Medium | testing / perf | P1 |
| R23 | [Fixed-step catch-up cap](R23-fixed-step-catchup-cap.md) | Low | simulation timing | P2 |
| R24 | [Invalid local player](R24-invalid-local-player.md) | Low | robustness | P2 |
| R25 | [Shared host bootstrap](R25-shared-host-bootstrap.md) | Low | integration | P2 |
| R26 | [Enemy laboratory research](R26-enemy-laboratory-research.md) | High | gameplay / trust | P0 |
| R27 | [FoW selection & tracers](R27-fow-selection-tracers.md) | High | fair observation | P0 |
| R28 | [Research content validation](R28-research-content-validation.md) | High | content / validation | P0 |
| R29 | [Research allocation determinism](R29-research-allocation-determinism.md) | High | research | P0 |
| R30 | [Build menu runtime catalog](R30-build-menu-runtime-catalog.md) | High | UI / content | P0/P1 |
| R31 | [Roster & victory by teams](R31-roster-victory-teams.md) | High/Medium | gameplay / integration | P1 |
| R32 | [Configurable TPS](R32-configurable-tps.md) | Medium | simulation timing | P1 |
| R33 | [SFML rendering hotspots](R33-sfml-rendering-hotspots.md) | Medium | rendering / perf | P2 |
| R34 | [Cross-catalog schema validation](R34-cross-catalog-schema-validation.md) | Medium | content / validation | P1/P2 |

## Приоритетные (P0) для немедленного разбора
R1, R2, R3, R4, R5, R8, R9, R10, R26, R27, R28, R29 — критичны для детерминизма lockstep-симуляции, честного наблюдения (FoW) и trust boundary в мультиплеере.
