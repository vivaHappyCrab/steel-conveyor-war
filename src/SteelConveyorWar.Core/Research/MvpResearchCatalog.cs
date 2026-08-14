namespace SteelConveyorWar.Core;

public static class MvpResearchCatalog
{
    public static ResearchCatalog CreateEmbedded()
    {
        var technologies = BuildTechnologies();
        var tiers = BuildTiers();
        var profiles = BuildProfiles();
        var catalog = new ResearchCatalog(
            technologies,
            tiers,
            profiles,
            BaselineCapabilities: Array.Empty<string>(),
            ContentHash: "embedded");
        ResearchContentValidator.Validate(catalog);
        return catalog with { ContentHash = ResearchContentHash.Compute(catalog) };
    }

    private static IReadOnlyDictionary<TechnologyId, TechnologyDefinition> BuildTechnologies()
    {
        var list = new List<TechnologyDefinition>();

        void Tech(
            TechnologyId id,
            string tier,
            int effort,
            ItemId pack,
            IEnumerable<ResearchEffect> effects,
            params string[] tags)
        {
            list.Add(new TechnologyDefinition(
                id,
                tier,
                new ResearchCostDefinition(effort * 10, [new SciencePackCost(pack, 1)]),
                effects.ToList(),
                tags,
                ResearchDisplayNames.GetDisplayName(id),
                ResearchDisplayNames.GetDescription(id, tags)));
        }

        // T1 mandatory B/C-style pillars (also used by hybrid)
        Tech(TechnologyId.ProductionI, ResearchTierIds.T1, 3, ItemId.SciencePackT1,
            [new AddModifierEffect(ResearchStatIds.FactoryWorkTicks, ModifierOperation.Multiply, 9_000)],
            "mandatory", "cycle");
        Tech(TechnologyId.EnergyI, ResearchTierIds.T1, 3, ItemId.SciencePackT1,
            [new AddModifierEffect(ResearchStatIds.EnergyShortagePenalty, ModifierOperation.Multiply, 8_000)],
            "mandatory", "cycle");
        Tech(TechnologyId.CommandI, ResearchTierIds.T1, 3, ItemId.SciencePackT1,
            [new AddModifierEffect(ResearchStatIds.ConstructionTicks, ModifierOperation.Multiply, 9_000)],
            "mandatory", "cycle");

        // Variant A T1 qualification projects (also used by B's optional pool)
        Tech(TechnologyId.ImprovedConveyors, ResearchTierIds.T1, 3, ItemId.SciencePackT1,
            [new AddModifierEffect(ResearchStatIds.ConveyorMoveTicks, ModifierOperation.Multiply, 7_000)],
            "optional", "qualification", "production", "tactical");
        Tech(new TechnologyId("technology.t1.power-reserve"), ResearchTierIds.T1, 3, ItemId.SciencePackT1,
            [new AddModifierEffect(ResearchStatIds.EnergyShortagePenalty, ModifierOperation.Multiply, 7_000)],
            "optional", "qualification", "infrastructure", "tactical");

        var bonuses = GameplayTablesCatalog.Embedded.ResearchBonuses;
        var groundAttackEffects = bonuses.GroundUnitAttackBonus
            .Where(pair => pair.Value > 0)
            .OrderBy(pair => pair.Key.ToString(), StringComparer.Ordinal)
            .Select(pair => (ResearchEffect)new AddModifierEffect(
                ResearchStatIds.AttackDamage,
                ModifierOperation.Add,
                pair.Value * ModifierResolver.BasisPointsScale,
                pair.Key.ToString()))
            .ToArray();
        var groundArmorEffects = bonuses.GroundUnitArmorBonus
            .Where(pair => pair.Value > 0)
            .OrderBy(pair => pair.Key.ToString(), StringComparer.Ordinal)
            .Select(pair => (ResearchEffect)new AddModifierEffect(
                ResearchStatIds.Armor,
                ModifierOperation.Add,
                pair.Value * ModifierResolver.BasisPointsScale,
                pair.Key.ToString()))
            .ToArray();

        // T1 optional combat/bastion bonuses (LightBot/Scout/Walls are T1 baseline unlocks).
        Tech(TechnologyId.LightBot, ResearchTierIds.T1, 3, ItemId.SciencePackT1,
            groundAttackEffects,
            "optional", "tactical");
        Tech(TechnologyId.Scout, ResearchTierIds.T1, 3, ItemId.SciencePackT1,
            groundArmorEffects,
            "optional", "tactical");
        Tech(TechnologyId.MachineGunTurret, ResearchTierIds.T1, 3, ItemId.SciencePackT1,
            [
                UnlockContentEffect.Entity(EntityKind.MachineGunTurret),
                new AddModifierEffect(ResearchStatIds.AttackDamage, ModifierOperation.Multiply, 11_000)
            ],
            "optional", "tactical");
        Tech(TechnologyId.ConcreteWalls, ResearchTierIds.T1, 2, ItemId.SciencePackT1,
            [new AddModifierEffect(ResearchStatIds.MaxBastions, ModifierOperation.Add, 10_000)],
            "optional", "tactical");
        Tech(new TechnologyId("technology.t1.forward-observer"), ResearchTierIds.T1, 2, ItemId.SciencePackT1,
            [new AddModifierEffect(ResearchStatIds.VisionRadius, ModifierOperation.Add, 10_000)],
            "optional", "tactical");
        Tech(new TechnologyId("technology.t1.field-repair"), ResearchTierIds.T1, 3, ItemId.SciencePackT1,
            [new GrantCapabilityEffect(ResearchCapabilityIds.RepairOutOfCombat)],
            "optional", "tactical");
        Tech(new TechnologyId("technology.t1.prefab-fortifications"), ResearchTierIds.T1, 2, ItemId.SciencePackT1,
            [
                new AddModifierEffect(ResearchStatIds.ConstructionTicks, ModifierOperation.Multiply, 8_000),
                new AddModifierEffect(ResearchStatIds.AttackCooldownTicks, ModifierOperation.Multiply, 9_000)
            ],
            "optional", "tactical");
        Tech(new TechnologyId("technology.t1.production-reserve"), ResearchTierIds.T1, 2, ItemId.SciencePackT1,
            [new AddModifierEffect(ResearchStatIds.HubStorageStacks, ModifierOperation.Add, 20_000)],
            "optional", "tactical");
        Tech(new TechnologyId("technology.t1.fast-regroup"), ResearchTierIds.T1, 2, ItemId.SciencePackT1,
            [new AddModifierEffect(ResearchStatIds.FactoryWorkTicks, ModifierOperation.Multiply, 9_000)],
            "optional", "tactical");

        // T1 doctrines
        Tech(new TechnologyId("technology.t1.doctrine.mobile-groups"), ResearchTierIds.T1, 3, ItemId.SciencePackT1,
            [new AddModifierEffect(ResearchStatIds.FactoryWorkTicks, ModifierOperation.Multiply, 9_000, EntityKind.LightBot.ToString())],
            "doctrine", "tactical", "optional");
        Tech(new TechnologyId("technology.t1.doctrine.fortified-line"), ResearchTierIds.T1, 3, ItemId.SciencePackT1,
            [new AddModifierEffect(ResearchStatIds.ConstructionTicks, ModifierOperation.Multiply, 8_000)],
            "doctrine", "tactical", "optional");

        // T2 mandatory B
        Tech(TechnologyId.MetallurgyII, ResearchTierIds.T2, 4, ItemId.SciencePackT2,
            [new AddModifierEffect(ResearchStatIds.SmelterWorkTicks, ModifierOperation.Multiply, 9_000)],
            "mandatory", "cycle");
        Tech(TechnologyId.SupplyII, ResearchTierIds.T2, 4, ItemId.SciencePackT2,
            [new AddModifierEffect(ResearchStatIds.ConveyorMoveTicks, ModifierOperation.Multiply, 8_500)],
            "mandatory", "cycle");
        Tech(TechnologyId.CommandII, ResearchTierIds.T2, 4, ItemId.SciencePackT2,
            [
                new AddModifierEffect(ResearchStatIds.ConstructionTicks, ModifierOperation.Multiply, 9_000),
                new AddModifierEffect(ResearchStatIds.BastionTemplateCapacity, ModifierOperation.Add, 60_000)
            ],
            "mandatory", "cycle");

        Tech(TechnologyId.UndergroundConveyors, ResearchTierIds.T2, 3, ItemId.SciencePackT2,
            [UnlockContentEffect.Entity(EntityKind.UndergroundConveyor)],
            "optional", "qualification", "logistics", "tactical");
        Tech(TechnologyId.ConstructionDrone, ResearchTierIds.T2, 3, ItemId.SciencePackT2,
            [new AddModifierEffect(ResearchStatIds.ConstructionTicks, ModifierOperation.Multiply, 7_500)],
            "optional", "qualification", "logistics", "tactical");
        Tech(TechnologyId.AdditionalBastions, ResearchTierIds.T2, 4, ItemId.SciencePackT2,
            [
                new GrantCapabilityEffect(ResearchCapabilityIds.AdditionalBastions),
                new AddModifierEffect(ResearchStatIds.BastionTemplateCapacity, ModifierOperation.Add, 20_000)
            ],
            "optional", "qualification", "command", "tactical");

        // T2 legacy unlocks / optionals
        Tech(TechnologyId.MediumBot, ResearchTierIds.T2, 4, ItemId.SciencePackT2,
            [UnlockContentEffect.Entity(EntityKind.MediumBot), UnlockContentEffect.Recipe(EntityKind.MediumBot.ToString())],
            "optional", "tactical");
        Tech(TechnologyId.MediumTank, ResearchTierIds.T2, 4, ItemId.SciencePackT2,
            [UnlockContentEffect.Entity(EntityKind.MediumTank), UnlockContentEffect.Recipe(EntityKind.MediumTank.ToString())],
            "optional", "tactical");
        Tech(TechnologyId.RocketLauncher, ResearchTierIds.T2, 4, ItemId.SciencePackT2,
            [UnlockContentEffect.Entity(EntityKind.RocketLauncher), UnlockContentEffect.Recipe(EntityKind.RocketLauncher.ToString())],
            "optional", "tactical");
        Tech(TechnologyId.AntiAirTurret, ResearchTierIds.T2, 3, ItemId.SciencePackT2,
            [
                UnlockContentEffect.Entity(EntityKind.AntiAirTurret),
                UnlockContentEffect.Entity(EntityKind.AntiAirBot),
                UnlockContentEffect.Recipe(EntityKind.AntiAirBot.ToString())
            ],
            "optional", "tactical");
        Tech(TechnologyId.SteelWalls, ResearchTierIds.T2, 3, ItemId.SciencePackT2,
            [UnlockContentEffect.Entity(EntityKind.SteelWall)],
            "optional", "tactical");
        Tech(new TechnologyId("technology.t2.predictive-aa"), ResearchTierIds.T2, 3, ItemId.SciencePackT2,
            [new AddModifierEffect(ResearchStatIds.VisionRadius, ModifierOperation.Add, 10_000)],
            "optional", "tactical");
        Tech(new TechnologyId("technology.t2.reinforced-hubs"), ResearchTierIds.T2, 3, ItemId.SciencePackT2,
            [new AddModifierEffect(ResearchStatIds.HubStorageStacks, ModifierOperation.Add, 40_000)],
            "optional", "tactical");
        Tech(new TechnologyId("technology.t2.mobile-repair"), ResearchTierIds.T2, 3, ItemId.SciencePackT2,
            [new GrantCapabilityEffect(ResearchCapabilityIds.RepairOutOfCombat)],
            "optional", "tactical");
        Tech(new TechnologyId("technology.t2.drone-coordination"), ResearchTierIds.T2, 3, ItemId.SciencePackT2,
            [new AddModifierEffect(ResearchStatIds.ConstructionTicks, ModifierOperation.Multiply, 8_000)],
            "optional", "tactical");
        Tech(new TechnologyId("technology.t2.ammo-priority"), ResearchTierIds.T2, 2, ItemId.SciencePackT2,
            [new AddModifierEffect(ResearchStatIds.FactoryWorkTicks, ModifierOperation.Multiply, 9_500)],
            "optional", "tactical");

        // T2 doctrines
        Tech(new TechnologyId("technology.t2.doctrine.armor-breakthrough"), ResearchTierIds.T2, 4, ItemId.SciencePackT2,
            [new AddModifierEffect(ResearchStatIds.FactoryWorkTicks, ModifierOperation.Multiply, 8_500, EntityKind.MediumTank.ToString())],
            "doctrine", "tactical", "optional");
        Tech(new TechnologyId("technology.t2.doctrine.ranged-pressure"), ResearchTierIds.T2, 4, ItemId.SciencePackT2,
            [new AddModifierEffect(ResearchStatIds.FactoryWorkTicks, ModifierOperation.Multiply, 8_500, EntityKind.RocketLauncher.ToString())],
            "doctrine", "tactical", "optional");

        return list.ToDictionary(tech => tech.Id);
    }

