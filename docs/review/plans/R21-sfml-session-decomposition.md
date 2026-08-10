# R21 — Декомпозиция SfmlPlaySession / HudOverlay

**Severity:** Medium · **Домен:** maintainability / testability · **Roadmap:** P2
**Статус валидации:** ✅ Подтверждено по коду (размеры — приближённо)

## Проблема
Декомпозиция SFML улучшена (`SfmlGameRunner` — тонкий composition facade), но `SfmlPlaySession` и `HudOverlay` остаются крупными: один большой `Run` с обработчиками событий/состоянием/циклом. Input command mapping и state-переходы трудно unit-тестировать без окна.

## Где в коде
- `src/SteelConveyorWar.Sfml/SfmlPlaySession.cs` — ~998 непустых строк (в отчёте ~1108 с учётом пустых); один `Run` с event handlers/state/loop.
- `src/SteelConveyorWar.Sfml/Ui/HudOverlay.cs` — ~805 непустых строк (в отчёте ~901).

## План фикса
1. Выделить чистый `InputCommandMapper`: (событие ввода + selection/mode) → команда (DTO), без побочных эффектов — легко unit-тестируется. Согласовать с R2 (sink).
2. Выделить `SessionState`/state-machine (build menu open, overlay open, selection) в отдельный тип с явными переходами.
3. Игровой цикл (`Run`) оставить тонким: pump events → mapper → sink → advance → render.
4. `HudOverlay` разбить на подпанели/модели (часть моделей уже есть: `HudModel`-подобные) и вынести чтение состояния в snapshot (R27).

## Тесты
- Unit-тесты `InputCommandMapper` без окна: ввод → ожидаемая команда.
- Unit-тесты state-machine переходов.

## Definition of Done
- Input-mapping и state-переходы покрыты unit-тестами без создания окна SFML.

## Связанные замечания
R2, R6, R27, R30.
