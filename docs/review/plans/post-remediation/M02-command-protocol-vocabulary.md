# M02 — Command protocol: полный vocabulary + payload equality

**Source review:** [`CODE_REVIEW_POST_REMEDIATION_2026-08-11.md`](../../CODE_REVIEW_POST_REMEDIATION_2026-08-11.md) § M2  
**Severity:** Medium · **Домен:** protocol / bots · **Roadmap:** P1  
**Related residual:** R11 (partial), R19  
**Статус валидации:** ✅ Подтверждено по коду

---

## Проблема

1. `TrySetProjectWeight` публичен, но нет command DTO / serializer case.
2. `AssignFactoryBastion` сериализуется и рекламируется в `GetAvailableCommandKinds`, но handler всегда `false`.
3. All-kind serializer tests часто проверяют только Kind/runtime type, не value equality payloads.
4. Batch APIs (`SerializeMany` / `DeserializeMany`) почти без coverage.

Bot/host не может доверять advertised vocabulary.

---

## Доказательства

- `GameSimulation.cs:794-805` — `TrySetProjectWeight` (public, no DTO)
- `ResearchSystem.cs:187-231` — мутирует **project weights** parallel-track (это **не** то же самое, что `SetTrackAllocationCommand` / basis points)
- `GameSimulation.cs:855-863` — obsolete assign always false
- `SimulationCommandKind.cs:18` + serializer/dispatcher — AssignFactoryBastion всё ещё в wire
- `PlayerView.cs:11-14,136` — advertises all enum values (включая dead kind; без project-weight)
- `CommandProtocolTests.cs:56-66` / `CommandSerializerPropertyTests.cs:27-31` — только Kind/Actor/Tick/Sequence/runtime type
- `SerializeMany` / `DeserializeMany` / `TryDeserializeMany` — zero test hits по именам API

Важно: старый R11 plan deviation «weights покрыты SetTrackAllocation» **некорректен** для project weights.

---

## Целевой дизайн

```text
Capability registry:
  advertised kinds == serializable kinds == applicable kinds
Obsolete kinds: removed from enum OR marked Obsolete and filtered from GetAvailableCommandKinds
Every authoritative research intent has DTO
Tests: round-trip value equality for every kind + batch APIs
```

---

## План фикса

1. Добавить `SetProjectWeightCommand` (actor, trackId, technologyId, weight) + serializer + ApplyCommand → `TrySetProjectWeight` с actor checks.
2. Удалить `AssignFactoryBastion` из public vocabulary **или** filter in `GetAvailableCommandKinds` and serializer (prefer remove/obsolete + protocol bump note; deserialize→reject для legacy).
3. Introduce `CommandPayloadEquality` / snapshot assert helpers.
4. Expand property tests to compare **all payload fields** after round-trip (не только header).
5. Add tests for SerializeMany/DeserializeMany/TryDeserializeMany (success + truncated/malformed/`Try*` false).
6. Optional: SFML/UI path для project weights, если дизайн требует player control; `GetAvailableCommandKinds` → frozen filtered list.

---

## Тесты

- Round-trip equality matrix per `SimulationCommandKind`
- SetProjectWeight apply + ownership
- Available kinds never contains always-false obsolete commands
- Batch deserialize partial failure behavior documented

---

## Definition of Done

- [ ] Advertised = serializable = applicable.
- [ ] Project weight is command-path capable.
- [ ] Full payload equality tests exist.
- [ ] R11 residual closed for vocabulary completeness.

## Специалисты

`core-simulation`, `multiplayer-correctness`, `test-ci`.
