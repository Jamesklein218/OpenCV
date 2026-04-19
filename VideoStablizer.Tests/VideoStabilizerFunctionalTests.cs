using VideoStabilization;
using VideoStabilization.Models;
using Xunit;

namespace VideoStablizer.Tests;

public class VideoStabilizerFunctionalTests
{
    private static readonly string RunStamp = DateTime.Now.ToString("yyyyMMdd_HHmmss");

    [Theory]
    [MemberData(nameof(StabilizationTestData.FootageByConfig), MemberType = typeof(StabilizationTestData))]
    public void Stabilize_runs_without_throwing(string footage, TestConfiguration cfg)
    {
        Assert.True(File.Exists(footage), $"Missing footage: {footage}");

        var footageDir = Path.GetDirectoryName(footage)!;
        var output = Path.Combine(
            footageDir,
            $"{Path.GetFileNameWithoutExtension(footage)}__{cfg.Name}__{RunStamp}.mp4");

        var options = new VideoStabilizerOptions
        {
            InputPath = footage,
            OutputPath = output,
            Detect = cfg.Detect,
            Transform = cfg.Transform,
        };

        new VideoStabilizer(options).Stabilize(footage, output);
    }
}
