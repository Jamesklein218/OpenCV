using System.Diagnostics;
using System.Globalization;
using System.Text.Json;

namespace VideoStablizer.Tests;

public static class FfprobeHelper
{
    public static StreamInfo GetVideoStreamInfo(string path)
    {
        var args = string.Join(" ", new[]
        {
            "-v", "error",
            "-select_streams", "v:0",
            "-show_entries",
            "stream=width,height,r_frame_rate,avg_frame_rate,pix_fmt,color_space,color_transfer,color_primaries,color_range,codec_name,nb_frames,duration",
            "-of", "json",
            Quote(path),
        });

        var (stdout, stderr, exit) = RunProcess("ffprobe", args);
        if (exit != 0)
            throw new InvalidOperationException($"ffprobe failed (exit {exit}) for {path}: {stderr}");

        using var doc = JsonDocument.Parse(stdout);
        if (!doc.RootElement.TryGetProperty("streams", out var streams) || streams.GetArrayLength() == 0)
            throw new InvalidOperationException($"ffprobe returned no video streams for {path}");

        var s = streams[0];

        return new StreamInfo
        {
            Width = GetInt(s, "width"),
            Height = GetInt(s, "height"),
            RFrameRate = GetString(s, "r_frame_rate") ?? "",
            AvgFrameRate = GetString(s, "avg_frame_rate") ?? "",
            PixFmt = GetString(s, "pix_fmt") ?? "",
            ColorSpace = NullIfUnknown(GetString(s, "color_space")),
            ColorTransfer = NullIfUnknown(GetString(s, "color_transfer")),
            ColorPrimaries = NullIfUnknown(GetString(s, "color_primaries")),
            ColorRange = NullIfUnknown(GetString(s, "color_range")),
            CodecName = GetString(s, "codec_name") ?? "",
            NbFrames = long.TryParse(GetString(s, "nb_frames"), NumberStyles.Integer, CultureInfo.InvariantCulture, out var nf) ? nf : -1,
            DurationSeconds = double.TryParse(GetString(s, "duration"), NumberStyles.Float, CultureInfo.InvariantCulture, out var d) ? d : null,
        };
    }

    private static int GetInt(JsonElement el, string name)
    {
        if (!el.TryGetProperty(name, out var v)) return 0;
        return v.ValueKind switch
        {
            JsonValueKind.Number => v.GetInt32(),
            JsonValueKind.String => int.TryParse(v.GetString(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var i) ? i : 0,
            _ => 0,
        };
    }

    private static string? GetString(JsonElement el, string name)
    {
        if (!el.TryGetProperty(name, out var v)) return null;
        return v.ValueKind == JsonValueKind.String ? v.GetString() : null;
    }

    private static string? NullIfUnknown(string? s) =>
        string.IsNullOrEmpty(s) || s.Equals("unknown", StringComparison.OrdinalIgnoreCase) ? null : s;

    internal static (string stdout, string stderr, int exit) RunProcess(string fileName, string arguments)
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

    internal static string Quote(string s) => "\"" + s.Replace("\"", "\\\"") + "\"";
}
