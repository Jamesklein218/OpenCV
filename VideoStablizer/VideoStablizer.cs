using VideoStabilization.Models;

namespace VideoStabilization;

using System;
using System.Collections.Generic;
using System.Linq;
using OpenCvSharp;

public class VideoStabilizer
{
    private readonly int _smoothingRadius;
    private readonly double _cropRatio;
    private readonly int _maxFeatures;
    private readonly double _qualityLevel;
    private readonly double _minDistance;

    public VideoStabilizer(VideoStabilizerOptions options)
    {
        _smoothingRadius = options.SmoothingRadius;
        _cropRatio = Math.Clamp(options.CropRatio, 0.0, 0.3);
        _maxFeatures = options.MaxFeatures;
        _qualityLevel = options.QualityLevel;
        _minDistance = options.MinDistance;
    }

    /// <summary>
    /// Stabilizes a video file and writes the result to <paramref name="outputPath"/>.
    /// Two-pass approach: estimate all transforms, smooth globally, then warp.
    /// </summary>
    public void Stabilize(string inputPath, string outputPath)
    {
        if (!System.IO.File.Exists(inputPath))
            throw new System.IO.FileNotFoundException("Input video not found.", inputPath);

        // ── Pass 1: Estimate frame-to-frame transforms ──────────────────
        Console.WriteLine("[Pass 1] Estimating motion...");
        var transforms = EstimateMotion(inputPath, out int frameCount, out Size frameSize, out double fps);

        if (transforms.Count == 0)
            throw new InvalidOperationException("No transforms could be estimated. Video may be too short or featureless.");

        Console.WriteLine($"  Estimated {transforms.Count} transforms from {frameCount} frames.");

        // ── Compute cumulative trajectory and smooth it ──────────────────
        var trajectory = ComputeTrajectory(transforms);
        var smoothedTrajectory = SmoothTrajectory(trajectory, _smoothingRadius);
        var smoothedTransforms = RecomputeTransforms(transforms, trajectory, smoothedTrajectory);

        // ── Pass 2: Apply smoothed transforms and write output ───────────
        Console.WriteLine("[Pass 2] Applying stabilization...");
        ApplyStabilization(inputPath, outputPath, smoothedTransforms, frameSize, fps);

        Console.WriteLine($"[Done] Stabilized video saved to: {outputPath}");
    }

    // ─────────────────────────────────────────────────────────────────────
    //  Motion Estimation (feature tracking with RANSAC)
    // ─────────────────────────────────────────────────────────────────────

