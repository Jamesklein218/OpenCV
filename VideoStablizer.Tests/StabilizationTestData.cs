using Microsoft.Extensions.Configuration;

namespace VideoStablizer.Tests;

public static class StabilizationTestData
{
    private static readonly Lazy<List<string>> LazyFootages =
        new(() => LoadJson<FootagesFile>("footages.json").Footages);

    private static readonly Lazy<List<TestConfiguration>> LazyConfigurations =
        new(() => LoadJson<ConfigurationsFile>("configurations.json").Configurations);

    public static IEnumerable<object[]> FootageByConfig()
    {
        foreach (var footage in LazyFootages.Value)
            foreach (var cfg in LazyConfigurations.Value)
                yield return new object[] { footage, cfg };
    }

    private static T LoadJson<T>(string fileName) where T : new()
    {
        var config = new ConfigurationBuilder()
            .SetBasePath(AppContext.BaseDirectory)
            .AddJsonFile(fileName, optional: false)
            .Build();

        return config.Get<T>() ?? new T();
    }
}
