# R04 — Immutable observation snapshot вместо live-ссылок

**Severity:** High · **Домен:** fair observation / bots · **Roadmap:** P0/P1
**Статус валидации:** ✅ Подтверждено по коду

## Проблема
`PlayerView` фильтрует видимость только в момент запроса, но возвращает реальные `WorldEntity`. Бот может получить enemy entity, пока тайл видим, сохранить ссылку и после ухода противника в FoW продолжать читать актуальные `Position`, `Health`, order/buffers из того же объекта. Кроме того, видимая enemy entity раскрывает весь внутренний state, а не ограниченный game-design snapshot.

## Где в коде
- `src/SteelConveyorWar.Core/Observation/PlayerView.cs:55-74` — `GetVisibleEntities`/`GetVisibleEntity` возвращают живые `WorldEntity` (в Fair-режиме `Where(IsFairEntityVisible)`, но объекты те же).
- `src/SteelConveyorWar.Core/Observation/IPlayerView.cs:27-38` — контракт отдаёт `WorldEntity`.

## План фикса
1. Ввести immutable DTO: `VisibleEntitySnapshot` (id, kind, ownerId, tile/world position, health, direction — только то, что допустимо видеть по game-design), плюс `OwnEconomySnapshot`/`OwnResearchSnapshot` для собственных сущностей.
2. Ввести `PlayerObservationSnapshot`, привязанный к конкретному тику: список видимых `VisibleEntitySnapshot`, terrain/visibility slice, собственная экономика/исследования, observation tick.
3. Строить snapshot копированием значений (без выдачи `WorldEntity`).
4. Изменить `IPlayerView`/`PlayerView` так, чтобы наружу отдавались только snapshot-DTO. Для собственных сущностей — расширенный snapshot, для чужих — ограниченный.
5. Удалить/пометить `internal` прямой доступ к `WorldEntity` из observation-контракта.

## Тесты
- Тест: сохранённая ссылка на snapshot enemy не обновляется после ухода в FoW (значения «заморожены» на тике получения).
- Тест: snapshot чужой сущности не содержит скрытых полей (buffers/orders).
- Тест: собственные сущности содержат полный экономический snapshot.

## Definition of Done
- Ни один observation-путь не возвращает `WorldEntity`.
- Есть тест «retained reference не раскрывает hidden state».

## Связанные замечания
R19 (полнота observation contract), R27 (та же проблема в SFML HUD).
