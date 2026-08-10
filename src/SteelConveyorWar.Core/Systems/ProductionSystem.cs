namespace SteelConveyorWar.Core;

public sealed partial class GameSimulation
{
    /// <summary>
    /// Raw resource extraction and building recipe work (smelter/refinery/assembler).
    /// </summary>
    private static class ProductionSystem
    {
        public static void Tick(GameSimulation sim)
        {
            sim.ProduceRawResources();
            sim.ProcessBuildingWork();
        }
    }

    private void ProduceRawResources()
    {
        foreach (var entity in World.Entities.Where(entity => entity.IsAlive).OrderBy(entity => entity.Id))
        {
            var terrain = World.GetTerrain(entity.Position);
            ItemId? product = entity.Kind switch
            {
                EntityKind.Mine when terrain == TerrainType.IronOre => ItemId.IronOre,
                EntityKind.Mine when terrain == TerrainType.CopperOre => ItemId.CopperOre,
                EntityKind.CoalMine when terrain == TerrainType.Coal => ItemId.Coal,
                EntityKind.OilWell when terrain == TerrainType.Oil => ItemId.CrudeOil,
                _ => null
            };

            if (product is null)
            {
                continue;
            }

            // Full output = idle (clear / don't advance ticks).
            if (entity.OutputBuffer.Count(product.Value) >= MvpDefinitions.GetMaxStackSize(product.Value))
            {
                entity.WorkTicksRemaining = 0;
                entity.WorkTicksTotal = 0;
                continue;
            }

            if (entity.WorkTicksRemaining <= 0)
            {
                var cycleTicks = entity.Kind switch
                {
                    EntityKind.Mine => MvpDefinitions.OreMineWorkTicks,
                    EntityKind.CoalMine => MvpDefinitions.CoalMineWorkTicks,
                    _ => MvpDefinitions.MineWorkTicks
                };
                entity.WorkTicksTotal = cycleTicks;
                entity.WorkTicksRemaining = cycleTicks;
            }

            // Drain each active work tick (same as smelters/assemblers); empty buffer pauses progress.
            if (!TryConsumeBuildingEnergy(entity))
            {
                continue;
            }

            entity.WorkTicksRemaining--;
            if (entity.WorkTicksRemaining > 0)
            {
                continue;
            }

            TryAddToBuffer(entity.OutputBuffer, product.Value, 1);
            entity.WorkTicksTotal = 0;
        }
    }

    private void ProcessBuildingWork()
    {
        foreach (var building in World.Entities.Where(entity => entity.IsAlive))
        {
            if (building.WorkTicksRemaining > 0 && building.PendingOutputItem is not null && !MvpDefinitions.FactoryKinds.Contains(building.Kind))
            {
                if (!TryConsumeBuildingEnergy(building))
                {
                    continue;
                }

                building.WorkTicksRemaining--;
                if (building.WorkTicksRemaining == 0)
                {
                    TryCompletePendingOutput(building);
                    if (building.WorkTicksRemaining == 0 && building.PendingOutputItem is null)
                    {
                        building.WorkTicksTotal = 0;
                    }
                }

                continue;
            }

            if (building.WorkTicksRemaining == 0 && building.PendingOutputItem is not null && !MvpDefinitions.FactoryKinds.Contains(building.Kind))
            {
                TryCompletePendingOutput(building);
                if (building.PendingOutputItem is null)
                {
                    building.WorkTicksTotal = 0;
                }

                continue;
            }

            if (building.WorkTicksRemaining > 0)
            {
                continue;
            }

            switch (building.Kind)
            {
                case EntityKind.Smelter:
                    TryStartSmelterRecipe(building);
                    break;
                case EntityKind.Refinery:
                    StartItemRecipe(building, ItemId.CrudeOil, ItemId.Fuel, 30);
                    break;
                case EntityKind.Assembler:
                    StartAssemblerRecipe(building);
                    break;
            }
        }
    }

    private void TryStartSmelterRecipe(WorldEntity smelter)
    {
        if (smelter.PendingOutputItem is not null)
        {
            return;
        }

        if (smelter.ActiveSmeltRecipe is { } sticky && TryBeginSmeltRecipe(smelter, sticky))
        {
            return;
        }

        foreach (var candidate in new[] { SmeltRecipeId.IronPlate, SmeltRecipeId.CopperPlate, SmeltRecipeId.Steel })
        {
            if (smelter.ActiveSmeltRecipe == candidate)
            {
                continue;
            }

            if (TryBeginSmeltRecipe(smelter, candidate))
            {
                return;
            }
        }
    }