    private static IReadOnlyDictionary<string, TierDefinition> BuildTiers()
    {
        return new Dictionary<string, TierDefinition>
        {
            [ResearchTierIds.T1] = new(
                ResearchTierIds.T1,
                ItemId.SciencePackT1,
                [
                    UnlockContentEffect.Entity(EntityKind.Mine),
                    UnlockContentEffect.Entity(EntityKind.Smelter),
                    UnlockContentEffect.Entity(EntityKind.SolarPanel),
                    UnlockContentEffect.Entity(EntityKind.Assembler),
                    UnlockContentEffect.Entity(EntityKind.Conveyor),
                    UnlockContentEffect.Entity(EntityKind.Inserter),
                    UnlockContentEffect.Entity(EntityKind.Hub),
                    UnlockContentEffect.Entity(EntityKind.Laboratory),
                    UnlockContentEffect.Entity(EntityKind.TankFactory),
                    UnlockContentEffect.Entity(EntityKind.DroneCenter),
                    UnlockContentEffect.Entity(EntityKind.Bastion),
                    UnlockContentEffect.Entity(EntityKind.BasicTank),
                    UnlockContentEffect.Entity(EntityKind.LightBot),
                    UnlockContentEffect.Entity(EntityKind.Scout),
                    UnlockContentEffect.Entity(EntityKind.Wall),
                    UnlockContentEffect.Entity(EntityKind.CannonTurret),
                    UnlockContentEffect.Recipe(EntityKind.BasicTank.ToString()),
                    UnlockContentEffect.Recipe(EntityKind.LightBot.ToString()),
                    UnlockContentEffect.Recipe(EntityKind.Scout.ToString()),
                    UnlockContentEffect.ItemRecipe(ItemRecipeId.IronGear),
                    UnlockContentEffect.ItemRecipe(ItemRecipeId.Composite),
                    UnlockContentEffect.ItemRecipe(ItemRecipeId.SciencePackT1)
                ]),
            [ResearchTierIds.T2] = new(
                ResearchTierIds.T2,
                ItemId.SciencePackT2,
                [
                    UnlockContentEffect.Entity(EntityKind.CoalMine),
                    UnlockContentEffect.Entity(EntityKind.OilWell),
                    UnlockContentEffect.Entity(EntityKind.Refinery),
                    UnlockContentEffect.Entity(EntityKind.CoalPlant),
                    UnlockContentEffect.Entity(EntityKind.SteelWall),
                    UnlockContentEffect.ItemRecipe(ItemRecipeId.SciencePackT2),
                    new GrantCapabilityEffect(ResearchCapabilityIds.Tier2Content)
                ]),
            ["tier.t3-boundary"] = new(
                "tier.t3-boundary",
                ItemId.SciencePackT2,
                [])
        };
    }

