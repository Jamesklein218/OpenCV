namespace VideoStabilization.Models;

/// <summary>
/// Stabilization and output options — corresponds to ffmpeg vidstabtransform.
/// </summary>
public class TransformOptions
{
    /// <summary>Frames on each side for Gaussian smoothing (vid.stab: smoothing).</summary>
    public int SmoothingRadius { get; set; } = 30;

    /// <summary>Fraction of border to crop when OptZoom is disabled (vid.stab: zoom). Clamped 0–0.3.</summary>
    public double CropRatio { get; set; } = 0.05;

    /// <summary>Maximum stabilization shift in pixels per frame. -1 = no limit (vid.stab: maxshift).</summary>
    public double MaxShift { get; set; } = -1;

    /// <summary>Maximum stabilization rotation in radians per frame. -1 = no limit (vid.stab: maxangle).</summary>
    public double MaxAngle { get; set; } = -1;

    /// <summary>
    /// Optimal zoom mode to avoid black borders: 0 = disabled (use CropRatio),
    /// 1 = static (one zoom factor for the whole video), 2 = dynamic (per-frame zoom).
    /// Matches vid.stab optzoom.
    /// </summary>
    public int OptZoom { get; set; } = 1;
}