    private List<TransformParam> EstimateMotion(
        string inputPath, out int frameCount, out Size frameSize, out double fps)
    {
        var transforms = new List<TransformParam>();

        using var capture = new VideoCapture(inputPath);
        fps = capture.Fps;
        frameSize = new Size((int)capture.Get(VideoCaptureProperties.FrameWidth),
                             (int)capture.Get(VideoCaptureProperties.FrameHeight));
        frameCount = (int)capture.Get(VideoCaptureProperties.FrameCount);

        using var prevFrame = new Mat();
        capture.Read(prevFrame);

        if (prevFrame.Empty())
            throw new InvalidOperationException("Cannot read the first frame.");

        using var prevGray = new Mat();
        Cv2.CvtColor(prevFrame, prevGray, ColorConversionCodes.BGR2GRAY);

        int processedFrames = 0;

        while (true)
        {
            using var currFrame = new Mat();
            if (!capture.Read(currFrame) || currFrame.Empty())
                break;

            using var currGray = new Mat();
            Cv2.CvtColor(currFrame, currGray, ColorConversionCodes.BGR2GRAY);

            // Detect good features in the previous frame
            var prevCorners = Cv2.GoodFeaturesToTrack(
                prevGray, _maxFeatures, _qualityLevel, _minDistance,
                null, 3, false, 0.04);

            if (prevCorners.Length < 4)
            {
                // Not enough features — assume identity (no correction)
                transforms.Add(new TransformParam(0, 0, 0));
                currGray.CopyTo(prevGray);
                processedFrames++;
                continue;
            }

            // Convert to Point2f array for calcOpticalFlowPyrLK
            var prevPts = prevCorners.Select(p => new Point2f((float)p.X, (float)p.Y)).ToArray();

            // Track features into the current frame
            var currPts = new Point2f[prevPts.Length];
            Cv2.CalcOpticalFlowPyrLK(
                prevGray, currGray, prevPts, ref currPts,
                out byte[] status, out float[] err,
                new Size(21, 21), 3, null,
                OpticalFlowFlags.None, 1e-4);

            // Keep only successfully tracked points
            var goodPrev = new List<Point2f>();
            var goodCurr = new List<Point2f>();
            for (int i = 0; i < status.Length; i++)
            {
                if (status[i] == 1)
                {
                    goodPrev.Add(prevPts[i]);
                    goodCurr.Add(currPts[i]);
                }
            }

            if (goodPrev.Count < 4)
            {
                transforms.Add(new TransformParam(0, 0, 0));
                currGray.CopyTo(prevGray);
                processedFrames++;
                continue;
            }

            // Estimate rigid transform (translation + rotation) with RANSAC
            using var inliersMat = new Mat();
            using var T = Cv2.EstimateAffinePartial2D(
                InputArray.Create(goodPrev.ToArray()),
                InputArray.Create(goodCurr.ToArray()),
                OutputArray.Create(inliersMat));

            if (T.Empty())
            {
                transforms.Add(new TransformParam(0, 0, 0));
            }
            else
            {
                // T is a 2×3 matrix: [[cos θ, -sin θ, tx], [sin θ, cos θ, ty]]
                double dx = T.At<double>(0, 2);
                double dy = T.At<double>(1, 2);
                double angle = Math.Atan2(T.At<double>(1, 0), T.At<double>(0, 0));

                // Sanity check: reject implausibly large transforms
                if (Math.Abs(dx) > frameSize.Width * 0.25 ||
                    Math.Abs(dy) > frameSize.Height * 0.25 ||
                    Math.Abs(angle) > Math.PI / 6)
                {
                    transforms.Add(new TransformParam(0, 0, 0));
                }
                else
                {
                    transforms.Add(new TransformParam(dx, dy, angle));
                }
            }

            currGray.CopyTo(prevGray);
            processedFrames++;

            if (processedFrames % 100 == 0)
                Console.WriteLine($"  Processed {processedFrames}/{frameCount - 1} frame pairs...");
        }

        return transforms;
    }

    // ─────────────────────────────────────────────────────────────────────
    //  Trajectory Computation & Smoothing
    // ─────────────────────────────────────────────────────────────────────
    private static List<Trajectory> ComputeTrajectory(List<TransformParam> transforms)
    {
        var trajectory = new List<Trajectory>(transforms.Count);
        double x = 0, y = 0, angle = 0;

        foreach (var t in transforms)
        {
            x += t.Dx;
            y += t.Dy;
            angle += t.Angle;
            trajectory.Add(new Trajectory(x, y, angle));
        }

        return trajectory;
    }

    /// <summary>
    /// Moving-average smoothing of the cumulative trajectory.
    /// </summary>
    private static List<Trajectory> SmoothTrajectory(List<Trajectory> trajectory, int radius)
    {
        var smoothed = new List<Trajectory>(trajectory.Count);

        for (int i = 0; i < trajectory.Count; i++)
        {
            double sumX = 0, sumY = 0, sumAngle = 0;
            int count = 0;

            for (int j = -radius; j <= radius; j++)
            {
                int idx = Math.Clamp(i + j, 0, trajectory.Count - 1);
                sumX += trajectory[idx].X;
                sumY += trajectory[idx].Y;
                sumAngle += trajectory[idx].Angle;
                count++;
            }

            smoothed.Add(new Trajectory(sumX / count, sumY / count, sumAngle / count));
        }

        return smoothed;
    }