    private static IReadOnlyDictionary<string, ResearchProfileDefinition> BuildProfiles()
    {
        var serialPrimary = new ResearchScheduleDefinition(
            [
                new ResearchTrackDefinition(ResearchTrackIds.Primary, ResearchTrackMode.Serial, 1, [], 100)
            ],
            new ResearchBudgetDefinition(10_000, new Dictionary<string, int> { [ResearchTrackIds.Primary] = 10_000 }, false));

        var t2ContentEffects = new ResearchEffect[]
        {
            UnlockContentEffect.Entity(EntityKind.CoalMine),
            UnlockContentEffect.Entity(EntityKind.OilWell),
            UnlockContentEffect.Entity(EntityKind.Refinery),
            UnlockContentEffect.Entity(EntityKind.CoalPlant),
            UnlockContentEffect.ItemRecipe(ItemRecipeId.SciencePackT2),
            new GrantCapabilityEffect(ResearchCapabilityIds.Tier2Content),
            new GrantCapabilityEffect(ResearchCapabilityIds.AdditionalBastions)
        };

        var t3Milestone = new ResearchEffect[]
        {
            new CompleteMilestoneEffect(ResearchMilestoneIds.T3Qualified)
        };

        var profileB = new ResearchProfileDefinition(
            ResearchProfileIds.MvpB,
            serialPrimary,
            new Dictionary<string, TierGateDefinition>
            {
                [ResearchTierIds.T1] = new(
                    "gate.b.t1-to-t2",
                    ResearchTierIds.T1,
                    ResearchTierIds.T2,
                    [
                        new GateRequirementDefinition("req.production", 1, [TechnologyId.ProductionI]),
                        new GateRequirementDefinition("req.energy", 1, [TechnologyId.EnergyI]),
                        new GateRequirementDefinition("req.command", 1, [TechnologyId.CommandI])
                    ],
                    t2ContentEffects),
                [ResearchTierIds.T2] = new(
                    "gate.b.t2-to-t3",
                    ResearchTierIds.T2,
                    null,
                    [
                        new GateRequirementDefinition("req.metallurgy", 1, [TechnologyId.MetallurgyII]),
                        new GateRequirementDefinition("req.supply", 1, [TechnologyId.SupplyII]),
                        new GateRequirementDefinition("req.command-ii", 1, [TechnologyId.CommandII])
                    ],
                    t3Milestone)
            },
            [
                new ExclusiveGroupDefinition(
                    "exclusive.b.t1-doctrine",
                    [
                        new TechnologyId("technology.t1.doctrine.mobile-groups"),
                        new TechnologyId("technology.t1.doctrine.fortified-line")
                    ],
                    1,
                    ExclusiveLockOn.Complete,
                    true),
                new ExclusiveGroupDefinition(
                    "exclusive.b.t2-doctrine",
                    [
                        new TechnologyId("technology.t2.doctrine.armor-breakthrough"),
                        new TechnologyId("technology.t2.doctrine.ranged-pressure")
                    ],
                    1,
                    ExclusiveLockOn.Complete,
                    true)
            ],
            [
                new OptionalPoolDefinition("pool.b.t1",
                [
                    TechnologyId.ImprovedConveyors,
                    new TechnologyId("technology.t1.power-reserve"),
                    new TechnologyId("technology.t1.forward-observer"),
                    TechnologyId.LightBot,
                    new TechnologyId("technology.t1.field-repair"),
                    new TechnologyId("technology.t1.prefab-fortifications"),
                    new TechnologyId("technology.t1.production-reserve"),
                    new TechnologyId("technology.t1.fast-regroup"),
                    TechnologyId.Scout,
                    TechnologyId.MachineGunTurret,
                    TechnologyId.ConcreteWalls,
                    new TechnologyId("technology.t1.doctrine.mobile-groups"),
                    new TechnologyId("technology.t1.doctrine.fortified-line")
                ]),
                new OptionalPoolDefinition("pool.b.t2",
                [
                    TechnologyId.SteelWalls,
                    TechnologyId.UndergroundConveyors,
                    TechnologyId.AdditionalBastions,
                    TechnologyId.ConstructionDrone,
                    TechnologyId.AntiAirTurret,
                    TechnologyId.MediumBot,
                    TechnologyId.MediumTank,
                    TechnologyId.RocketLauncher,
                    new TechnologyId("technology.t2.predictive-aa"),
                    new TechnologyId("technology.t2.reinforced-hubs"),
                    new TechnologyId("technology.t2.mobile-repair"),
                    new TechnologyId("technology.t2.drone-coordination"),
                    new TechnologyId("technology.t2.ammo-priority"),
                    new TechnologyId("technology.t2.doctrine.armor-breakthrough"),
                    new TechnologyId("technology.t2.doctrine.ranged-pressure")
                ])
            ],
            15_000);

        return new Dictionary<string, ResearchProfileDefinition>
        {
            [profileB.Id] = profileB
        };
    }
}

public static class ResearchContentHash
{
    public static string Compute(ResearchCatalog catalog)
    {
        var techIds = string.Join('|', catalog.Technologies.Keys.Select(id => id.Value).OrderBy(v => v, StringComparer.Ordinal));
        var profileIds = string.Join('|', catalog.Profiles.Keys.OrderBy(v => v, StringComparer.Ordinal));
        var payload = $"{techIds}#{profileIds}#{catalog.Tiers.Count}";
        var bytes = System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(payload));
        return Convert.ToHexString(bytes).ToLowerInvariant();
    }
}
