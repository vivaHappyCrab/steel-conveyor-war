# M01 — Закрыть record `with` bypass для immutable payloads

**Source review:** [`CODE_REVIEW_POST_REMEDIATION_2026-08-11.md`](../../CODE_REVIEW_POST_REMEDIATION_2026-08-11.md) § M1  
**Severity:** Medium · **Домен:** encapsulation / determinism · **Roadmap:** P1  
**Related residual:** R12 (partial), H07  
**Статус валидации:** ✅ Подтверждено по коду

---

## Проблема

Конструкторы `BastionOrder` / `SetTrackAllocationCommand` копируют коллекции в immutable backing, но свойства остаются `public ... { get; init; }`. Через `with { WaypointList = mutableList }` / `with { Allocations = mutableDict }` можно снова подставить mutable коллекцию до enqueue.

Существующие `ImmutablePayloadTests` покрывают mutation источника после ctor, но не `with`-replacement.

---

## Доказательства

- `Domain/ValueObjects.cs:93-110` — ctor копирует в `ImmutableArray`, но `WaypointList { get; init; }`
- `Commands/SimulationCommands.cs:94-104` — ctor → `ImmutableDictionary`, но `Allocations { get; init; }`
- `ImmutablePayloadTests.cs:12-76` — только source-mutation после ctor/enqueue, **нет** `with`-replacement
- Легитимный scalar `with` (не баг): `MovementSystem.cs:54` — `unit.Order with { WaypointIndex = nextIndex }`
- R12 plan «Отклонения» намеренно оставил interface-typed init — именно это и оставляет дыру

---

## План фикса

1. Хранить конкретный immutable тип в private/init-only field:
   - `ImmutableArray<TilePosition> _waypoints`
   - `ImmutableDictionary<string,int> _allocations`
2. Публичный getter возвращает интерфейс/immutable view **без** public init setter для collection properties.
3. Для `with` по скалярам (`WaypointIndex`, `Kind`, `Target`) сохранить record semantics через manual `with`-friendly design:
   - option A: non-record class + `WithWaypointIndex`;
   - option B: record with collection props as `get`-only and ctor-only assignment (C# init required only in ctor).
4. Prefer: make collection properties get-only; provide `BastionOrder WithWaypointIndex(int)` used by MovementSystem.
5. Tests: `with` attempting collection replace does not compile **or** re-freezes; mutate after illicit replace impossible.

Recommended: change to get-only properties + explicit with-methods for scalar updates. Update MovementSystem call sites.

---

## Тесты

- Compile/runtime: cannot replace WaypointList with List via `with` (or replacement re-copies)
- Scalar waypoint index update still works
- Existing source-mutation tests remain green

---

## Definition of Done

- [ ] No public API allows substituting mutable collection into enqueued payload.
- [ ] Tests cover `with` bypass.
- [ ] R12 marked closed.

## Специалисты

`core-simulation`, `multiplayer-correctness`, `test-ci`.
