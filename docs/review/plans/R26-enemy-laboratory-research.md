# R26 — Запретить управление research чужой лаборатории (SFML)

**Severity:** High (gameplay authority bug) · **Домен:** gameplay / trust · **Roadmap:** P0
**Статус валидации:** ✅ Подтверждено по коду — это текущий баг, не только будущий сетевой риск
**Статус реализации:** ✅ Реализовано 2026-08-11 (`SfmlPlaySession.cs`, `GameSimulation.cs`; тесты в `ResearchActorAuthorizationTests.cs`). ⚠️ Требуется прогон `dotnet build -c Release` + `dotnet test` (VM недоступна, изменения не скомпилированы).

### Что сделано
- `SfmlPlaySession` (number-shortcut лаборатории): ввод игнорируется, если `selectedEntity.OwnerId != localPlayer`; snapshot и `TrySelectResearch` теперь всегда вызываются с `localPlayer` (а не с owner выбранной сущности), плюс явный `actor: localPlayer`.
- `GameSimulation.TrySelectResearch` — добавлен опциональный `PlayerId? actor`: если `actor` задан и не равен `playerId` (владельцу research), команда отклоняется (`NotAvailable`) — defense-in-depth на Core boundary (R1). `TryStartResearch` пробрасывает `actor`.

### Тесты (`SteelConveyorWar.Core.Tests`)
- `TrySelectResearch_ForeignActor_IsRejected`, `TryStartResearch_ForeignActor_IsRejected` — actor=P1, target=P2 → отклонено.
- `OwnerActor_BehavesIdenticallyToLegacyNullActor` — для владельца gate прозрачен (результат совпадает с legacy no-actor вызовом).

### Отклонения от плана
- Пункт 4 (единый local command controller, R2) не входит в объём R26 — реализуется в R2. Здесь actor-gate добавлен непосредственно в существующие Core-методы research; при переводе на командную шину (R2) actor будет проставляться централизованно.
- Research в Core keyed по владеющему игроку (нет отдельного lab-entity параметра), поэтому авторизация сведена к `actor == playerId`.

## Проблема
Number-shortcut для лаборатории не проверяет, что выбранная лаборатория принадлежит локальному игроку. Вместо этого берётся владелец выбранной лаборатории и research переключается от его имени. Игрок может выбрать видимую enemy laboratory и управлять исследованиями противника.

## Где в коде
- `src/SteelConveyorWar.Sfml/SfmlPlaySession.cs:384-395`:
  ```csharp
  var ownerId = selectedEntity.OwnerId ?? localPlayer;
  var panel = ResearchPanelModel.FromSnapshot(simulation.GetResearchSnapshot(ownerId), recipePage);
  ...
  simulation.TrySelectResearch(ownerId, entry.Id, ...);
  ```
  `ownerId` = владелец выбранной лаборатории, без сверки с `localPlayer`.

## План фикса
1. В shortcut требовать `selectedEntity.OwnerId == localPlayer`; иначе игнорировать ввод (или показать «not your laboratory»).
2. Всегда вызывать research-команду с `actor = localPlayer` (а не с owner выбранной сущности).
3. Продублировать защиту на Core boundary: `TrySelectResearch`/`TryStartResearch` должны отклонять actor, не владеющий целью (см. R1 authorization) — чтобы UI-баг не мог обойти правило.
4. Ввести единый local command controller (R2), где actor всегда = localPlayer.

## Тесты
- Adapter-тест P1/P2: попытка переключить research выбранной enemy laboratory игнорируется, состояние research противника не меняется.
- Core-тест: `TrySelectResearch(actor=P1, labOwner=P2)` → отклонено.

## Definition of Done
- Невозможно управлять research чужой лаборатории ни через SFML, ни через Core.

## Связанные замечания
R1 (authorization), R2 (local controller), R27 (та же выбранная-enemy проблема).
