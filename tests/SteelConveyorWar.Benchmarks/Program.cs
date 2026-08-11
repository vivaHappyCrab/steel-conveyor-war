using System.Globalization;
using System.Text.Json;
using BenchmarkDotNet.Configs;
using BenchmarkDotNet.Exporters.Json;
using BenchmarkDotNet.Running;
using SteelConveyorWar.Benchmarks;

var argsList = args.ToList();
var jsonOut = TakeOption(argsList, "--json-out") ?? "artifacts/benchmarks/results.json";
var quick = argsList.RemoveAll(a => a is "--quick" or "-q") > 0
            || string.Equals(Environment.GetEnvironmentVariable("SCW_BENCH_QUICK"), "1", StringComparison.Ordinal);

Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(jsonOut))!);

if (quick)
{
    // Alloc smoke / CI soft gate: measure tick ns + alloc without BenchmarkDotNet warm-up cost.
    var report = TickScenarioRunner.RunQuickMatrix();
    File.WriteAllText(jsonOut, JsonSerializer.Serialize(report, new JsonSerializerOptions { WriteIndented = true }));
    Console.WriteLine($"Wrote quick benchmark report to {jsonOut}");
    var failed = report.Scenarios.Where(s => s.ExceededSoftBudget).ToList();
    if (failed.Count > 0)
    {
        foreach (var f in failed)
        {
            Console.Error.WriteLine(
                $"SOFT FAIL {f.Name}: p95={f.P95TickNs}ns alloc/tick={f.AllocBytesPerTick} " +
                $"(budgets p95={f.P95BudgetNs} alloc={f.AllocBudgetBytes})");
        }

        // Soft fail: non-zero only when SCW_BENCH_HARD_GATE=1 (after calibration).
        if (string.Equals(Environment.GetEnvironmentVariable("SCW_BENCH_HARD_GATE"), "1", StringComparison.Ordinal))
        {
            return 1;
        }
    }

    return 0;
}

var config = ManualConfig.Create(DefaultConfig.Instance)
    .AddExporter(JsonExporter.Full)
    .WithOptions(ConfigOptions.DisableOptimizationsValidator);

_ = BenchmarkRunner.Run<SimulationTickBenchmarks>(config, argsList.ToArray());
return 0;

static string? TakeOption(List<string> list, string name)
{
    var idx = list.FindIndex(a => string.Equals(a, name, StringComparison.OrdinalIgnoreCase));
    if (idx < 0 || idx + 1 >= list.Count)
    {
        return null;
    }

    var value = list[idx + 1];
    list.RemoveAt(idx + 1);
    list.RemoveAt(idx);
    return value;
}
