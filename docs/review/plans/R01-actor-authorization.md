# R01 — Actor authorization для всех command handlers

**Severity:** High · **Домен:** command / trust boundary · **Roadmap:** P0
**Статус валидации:** ✅ Подтверждено по коду
**Статус реализации:** ✅ Реализовано 2026-08-11 (правки в `GameSimulation.cs` / `GameSimulation.Commands.cs`, тесты в `OwnershipCommandTests.cs`). ⚠️ Требуется прогон `dotnet build -c Release` + `dotnet test` (сборочная VM была недоступна в сессии реализации).

### Что сделано
- Введён helper `IsAuthorizedCommander(commander, actor)`: при переданном `actor` (untrusted command-path через `ApplyCommand`) командир должен принадлежать этому actor; локальные/внутренние вызовы передают `null` и сохраняют прежнее поведение.
- Во все ранее не-авторизованные entity-targeted `Try*`-методы добавлен необязательный параметр `PlayerId? actor = null` и guard: `TryPlaceGhostBuildFromCommander`, `TryQueueCommanderBuild`, `TryQueueCommanderDemolish`, `TryDemolishBuilding`, `TryCollectOutputBuffer`, `TryWithdrawFromHubOrOutput`, `TryDepositToHubOrInput`, `TryDepositItemTypeToHubOrInput`, `TryWithdrawItemTypeFromHubOrOutput`.
- `ApplyCommand` пробрасывает `c.Actor` во все соответствующие ветки; внутренние вызовы (`TryQueueCommanderBuild`→`TryPlaceGhostBuildFromCommander`, `TryQueueCommanderDemolish`→`TryDemolishBuilding`) форвардят actor.
- Тесты: `ApplyCommand_RejectsForeignActor_AcrossCommanderCommandKinds` (негатив по всем защищённым command kind + проверка неизменности состояния), `ApplyCommand_AllowsOwnerActor_ForCommanderCommand` (позитив).

### Отклонения от исходного плана
- Параметр `actor` сделан **необязательным (`null` = opt-out)**, а не обязательным, чтобы не ломать десятки доверенных локальных вызовов (SFML input, `CommanderOrdersSystem`, существующие тесты). Trust-boundary закрывается на untrusted-пути `ApplyCommand`, который всегда передаёт `c.Actor`.
- Единый `CommandResult`/логирование отказа (п.5) не вводился — относится к R11.
- Легаси `AssignFactoryBastionCommand` не трогался (no-op, без security-impact — как отмечено в исходном плане).

## Проблема
Диспетчер `ApplyCommand` пробрасывает `Actor` только части команд (move/stop/rotate/research/factory/bastion/assembler), но игнорирует его для build/demolish/collect/withdraw/deposit. Знание чужого `CommanderId`/`EntityId` достаточно, чтобы выполнить действие от имени другого игрока. Для будущего сетевого host это прямой обход trust-boundary.

## Где в коде
- `src/SteelConveyorWar.Core/GameSimulation.Commands.cs:52-79` — `ApplyCommand`:
  - строки 56-60: `QueueCommanderBuildCommand`, `QueueCommanderDemolishCommand`, `PlaceGhostBuildFromCommanderCommand` вызываются **без** `Actor`;
  - строки 67: `AssignFactoryBastionCommand` — без `Actor` (но legacy-handler всегда `false`, см. ниже);
  - строки 71-77: `CollectOutputBufferCommand`, `WithdrawFromHubOrOutputCommand`, `DepositToHubOrInputCommand`, typed deposit/withdraw — без `Actor`.
- `src/SteelConveyorWar.Core/GameSimulation.cs` — целевые `Try*`-методы (`TryQueueCommanderBuild`, `TryQueueCommanderDemolish`, `TryCollectOutputBuffer`, `TryWithdraw*`, `TryDeposit*`) не принимают actor и не сверяют владельца.
- `AssignFactoryBastionCommand` — исключение без security-impact: legacy handler всегда возвращает `false` (обёртка `TryAssignFactoryBastion`), фактического эффекта нет.

## План фикса
1. Добавить параметр `PlayerId actor` во все entity-targeted `Try*`-методы, которые сейчас его не принимают (build/demolish/place/collect/withdraw/deposit).
2. Ввести единый authorization-слой перед диспетчеризацией: helper `AuthorizeActor(actor, targetEntity/commander)` который сверяет `entity.OwnerId == actor` (или роль команды) и возвращает `false` при несоответствии — до любой мутации состояния.
3. Пробросить `Actor` во всех ветках `ApplyCommand` (строки 56-77).
4. Легаси `AssignFactoryBastionCommand` — либо удалить из модели команд, либо тоже провести через authorization для единообразия (оставив no-op).
5. Возвращать явный `CommandResult` (см. R11) вместо «тихого» `false`, чтобы отказ можно было залогировать.

## Тесты
- Негативный тест на **каждый** command kind: `Actor = P2`, цель принадлежит `P1` → команда отклоняется, состояние не меняется.
- Позитивный тест: `Actor` == владелец → команда применяется.
- Тест на `QueueCommanderBuildCommand(Actor = P2, CommanderId = P1)` → отклонено.

## Definition of Done
- Ни один handler не мутирует состояние без проверки actor.
- Есть параметризованный negative-test по всем `SimulationCommandKind`.
- `Release build` и все тесты зелёные.

## Связанные замечания
R2 (единый command sink), R9 (валидация untrusted), R11 (command result/logging), R26 (тот же класс authority-багов в SFML).
