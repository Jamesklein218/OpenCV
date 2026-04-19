namespace VideoStabilization.Models;

/// <summary>
/// Cumulative camera trajectory. Scale is stored as log-scale (sum of LogScale deltas)
/// so it remains additive and is smoothed the same way as X/Y/Angle.
/// </summary>
public struct Trajectory
{
    public double X;
    public double Y;
    public double Angle;
    public double Scale;

    public Trajectory(double x, double y, double angle, double scale = 0)
    {
        X = x;
        Y = y;
        Angle = angle;
        Scale = scale;
    }
}
