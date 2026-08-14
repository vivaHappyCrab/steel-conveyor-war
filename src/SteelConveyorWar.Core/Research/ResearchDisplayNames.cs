namespace SteelConveyorWar.Core;

/// <summary>Player-facing Russian copy for research technologies (ids stay stable).</summary>
public static class ResearchDisplayNames
{
    private static readonly Dictionary<string, (string Name, string Description)> Ru = new(StringComparer.Ordinal)
    {
        ["technology.t1.production-i"] = ("Производство I", "Ускоряет работу заводов. Обязательный столп для перехода на T2."),
        ["technology.t1.energy-i"] = ("Энергия I", "Смягчает штраф от нехватки энергии. Обязательный столп для T2."),
        ["technology.t1.command-i"] = ("Командование I", "Ускоряет строительство БМК. Обязательный столп для T2."),
        ["technology.t1.automated-base"] = ("Автобаза", "Автоматизация базы: ускорение заводских циклов."),
        ["technology.t1.distant-expedition"] = ("Далёкая экспедиция", "Ускоряет строительство на удалённых позициях."),
        ["technology.t1.new-resource-mastery"] = ("Освоение ресурсов", "Открывает доступ к контенту следующего тира."),
        ["technology.t1.improved-conveyors"] = ("Улучшенные конвейеры", "Конвейеры перемещают предметы быстрее."),
        ["technology.t1.mass-production"] = ("Массовое производство", "Дополнительное ускорение заводских циклов."),
        ["technology.t1.distributed-energy"] = ("Распределённая энергия", "Снижает влияние нехватки энергии."),
        ["technology.t1.power-reserve"] = ("Энергорезерв", "Улучшает устойчивость энергосети."),
        ["technology.t1.energy-reserve"] = ("Запас энергии", "Резерв мощности для пиковой нагрузки."),
        ["technology.t1.expedition-logistics"] = ("Экспедиционная логистика", "Быстрее разворачивает полевую инфраструктуру."),
        ["technology.t1.defensive-engineering"] = ("Оборонная инженерия", "Ускоряет возведение полевых укреплений."),
        ["technology.t1.forward-observer"] = ("Передовой наблюдатель", "Улучшает разведку и наведение."),
        ["technology.t1.ground-unit-attack"] = ("Атака наземных юнитов", "Увеличивает атаку всех наземных юнитов."),
        ["technology.t1.field-repair"] = ("Полевой ремонт", "Юниты медленно чинятся вне боя."),
        ["technology.t1.prefab-fortifications"] = ("Сборные укрепления", "Дешевле и быстрее ставить оборону."),
        ["technology.t1.production-reserve"] = ("Производственный резерв", "Запас мощности производства."),
        ["technology.t1.fast-regroup"] = ("Быстрый сбор", "Юниты быстрее реагируют на приказы бастиона."),
        ["technology.t1.ground-unit-armor"] = ("Броня наземных юнитов", "Увеличивает броню всех наземных юнитов."),
        ["technology.t1.machine-gun-turret"] = ("Пулемётная турель", "Открывает стационарную пулемётную оборону."),
        ["technology.t1.additional-bastion"] = ("Дополнительный бастион", "Увеличивает лимит бастионов на 1."),
        ["technology.t1.hub-buffering"] = ("Буферы хаба", "Увеличивает полезность складских хабов."),
        ["technology.t1.modular-tooling"] = ("Модульный инструмент", "Ускоряет смену производственных задач."),
        ["technology.t1.conveyor-telemetry"] = ("Телеметрия конвейеров", "Лучше диагностирует заторы логистики."),
        ["technology.t1.recon-archive"] = ("Архив разведки", "Усиливает эффективность разведки."),
        ["technology.t1.return-protocol"] = ("Протокол возврата", "Юниты быстрее возвращаются к бастиону."),
        ["technology.t1.doctrine.mobile-groups"] = ("Доктрина: мобильные группы", "Специализация на манёвре. Взаимоисключающая."),
        ["technology.t1.doctrine.fortified-line"] = ("Доктрина: укреплённая линия", "Специализация на обороне. Взаимоисключающая."),
        ["technology.t1.doctrine.fortification"] = ("Доктрина: фортификация", "Упор на статическую оборону."),
        ["technology.t1.doctrine.observation"] = ("Доктрина: наблюдение", "Упор на разведку и контроль."),
        ["technology.t1.doctrine.swarm"] = ("Доктрина: рой", "Упор на массовые лёгкие силы."),
        ["technology.t2.metallurgy-ii"] = ("Металлургия II", "Продвинутая металлургия. Обязательный столп для T3."),
        ["technology.t2.supply-ii"] = ("Снабжение II", "Улучшенная логистика снабжения армии."),
        ["technology.t2.command-ii"] = ("Командование II", "Расширяет управление бастионами и стройкой."),
        ["technology.t2.steel-walls"] = ("Стальные стены", "Открывает усиленные стальные стены."),
        ["technology.t2.underground-conveyors"] = ("Подземные конвейеры", "Открывает подземные участки лент."),
        ["technology.t2.additional-bastions"] = ("Доп. бастионы", "Расширяет шаблон армии бастионов."),
        ["technology.t2.construction-drone"] = ("Строительный дрон", "Открывает строительных дронов."),
        ["technology.t2.anti-air-turret"] = ("Зенитная турель", "Открывает ПВО-турели."),
        ["technology.t2.medium-bot"] = ("Средний бот", "Открывает средних боевых ботов."),
        ["technology.t2.medium-tank"] = ("Средний танк", "Открывает средние танки."),
        ["technology.t2.rocket-launcher"] = ("Ракетница", "Открывает ракетные установки."),
        ["technology.t2.predictive-aa"] = ("Предиктивное ПВО", "Улучшает эффективность зенитки."),
        ["technology.t2.reinforced-hubs"] = ("Усиленные хабы", "Хабы крепче и вместительнее."),
        ["technology.t2.mobile-repair"] = ("Мобильный ремонт", "Усиливает ремонт в поле."),
        ["technology.t2.drone-coordination"] = ("Координация дронов", "Дроны действуют эффективнее."),
        ["technology.t2.ammo-priority"] = ("Приоритет боеприпасов", "Боевые юниты лучше обеспечены."),
        ["technology.t2.auto-resupply"] = ("Автоснабжение", "Автоматизирует подвоз к фронту."),
        ["technology.t2.field-bastion-network"] = ("Сеть полевых бастионов", "Связывает бастионы в сеть."),
        ["technology.t2.fuel-intensification"] = ("Интенсификация топлива", "Эффективнее использует топливо."),
        ["technology.t2.fuel-logistics"] = ("Топливная логистика", "Улучшает доставку топлива."),
        ["technology.t2.steel-standardization"] = ("Стандартизация стали", "Ускоряет стальное производство."),
        ["technology.t2.doctrine.armor-breakthrough"] = ("Доктрина: бронепрорыв", "Специализация на прорыве. Взаимоисключающая."),
        ["technology.t2.doctrine.ranged-pressure"] = ("Доктрина: дальний нажим", "Специализация на дистанции. Взаимоисключающая."),
        ["technology.t2.doctrine.armor-fist"] = ("Доктрина: бронекулак", "Тяжёлый броневой кулак."),
        ["technology.t2.doctrine.maneuver-net"] = ("Доктрина: манёврная сеть", "Сетевой манёвр подразделений."),
        ["technology.t2.doctrine.siege-control"] = ("Доктрина: осадный контроль", "Контроль и осада позиций."),
    };

    public static string GetDisplayName(TechnologyId id)
    {
        if (Ru.TryGetValue(id.Value, out var entry))
        {
            return entry.Name;
        }

        var parts = id.Value.Split('.');
        return parts.Length == 0 ? id.Value : parts[^1].Replace('-', ' ');
    }

    public static string GetDescription(TechnologyId id, IReadOnlyList<string> tags)
    {
        if (Ru.TryGetValue(id.Value, out var entry))
        {
            return entry.Description;
        }

        return tags.Count == 0 ? "Исследование" : string.Join(", ", tags.Take(4));
    }
}
