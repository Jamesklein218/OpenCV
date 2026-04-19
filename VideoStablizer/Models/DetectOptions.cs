namespace VideoStabilization.Models;

/// <summary>
/// Motion analysis options — corresponds to ffmpeg vidstabdetect.
/// </summary>
public class DetectOptions
{
    /// <summary>Maximum feature corners to detect per frame.</summary>
    public int MaxFeatures { get; set; } = 200;

    /// <summary>Corner quality threshold for goodFeaturesToTrack (vid.stab: accuracy).</summary>
    public double QualityLevel { get; set; } = 0.01;

    /// <summary>Minimum pixel distance between detected corners.</summary>
    public double MinDistance { get; set; } = 30.0;
}
