namespace VideoStabilization.Models;

public class VideoStabilizerOptions
{
    public const string SectionName = "VideoStabilizer";

    public string InputPath { get; set; } = string.Empty;
    public string OutputPath { get; set; } = string.Empty;

    public DetectOptions Detect { get; set; } = new();
    public TransformOptions Transform { get; set; } = new();
}
