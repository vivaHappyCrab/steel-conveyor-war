# R27 — Selection и combat tracers не должны нарушать FoW

**Severity:** High (gameplay) · **Домен:** fair observation / rendering · **Roadmap:** P0
**Статус валидации:** ✅ Подтверждено по коду
**Статус реализации:** ✅ Реализовано 2026-08-11 (`Rendering/WorldRenderer.cs`, `SfmlPlaySession.cs`, `Ui/HudOverlay.cs`, `SteelConveyorWar.Sfml.csproj`; тесты в `CombatShotVisibilityTests.cs`). ⚠️ Требуется прогон `dotnet build -c Release` + `dotnet test` (VM недоступна, изменения не скомпилированы).

### Что сделано
- `WorldRenderer.IsShotVisibleToLocalPlayer` — новый gate: трассер показывается, только если виден attacker/target (owned или на `Visible`-тайле) либо один из endpoints лежит на `Visible`-тайле.
- `SfmlPlaySession`: combat shots фильтруются этим gate **на этапе захвата** в `lingeringShots` — скрытые выстрелы вообще не попадают в presentation-буфер локального игрока.
- `SfmlPlaySession`: каждую симуляционную итерацию ре-валидируется `selectedEntityId` — если выбранная сущность удалена или больше не видна локальному игроку (`IsVisibleToLocalPlayer`), selection сбрасывается, чтобы HUD не читал live HP/energy/position/orders.
- `HudOverlay.DrawHud`: защитный второй gate — панель выбранной сущности не рисуется, если она не видна локальному игроку.
- `SteelConveyorWar.Sfml.csproj`: добавлен `InternalsVisibleTo` для `SteelConveyorWar.Sfml.Tests` (доступ к internal-хелперам gate из тестов).

### Тесты (`SteelConveyorWar.Sfml.Tests`)
- `Shot_InHiddenEnemyArea_IsNotVisibleToLocalPlayer`, `Shot_ByOwnedAttacker_IsAlwaysVisible`, `Shot_BecomesVisible_WhenEndpointEntersVision`, `HiddenEnemySelection_WouldBeDropped_ByVisibilityGate` (seed 42: враг стартует в `Unknown`, `TryTeleportEntityForTests` + `AdvanceTick` делает его `Visible`).

### Отклонения от плана
- Пункт 1/2 плана предлагал читать данные selection/HUD из observation snapshot (R04). R04 ещё не реализован, поэтому применён эквивалентный по эффекту visibility-gate (сброс selection + защитный gate в HUD) — утечка скрытых данных закрыта тем же критерием видимости. После реализации R04 стоит перевести HUD на snapshot.
- Тест на «selection reset» проверяет сам gate (`IsVisibleToLocalPlayer`), а не игровой цикл `SfmlPlaySession.Run` (требует окна/SFML) — цикл использует ровно этот критерий.

## Проблема
Видимость проверяется только в момент клика/выбора. После ухода enemy из vision `selectedEntityId` сохраняется, а HUD продолжает читать live HP/energy/production/world position/queued orders. Дополнительно все `CombatShotsThisTick` сохраняются без фильтрации по локальному игроку, и renderer рисует их без visibility gate. Итог: скрытое движение/бой можно отслеживать через выбранный объект и endpoints трассеров.

## Где в коде
- `src/SteelConveyorWar.Sfml/SfmlPlaySession.cs:744-767` — видимость проверяется только при выборе.
- `src/SteelConveyorWar.Sfml/Ui/HudOverlay.cs:237-286` — HUD читает live HP/energy/production/position/orders выбранной сущности.
- `src/SteelConveyorWar.Sfml/SfmlPlaySession.cs:915-922` — глобальные `CombatShotsThisTick` без local-player фильтра.
- `src/SteelConveyorWar.Sfml/Rendering/WorldRenderer.cs:44-58,83-101` — отрисовка трассеров без visibility gate.

## План фикса
1. Каждый кадр ре-валидировать selection через observation snapshot (R04): если выбранная чужая сущность больше не видима — сбросить selection (или показывать только «last seen» замороженные данные, если это game-design решение).
2. HUD читать данные выбранной сущности только из snapshot, а не из live `WorldEntity`.
3. Фильтровать presentation-события (combat shots) по видимости наблюдателя: показывать выстрел, только если его позиция/участник видимы локальному игроку.
4. Renderer применяет visibility gate к трассерам.

## Тесты
- Тест: после ухода enemy в FoW HUD не обновляет его HP/позицию (selection сброшен или заморожен).
- Тест: combat shot в невидимой области не рендерится/не попадает в presentation-события локального игрока.

## Definition of Done
- Скрытое движение/бой невозможно отследить через selection или трассеры.

## Связанные замечания
R04 (snapshot), R19 (event stream), R26.
