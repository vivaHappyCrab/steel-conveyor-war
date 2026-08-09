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
                new ResearchCostDefinition(effort, [new SciencePackCost(pack, 1)]),
                effects.ToList(),
                tags));
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

        // Variant C T1 mandatory aliases
        Tech(new TechnologyId("technology.t1.automated-base"), ResearchTierIds.T1, 3, ItemId.SciencePackT1,
            [new AddModifierEffect(ResearchStatIds.FactoryWorkTicks, ModifierOperation.Multiply, 9_000)],
            "mandatory", "cycle");
        Tech(new TechnologyId("technology.t1.distant-expedition"), ResearchTierIds.T1, 3, ItemId.SciencePackT1,
            [new AddModifierEffect(ResearchStatIds.ConstructionTicks, ModifierOperation.Multiply, 9_000)],
            "mandatory", "cycle");
        Tech(new TechnologyId("technology.t1.new-resource-mastery"), ResearchTierIds.T1, 3, ItemId.SciencePackT1,
            [new GrantCapabilityEffect(ResearchCapabilityIds.Tier2Content)],
            "mandatory", "cycle");

        // Variant A T1 qualification projects
        Tech(TechnologyId.ImprovedConveyors, ResearchTierIds.T1, 3, ItemId.SciencePackT1,
            [new AddModifierEffect(ResearchStatIds.ConveyorMoveTicks, ModifierOperation.Multiply, 7_000)],
            "optional", "qualification", "production", "tactical");
        Tech(new TechnologyId("technology.t1.mass-production"), ResearchTierIds.T1, 3, ItemId.SciencePackT1,
            [new AddModifierEffect(ResearchStatIds.FactoryWorkTicks, ModifierOperation.Multiply, 8_500)],
            "optional", "qualification", "production", "tactical");
        Tech(new TechnologyId("technology.t1.distributed-energy"), ResearchTierIds.T1, 3, ItemId.SciencePackT1,
            [new AddModifierEffect(ResearchStatIds.EnergyShortagePenalty, ModifierOperation.Multiply, 7_500)],
            "optional", "qualification", "infrastructure", "tactical");
        Tech(new TechnologyId("technology.t1.power-reserve"), ResearchTierIds.T1, 3, ItemId.SciencePackT1,
            [new AddModifierEffect(ResearchStatIds.EnergyShortagePenalty, ModifierOperation.Multiply, 7_000)],
            "optional", "qualification", "infrastructure", "tactical");
        Tech(new TechnologyId("technology.t1.expedition-logistics"), ResearchTierIds.T1, 3, ItemId.SciencePackT1,
            [new AddModifierEffect(ResearchStatIds.ConstructionTicks, ModifierOperation.Multiply, 8_000)],
            "optional", "qualification", "field", "tactical");
        Tech(new TechnologyId("technology.t1.defensive-engineering"), ResearchTierIds.T1, 3, ItemId.SciencePackT1,
            [new AddModifierEffect(ResearchStatIds.ConstructionTicks, ModifierOperation.Multiply, 7_500)],
            "optional", "qualification", "field", "tactical");

        // Legacy / expansion optionals T1
        Tech(TechnologyId.LightBot, ResearchTierIds.T1, 3, ItemId.SciencePackT1,
            [UnlockContentEffect.Entity(EntityKind.LightBot)],
            "optional", "tactical");
        Tech(TechnologyId.Scout, ResearchTierIds.T1, 3, ItemId.SciencePackT1,
            [
                UnlockContentEffect.Entity(EntityKind.Scout),
                UnlockContentEffect.Recipe(EntityKind.Scout.ToString())
            ],
            "optional", "tactical");
        Tech(TechnologyId.MachineGunTurret, ResearchTierIds.T1, 3, ItemId.SciencePackT1,
            [UnlockContentEffect.Entity(EntityKind.MachineGunTurret)],
            "optional", "tactical");
        Tech(TechnologyId.ConcreteWalls, ResearchTierIds.T1, 2, ItemId.SciencePackT1,
            [UnlockContentEffect.Entity(EntityKind.Wall)],
            "optional", "tactical");
        Tech(new TechnologyId("technology.t1.forward-observer"), ResearchTierIds.T1, 2, ItemId.SciencePackT1,
            [new AddModifierEffect(ResearchStatIds.VisionRadius, ModifierOperation.Add, 10_000)],
            "optional", "tactical");
        Tech(new TechnologyId("technology.t1.field-repair"), ResearchTierIds.T1, 3, ItemId.SciencePackT1,
            [new GrantCapabilityEffect(ResearchCapabilityIds.RepairOutOfCombat)],
            "optional", "tactical");
        Tech(new TechnologyId("technology.t1.prefab-fortifications"), ResearchTierIds.T1, 2, ItemId.SciencePackT1,
            [new AddModifierEffect(ResearchStatIds.ConstructionTicks, ModifierOperation.Multiply, 8_000)],
            "optional", "tactical");
        Tech(new TechnologyId("technology.t1.production-reserve"), ResearchTierIds.T1, 2, ItemId.SciencePackT1,
            [new AddModifierEffect(ResearchStatIds.HubStorageStacks, ModifierOperation.Add, 20_000)],
            "optional", "tactical");
        Tech(new TechnologyId("technology.t1.fast-regroup"), ResearchTierIds.T1, 2, ItemId.SciencePackT1,
            [new AddModifierEffect(ResearchStatIds.FactoryWorkTicks, ModifierOperation.Multiply, 9_000)],
            "optional", "tactical");
        Tech(new TechnologyId("technology.t1.conveyor-telemetry"), ResearchTierIds.T1, 2, ItemId.SciencePackT1,
            [new AddModifierEffect(ResearchStatIds.ConveyorMoveTicks, ModifierOperation.Multiply, 8_500)],
            "optional", "tactical");
        Tech(new TechnologyId("technology.t1.energy-reserve"), ResearchTierIds.T1, 2, ItemId.SciencePackT1,
            [new AddModifierEffect(ResearchStatIds.EnergyShortagePenalty, ModifierOperation.Multiply, 7_500)],
            "optional", "tactical");
        Tech(new TechnologyId("technology.t1.modular-tooling"), ResearchTierIds.T1, 2, ItemId.SciencePackT1,
            [new AddModifierEffect(ResearchStatIds.FactoryWorkTicks, ModifierOperation.Multiply, 9_000)],
            "optional", "tactical");
        Tech(new TechnologyId("technology.t1.recon-archive"), ResearchTierIds.T1, 2, ItemId.SciencePackT1,
            [new AddModifierEffect(ResearchStatIds.VisionRadius, ModifierOperation.Add, 5_000)],
            "optional", "tactical");
        Tech(new TechnologyId("technology.t1.hub-buffering"), ResearchTierIds.T1, 2, ItemId.SciencePackT1,
            [new AddModifierEffect(ResearchStatIds.HubStorageStacks, ModifierOperation.Add, 30_000)],
            "optional", "tactical");
        Tech(new TechnologyId("technology.t1.return-protocol"), ResearchTierIds.T1, 2, ItemId.SciencePackT1,
            [new AddModifierEffect(ResearchStatIds.ConstructionTicks, ModifierOperation.Multiply, 9_000)],
            "optional", "tactical");

        // T1 doctrines
        Tech(new TechnologyId("technology.t1.doctrine.mobile-groups"), ResearchTierIds.T1, 3, ItemId.SciencePackT1,
            [new AddModifierEffect(ResearchStatIds.FactoryWorkTicks, ModifierOperation.Multiply, 9_000, EntityKind.LightBot.ToString())],
            "doctrine", "tactical", "optional");
        Tech(new TechnologyId("technology.t1.doctrine.fortified-line"), ResearchTierIds.T1, 3, ItemId.SciencePackT1,
            [new AddModifierEffect(ResearchStatIds.ConstructionTicks, ModifierOperation.Multiply, 8_000)],
            "doctrine", "tactical", "optional");
        Tech(new TechnologyId("technology.t1.doctrine.swarm"), ResearchTierIds.T1, 3, ItemId.SciencePackT1,
            [new AddModifierEffect(ResearchStatIds.FactoryWorkTicks, ModifierOperation.Multiply, 8_500, EntityKind.LightBot.ToString())],
            "doctrine", "tactical", "optional");
        Tech(new TechnologyId("technology.t1.doctrine.observation"), ResearchTierIds.T1, 3, ItemId.SciencePackT1,
            [new AddModifierEffect(ResearchStatIds.VisionRadius, ModifierOperation.Add, 20_000)],
            "doctrine", "tactical", "optional");
        Tech(new TechnologyId("technology.t1.doctrine.fortification"), ResearchTierIds.T1, 3, ItemId.SciencePackT1,
            [new AddModifierEffect(ResearchStatIds.ConstructionTicks, ModifierOperation.Multiply, 7_500)],
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

        // T2 C mandatory
        Tech(new TechnologyId("technology.t2.steel-standardization"), ResearchTierIds.T2, 4, ItemId.SciencePackT2,
            [new AddModifierEffect(ResearchStatIds.SmelterWorkTicks, ModifierOperation.Multiply, 8_500)],
            "mandatory", "cycle", "qualification", "industry");
        Tech(new TechnologyId("technology.t2.fuel-logistics"), ResearchTierIds.T2, 4, ItemId.SciencePackT2,
            [new AddModifierEffect(ResearchStatIds.FactoryWorkTicks, ModifierOperation.Multiply, 9_000)],
            "mandatory", "cycle");
        Tech(new TechnologyId("technology.t2.field-bastion-network"), ResearchTierIds.T2, 4, ItemId.SciencePackT2,
            [new GrantCapabilityEffect(ResearchCapabilityIds.AdditionalBastions)],
            "mandatory", "cycle", "qualification", "command");

        // T2 A qualification extras
        Tech(new TechnologyId("technology.t2.fuel-intensification"), ResearchTierIds.T2, 4, ItemId.SciencePackT2,
            [new AddModifierEffect(ResearchStatIds.FactoryWorkTicks, ModifierOperation.Multiply, 8_500)],
            "optional", "qualification", "industry", "tactical");
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
        Tech(new TechnologyId("technology.t2.auto-resupply"), ResearchTierIds.T2, 4, ItemId.SciencePackT2,
            [new AddModifierEffect(ResearchStatIds.FactoryWorkTicks, ModifierOperation.Multiply, 8_500)],
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
        Tech(new TechnologyId("technology.t2.doctrine.armor-fist"), ResearchTierIds.T2, 4, ItemId.SciencePackT2,
            [new AddModifierEffect(ResearchStatIds.FactoryWorkTicks, ModifierOperation.Multiply, 8_000, EntityKind.MediumTank.ToString())],
            "doctrine", "tactical", "optional");
        Tech(new TechnologyId("technology.t2.doctrine.maneuver-net"), ResearchTierIds.T2, 4, ItemId.SciencePackT2,
            [
                new GrantCapabilityEffect(ResearchCapabilityIds.AdditionalBastions),
                new AddModifierEffect(ResearchStatIds.MaxBastions, ModifierOperation.Add, 20_000)
            ],
            "doctrine", "tactical", "optional");
        Tech(new TechnologyId("technology.t2.doctrine.siege-control"), ResearchTierIds.T2, 4, ItemId.SciencePackT2,
            [new AddModifierEffect(ResearchStatIds.FactoryWorkTicks, ModifierOperation.Multiply, 8_000, EntityKind.RocketLauncher.ToString())],
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
                    UnlockContentEffect.Entity(EntityKind.CannonTurret),
                    UnlockContentEffect.Recipe(EntityKind.BasicTank.ToString()),
                    UnlockContentEffect.ItemRecipe(ItemRecipeId.IronGear),
                    UnlockContentEffect.ItemRecipe(ItemRecipeId.CopperWire),
                    UnlockContentEffect.ItemRecipe(ItemRecipeId.Circuit),
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

        var dualTrack = new ResearchScheduleDefinition(
            [
                new ResearchTrackDefinition(ResearchTrackIds.Cycle, ResearchTrackMode.WeightedParallel, 3, ["mandatory", "cycle"], 100),
                new ResearchTrackDefinition(ResearchTrackIds.Tactical, ResearchTrackMode.Serial, 1, ["optional", "tactical", "doctrine", "qualification"], 100)
            ],
            new ResearchBudgetDefinition(
                10_000,
                new Dictionary<string, int>
                {
                    [ResearchTrackIds.Cycle] = 7_000,
                    [ResearchTrackIds.Tactical] = 3_000
                },
                true));

        var t2ContentEffects = new ResearchEffect[]
        {
            UnlockContentEffect.Entity(EntityKind.CoalMine),
            UnlockContentEffect.Entity(EntityKind.OilWell),
            UnlockContentEffect.Entity(EntityKind.Refinery),
            UnlockContentEffect.Entity(EntityKind.CoalPlant),
            UnlockContentEffect.ItemRecipe(ItemRecipeId.SciencePackT2),
            new GrantCapabilityEffect(ResearchCapabilityIds.Tier2Content)
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

        var profileA = new ResearchProfileDefinition(
            ResearchProfileIds.MvpA,
            serialPrimary,
            new Dictionary<string, TierGateDefinition>
            {
                [ResearchTierIds.T1] = new(
                    "gate.a.t1-to-t2",
                    ResearchTierIds.T1,
                    ResearchTierIds.T2,
                    [
                        new GateRequirementDefinition("req.production", 1,
                        [
                            TechnologyId.ImprovedConveyors,
                            new TechnologyId("technology.t1.mass-production")
                        ]),
                        new GateRequirementDefinition("req.infrastructure", 1,
                        [
                            new TechnologyId("technology.t1.distributed-energy"),
                            new TechnologyId("technology.t1.power-reserve")
                        ]),
                        new GateRequirementDefinition("req.field", 1,
                        [
                            new TechnologyId("technology.t1.expedition-logistics"),
                            new TechnologyId("technology.t1.defensive-engineering")
                        ])
                    ],
                    t2ContentEffects),
                [ResearchTierIds.T2] = new(
                    "gate.a.t2-to-t3",
                    ResearchTierIds.T2,
                    null,
                    [
                        new GateRequirementDefinition("req.industry", 1,
                        [
                            new TechnologyId("technology.t2.steel-standardization"),
                            new TechnologyId("technology.t2.fuel-intensification")
                        ]),
                        new GateRequirementDefinition("req.logistics", 1,
                        [
                            TechnologyId.UndergroundConveyors,
                            TechnologyId.ConstructionDrone
                        ]),
                        new GateRequirementDefinition("req.command", 1,
                        [
                            TechnologyId.AdditionalBastions,
                            new TechnologyId("technology.t2.auto-resupply")
                        ])
                    ],
                    t3Milestone)
            },
            Array.Empty<ExclusiveGroupDefinition>(),
            [
                new OptionalPoolDefinition("pool.a.t1",
                [
                    TechnologyId.LightBot,
                    TechnologyId.Scout,
                    TechnologyId.MachineGunTurret,
                    TechnologyId.ConcreteWalls,
                    new TechnologyId("technology.t1.field-repair"),
                    new TechnologyId("technology.t1.modular-tooling"),
                    new TechnologyId("technology.t1.hub-buffering"),
                    new TechnologyId("technology.t1.fast-regroup")
                ]),
                new OptionalPoolDefinition("pool.a.t2",
                [
                    TechnologyId.MediumBot,
                    TechnologyId.MediumTank,
                    TechnologyId.RocketLauncher,
                    TechnologyId.AntiAirTurret,
                    TechnologyId.SteelWalls,
                    new TechnologyId("technology.t2.predictive-aa"),
                    new TechnologyId("technology.t2.mobile-repair"),
                    new TechnologyId("technology.t2.reinforced-hubs")
                ])
            ],
            15_000);

        var profileC = new ResearchProfileDefinition(
            ResearchProfileIds.MvpC,
            dualTrack,
            new Dictionary<string, TierGateDefinition>
            {
                [ResearchTierIds.T1] = new(
                    "gate.c.t1-to-t2",
                    ResearchTierIds.T1,
                    ResearchTierIds.T2,
                    [
                        new GateRequirementDefinition("req.auto", 1, [new TechnologyId("technology.t1.automated-base")]),
                        new GateRequirementDefinition("req.expedition", 1, [new TechnologyId("technology.t1.distant-expedition")]),
                        new GateRequirementDefinition("req.resources", 1, [new TechnologyId("technology.t1.new-resource-mastery")])
                    ],
                    t2ContentEffects),
                [ResearchTierIds.T2] = new(
                    "gate.c.t2-to-t3",
                    ResearchTierIds.T2,
                    null,
                    [
                        new GateRequirementDefinition("req.steel", 1, [new TechnologyId("technology.t2.steel-standardization")]),
                        new GateRequirementDefinition("req.fuel", 1, [new TechnologyId("technology.t2.fuel-logistics")]),
                        new GateRequirementDefinition("req.bastion", 1, [new TechnologyId("technology.t2.field-bastion-network")])
                    ],
                    t3Milestone)
            },
            [
                new ExclusiveGroupDefinition(
                    "exclusive.c.t1-doctrine",
                    [
                        new TechnologyId("technology.t1.doctrine.swarm"),
                        new TechnologyId("technology.t1.doctrine.observation"),
                        new TechnologyId("technology.t1.doctrine.fortification")
                    ],
                    1,
                    ExclusiveLockOn.Start,
                    true),
                new ExclusiveGroupDefinition(
                    "exclusive.c.t2-doctrine",
                    [
                        new TechnologyId("technology.t2.doctrine.armor-fist"),
                        new TechnologyId("technology.t2.doctrine.maneuver-net"),
                        new TechnologyId("technology.t2.doctrine.siege-control")
                    ],
                    1,
                    ExclusiveLockOn.Start,
                    true)
            ],
            [
                new OptionalPoolDefinition("pool.c.t1",
                [
                    new TechnologyId("technology.t1.conveyor-telemetry"),
                    new TechnologyId("technology.t1.energy-reserve"),
                    new TechnologyId("technology.t1.modular-tooling"),
                    new TechnologyId("technology.t1.field-repair"),
                    new TechnologyId("technology.t1.recon-archive"),
                    new TechnologyId("technology.t1.prefab-fortifications"),
                    new TechnologyId("technology.t1.hub-buffering"),
                    new TechnologyId("technology.t1.return-protocol"),
                    TechnologyId.LightBot,
                    TechnologyId.Scout,
                    TechnologyId.MachineGunTurret,
                    TechnologyId.ConcreteWalls,
                    new TechnologyId("technology.t1.doctrine.swarm"),
                    new TechnologyId("technology.t1.doctrine.observation"),
                    new TechnologyId("technology.t1.doctrine.fortification")
                ]),
                new OptionalPoolDefinition("pool.c.t2",
                [
                    TechnologyId.UndergroundConveyors,
                    TechnologyId.ConstructionDrone,
                    TechnologyId.AntiAirTurret,
                    TechnologyId.SteelWalls,
                    TechnologyId.MediumBot,
                    TechnologyId.MediumTank,
                    TechnologyId.RocketLauncher,
                    new TechnologyId("technology.t2.predictive-aa"),
                    new TechnologyId("technology.t2.reinforced-hubs"),
                    new TechnologyId("technology.t2.ammo-priority"),
                    new TechnologyId("technology.t2.doctrine.armor-fist"),
                    new TechnologyId("technology.t2.doctrine.maneuver-net"),
                    new TechnologyId("technology.t2.doctrine.siege-control")
                ])
            ],
            15_000);

        var hybrid = profileA with
        {
            Id = ResearchProfileIds.HybridAC,
            Schedule = dualTrack,
            ExclusiveGroups = profileC.ExclusiveGroups,
            OptionalPools = profileC.OptionalPools
        };

        return new Dictionary<string, ResearchProfileDefinition>
        {
            [profileA.Id] = profileA,
            [profileB.Id] = profileB,
            [profileC.Id] = profileC,
            [hybrid.Id] = hybrid
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
