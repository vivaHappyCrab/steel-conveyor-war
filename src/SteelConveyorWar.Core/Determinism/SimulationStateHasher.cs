using System.Security.Cryptography;
using System.Text;

namespace SteelConveyorWar.Core;

/// <summary>
/// Versioned deterministic fingerprint of authoritative simulation state.
/// Dual-run equality is the primary gate. Checked-in goldens remain deferred (#80) while the hash surface churns;
/// when added, refresh after intentional surface changes and bump <see cref="AlgorithmVersion"/> if needed.
/// Authoritative doubles are hashed as IEEE bits; numeric policy for remaining FP is ADR 0001.
/// </summary>
public static class SimulationStateHasher
{
    public const int AlgorithmVersion = 5;

    public static string Compute(GameSimulation simulation)
    {
        ArgumentNullException.ThrowIfNull(simulation);

        using var stream = new MemoryStream();
        using (var writer = new BinaryWriter(stream, Encoding.UTF8, leaveOpen: true))
        {
            writer.Write(AlgorithmVersion);
            writer.Write(simulation.RandomSeed);
            writer.Write(simulation.Tick);
            writer.Write((int)simulation.Status);
            writer.Write(simulation.WinnerId.HasValue);
            writer.Write(simulation.WinnerId?.Value ?? 0);
            writer.Write(simulation.ResearchCatalog.ContentHash);
            writer.Write(simulation.ResearchProfile.Id);
            writer.Write(simulation.NextEntityId);

            var world = simulation.World;
            writer.Write(world.Size.Width);
            writer.Write(world.Size.Height);
            for (var y = 0; y < world.Size.Height; y++)
            {
                for (var x = 0; x < world.Size.Width; x++)
                {
                    writer.Write((int)world.GetTerrain(new TilePosition(x, y)));
                }
            }

            foreach (var player in simulation.Players.OrderBy(player => player.Id.Value))
            {
                WritePlayer(writer, player, world.Size);
            }

            foreach (var entity in world.Entities.OrderBy(entity => entity.Id))
            {
                WriteEntity(writer, entity);
            }
        }

        var hash = SHA256.HashData(stream.ToArray());
        return Convert.ToHexString(hash).ToLowerInvariant();
    }

    private static void WritePlayer(BinaryWriter writer, PlayerState player, WorldSize size)
    {
        writer.Write(player.Id.Value);
        writer.Write(player.Name);
        writer.Write(player.TeamId);
        writer.Write(player.IsDefeated);
        writer.Write(player.PowerProduced);
        writer.Write(player.PowerDemand);
        WriteInventory(writer, player.Inventory);

        for (var y = 0; y < size.Height; y++)
        {
            for (var x = 0; x < size.Width; x++)
            {
                writer.Write((int)player.GetVisibility(new TilePosition(x, y)));
            }
        }

        WriteResearch(writer, player.Research);
    }