    /// <summary>
    /// Recompute per-frame transforms so the camera follows the smoothed trajectory.
    /// </summary>
    private static List<TransformParam> RecomputeTransforms(
        List<TransformParam> original,
        List<Trajectory> trajectory,
        List<Trajectory> smoothedTrajectory)
    {
        var result = new List<TransformParam>(original.Count);

        for (int i = 0; i < original.Count; i++)
        {
            double diffX = smoothedTrajectory[i].X - trajectory[i].X;
            double diffY = smoothedTrajectory[i].Y - trajectory[i].Y;
            double diffAngle = smoothedTrajectory[i].Angle - trajectory[i].Angle;

            result.Add(new TransformParam(
                original[i].Dx + diffX,
                original[i].Dy + diffY,
                original[i].Angle + diffAngle));
        }

        return result;
    }

    // ─────────────────────────────────────────────────────────────────────
    //  Pass 2: Apply transforms and write stabilized video
    // ─────────────────────────────────────────────────────────────────────

    private void ApplyStabilization(
        string inputPath, string outputPath,
        List<TransformParam> smoothedTransforms,
        Size frameSize, double fps)
    {
        if (!File.Exists(outputPath))
        {
            File.Create(outputPath).Close();
        }
        
        if (System.IO.File.Exists(outputPath))
            System.IO.File.Delete(outputPath);

        using var capture = new VideoCapture(inputPath);
        using var writer = new VideoWriter(
            outputPath,
            VideoWriter.FourCC('a', 'v', 'c', '1'),
            fps,
            frameSize);

        if (!writer.IsOpened())
            throw new InvalidOperationException($"Cannot open video writer for: {outputPath}");

        // Write the first frame unchanged
        using var firstFrame = new Mat();
        capture.Read(firstFrame);
        writer.Write(firstFrame);

        // Crop rectangle (removes black borders from stabilization shifts)
        int cropX = (int)(frameSize.Width * _cropRatio);
        int cropY = (int)(frameSize.Height * _cropRatio);
        var cropRect = new Rect(cropX, cropY,
                                frameSize.Width - 2 * cropX,
                                frameSize.Height - 2 * cropY);

        int frameIdx = 0;

        while (frameIdx < smoothedTransforms.Count)
        {
            using var frame = new Mat();
            if (!capture.Read(frame) || frame.Empty())
                break;

            var t = smoothedTransforms[frameIdx];

            // Build the 2×3 affine matrix from (dx, dy, angle)
            double cosA = Math.Cos(t.Angle);
            double sinA = Math.Sin(t.Angle);

            using var transformMatrix = new Mat(2, 3, MatType.CV_64FC1);
            transformMatrix.Set(0, 0, cosA);
            transformMatrix.Set(0, 1, -sinA);
            transformMatrix.Set(0, 2, t.Dx);
            transformMatrix.Set(1, 0, sinA);
            transformMatrix.Set(1, 1, cosA);
            transformMatrix.Set(1, 2, t.Dy);

            // Warp with border reflection to reduce black edges
            using var stabilizedFull = new Mat();
            Cv2.WarpAffine(frame, stabilizedFull, transformMatrix, frameSize,
                InterpolationFlags.Linear, BorderTypes.Reflect);

            // Crop to hide residual border artifacts, then resize back to original dimensions
            using var cropped = new Mat(stabilizedFull, cropRect);
            using var output = new Mat();
            Cv2.Resize(cropped, output, frameSize, 0, 0, InterpolationFlags.Linear);

            writer.Write(output);
            frameIdx++;

            if (frameIdx % 100 == 0)
                Console.WriteLine($"  Written {frameIdx}/{smoothedTransforms.Count} stabilized frames...");
        }
    }
}

