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

- `GameSimulation.cs:794-805` — `TrySetProjectWeight`
- `GameSimulation.cs:855-863` — obsolete assign always false
- `SimulationCommandKind.cs:18` — AssignFactoryBastion still present
- `PlayerView.cs:11-14,136` — advertises all enum values
- `CommandProtocolTests` / `CommandSerializerPropertyTests` — shallow asserts

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

1. Добавить `SetProjectWeightCommand` + serializer + ApplyCommand branch + actor auth.
2. Удалить `AssignFactoryBastion` из public vocabulary **или** filter in `GetAvailableCommandKinds` and serializer (prefer remove/obsolete with migration note).
3. Introduce `CommandPayloadEquality` / snapshot assert helpers.
4. Expand property tests to compare all fields after round-trip.
5. Add tests for SerializeMany/DeserializeMany/TryDeserializeMany (success + truncated/malformed).
6. Optional: `IPlayerView.GetAvailableCommandKinds` returns frozen immutable list filtered by registry.

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