    private static void WriteEntity(BinaryWriter writer, WorldEntity entity)
    {
        writer.Write(entity.Id);
        writer.Write((int)entity.Kind);
        writer.Write(entity.OwnerId.HasValue);
        writer.Write(entity.OwnerId?.Value ?? 0);
        writer.Write(entity.Position.X);
        writer.Write(entity.Position.Y);
        writer.Write(BitConverter.DoubleToInt64Bits(entity.WorldPosition.X));
        writer.Write(BitConverter.DoubleToInt64Bits(entity.WorldPosition.Y));
        writer.Write((int)entity.Direction);
        writer.Write(entity.MaxHealth);
        writer.Write(entity.Health);
        WriteInventory(writer, entity.Inventory);
        WriteInventory(writer, entity.InputBuffer);
        WriteInventory(writer, entity.OutputBuffer);

        writer.Write(entity.ConveyorItems.Count);
        foreach (var item in entity.ConveyorItems)
        {
            writer.Write((int)item.Item);
            writer.Write(item.ProgressTicks);
        }

        writer.Write(entity.HeldItem.HasValue);
        writer.Write(entity.HeldItem.HasValue ? (int)entity.HeldItem.Value : 0);
        writer.Write(entity.HeldTransferTicksRemaining);
        writer.Write(entity.BuildTargetKind.HasValue);
        writer.Write(entity.BuildTargetKind.HasValue ? (int)entity.BuildTargetKind.Value : 0);
        writer.Write(entity.ConstructionTicksRemaining);
        writer.Write(entity.ProductionTargetKind.HasValue);
        writer.Write(entity.ProductionTargetKind.HasValue ? (int)entity.ProductionTargetKind.Value : 0);
        writer.Write(entity.IsManualProductionTarget);
        writer.Write(entity.PendingOutputItem.HasValue);
        writer.Write(entity.PendingOutputItem.HasValue ? (int)entity.PendingOutputItem.Value : 0);
        writer.Write(entity.PendingOutputAmount);
        writer.Write(entity.WorkTicksRemaining);
        writer.Write(entity.WorkTicksTotal);
        writer.Write(entity.AttackCooldownRemaining);
        writer.Write(entity.EnergyBuffer);
        writer.Write(entity.EnergyBufferCapacity);
        writer.Write(entity.ActiveSmeltRecipe.HasValue);
        writer.Write(entity.ActiveSmeltRecipe.HasValue ? (int)entity.ActiveSmeltRecipe.Value : 0);
        writer.Write(entity.FilterItem.HasValue);
        writer.Write(entity.FilterItem.HasValue ? (int)entity.FilterItem.Value : 0);
        writer.Write(entity.AssignedBastionId.HasValue);
        writer.Write(entity.AssignedBastionId ?? 0);
        writer.Write(entity.IsGarrisoned);
        writer.Write(entity.SelectedItemRecipe.HasValue);
        writer.Write(entity.SelectedItemRecipe.HasValue ? (int)entity.SelectedItemRecipe.Value : 0);
        writer.Write((int)entity.Order.Kind);
        writer.Write(entity.Order.Target.HasValue);
        writer.Write(entity.Order.Target?.X ?? 0);
        writer.Write(entity.Order.Target?.Y ?? 0);
        writer.Write(entity.Order.WaypointIndex);
        writer.Write(entity.Order.WaypointList.Count);
        foreach (var waypoint in entity.Order.WaypointList)
        {
            writer.Write(waypoint.X);
            writer.Write(waypoint.Y);
        }
        writer.Write(entity.MoveTarget.HasValue);
        writer.Write(entity.MoveTarget?.X ?? 0);
        writer.Write(entity.MoveTarget?.Y ?? 0);
        writer.Write(entity.CurrentWaypoint.HasValue);
        writer.Write(entity.CurrentWaypoint?.X ?? 0);
        writer.Write(entity.CurrentWaypoint?.Y ?? 0);

        writer.Write(entity.MovementPath.Count);
        foreach (var step in entity.MovementPath)
        {
            writer.Write(step.X);
            writer.Write(step.Y);
        }

        writer.Write(entity.BastionTemplate.Count);
        foreach (var pair in entity.BastionTemplate.OrderBy(pair => (int)pair.Key))
        {
            writer.Write((int)pair.Key);
            writer.Write(pair.Value);
        }
    }

    private static void WriteResearch(BinaryWriter writer, PlayerResearchState research)
    {
        writer.Write(research.CurrentTierId);
        WriteOrderedStrings(writer, research.CompletedTechnologies.Select(id => id.Value));
        WriteOrderedStrings(writer, research.LockedTechnologies.Select(id => id.Value));
        WriteOrderedStrings(writer, research.ConfirmedExclusiveGroups);
        WriteOrderedStrings(writer, research.CompletedGateIds);
        WriteOrderedStrings(writer, research.Milestones);
        WriteOrderedStrings(writer, research.AppliedCapabilities);
        WriteOrderedStrings(writer, research.UnlockedEntityKinds);
        WriteOrderedStrings(writer, research.UnlockedRecipes);
        WriteOrderedStrings(writer, research.UnlockedItemRecipes);

        writer.Write(research.ProgressWorkUnits.Count);
        foreach (var pair in research.ProgressWorkUnits.OrderBy(pair => pair.Key.Value, StringComparer.Ordinal))
        {
            writer.Write(pair.Key.Value);
            writer.Write(pair.Value);
        }

        writer.Write(research.Tracks.Count);
        foreach (var track in research.Tracks.OrderBy(pair => pair.Key, StringComparer.Ordinal))
        {
            writer.Write(track.Key);
            writer.Write(track.Value.ActiveSerialTarget.HasValue);
            writer.Write(track.Value.ActiveSerialTarget?.Value ?? string.Empty);
            writer.Write(track.Value.AllocationBasisPoints);
            writer.Write(track.Value.ProjectWeights.Count);
            foreach (var weight in track.Value.ProjectWeights.OrderBy(pair => pair.Key.Value, StringComparer.Ordinal))
            {
                writer.Write(weight.Key.Value);
                writer.Write(weight.Value);
            }
        }

        writer.Write(research.AppliedModifiers.Count);
        foreach (var modifier in research.AppliedModifiers)
        {
            writer.Write(modifier.StatId);
            writer.Write((int)modifier.Operation);
            writer.Write(modifier.ValueBasisPoints);
            writer.Write(modifier.Selector ?? string.Empty);
        }
    }

    private static void WriteInventory(BinaryWriter writer, Inventory inventory)
    {
        writer.Write(inventory.Items.Count);
        foreach (var pair in inventory.Items.OrderBy(pair => (int)pair.Key))
        {
            writer.Write((int)pair.Key);
            writer.Write(pair.Value);
        }
    }

    private static void WriteOrderedStrings(BinaryWriter writer, IEnumerable<string> values)
    {
        var ordered = values.OrderBy(value => value, StringComparer.Ordinal).ToList();
        writer.Write(ordered.Count);
        foreach (var value in ordered)
        {
            writer.Write(value);
        }
    }
}
