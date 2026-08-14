using System.Security.Cryptography;
using System.Text;
using SteelConveyorWar.Core.Commands;

namespace SteelConveyorWar.Core;

/// <summary>
/// Versioned deterministic fingerprint of authoritative simulation state.
/// Dual-run equality is the primary gate. Checked-in goldens remain deferred (#80) while the hash surface churns;
/// when added, refresh after intentional surface changes and bump <see cref="AlgorithmVersion"/> if needed.
/// Authoritative millitile positions are hashed as raw int64; numeric policy is ADR 0001.
/// </summary>
/// <remarks>
/// Presentation exclusion gate (must stay out of the hash surface):
/// <list type="bullet">
/// <item><see cref="SimulationPresentationSink"/> / <see cref="GameSimulation.CombatShotsThisTick"/></item>
/// <item><see cref="PlayerState.EnergyStats"/></item>
/// <item><see cref="PlayerState.TechSignatures"/></item>
/// </list>
/// Policy: docs/MVP_IMPLEMENTATION_DECISIONS.md § Presentation state in Core.
/// Guarded by <c>DeterminismHashTests.PresentationSideChannels_DoNotAffectStateHash</c>.
/// </remarks>
public static class SimulationStateHasher
{
    public const int AlgorithmVersion = 10;

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
            // R10: fold the full content manifest (research + build costs + entities + tiles) into the
            // fingerprint, not just the research hash, so peers with divergent gameplay catalogs mismatch
            // at tick 0 instead of silently diverging once a difference is first exercised.
            writer.Write(SimulationContentManifest.Compute(simulation));
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

            // Presentation-only PlayerState.EnergyStats / TechSignatures are intentionally omitted.
            foreach (var player in simulation.Players.OrderBy(player => player.Id.Value))
            {
                WritePlayer(writer, player, world.Size);
            }

            foreach (var entity in world.Entities.OrderBy(entity => entity.Id))
            {
                WriteEntity(writer, entity);
            }

            // Presentation-only SimulationPresentationSink / CombatShotsThisTick intentionally omitted.
        }

        var hash = SHA256.HashData(stream.ToArray());
        return Convert.ToHexString(hash).ToLowerInvariant();
    }

    /// <summary>
    /// R13: separate fingerprint of the <b>not-yet-applied</b> command queue. <see cref="Compute"/>
    /// hashes only authoritative state, so two peers can hold identical state but diverge on future
    /// ticks if their pending queues differ. This hash makes that divergence observable: peers compare
    /// <c>(ComputeStateHash, ComputePendingCommandsHash)</c> as the full session fingerprint.
    /// Commands are ordered canonically by <c>(Tick, Actor, Sequence)</c> — the same key the tick loop
    /// applies them in — so enqueue / network-arrival order does not affect the result. An empty queue
    /// hashes to a stable constant (the empty-payload digest), not to the state hash.
    /// </summary>
    public static string ComputePendingCommandsHash(GameSimulation simulation)
    {
        ArgumentNullException.ThrowIfNull(simulation);

        var ordered = simulation.PendingCommands
            .OrderBy(command => command.Tick)
            .ThenBy(command => command.Actor.Value)
            .ThenBy(command => command.Sequence)
            .ThenBy(command => command.Kind)
            .ThenBy(command => SimulationCommandSerializer.Serialize(command), StringComparer.Ordinal);

        using var stream = new MemoryStream();
        using (var writer = new BinaryWriter(stream, Encoding.UTF8, leaveOpen: true))
        {
            writer.Write(AlgorithmVersion);
            var count = 0;
            foreach (var command in ordered)
            {
                // Canonical JSON envelope (versioned, R11) is the wire-stable representation of the intent.
                writer.Write(SimulationCommandSerializer.Serialize(command));
                count++;
            }

            writer.Write(count);
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
        writer.Write(entity.WorldPosition.X);
        writer.Write(entity.WorldPosition.Y);
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
        writer.Write(entity.InserterLongReach);
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

        // R08: queued commander orders drive future ticks and must be part of the hash surface.
        var buildOrder = entity.QueuedBuildOrder;
        writer.Write(buildOrder is not null);
        if (buildOrder is not null)
        {
            writer.Write((int)buildOrder.TargetKind);
            writer.Write(buildOrder.TargetPosition.X);
            writer.Write(buildOrder.TargetPosition.Y);
            writer.Write((int)buildOrder.Direction);
            writer.Write(buildOrder.SelectedItemRecipe.HasValue);
            writer.Write(buildOrder.SelectedItemRecipe.HasValue ? (int)buildOrder.SelectedItemRecipe.Value : 0);
            writer.Write(buildOrder.InserterLongReach);
        }

        var demolishOrder = entity.QueuedDemolishOrder;
        writer.Write(demolishOrder is not null);
        if (demolishOrder is not null)
        {
            writer.Write(demolishOrder.TargetEntityId);
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

        // H02: largest-remainder schedulers are authoritative hidden state (not rebuildable from weights alone).
        writer.Write(research.TrackSelectionRemainder.Count);
        foreach (var pair in research.TrackSelectionRemainder.OrderBy(pair => pair.Key, StringComparer.Ordinal))
        {
            writer.Write(pair.Key);
            writer.Write(pair.Value);
        }

        writer.Write(research.ProjectSelectionRemainder.Count);
        foreach (var pair in research.ProjectSelectionRemainder.OrderBy(pair => pair.Key.Value, StringComparer.Ordinal))
        {
            writer.Write(pair.Key.Value);
            writer.Write(pair.Value);
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
