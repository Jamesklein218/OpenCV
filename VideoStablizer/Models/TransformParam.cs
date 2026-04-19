namespace VideoStabilization.Models;

/// <summary>
/// Represents a 2D rigid-body transform (dx, dy, rotation).
/// </summary>
public struct TransformParam
{
    public double Dx;
    public double Dy;
    public double Angle;

    public TransformParam(double dx, double dy, double angle)
    {
        Dx = dx;
        Dy = dy;
        Angle = angle;
    }

    public static TransformParam operator +(TransformParam a, TransformParam b)
        => new(a.Dx + b.Dx, a.Dy + b.Dy, a.Angle + b.Angle);

    public static TransformParam operator -(TransformParam a, TransformParam b)
        => new(a.Dx - b.Dx, a.Dy - b.Dy, a.Angle - b.Angle);
}

