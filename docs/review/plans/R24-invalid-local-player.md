# R24 — Дружелюбная обработка невалидного local player

**Severity:** Low · **Домен:** SFML / UX · **Roadmap:** P2
**Статус валидации:** ✅ Подтверждено по коду

## Проблема
CLI проверяет положительный id, но session предполагает наличие commander и вызывает `First`. `--local-player 3` для default map (где есть только P1/P2) завершится исключением без domain-friendly ошибки.

## Где в коде
- `src/SteelConveyorWar.Sfml/SfmlPlaySession.cs:38-40` — предположение о наличии commander + `First`.

## План фикса
1. Проверять, что запрошенный `local-player` присутствует в roster карты, до запуска сессии.
2. При отсутствии — выбрасывать/возвращать понятную доменную ошибку («Player N not present on this map; available: …»), а не `InvalidOperationException` из `First`.
3. Заменить `First` на `FirstOrDefault` + явную проверку/сообщение.

## Тесты
- Тест: `--local-player 3` на 2-игроковой карте → понятная ошибка, без необработанного исключения.
- Тест: валидный id запускается нормально.

## Definition of Done
- Невалидный local player даёт дружелюбное сообщение, а не сырое исключение.

## Связанные замечания
R31 (roster/seat), R25 (host bootstrap).
