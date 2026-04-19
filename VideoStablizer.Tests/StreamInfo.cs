namespace VideoStablizer.Tests;

public sealed class StreamInfo
{
    public int Width { get; init; }
    public int Height { get; init; }
    public string RFrameRate { get; init; } = "";
    public string AvgFrameRate { get; init; } = "";
    public string PixFmt { get; init; } = "";
    public string? ColorSpace { get; init; }
    public string? ColorTransfer { get; init; }
    public string? ColorPrimaries { get; init; }
    public string? ColorRange { get; init; }
    public string CodecName { get; init; } = "";
    public long NbFrames { get; init; } = -1;
    public double? DurationSeconds { get; init; }
}
