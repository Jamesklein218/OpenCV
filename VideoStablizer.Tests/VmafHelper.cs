using System.Text.Json;

namespace VideoStablizer.Tests;

public sealed record VmafResult(double Mean, double Min, double HarmonicMean);

public static class VmafHelper
{
    public static VmafResult ComputeVmaf(string distortedPath, string referencePath)
    {
        var logPath = Path.Combine(Path.GetTempPath(), $"vmaf_{Guid.NewGuid():N}.json");

        try
        {
            var filter = $"[0:v][1:v]libvmaf=log_fmt=json:log_path={EscapeForFilter(logPath)}";

            var args = string.Join(" ", new[]
            {
                "-nostats",
                "-loglevel", "error",
                "-i", FfprobeHelper.Quote(distortedPath),
                "-i", FfprobeHelper.Quote(referencePath),
                "-lavfi", FfprobeHelper.Quote(filter),
                "-f", "null",
                "-",
            });

            var (_, stderr, exit) = FfprobeHelper.RunProcess("ffmpeg", args);
            if (exit != 0)
                throw new InvalidOperationException(
                    $"ffmpeg libvmaf failed (exit {exit}) for {distortedPath} vs {referencePath}: {stderr}");

            if (!File.Exists(logPath))
                throw new InvalidOperationException(
                    $"libvmaf did not produce log at {logPath}. ffmpeg stderr: {stderr}");

            using var doc = JsonDocument.Parse(File.ReadAllText(logPath));
            var vmaf = doc.RootElement.GetProperty("pooled_metrics").GetProperty("vmaf");

            return new VmafResult(
                Mean: vmaf.GetProperty("mean").GetDouble(),
                Min: vmaf.GetProperty("min").GetDouble(),
                HarmonicMean: vmaf.GetProperty("harmonic_mean").GetDouble());
        }
        finally
        {
            if (File.Exists(logPath))
            {
                try { File.Delete(logPath); } catch { /* best effort */ }
            }
        }
    }

    private static string EscapeForFilter(string path) =>
        path.Replace("\\", "\\\\").Replace(":", "\\:");
}
