using System.Diagnostics;
using System.Text.Json;

namespace VideoStabilization;

internal static class FfmpegTranscoder
{
    private sealed record ReferenceProfile(
        string PixFmt,
        string? ColorSpace,
        string? ColorTransfer,
        string? ColorPrimaries,
        string? ColorRange);

    /// <summary>
    /// Re-encodes <paramref name="intermediatePath"/> into <paramref name="outputPath"/> so that
    /// its pixel format and color metadata match <paramref name="referencePath"/>. Intended to
    /// restore stream configuration lost when OpenCvSharp's VideoWriter drops to 8-bit yuv420p
    /// without color tags.
    /// </summary>
    public static void TranscodeToMatchReference(
        string intermediatePath,
        string referencePath,
        string outputPath)
    {
        var profile = ProbeProfile(referencePath);

        if (System.IO.File.Exists(outputPath))
            System.IO.File.Delete(outputPath);

        var args = new List<string>
        {
            "-y",
            "-nostats",
            "-loglevel", "error",
            "-i", Quote(intermediatePath),
            "-i", Quote(referencePath),
            "-map", "0:v:0",
            "-map", "1:a?",
            "-c:a", "copy",
        };

        // The intermediate has no color tags, so -colorspace/-color_trc/-color_primaries
        // at the encoder level aren't enough — ffmpeg silently drops primaries/trc when
        // the input frames have "unspecified". setparams tags the frames themselves, so
        // x264 then writes them into the bitstream VUI and the muxer into the MP4 colr atom.
        var setparams = BuildSetParamsFilter(profile);
        if (setparams is not null)
        {
            args.Add("-vf");
            args.Add(Quote(setparams));
        }

        args.Add("-c:v");
        args.Add("libx264");
        args.Add("-preset");
        args.Add("medium");
        args.Add("-crf");
        args.Add("18");
        args.Add("-pix_fmt");
        args.Add(profile.PixFmt);

        if (!string.IsNullOrEmpty(profile.ColorSpace)) { args.Add("-colorspace"); args.Add(profile.ColorSpace); }
        if (!string.IsNullOrEmpty(profile.ColorTransfer)) { args.Add("-color_trc"); args.Add(profile.ColorTransfer); }
        if (!string.IsNullOrEmpty(profile.ColorPrimaries)) { args.Add("-color_primaries"); args.Add(profile.ColorPrimaries); }
        if (!string.IsNullOrEmpty(profile.ColorRange)) { args.Add("-color_range"); args.Add(profile.ColorRange); }

        args.Add("-movflags");
        args.Add("+faststart");
        args.Add(Quote(outputPath));

        var (_, stderr, exit) = RunProcess("ffmpeg", string.Join(" ", args));
        if (exit != 0)
            throw new InvalidOperationException(
                $"ffmpeg transcode failed (exit {exit}). args=[{string.Join(" ", args)}] stderr={stderr}");

        if (!System.IO.File.Exists(outputPath))
            throw new InvalidOperationException($"ffmpeg transcode produced no file at {outputPath}");
    }

    private static ReferenceProfile ProbeProfile(string path)
    {
        var args = string.Join(" ", new[]
        {
            "-v", "error",
            "-select_streams", "v:0",
            "-show_entries", "stream=pix_fmt,color_space,color_transfer,color_primaries,color_range",
            "-of", "json",
            Quote(path),
        });

        var (stdout, stderr, exit) = RunProcess("ffprobe", args);
        if (exit != 0)
            throw new InvalidOperationException($"ffprobe failed (exit {exit}) for {path}: {stderr}");

        using var doc = JsonDocument.Parse(stdout);
        var streams = doc.RootElement.GetProperty("streams");
        if (streams.GetArrayLength() == 0)
            throw new InvalidOperationException($"ffprobe found no video stream in {path}");

        var s = streams[0];
        return new ReferenceProfile(
            PixFmt: GetString(s, "pix_fmt") ?? "yuv420p",
            ColorSpace: NullIfUnknown(GetString(s, "color_space")),
            ColorTransfer: NullIfUnknown(GetString(s, "color_transfer")),
            ColorPrimaries: NullIfUnknown(GetString(s, "color_primaries")),
            ColorRange: NullIfUnknown(GetString(s, "color_range")));
    }

    private static string? BuildSetParamsFilter(ReferenceProfile p)
    {
        var parts = new List<string>();
        if (!string.IsNullOrEmpty(p.ColorSpace)) parts.Add($"colorspace={p.ColorSpace}");
        if (!string.IsNullOrEmpty(p.ColorTransfer)) parts.Add($"color_trc={p.ColorTransfer}");
        if (!string.IsNullOrEmpty(p.ColorPrimaries)) parts.Add($"color_primaries={p.ColorPrimaries}");
        if (!string.IsNullOrEmpty(p.ColorRange)) parts.Add($"range={p.ColorRange}");
        return parts.Count == 0 ? null : "setparams=" + string.Join(":", parts);
    }

    private static string? GetString(JsonElement el, string name) =>
        el.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;

    private static string? NullIfUnknown(string? s) =>
        string.IsNullOrEmpty(s) || s.Equals("unknown", StringComparison.OrdinalIgnoreCase) ? null : s;

    private static (string stdout, string stderr, int exit) RunProcess(string fileName, string arguments)
    {
        var psi = new ProcessStartInfo
        {
            FileName = fileName,
            Arguments = arguments,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };

        using var p = Process.Start(psi)
            ?? throw new InvalidOperationException($"Failed to start {fileName}");

        var stdout = p.StandardOutput.ReadToEnd();
        var stderr = p.StandardError.ReadToEnd();
        p.WaitForExit();

        return (stdout, stderr, p.ExitCode);
    }

    private static string Quote(string s) => "\"" + s.Replace("\"", "\\\"") + "\"";
}
