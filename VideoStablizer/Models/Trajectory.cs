namespace VideoStabilization.Models;

/// <summary>
/// Cumulative trajectory for smoothing.
/// </summary>
public struct Trajectory
{
    public double X;
    public double Y;
    public double Angle;

    public Trajectory(double x, double y, double angle)
    {
        X = x;
        Y = y;
        Angle = angle;
    }
}

