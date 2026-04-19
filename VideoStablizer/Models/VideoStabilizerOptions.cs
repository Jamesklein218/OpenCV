namespace VideoStabilization.Models;

public class VideoStabilizerOptions
{
    public const string SectionName = "VideoStabilizer";

    public string InputPath { get; set; } = string.Empty;
    public string OutputPath { get; set; } = string.Empty;

    /// <summary>Frames on each side for moving-average smoothing (higher = smoother, more latency).</summary>
    public int SmoothingRadius { get; set; } = 30;

    /// <summary>Fraction of border to crop after stabilization (0.0–0.3). 0.05 = 5% each side.</summary>
    public double CropRatio { get; set; } = 0.05;

    /// <summary>Maximum feature corners to detect per frame.</summary>
    public int MaxFeatures { get; set; } = 200;

    /// <summary>Corner quality threshold for goodFeaturesToTrack.</summary>
    public double QualityLevel { get; set; } = 0.01;

    /// <summary>Minimum pixel distance between detected corners.</summary>
    public double MinDistance { get; set; } = 30.0;
}