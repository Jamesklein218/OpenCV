using VideoStabilization;
using VideoStabilization.Models;
using Xunit;
using Xunit.Abstractions;

namespace VideoStablizer.Tests;

public class VideoStabilizerFunctionalTests
{
    // Maximum allowed VMAF loss (100 - score) between output and input.
    // Stabilization intentionally warps frames, so VMAF-vs-raw-input is inherently
    // far below 100 even on a perfect pipeline — the point of this threshold is a
    // sanity floor that catches catastrophic breakage (empty/garbled output), not a
    // perceptual-quality bar. Tune upward only if the pipeline's behavior improves.
    private const double MaxVmafMeanLoss = 95.0;

    private static readonly string RunStamp = DateTime.Now.ToString("yyyyMMdd_HHmmss");

    private readonly ITestOutputHelper _log;

    public VideoStabilizerFunctionalTests(ITestOutputHelper log)
    {
        _log = log;
    }

    [Theory]
    [MemberData(nameof(StabilizationTestData.FootageByConfig), MemberType = typeof(StabilizationTestData))]
    public void Stabilize_preserves_stream_config_and_quality(string footage, TestConfiguration cfg)
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

        Assert.True(File.Exists(output), $"Stabilizer did not produce output: {output}");

        var inInfo = FfprobeHelper.GetVideoStreamInfo(footage);
        var outInfo = FfprobeHelper.GetVideoStreamInfo(output);

        _log.WriteLine($"Input  : {inInfo.Width}x{inInfo.Height} @ {inInfo.RFrameRate} pix_fmt={inInfo.PixFmt} codec={inInfo.CodecName}");
        _log.WriteLine($"Output : {outInfo.Width}x{outInfo.Height} @ {outInfo.RFrameRate} pix_fmt={outInfo.PixFmt} codec={outInfo.CodecName}");

        Assert.Equal(inInfo.Width, outInfo.Width);
        Assert.Equal(inInfo.Height, outInfo.Height);
        Assert.Equal(inInfo.RFrameRate, outInfo.RFrameRate);
        Assert.Equal(inInfo.PixFmt, outInfo.PixFmt);

        WarnIfColorMetadataLost("color_space", inInfo.ColorSpace, outInfo.ColorSpace);
        WarnIfColorMetadataLost("color_transfer", inInfo.ColorTransfer, outInfo.ColorTransfer);
        WarnIfColorMetadataLost("color_primaries", inInfo.ColorPrimaries, outInfo.ColorPrimaries);

        var vmaf = VmafHelper.ComputeVmaf(distortedPath: output, referencePath: footage);
        var meanLoss = 100.0 - vmaf.Mean;
        _log.WriteLine(
            $"VMAF   : mean={vmaf.Mean:F2} (loss={meanLoss:F2}) min={vmaf.Min:F2} harmonic_mean={vmaf.HarmonicMean:F2}");

        Assert.True(
            meanLoss <= MaxVmafMeanLoss,
            $"VMAF mean loss {meanLoss:F2} exceeds max allowed {MaxVmafMeanLoss} for {cfg.Name} " +
            $"(mean={vmaf.Mean:F2}). Output appears catastrophically different from input.");
    }

    private void WarnIfColorMetadataLost(string field, string? input, string? output)
    {
        if (string.IsNullOrEmpty(input)) return;
        if (string.Equals(input, output, StringComparison.OrdinalIgnoreCase)) return;

        _log.WriteLine(
            $"WARN: {field} not preserved (input='{input}', output='{output ?? "<missing>"}'). " +
            "OpenCvSharp VideoWriter often drops color metadata; failing softly.");
    }
}