    private bool TryBeginSmeltRecipe(WorldEntity smelter, SmeltRecipeId recipe)
    {
        switch (recipe)
        {
            case SmeltRecipeId.IronPlate:
                if (!smelter.InputBuffer.Has(ItemId.IronOre, 1))
                {
                    return false;
                }

                smelter.ActiveSmeltRecipe = SmeltRecipeId.IronPlate;
                StartItemRecipe(smelter, ItemId.IronOre, ItemId.IronPlate, 40);
                return smelter.PendingOutputItem is not null;

            case SmeltRecipeId.CopperPlate:
                if (!smelter.InputBuffer.Has(ItemId.CopperOre, 1))
                {
                    return false;
                }

                smelter.ActiveSmeltRecipe = SmeltRecipeId.CopperPlate;
                StartItemRecipe(smelter, ItemId.CopperOre, ItemId.CopperPlate, 40);
                return smelter.PendingOutputItem is not null;

            case SmeltRecipeId.Steel:
                if (!smelter.InputBuffer.Has(ItemId.IronPlate, 2) || !smelter.InputBuffer.Has(ItemId.Coal, 1))
                {
                    return false;
                }

                smelter.InputBuffer.TryRemove(ItemId.Coal, 1);
                smelter.InputBuffer.TryRemove(ItemId.IronPlate, 2);
                smelter.ActiveSmeltRecipe = SmeltRecipeId.Steel;
                smelter.PendingOutputItem = ItemId.Steel;
                smelter.PendingOutputAmount = 1;
                var steelTicks = 60;
                if (smelter.OwnerId is not null)
                {
                    steelTicks = ResolveStat(smelter.OwnerId.Value, ResearchStatIds.SmelterWorkTicks, steelTicks);
                }

                smelter.WorkTicksTotal = steelTicks;
                smelter.WorkTicksRemaining = steelTicks;
                return true;

            default:
                return false;
        }
    }

    private void StartItemRecipe(WorldEntity building, ItemId input, ItemId output, int workTicks)
    {
        if (building.PendingOutputItem is not null || !building.InputBuffer.TryRemove(input, 1))
        {
            return;
        }

        if (building.OwnerId is not null)
        {
            var statId = building.Kind == EntityKind.Smelter || building.Kind == EntityKind.Refinery
                ? ResearchStatIds.SmelterWorkTicks
                : ResearchStatIds.FactoryWorkTicks;
            workTicks = ResolveStat(building.OwnerId.Value, statId, workTicks);
        }

        building.PendingOutputItem = output;
        building.PendingOutputAmount = 1;
        building.WorkTicksTotal = workTicks;
        building.WorkTicksRemaining = workTicks;
    }

    private void StartAssemblerRecipe(WorldEntity assembler)
    {
        if (assembler.SelectedItemRecipe is null || !MvpDefinitions.ItemRecipes.TryGetValue(assembler.SelectedItemRecipe.Value, out var recipe))
        {
            return;
        }

        if (assembler.OwnerId is not null
            && !CapabilityResolver.IsItemRecipeUnlocked(GetPlayer(assembler.OwnerId.Value).Research, recipe.Id))
        {
            return;
        }

        if (assembler.PendingOutputItem is not null || !assembler.InputBuffer.TryRemoveAll(recipe.Inputs))
        {
            return;
        }

        var workTicks = recipe.WorkTicks;
        if (assembler.OwnerId is not null)
        {
            workTicks = ResolveStat(assembler.OwnerId.Value, ResearchStatIds.FactoryWorkTicks, workTicks);
        }

        assembler.PendingOutputItem = recipe.OutputItem;
        assembler.PendingOutputAmount = recipe.OutputAmount;
        assembler.WorkTicksTotal = workTicks;
        assembler.WorkTicksRemaining = workTicks;
    }

    private static bool TryCompletePendingOutput(WorldEntity building)
    {
        if (building.PendingOutputItem is null || !TryAddToBuffer(building.OutputBuffer, building.PendingOutputItem.Value, building.PendingOutputAmount))
        {
            return false;
        }

        building.PendingOutputItem = null;
        building.PendingOutputAmount = 1;
        return true;
    }
}
