namespace VideoStabilization.Models;

/// <summary>
/// Represents a 2D similarity transform: translation (Dx, Dy), rotation (Angle), and scale.
/// Scale is stored as LogScale = log(scale) so all four components are additive,
/// matching how Trajectory accumulates them via cumulative sum.
/// </summary>
public struct TransformParam
{
    public double Dx;
    public double Dy;
    public double Angle;
    public double LogScale;

    public double Scale => Math.Exp(LogScale);

    public TransformParam(double dx, double dy, double angle, double logScale = 0)
    {
        Dx = dx;
        Dy = dy;
        Angle = angle;
        LogScale = logScale;
    }

    public static TransformParam operator +(TransformParam a, TransformParam b)
        => new(a.Dx + b.Dx, a.Dy + b.Dy, a.Angle + b.Angle, a.LogScale + b.LogScale);

    public static TransformParam operator -(TransformParam a, TransformParam b)
        => new(a.Dx - b.Dx, a.Dy - b.Dy, a.Angle - b.Angle, a.LogScale - b.LogScale);
}