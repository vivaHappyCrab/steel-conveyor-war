namespace SteelConveyorWar.Core;

public sealed partial class GameSimulation
{
    /// <summary>
    /// Advances research progress and syncs max-health after completions.
    /// </summary>
    private static class ResearchTickSystem
    {
        public static void Tick(GameSimulation sim)
        {
            sim.ProcessResearch();
        }
    }

    private void ProcessResearch()
    {
        _researchSystem.ProcessResearch(this, Tick);
        // MaxHealth modifiers only land on completion; sync caps/HP for living entities.
        SyncAllResolvedMaxHealth();
    }
}
