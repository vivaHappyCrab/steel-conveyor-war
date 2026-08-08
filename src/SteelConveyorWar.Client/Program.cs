using SteelConveyorWar.Sfml;

var smokeTest = args.Contains("--smoke-test", StringComparer.OrdinalIgnoreCase);

new SfmlGameRunner().Run(smokeTest ? 3 : null);
