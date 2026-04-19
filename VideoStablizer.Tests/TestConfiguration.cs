using VideoStabilization.Models;

namespace VideoStablizer.Tests;

public sealed class TestConfiguration
{
    public string Name { get; set; } = "";
    public DetectOptions Detect { get; set; } = new();
    public TransformOptions Transform { get; set; } = new();

    public override string ToString() => Name;
}

public sealed class ConfigurationsFile
{
    public List<TestConfiguration> Configurations { get; set; } = new();
}

public sealed class FootagesFile
{
    public List<string> Footages { get; set; } = new();
}
