# R27 — Selection и combat tracers не должны нарушать FoW

**Severity:** High (gameplay) · **Домен:** fair observation / rendering · **Roadmap:** P0
**Статус валидации:** ✅ Подтверждено по коду

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
